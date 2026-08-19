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
using OpenRA.Mods.Common.Traits;
using OpenRA.FileSystem;
using OpenRA.Traits;

namespace OpenRA.Mods.OpenSA.UtilityCommands
{
	/// <summary>
	/// Extracts read-only spatial evidence for the RMG Phase 7A battlefield-layout audit.
	/// No map or gameplay definition is modified.
	/// </summary>
	sealed class AuditRmgBattlefieldLayoutsCommand : IUtilityCommand
	{
		const string SchemaVersion = "1.0";
		const string CommandName = "--audit-rmg-battlefield-layouts";
		const int SectorGridSize = 4;
		const int RouteBandRadius = 8;
		const double EdgeBandFraction = 0.15;
		const double MeaningfulSectorFraction = 0.02;
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

		static readonly string[] ReferenceMapNames =
		{
			"Candyland Battleground", "Desert Battleground", "Narrow Passage", "Swamp Battleground"
		};


		static readonly HashSet<ushort> EmbeddedDetailTemplateIds = new()
		{
			61, 62, 93, 94
		};
		string IUtilityCommand.Name => CommandName;

		bool IUtilityCommand.ValidateArguments(string[] args)
		{
			return args.Length >= 2;
		}

		[Desc("OUTPUT-DIRECTORY", "Audit shipped battlefield layouts and decoration coverage as JSON. No maps are modified.")]
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

			var maps = new List<MapAudit>();
			foreach (var preview in previews)
			{
				using var map = new Map(utility.ModData, preview.Package);
				maps.Add(AnalyzeMap(map, PackageName(preview.Package)));
			}

			for (var i = 2; i < args.Length; i++)
			{
				var packagePath = Path.GetFullPath(args[i]);
				if (!ZipFileLoader.TryParseReadWritePackage(packagePath, out var package))
					throw new InvalidDataException($"Unable to open generated comparison map '{packagePath}'.");

				using (package)
				using (var map = new Map(utility.ModData, package))
				{
					maps.Add(AnalyzeMap(map, Path.GetFileNameWithoutExtension(packagePath), "GENERATED_PROTOTYPE"));
				}
			}

			var classificationCounts = new JObject();
			foreach (var group in maps.GroupBy(m => m.Classification).OrderBy(g => g.Key, StringComparer.Ordinal))
				classificationCounts[group.Key] = group.Count();

			var tilesetCounts = new JObject();
			foreach (var group in maps.GroupBy(m => m.Tileset).OrderBy(g => g.Key, StringComparer.Ordinal))
				tilesetCounts[group.Key] = group.Count();

			var report = new JObject
			{
				["schema_version"] = SchemaVersion,
				["generated_utc"] = DateTime.UtcNow.ToString("O"),
				["extractor"] = $"OpenRA.Utility sa {CommandName}",
				["scope"] = $"all available shipped System-class maps plus {Math.Max(0, args.Length - 2)} explicitly supplied generated comparison maps",
				["map_count"] = maps.Count,
				["classification_counts"] = classificationCounts,
				["tileset_counts"] = tilesetCounts,
				["methodology"] = new JObject
				{
					["spatial_grid"] = $"{SectorGridSize}x{SectorGridSize} normalized sectors over map bounds",
					["edge_band"] = $"outer {EdgeBandFraction:P0} of normalized map depth",
					["central_half"] = "normalized x and y both within [0.25, 0.75]",
					["meaningful_surface_sector"] = $"surface occupies at least {MeaningfulSectorFraction:P0} of sector cells",
					["movement_modifier_terrain"] = "Rock (unit speed 75) plus Vegetation (unit speed 50); Clear speed is 100",
					["decoration_actors"] = "actor types beginning plant_; passable and blocking footprints are reported separately",
					["embedded_detail_stamps"] = "canonical 2x2 fixed visual-detail templates 61, 62, 93, and 94; reported separately from plant_ actors",
					["traffic_evidence"] = "terrain-neutral geometric shortest paths between skirmish spawns and from each spawn to each strategic colony, expanded by eight cells; terrain costs are intentionally excluded to avoid circularly routing around the feature being measured",
					["traffic_scope_limit"] = "scripted campaign/custom objectives cannot be inferred reliably from static map packages; their spatial terrain and decoration evidence is included, but traffic metrics are intentionally unavailable",
					["static_blockers_in_traffic"] = "blocking plant_ decoration footprints only; transient units and scenario structures are not treated as permanent blockers"
				},
				["aggregates_by_classification"] = GroupAggregates(maps, m => m.Classification),
				["aggregates_by_tileset"] = GroupAggregates(maps, m => m.Tileset),
				["reference_maps"] = new JArray(maps.Where(m => ReferenceMapNames.Any(name => MatchesName(m, name)))
					.Select(m => ReferenceSummary(m))),
				["maps"] = new JArray(maps.Select(m => m.Json))
			};

