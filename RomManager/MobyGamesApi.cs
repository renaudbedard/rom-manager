using RestSharp;
using RestSharp.Authenticators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RomManager
{
	public class MGGamesResponse
	{
		public List<MGGame> Games { get; set; }
	}
	public class MGGame
	{
		public int GameId { get; set; }
		public string Title { get; set; }
		public float MobyScore { get; set; }
		public int NumVotes { get; set; }
	}

	public class MobyGamesApi
	{
		const float RequestCooldown = 1;
		const int GamesPerRequest = 100;

		RestClient client;
		Dictionary<int, List<MGGame>> gamesPerPlatform = new Dictionary<int, List<MGGame>>();
		DateTime lastRequest;

		public MobyGamesApi()
		{
			client = new RestClient("https://api.mobygames.com/v1/");
			client.Authenticator = new HttpBasicAuthenticator(ApiKeys.MobyGames, null);
		}

		void CheckRequestFrequency()
		{
			var sinceLastRequest = DateTime.Now - lastRequest;
			if (sinceLastRequest.TotalSeconds < RequestCooldown)
				Thread.Sleep((int) ((RequestCooldown - sinceLastRequest.TotalSeconds) * 1000) + 64);
		}

		public async Task GetGames(int platformId)
		{
			IRestResponse<MGGamesResponse> response;
			int offset = 0;

			do
			{
				var request = new RestRequest("games", Method.GET);
				request.AddParameter("platform", platformId);
				request.AddParameter("format", "brief");
				request.AddParameter("offset", offset);

				CheckRequestFrequency();
				response = await client.ExecuteGetTaskAsync<MGGamesResponse>(request);
				lastRequest = DateTime.Now;

				List<MGGame> platformGames;
				if (!gamesPerPlatform.TryGetValue(platformId, out platformGames))
					gamesPerPlatform[platformId] = (platformGames = new List<MGGame>());

				platformGames.AddRange(response.Data.Games);
				offset += GamesPerRequest;

				Console.WriteLine($"Added { response.Data.Games.Count } games to platform { platformId }...");
			}
			while (response.Data.Games.Count == GamesPerRequest);

			Console.WriteLine($"Done! Total of { gamesPerPlatform[platformId].Count } games.");
		}

		public async Task<MGGame> Match(int platformId, string title)
		{
			if (!gamesPerPlatform.ContainsKey(platformId))
				await GetGames(platformId);

			var games = gamesPerPlatform[platformId];
			return games[games.AsParallel()
				.Select((x, i) => new { Index = i, LD = StringHelper.LevenshteinDistance(title, x.Title) })
				.OrderBy(x => x.LD)
				.First().Index];

			// TODO: if edit distance is too great, disregard entirely
			// TODO: grab mobyscore for 100-batch of matches
		}
	}
}
