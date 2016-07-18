using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using unirest_net.http;

namespace RomManager
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        GameLibrary Games;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Games = (Application.Current.Resources["Games"] as ObjectDataProvider).Data as GameLibrary;

            var requestURI = string.Format(
                            "https://igdbcom-internet-game-database-v1.p.mashape.com/games/?search={0}&fields={1}&limit={2}",
                            Uri.EscapeDataString("a link to the past"), // search
                            Uri.EscapeDataString("*"), // fields
                            1); // limit

            Task<HttpResponse<Game[]>> asyncResponse = Unirest.get(requestURI)
                .header("X-Mashape-Key", "Xfb4BtFk3RmshL3HnA1fgXXPEyGEp16mndvjsntX7K4pclsD7h")
                .header("Accept", "application/json")
                .asJsonAsync<Game[]>();

            asyncResponse.ContinueWith(context =>
            {
                Parallel.ForEach(context.Result.Body, game =>
                {
                    // resolve cover image
                    if (game.Cover != null)
                    {
                        var uri = string.Format("https://res.cloudinary.com/igdb/image/upload/t_{0}/{1}.jpg", "cover_big_2x", game.Cover.CloudinaryId);
                        Dispatcher.Invoke(() => game.Cover.BitmapImage = new BitmapImage(new Uri(uri)));
                    }

                    // resolve screenshots
                    foreach (var screenshot in game.Screenshots)
                    {
                        var uri = string.Format("https://res.cloudinary.com/igdb/image/upload/t_{0}/{1}.jpg", "screenshot_big", screenshot.CloudinaryId);
                        Dispatcher.Invoke(() => screenshot.BitmapImage = new BitmapImage(new Uri(uri)));
                    }

                    Dispatcher.Invoke(() => Games.Add(game));
                });
            });
        }
    }
}