			var outputPath = Path.Combine(outputDirectory, "battlefield_layout_audit.json");
			File.WriteAllText(outputPath, report.ToString(Formatting.Indented) + Environment.NewLine);
			Console.WriteLine($"Audited {previews.Length} shipped battlefield layouts and {Math.Max(0, args.Length - 2)} generated comparisons.");
			Console.WriteLine($"Campaign={maps.Count(m => m.Classification == "CAMPAIGN")}, Custom={maps.Count(m => m.Classification == "CUSTOM_CHALLENGE")}, Skirmish={maps.Count(m => m.Classification == "SKIRMISH_REFERENCE")}, Other={maps.Count(m => m.Classification == "UNKNOWN")}");
			Console.WriteLine($"Generated={maps.Count(m => m.Classification == "GENERATED_PROTOTYPE")}");
			Console.WriteLine(outputPath);
		}

		static MapAudit AnalyzeMap(Map map, string directory, string classificationOverride = null)
		{
			var players = new MapPlayers(map.PlayerDefinitions).Players.Values.ToArray();
			var playablePlayers = players.Where(p => p.Playable).ToArray();
			var hasScript = map.Package.Contains("script.lua");
			var classification = classificationOverride ?? ClassifyMap(directory, map, playablePlayers.Length, hasScript);
			var actors = ReadActors(map);
			var spawns = actors.Where(a => a.Type.Equals("mpspawn", StringComparison.OrdinalIgnoreCase)).ToArray();
			var colonies = actors.Where(a => ColonyActorTypes.Contains(a.Type)).ToArray();
			var decorations = actors.Where(a => a.Type.StartsWith("plant_", StringComparison.OrdinalIgnoreCase)).ToArray();
			var locomotor = map.Rules.Actors[SystemActors.World].TraitInfos<LocomotorInfo>()
				.Single(l => l.Name.Equals("unit", StringComparison.OrdinalIgnoreCase));
			var grid = BuildGrid(map, locomotor, decorations);

			var clear = AnalyzeSurface(grid, type => type.Equals("Clear", StringComparison.OrdinalIgnoreCase));
			var rock = AnalyzeSurface(grid, type => type.Equals("Rock", StringComparison.OrdinalIgnoreCase));
			var vegetation = AnalyzeSurface(grid, type => type.Equals("Vegetation", StringComparison.OrdinalIgnoreCase));
			var water = AnalyzeSurface(grid, type => type.Equals("Water", StringComparison.OrdinalIgnoreCase));
			var movement = AnalyzeSurface(grid, IsMovementModifier);
			var decoration = AnalyzeDecorations(grid, decorations);
			var embeddedDetails = AnalyzeEmbeddedDetails(map, grid);
			var traffic = classification == "SKIRMISH_REFERENCE" || classification == "GENERATED_PROTOTYPE" ?
				AnalyzeTraffic(grid, spawns, colonies, decorations) : null;

			var json = new JObject
			{
				["directory"] = directory,
				["uid"] = map.Uid,
				["title"] = map.Title,
				["classification"] = classification,
				["tileset"] = map.Tileset,
				["has_script"] = hasScript,
				["bounds"] = new JObject
				{
					["left"] = map.Bounds.Left,
					["top"] = map.Bounds.Top,
					["width"] = map.Bounds.Width,
					["height"] = map.Bounds.Height
				},
				["playable_cell_count"] = grid.CellCount,
				["anchor_counts"] = new JObject
				{
					["spawns"] = spawns.Length,
					["strategic_colonies"] = colonies.Length
				},
				["terrain_layout"] = new JObject
				{
					["clear"] = clear.Json,
					["rock"] = rock.Json,
					["vegetation"] = vegetation.Json,
					["water"] = water.Json,
					["movement_modifier_combined"] = movement.Json
				},
				["decoration_layout"] = decoration.Json,
				["embedded_detail_layout"] = embeddedDetails.Json,
				["traffic_layout"] = traffic?.Json,
				["traffic_layout_status"] = traffic == null ? "UNAVAILABLE_SCRIPTED_OR_NON_REFERENCE" :
					classification == "GENERATED_PROTOTYPE" ? "AVAILABLE_GENERATED_STATIC" : "AVAILABLE_SKIRMISH_STATIC"
			};

			return new MapAudit(directory, map.Title, classification, map.Tileset, json,
				movement.CellFraction, movement.EdgeFraction, movement.CentralFraction, movement.SectorCoverage,
				decoration.DensityPerThousand, decoration.SectorCoverage, decoration.BlockingFraction,
				embeddedDetails.DensityPerThousand, embeddedDetails.SectorCoverage,
				traffic?.MovementFraction, traffic?.MovementEnrichment);
		}

