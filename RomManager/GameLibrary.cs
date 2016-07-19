using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using unirest_net.http;

namespace RomManager
{
    public class GameLibrary
    {
        Dispatcher uiDispatcher;

        public ObservableCollection<Game> Games { get; private set; } = new ObservableCollection<Game>();
        public Game CurrentGame { get; set; }

        public GameLibrary()
        {
            uiDispatcher = Dispatcher.CurrentDispatcher;

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
                        uiDispatcher.InvokeAsync(() => game.Cover.BitmapImage = new BitmapImage(new Uri(uri)));
                    }

                    // resolve screenshots
                    foreach (var screenshot in game.Screenshots)
                    {
                        var uri = string.Format("https://res.cloudinary.com/igdb/image/upload/t_{0}/{1}.jpg", "screenshot_big", screenshot.CloudinaryId);
                        uiDispatcher.InvokeAsync(() => screenshot.BitmapImage = new BitmapImage(new Uri(uri)));
                    }

                    uiDispatcher.InvokeAsync(() => Games.Add(game));
                });
            });
        }
    }
}
