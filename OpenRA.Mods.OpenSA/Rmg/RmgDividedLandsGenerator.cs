#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Diagnostics;
using System.Linq;
using Newtonsoft.Json.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.OpenSA.Rmg.Reassessment;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public static partial class RmgGenerator
	{
		static RmgGenerationResult GenerateDividedLands(RmgProfile profile, RmgGenerationSettings settings)
		{
			var plan = PlanDividedLands(profile, settings);
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
					throw new InvalidOperationException("Divided Lands changed a protected home area or crossing.");
			}

			plan.Report["topology"] = DividedLandsTopology.Validate(TerrainComparison.NativeBytes(terrain.Map), settings, plan.Starts);
			var result = CompleteRegionsWithPlan(profile, settings, terrain, null, plan);
			var report = result.Map.RegionsReport;
			report["experiment_id"] = "divided-lands-v21"; report["identity"] = settings.Canonical(profile);
			report["accessibility_requirement"] = "CONNECTED_HOME_TERRITORIES";
			var metrics = terrain.Report["metrics"];
			if ((double)metrics["water_percent_map"] > terrainSettings.WaterPercent + 2)
				result.Validation.Warnings.Add(new RmgValidationIssue("DIVIDED_LANDS_WATER_FLOOR", "Continuous channels set minimum water coverage."));
			if ((double)metrics["water_percent_map"] < terrainSettings.WaterPercent - 2 ||
				(double)metrics["gravel_percent_land"] < terrainSettings.GravelPercent - 2 || (double)metrics["moss_percent_land"] < terrainSettings.MossPercent - 2)
				result.Validation.Warnings.Add(new RmgValidationIssue("DIVIDED_LANDS_TERRAIN_CAPACITY", "Home areas, crossings and native transitions limit terrain coverage."));
			return result;
		}

		static BattlefieldPlan PlanDividedLands(RmgProfile profile, RmgGenerationSettings settings)
		{
			var timer = Stopwatch.StartNew(); var size = settings.MapSize; var geometry = new DividedLandsGeometry(settings);
			var center = geometry.Center; var margin = size == 64 ? 10 : 16;
			var anchor = settings.PlayerCount switch
			{
				2 => (settings.Seed & 1) == 0 ? new RmgPoint(margin, size / 2 - 1) : new RmgPoint(size / 2 - 1, margin),
				4 => new RmgPoint(margin, margin),
				_ => new RmgPoint(margin, (int)Math.Round(center - (center - margin) * (Math.Sqrt(2) - 1)))
			};
			var starts = RmgMirroring.Points(anchor, size, settings.MirroringAxes, settings.Seed).ToList();
			var clear = new bool[size * size]; var land = new bool[clear.Length];
			for (var i = 0; i < clear.Length; i++)
			{
				var x = i % size; var y = i / size; var distance = geometry.Distance(x, y);
				clear[i] = geometry.Gate(x, y, geometry.Gates, geometry.Width / 2D) || starts.Any(p => RegionDistanceSquared(p, new RmgPoint(x, y)) <= 81);
				land[i] = distance >= geometry.MaxChannelHalf || clear[i];
			}

			var shoreDistances = geometry.PotentialWaterDistances();
			var blank = new RmgLogicalMap(size / 2, size / 2);
			Array.Fill(blank.NativeTerrainIntents, RmgNativeTerrainIntent.Clear);
			var footprints = profile.NeutralColonyActors.ToDictionary(type => type, type => Game.ModData.DefaultRules.Actors[type]
				.TraitInfos<BuildingInfo>().SelectMany(b => b.OccupiedTiles(CPos.Zero)).ToArray());
			bool ShoreFits(string type, RmgPoint anchor) => type == null || footprints[type].All(offset =>
				anchor.X + offset.X >= 0 && anchor.Y + offset.Y >= 0 && anchor.X + offset.X < size && anchor.Y + offset.Y < size &&
				shoreDistances[(anchor.Y + offset.Y) * size + anchor.X + offset.X] >= 3);
			var sites = new RegionsSites(Game.ModData, blank, true, ShoreFits);
			if (!sites.NativeOrbitFits(null, starts) || starts.SelectMany((p, i) => starts.Skip(i + 1).Select(q => profile.ColonyCombatRules.StartMarginAtNative(p, q))).Any(m => m < 0))
				throw new RmgGenerationRejectedException("DIVIDED_LANDS_STARTS", "This size cannot fit safe Divided Lands starting sites.");
			sites.ReserveNativeOrbit(null, starts);
			var startingCells = sites.ReservedCells.ToArray();
			for (var y = 0; y < size; y++)
				for (var x = 0; x < size; x++)
					blank.NativeTerrainIntents[4 * (y / 2 * (size / 2) + x / 2) + 2 * (y % 2) + x % 2] =
						shoreDistances[y * size + x] > 0 ? RmgNativeTerrainIntent.Clear : RmgNativeTerrainIntent.Water;
			var candidates = Enumerable.Range(0, size * size)
				.Where(i => RmgMirroring.Canonical(i, size, settings.MirroringAxes, settings.Seed) == i)
				.Select(i => new RmgPoint(i % size, i / size))
				.Where(p => p.X >= 8 && p.Y >= 8 && p.X < size - 8 && p.Y < size - 8 &&
					(shoreDistances[p.Y * size + p.X] <= 10 || ((size / 2 - 1 - Math.Min(p.X, size - 1 - p.X)) % 3 == 0 && (size / 2 - 1 - Math.Min(p.Y, size - 1 - p.Y)) % 3 == 0)))
				.Select(p => RmgMirroring.Points(p, size, settings.MirroringAxes, settings.Seed)).Where(o => o.Length == settings.PlayerCount)
				.OrderBy(o => shoreDistances[o[0].Y * size + o[0].X] / 4)
				.ThenBy(o => TerrainComparison.Mix(settings.Seed, (ulong)(2101 + o[0].Y * size + o[0].X))).ToArray();
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
				["actor_planning_ms"] = timer.Elapsed.TotalMilliseconds, ["territories"] = starts.Count,
				["land_crossings"] = RmgDividedLandsParameters.Name(settings.LandCrossings), ["crossing_width_native"] = geometry.Width,
				["crossing_positions_native"] = new JArray(geometry.Gates), ["divider_angles"] = new JArray(geometry.Angles),
				["maximum_channel_half_width_native"] = geometry.MaxChannelHalf, ["planned_starts"] = new JArray(starts.Select(p => p.ToString())),
				["planned_colony_sites"] = blank.Actors.Count, ["density_changes_water"] = false,
				["minimum_shore_clearance_native"] = 2, ["placement_order"] = "SHORE_BANDS_THEN_SEEDED_HOME_SITES", ["fairness_scope"] = "EQUAL_TYPED_HOME_OPPORTUNITIES_AND_TERRAIN_COSTS_BEFORE_OWNERSHIP"
			};
			return new BattlefieldPlan(starts, blank.Actors.ToArray(), strict, evaluations, types, clear, land, DividedLandsFields(settings, geometry, clear, land), report);
		}

		static TerrainComparisonFields DividedLandsFields(RmgGenerationSettings settings, DividedLandsGeometry geometry, bool[] clear, bool[] land)
		{
			var size = settings.MapSize; var depth = BattlefieldDepth(settings.TerrainComplexity);
			var phase = geometry.Phase;
			var width = size / 2; var lattice = width + 1;
			var water = new double[width * width]; var waterAllowed = new bool[water.Length]; var required = new bool[water.Length];
			var geology = new double[lattice * lattice]; var moisture = new double[geology.Length]; var landAllowed = new bool[geology.Length];
			for (var i = 0; i < water.Length; i++)
			{
				var canonical = RmgMirroring.Canonical(i, width, settings.MirroringAxes, settings.Seed);
				var x = 2 * (canonical % width) + .5; var y = 2 * (canonical / width) + .5;
				var distance = geometry.Distance(x, y); var variation = geometry.Variation(x, y, depth);
				water[i] = -distance / variation;
				waterAllowed[i] = distance <= geometry.MaxChannelHalf * variation &&
					!Enumerable.Range(0, 4).Any(f => land[(2 * (i / width) + f / 2) * size + 2 * (i % width) + f % 2]);
				required[i] = waterAllowed[i] && distance <= (size == 64 || (size == 128 && settings.PlayerCount == 8) ? 3 : 5);
				if (required[i]) water[i] += 10000;
			}

			for (var i = 0; i < required.Length; i++)
				if (!RmgMirroring.Orbit(i, width, settings.MirroringAxes, settings.Seed).All(m => waterAllowed[m])) required[i] = false;
			for (var i = 0; i < geology.Length; i++)
			{
				var canonical = RmgMirroring.Canonical(i, lattice, settings.MirroringAxes, settings.Seed);
				var x = 2 * (canonical % lattice) - .5; var y = 2 * (canonical / lattice) - .5;
				var distance = geometry.Distance(x, y); var u = geometry.Along(x, y);
				var patches = Math.Cos(u / size * Math.PI * (3 + depth * 2) + phase) + .7 * Math.Cos(distance / size * Math.PI * (4 + depth * 3) + phase);
				geology[i] = -distance / geometry.MaxChannelHalf + patches;
				moisture[i] = -distance / geometry.MaxChannelHalf - patches;
				landAllowed[i] = true;
				for (var py = Math.Max(0, 2 * (i / lattice) - 2); py <= Math.Min(size - 1, 2 * (i / lattice) + 2); py++)
					for (var px = Math.Max(0, 2 * (i % lattice) - 2); px <= Math.Min(size - 1, 2 * (i % lattice) + 2); px++)
						if (clear[py * size + px]) landAllowed[i] = false;
			}

			return new TerrainComparisonFields(water, geology, moisture, waterAllowed, landAllowed) { RequiredWater = required };
		}
	}
}