		static GridModel BuildGrid(Map map, LocomotorInfo locomotor, ActorPoint[] decorations)
		{
			var grid = new GridModel(map.Bounds.Left, map.Bounds.Top, map.Bounds.Width, map.Bounds.Height);
			for (var y = map.Bounds.Top; y < map.Bounds.Bottom; y++)
			{
				for (var x = map.Bounds.Left; x < map.Bounds.Right; x++)
				{
					var cell = new CPos(x, y);
					var index = grid.Index(cell);
					var terrainType = map.GetTerrainInfo(cell).Type;
					grid.TerrainTypes[index] = terrainType;
					if (locomotor.TerrainSpeeds.TryGetValue(terrainType, out _))
					{
						grid.Passable[index] = true;
					}
				}
			}

			foreach (var cell in decorations.SelectMany(d => d.OccupiedCells).Distinct())
			{
				var index = grid.IndexOrMinusOne(cell);
				if (index >= 0)
					grid.Passable[index] = false;
			}

			return grid;
		}

		static SurfaceMetric AnalyzeSurface(GridModel grid, Func<string, bool> predicate)
		{
			var sectorCounts = new int[SectorGridSize * SectorGridSize];
			var sectorCellCounts = new int[sectorCounts.Length];
			var cellCount = 0;
			var edgeCount = 0;
			var centralCount = 0;
			var borderDepth = 0.0;
			for (var i = 0; i < grid.CellCount; i++)
			{
				var sector = grid.Sector(i);
				sectorCellCounts[sector]++;
				if (!predicate(grid.TerrainTypes[i]))
					continue;

				cellCount++;
				sectorCounts[sector]++;
				var depth = grid.NormalizedBorderDepth(i);
				borderDepth += depth;
				if (depth <= EdgeBandFraction)
					edgeCount++;
				if (grid.IsCentralHalf(i))
					centralCount++;
			}

			var sectorsWithAny = sectorCounts.Count(c => c > 0);
			var meaningfulSectors = Enumerable.Range(0, sectorCounts.Length)
				.Count(i => sectorCellCounts[i] > 0 && sectorCounts[i] / (double)sectorCellCounts[i] >= MeaningfulSectorFraction);
			var cellFraction = cellCount / (double)grid.CellCount;
			var edgeFraction = cellCount == 0 ? 0 : edgeCount / (double)cellCount;
			var centralFraction = cellCount == 0 ? 0 : centralCount / (double)cellCount;
			var sectorCoverage = sectorsWithAny / (double)sectorCounts.Length;
			var json = new JObject
			{
				["cell_count"] = cellCount,
				["cell_fraction"] = Round(cellFraction),
				["edge_band_fraction_of_surface"] = Round(edgeFraction),
				["central_half_fraction_of_surface"] = Round(centralFraction),
				["mean_normalized_border_depth"] = cellCount == 0 ? 0 : Round(borderDepth / cellCount),
				["sectors_with_any"] = sectorsWithAny,
				["sectors_with_meaningful_coverage"] = meaningfulSectors,
				["sector_coverage_fraction"] = Round(sectorCoverage),
				["sector_cell_counts"] = new JArray(sectorCounts)
			};

			return new SurfaceMetric(json, cellCount, cellFraction, edgeFraction, centralFraction, sectorCoverage);
		}

