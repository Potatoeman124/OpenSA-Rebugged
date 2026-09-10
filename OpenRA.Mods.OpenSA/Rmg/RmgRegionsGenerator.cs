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
		public static int RegionsWaterPercent(RmgParameterLevel level) => level switch
		{
			RmgParameterLevel.Low => 16,
			RmgParameterLevel.Standard => 20,
			RmgParameterLevel.High => 24,
			RmgParameterLevel.Extreme => 32,
			RmgParameterLevel.Ultra => 44,
			_ => throw new ArgumentOutOfRangeException(nameof(level))
		};

		static RmgGenerationResult GenerateRegions(RmgProfile profile, RmgGenerationSettings settings)
		{
			var terrainSettings = new TerrainComparisonSettings(settings.Seed, settings.MapSize, TerrainConstruction.Regions, settings.TerrainComplexity)
			{
				MirroringAxes = settings.MirroringAxes,
				WaterPercent = RegionsWaterPercent(settings.WaterAmount),
				GravelPercent = profile.RockLandPercentFor(settings.TacticalTerrain),
				MossPercent = profile.VegetationLandPercentFor(settings.TacticalTerrain),
				OriginalSurfaceRelations = settings.OriginalSurfaceRelations,
				Continuity = settings.GeneratorVersion is 12 or 13 or 14 or 15 or 16 or 17,
				ExtendedComplexity = settings.GeneratorVersion is 13 or 14 or 15 or 16 or 17
			};
			RmgLogicalMap reference = null;
			if (settings.GeneratorVersion is 13 or 14 or 15 or 16 or 17 || (settings.GeneratorVersion == 12 && settings.TerrainComplexity != TerrainComplexity.Low))
				reference = TerrainComparison.Generate(Game.ModData, terrainSettings.ContinuityReference).Map;
			var terrain = TerrainComparison.Generate(Game.ModData, terrainSettings, reference);
			if (settings.GeneratorVersion == 12 && reference == null)
				reference = terrain.Map;
			return settings.GeneratorVersion == 17 ? CompleteRegionsWithPlan(profile, settings, terrain, reference) :
				CompleteRegions(profile, settings, terrain, reference);
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

			// V12+ starts are anchored to the same Low-complexity terrain for every
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
			var strictColonyCount = 0;
			long fallbackEvaluations = 0;
			var requestedTypes = Array.Empty<string>();
			var allowNeutralOverlap = settings.GeneratorVersion is 14 or 15 or 16 && !settings.PreventColonyOverlapping;
			if (settings.GeneratorVersion is 15 or 16)
				colonyCount = PlaceWeightedRegionsColonies(map, profile, settings, sites, candidates,
					out strictColonyCount, out fallbackEvaluations, out requestedTypes);
			else
			{
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

				strictColonyCount = colonyCount;
				if (allowNeutralOverlap && colonyCount < settings.NeutralColonyCount)
					colonyCount += FillRegionsColonyShortfall(map, profile, settings, sites, candidates,
						firstType + colonyCount, out fallbackEvaluations);
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
			ValidateColonyCombatSpace(map, profile, validation, allowNeutralOverlap);
			if (colonyCount < settings.EffectiveNeutralColonyCount)
				validation.Warnings.Add(new RmgValidationIssue("NEUTRAL_CAPACITY",
					$"Placed {colonyCount}/{settings.NeutralColonyCount} neutral colonies on valid existing terrain."));
			map.RegionsReport = terrain.Report;
			map.RegionsReport["status"] = $"PLAYABLE_REGIONS_V{settings.GeneratorVersion}";
			map.RegionsReport["placement_status"] = "LOCAL_SITES_VALID";
			map.RegionsReport["terrain_repainted_for_placement"] = false;
			map.RegionsReport["placement_ms"] = placementMs;
			map.RegionsReport["doodads_ms"] = timer.Elapsed.TotalMilliseconds;
			map.RegionsReport["neutral_colonies_requested"] = settings.EffectiveNeutralColonyCount;
			map.RegionsReport["neutral_colonies_placed"] = colonyCount;
			if (settings.GeneratorVersion is 14 or 15 or 16)
			{
				map.RegionsReport["prevent_colony_overlapping"] = settings.PreventColonyOverlapping;
				map.RegionsReport["neutral_colonies_strict"] = strictColonyCount;
				map.RegionsReport["neutral_colonies_fallback"] = colonyCount - strictColonyCount;
				map.RegionsReport["fallback_pair_evaluations"] = fallbackEvaluations;
				map.RegionsReport["neutral_overlapping_pairs"] = validation.Metrics.GetValueOrDefault("neutral_overlapping_pairs");
				map.RegionsReport["maximum_neutral_overlap_native"] = validation.Metrics.GetValueOrDefault("maximum_neutral_overlap_native");
			}
			if (settings.GeneratorVersion is 15 or 16)
			{
				map.RegionsReport["neutral_colony_weights"] = settings.NeutralColonyWeights.ToJson();
				map.RegionsReport["neutral_colonies_density_target"] = settings.NeutralColonyCount;
				map.RegionsReport["neutral_colonies_disabled"] = settings.NeutralColonyWeights.Total == 0;
				map.RegionsReport["neutral_colonies_drawn_by_type"] = new JObject(RmgColonyWeights.Keys.Select(key =>
					new JProperty(key, requestedTypes.Count(type => type == key + "_colony"))));
				map.RegionsReport["neutral_colonies_placed_by_type"] = new JObject(RmgColonyWeights.Keys.Select(key =>
					new JProperty(key, map.Actors.Count(actor => actor.Role == "neutral-colony" && actor.Type == key + "_colony"))));
			}
			if (settings.GeneratorVersion == 16)
			{
				map.RegionsReport["starting_colony_shares"] = new JArray(settings.StartingColonyShares);
				var counts = RmgColonyOwnership.Allocate(colonyCount, settings.StartingColonyShares);
				map.RegionsReport["starting_colonies_allocated_if_all_slots_occupied"] = new JArray(counts);
				map.RegionsReport["unowned_colonies_if_all_slots_occupied"] = colonyCount - counts.Sum();
				map.RegionsReport["ownership_assignment"] = settings.StartingColonyMode == RmgColonyOwnershipMode.Random ?
					"runtime-player-slot-and-seeded-random" : "runtime-player-slot-and-actual-start";
				if (settings.StartingColonyMode != RmgColonyOwnershipMode.ClosestToSpawn)
					map.RegionsReport["starting_colony_mode"] = RmgColonyOwnership.ModeName(settings.StartingColonyMode);
			}
			map.RegionsReport["doodads_requested"] = decorationTarget;
			map.RegionsReport["doodads_placed"] = decorations.Count;
			map.RegionsReport["symmetry_requirement"] = "NOT_REQUIRED";
			map.RegionsReport["strategic_routes_requirement"] = "NOT_REQUIRED";
			map.RegionsReport["placement_candidates"] = candidates.Count;
			if (reference != null)
			{
				map.RegionsReport["geography_contract"] = settings.GeneratorVersion is 13 or 14 or 15 or 16 ? "fixed-regions-extended-detail-v13" : "fixed-regions-bounded-detail-v12";
				map.RegionsReport["preferred_start_reference"] = settings.GeneratorVersion is 13 or 14 or 15 or 16 ? "same-settings-v12-low-complexity" : "same-settings-low-complexity";
				map.RegionsReport["start_displacement_native"] = new JArray(map.Starts.Select((point, i) =>
					i < preferred.Count ? 2 * Math.Sqrt(RegionDistanceSquared(point, preferred[i])) : (double?)null));
			}
			return new RmgGenerationResult
			{
				Settings = settings, Profile = profile, Map = map, Validation = validation,
				LogicalHash = HashLogicalMap(map), ActorHash = HashActors(map), GraphHash = HashGraph(map)
			};
		}
		// Penalties only increase as colonies are added. Lazy queue updates therefore select
		// the globally smallest current penalty without rescanning every site on every placement.
		static int FillRegionsColonyShortfall(RmgLogicalMap map, RmgProfile profile, RmgGenerationSettings settings,
			RegionsSites sites, List<RmgPoint> candidates, int firstType, out long pairEvaluations)
		{
			var colonies = map.Actors.Where(actor => actor.Role == "neutral-colony").ToList();
			var initialCount = colonies.Count;
			var rules = profile.ColonyCombatRules;
			var queue = new PriorityQueue<RegionsColonyCandidate, (int Depth, long SquaredDepth, int Pairs, int Rank)>();
			pairEvaluations = 0;
			for (var i = 0; i < candidates.Count; i++)
				for (var offset = 0; offset < profile.NeutralColonyActors.Length; offset++)
				{
					var type = profile.NeutralColonyActors[(firstType + offset) % profile.NeutralColonyActors.Length];
					var point = candidates[i];
					if (!sites.ColonyFits(type, point) || map.Starts.Any(start =>
						!rules.CombatSpaceIsSafeFromAnyStartingActor(type, point, start)))
						continue;
					var candidate = new RegionsColonyCandidate(type, point, i * profile.NeutralColonyActors.Length + offset);
					queue.Enqueue(candidate, candidate.Priority);
				}

			while (colonies.Count < settings.NeutralColonyCount && queue.TryDequeue(out var candidate, out var previous))
			{
				if (!sites.ColonyFits(candidate.Type, candidate.Point))
					continue;
				for (; candidate.ScoredColonies < colonies.Count; candidate.ScoredColonies++)
				{
					var other = colonies[candidate.ScoredColonies];
					var depth = Math.Max(0, -rules.CombatSpaceMarginNative(candidate.Type, candidate.Point, other.Type, other.LogicalLocation));
					pairEvaluations++;
					candidate.Depth = Math.Max(candidate.Depth, depth);
					candidate.SquaredDepth += (long)depth * depth;
					if (depth > 0)
						candidate.Pairs++;
				}

				if (candidate.Priority != previous)
				{
					queue.Enqueue(candidate, candidate.Priority);
					continue;
				}

				var actor = new RmgActorPlan(candidate.Type, profile.ColonyOwner, "neutral-colony",
					candidate.Point, settings.PlayerCount + colonies.Count);
				map.Actors.Add(actor);
				colonies.Add(actor);
				sites.ReserveColony(candidate.Type, candidate.Point);
			}

			return colonies.Count - initialCount;
		}

		sealed class RegionsColonyCandidate
		{
			public readonly string Type;
			public readonly RmgPoint Point;
			public readonly int Rank;
			public int ScoredColonies;
			public int Depth;
			public long SquaredDepth;
			public int Pairs;
			public (int, long, int, int) Priority => (Depth, SquaredDepth, Pairs, Rank);

			public RegionsColonyCandidate(string type, RmgPoint point, int rank)
			{
				Type = type;
				Point = point;
				Rank = rank;
			}
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
		readonly Func<string, RmgPoint, bool> nativeSiteFilter;
		readonly Dictionary<string, RmgPoint[]> colonies;
		readonly RmgPoint[] starts;
		readonly HashSet<RmgPoint> reserved = new();
		public IEnumerable<RmgPoint> ReservedCells => reserved;

		public RegionsSites(ModData modData, RmgLogicalMap map, bool dirtOnly, Func<string, RmgPoint, bool> nativeSiteFilter = null)
		{
			this.map = map;
			this.dirtOnly = dirtOnly;
			this.nativeSiteFilter = nativeSiteFilter;
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

		public bool NativeOrbitFits(string actor, IReadOnlyList<RmgPoint> anchors)
		{
			if (nativeSiteFilter != null && anchors.Any(anchor => !nativeSiteFilter(actor, anchor))) return false;
			var offsets = actor == null ? starts : colonies[actor];
			var occupied = new HashSet<RmgPoint>();
			foreach (var anchor in anchors)
				foreach (var offset in offsets)
				{
					var cell = new RmgPoint(anchor.X + offset.X, anchor.Y + offset.Y);
					if (cell.X < 1 || cell.Y < 1 || cell.X >= 2 * map.Width - 1 || cell.Y >= 2 * map.Height - 1 ||
						reserved.Contains(cell) || !occupied.Add(cell) || TerrainComparison.Native(map, cell.X, cell.Y) == RmgNativeTerrainIntent.Water ||
						(dirtOnly && TerrainComparison.Native(map, cell.X, cell.Y) != RmgNativeTerrainIntent.Clear)) return false;
				}

			return true;
		}

		public void ReserveNativeOrbit(string actor, IEnumerable<RmgPoint> anchors)
		{
			foreach (var anchor in anchors)
				foreach (var offset in actor == null ? starts : colonies[actor])
					reserved.Add(new RmgPoint(anchor.X + offset.X, anchor.Y + offset.Y));
		}

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
