#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Diagnostics;
using System.Linq;
using Newtonsoft.Json.Linq;
using OpenRA.Mods.OpenSA.Rmg.Reassessment;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public static partial class RmgGenerator
	{
		static RmgGenerationResult GenerateRing(RmgProfile profile, RmgGenerationSettings settings)
		{
			var plan = PlanRing(profile, settings);
			var terrainSettings = new TerrainComparisonSettings(settings.Seed, settings.MapSize, TerrainConstruction.Regions, settings.TerrainComplexity)
			{
				Continuity = true, ExtendedComplexity = true, MirroringAxes = settings.MirroringAxes,
				WaterPercent = RegionsWaterPercent(settings.WaterAmount), GravelPercent = profile.RockLandPercentFor(settings.TacticalTerrain),
				MossPercent = profile.VegetationLandPercentFor(settings.TacticalTerrain), OriginalSurfaceRelations = settings.OriginalSurfaceRelations
			};
			var terrain = TerrainComparison.Generate(Game.ModData, terrainSettings, fields: plan.Fields);
			for (var i = 0; i < plan.Clear.Length; i++)
			{
				var cell = TerrainComparison.Native(terrain.Map, i % settings.MapSize, i / settings.MapSize);
				if ((plan.Clear[i] && cell != RmgNativeTerrainIntent.Clear) || (plan.Land[i] && cell == RmgNativeTerrainIntent.Water))
					throw new InvalidOperationException("Ring terrain changed its continuous route or reserved colony belt.");
			}

			plan.Report["topology"] = RingTopology.Validate(terrain.Map, settings, plan.Report);
			var result = CompleteRegionsWithPlan(profile, settings, terrain, null, plan);
			var report = result.Map.RegionsReport;
			report["experiment_id"] = "ring-v20"; report["identity"] = settings.Canonical(profile);
			report["accessibility_requirement"] = "STARTS_AND_COLONIES_CONNECTED";
			var metrics = terrain.Report["metrics"];
			if ((double)metrics["water_percent_map"] > terrainSettings.WaterPercent + 2)
				result.Validation.Warnings.Add(new RmgValidationIssue("RING_WATER_FLOOR", "The central lake sets minimum water coverage for this ring geometry."));
			if ((double)metrics["water_percent_map"] < terrainSettings.WaterPercent - 2 ||
				(double)metrics["gravel_percent_land"] < terrainSettings.GravelPercent - 2 || (double)metrics["moss_percent_land"] < terrainSettings.MossPercent - 2)
				result.Validation.Warnings.Add(new RmgValidationIssue("RING_TERRAIN_CAPACITY", "The continuous ring, colony sites and transitions limit terrain coverage."));
			return result;
		}

		static BattlefieldPlan PlanRing(RmgProfile profile, RmgGenerationSettings settings)
		{
			var timer = Stopwatch.StartNew();
			var size = settings.MapSize; var center = (size - 1) / 2D; var radius = size * .34;
			double Radial(double x, double y) => RmgRingParameters.Radius(x - center, y - center, settings.RingShape);
			var angle = settings.PlayerCount == 4 ? Math.PI / 4 : settings.PlayerCount == 8 ? Math.PI / 8 : 0;
			if (settings.PlayerCount == 2 && (settings.Seed & 1) != 0) angle = Math.PI / 2;
			var startRadius = size == 128 && settings.PlayerCount == 8 ? size * .42 : radius;
			var scale = startRadius / RmgRingParameters.Radius(Math.Cos(angle), Math.Sin(angle), settings.RingShape);
			var anchor = new RmgPoint((int)Math.Round(center - scale * Math.Cos(angle)), (int)Math.Round(center - scale * Math.Sin(angle)));
			var starts = RmgMirroring.Points(anchor, size, settings.MirroringAxes, settings.Seed).ToList();
			var siteHalf = Math.Clamp(size * .10, 12, 48);
			var widthExtension = settings.LaneWidth switch { RmgBattlefieldLaneWidth.Narrow => 0, RmgBattlefieldLaneWidth.Standard => .025, _ => .055 };
			var halfWidth = siteHalf + size * widthExtension;
			var clear = new bool[size * size]; var land = new bool[clear.Length]; var placement = new bool[clear.Length];
			for (var i = 0; i < clear.Length; i++)
			{
				var x = i % size; var y = i / size; var distance = Math.Abs(Radial(x, y) - radius);
				clear[i] = distance <= 2 || starts.Any(p => RegionDistanceSquared(p, new RmgPoint(x, y)) <= 81);
				placement[i] = distance <= siteHalf;
				land[i] = distance <= halfWidth || clear[i];
			}

			var blank = new RmgLogicalMap(size / 2, size / 2);
			Array.Fill(blank.NativeTerrainIntents, RmgNativeTerrainIntent.Clear);
			var sites = new RegionsSites(Game.ModData, blank, true);
			if (!sites.NativeOrbitFits(null, starts) || starts.SelectMany((p, i) => starts.Skip(i + 1).Select(q => profile.ColonyCombatRules.StartMarginAtNative(p, q))).Any(m => m < 0))
				throw new RmgGenerationRejectedException("RING_STARTS", "This size cannot fit safe Ring starting sites.");
			sites.ReserveNativeOrbit(null, starts);
			var startingCells = sites.ReservedCells.ToArray();
			for (var y = 0; y < size; y++)
				for (var x = 0; x < size; x++)
					blank.NativeTerrainIntents[4 * (y / 2 * (size / 2) + x / 2) + 2 * (y % 2) + x % 2] =
						placement[y * size + x] && (size < 256 || Math.Abs(Radial(x, y) - radius) > 3) ? RmgNativeTerrainIntent.Clear : RmgNativeTerrainIntent.Water;
			var candidates = Enumerable.Range(0, size * size)
				.Where(i => RmgMirroring.Canonical(i, size, settings.MirroringAxes, settings.Seed) == i)
				.Select(i => new RmgPoint(i % size, i / size))
				.Where(p => p.X >= 8 && p.Y >= 8 && p.X < size - 8 && p.Y < size - 8 &&
					(size / 2 - 1 - Math.Min(p.X, size - 1 - p.X)) % 3 == 0 && (size / 2 - 1 - Math.Min(p.Y, size - 1 - p.Y)) % 3 == 0)
				.Select(p => RmgMirroring.Points(p, size, settings.MirroringAxes, settings.Seed)).Where(o => o.Length == settings.PlayerCount)
				.OrderByDescending(o => starts.Min(p => RegionDistanceSquared(p, o[0])) / (size * size * .025))
				.ThenBy(o => TerrainComparison.Mix(settings.Seed, (ulong)(2001 + o[0].Y * size + o[0].X))).ToArray();
			PlaceMirroredColonies(blank, profile, settings, sites, candidates, starts, out var strict, out var evaluations, out var types);
			foreach (var p in settings.OriginalSurfaceRelations ? sites.ReservedCells : startingCells) clear[p.Y * size + p.X] = true;
			foreach (var mask in new[] { clear, land })
			{
				var original = (bool[])mask.Clone();
				for (var i = 0; i < original.Length; i++)
					if (original[i]) foreach (var member in RmgMirroring.Orbit(i, size, settings.MirroringAxes, settings.Seed)) mask[member] = true;
			}

			var report = new JObject
			{
				["actor_planning_ms"] = timer.Elapsed.TotalMilliseconds, ["ring_shape"] = RmgRingParameters.Name(settings.RingShape),
				["ring_radius_native"] = radius, ["ring_half_width_native"] = halfWidth, ["colony_belt_half_width_native"] = siteHalf,
				["planned_starts"] = new JArray(starts.Select(p => p.ToString())), ["planned_colony_sites"] = blank.Actors.Count,
				["protected_land_cells"] = land.Count(v => v), ["placement_order"] = "CONTESTED_RING_SHOULDERS_FIRST",
				["density_changes_water"] = false, ["fairness_scope"] = "EQUAL_TERRAIN_TRAVEL_COSTS_TO_TYPED_COLONY_POOLS_BEFORE_OWNERSHIP"
			};
			return new BattlefieldPlan(starts, blank.Actors.ToArray(), strict, evaluations, types, clear, land, RingFields(settings, radius, halfWidth, clear, land), report);
		}

		static TerrainComparisonFields RingFields(RmgGenerationSettings settings, double radius, double halfWidth, bool[] clear, bool[] land)
		{
			var size = settings.MapSize; var center = (size - 1) / 2D; var depth = BattlefieldDepth(settings.TerrainComplexity);
			var amplitude = Math.Min(size * (.003 + depth * .023), Math.Max(0, radius - halfWidth - 5));
			var frequency = 8 * (1 + depth);
			var phase = TerrainComparison.Mix(settings.Seed, 2010) % 1024 / 1024D * Math.PI;
			double Detail(double x, double y) => amplitude * (.5 + .5 * Math.Cos(Math.Atan2(y - center, x - center) * frequency + phase));
			double Priority(double x, double y)
			{
				var r = RmgRingParameters.Radius(x - center, y - center, settings.RingShape);
				var detail = Detail(x, y);
				return Math.Max(radius - halfWidth - detail - r, r - radius - halfWidth - detail);
			}

			var width = size / 2; var lattice = width + 1;
			var water = new double[width * width]; var waterAllowed = new bool[water.Length]; var required = new bool[water.Length];
			var geology = new double[lattice * lattice]; var moisture = new double[geology.Length]; var landAllowed = new bool[geology.Length];
			for (var i = 0; i < water.Length; i++)
			{
				var canonical = RmgMirroring.Canonical(i, width, settings.MirroringAxes, settings.Seed);
				var x = 2 * (canonical % width) + .5; var y = 2 * (canonical / width) + .5;
				water[i] = Priority(x, y);
				var radial = RmgRingParameters.Radius(x - center, y - center, settings.RingShape);
				var detail = Detail(x, y);

				// The shaped shoreline is a hard envelope: excess water cannot smooth away complexity.
				waterAllowed[i] = (radial < radius - halfWidth - detail || radial > radius + halfWidth + detail) &&
					!Enumerable.Range(0, 4).Any(f => land[(2 * (i / width) + f / 2) * size + 2 * (i % width) + f % 2]);
				required[i] = waterAllowed[i] && radial < radius - halfWidth - detail;
				if (required[i]) water[i] += 10000;
			}

			for (var i = 0; i < required.Length; i++)
				if (!RmgMirroring.Orbit(i, width, settings.MirroringAxes, settings.Seed).All(m => waterAllowed[m])) required[i] = false;
			for (var i = 0; i < geology.Length; i++)
			{
				var canonical = RmgMirroring.Canonical(i, lattice, settings.MirroringAxes, settings.Seed);
				var x = 2 * (canonical % lattice) - .5; var y = 2 * (canonical / lattice) - .5;
				geology[i] = Priority(x, y) + Detail(x, y) * .6; moisture[i] = Priority(x, y) - Detail(x, y) * .6;
				landAllowed[i] = true;
				for (var py = Math.Max(0, 2 * (i / lattice) - 2); py <= Math.Min(size - 1, 2 * (i / lattice) + 2); py++)
					for (var px = Math.Max(0, 2 * (i % lattice) - 2); px <= Math.Min(size - 1, 2 * (i % lattice) + 2); px++)
						if (clear[py * size + px]) landAllowed[i] = false;
			}

			return new TerrainComparisonFields(water, geology, moisture, waterAllowed, landAllowed) { RequiredWater = required };
		}
	}
}
