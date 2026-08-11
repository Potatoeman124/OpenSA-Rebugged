#region Copyright & License Information
/*
 * Copyright The OpenSA Developers (see CREDITS)
 * This file is part of OpenSA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenRA.Mods.Common.Terrain;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.OpenSA.UtilityCommands
{
	/// <summary>
	/// Extracts reproducible, read-only evidence for the RMG Phase 1 map-corpus audit.
	/// This command intentionally does not create or modify maps.
	/// </summary>
	sealed class AuditRmgMapCorpusCommand : IUtilityCommand
	{
		const string SchemaVersion = "1.0";
		const string CommandName = "--audit-rmg-map-corpus";
		const double SqrtTwo = 1.4142135623730951;

		static readonly CVec[] Directions =
		{
			new(-1, -1), new(-1, 0), new(-1, 1), new(0, -1),
			new(0, 1), new(1, -1), new(1, 0), new(1, 1)
		};

		static readonly HashSet<string> ColonyActorTypes = new(StringComparer.OrdinalIgnoreCase)
		{
			"ants_colony", "beetles_colony", "scorpions_colony", "spiders_colony", "wasps_colony", "randomcolony"
		};

		string IUtilityCommand.Name => CommandName;

		bool IUtilityCommand.ValidateArguments(string[] args)
		{
			return args.Length == 2;
		}

		[Desc("OUTPUT-DIRECTORY", "Extract the shipped OpenSA map corpus and terrain-template catalogue as JSON. No maps are modified.")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			Game.ModData = utility.ModData;
			var outputDirectory = Path.GetFullPath(args[1]);
			Directory.CreateDirectory(outputDirectory);

			utility.ModData.MapCache.LoadMaps();
			var previews = utility.ModData.MapCache
				.Where(p => p.Class == MapClassification.System && p.Status == MapStatus.Available)
				.OrderBy(p => PackageName(p.Package), StringComparer.OrdinalIgnoreCase)
				.ToArray();

			var maps = new JArray();
			foreach (var preview in previews)
			{
				using var map = new Map(utility.ModData, preview.Package);
				maps.Add(AnalyzeMap(map, PackageName(preview.Package)));
			}

			var classificationCounts = new JObject();
			foreach (var group in maps.Children<JObject>()
				.GroupBy(m => (string)m["classification"])
				.OrderBy(g => g.Key, StringComparer.Ordinal))
				classificationCounts[group.Key] = group.Count();

			var corpus = new JObject
			{
				["schema_version"] = SchemaVersion,
				["generated_utc"] = DateTime.UtcNow.ToString("O"),
				["extractor"] = $"OpenRA.Utility sa {CommandName}",
				["scope"] = "shipped System-class maps only",
				["map_count"] = maps.Count,
				["classification_counts"] = classificationCounts,
				["methodology"] = new JObject
				{
					["ground_locomotor"] = "unit",
					["terrain_authority"] = "Map.GetTerrainInfo(cell) resolved by the pinned engine",
					["static_obstacles"] = "Resolved non-positionable IOccupySpaceInfo footprints from map actors",
					["dynamic_units"] = "Excluded from permanent passability",
					["neighbors"] = "Eight CVec directions, matching the rectangular OpenRA path graph",
					["path_cost"] = "Destination terrain cost; diagonal steps multiplied by sqrt(2); lane bias omitted",
					["distance_unit"] = "clear-terrain-equivalent cells (raw locomotor cost divided by 100)",
					["route_redundancy"] = "A second Dijkstra run after blocking a 3x3 window at the narrowest interior cell of the first route",
					["choke_width"] = "Minimum passable native-cell cross-section perpendicular to the interior shortest-path tangent",
					["limitations"] = new JArray(
						"No live World is constructed, so scripted runtime actor changes are not modeled.",
						"Lane-bias smoothing and temporary/dynamic actor blocking are intentionally omitted.",
						"Route redundancy is a deterministic obstruction test, not an exact maximum-flow count.")
				},
				["maps"] = maps
			};

			var terrainCatalogue = AnalyzeTerrainCatalogue(utility.ModData);
			WriteJson(Path.Combine(outputDirectory, "map_corpus.json"), corpus);
			WriteJson(Path.Combine(outputDirectory, "terrain_template_catalogue.json"), terrainCatalogue);

			Console.WriteLine($"Audited {maps.Count} shipped maps.");
			Console.WriteLine(Path.Combine(outputDirectory, "map_corpus.json"));
			Console.WriteLine(Path.Combine(outputDirectory, "terrain_template_catalogue.json"));
		}

		static JObject AnalyzeMap(Map map, string directory)
		{
			var players = new MapPlayers(map.PlayerDefinitions).Players.Values.ToArray();
			var playablePlayers = players.Where(p => p.Playable).ToArray();
			var hasScript = map.Package.Contains("script.lua");
			var hasRules = map.Package.Contains("rules.yaml");
			var classification = ClassifyMap(directory, map, playablePlayers.Length, hasScript, out var classificationReason);
			var actorRecords = ReadActors(map);
			var spawns = actorRecords.Where(a => a.Type.Equals("mpspawn", StringComparison.OrdinalIgnoreCase) && a.Location != null).ToArray();
			var colonies = actorRecords.Where(a => a.IsStrategicColony && a.Location != null).ToArray();
			var playableNames = playablePlayers.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
			var neutralColonies = colonies.Where(c => c.Owner == null || c.Owner.Equals("Neutral", StringComparison.OrdinalIgnoreCase) ||
				c.Owner.Equals("Creeps", StringComparison.OrdinalIgnoreCase)).ToArray();
			var authoredStartingColonies = colonies.Where(c => c.Owner != null && playableNames.Contains(c.Owner)).ToArray();
			var worldActor = map.Rules.Actors[SystemActors.World];
			var runtimeStartingColonyActors = worldActor.TraitInfos<StartingUnitsInfo>()
				.Where(s => s.Class.Equals("none", StringComparison.OrdinalIgnoreCase) && s.BaseActor != null)
				.Select(s => s.BaseActor).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
			var runtimeStartingColoniesEnabled = worldActor.HasTraitInfo<SpawnStartingUnitsInfo>() && runtimeStartingColonyActors.Length > 0;
			var customRuleActorKeys = CustomRuleActorKeys(map);
			var locomotor = GetGroundLocomotor(map.Rules);
			var model = BuildPassabilityModel(map, locomotor, actorRecords);

			var actorTypeCounts = CountBy(actorRecords.Select(a => a.Type));
			var actorOwnerCounts = CountBy(actorRecords.Select(a => a.Owner ?? "<unspecified>"));
			var terrainTypeCounts = CountBy(model.TerrainTypes);
			var templateUsage = model.TemplateUsage.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key.ToString(), kv => (object)kv.Value);

			var result = new JObject
			{
				["directory"] = directory,
				["uid"] = map.Uid,
				["classification"] = classification,
				["classification_reason"] = classificationReason,
				["metadata"] = new JObject
				{
					["title"] = map.Title,
					["author"] = map.Author,
					["tileset"] = map.Tileset,
					["map_format"] = map.MapFormat,
					["map_size"] = new JArray(map.MapSize.X, map.MapSize.Y),
					["bounds"] = new JObject
					{
						["left"] = map.Bounds.Left,
						["top"] = map.Bounds.Top,
						["width"] = map.Bounds.Width,
						["height"] = map.Bounds.Height
					},
					["visibility"] = map.Visibility.ToString(),
					["categories"] = new JArray(map.Categories),
					["package_files"] = new JArray(map.Package.Contents.OrderBy(f => f, StringComparer.OrdinalIgnoreCase)),
					["has_script"] = hasScript,
					["has_custom_rules"] = HasDefinition(map.RuleDefinitions) || hasRules,
					["has_custom_weapons"] = HasDefinition(map.WeaponDefinitions),
					["has_custom_sequences"] = HasDefinition(map.SequenceDefinitions),
					["has_custom_model_sequences"] = HasDefinition(map.ModelSequenceDefinitions),
					["has_custom_voices"] = HasDefinition(map.VoiceDefinitions),
					["has_custom_music"] = HasDefinition(map.MusicDefinitions),
					["has_custom_notifications"] = HasDefinition(map.NotificationDefinitions),
					["custom_rule_actor_keys"] = new JArray(customRuleActorKeys),
					["invalid_custom_rules"] = map.InvalidCustomRules
				},
				["players"] = new JObject
				{
					["defined_count"] = players.Length,
					["playable_count"] = playablePlayers.Length,
					["bot_count"] = players.Count(p => p.Bot != null),
					["playable"] = new JArray(playablePlayers.Select(p => new JObject
					{
						["name"] = p.Name,
						["faction"] = p.Faction,
						["required"] = p.Required,
						["lock_spawn"] = p.LockSpawn,
						["lock_team"] = p.LockTeam
					}))
				},
				["actors"] = new JObject
				{
					["total_count"] = actorRecords.Count,
					["by_type"] = JObject.FromObject(actorTypeCounts),
					["by_owner"] = JObject.FromObject(actorOwnerCounts),
					["static_blocked_cell_count"] = model.StaticBlockedCells,
					["positionable_map_actors_by_owner"] = JObject.FromObject(CountBy(actorRecords.Where(a => a.IsPositionable)
						.Select(a => a.Owner ?? "<unspecified>"))),
					["tower_or_turret_actor_count"] = actorRecords.Count(a => a.Type.Contains("turret", StringComparison.OrdinalIgnoreCase) ||
						a.Type.Contains("tower", StringComparison.OrdinalIgnoreCase) || a.Type.Contains("tesla_coil", StringComparison.OrdinalIgnoreCase)),
					["other_static_structure_count"] = actorRecords.Count(a => a.OccupiedCells.Length > 0 && !a.IsStrategicColony),
					["spawn_count"] = spawns.Length,
					["spawns"] = new JArray(spawns.Select(s => Point(s.Location.Value))),
					["strategic_colony_count"] = colonies.Length,
					["authored_starting_colony_count"] = authoredStartingColonies.Length,
					["neutral_or_creep_colony_count"] = neutralColonies.Length,
					["runtime_starting_colonies_enabled"] = runtimeStartingColoniesEnabled,
					["runtime_starting_colony_count"] = classification == "SKIRMISH_REFERENCE" && runtimeStartingColoniesEnabled ? playablePlayers.Length : null,
					["runtime_starting_colony_actor_types"] = new JArray(runtimeStartingColonyActors),
					["runtime_starting_colony_source"] = "SpawnStartingUnits places the faction StartingUnits BaseActor at each player's mpspawn-derived HomeLocation.",
					["strategic_colonies"] = new JArray(colonies.Select(ColonyJson))
				},
				["lobby_suitability"] = classification == "SKIRMISH_REFERENCE" ? "CONFIRMED" : "NOT_A_REFERENCE",
				["terrain"] = new JObject
				{
					["playable_cell_count"] = model.CellCount,
					["terrain_type_counts"] = JObject.FromObject(terrainTypeCounts),
					["template_usage_by_native_cell"] = JObject.FromObject(templateUsage),
					["terrain_passable_cell_count"] = model.TerrainPassableCount,
					["static_actor_blocked_passable_cell_count"] = model.StaticBlockedCells,
					["final_passable_cell_count"] = model.Passable.Count(p => p),
					["final_passable_fraction"] = Round(model.Passable.Count(p => p) / (double)model.CellCount, 6),
					["macro_grid_2x2"] = AnalyzeMacroGrid(map)
				}
			};

			if (classification == "SKIRMISH_REFERENCE")
				result["reference_metrics"] = AnalyzeReferenceMap(map, model, spawns, colonies);
			else
				result["reference_metrics"] = null;

			return result;
		}

		static JObject AnalyzeReferenceMap(Map map, PassabilityModel model, ActorRecord[] spawns, ActorRecord[] colonies)
		{
			var components = CalculateComponents(model);
			var clearance = CalculateClearance(model);
			var spawnCells = spawns.Select(s => s.Location.Value).ToArray();
			var spawnIndexes = spawnCells.Select(model.IndexOrMinusOne).ToArray();
			var pairMetrics = new JArray();

			for (var i = 0; i < spawnCells.Length; i++)
			{
				for (var j = i + 1; j < spawnCells.Length; j++)
				{
					var path = ShortestPath(model, spawnCells[i], new[] { spawnCells[j] });
					pairMetrics.Add(PairMetric(model, clearance, spawnCells[i], spawnCells[j], i, j, path));
				}
			}

			var spawnMetrics = new JArray();
			for (var i = 0; i < spawnCells.Length; i++)
			{
				var index = model.IndexOrMinusOne(spawnCells[i]);
				spawnMetrics.Add(new JObject
				{
					["spawn_index"] = i,
					["location"] = Point(spawnCells[i]),
					["passable"] = index >= 0 && model.Passable[index],
					["component"] = index >= 0 ? components.Labels[index] : -1,
					["reachable_cell_count"] = index >= 0 && components.Labels[index] >= 0 ? components.Sizes[components.Labels[index]] : 0,
					["reachable_fraction_of_final_passable"] = index >= 0 && components.Labels[index] >= 0 ?
						Round(components.Sizes[components.Labels[index]] / (double)model.Passable.Count(p => p), 6) : 0,
					["clearance_cells"] = index >= 0 ? clearance[index] : 0,
					["local_passable_fraction_r12"] = Round(LocalPassableFraction(model, spawnCells[i], 12), 6),
					["escape_sector_count_r12"] = EscapeSectorCount(model, components, spawnCells[i], 12),
					["start_exit_count_approximation"] = "Occupied angular sectors on the radius-12 ring that share the start's passable component."
				});
			}

			var colonyMetrics = new JArray();
			var colonyDistancesByColony = new List<double[]>();
			var nearestColonyDistances = Enumerable.Range(0, spawnCells.Length).Select(_ => double.PositiveInfinity).ToArray();
			foreach (var colony in colonies)
			{
				var accessCells = ColonyAccessCells(model, colony);
				var distances = new double[spawnCells.Length];
				for (var i = 0; i < spawnCells.Length; i++)
				{
					var path = ShortestPath(model, spawnCells[i], accessCells);
					distances[i] = path.Reachable ? path.Cost / 100.0 : double.PositiveInfinity;
					nearestColonyDistances[i] = Math.Min(nearestColonyDistances[i], distances[i]);
				}

				colonyDistancesByColony.Add(distances);
				colonyMetrics.Add(ColonyMetric(map, model, colony, distances));
			}

			var centralTargets = CentralRegionCells(model, 5);
			for (var i = 0; i < spawnCells.Length; i++)
			{
				var orderedColonies = colonyDistancesByColony.Select(d => d[i]).Where(double.IsFinite).OrderBy(d => d).ToArray();
				var centralPath = ShortestPath(model, spawnCells[i], centralTargets);
				var spawnMetric = (JObject)spawnMetrics[i];
				spawnMetric["nearest_neutral_colony_distance"] = orderedColonies.Length == 0 ? null : Round(orderedColonies[0], 4);
				spawnMetric["second_nearest_neutral_colony_distance"] = orderedColonies.Length < 2 ? null : Round(orderedColonies[1], 4);
				spawnMetric["central_region_distance"] = centralPath.Reachable ? Round(centralPath.Cost / 100.0, 4) : null;
			}

			var reachablePairDistances = pairMetrics.Children<JObject>()
				.Where(p => (bool)p["reachable"])
				.Select(p => (double)p["path_distance"])
				.ToArray();
			var finiteNearestColonies = nearestColonyDistances.Where(double.IsFinite).ToArray();
			var symmetry = AnalyzeSymmetry(model, spawnCells, colonies);
			var coreRegions = CalculateCoreRegions(model, clearance, 3);
			var pairWidths = pairMetrics.Children<JObject>().Where(p => p["estimated_choke_width_cells"]?.Type == JTokenType.Integer)
				.Select(p => (int)p["estimated_choke_width_cells"]).ToArray();
			var bottleneckPairs = pairMetrics.Children<JObject>().Where(p => p["estimated_choke_width_cells"]?.Type == JTokenType.Integer &&
				(int)p["estimated_choke_width_cells"] <= 8).ToArray();
			var uniqueBottlenecks = bottleneckPairs.Select(p => $"{(int)p["narrowest_interior_cell"]["x"]},{(int)p["narrowest_interior_cell"]["y"]}")
				.Distinct(StringComparer.Ordinal).Count();
			for (var i = 0; i < colonyMetrics.Count; i++)
			{
				var colonyMetric = (JObject)colonyMetrics[i];
				var location = colonies[i].Location.Value;
				colonyMetric["nearby_audited_chokepoint_count_r12"] = bottleneckPairs.Count(p =>
				{
					var point = p["narrowest_interior_cell"];
					return Euclidean(location, new CPos((int)point["x"], (int)point["y"])) <= 12;
				});
			}

			var meanPeerDistanceBySpawn = Enumerable.Range(0, spawnCells.Length).Select(i =>
			{
				var distances = pairMetrics.Children<JObject>()
					.Where(p => (bool)p["reachable"] && ((int)p["a"] == i || (int)p["b"] == i))
					.Select(p => (double)p["path_distance"]).ToArray();
				return distances.Length == 0 ? double.PositiveInfinity : distances.Average();
			}).ToArray();
			var finitePeerMeans = meanPeerDistanceBySpawn.Where(double.IsFinite).ToArray();
			var localAreaValues = spawnMetrics.Children<JObject>().Select(s => (double)s["local_passable_fraction_r12"]).ToArray();
			var clearanceValues = spawnMetrics.Children<JObject>().Select(s => (double)(int)s["clearance_cells"]).ToArray();
			var peerCv = finitePeerMeans.Length < 2 ? 0 : CoefficientOfVariation(finitePeerMeans);
			var colonyCv = finiteNearestColonies.Length < 2 ? 0 : CoefficientOfVariation(finiteNearestColonies);
			var localAreaCv = localAreaValues.Length < 2 ? 0 : CoefficientOfVariation(localAreaValues);
			var clearanceCv = clearanceValues.Length < 2 ? 0 : CoefficientOfVariation(clearanceValues);
			var functionalImbalance = 0.35 * Math.Min(1, peerCv) + 0.35 * Math.Min(1, colonyCv) +
				0.2 * Math.Min(1, localAreaCv) + 0.1 * Math.Min(1, clearanceCv);

			return new JObject
			{
				["ground_connectivity"] = new JObject
				{
					["component_count"] = components.Sizes.Count,
					["largest_component_cells"] = components.Sizes.Count == 0 ? 0 : components.Sizes.Max(),
					["largest_component_fraction_of_passable"] = components.Sizes.Count == 0 ? 0 :
						Round(components.Sizes.Max() / (double)model.Passable.Count(p => p), 6),
					["core_region_count_clearance_gte_3"] = coreRegions,
					["all_spawns_connected"] = spawnIndexes.Length > 0 && spawnIndexes.All(i => i >= 0 && model.Passable[i]) &&
						spawnIndexes.Select(i => components.Labels[i]).Distinct().Count() == 1
				},
				["spawns"] = spawnMetrics,
				["spawn_pairs"] = pairMetrics,
				["route_summary"] = new JObject
				{
					["minimum_route_width_cells"] = pairWidths.Length == 0 ? null : pairWidths.Min(),
					["mean_route_width_cells"] = pairWidths.Length == 0 ? null : Round(pairWidths.Average(), 4),
					["route_width_distribution_cells"] = new JArray(pairWidths.OrderBy(w => w)),
					["bottleneck_pair_count_width_lte_8"] = bottleneckPairs.Length,
					["approximate_unique_chokepoint_count"] = uniqueBottlenecks,
					["alternate_route_pair_count"] = pairMetrics.Children<JObject>().Count(p => (bool)p["alternate_route_available"]),
					["pair_count"] = pairMetrics.Count
				},
				["spawn_pair_path_distance_mean"] = reachablePairDistances.Length == 0 ? null : Round(reachablePairDistances.Average(), 4),
				["spawn_pair_path_distance_cv"] = reachablePairDistances.Length < 2 ? 0 : Round(CoefficientOfVariation(reachablePairDistances), 6),
				["colonies"] = colonyMetrics,
				["nearest_colony_distance_by_spawn"] = new JArray(nearestColonyDistances.Select(NullableDistance)),
				["nearest_colony_distance_cv"] = Round(colonyCv, 6),
				["functional_fairness"] = new JObject
				{
					["mean_peer_path_distance_by_spawn"] = new JArray(meanPeerDistanceBySpawn.Select(NullableDistance)),
					["mean_peer_path_distance_cv"] = Round(peerCv, 6),
					["nearest_colony_distance_cv"] = Round(colonyCv, 6),
					["local_passable_area_r12_cv"] = Round(localAreaCv, 6),
					["spawn_clearance_cv"] = Round(clearanceCv, 6),
					["composite_imbalance_score"] = Round(functionalImbalance, 6),
					["assessment"] = functionalImbalance <= 0.08 ? "strong" : functionalImbalance <= 0.15 ? "moderate" : "weak",
					["interpretation"] = "Lower is more balanced. Composite weights: peer access 35%, nearest colony 35%, local passable area 20%, clearance 10%."
				},
				["symmetry"] = symmetry,
				["candidate_archetypes"] = CandidateArchetypes(model, pairMetrics, symmetry)
			};
		}

		static JObject PairMetric(PassabilityModel model, int[] clearance, CPos a, CPos b, int aIndex, int bIndex, PathResult path)
		{
			var result = new JObject
			{
				["a"] = aIndex,
				["b"] = bIndex,
				["euclidean_distance"] = Round(Euclidean(a, b), 4),
				["reachable"] = path.Reachable
			};

			if (!path.Reachable)
			{
				result["path_distance"] = null;
				result["path_stretch_vs_euclidean"] = null;
				result["interior_min_clearance_cells"] = null;
				result["estimated_choke_width_cells"] = null;
				result["alternate_route_available"] = false;
				result["alternate_route_stretch"] = null;
				return result;
			}

			var pathDistance = path.Cost / 100.0;
			var interiorStart = Math.Min(path.Cells.Count - 1, Math.Max(1, path.Cells.Count / 10));
			var interiorEnd = Math.Max(interiorStart, Math.Min(path.Cells.Count - 2, path.Cells.Count * 9 / 10));
			var interiorIndexes = Enumerable.Range(interiorStart, Math.Max(1, interiorEnd - interiorStart + 1)).ToArray();
			var narrowestIndex = interiorIndexes.OrderBy(k => CrossSectionWidth(model, path.Cells, k))
				.ThenBy(k => clearance[model.Index(path.Cells[k])]).First();
			var narrowest = path.Cells[narrowestIndex];
			var minClearance = clearance[model.Index(narrowest)];
			var chokeWidth = CrossSectionWidth(model, path.Cells, narrowestIndex);
			var extraBlocked = new HashSet<int>();
			for (var dy = -1; dy <= 1; dy++)
				for (var dx = -1; dx <= 1; dx++)
				{
					var c = narrowest + new CVec(dx, dy);
					var index = model.IndexOrMinusOne(c);
					if (index >= 0 && c != a && c != b)
						extraBlocked.Add(index);
				}

			var alternate = ShortestPath(model, a, new[] { b }, extraBlocked);
			result["path_distance"] = Round(pathDistance, 4);
			result["path_stretch_vs_euclidean"] = Round(pathDistance / Math.Max(1, Euclidean(a, b)), 6);
			result["interior_min_clearance_cells"] = minClearance;
			result["estimated_choke_width_cells"] = chokeWidth;
			result["choke_width_method"] = "Passable native-cell run perpendicular to the local shortest-path tangent";
			result["narrowest_interior_cell"] = Point(narrowest);
			result["alternate_route_available"] = alternate.Reachable;
			result["alternate_route_stretch"] = alternate.Reachable ? Round(alternate.Cost / 100.0 / pathDistance, 6) : null;
			return result;
		}

		static JObject ColonyMetric(Map map, PassabilityModel model, ActorRecord colony, double[] distances)
		{
			var finite = distances.Select((d, i) => (Distance: d, Spawn: i)).Where(x => double.IsFinite(x.Distance))
				.OrderBy(x => x.Distance).ToArray();
			var center = new CPos(map.Bounds.Left + map.Bounds.Width / 2, map.Bounds.Top + map.Bounds.Height / 2);
			var location = colony.Location.Value;
			var minEdge = Math.Min(Math.Min(location.X - map.Bounds.Left, map.Bounds.Right - 1 - location.X),
				Math.Min(location.Y - map.Bounds.Top, map.Bounds.Bottom - 1 - location.Y));
			var maxDimension = Math.Max(map.Bounds.Width, map.Bounds.Height);
			var roles = new JArray();
			var centerDistance = Euclidean(location, center) / maxDimension;
			var nearEdge = minEdge <= Math.Max(4, maxDimension / 12);
			var accessGap = finite.Length >= 2 ?
				Math.Abs(finite[1].Distance - finite[0].Distance) / Math.Max(1, (finite[0].Distance + finite[1].Distance) / 2) : (double?)null;
			if (finite.Length >= 2)
			{
				if (accessGap <= 0.15 && centerDistance <= 0.2)
					roles.Add("CENTRAL_CONTESTED");
				else if (accessGap <= 0.15)
					roles.Add("SIDE_CONTESTED");
				else if (finite[0].Distance <= maxDimension * 0.2)
					roles.Add("NEAR_START");
				else if (finite[0].Distance <= finite[1].Distance * 0.75 &&
					LocalPassableFraction(model, location, 10) >= 0.7)
					roles.Add("SAFE");
			}

			if (nearEdge)
				roles.Add("PERIPHERAL");
			if (colony.Type.Equals("randomcolony", StringComparison.OrdinalIgnoreCase))
				roles.Add("SPECIAL");

			return new JObject
			{
				["actor_key"] = colony.Key,
				["type"] = colony.Type,
				["owner"] = colony.Owner,
				["location"] = Point(location),
				["path_distance_by_spawn"] = new JArray(distances.Select(NullableDistance)),
				["nearest_spawn"] = finite.Length == 0 ? null : finite[0].Spawn,
				["nearest_distance"] = finite.Length == 0 ? null : Round(finite[0].Distance, 4),
				["second_nearest_distance"] = finite.Length < 2 ? null : Round(finite[1].Distance, 4),
				["access_gap_ratio"] = accessGap == null ? null : Round(accessGap.Value, 6),
				["local_passable_fraction_r10"] = Round(LocalPassableFraction(model, location, 10), 6),
				["local_obstacle_fraction_r10"] = Round(1 - LocalPassableFraction(model, location, 10), 6),
				["approach_sector_count_r8"] = PassableSectorCount(model, location, 8),
				["roles"] = roles
			};
		}

		static JArray CandidateArchetypes(PassabilityModel model, JArray pairMetrics, JObject symmetry)
		{
			var ret = new JArray();
			var waterFraction = model.TerrainTypes.Count(t => t.Equals("Water", StringComparison.OrdinalIgnoreCase)) / (double)model.CellCount;
			var passableFraction = model.Passable.Count(p => p) / (double)model.CellCount;
			var chokes = pairMetrics.Children<JObject>().Where(p => p["estimated_choke_width_cells"]?.Type == JTokenType.Integer)
				.Select(p => (int)p["estimated_choke_width_cells"]).ToArray();
			var bestScore = (double)symmetry["best"]["strict_mismatch_score"];

			if (waterFraction >= 0.2)
				ret.Add("water-separated");
			if (passableFraction <= 0.6)
				ret.Add("constricted");
			if (chokes.Length > 0 && chokes.Average() <= 7)
				ret.Add("chokepoint-focused");
			if (bestScore <= 0.05)
				ret.Add("geometrically-symmetric");
			if (ret.Count == 0)
				ret.Add("open-field");
			return ret;
		}

		static JObject AnalyzeSymmetry(PassabilityModel model, CPos[] spawns, ActorRecord[] colonies)
		{
			var transforms = new List<(string Name, Func<int, int, (int X, int Y)> Apply)>
			{
				("mirror_x", (x, y) => (model.Width - 1 - x, y)),
				("mirror_y", (x, y) => (x, model.Height - 1 - y)),
				("rotate_180", (x, y) => (model.Width - 1 - x, model.Height - 1 - y))
			};
			if (model.Width == model.Height)
			{
				transforms.Add(("rotate_90", (x, y) => (model.Height - 1 - y, x)));
				transforms.Add(("rotate_270", (x, y) => (y, model.Width - 1 - x)));
			}

			var spawnSet = spawns.Select(c => $"{c.X},{c.Y}").ToHashSet(StringComparer.Ordinal);
			var colonySet = colonies.Where(c => c.Location != null)
				.Select(c => $"{c.Type}:{c.Location.Value.X},{c.Location.Value.Y}").ToHashSet(StringComparer.OrdinalIgnoreCase);
			var results = new JArray();
			foreach (var (transformName, applyTransform) in transforms)
			{
				var mismatch = 0;
				for (var y = 0; y < model.Height; y++)
					for (var x = 0; x < model.Width; x++)
					{
						var (targetX, targetY) = applyTransform(x, y);
						if (model.Passable[model.IndexLocal(x, y)] != model.Passable[model.IndexLocal(targetX, targetY)])
							mismatch++;
					}

				var transformedSpawns = spawns.Select(c =>
				{
					var (localX, localY) = applyTransform(c.X - model.Left, c.Y - model.Top);
					return $"{localX + model.Left},{localY + model.Top}";
				}).ToHashSet(StringComparer.Ordinal);
				var transformedColonies = colonies.Where(c => c.Location != null).Select(c =>
				{
					var (localX, localY) = applyTransform(c.Location.Value.X - model.Left, c.Location.Value.Y - model.Top);
					return $"{c.Type}:{localX + model.Left},{localY + model.Top}";
				}).ToHashSet(StringComparer.OrdinalIgnoreCase);

				var passabilityMismatch = mismatch / (double)model.CellCount;
				var spawnMismatch = SetMismatch(spawnSet, transformedSpawns);
				var colonyMismatch = SetMismatch(colonySet, transformedColonies);
				results.Add(new JObject
				{
					["transform"] = transformName,
					["passability_mismatch_fraction"] = Round(passabilityMismatch, 6),
					["spawn_mismatch_fraction"] = Round(spawnMismatch, 6),
					["typed_colony_mismatch_fraction"] = Round(colonyMismatch, 6),
					["strict_mismatch_score"] = Round(0.7 * passabilityMismatch + 0.1 * spawnMismatch + 0.2 * colonyMismatch, 6)
				});
			}

			var best = results.Children<JObject>().OrderBy(r => (double)r["strict_mismatch_score"]).First();
			return new JObject
			{
				["strict_definition"] = "Native-cell passability plus exact spawn and typed-colony coordinates under a bounds-relative transform",
				["transforms"] = results,
				["best"] = best.DeepClone()
			};
		}

		static int CrossSectionWidth(PassabilityModel model, List<CPos> path, int pathIndex)
		{
			var previous = path[Math.Max(0, pathIndex - 1)];
			var next = path[Math.Min(path.Count - 1, pathIndex + 1)];
			var dx = Math.Sign(next.X - previous.X);
			var dy = Math.Sign(next.Y - previous.Y);
			var normal = new CVec(-dy, dx);
			if (normal.X == 0 && normal.Y == 0)
				return 1;

			var width = 1;
			for (var sign = -1; sign <= 1; sign += 2)
			{
				for (var step = 1; step <= Math.Max(model.Width, model.Height); step++)
				{
					var cell = path[pathIndex] + new CVec(normal.X * step * sign, normal.Y * step * sign);
					var index = model.IndexOrMinusOne(cell);
					if (index < 0 || !model.Passable[index])
						break;
					width++;
				}
			}

			return width;
		}

		static double SetMismatch(HashSet<string> expected, HashSet<string> actual)
		{
			if (expected.Count == 0 && actual.Count == 0)
				return 0;

			var union = expected.Union(actual, expected.Comparer).Count();
			var intersection = expected.Intersect(actual, expected.Comparer).Count();
			return (union - intersection) / (double)union;
		}

		static JObject AnalyzeMacroGrid(Map map)
		{
			var completeBlocks = 0;
			var sameTemplate = 0;
			var canonicalFrames = 0;
			for (var y = map.Bounds.Top; y + 1 < map.Bounds.Bottom; y += 2)
			{
				for (var x = map.Bounds.Left; x + 1 < map.Bounds.Right; x += 2)
				{
					completeBlocks++;
					var tiles = new[]
					{
						map.Tiles[new CPos(x, y)], map.Tiles[new CPos(x + 1, y)],
						map.Tiles[new CPos(x, y + 1)], map.Tiles[new CPos(x + 1, y + 1)]
					};
					if (tiles.All(t => t.Type == tiles[0].Type))
						sameTemplate++;
					if (tiles[0].Index == 0 && tiles[1].Index == 1 && tiles[2].Index == 2 && tiles[3].Index == 3)
						canonicalFrames++;
				}
			}

			return new JObject
			{
				["origin"] = new JArray(map.Bounds.Left, map.Bounds.Top),
				["complete_block_count"] = completeBlocks,
				["incomplete_right_edge"] = map.Bounds.Width % 2 != 0,
				["incomplete_bottom_edge"] = map.Bounds.Height % 2 != 0,
				["same_template_block_count"] = sameTemplate,
				["same_template_fraction"] = completeBlocks == 0 ? 0 : Round(sameTemplate / (double)completeBlocks, 6),
				["canonical_frame_0123_block_count"] = canonicalFrames,
				["canonical_frame_0123_fraction"] = completeBlocks == 0 ? 0 : Round(canonicalFrames / (double)completeBlocks, 6)
			};
		}

		static JObject AnalyzeTerrainCatalogue(ModData modData)
		{
			var defaultLocomotor = GetGroundLocomotor(modData.DefaultRules);
			var tilesets = new JArray();
			foreach (var terrainEntry in modData.DefaultTerrainInfo.OrderBy(kv => kv.Key, StringComparer.Ordinal))
			{
				var terrain = terrainEntry.Value;
				if (terrain is not ITemplatedTerrainInfo templated)
					continue;

				var terrainTypes = new JArray(terrain.TerrainTypes.Select(t =>
				{
					var passable = defaultLocomotor.TerrainSpeeds.TryGetValue(t.Type, out var speed);
					return new JObject
					{
						["type"] = t.Type,
						["ground_passable"] = passable,
						["ground_speed_percent"] = passable ? speed.Speed : 0,
						["ground_pathing_cost"] = passable ? speed.Cost : null
					};
				}));

				var templates = new JArray();
				foreach (var template in templated.Templates.Values.OrderBy(t => t.Id))
				{
					var tiles = new JArray();
					var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
					for (var i = 0; i < template.TilesCount; i++)
					{
						var tile = template[i];
						if (tile == null)
						{
							tiles.Add(new JObject { ["index"] = i, ["defined"] = false });
							continue;
						}

						var type = terrain.TerrainTypes[tile.TerrainType].Type;
						types.Add(type);
						tiles.Add(new JObject
						{
							["index"] = i,
							["defined"] = true,
							["terrain_type"] = type,
							["height"] = tile.Height,
							["ramp_type"] = tile.RampType
						});
					}

					var homogeneous = types.Count == 1;
					var allDefined = tiles.Children<JObject>().All(t => (bool)t["defined"]);
					var semanticClass = homogeneous ? $"homogeneous_{types.Single().ToLowerInvariant()}" :
						types.Count == 2 ? $"transition_{string.Join("_", types.OrderBy(t => t, StringComparer.OrdinalIgnoreCase)).ToLowerInvariant()}" :
						"mixed_multi_terrain";
					templates.Add(new JObject
					{
						["id"] = template.Id,
						["size"] = new JArray(template.Size.X, template.Size.Y),
						["pick_any"] = template.PickAny,
						["categories"] = new JArray(template.Categories ?? Array.Empty<string>()),
						["tiles"] = tiles,
						["terrain_types"] = new JArray(types.OrderBy(t => t, StringComparer.OrdinalIgnoreCase)),
						["homogeneous"] = homogeneous,
						["semantic_class"] = semanticClass,
						["is_2x2_macro_template"] = template.Size.X == 2 && template.Size.Y == 2 && template.TilesCount == 4,
						["safe_mvp_direct_placement"] = template.Size.X == 2 && template.Size.Y == 2 && template.TilesCount == 4 && allDefined && homogeneous,
						["safe_mvp_reason"] = homogeneous && allDefined ?
							"All four native cells share one terrain type; safe inside a uniform region. Cross-type boundaries still require transition handling." :
							"Requires transition/adjacency handling or contains undefined cells."
					});
				}

				tilesets.Add(new JObject
				{
					["id"] = terrain.Id,
					["terrain_types"] = terrainTypes,
					["template_count"] = templates.Count,
					["templates"] = templates,
					["all_templates_2x2"] = templates.Children<JObject>().All(t => (bool)t["is_2x2_macro_template"]),
					["safe_mvp_template_ids"] = new JArray(templates.Children<JObject>()
						.Where(t => (bool)t["safe_mvp_direct_placement"]).Select(t => (int)t["id"]))
				});
			}

			return new JObject
			{
				["schema_version"] = SchemaVersion,
				["generated_utc"] = DateTime.UtcNow.ToString("O"),
				["extractor"] = $"OpenRA.Utility sa {CommandName}",
				["mvp_policy"] = new JObject
				{
					["safe_direct_placement"] = "Fully defined homogeneous 2x2 templates inside uniform semantic regions only",
					["deferred"] = "Mixed terrain templates until a deterministic semantic transition tiler is specified and validated",
					["native_cell_authority"] = "Template metadata is advisory; final passability must be re-queried per native cell"
				},
				["tilesets"] = tilesets
			};
		}

		static List<ActorRecord> ReadActors(Map map)
		{
			var actors = new List<ActorRecord>();
			foreach (var definition in map.ActorDefinitions)
			{
				var reference = new ActorReference(definition.Value.Value, definition.Value.ToDictionary());
				var location = reference.GetOrDefault<LocationInit>()?.Value;
				var owner = reference.GetOrDefault<OwnerInit>()?.InternalName;
				var occupied = new HashSet<CPos>();
				var isPositionable = false;
				if (map.Rules.Actors.TryGetValue(reference.Type, out var actorInfo))
				{
					isPositionable = actorInfo.HasTraitInfo<IPositionableInfo>();
					if (location != null && !isPositionable)
						foreach (var occupySpace in actorInfo.TraitInfos<IOccupySpaceInfo>())
							foreach (var cell in occupySpace.OccupiedCells(actorInfo, location.Value).Keys)
								if (map.Contains(cell))
									occupied.Add(cell);
				}

				actors.Add(new ActorRecord(definition.Key, reference.Type, owner, location, occupied.ToArray(),
					ColonyActorTypes.Contains(reference.Type), isPositionable));
			}

			return actors;
		}

		static PassabilityModel BuildPassabilityModel(Map map, LocomotorInfo locomotor, List<ActorRecord> actors)
		{
			var model = new PassabilityModel(map.Bounds.Left, map.Bounds.Top, map.Bounds.Width, map.Bounds.Height);
			for (var y = map.Bounds.Top; y < map.Bounds.Bottom; y++)
			{
				for (var x = map.Bounds.Left; x < map.Bounds.Right; x++)
				{
					var cell = new CPos(x, y);
					var index = model.Index(cell);
					var terrainType = map.GetTerrainInfo(cell).Type;
					model.TerrainTypes[index] = terrainType;
					model.TemplateUsage.Increment(map.Tiles[cell].Type);
					if (locomotor.TerrainSpeeds.TryGetValue(terrainType, out var speed))
					{
						model.Passable[index] = true;
						model.TerrainPassableCount++;
						model.Cost[index] = speed.Cost;
					}
				}
			}

			foreach (var cell in actors.SelectMany(a => a.OccupiedCells).Distinct())
			{
				var index = model.IndexOrMinusOne(cell);
				if (index >= 0 && model.Passable[index])
				{
					model.Passable[index] = false;
					model.StaticBlockedCells++;
				}
			}

			return model;
		}

		static ComponentResult CalculateComponents(PassabilityModel model, int minimumClearance = 0, int[] clearance = null)
		{
			var labels = Enumerable.Repeat(-1, model.CellCount).ToArray();
			var sizes = new List<int>();
			var queue = new Queue<int>();
			for (var start = 0; start < model.CellCount; start++)
			{
				if (!model.Passable[start] || labels[start] >= 0 || (clearance != null && clearance[start] < minimumClearance))
					continue;

				var label = sizes.Count;
				var size = 0;
				labels[start] = label;
				queue.Enqueue(start);
				while (queue.Count > 0)
				{
					var current = queue.Dequeue();
					size++;
					foreach (var neighbor in model.NeighborIndexes(current))
					{
						if (!model.Passable[neighbor] || labels[neighbor] >= 0 || (clearance != null && clearance[neighbor] < minimumClearance))
							continue;
						labels[neighbor] = label;
						queue.Enqueue(neighbor);
					}
				}

				sizes.Add(size);
			}

			return new ComponentResult(labels, sizes);
		}

		static int CalculateCoreRegions(PassabilityModel model, int[] clearance, int threshold)
		{
			return CalculateComponents(model, threshold, clearance).Sizes.Count(s => s >= 25);
		}

		static int[] CalculateClearance(PassabilityModel model)
		{
			var distance = Enumerable.Repeat(int.MaxValue, model.CellCount).ToArray();
			var queue = new Queue<int>();
			for (var i = 0; i < model.CellCount; i++)
			{
				var (localX, localY) = model.Local(i);
				if (!model.Passable[i])
				{
					distance[i] = 0;
					queue.Enqueue(i);
				}
				else if (localX == 0 || localY == 0 || localX == model.Width - 1 || localY == model.Height - 1)
				{
					distance[i] = 1;
					queue.Enqueue(i);
				}
			}

			while (queue.Count > 0)
			{
				var current = queue.Dequeue();
				foreach (var neighbor in model.NeighborIndexes(current))
				{
					if (distance[neighbor] <= distance[current] + 1)
						continue;
					distance[neighbor] = distance[current] + 1;
					queue.Enqueue(neighbor);
				}
			}

			return distance;
		}

		static PathResult ShortestPath(PassabilityModel model, CPos start, IEnumerable<CPos> targets, HashSet<int> extraBlocked = null)
		{
			var startIndex = model.IndexOrMinusOne(start);
			var targetIndexes = targets.Select(model.IndexOrMinusOne).Where(i => i >= 0 && model.Passable[i]).ToHashSet();
			if (startIndex < 0 || !model.Passable[startIndex] || targetIndexes.Count == 0 || extraBlocked?.Contains(startIndex) == true)
				return PathResult.Unreachable;

			var distances = Enumerable.Repeat(double.PositiveInfinity, model.CellCount).ToArray();
			var previous = Enumerable.Repeat(-1, model.CellCount).ToArray();
			var queue = new PriorityQueue<int, double>();
			distances[startIndex] = 0;
			queue.Enqueue(startIndex, 0);
			var found = -1;
			while (queue.TryDequeue(out var current, out var priority))
			{
				if (priority > distances[current])
					continue;
				if (targetIndexes.Contains(current))
				{
					found = current;
					break;
				}

				var (currentX, currentY) = model.Local(current);
				foreach (var neighbor in model.NeighborIndexes(current))
				{
					if (!model.Passable[neighbor] || extraBlocked?.Contains(neighbor) == true)
						continue;
					var (neighborX, neighborY) = model.Local(neighbor);
					var diagonal = currentX != neighborX && currentY != neighborY;
					var candidate = distances[current] + model.Cost[neighbor] * (diagonal ? SqrtTwo : 1);
					if (candidate >= distances[neighbor])
						continue;
					distances[neighbor] = candidate;
					previous[neighbor] = current;
					queue.Enqueue(neighbor, candidate);
				}
			}

			if (found < 0)
				return PathResult.Unreachable;

			var cells = new List<CPos>();
			for (var current = found; current >= 0; current = previous[current])
				cells.Add(model.Cell(current));
			cells.Reverse();
			return new PathResult(true, distances[found], cells);
		}

		static CPos[] ColonyAccessCells(PassabilityModel model, ActorRecord colony)
		{
			var occupied = colony.OccupiedCells.Length > 0 ? colony.OccupiedCells : new[] { colony.Location.Value };
			for (var radius = 1; radius <= 5; radius++)
			{
				var candidates = new HashSet<CPos>();
				foreach (var origin in occupied)
					for (var dy = -radius; dy <= radius; dy++)
						for (var dx = -radius; dx <= radius; dx++)
						{
							if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius)
								continue;
							var cell = origin + new CVec(dx, dy);
							var index = model.IndexOrMinusOne(cell);
							if (index >= 0 && model.Passable[index])
								candidates.Add(cell);
						}

				if (candidates.Count > 0)
					return candidates.ToArray();
			}

			return Array.Empty<CPos>();
		}

		static CPos[] CentralRegionCells(PassabilityModel model, int radius)
		{
			var center = new CPos(model.Left + model.Width / 2, model.Top + model.Height / 2);
			var cells = new List<CPos>();
			for (var dy = -radius; dy <= radius; dy++)
				for (var dx = -radius; dx <= radius; dx++)
				{
					if (dx * dx + dy * dy > radius * radius)
						continue;
					var cell = center + new CVec(dx, dy);
					var index = model.IndexOrMinusOne(cell);
					if (index >= 0 && model.Passable[index])
						cells.Add(cell);
				}

			return cells.ToArray();
		}

		static double LocalPassableFraction(PassabilityModel model, CPos center, int radius)
		{
			var passable = 0;
			var total = 0;
			for (var dy = -radius; dy <= radius; dy++)
				for (var dx = -radius; dx <= radius; dx++)
				{
					if (dx * dx + dy * dy > radius * radius)
						continue;
					var index = model.IndexOrMinusOne(center + new CVec(dx, dy));
					if (index < 0)
						continue;
					total++;
					if (model.Passable[index])
						passable++;
				}

			return total == 0 ? 0 : passable / (double)total;
		}

		static int EscapeSectorCount(PassabilityModel model, ComponentResult components, CPos center, int radius)
		{
			var centerIndex = model.IndexOrMinusOne(center);
			if (centerIndex < 0 || components.Labels[centerIndex] < 0)
				return 0;

			var sectors = new bool[8];
			for (var dy = -radius; dy <= radius; dy++)
				for (var dx = -radius; dx <= radius; dx++)
				{
					var distance = Math.Sqrt(dx * dx + dy * dy);
					if (distance < radius - 1 || distance > radius + 0.5)
						continue;
					var index = model.IndexOrMinusOne(center + new CVec(dx, dy));
					if (index < 0 || components.Labels[index] != components.Labels[centerIndex])
						continue;
					var angle = Math.Atan2(dy, dx) + Math.PI;
					sectors[Math.Min(7, (int)(angle / (Math.PI / 4)))] = true;
				}

			return sectors.Count(x => x);
		}

		static int PassableSectorCount(PassabilityModel model, CPos center, int radius)
		{
			var sectors = new bool[8];
			for (var dy = -radius; dy <= radius; dy++)
				for (var dx = -radius; dx <= radius; dx++)
				{
					var distance = Math.Sqrt(dx * dx + dy * dy);
					if (distance < radius - 1 || distance > radius + 0.5)
						continue;
					var index = model.IndexOrMinusOne(center + new CVec(dx, dy));
					if (index < 0 || !model.Passable[index])
						continue;
					var angle = Math.Atan2(dy, dx) + Math.PI;
					sectors[Math.Min(7, (int)(angle / (Math.PI / 4)))] = true;
				}

			return sectors.Count(x => x);
		}

		static LocomotorInfo GetGroundLocomotor(Ruleset rules)
		{
			return rules.Actors[SystemActors.World].TraitInfos<LocomotorInfo>()
				.Single(l => l.Name.Equals("unit", StringComparison.OrdinalIgnoreCase));
		}

		static string ClassifyMap(string directory, Map map, int playableCount, bool hasScript, out string reason)
		{
			var conquest = map.Categories.Any(c => c.Equals("Conquest", StringComparison.OrdinalIgnoreCase));
			var campaign = map.Categories.Any(c => c.Equals("Campaign", StringComparison.OrdinalIgnoreCase));
			var spawnCount = map.ActorDefinitions.Count(a => a.Value.Value.Equals("mpspawn", StringComparison.OrdinalIgnoreCase));
			if (map.Visibility.HasFlag(MapVisibility.Lobby) && conquest && !hasScript && playableCount >= 2 && spawnCount == playableCount)
			{
				reason = "Lobby-visible Conquest map with matching playable slots and mpspawn actors, and no scenario script.";
				return "SKIRMISH_REFERENCE";
			}

			if (map.Visibility.HasFlag(MapVisibility.MissionSelector) && hasScript && directory.StartsWith("Custom_Mission_", StringComparison.OrdinalIgnoreCase))
			{
				reason = "Scripted MissionSelector scenario in the Custom_Mission corpus.";
				return "CUSTOM_CHALLENGE";
			}

			if (map.Visibility.HasFlag(MapVisibility.MissionSelector) && campaign && hasScript)
			{
				reason = "Scripted Campaign-category MissionSelector scenario.";
				return "CAMPAIGN";
			}

			reason = "No honest match in the Phase 1 classification vocabulary; retained for completeness.";
			return "UNKNOWN";
		}

		static JObject ColonyJson(ActorRecord colony)
		{
			return new JObject
			{
				["actor_key"] = colony.Key,
				["type"] = colony.Type,
				["owner"] = colony.Owner,
				["location"] = Point(colony.Location.Value),
				["occupied_cells"] = new JArray(colony.OccupiedCells.Select(Point))
			};
		}

		static JObject Point(CPos cell)
		{
			return new JObject { ["x"] = cell.X, ["y"] = cell.Y };
		}

		static JToken NullableDistance(double value)
		{
			return double.IsFinite(value) ? new JValue(Round(value, 4)) : JValue.CreateNull();
		}

		static Dictionary<string, int> CountBy(IEnumerable<string> values)
		{
			return values.GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
				.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
		}

		static bool HasDefinition(MiniYaml definition)
		{
			return definition != null && (!string.IsNullOrWhiteSpace(definition.Value) || definition.Nodes.Count > 0);
		}

		static string[] CustomRuleActorKeys(Map map)
		{
			if (!HasDefinition(map.RuleDefinitions))
				return Array.Empty<string>();

			var nodes = new List<MiniYamlNode>(map.RuleDefinitions.Nodes);
			if (!string.IsNullOrWhiteSpace(map.RuleDefinitions.Value))
			{
				foreach (var file in FieldLoader.GetValue<string[]>("Rules", map.RuleDefinitions.Value))
				{
					if (!map.Package.Contains(file))
						continue;
					using var stream = map.Package.GetStream(file);
					nodes.AddRange(MiniYaml.FromStream(stream, file));
				}
			}

			return nodes.Select(n => n.Key)
				.Where(k => !k.Equals("World", StringComparison.OrdinalIgnoreCase) &&
					!k.Equals("Player", StringComparison.OrdinalIgnoreCase))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
				.ToArray();
		}

		static string PackageName(OpenRA.FileSystem.IReadOnlyPackage package)
		{
			return Path.GetFileName(package.Name.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		}

		static void WriteJson(string path, JObject value)
		{
			File.WriteAllText(path, value.ToString(Formatting.Indented) + Environment.NewLine);
		}

		static double Euclidean(CPos a, CPos b)
		{
			var dx = a.X - b.X;
			var dy = a.Y - b.Y;
			return Math.Sqrt(dx * dx + dy * dy);
		}

		static double CoefficientOfVariation(double[] values)
		{
			var mean = values.Average();
			if (mean == 0)
				return 0;
			var variance = values.Sum(v => (v - mean) * (v - mean)) / values.Length;
			return Math.Sqrt(variance) / mean;
		}

		static double Round(double value, int digits)
		{
			return Math.Round(value, digits, MidpointRounding.AwayFromZero);
		}

		sealed record ActorRecord(string Key, string Type, string Owner, CPos? Location, CPos[] OccupiedCells,
			bool IsStrategicColony, bool IsPositionable);

		sealed record ComponentResult(int[] Labels, List<int> Sizes);

		sealed record PathResult(bool Reachable, double Cost, List<CPos> Cells)
		{
			public static readonly PathResult Unreachable = new(false, double.PositiveInfinity, new List<CPos>());
		}

		sealed class PassabilityModel
		{
			public readonly int Left;
			public readonly int Top;
			public readonly int Width;
			public readonly int Height;
			public readonly bool[] Passable;
			public readonly short[] Cost;
			public readonly string[] TerrainTypes;
			public readonly Dictionary<ushort, int> TemplateUsage = new();
			public int TerrainPassableCount;
			public int StaticBlockedCells;

			public PassabilityModel(int left, int top, int width, int height)
			{
				Left = left;
				Top = top;
				Width = width;
				Height = height;
				Passable = new bool[CellCount];
				Cost = new short[CellCount];
				TerrainTypes = new string[CellCount];
			}

			public int CellCount => Width * Height;

			public int Index(CPos cell) => IndexLocal(cell.X - Left, cell.Y - Top);

			public int IndexLocal(int x, int y) => y * Width + x;

			public int IndexOrMinusOne(CPos cell)
			{
				var x = cell.X - Left;
				var y = cell.Y - Top;
				return x < 0 || y < 0 || x >= Width || y >= Height ? -1 : IndexLocal(x, y);
			}

			public (int X, int Y) Local(int index) => (index % Width, index / Width);

			public CPos Cell(int index)
			{
				var (x, y) = Local(index);
				return new CPos(x + Left, y + Top);
			}

			public IEnumerable<int> NeighborIndexes(int index)
			{
				var (localX, localY) = Local(index);
				foreach (var direction in Directions)
				{
					var x = localX + direction.X;
					var y = localY + direction.Y;
					if (x >= 0 && y >= 0 && x < Width && y < Height)
						yield return IndexLocal(x, y);
				}
			}
		}
	}

	static class RmgAuditDictionaryExtensions
	{
		public static void Increment<TKey>(this Dictionary<TKey, int> dictionary, TKey key)
		{
			dictionary.TryGetValue(key, out var value);
			dictionary[key] = value + 1;
		}
	}
}
