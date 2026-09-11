#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using OpenRA.Mods.Common.Terrain;
using OpenRA.Mods.OpenSA.Rmg.Reassessment;
using OpenRA.Mods.OpenSA.Terrain;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public static class RmgChaosBiomeMix
	{
		public const string CompositeTileset = "CHAOS";
		public const int TemplateStride = 256;
		public static bool IsMixed(RmgGenerationSettings settings) => settings.GeneratorVersion == 25 && settings.ChaosBiomes != RmgChaosBiomes.Single;
		public static string MapTileset(RmgGenerationSettings settings) => IsMixed(settings) ? CompositeTileset : settings.Tileset;
		public static ushort TranslateTemplate(ushort template, byte biome) => checked((ushort)(template + biome * TemplateStride));

		// Macro-aligned regions keep the four frames of every original shore/land tile together.
		// Geography, colony density and complexity never reshuffle a seed's biome plan.
		public static byte[] CreatePlan(RmgGenerationSettings settings)
		{
			var width = settings.MapSize / 2;
			var result = new byte[width * width];
			var primary = Array.IndexOf(RmgBiome.Tilesets, settings.Tileset);
			if (!IsMixed(settings)) { Array.Fill(result, (byte)primary); return result; }
			var fractured = settings.ChaosBiomes == RmgChaosBiomes.Fractured;
			var count = 4 + settings.MapSize / 128;
			if (fractured) count = count * 4 + settings.MapSize / 64;
			var random = new DeterministicRandom(TerrainComparison.Mix(settings.Seed, 2519));
			var centers = new List<RmgPoint>();
			static long Distance(RmgPoint a, RmgPoint b) => (long)(a.X - b.X) * (a.X - b.X) + (long)(a.Y - b.Y) * (a.Y - b.Y);
			for (var n = 0; n < count; n++)
			{
				var candidates = Enumerable.Range(0, 64).Select(_ => new RmgPoint(2 + random.NextInt(width - 4), 2 + random.NextInt(width - 4)));
				centers.Add(candidates.OrderByDescending(p => centers.Select(q => Distance(p, q)).DefaultIfEmpty(width * width).Min()).First());
			}

			var rotation = Enumerable.Range(0, count).Select(_ => random.NextInt(6283) / 1000D).ToArray();
			var stretch = Enumerable.Range(0, count).Select(_ => .7 + random.NextInt(1000) / 1000D).ToArray();
			var kinds = new[] { primary, (primary + 1) % 4, (primary + 2) % 4, (primary + 3) % 4 };
			var grain = Math.Max(3, width / (fractured ? 12D : 6D));
			var cosine = rotation.Select(Math.Cos).ToArray(); var sine = rotation.Select(Math.Sin).ToArray();
			var waveX = rotation.Select(r => Enumerable.Range(0, width).Select(x => Math.Sin(x / grain + r)).ToArray()).ToArray();
			var waveY = rotation.Select(r => Enumerable.Range(0, width).Select(y => Math.Cos(y / grain - r)).ToArray()).ToArray();
			for (var i = 0; i < result.Length; i++)
			{
				var x = i % width; var y = i / width; var best = double.MaxValue;
				for (var n = 0; n < count; n++)
				{
					var dx = x - centers[n].X; var dy = y - centers[n].Y;
					var u = dx * cosine[n] + dy * sine[n];
					var v = dy * cosine[n] - dx * sine[n];
					var ripple = 1 + .27 * waveX[n][x] * waveY[n][y];
					var distance = (u * u * stretch[n] + v * v / stretch[n]) * ripple;
					if (distance < best) { best = distance; result[i] = (byte)kinds[n % 4]; }
				}
			}

			if (result.Distinct().Count() != 4) throw new InvalidOperationException("Chaos must retain all four mixed biomes.");
			return result;
		}

		public static void ApplyDecorations(RmgLogicalMap map, RmgGenerationSettings settings)
		{
			if (!IsMixed(settings)) return;
			var plan = CreatePlan(settings);
			var source = RmgBiome.Decorations(settings.Tileset);
			var translated = RmgBiome.Tilesets.Select(RmgBiome.Decorations).ToArray();
			for (var i = 0; i < map.Actors.Count; i++)
			{
				var actor = map.Actors[i];
				if (actor.Role != "decoration-passable") continue;
				var kind = Array.IndexOf(source, actor.Type);
				if (kind < 0) throw new InvalidDataException("Unknown Chaos decorative role.");
				var biome = plan[actor.LogicalLocation.Y * map.Width + actor.LogicalLocation.X];
				map.Actors[i] = actor with { Type = translated[biome][kind] };
			}
		}

		public static JObject Report(RmgGenerationSettings settings)
		{
			var plan = CreatePlan(settings);
			var width = settings.MapSize / 2;
			var boundaries = 0;
			for (var i = 0; i < plan.Length; i++)
			{
				if (i % width + 1 < width && plan[i] != plan[i + 1]) boundaries++;
				if (i + width < plan.Length && plan[i] != plan[i + width]) boundaries++;
			}

			return new JObject
			{
				["mode"] = settings.ChaosBiomes.ToString().ToLowerInvariant(), ["map_tileset"] = MapTileset(settings),
				["primary_biome"] = settings.Tileset, ["mixed_biome_count"] = plan.Distinct().Count(),
				["macro_cells_by_biome"] = new JObject(RmgBiome.Tilesets.Select((name, i) => new JProperty(name, plan.Count(b => b == i)))),
				["biome_boundary_native_edges"] = boundaries * 2, ["biome_plan_sha256"] = TerrainComparison.Hash(plan)
			};
		}

		// Override an existing spawner instance only in mixed maps. Adding global traits would
		// consume extra synchronized random draws even on the older, single-biome maps.
		public static void ApplyRules(Map map)
		{
			var world = map.RuleDefinitions.Nodes.Single(n => n.Key == "World").Value.Nodes;
			world.Add(new MiniYamlNode("CreepFlyerSpawner@dragonfly", new MiniYaml(null, new List<MiniYamlNode>
			{
				new("Tileset", CompositeTileset), new("ActorTypes", "dragonfly, fly, moth, flying_machine"),
				new("SpawnInterval", "625, 1000"), new("InitialSpawnDelay", "625, 1000")
			})));
			world.Add(new MiniYamlNode("PlantSpawner@NORMAL", new MiniYaml(null, new List<MiniYamlNode>
			{
				new("Tileset", CompositeTileset), new("Maximum", "20"), new("SpawnInterval", "325, 750"), new("InitialSpawnDelay", "325, 750"),
				new("PlantActors", "popcorn, venus, thorn, gumnut, puff, mushroom, freckle, lolly_blue, lolly_orange, lolly_white, lolly_red"),
				new("PlantActorShares", "100, 100, 100, 100, 100, 100, 100, 100, 100, 100, 100")
			})));
			world.Add(new MiniYamlNode("MusicPlaylistBuilder", new MiniYaml(null, new List<MiniYamlNode>
			{
				new("Tilesets", new MiniYaml(null, new List<MiniYamlNode> { new(CompositeTileset, "sounds|DREAMSCAPE") }))
			})));
		}

		public static void ValidateCatalogue(ModData modData)
		{
			var composite = (ITemplatedTerrainInfo)modData.DefaultTerrainInfo[CompositeTileset];
			for (byte biome = 0; biome < RmgBiome.Tilesets.Length; biome++)
			{
				RmgBiome.ValidateCatalogue(modData, RmgBiome.Tilesets[biome]);
				var source = (ITemplatedTerrainInfo)modData.DefaultTerrainInfo[RmgBiome.Tilesets[biome]];
				for (ushort id = 0; id <= 100; id++)
				{
					var original = (CustomTerrainTemplateInfo)source.Templates[id];
					var translated = (CustomTerrainTemplateInfo)composite.Templates[TranslateTemplate(id, biome)];
					if (translated.Size != original.Size || translated.Palette != original.Palette || translated.PickAny != original.PickAny ||
						!translated.Images.SequenceEqual(original.Images) || !translated.Frames.SequenceEqual(original.Frames))
						throw new InvalidDataException("Chaos changed a source biome's native artwork or dimensions.");
					for (var frame = 0; frame < 4; frame++)
					{
						var a = (CustomTerrainTileInfo)original[frame]; var b = (CustomTerrainTileInfo)translated[frame];
						if (source.TerrainTypes[a.TerrainType].Type != composite.TerrainTypes[b.TerrainType].Type ||
							a.ZOffset != b.ZOffset || a.ZRamp != b.ZRamp || a.MinColor != b.MinColor || a.MaxColor != b.MaxColor)
							throw new InvalidDataException("Chaos changed a source biome's terrain, height or preview colors.");
					}
				}
			}
		}
	}
}