		static EmbeddedDetailMetric AnalyzeEmbeddedDetails(Map map, GridModel grid)
		{
			var anchors = new List<(CPos Location, ushort Template)>();
			for (var y = grid.Top; y < grid.Top + grid.Height - 1; y++)
			{
				for (var x = grid.Left; x < grid.Left + grid.Width - 1; x++)
				{
					var cell = new CPos(x, y);
					var tile = map.Tiles[cell];
					if (tile.Index != 0 || !EmbeddedDetailTemplateIds.Contains(tile.Type) ||
						!MatchesTile(map, cell + new CVec(1, 0), tile.Type, 1) ||
						!MatchesTile(map, cell + new CVec(0, 1), tile.Type, 2) ||
						!MatchesTile(map, cell + new CVec(1, 1), tile.Type, 3))
						continue;

					anchors.Add((cell, tile.Type));
				}
			}

			var sectorCounts = new int[SectorGridSize * SectorGridSize];
			var edgeCount = 0;
			var centralCount = 0;
			foreach (var anchor in anchors)
			{
				var index = grid.Index(anchor.Location);
				sectorCounts[grid.Sector(index)]++;
				if (grid.NormalizedBorderDepth(index) <= EdgeBandFraction)
					edgeCount++;
				if (grid.IsCentralHalf(index))
					centralCount++;
			}

			var density = anchors.Count * 1000.0 / grid.CellCount;
			var coverage = sectorCounts.Count(c => c > 0) / (double)sectorCounts.Length;
			var json = new JObject
			{
				["canonical_stamp_count"] = anchors.Count,
				["density_per_1000_cells"] = Round(density),
				["edge_band_fraction_of_stamps"] = anchors.Count == 0 ? 0 : Round(edgeCount / (double)anchors.Count),
				["central_half_fraction_of_stamps"] = anchors.Count == 0 ? 0 : Round(centralCount / (double)anchors.Count),
				["sectors_with_any"] = sectorCounts.Count(c => c > 0),
				["sector_coverage_fraction"] = Round(coverage),
				["sector_stamp_counts"] = new JArray(sectorCounts),
				["by_template"] = JObject.FromObject(anchors.GroupBy(a => a.Template).OrderBy(g => g.Key)
					.ToDictionary(g => g.Key.ToString(), g => g.Count()))
			};

			return new EmbeddedDetailMetric(json, density, coverage);
		}

		static bool MatchesTile(Map map, CPos cell, ushort type, byte index)
		{
			if (!map.Contains(cell))
				return false;
			var tile = map.Tiles[cell];
			return tile.Type == type && tile.Index == index;
		}

		static DecorationMetric AnalyzeDecorations(GridModel grid, ActorPoint[] decorations)

