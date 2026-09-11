#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.OpenSA.Rmg.Reassessment;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public enum RmgIslandAmount { Few, Standard, Many, Extreme, Ultra }
	public enum RmgIslandSize { Small, Standard, Large, Extreme, Ultra }

	public static class RmgArchipelagoParameters
	{
		public static string Name(RmgIslandAmount value) => value.ToString().ToLowerInvariant();
		public static string Name(RmgIslandSize value) => value.ToString().ToLowerInvariant();
		public static RmgIslandAmount ParseAmount(string value) => value switch
		{
			"few" => RmgIslandAmount.Few, "standard" => RmgIslandAmount.Standard, "many" => RmgIslandAmount.Many,
			"extreme" => RmgIslandAmount.Extreme, "ultra" => RmgIslandAmount.Ultra, _ => throw new ArgumentException("Invalid island_amount.")
		};
		public static RmgIslandSize ParseSize(string value) => value switch
		{
			"small" => RmgIslandSize.Small, "standard" => RmgIslandSize.Standard, "large" => RmgIslandSize.Large,
			"extreme" => RmgIslandSize.Extreme, "ultra" => RmgIslandSize.Ultra, _ => throw new ArgumentException("Invalid island_size.")
		};
		public static void ValidateOptions(RmgPlayerSettings settings)
		{
			if (!Enum.IsDefined(settings.IslandAmount) || !Enum.IsDefined(settings.IslandSize) ||
				(!settings.IsArchipelago && (settings.IslandAmount != RmgIslandAmount.Standard || settings.IslandSize != RmgIslandSize.Standard)) ||
				(settings.LayoutFamily == RmgPlayerLayoutFamily.Archipelago && !settings.IsArchipelago))
				throw new ArgumentException("Island options require schema 19 and Archipelago.");
		}

		public static void Validate(RmgGenerationSettings settings)
		{
			if (!Enum.IsDefined(settings.IslandAmount) || !Enum.IsDefined(settings.IslandSize) ||
				(settings.GeneratorVersion != 24 && (settings.IslandAmount != RmgIslandAmount.Standard || settings.IslandSize != RmgIslandSize.Standard)))
				throw new ArgumentException("Island options apply only to Archipelago.");
			if (settings.GeneratorVersion == 24 && (settings.MirroringAxes != 0 || settings.LaneWidth != RmgBattlefieldLaneWidth.Standard ||
				settings.BlockShape != RmgBattlefieldBlockShape.CutCorners || settings.SideConnections != RmgCrossroadsConnections.Standard ||
				settings.LandCrossings != RmgLandCrossings.One || settings.RingShape != RmgRingShape.Round || !settings.GenerateCastles))
				throw new ArgumentException("Archipelago requires asymmetric geography and island options.");
		}

		public static int Count(int size, RmgIslandAmount amount) => (size switch
		{
			64 => new[] { 2, 2, 2, 2, 2 }, 128 => new[] { 2, 3, 4, 6, 8 },
			256 => new[] { 3, 7, 13, 22, 32 }, _ => new[] { 4, 12, 24, 42, 64 }
		})[(int)amount];
	}

	sealed class ArchipelagoGeometry
	{
		public readonly List<RmgPoint> Centers = new();
		public readonly List<RmgPoint> Starts = new();
		public readonly List<RmgPoint> Nests = new();
		public readonly bool[] Clear, Land, Sea;
		public readonly int[] Region;
		public readonly double[] Boundary;
		public readonly double LandRadiusFraction;

		public static ArchipelagoGeometry Create(RmgGenerationSettings settings, RmgProfile profile)
		{
			for (var attempt = 0; attempt < 32; attempt++)
				try { return new ArchipelagoGeometry(settings, profile, attempt); }
				catch (RmgGenerationRejectedException e) when (e.RejectionCode == "ARCHIPELAGO_START_CAPACITY" && attempt < 31) { }
			throw new InvalidOperationException("Island anchor planning exhausted its attempts.");
		}

		ArchipelagoGeometry(RmgGenerationSettings settings, RmgProfile profile, int attempt)
		{
			var size = settings.MapSize; var count = RmgArchipelagoParameters.Count(size, settings.IslandAmount);
			var random = new DeterministicRandom(TerrainComparison.Mix(settings.Seed, 2401 + (ulong)attempt * 7919));
			long Distance(RmgPoint a, RmgPoint b) => (long)(a.X - b.X) * (a.X - b.X) + (long)(a.Y - b.Y) * (a.Y - b.Y);

			// Progressive dispersed anchors: changing island amount retains earlier seeds.
			for (var n = 0; n < count; n++)
			{
				var candidates = Enumerable.Range(0, 256).Select(_ => new RmgPoint(11 + random.NextInt(size - 22), 11 + random.NextInt(size - 22))).ToArray();
				var next = candidates.OrderByDescending(p => Centers.Select(q => Distance(p, q)).DefaultIfEmpty(size * size).Min()).First();
				Centers.Add(next); Nests.Add(new RmgPoint(next.X - 2, next.Y - 2));
			}

			Region = new int[size * size]; Boundary = new double[Region.Length];
			Clear = new bool[Region.Length]; Land = new bool[Region.Length]; Sea = new bool[size * size / 4];
			for (var i = 0; i < Region.Length; i++)
			{
				var p = new RmgPoint(i % size, i / size);
				var near = Enumerable.Range(0, count).OrderBy(n => Distance(p, Centers[n])).First();
				Region[i] = near;
				var edge = (double)Math.Min(Math.Min(p.X, size - 1 - p.X), Math.Min(p.Y, size - 1 - p.Y));
				for (var n = 0; n < count; n++)
					if (n != near) edge = Math.Min(edge, (Distance(p, Centers[n]) - Distance(p, Centers[near])) / (2 * Math.Sqrt(Distance(Centers[n], Centers[near]))));
				Boundary[i] = edge;
			}

			var rules = profile.ColonyCombatRules;
			var possible = Enumerable.Range(0, Region.Length).Where(i => Boundary[i] >= 9)
				.Select(i => new RmgPoint(i % size - 2, i / size - 2)).Where(p => Nests.All(n => rules.ColonyStartMarginAtNative("wasps_colony", n, p) >= 0)).ToArray();
			for (var n = 0; n < settings.PlayerCount; n++)
			{
				var next = possible.Where(p => Starts.All(q => rules.StartMarginAtNative(p, q) >= 0))
					.OrderByDescending(p =>
					{
						var i = (p.Y + 2) * size + p.X + 2;
						var centrality = Boundary[i] / (Boundary[i] + Math.Sqrt(Distance(p, Nests[Region[i]])));
						return Starts.Select(q => Distance(p, q)).DefaultIfEmpty(Nests.Min(q => Distance(p, q))).Min() * centrality * centrality;
					}).Take(1).ToArray();
				if (next.Length == 0) throw new RmgGenerationRejectedException("ARCHIPELAGO_START_CAPACITY", "Not enough space for safe starts and an independent Wasps nest per island. Use fewer islands or players, or a larger map.");
				Starts.Add(next[0]);
			}

			// Only the seed's minimum nest/start sites are reserved. Colony density never shapes land.
			void Disk(RmgPoint p, double radius, bool clear)
			{
				for (var y = Math.Max(0, (int)(p.Y - radius)); y <= Math.Min(size - 1, p.Y + radius); y++)
					for (var x = Math.Max(0, (int)(p.X - radius)); x <= Math.Min(size - 1, p.X + radius); x++)
						if (Distance(new RmgPoint(x, y), p) <= radius * radius) { Land[y * size + x] = true; if (clear) Clear[y * size + x] = true; }
			}

			foreach (var p in Centers) Disk(p, 8, true);
			foreach (var start in Starts)
			{
				var center = new RmgPoint(start.X + 2, start.Y + 2); var nest = Centers[Region[center.Y * size + center.X]];
				Disk(center, 8, true);
				var length = Math.Sqrt(Distance(center, nest));
				for (var j = 0; j <= length; j++) Disk(new RmgPoint((int)Math.Round(center.X + (nest.X - center.X) * j / length), (int)Math.Round(center.Y + (nest.Y - center.Y) * j / length)), 4, false);
			}

			var sizeFractions = new[] { .43, .62, .78, .89, .97 };
			var waterErosion = new[] { -.06, 0, .06, .13, .21 };
			LandRadiusFraction = sizeFractions[(int)settings.IslandSize] - waterErosion[(int)settings.WaterAmount];
			var depth = (int)settings.TerrainComplexity; var phase = settings.Seed % 997 / 997D * Math.PI * 2;
			for (var i = 0; i < Land.Length; i++)
			{
				var p = new RmgPoint(i % size, i / size); var c = Centers[Region[i]];
				var radius = Math.Sqrt(Distance(p, c)); var angle = Math.Atan2(p.Y - c.Y, p.X - c.X);
				var broad = .08 * Math.Sin(angle * 3 + phase + Region[i]) + .05 * Math.Cos(angle * 2 - phase);
				var detail = depth * .037 * Math.Sin(angle * (5 + depth * 2) + phase) + depth * .028 * Math.Sin(p.X / (12D - depth * 2) + phase) * Math.Cos(p.Y / (15D - depth * 2) - phase);
				var fraction = Math.Clamp(LandRadiusFraction + broad + detail, .18, 1);
				if (Boundary[i] >= 4 && radius <= (radius + Boundary[i] - 4) * fraction) Land[i] = true;
			}

			for (var i = 0; i < Sea.Length; i++)
			{
				var x = 2 * (i % (size / 2)); var y = 2 * (i / (size / 2));
				Sea[i] = Enumerable.Range(0, 4).Any(f => Boundary[(y + f / 2) * size + x + f % 2] < 4) || !Enumerable.Range(0, 4).Any(f => Land[(y + f / 2) * size + x + f % 2]);
			}

			// Keep only dry cells connected to a planned island, before native shore emission.
			var labels = Components(Sea.Select(v => !v).ToArray(), size / 2);
			var keep = Centers.Select(p => labels[p.Y / 2 * (size / 2) + p.X / 2]).ToHashSet();
			for (var i = 0; i < Sea.Length; i++) if (!keep.Contains(labels[i])) Sea[i] = true;
		}

		public static int[] Components(bool[] land, int width)
		{
			var labels = Enumerable.Repeat(-1, land.Length).ToArray(); var queue = new Queue<int>(); var count = 0;
			for (var i = 0; i < land.Length; i++)
			{
				if (!land[i] || labels[i] >= 0) continue;
				labels[i] = count; queue.Enqueue(i);
				while (queue.TryDequeue(out var at))
					foreach (var next in Neighbors(at, width))
						if (land[next] && labels[next] < 0) { labels[next] = count; queue.Enqueue(next); }
				count++;
			}

			return labels;
		}

		static IEnumerable<int> Neighbors(int i, int width)
		{
			if (i % width > 0) yield return i - 1;
			if (i % width < width - 1) yield return i + 1;
			if (i >= width) yield return i - width;
			if (i + width < width * width) yield return i + width;
		}
	}
}
