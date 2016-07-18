using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using unirest_net.http;

namespace RomManager
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            TestAPI();
        }

        public void TestAPI()
        {
            var requestURI = string.Format(
                "https://igdbcom-internet-game-database-v1.p.mashape.com/games/?search={0}&fields={1}&limit={2}",
                Uri.EscapeDataString("a link to the past"), // search
                Uri.EscapeDataString("*"), // fields
                1); // limit

            HttpResponse<Game[]> response = Unirest.get(requestURI)
                .header("X-Mashape-Key", "Xfb4BtFk3RmshL3HnA1fgXXPEyGEp16mndvjsntX7K4pclsD7h")
                .header("Accept", "application/json")
                .asJson<Game[]>();

            var games = response.Body;
            foreach (var game in games)
            {
                // resolve cover image
                if (game.Cover != null)
                    game.Cover.BitmapImage = new BitmapImage(new Uri(string.Format("https://res.cloudinary.com/igdb/image/upload/t_{0}/{1}.jpg", "cover_big_2x", game.Cover.CloudinaryId)));

                // resolve screenshots
                foreach (var screenshot in game.Screenshots)
                    screenshot.BitmapImage = new BitmapImage(new Uri(string.Format("https://res.cloudinary.com/igdb/image/upload/t_{0}/{1}.jpg", "screenshot_big", screenshot.CloudinaryId)));
            }

            image.Source = games[0].Cover.BitmapImage;
        }
    }
}

