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

namespace OpenRA.Mods.OpenSA.UtilityCommands
{
	/// <summary>
	/// Extracts read-only NORMAL tileset transition evidence for the RMG Phase 5A audit.
	/// Original sprites are resolved by the installed content packages but are never copied.
	/// </summary>
	sealed class AuditRmgTerrainTransitionsCommand : IUtilityCommand
	{
		const string SchemaVersion = "1.0";
		const string CommandName = "--audit-rmg-terrain-transitions";
		const string Tileset = "NORMAL";

		string IUtilityCommand.Name => CommandName;

		bool IUtilityCommand.ValidateArguments(string[] args)
		{
			return args.Length == 2;
		}

		[Desc("OUTPUT-DIRECTORY", "Audit NORMAL terrain-template usage and adjacency as JSON. No maps or sprites are modified or copied.")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			Game.ModData = utility.ModData;
			var outputDirectory = Path.GetFullPath(args[1]);
			Directory.CreateDirectory(outputDirectory);

			if (!utility.ModData.DefaultTerrainInfo.TryGetValue(Tileset, out var terrainInfo) ||
				terrainInfo is not ITemplatedTerrainInfo templated)
				throw new InvalidDataException($"Tileset '{Tileset}' is missing or is not templated.");

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
				["scope"] = "Shipped System-class NORMAL maps and tracked NORMAL terrain metadata",
				["copyright_boundary"] = "The audit stores template identifiers and structural counts only. External sprite bytes and rendered derivatives are not copied.",
				["methodology"] = new JObject
				{
					["native_cell_authority"] = "Map.Tiles and terrain metadata resolved by the pinned OpenRA engine",
					["canonical_stamp"] = "A 2x2 non-PickAny template occurrence containing one Type and frame indexes 0,1,2,3 in row-major order",
					["macro_adjacency"] = "Canonical stamps whose anchors differ by exactly two native cells horizontally or vertically",
					["noncanonical_cell"] = "A native cell using an audited non-PickAny template that is not part of a canonical stamp",
					["limitation"] = "Adjacency evidence describes authored usage; sprite-edge compatibility still requires visual confirmation before implementation."
				},
				["summary"] = Summary(maps, templates),
				["templates"] = new JArray(templates.Values.Select(t => t.ToJson())),
				["horizontal_adjacency"] = AdjacencyJson(horizontalAdjacency, "left_template", "right_template"),
				["vertical_adjacency"] = AdjacencyJson(verticalAdjacency, "top_template", "bottom_template"),
				["maps"] = maps
			};

			var outputPath = Path.Combine(outputDirectory, "normal_terrain_transition_audit.json");
			File.WriteAllText(outputPath, result.ToString(Formatting.Indented) + Environment.NewLine);
			Console.WriteLine($"Audited {maps.Count} shipped NORMAL maps.");
			Console.WriteLine(outputPath);
		}

		static bool AuditedTemplate(TerrainTemplateInfo template)
		{
			return template.Categories != null && template.Categories.Any(c =>
				c.Equals("Water", StringComparison.OrdinalIgnoreCase) || c.Equals("Terrain", StringComparison.OrdinalIgnoreCase));
		}

		static JObject AnalyzeMap(Map map, string directory, Dictionary<ushort, TemplateAggregate> templates,
			Dictionary<(ushort Left, ushort Right), int> horizontalAdjacency,
			Dictionary<(ushort Top, ushort Bottom), int> verticalAdjacency)
		{
			var width = map.MapSize.X;
			var height = map.MapSize.Y;
			var canonicalCells = new bool[width * height];
			var anchors = new Dictionary<(int X, int Y), ushort>();
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
						aggregate.Template.PickAny || aggregate.Template.Size.X != 2 || aggregate.Template.Size.Y != 2)
						continue;

					if (!Matches(map, x + 1, y, topLeft.Type, 1) || !Matches(map, x, y + 1, topLeft.Type, 2) ||
						!Matches(map, x + 1, y + 1, topLeft.Type, 3))
						continue;

