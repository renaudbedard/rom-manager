using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace RomManager
{
    class Game
    {
        public int Id;
        public string Name;
        public string Url;
        public string Summary;
        public string Storyline;
        public int Collection;
        public float Rating;
        [JsonProperty("rating_count")]
        public int RatingCount;
        public Image[] Screenshots;
        public Image Cover;
    }

    class Image
    {
        [JsonProperty("cloudinary_id")]
        public string CloudinaryId;
        public int Width;
        public int Height;

        [JsonIgnore]
        public BitmapImage BitmapImage;
    }
}
