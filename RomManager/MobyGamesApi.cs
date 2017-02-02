using System.Collections.Generic;

namespace RomManager
{
	public class MobyGamesApi
	{
		public class GamesResponse
		{
			public List<Game> Games { get; set; }
		}
		public class Game
		{
			public string Title { get; set; }
			public float MobyScore { get; set; }
			public int NumVotes { get; set; }
		}
	}
}