		{
			var sectorCounts = new int[SectorGridSize * SectorGridSize];
			var edgeCount = 0;
			var centralCount = 0;
			var valid = decorations.Where(d => grid.IndexOrMinusOne(d.Location) >= 0).ToArray();
			foreach (var decoration in valid)
			{
				var index = grid.Index(decoration.Location);
				sectorCounts[grid.Sector(index)]++;
				if (grid.NormalizedBorderDepth(index) <= EdgeBandFraction)
					edgeCount++;
				if (grid.IsCentralHalf(index))
					centralCount++;
			}

			var blocking = valid.Count(d => d.OccupiedCells.Length > 0);
			var density = valid.Length * 1000.0 / grid.CellCount;
			var coverage = sectorCounts.Count(c => c > 0) / (double)sectorCounts.Length;
			var blockingFraction = valid.Length == 0 ? 0 : blocking / (double)valid.Length;
			var json = new JObject
			{
				["actor_count"] = valid.Length,
				["density_per_1000_cells"] = Round(density),
				["blocking_actor_count"] = blocking,
				["passable_actor_count"] = valid.Length - blocking,
				["blocking_fraction"] = Round(blockingFraction),
				["edge_band_fraction_of_actors"] = valid.Length == 0 ? 0 : Round(edgeCount / (double)valid.Length),
				["central_half_fraction_of_actors"] = valid.Length == 0 ? 0 : Round(centralCount / (double)valid.Length),
				["sectors_with_any"] = sectorCounts.Count(c => c > 0),
				["sector_coverage_fraction"] = Round(coverage),
				["sector_actor_counts"] = new JArray(sectorCounts),
				["by_type"] = JObject.FromObject(CountBy(valid.Select(d => d.Type))),
				["blocking_by_type"] = JObject.FromObject(CountBy(valid.Where(d => d.OccupiedCells.Length > 0).Select(d => d.Type))),
				["passable_by_type"] = JObject.FromObject(CountBy(valid.Where(d => d.OccupiedCells.Length == 0).Select(d => d.Type))),
				["by_native_terrain"] = JObject.FromObject(CountBy(valid.Select(d => grid.TerrainTypes[grid.Index(d.Location)])))
			};

			return new DecorationMetric(json, density, coverage, blockingFraction);
		}

		static TrafficMetric AnalyzeTraffic(GridModel grid, ActorPoint[] spawns, ActorPoint[] colonies, ActorPoint[] decorations)
		{
			var routeMask = new bool[grid.CellCount];
			var routeCount = 0;
			var unreachableCount = 0;
			for (var i = 0; i < spawns.Length; i++)
			{
				for (var j = i + 1; j < spawns.Length; j++)
					AddRoute(grid, spawns[i].Location, new[] { spawns[j].Location }, routeMask, ref routeCount, ref unreachableCount);

				foreach (var colony in colonies)
					AddRoute(grid, spawns[i].Location, TargetNeighborhood(grid, colony.Location, 3), routeMask, ref routeCount, ref unreachableCount);
			}

			var routeBandCellCount = 0;
			var movementCellCount = 0;
			for (var i = 0; i < grid.CellCount; i++)
			{
				if (!routeMask[i] || !grid.Passable[i])
					continue;
				routeBandCellCount++;
				if (IsMovementModifier(grid.TerrainTypes[i]))
					movementCellCount++;
			}

			var totalMovementCells = grid.TerrainTypes.Count(IsMovementModifier);
			var totalMovementFraction = totalMovementCells / (double)grid.CellCount;
			var movementFraction = routeBandCellCount == 0 ? 0 : movementCellCount / (double)routeBandCellCount;
			var movementEnrichment = totalMovementFraction == 0 ? 0 : movementFraction / totalMovementFraction;
			var decorationInBand = decorations.Count(d =>
			{
				var index = grid.IndexOrMinusOne(d.Location);
				return index >= 0 && routeMask[index];
			});
			var json = new JObject
			{
				["route_count"] = routeCount,
				["unreachable_route_count"] = unreachableCount,
				["route_band_radius_cells"] = RouteBandRadius,
				["route_band_cell_count"] = routeBandCellCount,
				["route_band_fraction_of_map"] = Round(routeBandCellCount / (double)grid.CellCount),
				["movement_modifier_cell_count_in_route_band"] = movementCellCount,
				["movement_modifier_fraction_in_route_band"] = Round(movementFraction),
				["movement_modifier_capture_fraction"] = totalMovementCells == 0 ? 0 : Round(movementCellCount / (double)totalMovementCells),
				["movement_modifier_enrichment_ratio"] = Round(movementEnrichment),
				["decoration_actor_count_in_route_band"] = decorationInBand,
				["decoration_actor_capture_fraction"] = decorations.Length == 0 ? 0 : Round(decorationInBand / (double)decorations.Length)
			};

			return new TrafficMetric(json, movementFraction, movementEnrichment);
		}

