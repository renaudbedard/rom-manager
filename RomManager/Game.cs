using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace RomManager
{
    public class Game
    {
        public string FilePath { get; set; }
        public string FileName
        {
            get { return Path.GetFileName(FilePath); }
        }

        public string Title { get; set; }
        public string Region { get; set; }
        public string SystemName { get; set; }
        public string Description { get; set; }
        public string Developer { get; set; }
        public string Publisher { get; set; }
        public string Genres { get; set; }
        public string ReleaseDate { get; set; }

        public string FrontCoverCachePath { get; set; }
        public Uri FrontCoverUri { get; set; }
        public string BackCoverCachePath { get; set; }
        public Uri BackCoverUri { get; set; }

        public BitmapImage FrontCover { get; set; }
        public BitmapImage BackCover { get; set; }
    }
}
