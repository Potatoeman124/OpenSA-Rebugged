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
using OpenRA.FileFormats;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Terrain;
using OpenRA.Mods.OpenSA.Terrain;
using OpenRA.Primitives;

namespace OpenRA.Mods.OpenSA.UtilityCommands
{
	static class RmgTerrainAtlasExporter
	{
		const int Columns = 8;
		const int Gap = 4;

		public static void WriteNormalWaterAtlas(ModData modData, ITemplatedTerrainInfo terrainInfo, string outputPath)
		{
			var templates = terrainInfo.Templates.Values
				.Where(t => t.Categories != null && t.Categories.Any(c => c.Equals("Water", StringComparison.OrdinalIgnoreCase)))
				.OrderBy(t => t.Id)
				.ToArray();
			if (templates.Length == 0)
				throw new InvalidDataException("The NORMAL tileset defines no Water templates.");

			var frameCache = new FrameCache(modData.DefaultFileSystem, modData.SpriteLoaders);
			var firstFrames = frameCache[Images(templates[0]).Single()];
			ValidateFrames(templates[0], firstFrames);
			var frameWidth = firstFrames[0].Size.Width;
			var frameHeight = firstFrames[0].Size.Height;
			var stampWidth = 2 * frameWidth;
			var stampHeight = 2 * frameHeight;
			var rows = (templates.Length + Columns - 1) / Columns;
			var width = Columns * stampWidth + (Columns - 1) * Gap;
			var height = rows * stampHeight + (rows - 1) * Gap;
			var data = new byte[width * height];

			for (var i = 0; i < templates.Length; i++)
			{
				var frames = frameCache[Images(templates[i]).Single()];
				ValidateFrames(templates[i], frames);
				var stampX = i % Columns * (stampWidth + Gap);
				var stampY = i / Columns * (stampHeight + Gap);
				for (var frame = 0; frame < 4; frame++)
				{
					if (frames[frame].Size.Width != frameWidth || frames[frame].Size.Height != frameHeight)
						throw new InvalidDataException($"Water template {templates[i].Id} has inconsistent frame dimensions.");

					var frameX = stampX + frame % 2 * frameWidth;
					var frameY = stampY + frame / 2 * frameHeight;
					for (var y = 0; y < frameHeight; y++)
						Buffer.BlockCopy(frames[frame].Data, y * frameWidth, data, (frameY + y) * width + frameX, frameWidth);
				}
			}

			var png = new Png(data, SpriteFrameType.Indexed8, width, height, ReadPalette(modData));
			png.Save(outputPath);
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

		static void ValidateFrames(TerrainTemplateInfo template, ISpriteFrame[] frames)
		{
			if (frames.Length != 4 || frames.Any(f => f == null || f.Type != SpriteFrameType.Indexed8))
				throw new InvalidDataException($"Water template {template.Id} does not resolve to four indexed DDF quarter-frames.");
		}

		static Color[] ReadPalette(ModData modData)
		{
			var colors = new Color[Palette.Size];
			using var stream = modData.DefaultFileSystem.Open("OpenSA.PAL");
			for (var i = 0; i < colors.Length; i++)
			{
				var r = stream.ReadUInt8();
				var g = stream.ReadUInt8();
				var b = stream.ReadUInt8();
				var a = stream.ReadUInt8();
				colors[i] = Color.FromArgb(a == 4 ? 0xff : 0x00, r, g, b);
			}

			return colors;
		}
	}
}
