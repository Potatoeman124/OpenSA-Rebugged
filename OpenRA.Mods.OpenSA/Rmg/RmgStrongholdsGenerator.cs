#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Newtonsoft.Json.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.OpenSA.Rmg.Reassessment;

namespace OpenRA.Mods.OpenSA.Rmg
{
	// Fort outlines, entrances and candidate areas are seed-fixed. Detail never relocates a fort.
	sealed class Stronghold
	{
		public readonly RmgPoint Center;
		public readonly double Radius, Facing, Phase;
		public readonly bool Castle;
		public Stronghold(RmgPoint center, double radius, double facing, double phase, bool castle)
		{
			Center = center; Radius = radius; Facing = facing; Phase = phase; Castle = castle;
		}

		public double Front(double x, double y) => (x - Center.X) * Math.Cos(Facing) + (y - Center.Y) * Math.Sin(Facing);
		public double Side(double x, double y) => -(x - Center.X) * Math.Sin(Facing) + (y - Center.Y) * Math.Cos(Facing);
		public double Distance(double x, double y) => Math.Sqrt((x - Center.X) * (x - Center.X) + (y - Center.Y) * (y - Center.Y));
		public double Boundary(double x, double y)
		{
			var angle = Math.Atan2(y - Center.Y, x - Center.X) - Facing;
			var c = Math.Abs(Math.Cos(angle)); var s = Math.Abs(Math.Sin(angle));
			return Radius * (.92 + .035 * Math.Cos(3 * angle + Phase)) / Math.Max(Math.Max(c, s), .72 * (c + s));
		}

		public double Edge(double x, double y) => Distance(x, y) - Boundary(x, y);
		public RmgPoint Point(double front, double side) => new((int)Math.Round(Center.X + front * Math.Cos(Facing) - side * Math.Sin(Facing)),
			(int)Math.Round(Center.Y + front * Math.Sin(Facing) + side * Math.Cos(Facing)));
		public JObject ToJson() => new()
		{
			["center"] = new JArray(Center.X, Center.Y), ["radius_native"] = Radius,
			["facing_radians"] = Facing, ["phase"] = Phase, ["castle"] = Castle
		};
	}

	sealed class StrongholdsGeometry
	{
		public readonly List<Stronghold> Forts = new();
		public readonly List<RmgPoint> Starts = new();
		public readonly List<(RmgPoint A, RmgPoint B)> Roads = new();
		public readonly int Size;
		public readonly double GateHalf;
		readonly ulong seed;
		public StrongholdsGeometry(RmgGenerationSettings settings, RmgProfile profile)
		{
			Size = settings.MapSize; seed = settings.Seed;
			GateHalf = Size == 64 ? 3 : Size == 128 ? 4 : 5;
			var random = new DeterministicRandom(TerrainComparison.Mix(seed, 2200));
			double Next() => random.NextInt(10000) / 10000D;
			var hub = new RmgPoint((int)(Size * (.45 + .1 * Next())), (int)(Size * (.45 + .1 * Next())));
			var phase = Next() * Math.PI * 2;
			for (var i = 0; i < settings.PlayerCount; i++)
			{
				RmgPoint center;
				if (Size == 64)
				{
					var corners = new[] { new RmgPoint(10, 10), new RmgPoint(51, 50), new RmgPoint(50, 10), new RmgPoint(10, 50) };
					center = corners[(i + (int)(seed % 4)) % 4];
					center = new RmgPoint(center.X + random.NextInt(3) - 1, center.Y + random.NextInt(3) - 1);
				}
				else
				{
					var angle = phase + (i + .13 * (Next() - .5)) * 2 * Math.PI / settings.PlayerCount;
					var radius = Size * (.36 + .035 * Next());
					center = new RmgPoint((int)(Size / 2D + radius * Math.Cos(angle)), (int)(Size / 2D + radius * Math.Sin(angle)));
				}

				Starts.Add(center);
			}

			if (Starts.SelectMany((p, i) => Starts.Skip(i + 1).Select(q => profile.ColonyCombatRules.StartMarginAtNative(p, q))).Any(m => m < 0))
				throw new RmgGenerationRejectedException("STRONGHOLDS_STARTS", "This seed cannot fit safe stronghold starts at this size.");
			foreach (var center in Starts)
			{
				var separation = Starts.Where(p => p != center).Select(p => Math.Sqrt((p.X - center.X) * (p.X - center.X) + (p.Y - center.Y) * (p.Y - center.Y))).DefaultIfEmpty(Size).Min();
				var radius = Math.Max(10, Math.Min(Size * .30 / Math.Sqrt(settings.PlayerCount), separation * .40)) * (.96 + .08 * Next());
				Forts.Add(new Stronghold(center, radius, Math.Atan2(hub.Y - center.Y, hub.X - center.X), Next() * Math.PI * 2, false));
			}

			// Optional castles use a separate stream and cannot move the player strongholds.
			if (settings.GenerateCastles)
			{
				var desired = Size == 512 ? 7 : Size == 256 ? 4 : 1;
				var radius = Size == 512 ? 27 : Size == 256 ? 20 : Size == 128 ? 12 : 9;
				var candidates = Enumerable.Range(0, Size / 8 * (Size / 8)).Select(i => new RmgPoint(i % (Size / 8) * 8 + 4, i / (Size / 8) * 8 + 4))
					.Where(p => p.X > radius + 5 && p.Y > radius + 5 && p.X < Size - radius - 5 && p.Y < Size - radius - 5)
					.OrderBy(p => TerrainComparison.Mix(seed, (ulong)(2220 + p.Y * Size + p.X))).ToArray();
				foreach (var p in candidates)
				{
					if (Forts.Count - Starts.Count >= desired) break;
					if (Forts.Any(f => f.Distance(p.X, p.Y) < f.Radius + radius + (Size == 64 ? 3 : 10))) continue;
					var salt = TerrainComparison.Mix(seed, (ulong)(2240 + p.Y * Size + p.X));
					Forts.Add(new Stronghold(p, radius * (.94 + salt % 100 / 800D), Math.Atan2(hub.Y - p.Y, hub.X - p.X) + .4, salt % 1024 / 1024D * Math.PI * 2, true));
				}
			}

			foreach (var fort in Forts)
			{
				var entrance = fort.Point(fort.Radius + 12, 0);
				Roads.Add((fort.Center, entrance));
				Roads.Add((entrance, hub));
			}
		}

