#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion
using System;
using System.IO;
using System.Linq;
using OpenRA.Mods.Common.Terrain;
using OpenRA.Mods.OpenSA.Terrain;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public static class RmgBiome
	{
		public static readonly string[] Tilesets = { "NORMAL", "DESERT", "SWAMP", "CANDY" };
		public static bool IsSupported(string value) => Tilesets.Contains(value);
		public static string Parse(string value)
		{
			var id = value?.ToUpperInvariant();
			if (!IsSupported(id)) throw new ArgumentException("tileset must be NORMAL, DESERT, SWAMP or CANDY.");
			return id;
		}

		// Same decorative roles and number of choices, preserving seeded positions across themes.
		public static string[] Decorations(string tileset) => tileset switch
		{
			"NORMAL" => new[] { "plant_flower", "rmg_plant_broad_leaf_grass", "rmg_plant_brown_mushroom", "rmg_plant_toad_stool" },
			"DESERT" => new[] { "plant_desert_flower", "rmg_plant_desert_grass", "rmg_plant_gumnut", "rmg_plant_serata" },
			"SWAMP" => new[] { "plant_cherry_flower", "rmg_plant_clover", "rmg_plant_moon_mushroom", "rmg_plant_moon_flower" },
			"CANDY" => new[] { "plant_smarty", "rmg_plant_jelly_bean", "plant_choc_chip", "rmg_plant_jaffa" },
			_ => throw new ArgumentException("Unsupported RMG biome.")
		};

		// The audited identity mapping is checked against loaded engine data before materialization.
		public static void ValidateCatalogue(ModData modData, string tileset)
		{
			if (!IsSupported(tileset)) throw new ArgumentException("Unsupported RMG biome.");
			var source = (ITemplatedTerrainInfo)modData.DefaultTerrainInfo["NORMAL"];
			var target = (ITemplatedTerrainInfo)modData.DefaultTerrainInfo[tileset];
			for (ushort id = 0; id <= 100; id++)
			{
				if (!source.Templates.TryGetValue(id, out var a) || !target.Templates.TryGetValue(id, out var b) ||
					b.Size.X != 2 || b.Size.Y != 2 || b.TilesCount != 4 ||
					a is not CustomTerrainTemplateInfo original || b is not CustomTerrainTemplateInfo translated ||
					!translated.Frames.SequenceEqual(original.Frames) || translated.Palette != original.Palette)
					throw new InvalidDataException($"{tileset} template {id} violates the shared Regions catalogue.");
				var pickAny = (tileset != "NORMAL" && id == 27) || (!(tileset == "DESERT" && id == 26) && a.PickAny);
				if (b.PickAny != pickAny) throw new InvalidDataException($"{tileset} template {id} changed its audited PickAny policy.");
				for (var frame = 0; frame < 4; frame++)
				{
					if (a[frame] is not CustomTerrainTileInfo af || b[frame] is not CustomTerrainTileInfo bf ||
						source.TerrainTypes[af.TerrainType].Type != target.TerrainTypes[bf.TerrainType].Type ||
						af.ZOffset != bf.ZOffset || af.ZRamp != bf.ZRamp)
						throw new InvalidDataException($"{tileset} template {id}, frame {frame} changed terrain or height semantics.");
				}
			}
		}
	}
}
