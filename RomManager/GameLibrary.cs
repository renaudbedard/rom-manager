using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.SQLite;
using System.IO;
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
        public void Analyze()
        {
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
                Parallel.ForEach(di.GetFiles(), file =>
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
                });

                allFilesHashed = true;
                fileHashed.Set();
            });

            var pathToDb = $"{AppDomain.CurrentDomain.BaseDirectory}openvgdb.sqlite";

            // match roms in database
            bool allRomsMatched = false;
            var romMatched = new ManualResetEventSlim();
            Task.Run(() =>
            {
                using (var connection = new SQLiteConnection($"Data Source={pathToDb}; Version=3;"))
                {
                    connection.Open();
                    using (var command = connection.CreateCommand())
                    {
                        while (!allFilesHashed)
                        {
                            fileHashed.Wait();

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
            Task.Run(() =>
            {
                using (var connection = new SQLiteConnection($"Data Source={pathToDb}; Version=3;"))
                {
                    connection.Open();
                    using (var command = connection.CreateCommand())
                    {
                        while (!allRomsMatched)
                        {
                            romMatched.Wait();

                            Tuple<string, long?> pair;
                            while (matchedRoms.TryDequeue(out pair))
                            {
                                var game = new Game { FilePath = pair.Item1 };

                                if (pair.Item2.HasValue)
                                {
                                    command.CommandText = $"SELECT releaseId, releaseTitleName, TEMPsystemName, releaseCoverFront, releaseCoverBack, releaseDescription, releaseDeveloper, releasePublisher, releaseGenre, releaseDate FROM RELEASES WHERE romID = '{pair.Item2.Value}' LIMIT 1";
                                    using (var reader = command.ExecuteReader())
                                    {
                                        if (reader.HasRows)
                                        {
                                            reader.Read();

                                            var releaseId = reader.GetInt64(0);
                                            game.Title = GetNullable<string>(reader.GetValue(1));
                                            game.SystemName = GetNullable<string>(reader.GetValue(2));
                                            game.Description = GetNullable<string>(reader.GetValue(5));
                                            game.Developer = GetNullable<string>(reader.GetValue(6));
                                            game.Publisher = GetNullable<string>(reader.GetValue(7));
                                            game.Genre = GetNullable<string>(reader.GetValue(8))?.Split(',');
                                            game.ReleaseDate = GetNullable<string>(reader.GetValue(9));

                                            var frontCoverUris = TryGetCachedImage(GetNullable<string>(reader.GetValue(3)), "FrontCover", releaseId);
                                            game.FrontCoverUri = frontCoverUris.Item1;
                                            game.FrontCoverCachePath = frontCoverUris.Item2;


                                            var backCoverUris = TryGetCachedImage(GetNullable<string>(reader.GetValue(4)), "BackCover", releaseId);
                                            game.BackCoverUri = backCoverUris.Item1;
                                            game.BackCoverCachePath = backCoverUris.Item2;
                                        }
                                    }
                                }

                                // make sure we have fallback images
                                if (game.FrontCoverUri == null)
                                    game.FrontCoverUri = TryGetCachedImage(null, "FrontCover", 0).Item1;
                                if (game.BackCoverUri == null)
                                    game.BackCoverUri = TryGetCachedImage(null, "BackCover", 0).Item1;

                                dispatcher.InvokeAsync(() =>
                                {
                                    game.FrontCover = ExpandImage(game.FrontCoverUri, game.FrontCoverCachePath);
                                    game.BackCover = ExpandImage(game.BackCoverUri, game.BackCoverCachePath);

                                    Games.Add(game);
                                });
                            }
                        }
                    }
                    connection.Close();

                    analyzing = false;
                    PropertyChanged(this, new PropertyChangedEventArgs("AnalyzeStopCaption"));
                }
            });
        }

        static T GetNullable<T>(object value) where T : class
        {
            return value is DBNull ? null : (T) value;
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
