using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.SQLite;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace RomManager
{
    public class GameLibrary : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<Game> Games { get; private set; } = new ObservableCollection<Game>();
        public Game CurrentGame { get; set; }

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

        public bool HasFolderPath
        {
            get { return FolderPath != null; }
        }

        public void Analyze()
        {
            // hash files
            var hashedFiles = new Dictionary<string, string>();
            var di = new DirectoryInfo(folderPath);
            var sb = new StringBuilder();
            using (var sha1 = SHA1.Create())
            {
                foreach (var file in di.GetFiles())
                {
                    sb.Clear();

                    var filePath = file.FullName;
                    var hash = sha1.ComputeHash(File.OpenRead(filePath));

                    foreach (byte b in hash)
                        sb.Append(b.ToString("X2"));
                    hashedFiles[filePath] = sb.ToString();
                }
            }

            // open database
            var pathToDb = $"{AppDomain.CurrentDomain.BaseDirectory}openvgdb.sqlite";
            using (var connection = new SQLiteConnection($"Data Source={pathToDb}; Version=3;"))
            {
                connection.Open();
                var command = connection.CreateCommand();

                // match roms in database
                var romIds = new Dictionary<string, long?>();
                foreach (var kvp in hashedFiles)
                {
                    command.CommandText = $"SELECT romID FROM ROMs WHERE romHashSHA1 = '{kvp.Value}' LIMIT 1";
                    var romId = command.ExecuteScalar();
                    if (romId != null)
                        romIds[kvp.Key] = (long)romId;
                    else
                    {
                        var extensionlessFileName = Path.GetFileNameWithoutExtension(kvp.Key);
                        command.CommandText = $"SELECT romID FROM ROMs WHERE romExtensionlessFileName = '{extensionlessFileName}' LIMIT 1";
                        romId = command.ExecuteScalar();
                        if (romId != null)
                            romIds[kvp.Key] = (long)romId;
                        else
                        {
                            romIds[kvp.Key] = null;
                            Console.WriteLine($"Could not find match for '{extensionlessFileName}'");
                        }
                    }
                }

                // get metadata
                foreach (var rom in romIds)
                {
                    var game = new Game { FilePath = rom.Key };
                    bool needsAdd = true;

                    if (rom.Value.HasValue)
                    {
                        command.CommandText = $"SELECT releaseId, releaseTitleName, TEMPsystemName, releaseCoverFront, releaseCoverBack, releaseDescription, releaseDeveloper, releasePublisher, releaseGenre, releaseDate FROM RELEASES WHERE romID = '{rom.Value.Value}' LIMIT 1";
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

                                game.FrontCover = TryGetCachedImage(GetNullable<string>(reader.GetValue(3)), "FrontCover", releaseId);
                                game.BackCover = TryGetCachedImage(GetNullable<string>(reader.GetValue(4)), "BackCover", releaseId);

                                if (game.FrontCover.IsDownloading)
                                {
                                    game.FrontCover.DownloadCompleted += (_, __) => Games.Add(game);
                                    needsAdd = false;
                                }
                            }
                        }
                    }

                    if (needsAdd)
                        Games.Add(game);
                }

                // close database
                connection.Close();
            }
        }

        static T GetNullable<T>(object value) where T : class
        {
            return value is DBNull ? null : (T) value;
        }
        static T? MakeNullable<T>(object value) where T : struct
        {
            return value is DBNull ? null : (T?)(T)value;
        }

        static BitmapImage TryGetCachedImage(string url, string type, long releaseId)
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

            BitmapImage image;

            string imageFilePath;
            if (url == null)
            {
                // TODO: per-system fallback images
                imageFilePath = Path.Combine($"{AppDomain.CurrentDomain.BaseDirectory}Fallback Covers", $"fallback_{type}_md.jpg");
            }
            else
                imageFilePath = Path.Combine(byType, releaseId.ToString()) + url.Substring(url.LastIndexOf('.'));

            if (!File.Exists(imageFilePath))
            {
                image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(url);
                image.DecodePixelHeight = 256;
                image.EndInit();

                image.DownloadCompleted += (_, __) => 
                {
                    JpegBitmapEncoder encoder = new JpegBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(image));

                    using (var filestream = new FileStream(imageFilePath, FileMode.Create))
                    {
                        encoder.Save(filestream);
                    }
                };
            }
            else
                image = new BitmapImage(new Uri(imageFilePath));

            return image;
        }
    }
}
