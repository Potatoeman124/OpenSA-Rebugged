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
	/// Renders a local-only NORMAL Water template atlas for the RMG Phase 5A visual audit.
	/// The output belongs under ignored artifacts and must never be staged.
	/// </summary>
	sealed class ExportRmgNormalWaterAtlasCommand : IUtilityCommand
	{
		const string CommandName = "--export-rmg-normal-water-atlas";

		string IUtilityCommand.Name => CommandName;

		bool IUtilityCommand.ValidateArguments(string[] args)
		{
			return args.Length == 2;
		}

		[Desc("OUTPUT-DIRECTORY", "Render an ignored local NORMAL Water template atlas and identifier layout. No source assets are modified.")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			Game.ModData = utility.ModData;
			var outputDirectory = Path.GetFullPath(args[1]);
			Directory.CreateDirectory(outputDirectory);

			if (!utility.ModData.DefaultTerrainInfo.TryGetValue("NORMAL", out var terrainInfo) ||
				terrainInfo is not ITemplatedTerrainInfo templated)
				throw new InvalidDataException("NORMAL tileset is missing or is not templated.");

			var waterTemplates = templated.Templates.Values
				.Where(t => t.Categories != null && t.Categories.Any(c => c.Equals("Water", StringComparison.OrdinalIgnoreCase)))
				.OrderBy(t => t.Id)
				.ToArray();
			var atlasPath = Path.Combine(outputDirectory, "normal_water_template_atlas.png");
			RmgTerrainAtlasExporter.WriteNormalWaterAtlas(utility.ModData, templated, atlasPath);

			var layout = new JObject
			{
				["schema_version"] = "1.0",
				["generated_utc"] = DateTime.UtcNow.ToString("O"),
				["extractor"] = $"OpenRA.Utility sa {CommandName}",
				["copyright_boundary"] = "Local ignored visual evidence derived from user-installed original assets. Never stage or distribute this atlas.",
				["grid"] = new JObject
				{
					["columns"] = 8,
					["order"] = "row-major by template id"
				},
				["templates"] = new JArray(waterTemplates.Select((template, index) => new JObject
				{
					["id"] = template.Id,
					["row"] = index / 8,
					["column"] = index % 8,
					["images"] = new JArray(Images(template)),
					["pick_any"] = template.PickAny
				}))
			};

			var layoutPath = Path.Combine(outputDirectory, "normal_water_template_atlas_layout.json");
			File.WriteAllText(layoutPath, layout.ToString(Formatting.Indented) + Environment.NewLine);
			Console.WriteLine(atlasPath);
			Console.WriteLine(layoutPath);
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
	}
}