		public static double SegmentDistance(double x, double y, RmgPoint a, RmgPoint b)
		{
			var dx = b.X - a.X; var dy = b.Y - a.Y;
			var t = Math.Clamp(((x - a.X) * dx + (y - a.Y) * dy) / Math.Max(1, (double)dx * dx + (double)dy * dy), 0, 1);
			return Math.Sqrt(Math.Pow(x - a.X - t * dx, 2) + Math.Pow(y - a.Y - t * dy, 2));
		}

		public double RoadDistance(double x, double y) => Roads.Min(r => SegmentDistance(x, y, r.A, r.B));
		public bool Land(double x, double y) => Forts.Any(f => f.Edge(x, y) <= 0) || RoadDistance(x, y) <= GateHalf + 2;
		public bool Clear(double x, double y) => Starts.Any(p => Math.Max(Math.Abs(x - p.X), Math.Abs(y - p.Y)) <= 7) ||
			Forts.Any(f => f.Edge(x, y) < -8 && SegmentDistance(x, y, f.Center, f.Point(f.Radius, 0)) < 2);
		public double Water(double x, double y, int depth)
		{
			var nearest = Forts.Min(f => Math.Abs(f.Edge(x, y) - (4 + depth * .75 * (1 + Math.Cos(Math.Atan2(y - f.Center.Y, x - f.Center.X) * 6 + f.Phase)))));
			var background = Math.Sin(x / 29 + seed % 13) + Math.Cos(y / 37 + seed % 17) + .4 * Math.Sin((x + y) / 19);
			return 6 - nearest / 6 + background * (.1 + .12 * depth);
		}

		public (double Geology, double Moisture) Surface(double x, double y, int depth)
		{
			var fort = Forts.OrderBy(f => Math.Abs(f.Edge(x, y))).First();
			var edge = fort.Edge(x, y);
			var angle = Math.Atan2(y - fort.Center.Y, x - fort.Center.X);
			var crenels = Math.Cos(angle * (4 + depth * 2) + fort.Phase);
			var belt = -Math.Min(Math.Abs(edge + 4), Math.Abs(edge - 15)) / 7;
			var patch = Math.Sin(x / 13 + seed % 23) + Math.Cos(y / 17 + seed % 19);
			return (belt + .3 * depth * crenels + .35 * patch, edge > -8 ? 4 + crenels : -3 + patch);
		}
	}

