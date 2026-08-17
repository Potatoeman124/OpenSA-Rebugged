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

namespace OpenRA.Mods.OpenSA.UtilityCommands
{
	/// <summary>
	/// Extracts read-only NORMAL land-surface evidence for the RMG Phase 6A audit.
	/// Original sprites are resolved by the installed content packages but are never copied.
	/// </summary>
	sealed class AuditRmgNormalLandSurfacesCommand : IUtilityCommand
	{
		const string SchemaVersion = "1.0";
		const string CommandName = "--audit-rmg-normal-land-surfaces";
		const string Tileset = "NORMAL";

		static readonly string[] SurfaceTerrainTypes = { "Clear", "Rock", "Vegetation" };
		static readonly CVec[] CardinalDirections =
		{
			new(-1, 0), new(1, 0), new(0, -1), new(0, 1)
		};

		static readonly CVec[] NeighborDirections =
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

		[Desc("OUTPUT-DIRECTORY", "Audit NORMAL Clear, Rock, and Vegetation template usage and spatial patterns as JSON. No maps or sprites are modified or copied.")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			Game.ModData = utility.ModData;
			var outputDirectory = Path.GetFullPath(args[1]);
			Directory.CreateDirectory(outputDirectory);

			if (!utility.ModData.DefaultTerrainInfo.TryGetValue(Tileset, out var terrainInfo) ||
				terrainInfo is not ITemplatedTerrainInfo templated)
				throw new InvalidDataException($"Tileset '{Tileset}' is missing or is not templated.");

			var locomotor = GetGroundLocomotor(utility.ModData.DefaultRules);
			var templates = templated.Templates.Values
				.Where(AuditedTemplate)
				.OrderBy(t => t.Id)
				.ToDictionary(t => t.Id, t => new TemplateAggregate(t, terrainInfo));
			var horizontalAdjacency = new Dictionary<(ushort Left, ushort Right), int>();
			var verticalAdjacency = new Dictionary<(ushort Top, ushort Bottom), int>();

			utility.ModData.MapCache.LoadMaps();
			var maps = new JArray();
			foreach (var preview in utility.ModData.MapCache
				.Where(p => p.Class == MapClassification.System && p.Status == MapStatus.Available)
				.OrderBy(p => PackageName(p.Package), StringComparer.OrdinalIgnoreCase))
			{
				using var map = new Map(utility.ModData, preview.Package);
				if (!map.Tileset.Equals(Tileset, StringComparison.OrdinalIgnoreCase))
					continue;

				maps.Add(AnalyzeMap(map, PackageName(preview.Package), templates, horizontalAdjacency, verticalAdjacency));
			}

			var result = new JObject
			{
				["schema_version"] = SchemaVersion,
				["generated_utc"] = DateTime.UtcNow.ToString("O"),
				["extractor"] = $"OpenRA.Utility sa {CommandName}",
				["scope"] = "Shipped System-class NORMAL maps and tracked NORMAL Clear, Rock, and Vegetation terrain metadata",
				["copyright_boundary"] = "The audit stores template identifiers, native terrain semantics, actor coordinates, and structural counts only. External sprite bytes and rendered derivatives are not copied.",
				["methodology"] = new JObject
				{
					["native_cell_authority"] = "Map.GetTerrainInfo(cell) resolved by the pinned OpenRA engine",
					["canonical_stamp"] = "A 2x2 non-PickAny template occurrence containing one Type and frame indexes 0,1,2,3 in row-major order",
					["component_connectivity"] = "Four-neighbor native-cell connectivity inside the playable map bounds",
					["water_proximity"] = "Eight-neighbor native cells adjacent to engine-resolved Water",
					["landmark_distance"] = "Chebyshev native-cell distance from the authored actor anchor to the nearest cell of the selected terrain type",
					["limitation"] = "Authored frequency and clearance are calibration evidence, not automatic safety limits. Generator route, production, combat-space, and native-movement contracts remain authoritative."
				},
				["summary"] = Summary(maps, templates, terrainInfo, locomotor),
				["templates"] = new JArray(templates.Values.Select(t => t.ToJson())),
				["horizontal_fixed_stamp_adjacency"] = AdjacencyJson(horizontalAdjacency, "left_template", "right_template"),
				["vertical_fixed_stamp_adjacency"] = AdjacencyJson(verticalAdjacency, "top_template", "bottom_template"),
				["maps"] = maps
			};

			var outputPath = Path.Combine(outputDirectory, "normal_land_surface_audit.json");
			File.WriteAllText(outputPath, result.ToString(Formatting.Indented) + Environment.NewLine);
			Console.WriteLine($"Audited land surfaces in {maps.Count} shipped NORMAL maps.");
			Console.WriteLine(outputPath);
		}

