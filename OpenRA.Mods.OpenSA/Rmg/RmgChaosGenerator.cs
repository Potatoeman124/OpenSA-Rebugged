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
	public static partial class RmgGenerator
	{
		static RmgGenerationResult GenerateChaos(RmgProfile profile, RmgGenerationSettings settings)
		{
			var geometry = new ChaosGeometry(settings, profile);
			var size = settings.MapSize; var width = size / 2; var lattice = width + 1;
			var allowed = new bool[width * width]; var removedWater = new bool[allowed.Length]; var landAllowed = new bool[lattice * lattice];
			for (var i = 0; i < allowed.Length; i++)
			{
				var x = 2 * (i % width); var y = 2 * (i / width);
				allowed[i] = !Enumerable.Range(0, 4).Any(f => geometry.Land[(y + f / 2) * size + x + f % 2]);
			}

			void UpdateSurfaceReservations()
			{
				for (var i = 0; i < landAllowed.Length; i++)
				{
					var x = 2 * (i % lattice); var y = 2 * (i / lattice); landAllowed[i] = true;
					for (var py = Math.Max(0, y - 2); py <= Math.Min(size - 1, y + 2); py++)
						for (var px = Math.Max(0, x - 2); px <= Math.Min(size - 1, x + 2); px++) if (geometry.Clear[py * size + px]) landAllowed[i] = false;
				}
			}

			UpdateSurfaceReservations();
			var fields = new TerrainComparisonFields(geometry.Water, geometry.Geology, geometry.Moisture, allowed, landAllowed) { RequiredWater = removedWater };
			var waterTargets = new[] { 18, 33, 46, 58, 70 };
			var terrainSettings = new TerrainComparisonSettings(settings.Seed, size, TerrainConstruction.Regions, settings.TerrainComplexity)
			{
				Continuity = true, ExtendedComplexity = true, OriginalSurfaceRelations = settings.OriginalSurfaceRelations, OceanOutside = true,
				WaterPercent = waterTargets[(int)settings.WaterAmount],
				GravelPercent = Math.Min(43, profile.RockLandPercentFor(settings.TacticalTerrain) + 8),
				MossPercent = Math.Min(34, profile.VegetationLandPercentFor(settings.TacticalTerrain) + 6)
			};
			var footprints = profile.NeutralColonyActors.ToDictionary(t => t, t => Game.ModData.DefaultRules.Actors[t].TraitInfos<BuildingInfo>().SelectMany(b => b.OccupiedTiles(CPos.Zero)).ToArray());
			var spacingFootprints = footprints.ToDictionary(pair => pair.Key, pair => pair.Value.SelectMany(o =>
				Enumerable.Range(-2, 5).SelectMany(dy => Enumerable.Range(-2, 5).Select(dx => new RmgPoint(o.X + dx, o.Y + dy)))).Distinct().ToArray());
			TerrainComparisonResult terrain = null; int[] islands = null; int[] shore = null; var nests = new List<RmgPoint>(); var removed = 0;
			bool FitsShore(string type, RmgPoint p) => type == null || footprints[type].All(o => p.X + o.X >= 0 && p.Y + o.Y >= 0 && p.X + o.X < size && p.Y + o.Y < size && shore[(p.Y + o.Y) * size + p.X + o.X] >= 3);
			for (var attempt = 0; attempt < 16; attempt++)
			{
				terrain = TerrainComparison.Generate(Game.ModData, terrainSettings, fields: fields);
				islands = ArchipelagoGeometry.Components(TerrainComparison.NativeBytes(terrain.Map).Select(b => b != 1).ToArray(), size);
				shore = DividedLandsGeometry.WaterDistances(islands.Select(i => i < 0).ToArray(), size);
				var sites = new RegionsSites(Game.ModData, terrain.Map, false, FitsShore);
				if (!sites.NativeOrbitFits(null, geometry.Starts)) throw new RmgGenerationRejectedException("CHAOS_START_SITES", "A protected Chaos starting site does not fit.");
				sites.ReserveNativeOrbit(null, geometry.Starts); nests.Clear(); var fragments = new List<int>();
				foreach (var island in Enumerable.Range(0, islands.Length).Where(i => islands[i] >= 0).GroupBy(i => islands[i]))
				{
					var candidates = geometry.CoreNests.Where(p => islands[p.Y * size + p.X] == island.Key)
						.Concat(island.Where(i => shore[i] >= 6).OrderByDescending(i => shore[i]).ThenBy(i => TerrainComparison.Mix(settings.Seed, (ulong)(2530 + i)))
							.Select(i => new RmgPoint(i % size - 2, i / size - 2)));
					var chosen = candidates.Where(p => sites.NativeOrbitFits("wasps_colony", new[] { p }) &&
						geometry.Starts.All(q => profile.ColonyCombatRules.ColonyStartMarginAtNative("wasps_colony", p, q) >= 0) &&
						nests.All(q => profile.ColonyCombatRules.ColonyMarginAtNative("wasps_colony", p, "wasps_colony", q) >= 0)).Take(1).ToArray();
					if (chosen.Length == 0)
					{
						if (geometry.Starts.Any(p => islands[p.Y * size + p.X] == island.Key)) throw new RmgGenerationRejectedException("CHAOS_NEST_CAPACITY", "An inhabited landmass cannot fit independent flying access.");
						fragments.AddRange(island); continue;
					}

					nests.Add(chosen[0]); sites.ReserveNativeOrbit("wasps_colony", chosen);
				}

				if (fragments.Count == 0) break;
				removed += fragments.Count;
				foreach (var i in fragments)
					for (var y = Math.Max(0, i / size / 2 - 1); y <= Math.Min(width - 1, i / size / 2 + 1); y++)
						for (var x = Math.Max(0, i % size / 2 - 1); x <= Math.Min(width - 1, i % size / 2 + 1); x++)
							if (allowed[y * width + x]) removedWater[y * width + x] = true;
				if (attempt == 15) throw new RmgGenerationRejectedException("CHAOS_FRAGMENT_LIMIT", "Chaos shorelines did not settle into usable landmasses.");
			}

			// Mandatory access is terrain planning, independent of density, species weights,
			// overlap, and ownership. Once these refuges exist, optional colonies cannot repaint.
			foreach (var nest in nests) geometry.Disk(nest.X + 2, nest.Y + 2, 8, true);
			UpdateSurfaceReservations();
			terrain = TerrainComparison.Generate(Game.ModData, terrainSettings, fields: fields);
			var finalIslands = ArchipelagoGeometry.Components(TerrainComparison.NativeBytes(terrain.Map).Select(b => b != 1).ToArray(), size);
			if (!finalIslands.SequenceEqual(islands)) throw new InvalidOperationException("Chaos surface reservations changed its water topology.");
			var timer = Stopwatch.StartNew();
			RegionsSites finalSites = null;
			bool FitsOpen(string type, RmgPoint p)
			{
				if (!FitsShore(type, p)) return false;
				if (type == null || finalSites == null) return true;

				// Keep walking gaps between physical buildings even when their turret ranges
				// may overlap. This prevents dense requests from sealing narrow passages.
				var occupied = finalSites.ReservedCells;
				foreach (var o in spacingFootprints[type])
					if (occupied.Contains(new RmgPoint(p.X + o.X, p.Y + o.Y))) return false;
				return true;
			}

			finalSites = new RegionsSites(Game.ModData, terrain.Map, settings.OriginalSurfaceRelations, FitsOpen);
			finalSites.ReserveNativeOrbit(null, geometry.Starts);
			var blank = new RmgLogicalMap(width, width);
			foreach (var nest in nests)
			{
				if (!finalSites.NativeOrbitFits("wasps_colony", new[] { nest })) throw new RmgGenerationRejectedException("CHAOS_NEST_SITE", "A mandatory Chaos refuge does not fit.");
				blank.Actors.Add(RmgMirroring.Actor("wasps_colony", profile.ColonyOwner, "neutral-colony", nest, settings.PlayerCount + blank.Actors.Count) with { MandatoryNeutral = true });
				finalSites.ReserveNativeOrbit("wasps_colony", new[] { nest });
			}

			var random = new DeterministicRandom(TerrainComparison.Mix(settings.Seed, 2540));
			var optional = Enumerable.Range(0, size * size).Where(i => islands[i] >= 0 && shore[i] >= 3).Select(i => new[] { new RmgPoint(i % size, i / size) }).ToArray();
			for (var i = optional.Length - 1; i > 0; i--) { var j = random.NextInt(i + 1); (optional[i], optional[j]) = (optional[j], optional[i]); }
			PlaceMirroredColonies(blank, profile, settings, finalSites, optional, geometry.Starts, out var strict, out var evaluations, out var drawn);
			var report = geometry.Report;
			report["biomes"] = RmgChaosBiomeMix.Report(settings);
			report["actor_planning_ms"] = timer.Elapsed.TotalMilliseconds; report["islands_actual"] = nests.Count;
			report["island_areas_native"] = new JArray(islands.Where(i => i >= 0).GroupBy(i => i).Select(g => g.Count()));
			report["mandatory_nests"] = new JArray(nests.Select(p => new JArray(p.X, p.Y))); report["removed_fragment_cells"] = removed;
			report["protected_access_cells"] = geometry.Land.Count(v => v); report["biome_mixing"] = RmgChaosParameters.Name(settings.ChaosBiomes);
			var plan = new BattlefieldPlan(geometry.Starts, blank.Actors.ToArray(), strict, evaluations, drawn, geometry.Clear, geometry.Land, fields, report);
			var result = CompleteRegionsWithPlan(profile, settings, terrain, null, plan);
			var summary = result.Map.RegionsReport;
			summary["experiment_id"] = "chaos-v25"; summary["identity"] = settings.Canonical(profile);
			summary["accessibility_requirement"] = "WASPS_ACCESS_ON_EVERY_ISLAND"; summary["strategic_routes_requirement"] = "NO_FAIRNESS_OR_GLOBAL_LAND_CONNECTION";
			summary["preferred_start_reference"] = "seeded-chaos-refuges";
			summary["neutral_colonies_requested"] = Math.Max(settings.EffectiveNeutralColonyCount, nests.Count);
			summary["neutral_colonies_group_target"] = Math.Max(settings.EffectiveNeutralColonyCount, nests.Count);
			summary["neutral_colonies_disabled"] = false; summary["mandatory_neutral_nests"] = nests.Count;
			if (nests.Count > settings.EffectiveNeutralColonyCount) result.Validation.Warnings.Add(new RmgValidationIssue("CHAOS_NEST_FLOOR", "Neutral Wasps refuges override the colony target and Wasps weight."));
			return result;
		}
	}
}
