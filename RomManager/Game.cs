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

        public int MobyGamesPlatformId
        {
            get
            {
                switch (SystemName)
                {
                    case "3DO Interactive Multiplayer": return 35;
                    case "Arcade": return 143;
                    case "Atari 2600": return 28;
                    case "Atari 5200": return 33;
                    case "Atari 7800": return 34;
                    case "Atari Lynx": return 18;
                    case "Atari Jaguar": return 17;
                    case "Bandai WonderSwan": return 48;
                    case "Bandai WonderSwan Color": return 49;
                    case "Coleco ColecoVision": return 29;
                    case "GCE Vectrex": return 37;
                    case "Intellivision": return 30;
                    case "NEC PC Engine/TurboGrafx-16": return 40;
                    case "NEC PC ENgine/TurboGrafx-CD": return 45;
                    case "NEC PC-FX": return 59;
                    case "NEC SuperGrafx": return 127;
                    case "Nintendo Famicom Disk System": return 22;
                    case "Nintendo Game Boy": return 10;
                    case "Nintendo Game Boy Advance": return 12;
                    case "Nintendo Game Boy Color": return 11;
                    case "Nintendo GameCube": return 14;
                    case "Nintendo 64": return 9;
                    case "Nintendo DS": return 44;
                    case "Nintendo Entertainment System": return 22;
                    case "Nintendo Super Entertainment System": return 15;
                    case "Nintendo Virtual Boy": return 38;
                    case "Nintendo Wii": return 82;
                    case "Sega 32X": return 21;
                    case "Sega CD/Mega-CD": return 20;
                    case "Sega Master System": return 26;
                    case "Sega Saturn": return 23;
                    case "Sega Game Gear": return 25;
                    case "Sega Genesis/Mega Drive": return 16;
                    case "Sega SG-1000": return 114;
                    case "SNK Neo Geo Pocket": return 52;
                    case "SNK Neo Geo Pocket Color": return 53;
                    case "Sony PlayStation Portable": return 46;
                    case "Sony PlayStation": return 6;
                    case "Magnavox Odyssey": return 75;
                    case "Magnavox Odyssey2": return 78;
                    case "Commodore 64": return 27;
                    case "Microsoft MSX": return 57;
                    default: return -1;
                }
            }
        }

        public string Title { get; set; }
        public string Region { get; set; }
        public string SystemName { get; set; }
        public string Description { get; set; }
        public string Developer { get; set; }
        public string Genres { get; set; }
        public string ReleaseDate { get; set; }

        public float MobyScore { get; set; }
        public string ScorePercentage
        {
            get { return string.Format("{0:P0}", MobyScore / 5.0f); }
        }

        public string FrontCoverCachePath { get; set; }
        public Uri FrontCoverUri { get; set; }
        public string BackCoverCachePath { get; set; }
        public Uri BackCoverUri { get; set; }

        public BitmapImage FrontCover { get; set; }
        public BitmapImage BackCover { get; set; }
    }
}