	public static partial class RmgGenerator
	{
		static RmgGenerationResult GenerateStrongholds(RmgProfile profile, RmgGenerationSettings settings)
		{
			var geometry = new StrongholdsGeometry(settings, profile);
			var plan = PlanStrongholds(profile, settings, geometry);
			var terrainSettings = new TerrainComparisonSettings(settings.Seed, settings.MapSize, TerrainConstruction.Regions, settings.TerrainComplexity)
			{
				Continuity = true, ExtendedComplexity = true, WaterPercent = RegionsWaterPercent(settings.WaterAmount),
				GravelPercent = profile.RockLandPercentFor(settings.TacticalTerrain), MossPercent = profile.VegetationLandPercentFor(settings.TacticalTerrain),
				OriginalSurfaceRelations = settings.OriginalSurfaceRelations
			};
			var terrain = TerrainComparison.Generate(Game.ModData, terrainSettings, fields: plan.Fields);
			for (var i = 0; i < plan.Clear.Length; i++)
			{
				var cell = TerrainComparison.Native(terrain.Map, i % settings.MapSize, i / settings.MapSize);
				if ((plan.Clear[i] && cell != RmgNativeTerrainIntent.Clear) || (plan.Land[i] && cell == RmgNativeTerrainIntent.Water))
					throw new InvalidOperationException("Strongholds changed a protected site or entrance.");
			}

			var result = CompleteRegionsWithPlan(profile, settings, terrain, null, plan);
			result.Map.RegionsReport["experiment_id"] = "strongholds-v22";
			result.Map.RegionsReport["identity"] = settings.Canonical(profile);
			result.Map.RegionsReport["accessibility_requirement"] = "STARTS_AND_COLONIES_CONNECTED";
			var metrics = terrain.Report["metrics"];
			if ((double)metrics["water_percent_map"] < terrainSettings.WaterPercent - 2 ||
				(double)metrics["gravel_percent_land"] < terrainSettings.GravelPercent - 2 || (double)metrics["moss_percent_land"] < terrainSettings.MossPercent - 2)
				result.Validation.Warnings.Add(new RmgValidationIssue("STRONGHOLDS_TERRAIN_CAPACITY", "Fort interiors, entrances and native transitions limit terrain coverage."));
			return result;
		}