					anchors[(x, y)] = topLeft.Type;
					canonicalCells[y * width + x] = true;
					canonicalCells[y * width + x + 1] = true;
					canonicalCells[(y + 1) * width + x] = true;
					canonicalCells[(y + 1) * width + x + 1] = true;
					aggregate.AddCanonicalStamp(directory);
				}
			}

			foreach (var anchor in anchors.OrderBy(a => a.Key.Y).ThenBy(a => a.Key.X))
			{
				if (anchors.TryGetValue((anchor.Key.X + 2, anchor.Key.Y), out var right))
					Increment(horizontalAdjacency, (anchor.Value, right));
				if (anchors.TryGetValue((anchor.Key.X, anchor.Key.Y + 2), out var bottom))
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
				["canonical_stamp_count"] = anchors.Count,
				["canonical_stamp_counts"] = CountsByTemplate(anchors.Values.GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count())),
				["noncanonical_cell_count"] = noncanonicalByTemplate.Values.Sum(),
				["noncanonical_cell_counts"] = CountsByTemplate(noncanonicalByTemplate)
			};
		}

		static bool Matches(Map map, int x, int y, ushort type, byte index)
		{
			var tile = map.Tiles[new MPos(x, y)];
			return tile.Type == type && tile.Index == index;
		}

		static JObject Summary(JArray maps, Dictionary<ushort, TemplateAggregate> templates)
		{
			var water = templates.Values.Where(t => t.IsWaterCategory).ToArray();
			return new JObject
			{
				["normal_map_count"] = maps.Count,
				["audited_template_count"] = templates.Count,
				["water_template_ids"] = new JArray(water.Select(t => t.Template.Id)),
				["water_pick_any_ids"] = new JArray(water.Where(t => t.Template.PickAny).Select(t => t.Template.Id)),
				["water_non_pick_any_ids"] = new JArray(water.Where(t => !t.Template.PickAny).Select(t => t.Template.Id)),
				["mixed_clear_water_ids"] = new JArray(water.Where(t => t.TerrainTypes.SetEquals(new[] { "Clear", "Water" })).Select(t => t.Template.Id)),
				["homogeneous_water_non_pick_any_ids"] = new JArray(water.Where(t => !t.Template.PickAny && t.TerrainTypes.SetEquals(new[] { "Water" })).Select(t => t.Template.Id)),
				["canonical_stamp_count"] = templates.Values.Sum(t => t.CanonicalStampCount),
				["noncanonical_cell_count"] = templates.Values.Sum(t => t.NoncanonicalCellCount)
			};
		}

		static JObject CountsByTemplate(Dictionary<ushort, int> counts)
		{
			var result = new JObject();
			foreach (var count in counts.OrderBy(kv => kv.Key))
				result[count.Key.ToString()] = count.Value;
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

		static void Increment<TKey>(Dictionary<TKey, int> dictionary, TKey key)
		{
			dictionary.TryGetValue(key, out var value);
			dictionary[key] = value + 1;
		}

		sealed class TemplateAggregate
		{
			public readonly TerrainTemplateInfo Template;
			public readonly HashSet<string> TerrainTypes;
			public readonly bool IsWaterCategory;
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
				TerrainTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				for (var i = 0; i < template.TilesCount; i++)
				{
					var tile = template[i];
					if (tile != null)
						TerrainTypes.Add(terrainInfo.TerrainTypes[tile.TerrainType].Type);
				}

				IsWaterCategory = template.Categories != null && template.Categories.Any(c => c.Equals("Water", StringComparison.OrdinalIgnoreCase));
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
						["terrain_type"] = TerrainTypesForIndex(i)
					});
				}

				return new JObject
				{
					["id"] = Template.Id,
					["images"] = new JArray(images),
					["categories"] = new JArray(Template.Categories ?? Array.Empty<string>()),
					["size"] = new JArray(Template.Size.X, Template.Size.Y),
					["pick_any"] = Template.PickAny,
					["terrain_types"] = new JArray(TerrainTypes.OrderBy(t => t, StringComparer.OrdinalIgnoreCase)),
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

			string TerrainTypesForIndex(int index)
			{
				var tile = Template[index];
				return tile == null ? null : terrainInfo.TerrainTypes[tile.TerrainType].Type;
			}
		}
	}
}
