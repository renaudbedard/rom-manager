using AngleSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

        bool analyzing;
        bool cancelRequested;
        public void ToggleAnalyze()
        {
            if (analyzing)
            {
                cancelRequested = true;
                return;
            }

            Games.Clear();

            cancelRequested = false;
            analyzing = true;

            PropertyChanged(this, new PropertyChangedEventArgs("AnalyzeStopCaption"));

            var hashedFiles = new ConcurrentQueue<Tuple<string, string>>();
            var matchedRoms = new ConcurrentQueue<Tuple<string, long?>>();

            // hash files
            var di = new DirectoryInfo(folderPath);
            var fileHashed = new ManualResetEventSlim();
            bool allFilesHashed = false;
            Task.Run(() =>
            {
                Parallel.ForEach(di.GetFiles(), (file, parallelLoopState) =>
                {
                    var filePath = file.FullName;

                    byte[] hash;
                    using (var sha1 = SHA1.Create())
                        hash = sha1.ComputeHash(File.OpenRead(filePath));

                    var sb = new StringBuilder(hash.Length * 2);
                    foreach (byte b in hash)
                        sb.Append(b.ToString("X2"));

                    hashedFiles.Enqueue(new Tuple<string, string>(filePath, sb.ToString()));
                    fileHashed.Set();

                    if (cancelRequested)
                        parallelLoopState.Stop();
                });

                allFilesHashed = true;
                fileHashed.Set();
            });

            var pathToOvgdb = $"{AppDomain.CurrentDomain.BaseDirectory}openvgdb.sqlite";

            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RomManager");
            if (!Directory.Exists(appData))
                Directory.CreateDirectory(appData);
            var pathToMobyscoreDb = Path.Combine(appData, "mobyscores.sqlite");

            // create mobyscores db if not present
            if (!File.Exists(pathToMobyscoreDb))
            {
                SQLiteConnection.CreateFile(pathToMobyscoreDb);
                using (var connection = new SQLiteConnection($"Data Source={pathToMobyscoreDb}; Version=3;"))
                using (var command = connection.CreateCommand())
                {
                    connection.Open();
                    command.CommandText = "CREATE TABLE \"SCORES\" ( `releaseID` INTEGER, `mobyScore` REAL, PRIMARY KEY(`releaseID`) )";
                    command.ExecuteNonQuery();
                    connection.Close();
                }
            }

            // match roms in database
            bool allRomsMatched = false;
            var romMatched = new ManualResetEventSlim();
            Task.Run(() =>
            {
                using (var connection = new SQLiteConnection($"Data Source={pathToOvgdb}; Version=3;"))
                {
                    connection.Open();
                    using (var command = connection.CreateCommand())
                    {
                        while (!allFilesHashed)
                        {
                            fileHashed.Wait();

                            if (cancelRequested)
                                break;

                            Tuple<string, string> pair;
                            while (hashedFiles.TryDequeue(out pair))
                            {
                                command.CommandText = $"SELECT romID FROM ROMs WHERE romHashSHA1 = '{pair.Item2}' LIMIT 1";
                                var romId = command.ExecuteScalar();

                                if (romId != null)
                                    matchedRoms.Enqueue(new Tuple<string, long?>(pair.Item1, (long)romId));
                                else
                                {
                                    var extensionlessFileName = Path.GetFileNameWithoutExtension(pair.Item1);
                                    command.CommandText = $"SELECT romID FROM ROMs WHERE romExtensionlessFileName = '{extensionlessFileName}' LIMIT 1";
                                    romId = command.ExecuteScalar();

                                    if (romId != null)
                                        matchedRoms.Enqueue(new Tuple<string, long?>(pair.Item1, (long)romId));
                                    else
                                    {
                                        matchedRoms.Enqueue(new Tuple<string, long?>(pair.Item1, null));
                                        Console.WriteLine($"Could not find match for '{extensionlessFileName}'");
                                    }
                                }
                                romMatched.Set();
                            }
                        }
                    }
                    connection.Close();
                }

                allRomsMatched = true;
                romMatched.Set();
            });

            var dispatcher = Dispatcher.CurrentDispatcher;

            // get metadata
            Task.Run(async () =>
            {
                using (var ovgdbConnection = new SQLiteConnection($"Data Source={pathToOvgdb}; Version=3;"))
                using (var mobyscoreConnection = new SQLiteConnection($"Data Source={pathToMobyscoreDb}; Version=3;"))
                {
                    ovgdbConnection.Open();
                    mobyscoreConnection.Open();

                    using (var ovgdbCommand = ovgdbConnection.CreateCommand())
                    using (var mobyscoreCommand = mobyscoreConnection.CreateCommand())
                    {
                        while (!allRomsMatched)
                        {
                            romMatched.Wait();

                            if (cancelRequested)
                                break;

                            Tuple<string, long?> pair;
                            while (matchedRoms.TryDequeue(out pair))
                            {
                                var game = new Game { FilePath = pair.Item1 };
                                long? releaseId = null;

                                // fetch metadata from ogvdb
                                if (pair.Item2.HasValue)
                                {
                                    ovgdbCommand.CommandText = $"SELECT releaseId, releaseTitleName, TEMPsystemName, releaseCoverFront, releaseCoverBack, releaseDescription, releaseDeveloper, releaseGenre, releaseDate FROM RELEASES WHERE romID = '{pair.Item2.Value}' LIMIT 1";
                                    using (var reader = ovgdbCommand.ExecuteReader())
                                    {
                                        if (reader.HasRows)
                                        {
                                            reader.Read();

                                            releaseId = reader.GetInt64(0);
                                            game.Title = GetNullable<string>(reader.GetValue(1));
                                            game.SystemName = GetNullable<string>(reader.GetValue(2));
                                            game.Description = GetNullable<string>(reader.GetValue(5));
                                            game.Developer = GetNullable<string>(reader.GetValue(6));
                                            game.Genres = GetNullable<string>(reader.GetValue(7))?.Replace(",", ", ");
                                            game.ReleaseDate = GetNullable<string>(reader.GetValue(8));

                                            var frontCoverUris = TryGetCachedImage(GetNullable<string>(reader.GetValue(3)), "FrontCover", releaseId.Value);
                                            game.FrontCoverUri = frontCoverUris.Item1;
                                            game.FrontCoverCachePath = frontCoverUris.Item2;

                                            var backCoverUris = TryGetCachedImage(GetNullable<string>(reader.GetValue(4)), "BackCover", releaseId.Value);
                                            game.BackCoverUri = backCoverUris.Item1;
                                            game.BackCoverCachePath = backCoverUris.Item2;
                                        }
                                    }
                                }

                                // fallback for non-matched roms
                                if (!releaseId.HasValue)
                                {
                                    game.Title = game.FileName;
                                    game.Description = "No information found.";
                                    game.FrontCoverUri = TryGetCachedImage(null, "FrontCover", 0).Item1;
                                    game.BackCoverUri = TryGetCachedImage(null, "BackCover", 0).Item1;
                                }

                                // score info from MobyGames
                                if (releaseId.HasValue)
                                {
                                    mobyscoreCommand.CommandText = $"SELECT mobyScore FROM SCORES WHERE releaseId = {releaseId.Value}";
                                    var mobyscore = mobyscoreCommand.ExecuteScalar();
                                    if (mobyscore != null)
                                        game.MobyScore = (float) (double) mobyscore;
                                    else
                                    {
                                        await GetScoreAsync(game);
                                        mobyscoreCommand.CommandText = $"INSERT INTO SCORES (releaseId, mobyScore) VALUES ({releaseId.Value}, {game.MobyScore})";
                                        mobyscoreCommand.ExecuteNonQuery();
                                    }
                                }

                                // expand images on UI thread & add to list
                                dispatcher.InvokeAsync(() =>
                                {
                                    game.FrontCover = ExpandImage(game.FrontCoverUri, game.FrontCoverCachePath);
                                    game.BackCover = ExpandImage(game.BackCoverUri, game.BackCoverCachePath);

                                    Games.Add(game);
                                });

                                if (cancelRequested)
                                    break;
                            }
                        }
                    }
                    ovgdbConnection.Close();
                    mobyscoreConnection.Close();

                    analyzing = false;
                    PropertyChanged(this, new PropertyChangedEventArgs("AnalyzeStopCaption"));
                }
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
