using System;
using System.Windows.Media.Imaging;

namespace RomManager
{
    public class Game
    {
        public string FilePath;
        public string Title;
        public string Region;
        public string SystemName;
        public string Description;
        public string Developer;
        public string Publisher;
        public string[] Genre;
        public string ReleaseDate;

        public BitmapImage FrontCover { get; set; }
        public BitmapImage BackCover { get; set; }
    }
}
