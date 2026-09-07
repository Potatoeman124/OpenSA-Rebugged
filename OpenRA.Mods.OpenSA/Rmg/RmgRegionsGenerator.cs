#region Copyright & License Information
/*
 * Copyright The OpenSA Developers (see CREDITS)
 * This file is part of OpenSA, under the GNU General Public License, version 3 or later.
 */
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
		static RmgGenerationResult GenerateRegions(RmgProfile profile, RmgGenerationSettings settings)
		{
			var terrainSettings = new TerrainComparisonSettings(settings.Seed, settings.MapSize, TerrainConstruction.Regions, settings.TerrainComplexity)
			{
				WaterPercent = settings.WaterAmount switch { RmgParameterLevel.Low => 16, RmgParameterLevel.High => 24, _ => 20 },
				GravelPercent = profile.RockLandPercentFor(settings.TacticalTerrain),
				MossPercent = profile.VegetationLandPercentFor(settings.TacticalTerrain),
				OriginalSurfaceRelations = settings.OriginalSurfaceRelations,
				Continuity = settings.GeneratorVersion == 12
			};
			RmgLogicalMap reference = null;
			if (settings.GeneratorVersion == 12 && settings.TerrainComplexity != TerrainComplexity.Low)
				reference = TerrainComparison.Generate(Game.ModData, terrainSettings with { Complexity = TerrainComplexity.Low }).Map;
			var terrain = TerrainComparison.Generate(Game.ModData, terrainSettings, reference);
			if (settings.GeneratorVersion == 12 && reference == null)
				reference = terrain.Map;
			return CompleteRegions(profile, settings, terrain, reference);
		}

		static RmgGenerationResult CompleteRegions(RmgProfile profile, RmgGenerationSettings settings, TerrainComparisonResult terrain, RmgLogicalMap reference = null)
		{
			var map = terrain.Map;
			var frozen = TerrainComparison.Hash(TerrainComparison.NativeBytes(map));
			var timer = Stopwatch.StartNew();
			var sites = new RegionsSites(Game.ModData, map, settings.OriginalSurfaceRelations);
			var random = DeterministicRandom.ForStream(settings, profile, "regions-placement");
			var candidates = Enumerable.Range(0, map.Width * map.Height)
				.Select(i => new RmgPoint(i % map.Width, i / map.Width)).ToList();
			// Shuffle once. All dry components participate; no corridor or connectivity stage runs.
			for (var i = candidates.Count - 1; i > 0; i--)
			{
				var j = random.NextInt(i + 1);
				(candidates[i], candidates[j]) = (candidates[j], candidates[i]);
			}

			// V12 starts are anchored to the same Low-complexity terrain for every
			// selection, so relocating one start cannot reorder the whole map.
			var preferred = reference == null ? null : PreferredRegionStarts(profile, settings, reference, candidates);
			var startCandidates = candidates.Where(sites.StartFits).ToArray();
			for (var player = 0; player < settings.PlayerCount; player++)
			{
				var possible = startCandidates.Where(point => sites.StartFits(point) &&
					map.Starts.All(start => profile.ColonyCombatRules.StartingCombatSpaceMarginNative(point, start) >= 0));
				// Geographic spread is a placement preference, never a terrain-shaping rule.
				var selected = preferred != null && player < preferred.Count ?
					possible.OrderBy(point => RegionDistanceSquared(point, preferred[player])).Take(1).ToArray() :
					possible.OrderByDescending(point => map.Starts.Count == 0 ? 0 :
						map.Starts.Min(start => RegionDistanceSquared(point, start))).Take(1).ToArray();
				if (selected.Length == 0)
					throw new RmgGenerationRejectedException("REGIONS_START_CAPACITY",
						$"The completed terrain supports only {player}/{settings.PlayerCount} locally valid starts.");
				var anchor = selected[0];
				map.Starts.Add(anchor);
				map.Actors.Add(new RmgActorPlan(profile.SpawnActor, profile.SpawnOwner, "start", anchor, player));
				sites.ReserveStart(anchor);
			}

			var colonyCount = 0;
			var firstType = random.NextInt(profile.NeutralColonyActors.Length);
			foreach (var point in candidates)
			{
				if (colonyCount == settings.NeutralColonyCount)
					break;
				for (var offset = 0; offset < profile.NeutralColonyActors.Length; offset++)
				{
					var type = profile.NeutralColonyActors[(firstType + colonyCount + offset) % profile.NeutralColonyActors.Length];
					if (!sites.ColonyFits(type, point) || !ColonyCombatSpaceIsValid(map, profile, type, point))
						continue;
					map.Actors.Add(new RmgActorPlan(type, profile.ColonyOwner, "neutral-colony", point, settings.PlayerCount + colonyCount));
					sites.ReserveColony(type, point);
					colonyCount++;
					break;
				}
			}

			var placementMs = timer.Elapsed.TotalMilliseconds;
			timer.Restart();
			var decorationRandom = DeterministicRandom.ForStream(settings, profile, "regions-doodads");
			var decorationCandidates = Enumerable.Range(0, settings.MapSize * settings.MapSize)
				.Select(i => new RmgPoint(i % settings.MapSize, i / settings.MapSize)).ToArray();
			for (var i = decorationCandidates.Length - 1; i > 0; i--)
			{
				var j = decorationRandom.NextInt(i + 1);
				(decorationCandidates[i], decorationCandidates[j]) = (decorationCandidates[j], decorationCandidates[i]);
			}

			var decorationTarget = (settings.MapSize * settings.MapSize * profile.LandDecorationPerThousand + 500) / 1000;
			var decorations = new List<RmgPoint>();
			foreach (var native in decorationCandidates)
			{
				if (decorations.Count == decorationTarget)
					break;
				if (sites.NearReserved(native) || decorations.Any(other => other.ChebyshevDistance(native) < 4))
					continue;
				var bank = TerrainComparison.Native(map, native.X, native.Y) switch
				{
					RmgNativeTerrainIntent.Clear => profile.SoilDecorationActors,
					RmgNativeTerrainIntent.Rock => profile.RockDecorationActors,
					RmgNativeTerrainIntent.Vegetation => profile.VegetationDecorationActors,
					_ => Array.Empty<string>()
				};
				if (bank.Length == 0)
					continue;
				var type = bank[decorationRandom.NextInt(bank.Length)];
				map.Actors.Add(new RmgActorPlan(type, profile.SpawnOwner, "decoration-passable",
					new RmgPoint(native.X / 2, native.Y / 2), -1, native.X % 2 + 2 * (native.Y % 2)));
				decorations.Add(native);
			}

			if (frozen != TerrainComparison.Hash(TerrainComparison.NativeBytes(map)))
				throw new InvalidOperationException("Regions placement or doodads modified completed terrain.");
			map.NaturalSurfacesFrozen = true;
			var validation = new RmgValidationReport();
			ValidateColonyCombatSpace(map, profile, validation);
			if (colonyCount < settings.NeutralColonyCount)
				validation.Warnings.Add(new RmgValidationIssue("NEUTRAL_CAPACITY",
					$"Placed {colonyCount}/{settings.NeutralColonyCount} neutral colonies on valid existing terrain."));
			map.RegionsReport = terrain.Report;
			map.RegionsReport["status"] = $"PLAYABLE_REGIONS_V{settings.GeneratorVersion}";
			map.RegionsReport["placement_status"] = "LOCAL_SITES_VALID";
			map.RegionsReport["terrain_repainted_for_placement"] = false;
			map.RegionsReport["placement_ms"] = placementMs;
			map.RegionsReport["doodads_ms"] = timer.Elapsed.TotalMilliseconds;
			map.RegionsReport["neutral_colonies_requested"] = settings.NeutralColonyCount;
			map.RegionsReport["neutral_colonies_placed"] = colonyCount;
			map.RegionsReport["doodads_requested"] = decorationTarget;
			map.RegionsReport["doodads_placed"] = decorations.Count;
			map.RegionsReport["symmetry_requirement"] = "NOT_REQUIRED";
			map.RegionsReport["strategic_routes_requirement"] = "NOT_REQUIRED";
			map.RegionsReport["placement_candidates"] = candidates.Count;
			if (reference != null)
			{
				map.RegionsReport["geography_contract"] = "fixed-regions-bounded-detail-v12";
				map.RegionsReport["preferred_start_reference"] = "same-settings-low-complexity";
				map.RegionsReport["start_displacement_native"] = new JArray(map.Starts.Select((point, i) =>
					i < preferred.Count ? 2 * Math.Sqrt(RegionDistanceSquared(point, preferred[i])) : (double?)null));
			}
			return new RmgGenerationResult
			{
				Settings = settings, Profile = profile, Map = map, Validation = validation,
				LogicalHash = HashLogicalMap(map), ActorHash = HashActors(map), GraphHash = HashGraph(map)
			};
		}
		static long RegionDistanceSquared(RmgPoint a, RmgPoint b) =>
			(long)(a.X - b.X) * (a.X - b.X) + (long)(a.Y - b.Y) * (a.Y - b.Y);

		static List<RmgPoint> PreferredRegionStarts(RmgProfile profile, RmgGenerationSettings settings,
			RmgLogicalMap reference, List<RmgPoint> candidates)
		{
			var sites = new RegionsSites(Game.ModData, reference, settings.OriginalSurfaceRelations);
			var starts = new List<RmgPoint>();
			var valid = candidates.Where(sites.StartFits).ToArray();
			for (var player = 0; player < settings.PlayerCount; player++)
			{
				var next = valid.Where(point => sites.StartFits(point) &&
					starts.All(start => profile.ColonyCombatRules.StartingCombatSpaceMarginNative(point, start) >= 0))
					.OrderByDescending(point => starts.Count == 0 ? 0 : starts.Min(start => RegionDistanceSquared(point, start)))
					.Take(1).ToArray();
				if (next.Length == 0)
					break;
				starts.Add(next[0]);
				sites.ReserveStart(next[0]);
			}

			return starts;
		}

	}

	// Native offsets come from current runtime rules. Queries never mutate terrain.
	sealed class RegionsSites
	{
		readonly RmgLogicalMap map;
		readonly bool dirtOnly;
		readonly Dictionary<string, RmgPoint[]> colonies;
		readonly RmgPoint[] starts;
		readonly HashSet<RmgPoint> reserved = new();

		public RegionsSites(ModData modData, RmgLogicalMap map, bool dirtOnly)
		{
			this.map = map;
			this.dirtOnly = dirtOnly;
			RmgPoint[] Coverage(string actor) => modData.DefaultRules.Actors[actor].TraitInfos<BuildingInfo>()
				.SelectMany(info => info.Tiles(new CPos(0, 0)))
				.Concat(modData.DefaultRules.Actors[actor].TraitInfos<ExitInfo>().Select(exit => new CPos(exit.ExitCell.X, exit.ExitCell.Y)))
				.Select(cell => new RmgPoint(cell.X, cell.Y)).Append(new RmgPoint(0, 0)).Distinct().ToArray();
			colonies = new[] { "ants_colony", "beetles_colony", "scorpions_colony", "spiders_colony", "wasps_colony" }
				.ToDictionary(actor => actor, Coverage, StringComparer.OrdinalIgnoreCase);
			starts = modData.DefaultRules.Actors[SystemActors.World].TraitInfos<StartingUnitsInfo>()
				.Where(info => !string.IsNullOrEmpty(info.BaseActor))
				.SelectMany(info => Coverage(info.BaseActor).Select(offset =>
					new RmgPoint(offset.X + info.BaseActorOffset.X, offset.Y + info.BaseActorOffset.Y)))
				.Append(new RmgPoint(0, 0)).Distinct().ToArray();
		}

		IEnumerable<RmgPoint> Cells(RmgPoint anchor, IEnumerable<RmgPoint> offsets) =>
			offsets.Select(offset => new RmgPoint(2 * anchor.X + offset.X, 2 * anchor.Y + offset.Y));

		bool Fits(RmgPoint anchor, RmgPoint[] offsets) => Cells(anchor, offsets).All(cell =>
			cell.X >= 1 && cell.Y >= 1 && cell.X < 2 * map.Width - 1 && cell.Y < 2 * map.Height - 1 &&
			!reserved.Contains(cell) && TerrainComparison.Native(map, cell.X, cell.Y) != RmgNativeTerrainIntent.Water &&
			(!dirtOnly || TerrainComparison.Native(map, cell.X, cell.Y) == RmgNativeTerrainIntent.Clear));

		public bool StartFits(RmgPoint anchor) => Fits(anchor, starts);
		public bool ColonyFits(string actor, RmgPoint anchor) => Fits(anchor, colonies[actor]);
		public void ReserveStart(RmgPoint anchor) => reserved.UnionWith(Cells(anchor, starts));
		public void ReserveColony(string actor, RmgPoint anchor) => reserved.UnionWith(Cells(anchor, colonies[actor]));
		public bool NearReserved(RmgPoint cell)
		{
			for (var dy = -1; dy <= 1; dy++)
				for (var dx = -1; dx <= 1; dx++)
					if (reserved.Contains(new RmgPoint(cell.X + dx, cell.Y + dy)))
						return true;
			return false;
		}
	}
}
