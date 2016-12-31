using System;
using System.Windows.Media.Imaging;

namespace RomManager
{
    public class Game
    {
        public string FilePath { get; set; }
        public string Title { get; set; }
        public string Region { get; set; }
        public string SystemName { get; set; }
        public string Description { get; set; }
        public string Developer { get; set; }
        public string Publisher { get; set; }
        public string[] Genre { get; set; }
        public string ReleaseDate { get; set; }

        public string FrontCoverCachePath { get; set; }
        public Uri FrontCoverUri { get; set; }
        public string BackCoverCachePath { get; set; }
        public Uri BackCoverUri { get; set; }

        public BitmapImage FrontCover { get; set; }
        public BitmapImage BackCover { get; set; }
    }
}
