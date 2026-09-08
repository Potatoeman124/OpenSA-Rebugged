#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public enum RmgColonyOwnershipMode
	{
		ClosestToSpawn,
		Random
	}

	public static class RmgColonyOwnership
	{
		public static string ModeName(RmgColonyOwnershipMode mode) => mode switch
		{
			RmgColonyOwnershipMode.ClosestToSpawn => "closest-to-spawn",
			RmgColonyOwnershipMode.Random => "random",
			_ => throw new ArgumentException("Unknown starting colony ownership mode.")
		};

		public static string ModeDisplayName(RmgColonyOwnershipMode mode) => mode == RmgColonyOwnershipMode.Random ? "Random" : "Closest to Spawn";

		public static RmgColonyOwnershipMode ParseMode(string value) => value switch
		{
			"closest-to-spawn" => RmgColonyOwnershipMode.ClosestToSpawn,
			"random" => RmgColonyOwnershipMode.Random,
			_ => throw new ArgumentException("starting_colony_mode must be closest-to-spawn or random.")
		};

		public static void ValidateShares(int[] shares, int players)
		{
			if (shares == null || (shares.Length != 0 && shares.Length != players) || shares.Any(value => value < 0 || value > 100))
				throw new ArgumentException("Starting colony shares must contain one integer from 0 through 100 per player.");
		}

		public static int[] ParseShares(JToken token, int players)
		{
			if (token is not JArray array || array.Count != players || players < 1 || players > 8 ||
				array.Any(value => value.Type != JTokenType.Integer || (decimal)value < 0 || (decimal)value > 100))
				throw new ArgumentException("starting_colony_shares must contain one integer from 0 through 100 per player.");
			return array.Select(value => (int)value).ToArray();
		}

		public static int[] Allocate(int colonies, IReadOnlyList<int> shares)
		{
			if (colonies < 0 || shares.Any(value => value < 0 || value > 100))
				throw new ArgumentException("Invalid colony count or ownership shares.");
			var sum = shares.Sum();
			var counts = new int[shares.Count];
			if (sum == 0 || colonies == 0) return counts;
			var total = (int)(((long)colonies * Math.Min(sum, 100) + 99) / 100);
			for (var i = 0; i < counts.Length; i++) counts[i] = (int)((long)total * shares[i] / sum);
			var remaining = total - counts.Sum();
			foreach (var i in Enumerable.Range(0, shares.Count).Where(i => shares[i] > 0)
				.OrderByDescending(i => (long)total * shares[i] % sum).ThenBy(i => i).Take(remaining))
				counts[i]++;
			return counts;
		}

		// Preserve the same quotas in both modes. Closest ranks player/site pairs by distance;
		// Random shuffles quota labels across the entire colony pool.
		public static int[] Assign(IReadOnlyList<RmgPoint> colonies, IReadOnlyList<RmgPoint> starts, IReadOnlyList<int> shares,
			RmgColonyOwnershipMode mode = RmgColonyOwnershipMode.ClosestToSpawn, ulong seed = 0)
		{
			if (!Enum.IsDefined(mode)) throw new ArgumentException("Unknown starting colony ownership mode.");
			if (starts.Count != shares.Count) throw new ArgumentException("Starting positions and shares must match.");
			var remaining = Allocate(colonies.Count, shares);
			var owners = Enumerable.Repeat(-1, colonies.Count).ToArray();
			if (mode == RmgColonyOwnershipMode.Random)
			{
				// Shuffle quota labels across the entire pool, including unowned labels. Every colony has
				// the same chance of each owner; neither player order nor distance grants first choice.
				var next = 0;
				for (var p = 0; p < remaining.Length; p++)
					for (var n = 0; n < remaining[p]; n++) owners[next++] = p;
				// Dedicated RMG ownership stream: never consume world/lobby or terrain random state.
				var random = new DeterministicRandom(seed ^ 0x434F4C4F4E594F57UL);
				for (var i = owners.Length - 1; i > 0; i--)
				{
					var j = random.NextInt(i + 1);
					(owners[i], owners[j]) = (owners[j], owners[i]);
				}
				return owners;
			}
			long Distance(int p, int c) => (long)(starts[p].X - colonies[c].X) * (starts[p].X - colonies[c].X) +
				(long)(starts[p].Y - colonies[c].Y) * (starts[p].Y - colonies[c].Y);
			var pairs = Enumerable.Range(0, starts.Count).Where(p => remaining[p] > 0)
				.SelectMany(p => Enumerable.Range(0, colonies.Count).Select(c => (Player: p, Colony: c)))
				.OrderBy(pair => Distance(pair.Player, pair.Colony)).ThenBy(pair => pair.Player).ThenBy(pair => pair.Colony);
			foreach (var pair in pairs)
				if (remaining[pair.Player] > 0 && owners[pair.Colony] == -1)
				{
					owners[pair.Colony] = pair.Player;
					remaining[pair.Player]--;
				}
			return owners;
		}
	}
}
