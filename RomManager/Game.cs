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
        public int Id { get; set; }
        public string Name { get; set; }
        public string Url { get; set; }
        public string Summary { get; set; }
        public string Storyline { get; set; }
        public int Collection { get; set; }
        public float Rating { get; set; }
        [JsonProperty("rating_count")]
        public int RatingCount { get; set; }
        public Image[] Screenshots { get; set; }
        public Image Cover { get; set; }
    }

    class Image
    {
        [JsonProperty("cloudinary_id")]
        public string CloudinaryId { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        [JsonIgnore]
        public BitmapImage BitmapImage { get; set; }
    }
}
