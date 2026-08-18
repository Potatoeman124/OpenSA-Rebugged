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
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenRA.Mods.Common.Terrain;
using OpenRA.Mods.OpenSA.Terrain;

namespace OpenRA.Mods.OpenSA.UtilityCommands
{
	/// <summary>
	/// Renders local-only NORMAL Rock and Vegetation template atlases for the RMG Phase 6A visual audit.
	/// The output belongs under ignored artifacts and must never be staged.
	/// </summary>
	sealed class ExportRmgNormalLandAtlasCommand : IUtilityCommand
	{
		const string CommandName = "--export-rmg-normal-land-atlas";

		string IUtilityCommand.Name => CommandName;

		bool IUtilityCommand.ValidateArguments(string[] args)
		{
			return args.Length == 2;
		}

		[Desc("OUTPUT-DIRECTORY", "Render ignored local NORMAL Rock and Vegetation template atlases and identifier layouts. No source assets are modified.")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			Game.ModData = utility.ModData;
			var outputDirectory = Path.GetFullPath(args[1]);
			Directory.CreateDirectory(outputDirectory);

			if (!utility.ModData.DefaultTerrainInfo.TryGetValue("NORMAL", out var terrainInfo) ||
				terrainInfo is not ITemplatedTerrainInfo templated)
				throw new InvalidDataException("NORMAL tileset is missing or is not templated.");

			foreach (var category in new[] { "Rock", "Vegetation" })
			{
				var templates = templated.Templates.Values
					.Where(t => t.Categories != null && t.Categories.Any(c => c.Equals(category, StringComparison.OrdinalIgnoreCase)))
					.OrderBy(t => t.Id)
					.ToArray();
				var name = category.ToLowerInvariant();
				var atlasPath = Path.Combine(outputDirectory, $"normal_{name}_template_atlas.png");
				RmgTerrainAtlasExporter.WriteNormalCategoryAtlas(utility.ModData, templated, category, atlasPath);

				var layout = new JObject
				{
					["schema_version"] = "1.0",
					["generated_utc"] = DateTime.UtcNow.ToString("O"),
					["extractor"] = $"OpenRA.Utility sa {CommandName}",
					["category"] = category,
					["copyright_boundary"] = "Local ignored visual evidence derived from user-installed original assets. Never stage or distribute this atlas.",
					["grid"] = new JObject
					{
						["columns"] = 8,
						["order"] = "row-major by template id"
					},
					["templates"] = new JArray(templates.Select((template, index) => new JObject
					{
						["id"] = template.Id,
						["bank_local_index"] = BankLocalIndex(category, template.Id),
						["row"] = index / 8,
						["column"] = index % 8,
						["images"] = new JArray(Images(template)),
						["pick_any"] = template.PickAny,
						["native_terrain"] = new JArray(Enumerable.Range(0, template.TilesCount).Select(tileIndex =>
						{
							var tile = template[tileIndex];
							return tile == null ? null : terrainInfo.TerrainTypes[tile.TerrainType].Type;
						}))
					}))
				};

				var layoutPath = Path.Combine(outputDirectory, $"normal_{name}_template_atlas_layout.json");
				File.WriteAllText(layoutPath, layout.ToString(Formatting.Indented) + Environment.NewLine);
				Console.WriteLine(atlasPath);
				Console.WriteLine(layoutPath);
			}
		}

		static string[] Images(TerrainTemplateInfo template)
		{
			return template switch
			{
				CustomTerrainTemplateInfo custom => custom.Images,
				DefaultTerrainTemplateInfo standard => standard.Images,
				_ => Array.Empty<string>()
			};
		}

		static int BankLocalIndex(string category, ushort templateId)
		{
			return category.Equals("Rock", StringComparison.OrdinalIgnoreCase) ? templateId - 37 : templateId - 69;
		}
	}
}