		static void AddRoute(GridModel grid, CPos start, IEnumerable<CPos> targets, bool[] routeMask,
			ref int routeCount, ref int unreachableCount)
		{
			var path = ShortestPath(grid, start, targets);
			if (path == null)
			{
				unreachableCount++;
				return;
			}

			routeCount++;
			foreach (var cell in path)
			{
				for (var dy = -RouteBandRadius; dy <= RouteBandRadius; dy++)
				{
					for (var dx = -RouteBandRadius; dx <= RouteBandRadius; dx++)
					{
						if (dx * dx + dy * dy > RouteBandRadius * RouteBandRadius)
							continue;
						var index = grid.IndexOrMinusOne(cell + new CVec(dx, dy));
						if (index >= 0)
							routeMask[index] = true;
					}
				}
			}
		}

		static List<CPos> ShortestPath(GridModel grid, CPos start, IEnumerable<CPos> targets)
		{
			var startIndex = grid.IndexOrMinusOne(start);
			var targetIndexes = targets.Select(grid.IndexOrMinusOne).Where(i => i >= 0 && grid.Passable[i]).ToHashSet();
			if (startIndex < 0 || !grid.Passable[startIndex] || targetIndexes.Count == 0)
				return null;

			var distances = Enumerable.Repeat(double.PositiveInfinity, grid.CellCount).ToArray();
			var previous = Enumerable.Repeat(-1, grid.CellCount).ToArray();
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

				var (currentX, currentY) = grid.Local(current);
				foreach (var neighbor in grid.NeighborIndexes(current))
				{
					if (!grid.Passable[neighbor])
						continue;
					var (neighborX, neighborY) = grid.Local(neighbor);
					var diagonal = currentX != neighborX && currentY != neighborY;
					var candidate = distances[current] + (diagonal ? SqrtTwo : 1);
					if (candidate >= distances[neighbor])
						continue;
					distances[neighbor] = candidate;
					previous[neighbor] = current;
					queue.Enqueue(neighbor, candidate);
				}
			}

			if (found < 0)
				return null;

