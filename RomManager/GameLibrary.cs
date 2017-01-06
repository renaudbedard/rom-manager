using AngleSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace RomManager
{
    public class GameLibrary : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<Game> Games { get; private set; } = new ObservableCollection<Game>();

        Game currentGame;
        public Game CurrentGame
        {
            get { return currentGame; }
            set
            {
                currentGame = value;
                PropertyChanged(this, new PropertyChangedEventArgs("CurrentGame"));
            }
        }

        string folderPath;
        public string FolderPath
        {
            get { return folderPath; }
            set
            {
                folderPath = value;
                PropertyChanged(this, new PropertyChangedEventArgs("FolderPath"));
                PropertyChanged(this, new PropertyChangedEventArgs("HasFolderPath"));
            }
        }

        public string AnalyzeStopCaption
        {
            get { return analyzing ? "Stop" : "Analyze"; }
        }

        public bool HasFolderPath
        {
            get { return FolderPath != null; }
        }

        int thumbnailSize = 128;
        public int ThumbnailSize
        {
            get { return thumbnailSize; }
            set
            {
                thumbnailSize = value;
                PropertyChanged(this, new PropertyChangedEventArgs("ThumbnailSize"));
                PropertyChanged(this, new PropertyChangedEventArgs("ThumbnailMarginSize"));
            }
        }

        public int ThumbnailMarginSize
        {
            get { return thumbnailSize / 42; } 
        }

        struct HashedFile
        {
            public string FilePath;
            public string SHA1Hash;
        }
        struct MatchedFile
        {
            public string FilePath;
            public long? RomId;
        }

        bool analyzing;
        CancellationTokenSource cancellationTokenSource;
        List<IDataflowBlock> activeBlocks = new List<IDataflowBlock>();
        SQLiteConnection ovgdbConnection, mobyscoreConnection;
        Dispatcher uiDispatcher = Application.Current.Dispatcher;
        public void ToggleAnalyze()
        {
            Action teardown = () =>
            {
                ovgdbConnection.Dispose();
                mobyscoreConnection.Dispose();

                ovgdbConnection = mobyscoreConnection = null;

                cancellationTokenSource.Dispose();
                cancellationTokenSource = null;

                activeBlocks.Clear();

                analyzing = false;
                uiDispatcher.Invoke(() => PropertyChanged(this, new PropertyChangedEventArgs("AnalyzeStopCaption")));
            };

            if (analyzing)
            {
                cancellationTokenSource.Cancel();

                // wait for active blocks to finish processing before cleaning up
                Task.Run(async () =>
                {
                    try
                    {
                        await Task.WhenAll(activeBlocks.Select(x => x.Completion).ToArray());
                    }
                    catch (TaskCanceledException)
                    {
                        // apparently that's standard practice...?
                        // https://msdn.microsoft.com/en-us/library/hh228611(v=vs.110).aspx
                    }

                    teardown();
                });
                return;
            }

            cancellationTokenSource = new CancellationTokenSource();
            analyzing = true;
            PropertyChanged(this, new PropertyChangedEventArgs("AnalyzeStopCaption"));

            Games.Clear();

            // ensure we have a database for scores
            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RomManager");
            if (!Directory.Exists(appData))
                Directory.CreateDirectory(appData);

            var pathToMobyscoreDb = Path.Combine(appData, "mobyscores.sqlite");
            if (!File.Exists(pathToMobyscoreDb))
            {
                SQLiteConnection.CreateFile(pathToMobyscoreDb);
                using (mobyscoreConnection = new SQLiteConnection($"Data Source={pathToMobyscoreDb}; Version=3;"))
                using (var command = mobyscoreConnection.CreateCommand())
                {
                    mobyscoreConnection.Open();
                    command.CommandText = "CREATE TABLE \"SCORES\" ( `releaseID` INTEGER, `mobyScore` REAL, PRIMARY KEY(`releaseID`) )";
                    command.ExecuteNonQuery();
                }
            }

            var datablockOptions = new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cancellationTokenSource.Token
            };

            // file hashing block
            var hashingBlock = new TransformBlock<string, HashedFile>(filePath =>
                {
                    byte[] hash;
                    using (var sha1 = SHA1.Create())
                        hash = sha1.ComputeHash(File.OpenRead(filePath));

                    var sb = new StringBuilder(hash.Length * 2);
                    foreach (byte b in hash)
                        sb.Append(b.ToString("X2"));

                    return new HashedFile { FilePath = filePath, SHA1Hash = sb.ToString() };
                }, 
                datablockOptions);

            // rom matching block
            var matchingBlock = new TransformBlock<HashedFile, MatchedFile>(hashedFile =>
                {
                    using (var command = ovgdbConnection.CreateCommand())
                    {
                        command.CommandText = $"SELECT romID FROM ROMs WHERE romHashSHA1 = '{hashedFile.SHA1Hash}' LIMIT 1";
                        var romId = command.ExecuteScalar();

                        if (romId != null)
                            return new MatchedFile { FilePath = hashedFile.FilePath, RomId = (long) romId };

                        var extensionlessFileName = Path.GetFileNameWithoutExtension(hashedFile.FilePath);
                        command.CommandText = $"SELECT romID FROM ROMs WHERE romExtensionlessFileName = '{extensionlessFileName}' LIMIT 1";
                        romId = command.ExecuteScalar();

                        if (romId != null)
                            return new MatchedFile { FilePath = hashedFile.FilePath, RomId = (long) romId };

                        Console.WriteLine($"Could not find match for '{extensionlessFileName}'");
                        return new MatchedFile { FilePath = hashedFile.FilePath, RomId = null };
                    }
                },
                datablockOptions);

            // get metadata from OGVDB
            var localMetadataAndUiBlock = new TransformBlock<MatchedFile, Game>(matchedFile =>
                {
                    using (var ovgdbCommand = ovgdbConnection.CreateCommand())
                    {
                        var game = new Game { FilePath = matchedFile.FilePath };

                        // fetch metadata from ogvdb
                        if (matchedFile.RomId.HasValue)
                        {
                            ovgdbCommand.CommandText = $"SELECT releaseId, releaseTitleName, TEMPsystemName, releaseCoverFront, releaseCoverBack, releaseDescription, releaseDeveloper, releaseGenre, releaseDate FROM RELEASES WHERE romID = '{matchedFile.RomId}' LIMIT 1";
                            using (var reader = ovgdbCommand.ExecuteReader())
                            {
                                if (reader.HasRows)
                                {
                                    reader.Read();

                                    game.ReleaseId = reader.GetInt64(0);
                                    game.Title = GetNullable<string>(reader.GetValue(1));
                                    game.SystemName = GetNullable<string>(reader.GetValue(2));
                                    game.Description = GetNullable<string>(reader.GetValue(5));
                                    game.Developer = GetNullable<string>(reader.GetValue(6));
                                    game.Genres = GetNullable<string>(reader.GetValue(7))?.Replace(",", ", ");
                                    game.ReleaseDate = GetNullable<string>(reader.GetValue(8));

                                    var frontCoverUris = TryGetCachedImage(GetNullable<string>(reader.GetValue(3)), "FrontCover", game.ReleaseId.Value);
                                    game.FrontCoverUri = frontCoverUris.Item1;
                                    game.FrontCoverCachePath = frontCoverUris.Item2;

                                    var backCoverUris = TryGetCachedImage(GetNullable<string>(reader.GetValue(4)), "BackCover", game.ReleaseId.Value);
                                    game.BackCoverUri = backCoverUris.Item1;
                                    game.BackCoverCachePath = backCoverUris.Item2;
                                }
                            }
                        }

                        // fallback for non-matched roms
                        if (!game.ReleaseId.HasValue)
                        {
                            game.Title = game.FileName;
                            game.Description = "No information found.";
                            game.FrontCoverUri = TryGetCachedImage(null, "FrontCover", 0).Item1;
                            game.BackCoverUri = TryGetCachedImage(null, "BackCover", 0).Item1;
                        }

                        // expand images on UI thread & add to list
                        uiDispatcher.InvokeAsync(() =>
                        {
                            game.FrontCover = ExpandImage(game.FrontCoverUri, game.FrontCoverCachePath);
                            game.BackCover = ExpandImage(game.BackCoverUri, game.BackCoverCachePath);

                            Games.Add(game);
                            if (Games.Count == 1)
                                CurrentGame = Games[0];
                        });

                        return game;
                    };
                },
                datablockOptions);

            // get score info from MobyGames
            var scoreBlock = new ActionBlock<Game>(async game =>
                {
                    if (!game.ReleaseId.HasValue)
                        return;

                    using (var mobyscoreCommand = mobyscoreConnection.CreateCommand())
                    {
                        mobyscoreCommand.CommandText = $"SELECT mobyScore FROM SCORES WHERE releaseId = {game.ReleaseId.Value}";
                        var mobyscore = mobyscoreCommand.ExecuteScalar();
                        if (mobyscore != null)
                            game.MobyScore = (float)(double)mobyscore;
                        else
                        {
                            await GetScoreAsync(game);
                            mobyscoreCommand.CommandText = $"INSERT INTO SCORES (releaseId, mobyScore) VALUES ({game.ReleaseId.Value}, {game.MobyScore})";
                            mobyscoreCommand.ExecuteNonQuery();
                        }
                    }
                },
                datablockOptions);

            // link data blocks
            var linkOptions = new DataflowLinkOptions { PropagateCompletion = true };
            hashingBlock.LinkTo(matchingBlock, linkOptions);
            matchingBlock.LinkTo(localMetadataAndUiBlock, linkOptions);
            localMetadataAndUiBlock.LinkTo(scoreBlock, linkOptions);

            // register them as active (so we can wait on them as they're being canceled)
            activeBlocks.Add(matchingBlock);
            activeBlocks.Add(localMetadataAndUiBlock);
            activeBlocks.Add(scoreBlock);

            // do the actual data submission and waiting on a thread so we don't block the UI thread
            Task.Run(async () =>
                {
                    // open database connections
                    var pathToOvgdb = $"{AppDomain.CurrentDomain.BaseDirectory}openvgdb.sqlite";
                    ovgdbConnection = new SQLiteConnection($"Data Source={pathToOvgdb}; Version=3;");
                    ovgdbConnection.Open();
                    mobyscoreConnection = new SQLiteConnection($"Data Source={pathToMobyscoreDb}; Version=3;");
                    mobyscoreConnection.Open();

                    // submit data and wait for completion
                    var di = new DirectoryInfo(folderPath);
                    foreach (var file in di.GetFiles())
                        hashingBlock.Post(file.FullName);

                    // wait for dataflow completion
                    hashingBlock.Complete();
                    await scoreBlock.Completion;

                    teardown();
                });
        }

        static T GetNullable<T>(object value) where T : class
        {
            return value is DBNull ? null : (T)value;
        }

        async Task GetScoreAsync(Game game)
        {
            var config = Configuration.Default.WithDefaultLoader();
            var uriEscapedTitle = Uri.EscapeDataString(game.Title);
            var platformId = game.MobyGamesPlatformId;
            var searchPageUri = $"https://www.mobygames.com/search/quick?q={uriEscapedTitle}&p={platformId}&search=Go&sFilter=1&sG=on";
            var document = await BrowsingContext.New(config).OpenAsync(searchPageUri);
            var linkElement = document.QuerySelector("div.searchTitle a");

            if (linkElement != null)
            {
                var gamePageUri = linkElement.GetAttribute("href");
                var rankPageUri = $"{gamePageUri}/view-moby-score";
                document = await BrowsingContext.New(config).OpenAsync(rankPageUri);
                var scoreElement = document.QuerySelector("table.scoreWindow tr:last-child > td:nth-child(2)");
                if (scoreElement != null)
                {
                    game.MobyScore = float.Parse(scoreElement.TextContent);
                    Console.WriteLine($"'{game.Title}' : {game.MobyScore} / 5");
                }
                else
                    Console.WriteLine($"No score found for '{game.Title}'");
            }
            else
                Console.WriteLine($"No search result found for '{game.Title}'");
        }

        static Tuple<Uri, string> TryGetCachedImage(string url, string type, long releaseId)
        {
            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RomManager");
            if (!Directory.Exists(appData))
                Directory.CreateDirectory(appData);

            var cachedImageFolder = Path.Combine(appData, "CachedImages");
            if (!Directory.Exists(cachedImageFolder))
                Directory.CreateDirectory(cachedImageFolder);

            var byType = Path.Combine(cachedImageFolder, type);
            if (!Directory.Exists(byType))
                Directory.CreateDirectory(byType);

            string imageFilePath;
            if (url == null)
            {
                // TODO: per-system fallback images
                imageFilePath = Path.Combine($"{AppDomain.CurrentDomain.BaseDirectory}Fallback Covers", $"fallback_{type}_md.jpg");
            }
            else
                imageFilePath = Path.Combine(byType, releaseId.ToString()) + url.Substring(url.LastIndexOf('.'));

            if (!File.Exists(imageFilePath))
                return new Tuple<Uri, string>(new Uri(url), imageFilePath);
            else
                return new Tuple<Uri, string>(new Uri(imageFilePath), null);
        }

        static BitmapImage ExpandImage(Uri uri, string cachePath)
        {
            if (uri.IsFile)
                return new BitmapImage(uri);
            else
            {
                var image = new BitmapImage();
                image.BeginInit();
                {
                    image.UriSource = uri;
                    image.DecodePixelHeight = 512;

                    image.DownloadCompleted += (_, __) =>
                    {
                        JpegBitmapEncoder encoder = new JpegBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(image));

                        using (var filestream = new FileStream(cachePath, FileMode.Create))
                            encoder.Save(filestream);
                    };
                }
                image.EndInit();

                return image;
            }
        }
    }
}
