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
		static RmgGenerationResult GenerateArchipelago(RmgProfile profile, RmgGenerationSettings settings)
		{
			var geometry = ArchipelagoGeometry.Create(settings, profile);
			var size = settings.MapSize; var width = size / 2; var lattice = width + 1; var depth = (int)settings.TerrainComplexity;
			var geology = new double[lattice * lattice]; var moisture = new double[geology.Length]; var landAllowed = new bool[geology.Length];
			for (var i = 0; i < geology.Length; i++)
			{
				var x = 2 * (i % lattice); var y = 2 * (i / lattice);
				geology[i] = Math.Sin(x / 23D + settings.Seed % 23) + Math.Cos(y / 29D + settings.Seed % 19) + (.1 + .3 * depth) * Math.Sin(x / (13D - depth * 2)) * Math.Cos(y / (15D - depth * 2));
				moisture[i] = 3 * Math.Cos((x + y) / 21D + settings.Seed % 29) + Math.Sin(y / 11D);
				landAllowed[i] = true;
				for (var py = Math.Max(0, y - 2); py <= Math.Min(size - 1, y + 2); py++)
					for (var px = Math.Max(0, x - 2); px <= Math.Min(size - 1, x + 2); px++) if (geometry.Clear[py * size + px]) landAllowed[i] = false;
			}

			var fields = new TerrainComparisonFields(geometry.Sea.Select(v => v ? 1D : 0).ToArray(), geology, moisture, geometry.Sea, landAllowed) { RequiredWater = geometry.Sea };
			var terrainSettings = new TerrainComparisonSettings(settings.Seed, size, TerrainConstruction.Regions, settings.TerrainComplexity)
			{
				Continuity = true, ExtendedComplexity = true, OriginalSurfaceRelations = settings.OriginalSurfaceRelations, OceanOutside = true, WaterPercent = 100,
				GravelPercent = profile.RockLandPercentFor(settings.TacticalTerrain), MossPercent = profile.VegetationLandPercentFor(settings.TacticalTerrain)
			};
			TerrainComparisonResult terrain = null; int[] islands = null; var removed = 0;
			for (var attempt = 0; attempt < 8; attempt++)
			{
				terrain = TerrainComparison.Generate(Game.ModData, terrainSettings, fields: fields);
				islands = ArchipelagoGeometry.Components(TerrainComparison.NativeBytes(terrain.Map).Select(b => b != 1).ToArray(), size);
				var anchored = geometry.Centers.Select(p => islands[p.Y * size + p.X]).ToHashSet();
				var fragments = Enumerable.Range(0, islands.Length).Where(i => islands[i] >= 0 && !anchored.Contains(islands[i])).ToArray();
				if (fragments.Length == 0) break;

				// Shore normalization can create isolated dry slivers. Remove these during terrain
				// construction, before any actors exist; never leave an island without a nest.
				removed += fragments.Length;
				foreach (var i in fragments)
					for (var y = Math.Max(0, i / size / 2 - 1); y <= Math.Min(width - 1, i / size / 2 + 1); y++)
						for (var x = Math.Max(0, i % size / 2 - 1); x <= Math.Min(width - 1, i % size / 2 + 1); x++) geometry.Sea[y * width + x] = true;
			}

			var labels = islands.Where(i => i >= 0).Distinct().ToArray();
			if (labels.Length != geometry.Nests.Count || geometry.Centers.Select(p => islands[p.Y * size + p.X]).Distinct().Count() != labels.Length)
				throw new RmgGenerationRejectedException("ARCHIPELAGO_ISLANDS", $"Native shorelines did not preserve separate islands: {labels.Length} actual, {geometry.Nests.Count} planned, {geometry.Centers.Select(p => islands[p.Y * size + p.X]).Distinct().Count()} anchored.");
			var timer = Stopwatch.StartNew();
			var shore = DividedLandsGeometry.WaterDistances(islands.Select(i => i < 0).ToArray(), size);
			var footprints = profile.NeutralColonyActors.ToDictionary(t => t, t => Game.ModData.DefaultRules.Actors[t].TraitInfos<BuildingInfo>().SelectMany(b => b.OccupiedTiles(CPos.Zero)).ToArray());
			bool FitsShore(string type, RmgPoint p) => type == null || footprints[type].All(o => p.X + o.X >= 0 && p.Y + o.Y >= 0 && p.X + o.X < size && p.Y + o.Y < size && shore[(p.Y + o.Y) * size + p.X + o.X] >= 3);
			var sites = new RegionsSites(Game.ModData, terrain.Map, settings.OriginalSurfaceRelations, FitsShore);
			if (!sites.NativeOrbitFits(null, geometry.Starts)) throw new RmgGenerationRejectedException("ARCHIPELAGO_START_SITES", "An island starting site does not fit.");
			sites.ReserveNativeOrbit(null, geometry.Starts);
			var blank = new RmgLogicalMap(width, width);
			foreach (var nest in geometry.Nests)
			{
				if (!sites.NativeOrbitFits("wasps_colony", new[] { nest })) throw new RmgGenerationRejectedException("ARCHIPELAGO_NEST_SITE", "A mandatory Wasps nest does not fit its island.");
				blank.Actors.Add(RmgMirroring.Actor("wasps_colony", profile.ColonyOwner, "neutral-colony", nest, settings.PlayerCount + blank.Actors.Count) with { MandatoryNeutral = true });
				sites.ReserveNativeOrbit("wasps_colony", new[] { nest });
			}

			var random = new DeterministicRandom(TerrainComparison.Mix(settings.Seed, 2402));
			var candidates = Enumerable.Range(0, size * size).Where(i => islands[i] >= 0).Select(i => new[] { new RmgPoint(i % size, i / size) }).ToArray();
			for (var i = candidates.Length - 1; i > 0; i--) { var j = random.NextInt(i + 1); (candidates[i], candidates[j]) = (candidates[j], candidates[i]); }
			PlaceMirroredColonies(blank, profile, settings, sites, candidates, geometry.Starts, out var strict, out var evaluations, out var drawn);
			var report = new JObject
			{
				["actor_planning_ms"] = timer.Elapsed.TotalMilliseconds, ["island_amount"] = RmgArchipelagoParameters.Name(settings.IslandAmount),
				["island_size"] = RmgArchipelagoParameters.Name(settings.IslandSize), ["islands_requested"] = geometry.Nests.Count, ["islands_actual"] = labels.Length,
				["island_areas_native"] = new JArray(labels.Select(label => islands.Count(i => i == label))),
				["centers"] = new JArray(geometry.Centers.Select(p => new JArray(p.X, p.Y))),
				["mandatory_nests"] = new JArray(geometry.Nests.Select(p => new JArray(p.X, p.Y))),
				["removed_fragment_cells"] = removed, ["land_radius_fraction"] = geometry.LandRadiusFraction,
				["minimum_shore_clearance_native"] = 2, ["terrain_priority"] = "TERRAIN_BEFORE_COLONIES_NO_REPAINT",
				["mandatory_nest_ownership"] = "ALWAYS_NEUTRAL_AT_START"
			};
			var plan = new BattlefieldPlan(geometry.Starts, blank.Actors.ToArray(), strict, evaluations, drawn, geometry.Clear, geometry.Land, fields, report);
			var result = CompleteRegionsWithPlan(profile, settings, terrain, null, plan);
			var summary = result.Map.RegionsReport;
			summary["experiment_id"] = "archipelago-v24"; summary["identity"] = settings.Canonical(profile);
			summary["accessibility_requirement"] = "WASPS_ACCESS_ON_EVERY_ISLAND"; summary["strategic_routes_requirement"] = "DISCONNECTED_ISLANDS";
			summary["preferred_start_reference"] = "seeded-island-interiors";
			summary["neutral_colonies_requested"] = Math.Max(settings.EffectiveNeutralColonyCount, geometry.Nests.Count);
			summary["neutral_colonies_group_target"] = Math.Max(settings.EffectiveNeutralColonyCount, geometry.Nests.Count);
			summary["neutral_colonies_disabled"] = false; summary["mandatory_neutral_nests"] = geometry.Nests.Count;
			summary["water_requested_percent_map"] = null;
			if (geometry.Nests.Count > settings.EffectiveNeutralColonyCount) result.Validation.Warnings.Add(new RmgValidationIssue("ARCHIPELAGO_NEST_FLOOR", "One neutral Wasps nest per island overrides the colony target and Wasps weight."));
			return result;
		}
	}
}