			var path = new List<CPos>();
			for (var current = found; current >= 0; current = previous[current])
				path.Add(grid.Cell(current));
			path.Reverse();
			return path;
		}

		static CPos[] TargetNeighborhood(GridModel grid, CPos center, int radius)
		{
			var targets = new HashSet<CPos>();
			for (var dy = -radius; dy <= radius; dy++)
			{
				for (var dx = -radius; dx <= radius; dx++)
				{
					if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius)
						continue;
					var cell = center + new CVec(dx, dy);
					var index = grid.IndexOrMinusOne(cell);
					if (index >= 0 && grid.Passable[index])
						targets.Add(cell);
				}
			}

			return targets.ToArray();
		}

		static ActorPoint[] ReadActors(Map map)
		{
			var actors = new List<ActorPoint>();
			foreach (var definition in map.ActorDefinitions)
			{
				var reference = new ActorReference(definition.Value.Value, definition.Value.ToDictionary());
				var location = reference.GetOrDefault<LocationInit>()?.Value;
				if (location == null)
					continue;
				var owner = reference.GetOrDefault<OwnerInit>()?.InternalName;
				var occupied = new HashSet<CPos>();
				if (map.Rules.Actors.TryGetValue(reference.Type, out var actorInfo))
				{
					foreach (var occupySpace in actorInfo.TraitInfos<IOccupySpaceInfo>())
					{
						foreach (var cell in occupySpace.OccupiedCells(actorInfo, location.Value).Keys)
							if (map.Contains(cell))
								occupied.Add(cell);
					}
				}

				actors.Add(new ActorPoint(reference.Type, owner, location.Value, occupied.ToArray()));
			}

			return actors.ToArray();
		}

		static JObject GroupAggregates(IEnumerable<MapAudit> maps, Func<MapAudit, string> selector)
		{
			var result = new JObject();
			foreach (var group in maps.GroupBy(selector).OrderBy(g => g.Key, StringComparer.Ordinal))
			{
				result[group.Key] = new JObject
				{
					["map_count"] = group.Count(),
					["movement_modifier_cell_fraction_median"] = Round(Median(group.Select(m => m.MovementCellFraction))),
					["movement_modifier_edge_fraction_median"] = Round(Median(group.Select(m => m.MovementEdgeFraction))),
					["movement_modifier_central_fraction_median"] = Round(Median(group.Select(m => m.MovementCentralFraction))),
					["movement_modifier_sector_coverage_median"] = Round(Median(group.Select(m => m.MovementSectorCoverage))),
					["decoration_density_per_1000_cells_median"] = Round(Median(group.Select(m => m.DecorationDensity))),
					["decoration_sector_coverage_median"] = Round(Median(group.Select(m => m.DecorationSectorCoverage))),
					["blocking_decoration_fraction_median"] = Round(Median(group.Select(m => m.BlockingDecorationFraction))),
					["embedded_detail_density_per_1000_cells_median"] = Round(Median(group.Select(m => m.EmbeddedDetailDensity))),
					["embedded_detail_sector_coverage_median"] = Round(Median(group.Select(m => m.EmbeddedDetailSectorCoverage))),
					["traffic_movement_modifier_fraction_median"] = NullableMedian(group.Select(m => m.TrafficMovementFraction)),
					["traffic_movement_enrichment_median"] = NullableMedian(group.Select(m => m.TrafficMovementEnrichment))
				};
			}

			return result;
		}

		static JObject ReferenceSummary(MapAudit map)
		{
			return new JObject
			{
				["directory"] = map.Directory,
				["title"] = map.Title,
				["classification"] = map.Classification,
				["tileset"] = map.Tileset,
				["movement_modifier_cell_fraction"] = Round(map.MovementCellFraction),
				["movement_modifier_edge_fraction"] = Round(map.MovementEdgeFraction),
				["movement_modifier_central_fraction"] = Round(map.MovementCentralFraction),
				["movement_modifier_sector_coverage"] = Round(map.MovementSectorCoverage),
				["decoration_density_per_1000_cells"] = Round(map.DecorationDensity),
				["decoration_sector_coverage"] = Round(map.DecorationSectorCoverage),
				["blocking_decoration_fraction"] = Round(map.BlockingDecorationFraction),
				["embedded_detail_density_per_1000_cells"] = Round(map.EmbeddedDetailDensity),
				["embedded_detail_sector_coverage"] = Round(map.EmbeddedDetailSectorCoverage),
				["traffic_movement_modifier_fraction"] = map.TrafficMovementFraction,
				["traffic_movement_enrichment"] = map.TrafficMovementEnrichment
			};
		}

		static bool MatchesName(MapAudit map, string name)
		{
			var normalized = name.Replace(" ", "_", StringComparison.Ordinal);
			return map.Title.Equals(name, StringComparison.OrdinalIgnoreCase) ||
				map.Directory.Contains(normalized, StringComparison.OrdinalIgnoreCase);
		}

		static string ClassifyMap(string directory, Map map, int playableCount, bool hasScript)
		{
			var conquest = map.Categories.Any(c => c.Equals("Conquest", StringComparison.OrdinalIgnoreCase));
			var campaign = map.Categories.Any(c => c.Equals("Campaign", StringComparison.OrdinalIgnoreCase));
			var spawnCount = map.ActorDefinitions.Count(a => a.Value.Value.Equals("mpspawn", StringComparison.OrdinalIgnoreCase));
			if (map.Visibility.HasFlag(MapVisibility.Lobby) && conquest && !hasScript && playableCount >= 2 && spawnCount == playableCount)
				return "SKIRMISH_REFERENCE";
			if (map.Visibility.HasFlag(MapVisibility.MissionSelector) && hasScript && directory.StartsWith("Custom_Mission_", StringComparison.OrdinalIgnoreCase))
				return "CUSTOM_CHALLENGE";
			if (map.Visibility.HasFlag(MapVisibility.MissionSelector) && campaign && hasScript)
				return "CAMPAIGN";
			return "UNKNOWN";
		}

		static bool IsMovementModifier(string terrainType)
		{
			return terrainType.Equals("Rock", StringComparison.OrdinalIgnoreCase) ||
				terrainType.Equals("Vegetation", StringComparison.OrdinalIgnoreCase);
		}

		static Dictionary<string, int> CountBy(IEnumerable<string> values)
		{
			return values.GroupBy(v => v ?? "<unspecified>", StringComparer.OrdinalIgnoreCase)
				.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
		}

		static JToken NullableMedian(IEnumerable<double?> values)
		{
			var finite = values.Where(v => v != null).Select(v => v.Value).ToArray();
			return finite.Length == 0 ? JValue.CreateNull() : new JValue(Round(Median(finite)));
		}

		static double Median(IEnumerable<double> values)
		{
			var ordered = values.OrderBy(v => v).ToArray();
			if (ordered.Length == 0)
				return 0;
			var middle = ordered.Length / 2;
			return ordered.Length % 2 == 0 ? (ordered[middle - 1] + ordered[middle]) / 2 : ordered[middle];
		}

		static double Round(double value)
		{
			return Math.Round(value, 6, MidpointRounding.AwayFromZero);
		}

		static string PackageName(OpenRA.FileSystem.IReadOnlyPackage package)
		{
			return Path.GetFileName(package.Name.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		}

		sealed record ActorPoint(string Type, string Owner, CPos Location, CPos[] OccupiedCells);

		sealed record SurfaceMetric(JObject Json, int CellCount, double CellFraction, double EdgeFraction,
			double CentralFraction, double SectorCoverage);

		sealed record DecorationMetric(JObject Json, double DensityPerThousand, double SectorCoverage,
			double BlockingFraction);

		sealed record TrafficMetric(JObject Json, double MovementFraction, double MovementEnrichment);

		sealed record EmbeddedDetailMetric(JObject Json, double DensityPerThousand, double SectorCoverage);

		sealed record MapAudit(string Directory, string Title, string Classification, string Tileset, JObject Json,
			double MovementCellFraction, double MovementEdgeFraction, double MovementCentralFraction,
			double MovementSectorCoverage, double DecorationDensity, double DecorationSectorCoverage,
			double BlockingDecorationFraction, double EmbeddedDetailDensity, double EmbeddedDetailSectorCoverage,
			double? TrafficMovementFraction, double? TrafficMovementEnrichment);

		sealed class GridModel
		{
			public readonly int Left;
			public readonly int Top;
			public readonly int Width;

			public readonly int Height;
			public readonly bool[] Passable;
			public readonly string[] TerrainTypes;

			public GridModel(int left, int top, int width, int height)
			{
				Left = left;
				Top = top;
				Width = width;
				Height = height;
				Passable = new bool[CellCount];
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

			public int Sector(int index)
			{
				var (x, y) = Local(index);
				var sectorX = Math.Min(SectorGridSize - 1, x * SectorGridSize / Width);
				var sectorY = Math.Min(SectorGridSize - 1, y * SectorGridSize / Height);
				return sectorY * SectorGridSize + sectorX;
			}

			public double NormalizedBorderDepth(int index)
			{
				var (x, y) = Local(index);
				var normalizedX = Width <= 1 ? 0 : x / (double)(Width - 1);
				var normalizedY = Height <= 1 ? 0 : y / (double)(Height - 1);
				return Math.Min(Math.Min(normalizedX, 1 - normalizedX), Math.Min(normalizedY, 1 - normalizedY));
			}

			public bool IsCentralHalf(int index)
			{
				var (x, y) = Local(index);
				var normalizedX = Width <= 1 ? 0.5 : x / (double)(Width - 1);
				var normalizedY = Height <= 1 ? 0.5 : y / (double)(Height - 1);
				return normalizedX >= 0.25 && normalizedX <= 0.75 && normalizedY >= 0.25 && normalizedY <= 0.75;
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
}