		static BattlefieldPlan PlanStrongholds(RmgProfile profile, RmgGenerationSettings settings, StrongholdsGeometry geometry)
		{
			var timer = Stopwatch.StartNew(); var size = settings.MapSize; var width = size / 2; var lattice = width + 1;
			var clear = new bool[size * size]; var land = new bool[clear.Length];
			for (var i = 0; i < clear.Length; i++)
			{
				clear[i] = geometry.Clear(i % size, i / size);
				land[i] = geometry.Land(i % size, i / size) || clear[i];
			}

			var water = new double[width * width]; var waterAllowed = new bool[water.Length]; var potentialWater = new bool[clear.Length];
			for (var i = 0; i < water.Length; i++)
			{
				var x = 2 * (i % width); var y = 2 * (i / width);
				water[i] = geometry.Water(x + .5, y + .5, BattlefieldDepth(settings.TerrainComplexity));
				waterAllowed[i] = !Enumerable.Range(0, 4).Any(f => land[(y + f / 2) * size + x + f % 2]);
				if (waterAllowed[i]) for (var f = 0; f < 4; f++) potentialWater[(y + f / 2) * size + x + f % 2] = true;
			}

			var shore = DividedLandsGeometry.WaterDistances(potentialWater, size);
			var blank = new RmgLogicalMap(width, width);
			for (var i = 0; i < clear.Length; i++) blank.NativeTerrainIntents[4 * (i / size / 2 * width + i % size / 2) + 2 * (i / size % 2) + i % 2] =
				potentialWater[i] ? RmgNativeTerrainIntent.Water : RmgNativeTerrainIntent.Clear;
			var footprints = profile.NeutralColonyActors.ToDictionary(t => t, t => Game.ModData.DefaultRules.Actors[t].TraitInfos<BuildingInfo>()
				.SelectMany(b => b.OccupiedTiles(CPos.Zero)).ToArray());
			bool FitsShore(string type, RmgPoint p) => type == null || footprints[type].All(o => p.X + o.X >= 0 && p.Y + o.Y >= 0 && p.X + o.X < size && p.Y + o.Y < size &&
				shore[(p.Y + o.Y) * size + p.X + o.X] >= 3 && geometry.RoadDistance(p.X + o.X, p.Y + o.Y) > 2);
			var sites = new RegionsSites(Game.ModData, blank, true, FitsShore);
			if (!sites.NativeOrbitFits(null, geometry.Starts)) throw new RmgGenerationRejectedException("STRONGHOLDS_START_SITES", "A planned stronghold start does not fit.");
			sites.ReserveNativeOrbit(null, geometry.Starts);
			var startCells = sites.ReservedCells.ToArray();
			var candidates = Enumerable.Range(0, size * size).Where(i => i % size % 2 == 0 && i / size % 2 == 0)
				.Select(i => new RmgPoint(i % size, i / size)).Where(p => p.X >= 2 && p.Y >= 2 && p.X < size - 8 && p.Y < size - 8 &&
					geometry.Forts.Any(f => f.Edge(p.X + 2.5, p.Y + 2.5) < -2)).ToArray();
			var random = new DeterministicRandom(TerrainComparison.Mix(settings.Seed, 2202));
			var drawn = Enumerable.Range(0, settings.EffectiveNeutralColonyCount).Select(_ => settings.NeutralColonyWeights.ActorForTicket(random.NextInt(settings.NeutralColonyWeights.Total))).ToArray();
			var colonies = new List<(string Type, RmgPoint Point, int Fort)>(); var missing = new List<string>();
			var rules = profile.ColonyCombatRules; long evaluations = 0;
			int FortAt(RmgPoint p) => Enumerable.Range(0, geometry.Forts.Count).OrderBy(i => geometry.Forts[i].Distance(p.X + 2.5, p.Y + 2.5) / geometry.Forts[i].Radius).First();
			var fortIds = candidates.ToDictionary(p => p, FortAt);
			double RoleScore(string type, RmgPoint p)
			{
				var fort = geometry.Forts[fortIds[p]]; var front = fort.Front(p.X + 2.5, p.Y + 2.5) / fort.Radius;
				var side = Math.Abs(fort.Side(p.X + 2.5, p.Y + 2.5)) / fort.Radius;
				var frontline = type is "ants_colony" or "beetles_colony";
				var range = rules.AttackRangeNative(type);

				// Species roles remain stable whether starting combat clearance is enabled or disabled.
				// Keep support behind the production line by its weapon range plus a small buffer.
				var targetFront = frontline ? .65 : Math.Clamp(.65 - (range + 5) / fort.Radius, .05, .60);
				var targetSide = .60;
				var role = type == "wasps_colony" ? Math.Abs(front + .60) : Math.Abs(front - targetFront) + (frontline ? 0 : .35 * Math.Abs(side - targetSide));
				return role + TerrainComparison.Mix(settings.Seed, (ulong)(p.Y * size + p.X + 2260)) % 1000 / 8000D;
			}

			var ordered = drawn.Distinct().ToDictionary(t => t, t => candidates.OrderBy(p => RoleScore(t, p)).ToArray());
			bool Fits(string type, RmgPoint p) => sites.NativeOrbitFits(type, new[] { p }) && (!settings.RespectStartingSafeArea || geometry.Starts.All(s => rules.ColonyStartMarginAtNative(type, p, s) >= 0));
			void Place(string type, RmgPoint p)
			{
				blank.Actors.Add(RmgMirroring.Actor(type, profile.ColonyOwner, "neutral-colony", p, settings.PlayerCount + colonies.Count) with { StrongholdSpawn = fortIds[p] < settings.PlayerCount ? fortIds[p] + 1 : 0 });
				colonies.Add((type, p, fortIds[p])); sites.ReserveNativeOrbit(type, new[] { p });
			}

			var groups = ordered.ToDictionary(kv => kv.Key, kv => Enumerable.Range(0, geometry.Forts.Count)
				.Select(f => kv.Value.Where(p => fortIds[p] == f).ToArray()).ToArray());
			var cursors = drawn.Distinct().ToDictionary(t => t, _ => new int[geometry.Forts.Count]);
			foreach (var type in drawn)
			{
				// A rejected site never becomes valid as more colonies are added. Keep per-type
				// cursors so high density does not repeatedly rescore the entire map.
				var fortOrder = Enumerable.Range(0, geometry.Forts.Count).OrderBy(f => colonies.Count(c => c.Fort == f) / (geometry.Forts[f].Castle ? .5 : 1));
				var placed = false;
				foreach (var f in fortOrder)
				{
					var pool = groups[type][f];
					while (cursors[type][f] < pool.Length)
					{
						var p = pool[cursors[type][f]++];
						if (!Fits(type, p) || colonies.Any(c => rules.ColonyMarginAtNative(type, p, c.Type, c.Point) < 0)) continue;
						Place(type, p); placed = true; break;
					}

					if (placed) break;
				}

				if (!placed) missing.Add(type);
			}

			var strict = colonies.Count;
			if (!settings.PreventColonyOverlapping)
			{
				var queues = new Dictionary<string, PriorityQueue<MirroredColonyCandidate, (int, long, int, int)>>();
				foreach (var type in missing.Distinct())
				{
					var queue = new PriorityQueue<MirroredColonyCandidate, (int, long, int, int)>(); queues.Add(type, queue);
					for (var i = 0; i < ordered[type].Length; i++)
					{
						var p = ordered[type][i]; if (!Fits(type, p)) continue;
						var candidate = new MirroredColonyCandidate(new[] { p }, i); queue.Enqueue(candidate, candidate.Priority);
					}
				}

				foreach (var type in missing)
					while (queues[type].TryDequeue(out var candidate, out var previous))
					{
						var p = candidate.Points[0]; if (!Fits(type, p)) continue;
						for (; candidate.Scored < colonies.Count; candidate.Scored++)
						{
							var c = colonies[candidate.Scored]; candidate.Score(rules.ColonyMarginAtNative(type, p, c.Type, c.Point)); evaluations++;
						}

						if (candidate.Priority != previous) { queues[type].Enqueue(candidate, candidate.Priority); continue; }
						Place(type, p); break;
					}
			}

			foreach (var p in settings.OriginalSurfaceRelations ? sites.ReservedCells : startCells) clear[p.Y * size + p.X] = true;
			var geology = new double[lattice * lattice]; var moisture = new double[geology.Length]; var landAllowed = new bool[geology.Length];
			for (var i = 0; i < geology.Length; i++)
			{
				var x = 2 * (i % lattice); var y = 2 * (i / lattice);
				(geology[i], moisture[i]) = geometry.Surface(x - .5, y - .5, BattlefieldDepth(settings.TerrainComplexity));
				landAllowed[i] = true;
				for (var py = Math.Max(0, y - 2); py <= Math.Min(size - 1, y + 2); py++)
					for (var px = Math.Max(0, x - 2); px <= Math.Min(size - 1, x + 2); px++) if (clear[py * size + px]) landAllowed[i] = false;
			}

			var report = new JObject
			{
				["actor_planning_ms"] = timer.Elapsed.TotalMilliseconds, ["generate_castles"] = settings.GenerateCastles,
				["strongholds"] = settings.PlayerCount, ["castles"] = geometry.Forts.Count(f => f.Castle),
				["forts"] = new JArray(geometry.Forts.Select(f => f.ToJson())), ["minimum_shore_clearance_native"] = 2,
				["density_changes_water"] = false, ["fairness_scope"] = "ASYMMETRIC_GEOGRAPHY_NO_PARITY_REQUIREMENT",
				["colony_roles"] = new JArray(colonies.Select(c => new JObject
				{
					["type"] = c.Type, ["position"] = new JArray(c.Point.X, c.Point.Y),
					["fort"] = c.Fort, ["front_fraction"] = geometry.Forts[c.Fort].Front(c.Point.X + 2.5, c.Point.Y + 2.5) / geometry.Forts[c.Fort].Radius
				})),
				["colony_turret_ranges_native"] = new JObject(profile.NeutralColonyActors.Select(t => new JProperty(t, rules.AttackRangeNative(t)))),
				["gate_half_width_native"] = geometry.GateHalf, ["roads"] = new JArray(geometry.Roads.Select(r => new JArray(r.A.X, r.A.Y, r.B.X, r.B.Y)))
			};
			return new BattlefieldPlan(geometry.Starts, blank.Actors.ToArray(), strict, evaluations, drawn, clear, land,
				new TerrainComparisonFields(water, geology, moisture, waterAllowed, landAllowed), report);
		}
	}
}