		static bool AuditedTemplate(TerrainTemplateInfo template)
		{
			return template.Categories != null && template.Categories.Any(c =>
				c.Equals("Terrain", StringComparison.OrdinalIgnoreCase) ||
				c.Equals("Rock", StringComparison.OrdinalIgnoreCase) ||
				c.Equals("Vegetation", StringComparison.OrdinalIgnoreCase));
		}

		static JObject AnalyzeMap(Map map, string directory, Dictionary<ushort, TemplateAggregate> templates,
			Dictionary<(ushort Left, ushort Right), int> horizontalAdjacency,
			Dictionary<(ushort Top, ushort Bottom), int> verticalAdjacency)
		{
			var width = map.MapSize.X;
			var height = map.MapSize.Y;
			var canonicalCells = new bool[width * height];
			var allAnchors = new Dictionary<(int X, int Y), ushort>();
			var fixedAnchors = new Dictionary<(int X, int Y), ushort>();
			var nativeUsage = new Dictionary<ushort, int>();
			var indexUsage = new Dictionary<(ushort Type, byte Index), int>();

			for (var x = 0; x < width; x++)
			{
				for (var y = 0; y < height; y++)
				{
					var tile = map.Tiles[new MPos(x, y)];
					if (!templates.TryGetValue(tile.Type, out var aggregate))
						continue;

					Increment(nativeUsage, tile.Type);
					Increment(indexUsage, (tile.Type, tile.Index));
					aggregate.AddNativeCell(directory, tile.Index);
				}
			}

			for (var x = 0; x < width - 1; x++)
			{
				for (var y = 0; y < height - 1; y++)
				{
					var topLeft = map.Tiles[new MPos(x, y)];
					if (topLeft.Index != 0 || !templates.TryGetValue(topLeft.Type, out var aggregate) ||
						aggregate.Template.Size.X != 2 || aggregate.Template.Size.Y != 2)
						continue;

					if (!Matches(map, x + 1, y, topLeft.Type, 1) || !Matches(map, x, y + 1, topLeft.Type, 2) ||
						!Matches(map, x + 1, y + 1, topLeft.Type, 3))
						continue;

					allAnchors[(x, y)] = topLeft.Type;
					aggregate.AddCanonicalStamp(directory);
					if (aggregate.Template.PickAny)
						continue;

					fixedAnchors[(x, y)] = topLeft.Type;
					canonicalCells[y * width + x] = true;
					canonicalCells[y * width + x + 1] = true;
					canonicalCells[(y + 1) * width + x] = true;
					canonicalCells[(y + 1) * width + x + 1] = true;
				}
			}

			foreach (var anchor in fixedAnchors.OrderBy(a => a.Key.Y).ThenBy(a => a.Key.X))
			{
				if (fixedAnchors.TryGetValue((anchor.Key.X + 2, anchor.Key.Y), out var right))
					Increment(horizontalAdjacency, (anchor.Value, right));
				if (fixedAnchors.TryGetValue((anchor.Key.X, anchor.Key.Y + 2), out var bottom))
					Increment(verticalAdjacency, (anchor.Value, bottom));
			}

			var noncanonicalByTemplate = new Dictionary<ushort, int>();
			for (var x = 0; x < width; x++)
			{
				for (var y = 0; y < height; y++)
				{
					var tile = map.Tiles[new MPos(x, y)];
					if (!templates.TryGetValue(tile.Type, out var aggregate) || aggregate.Template.PickAny || canonicalCells[y * width + x])
						continue;

					Increment(noncanonicalByTemplate, tile.Type);
					aggregate.AddNoncanonicalCell(directory);
				}
			}

			var landmarks = ReadLandmarks(map);
			var landSurface = AnalyzeLandSurface(map, landmarks);
			return new JObject
			{
				["directory"] = directory,
				["title"] = map.Title,
				["uid"] = map.Uid,
				["map_size"] = new JArray(width, height),
				["bounds"] = new JArray(map.Bounds.Left, map.Bounds.Top, map.Bounds.Width, map.Bounds.Height),
				["audited_native_cell_count"] = nativeUsage.Values.Sum(),
				["template_usage_by_native_cell"] = CountsByTemplate(nativeUsage),
				["template_index_usage"] = new JArray(indexUsage.OrderBy(kv => kv.Key.Type).ThenBy(kv => kv.Key.Index).Select(kv => new JObject
				{
					["template"] = kv.Key.Type,
					["index"] = kv.Key.Index,
					["count"] = kv.Value
				})),
				["canonical_stamp_count"] = allAnchors.Count,
				["canonical_stamp_counts"] = CountsByTemplate(allAnchors.Values.GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count())),
				["canonical_fixed_stamp_count"] = fixedAnchors.Count,
				["canonical_fixed_stamp_counts"] = CountsByTemplate(fixedAnchors.Values.GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count())),
				["noncanonical_fixed_cell_count"] = noncanonicalByTemplate.Values.Sum(),
				["noncanonical_fixed_cell_counts"] = CountsByTemplate(noncanonicalByTemplate),
				["landmarks"] = new JObject
				{
					["spawns"] = new JArray(landmarks.Spawns.Select(Point)),
					["colonies"] = new JArray(landmarks.Colonies.Select(Point))
				},
				["land_surface"] = landSurface
			};
		}

		static JObject AnalyzeLandSurface(Map map, Landmarks landmarks)
		{
			var left = map.Bounds.Left;
			var top = map.Bounds.Top;
			var width = map.Bounds.Width;
			var height = map.Bounds.Height;
			var terrain = new string[width * height];
			var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			for (var x = 0; x < width; x++)
			{
				for (var y = 0; y < height; y++)
				{
					var type = map.GetTerrainInfo(new CPos(left + x, top + y)).Type;
					terrain[y * width + x] = type;
					Increment(counts, type);
				}
			}

			var total = width * height;
			var fractions = new JObject();
			foreach (var count in counts.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
				fractions[count.Key] = total == 0 ? 0 : Round(count.Value / (double)total, 6);

			var landTotal = SurfaceTerrainTypes.Sum(type => counts.TryGetValue(type, out var count) ? count : 0);
			var landFractions = new JObject();
			foreach (var type in SurfaceTerrainTypes)
				landFractions[type] = landTotal == 0 ? 0 : Round((counts.TryGetValue(type, out var count) ? count : 0) / (double)landTotal, 6);

			var surfaces = new JObject();
			foreach (var type in new[] { "Rock", "Vegetation" })
				surfaces[type] = AnalyzeSurfaceType(type, terrain, left, top, width, height, landmarks);

			return new JObject
			{
				["playable_native_cell_count"] = total,
				["native_land_cell_count"] = landTotal,
				["native_cell_counts"] = CountsByName(counts),
				["native_cell_fractions"] = fractions,
				["land_cell_fractions"] = landFractions,
				["surface_types"] = surfaces
			};
		}

		static JObject AnalyzeSurfaceType(string type, string[] terrain, int left, int top, int width, int height, Landmarks landmarks)
		{
			var targets = new List<CPos>();
			var adjacentToWater = 0;
			for (var x = 0; x < width; x++)
			{
				for (var y = 0; y < height; y++)
				{
					if (!terrain[y * width + x].Equals(type, StringComparison.OrdinalIgnoreCase))
						continue;

					targets.Add(new CPos(left + x, top + y));
					if (NeighborDirections.Any(direction =>
					{
						var nx = x + direction.X;
						var ny = y + direction.Y;
						return nx >= 0 && nx < width && ny >= 0 && ny < height &&
							terrain[ny * width + nx].Equals("Water", StringComparison.OrdinalIgnoreCase);
					}))
						adjacentToWater++;
				}
			}

			var components = ComponentSizes(type, terrain, width, height);
			var spawnDistances = NearestDistances(landmarks.Spawns, targets);
			var colonyDistances = NearestDistances(landmarks.Colonies, targets);
			return new JObject
			{
				["native_cell_count"] = targets.Count,
				["component_count"] = components.Count,
				["component_sizes"] = new JArray(components.OrderBy(size => size)),
				["component_size_stats"] = Stats(components.Select(size => (double)size)),
				["isolated_native_cell_count"] = components.Count(size => size == 1),
				["cells_adjacent_to_water"] = adjacentToWater,
				["water_adjacent_fraction"] = targets.Count == 0 ? 0 : Round(adjacentToWater / (double)targets.Count, 6),
				["nearest_spawn_distances"] = NullableValues(spawnDistances),
				["nearest_spawn_distance_stats"] = Stats(spawnDistances.Where(distance => distance != null).Select(distance => (double)distance.Value)),
				["nearest_colony_distances"] = NullableValues(colonyDistances),
				["nearest_colony_distance_stats"] = Stats(colonyDistances.Where(distance => distance != null).Select(distance => (double)distance.Value))
			};
		}

		static List<int> ComponentSizes(string type, string[] terrain, int width, int height)
		{
			var visited = new bool[terrain.Length];
			var sizes = new List<int>();
			for (var x = 0; x < width; x++)
			{
				for (var y = 0; y < height; y++)
				{
					var start = y * width + x;
					if (visited[start] || !terrain[start].Equals(type, StringComparison.OrdinalIgnoreCase))
						continue;

					var size = 0;
					var queue = new Queue<(int X, int Y)>();
					queue.Enqueue((x, y));
					visited[start] = true;
					while (queue.Count > 0)
					{
						(var cellX, var cellY) = queue.Dequeue();
						size++;
						foreach (var direction in CardinalDirections)
						{
							var nx = cellX + direction.X;
							var ny = cellY + direction.Y;
							if (nx < 0 || nx >= width || ny < 0 || ny >= height)
								continue;

							var index = ny * width + nx;
							if (visited[index] || !terrain[index].Equals(type, StringComparison.OrdinalIgnoreCase))
								continue;

							visited[index] = true;
							queue.Enqueue((nx, ny));
						}
					}

					sizes.Add(size);
				}
			}

			return sizes;
		}

		static int?[] NearestDistances(IReadOnlyList<CPos> landmarks, IReadOnlyList<CPos> targets)
		{
			var result = new int?[landmarks.Count];
			if (targets.Count == 0)
				return result;

			for (var i = 0; i < landmarks.Count; i++)
			{
				var distance = int.MaxValue;
				foreach (var target in targets)
					distance = Math.Min(distance, Math.Max(Math.Abs(landmarks[i].X - target.X), Math.Abs(landmarks[i].Y - target.Y)));
				result[i] = distance;
			}

			return result;
		}

		static Landmarks ReadLandmarks(Map map)
		{
			var spawns = new List<CPos>();
			var colonies = new List<CPos>();
			foreach (var definition in map.ActorDefinitions)
			{
				var reference = new ActorReference(definition.Value.Value, definition.Value.ToDictionary());
				var location = reference.GetOrDefault<LocationInit>()?.Value;
				if (location == null)
					continue;

				if (reference.Type.Equals("mpspawn", StringComparison.OrdinalIgnoreCase))
					spawns.Add(location.Value);
				if (ColonyActorTypes.Contains(reference.Type))
					colonies.Add(location.Value);
			}

			return new Landmarks(spawns, colonies);
		}

		static JObject Summary(JArray maps, Dictionary<ushort, TemplateAggregate> templates,
			ITerrainInfo terrainInfo, LocomotorInfo locomotor)
		{
			var clear = templates.Values.Where(t => t.Category.Equals("Terrain", StringComparison.OrdinalIgnoreCase)).ToArray();
			var rock = templates.Values.Where(t => t.Category.Equals("Rock", StringComparison.OrdinalIgnoreCase)).ToArray();
			var vegetation = templates.Values.Where(t => t.Category.Equals("Vegetation", StringComparison.OrdinalIgnoreCase)).ToArray();
			return new JObject
			{
				["normal_map_count"] = maps.Count,
				["audited_template_count"] = templates.Count,
				["clear_template_ids"] = new JArray(clear.Select(t => t.Template.Id)),
				["rock_template_ids"] = new JArray(rock.Select(t => t.Template.Id)),
				["vegetation_template_ids"] = new JArray(vegetation.Select(t => t.Template.Id)),
				["rock_pick_any_ids"] = new JArray(rock.Where(t => t.Template.PickAny).Select(t => t.Template.Id)),
				["vegetation_pick_any_ids"] = new JArray(vegetation.Where(t => t.Template.PickAny).Select(t => t.Template.Id)),
				["clear_rock_transition_ids"] = new JArray(rock.Where(t => t.Classification == "ClearRockTransition").Select(t => t.Template.Id)),
				["rock_category_clear_detail_ids"] = new JArray(rock.Where(t => t.Classification == "ClearDetail").Select(t => t.Template.Id)),
				["rock_vegetation_transition_ids"] = new JArray(vegetation.Where(t => t.Classification == "RockVegetationTransition").Select(t => t.Template.Id)),
				["vegetation_category_vegetation_detail_ids"] = new JArray(vegetation.Where(t => t.Classification == "VegetationDetail").Select(t => t.Template.Id)),
				["vegetation_category_rock_detail_ids"] = new JArray(vegetation.Where(t => t.Classification == "RockDetail").Select(t => t.Template.Id)),
				["canonical_stamp_count"] = templates.Values.Sum(t => t.CanonicalStampCount),
				["canonical_fixed_stamp_count"] = templates.Values.Where(t => !t.Template.PickAny).Sum(t => t.CanonicalStampCount),
				["noncanonical_fixed_cell_count"] = templates.Values.Sum(t => t.NoncanonicalCellCount),
				["ground_movement"] = new JArray(SurfaceTerrainTypes.Select(type => MovementJson(type, terrainInfo, locomotor))),
				["authored_detail_rates"] = AuthoredDetailRates(clear, rock, vegetation),
				["corpus_land_surface"] = CorpusSurfaceSummary(maps)
			};
		}

		static JObject AuthoredDetailRates(TemplateAggregate[] clear, TemplateAggregate[] rock, TemplateAggregate[] vegetation)
		{
			return new JObject
			{
				["clear_native_cosmetic"] = DetailRate(
					rock.Where(template => template.Classification == "ClearDetail"),
					clear.Where(template => template.Classification == "ClearInterior"), "Clear"),
				["rock_native_detail"] = DetailRate(
					vegetation.Where(template => template.Classification == "RockDetail"),
					rock.Where(template => template.Classification == "RockInterior"), "Rock"),
				["vegetation_native_detail"] = DetailRate(
					vegetation.Where(template => template.Classification == "VegetationDetail"),
					vegetation.Where(template => template.Classification == "VegetationInterior"), "Vegetation")
			};
		}

		static JObject DetailRate(IEnumerable<TemplateAggregate> detailTemplates,
			IEnumerable<TemplateAggregate> interiorTemplates, string nativeTerrain)
		{
			var details = detailTemplates.ToArray();
			var interiors = interiorTemplates.ToArray();
			var detailCount = details.Sum(template => template.CanonicalStampCount);
			var interiorCount = interiors.Sum(template => template.CanonicalStampCount);
			var total = detailCount + interiorCount;
			return new JObject
			{
				["native_terrain"] = nativeTerrain,
				["detail_template_ids"] = new JArray(details.Select(template => template.Template.Id)),
				["interior_template_ids"] = new JArray(interiors.Select(template => template.Template.Id)),
				["detail_canonical_stamp_count"] = detailCount,
				["interior_canonical_stamp_count"] = interiorCount,
				["detail_fraction_of_homogeneous_stamps"] = total == 0 ? 0 : Round(detailCount / (double)total, 6)
			};
		}

		static JObject CorpusSurfaceSummary(JArray maps)
		{
			var result = new JObject();
			foreach (var type in SurfaceTerrainTypes)
			{
				var counts = maps.Children<JObject>().Select(map =>
					(int?)map["land_surface"]?["native_cell_counts"]?[type] ?? 0).ToArray();
				var fractions = maps.Children<JObject>().Select(map =>
					(double?)map["land_surface"]?["native_cell_fractions"]?[type] ?? 0).ToArray();
				var landFractions = maps.Children<JObject>().Select(map =>
					(double?)map["land_surface"]?["land_cell_fractions"]?[type] ?? 0).ToArray();
				var entry = new JObject
				{
					["native_cell_count"] = counts.Sum(),
					["maps_with_terrain"] = counts.Count(count => count > 0),
					["map_coverage_fraction_stats"] = Stats(fractions),
					["land_fraction_stats"] = Stats(landFractions)
				};

				if (type.Equals("Rock", StringComparison.OrdinalIgnoreCase) || type.Equals("Vegetation", StringComparison.OrdinalIgnoreCase))
				{
					var surfaces = maps.Children<JObject>().Select(map =>
						(JObject)map["land_surface"]?["surface_types"]?[type]).Where(surface => surface != null).ToArray();
					entry["component_size_stats"] = Stats(surfaces.SelectMany(surface =>
						surface["component_sizes"].Values<double>()));
					entry["water_adjacent_fraction_stats"] = Stats(surfaces.Select(surface =>
						(double)surface["water_adjacent_fraction"]));
					entry["nearest_spawn_distance_stats"] = Stats(surfaces.SelectMany(surface =>
						surface["nearest_spawn_distances"].Children().Where(token => token.Type != JTokenType.Null).Select(token => (double)token)));
					entry["nearest_colony_distance_stats"] = Stats(surfaces.SelectMany(surface =>
						surface["nearest_colony_distances"].Children().Where(token => token.Type != JTokenType.Null).Select(token => (double)token)));
				}

				result[type] = entry;
			}

			var slowLandFractions = maps.Children<JObject>().Select(map =>
				((double?)map["land_surface"]?["land_cell_fractions"]?["Rock"] ?? 0) +
				((double?)map["land_surface"]?["land_cell_fractions"]?["Vegetation"] ?? 0)).ToArray();
			result["SlowLand"] = new JObject
			{
				["native_cell_count"] = (int)result["Rock"]["native_cell_count"] + (int)result["Vegetation"]["native_cell_count"],
				["maps_with_terrain"] = slowLandFractions.Count(fraction => fraction > 0),
				["land_fraction_stats"] = Stats(slowLandFractions)
			};

			return result;
		}

		static JObject MovementJson(string type, ITerrainInfo terrainInfo, LocomotorInfo locomotor)
		{
			var terrainType = terrainInfo.TerrainTypes.Single(terrain => terrain.Type.Equals(type, StringComparison.OrdinalIgnoreCase));
			var passable = locomotor.TerrainSpeeds.TryGetValue(terrainType.Type, out var speed);
			return new JObject
			{
				["type"] = terrainType.Type,
				["ground_passable"] = passable,
				["ground_speed_percent"] = passable ? speed.Speed : 0,
				["ground_pathing_cost"] = passable ? speed.Cost : null
			};
		}

		static JObject Stats(IEnumerable<double> values)
		{
			var ordered = values.OrderBy(value => value).ToArray();
			if (ordered.Length == 0)
				return new JObject { ["count"] = 0 };

			return new JObject
			{
				["count"] = ordered.Length,
				["minimum"] = Round(ordered[0], 6),
				["median"] = Round(Percentile(ordered, 0.5), 6),
				["p90"] = Round(Percentile(ordered, 0.9), 6),
				["mean"] = Round(ordered.Average(), 6),
				["maximum"] = Round(ordered[^1], 6)
			};
		}

		static double Percentile(IReadOnlyList<double> ordered, double percentile)
		{
			if (ordered.Count == 1)
				return ordered[0];

			var position = percentile * (ordered.Count - 1);
			var lower = (int)Math.Floor(position);
			var upper = (int)Math.Ceiling(position);
			if (lower == upper)
				return ordered[lower];

			return ordered[lower] + (ordered[upper] - ordered[lower]) * (position - lower);
		}

		static JArray NullableValues(IEnumerable<int?> values)
		{
			return new JArray(values.Select(value => value == null ? JValue.CreateNull() : new JValue(value.Value)));
		}

		static JObject Point(CPos point)
		{
			return new JObject { ["x"] = point.X, ["y"] = point.Y };
		}

		static bool Matches(Map map, int x, int y, ushort type, byte index)
		{
			var tile = map.Tiles[new MPos(x, y)];
			return tile.Type == type && tile.Index == index;
		}

		static JObject CountsByTemplate(Dictionary<ushort, int> counts)
		{
			var result = new JObject();
			foreach (var count in counts.OrderBy(kv => kv.Key))
				result[count.Key.ToString()] = count.Value;
			return result;
		}

		static JObject CountsByName(Dictionary<string, int> counts)
		{
			var result = new JObject();
			foreach (var count in counts.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
				result[count.Key] = count.Value;
			return result;
		}

		static JArray AdjacencyJson(Dictionary<(ushort First, ushort Second), int> counts, string firstName, string secondName)
		{
			return new JArray(counts.OrderBy(kv => kv.Key.First).ThenBy(kv => kv.Key.Second).Select(kv => new JObject
			{
				[firstName] = kv.Key.First,
				[secondName] = kv.Key.Second,
				["count"] = kv.Value
			}));
		}

		static string PackageName(OpenRA.FileSystem.IReadOnlyPackage package)
		{
			return Path.GetFileName(package.Name.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		}

		static LocomotorInfo GetGroundLocomotor(Ruleset rules)
		{
			return rules.Actors[SystemActors.World].TraitInfos<LocomotorInfo>()
				.Single(locomotor => locomotor.Name.Equals("unit", StringComparison.OrdinalIgnoreCase));
		}

		static void Increment<TKey>(Dictionary<TKey, int> dictionary, TKey key)
		{
			dictionary.TryGetValue(key, out var value);
			dictionary[key] = value + 1;
		}

		static double Round(double value, int digits)
		{
			return Math.Round(value, digits, MidpointRounding.AwayFromZero);
		}

		sealed class TemplateAggregate
		{
			public readonly TerrainTemplateInfo Template;
			public readonly HashSet<string> TerrainTypes;
			public readonly string Category;
			public readonly string Classification;
			readonly ITerrainInfo terrainInfo;
			readonly string[] images;
			readonly Dictionary<byte, int> indexUsage = new();
			readonly HashSet<string> usageMaps = new(StringComparer.OrdinalIgnoreCase);
			readonly HashSet<string> canonicalMaps = new(StringComparer.OrdinalIgnoreCase);
			readonly HashSet<string> noncanonicalMaps = new(StringComparer.OrdinalIgnoreCase);

			public int NativeCellCount { get; private set; }
			public int CanonicalStampCount { get; private set; }
			public int NoncanonicalCellCount { get; private set; }

			public TemplateAggregate(TerrainTemplateInfo template, ITerrainInfo terrainInfo)
			{
				Template = template;
				this.terrainInfo = terrainInfo;
				Category = template.Categories.Single(category =>
					category.Equals("Terrain", StringComparison.OrdinalIgnoreCase) ||
					category.Equals("Rock", StringComparison.OrdinalIgnoreCase) ||
					category.Equals("Vegetation", StringComparison.OrdinalIgnoreCase));
				TerrainTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				for (var i = 0; i < template.TilesCount; i++)
				{
					var tile = template[i];
					if (tile != null)
						TerrainTypes.Add(terrainInfo.TerrainTypes[tile.TerrainType].Type);
				}

				Classification = Classify(Category, TerrainTypes, template.PickAny);
				images = template is DefaultTerrainTemplateInfo defaultTemplate ? defaultTemplate.Images : Array.Empty<string>();
			}

			public void AddNativeCell(string map, byte index)
			{
				NativeCellCount++;
				usageMaps.Add(map);
				Increment(indexUsage, index);
			}

			public void AddCanonicalStamp(string map)
			{
				CanonicalStampCount++;
				canonicalMaps.Add(map);
			}

			public void AddNoncanonicalCell(string map)
			{
				NoncanonicalCellCount++;
				noncanonicalMaps.Add(map);
			}

			public JObject ToJson()
			{
				var tiles = new JArray();
				for (var i = 0; i < Template.TilesCount; i++)
				{
					var tile = Template[i];
					tiles.Add(tile == null ? new JObject { ["index"] = i, ["defined"] = false } : new JObject
					{
						["index"] = i,
						["defined"] = true,
						["terrain_type"] = TerrainTypeForIndex(i)
					});
				}

				return new JObject
				{
					["id"] = Template.Id,
					["images"] = new JArray(images),
					["category"] = Category,
					["classification"] = Classification,
					["bank_local_index"] = BankLocalIndex(Category, Template.Id),
					["size"] = new JArray(Template.Size.X, Template.Size.Y),
					["pick_any"] = Template.PickAny,
					["terrain_types"] = new JArray(TerrainTypes.OrderBy(type => type, StringComparer.OrdinalIgnoreCase)),
					["tiles"] = tiles,
					["native_cell_count"] = NativeCellCount,
					["usage_map_count"] = usageMaps.Count,
					["index_usage"] = JObject.FromObject(indexUsage.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key.ToString(), kv => kv.Value)),
					["canonical_stamp_count"] = CanonicalStampCount,
					["canonical_stamp_map_count"] = canonicalMaps.Count,
					["noncanonical_cell_count"] = NoncanonicalCellCount,
					["noncanonical_map_count"] = noncanonicalMaps.Count
				};
			}

			string TerrainTypeForIndex(int index)
			{
				var tile = Template[index];
				return tile == null ? null : terrainInfo.TerrainTypes[tile.TerrainType].Type;
			}

			static string Classify(string category, HashSet<string> terrainTypes, bool pickAny)
			{
				if (category.Equals("Terrain", StringComparison.OrdinalIgnoreCase) && terrainTypes.SetEquals(new[] { "Clear" }))
					return "ClearInterior";
				if (category.Equals("Rock", StringComparison.OrdinalIgnoreCase) && terrainTypes.SetEquals(new[] { "Clear", "Rock" }))
					return "ClearRockTransition";
				if (category.Equals("Rock", StringComparison.OrdinalIgnoreCase) && terrainTypes.SetEquals(new[] { "Clear" }))
					return "ClearDetail";
				if (category.Equals("Rock", StringComparison.OrdinalIgnoreCase) && terrainTypes.SetEquals(new[] { "Rock" }))
					return pickAny ? "RockInterior" : "RockDetail";
				if (category.Equals("Vegetation", StringComparison.OrdinalIgnoreCase) && terrainTypes.SetEquals(new[] { "Rock", "Vegetation" }))
					return "RockVegetationTransition";
				if (category.Equals("Vegetation", StringComparison.OrdinalIgnoreCase) && terrainTypes.SetEquals(new[] { "Vegetation" }))
					return pickAny ? "VegetationInterior" : "VegetationDetail";
				if (category.Equals("Vegetation", StringComparison.OrdinalIgnoreCase) && terrainTypes.SetEquals(new[] { "Rock" }))
					return "RockDetail";

				return "Unsupported";
			}

			static int BankLocalIndex(string category, ushort templateId)
			{
				if (category.Equals("Terrain", StringComparison.OrdinalIgnoreCase))
					return templateId - 32;
				if (category.Equals("Rock", StringComparison.OrdinalIgnoreCase))
					return templateId - 37;
				if (category.Equals("Vegetation", StringComparison.OrdinalIgnoreCase))
					return templateId - 69;
				return -1;
			}
		}

		sealed record Landmarks(IReadOnlyList<CPos> Spawns, IReadOnlyList<CPos> Colonies);
	}
}
