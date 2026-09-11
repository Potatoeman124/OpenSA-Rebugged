#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OpenRA.Mods.OpenSA.Rmg.Reassessment;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public enum RmgChaosScale { Small, Standard, Large }
	public enum RmgChaosBiomes { Single, Patchwork, Fractured }

	public static class RmgChaosParameters
	{
		public static string Name(RmgChaosScale value) => value.ToString().ToLowerInvariant();
		public static string Name(RmgChaosBiomes value) => value.ToString().ToLowerInvariant();
		public static RmgChaosScale ParseScale(string value) => value switch
		{
			"small" => RmgChaosScale.Small, "standard" => RmgChaosScale.Standard, "large" => RmgChaosScale.Large,
			_ => throw new ArgumentException("chaos_scale must be small, standard or large.")
		};
		public static RmgChaosBiomes ParseBiomes(string value) => value switch
		{
			"single" => RmgChaosBiomes.Single, "patchwork" => RmgChaosBiomes.Patchwork, "fractured" => RmgChaosBiomes.Fractured,
			_ => throw new ArgumentException("chaos_biomes must be single, patchwork or fractured.")
		};
		public static void ValidateOptions(RmgPlayerSettings settings)
		{
			if (!Enum.IsDefined(settings.ChaosScale) || !Enum.IsDefined(settings.ChaosBiomes) ||
				(!settings.IsChaos && (settings.ChaosScale != RmgChaosScale.Standard || settings.ChaosBiomes != RmgChaosBiomes.Patchwork)) ||
				(settings.LayoutFamily == RmgPlayerLayoutFamily.Chaos && !settings.IsChaos))
				throw new ArgumentException("Chaos options require schema 20 and Chaos.");
		}

		public static void Validate(RmgGenerationSettings settings)
		{
			if (!Enum.IsDefined(settings.ChaosScale) || !Enum.IsDefined(settings.ChaosBiomes) ||
				(settings.GeneratorVersion != 25 && (settings.ChaosScale != RmgChaosScale.Standard || settings.ChaosBiomes != RmgChaosBiomes.Patchwork)))
				throw new ArgumentException("Chaos options apply only to Chaos.");
			if (settings.GeneratorVersion == 25 && (settings.MirroringAxes != 0 || settings.LaneWidth != RmgBattlefieldLaneWidth.Standard ||
				settings.BlockShape != RmgBattlefieldBlockShape.CutCorners || settings.SideConnections != RmgCrossroadsConnections.Standard ||
				settings.LandCrossings != RmgLandCrossings.One || settings.RingShape != RmgRingShape.Round || !settings.GenerateCastles ||
				settings.OwnStartingStronghold || settings.ExtraRoutes != RmgLabyrinthRoutes.Standard))
				throw new ArgumentException("Chaos requires asymmetric geography and its own collision options.");
		}
	}

	sealed class ChaosGeometry
	{
		public readonly List<RmgPoint> Starts = new();
		public readonly List<RmgPoint> CoreNests = new();
		public readonly bool[] Clear, Land;
		public readonly double[] Water, Geology, Moisture;
		public readonly JObject Report;
		readonly int size;
		sealed record Collision(int Kind, double X, double Y, double Radius, double Angle, double Phase, int Level)
		{
			public double Cosine { get; } = Math.Cos(Angle);
			public double Sine { get; } = Math.Sin(Angle);
		}

		public ChaosGeometry(RmgGenerationSettings settings, RmgProfile profile)
		{
			size = settings.MapSize;
			var depth = (int)settings.TerrainComplexity;
			SelectAnchors(settings, profile);
			Clear = new bool[size * size]; Land = new bool[Clear.Length];
			foreach (var p in Starts.Concat(CoreNests)) Disk(p.X + 2, p.Y + 2, 8, true);
			foreach (var p in Starts)
			{
				var nest = CoreNests.OrderBy(q => Distance(p, q)).First();
				var salt = TerrainComparison.Mix(settings.Seed, (ulong)(2510 + p.Y * size + p.X));
				var bend = new RmgPoint(Math.Clamp((p.X + nest.X) / 2 + (int)(salt % 17) - 8, 9, size - 10),
					Math.Clamp((p.Y + nest.Y) / 2 + (int)(salt / 17 % 17) - 8, 9, size - 10));
				Passage(new RmgPoint(p.X + 2, p.Y + 2), bend); Passage(bend, new RmgPoint(nest.X + 2, nest.Y + 2));
			}

			// Earlier collisions retain centers, rotations and scale as complexity rises. New
			// fragments intervene locally; there is no global reflection or fairness scaffold.
			var random = new DeterministicRandom(TerrainComparison.Mix(settings.Seed, 2500));
			var collisions = new List<Collision>();
			var broadCount = 8 + size / 64;
			var scale = settings.ChaosScale switch { RmgChaosScale.Small => .62, RmgChaosScale.Large => 1.55, _ => 1D };
			for (var level = 0; level <= depth; level++)
				for (var j = 0; j < (level == 0 ? broadCount : 3 + size / 96); j++)
				{
					var radius = Math.Max(12, size * (.17 + random.NextInt(100) / 500D) * scale / (1 + level * .42));
					collisions.Add(new Collision((j + level * 3 + (int)(settings.Seed % 8)) % 8,
						4 + random.NextInt(size - 8), 4 + random.NextInt(size - 8), radius,
						random.NextInt(6283) / 1000D, random.NextInt(6283) / 1000D, level));
				}

			var width = size / 2; var lattice = width + 1;
			Water = new double[width * width]; Geology = new double[lattice * lattice]; Moisture = new double[Geology.Length];
			var phase = settings.Seed % 997 / 997D * Math.PI * 2;
			for (var y = 0; y < lattice; y++)
				for (var x = 0; x < lattice; x++)
				{
					var nx = 2D * x; var ny = 2D * y;
					var water = .45 * Math.Sin(nx / (size * .18) + phase) + .4 * Math.Cos(ny / (size * .15) - phase);
					var rock = Math.Sin(nx / 31 + phase) + Math.Cos(ny / 27 - phase); var moss = Math.Cos((nx + ny) / 33 + phase);
					foreach (var c in collisions)
					{
						var dx = nx - c.X; var dy = ny - c.Y; var cs = c.Cosine; var sn = c.Sine;
						var u = (dx * cs + dy * sn) / c.Radius; var v = (-dx * sn + dy * cs) / c.Radius;
						var radius = Math.Sqrt(u * u + v * v); if (radius > 1.45) continue;
						var a = Math.Atan2(v, u); var influence = Math.Clamp((1.45 - radius) * 2.4, 0, 1) * (c.Level == 0 ? .88 : .7);
						var frequency = 1 + depth * .42;
						var square = Math.Max(Math.Abs(u), Math.Abs(v));
						var local = c.Kind switch
						{
							0 => 1.2 * Math.Sin(5 * radius - .6 + .6 * Math.Sin(a * 3 + c.Phase)),
							1 => Math.Cos(radius * (8 + 2 * frequency) + a * 1.2 + c.Phase) * 1.5,
							2 => 1.3 - 7 * Math.Abs(square - .72) - (Math.Abs(u) < .11 || (v > .5 && Math.Abs(u - .35) < .12) ? 2 : 0),
							3 => 1.1 * Math.Cos(u * (8 + frequency * 6) + 2 * Math.Sin(v * (3 + frequency))) - .3 * Math.Cos(v * 5),
							4 => 1.1 - 5 * Math.Min(Math.Abs(u + .35 * Math.Sin(v * 3)), Math.Abs(v - .3 * u)),
							5 => 1.5 - 7 * Math.Abs(u + .25 * Math.Sin(v * (4 + frequency) + c.Phase)),
							6 => 1.7 * Math.Min(Math.Cos(u * (7 + 3 * frequency)), Math.Cos(v * (9 + 2 * frequency))),
							_ => .9 * Math.Sin(u * 5 + c.Phase) * Math.Cos(v * 4) + .7 * Math.Cos(radius * (6 + frequency * 3))
						};
						water = water * (1 - influence) + influence * local;
						rock = rock * (1 - influence * .65) + influence * (Math.Sin(u * (5 + depth * 2) + c.Phase) + Math.Cos(radius * 8 + a));
						moss = moss * (1 - influence * .8) + influence * (Math.Cos(v * (6 + depth * 2) - c.Phase) - Math.Sin(radius * 9 - a * 2));
					}

					if (x < width && y < width) Water[y * width + x] = water;
					Geology[y * lattice + x] = rock; Moisture[y * lattice + x] = moss * 4;
				}

			Report = new JObject
			{
				["collision_scale"] = RmgChaosParameters.Name(settings.ChaosScale), ["collision_count"] = collisions.Count,
				["collision_families"] = new JArray("islands", "spiral-rings", "fortress-moats", "labyrinth", "crossroads", "faults", "battlefield-blocks", "natural-fields"),
				["collisions"] = new JArray(collisions.Select(c => new JObject { ["kind"] = c.Kind, ["x"] = c.X, ["y"] = c.Y, ["radius"] = c.Radius, ["angle"] = c.Angle, ["level"] = c.Level })),
				["terrain_priority"] = "TERRAIN_BEFORE_COLONIES_NO_REPAINT", ["minimum_shore_clearance_native"] = 2,
				["mandatory_nest_ownership"] = "ALWAYS_NEUTRAL_AT_START"
			};
		}

		void SelectAnchors(RmgGenerationSettings settings, RmgProfile profile)
		{
			var rules = profile.ColonyCombatRules; var count = size <= 128 ? 1 : size == 256 ? 3 : 5;
			var candidates = new List<RmgPoint>();
			for (var y = 8; y <= size - 13; y += 3)
				for (var x = 8; x <= size - 13; x += 3) candidates.Add(new RmgPoint(x, y));
			for (var attempt = 0; attempt < 32; attempt++)
			{
				Starts.Clear(); CoreNests.Clear();
				var order = candidates.OrderBy(p => TerrainComparison.Mix(settings.Seed, (ulong)(2520 + p.Y * size + p.X + attempt * size * size))).ToArray();
				CoreNests.Add(size == 64 ? new RmgPoint(size / 2 - 3, size / 2 - 3) : order[0]);
				for (var n = 1; n < count; n++) CoreNests.Add(order.OrderByDescending(p => CoreNests.Min(q => Distance(p, q))).First());
				for (var n = 0; n < settings.PlayerCount; n++)
				{
					var possible = order.Where(p => CoreNests.All(q => rules.ColonyStartMarginAtNative("wasps_colony", q, p) >= 0) && Starts.All(q => rules.StartMarginAtNative(p, q) >= 0));
					var next = possible.OrderByDescending(p => Starts.Select(q => Distance(p, q)).DefaultIfEmpty(CoreNests.Min(q => Distance(p, q))).Min() * (size == 64 ? 1D : .18 + TerrainComparison.Mix(settings.Seed, (ulong)(2590 + p.Y * size + p.X)) % 1000 / 1000D)).Take(1).ToArray();
					if (next.Length == 0) break; Starts.Add(next[0]);
				}

				if (Starts.Count == settings.PlayerCount) return;
			}

			throw new RmgGenerationRejectedException("CHAOS_START_CAPACITY", "Unable to fit the requested starts and independent flying access.");
		}

		public void Disk(double x, double y, double radius, bool clear)
		{
			for (var py = Math.Max(0, (int)(y - radius)); py <= Math.Min(size - 1, y + radius); py++)
				for (var px = Math.Max(0, (int)(x - radius)); px <= Math.Min(size - 1, x + radius); px++)
					if ((px - x) * (px - x) + (py - y) * (py - y) <= radius * radius) { Land[py * size + px] = true; if (clear) Clear[py * size + px] = true; }
		}

		void Passage(RmgPoint a, RmgPoint b)
		{
			var length = Math.Sqrt(Distance(a, b));
			if (length == 0) { Disk(a.X, a.Y, 3.5, false); return; }
			for (var i = 0; i <= length; i++) Disk(a.X + (b.X - a.X) * i / length, a.Y + (b.Y - a.Y) * i / length, 3.5, false);
		}

		static long Distance(RmgPoint a, RmgPoint b) => (long)(a.X - b.X) * (a.X - b.X) + (long)(a.Y - b.Y) * (a.Y - b.Y);
	}
}
