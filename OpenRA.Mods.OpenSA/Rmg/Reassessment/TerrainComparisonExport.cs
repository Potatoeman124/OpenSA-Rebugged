#region Copyright & License Information
/*
 * Copyright The OpenSA Developers (see CREDITS)
 * This file is part of OpenSA, under the GNU General Public License, version 3 or later.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using OpenRA.FileFormats;
using OpenRA.FileSystem;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Terrain;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.OpenSA.Terrain;
using OpenRA.Mods.OpenSA.Widgets;
using OpenRA.Primitives;

namespace OpenRA.Mods.OpenSA.Rmg.Reassessment
{
	public static class TerrainComparisonExport
	{
		public static void Write(ModData modData, TerrainComparisonResult result, string directory)
		{
			if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
				throw new InvalidOperationException("Comparison output must be empty; retain earlier attempts.");
			Directory.CreateDirectory(directory);
			var timer = Stopwatch.StartNew();
			var path = Path.Combine(directory, "terrain.oramap");
			using (var map = CreateMap(modData, result.Map))
			{
				map.Title = $"COMPARISON ONLY {result.Settings.Method} {result.Settings.Complexity} {result.Settings.Seed}";
				using var package = ZipFileLoader.Create(path);
				map.Save(package);
			}

			using var folder = new Folder(directory);
			using var saved = folder.OpenPackage("terrain.oramap", modData.ModFiles);
			using var reloaded = new Map(modData, saved);
			ValidateSemantics(reloaded, result.Map);
			result.Report["package_reload"] = "PASS";
			result.Report["package_sha256"] = TerrainComparison.Hash(File.ReadAllBytes(path));
			result.Report["package_and_validation_ms"] = timer.Elapsed.TotalMilliseconds;
			timer.Restart();

			var native = TerrainComparison.NativeBytes(result.Map);
			File.WriteAllBytes(Path.Combine(directory, "semantic.u8"), native);
			File.WriteAllBytes(Path.Combine(directory, "intent.u8"), result.Intent);
			File.WriteAllBytes(Path.Combine(directory, "water-priority.f64"), Doubles(result.WaterPriority));
			File.WriteAllBytes(Path.Combine(directory, "geology-priority.f64"), Doubles(result.GeologyPriority));
			using (var stream = new BinaryWriter(File.Create(Path.Combine(directory, "templates.u16"))))
				foreach (var template in result.Map.TemplateIds) stream.Write(template);

			var colors = new[]
			{
				Color.FromArgb(255, 168, 131, 78), Color.FromArgb(255, 35, 99, 148),
				Color.FromArgb(255, 147, 143, 124), Color.FromArgb(255, 62, 111, 39)
			};
			var size = result.Settings.Size;
			new Png(native, SpriteFrameType.Indexed8, size, size, colors).Save(Path.Combine(directory, "semantic.png"));
			new Png(result.Intent, SpriteFrameType.Indexed8, size, size, colors).Save(Path.Combine(directory, "intent.png"));
			var renderer = new TextureRenderer(modData);
			renderer.Write(reloaded, Path.Combine(directory, "textures.png"), 0, 0, size, Math.Max(1, size * renderer.CellPixels / 2048));
			var crops = new JArray();
			foreach (var (label, x, y) in new[]
			{
				("north-west", size / 4 - 16, size / 4 - 16), ("center", size / 2 - 16, size / 2 - 16),
				("south-east", size * 3 / 4 - 16, size * 3 / 4 - 16), LargestDirtCrop(native, size)
			})
			{
				renderer.Write(reloaded, Path.Combine(directory, $"crop-{label}.png"), x, y, 32, 1);
				crops.Add(new JObject { ["label"] = label, ["x_native"] = x, ["y_native"] = y, ["side_native"] = 32 });
			}

			result.Report["texture_crops"] = crops;
			result.Report["texture_native_cell_pixels"] = renderer.CellPixels;
			result.Report["artifact_render_ms"] = timer.Elapsed.TotalMilliseconds;
			File.WriteAllText(Path.Combine(directory, "report.json"), result.Report.ToString() + Environment.NewLine);
		}

		public static void VerifyManifest(ModData modData, string path)
		{
			var manifest = JObject.Parse(File.ReadAllText(path));
			foreach (var entry in manifest["cases"])
			{
				var settings = new TerrainComparisonSettings(ulong.Parse((string)entry["seed"]), (int)entry["size"],
					Enum.Parse<TerrainConstruction>((string)entry["method"]), Enum.Parse<TerrainComplexity>((string)entry["complexity"]));
				var directory = Path.Combine(Path.GetDirectoryName(path), $"{settings.Size}-{settings.Seed}-{settings.Method}-{settings.Complexity}");
				var expected = File.ReadAllBytes(Path.Combine(directory, "templates.u16"));
				var result = TerrainComparison.Generate(modData, settings);
				if (!TerrainComparison.NativeBytes(result.Map).SequenceEqual(File.ReadAllBytes(Path.Combine(directory, "semantic.u8"))) ||
					!result.Intent.SequenceEqual(File.ReadAllBytes(Path.Combine(directory, "intent.u8"))) ||
					expected.Length != result.Map.TemplateIds.Length * 2 ||
					result.Map.TemplateIds.Where((id, i) => id != (expected[2 * i] | expected[2 * i + 1] << 8)).Any())
					throw new InvalidDataException($"Reproduction differs from the retained case: {settings.Identity}");
				Console.WriteLine($"Retained intent, semantics, and templates: PASS {settings.Identity}");
			}
		}

		public static void WriteReference(ModData modData, string path, string directory)
		{
			if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
				throw new IOException("Retain earlier reference exports.");
			Directory.CreateDirectory(directory);
			using var folder = new Folder(Path.GetDirectoryName(path));
			using var package = folder.OpenPackage(Path.GetFileName(path), modData.ModFiles);
			using var map = new Map(modData, package);
			var width = map.Bounds.Width;
			if (map.Bounds.Left != 2 || map.Bounds.Top != 2 || map.Bounds.Height != width)
				throw new InvalidDataException("This reference exporter expects square RMG bounds with a two-cell cordon.");
			var native = new byte[width * width];
			for (var y = 0; y < width; y++)
				for (var x = 0; x < width; x++)
					native[y * width + x] = (byte)Enum.Parse<RmgNativeTerrainIntent>(map.GetTerrainInfo(new CPos(x + 2, y + 2)).Type);
			File.WriteAllBytes(Path.Combine(directory, "semantic.u8"), native);
			var renderer = new TextureRenderer(modData);
			renderer.Write(map, Path.Combine(directory, "textures.png"), 0, 0, width, Math.Max(1, width * renderer.CellPixels / 2048));
			foreach (var (label, x, y) in new[]
			{
				("north-west", width / 4 - 16, width / 4 - 16), ("center", width / 2 - 16, width / 2 - 16),
				("south-east", width * 3 / 4 - 16, width * 3 / 4 - 16), LargestDirtCrop(native, width)
			})
				renderer.Write(map, Path.Combine(directory, $"crop-{label}.png"), x, y, 32, 1);
			using var stream = package.GetStream("map.yaml");
			var colonies = NeutralColonyPreview.Read(MiniYaml.FromStream(stream, "map.yaml"));
			var spawns = map.ActorDefinitions.Where(node => node.Value.Value == "mpspawn").Select(node =>
				FieldLoader.GetValue<CPos>("Location", node.Value.Nodes.First(value => value.Key == "Location").Value.Value)).ToArray();
			static JArray Positions(IEnumerable<CPos> cells) => new(cells.Select(cell => new JObject { ["x"] = cell.X, ["y"] = cell.Y }));
			File.WriteAllText(Path.Combine(directory, "reference.json"), new JObject
			{
				["source"] = path, ["title"] = map.Title, ["status"] = "UNMODIFIED_STORED_MAP_REFERENCE",
				["size"] = width, ["bounds_left"] = 2, ["bounds_top"] = 2,
				["source_package_sha256"] = TerrainComparison.Hash(File.ReadAllBytes(path)),
				["metrics"] = TerrainComparison.Measure(native, width),
				["neutral_preview_positions"] = Positions(colonies), ["player_starts"] = Positions(spawns),
				["preview_evidence"] = "Parsed actual saved package; live widget interaction not exercised."
			}.ToString() + Environment.NewLine);
		}

		static byte[] Doubles(double[] values)
		{
			using var stream = new MemoryStream();
			using var writer = new BinaryWriter(stream);
			foreach (var value in values) writer.Write(value);
			return stream.ToArray();
		}

		static (string Label, int X, int Y) LargestDirtCrop(byte[] cells, int width)
		{
			var dp = new int[width + 1, width + 1];
			var side = 0;
			var centerX = width / 2;
			var centerY = width / 2;
			for (var y = 0; y < width; y++)
				for (var x = 0; x < width; x++)
				{
					if (cells[y * width + x] != 0) continue;
					dp[y + 1, x + 1] = 1 + Math.Min(dp[y, x], Math.Min(dp[y + 1, x], dp[y, x + 1]));
					if (dp[y + 1, x + 1] <= side) continue;
					side = dp[y + 1, x + 1];
					centerX = x + 1 - side / 2;
					centerY = y + 1 - side / 2;
				}

			return ("largest-dirt", Math.Clamp(centerX - 16, 0, width - 32), Math.Clamp(centerY - 16, 0, width - 32));
		}

		public static Map CreateMap(ModData modData, RmgLogicalMap logical)
		{
			var size = logical.Width * 2;
			var map = new Map(modData, modData.DefaultTerrainInfo["NORMAL"], size + 4, size + 4)
			{
				RequiresMod = modData.Manifest.Id, Title = "TERRAIN COMPARISON ONLY",
				Author = TerrainComparisonSettings.ExperimentId, Visibility = 0,
				Categories = new[] { "Terrain comparison" }
			};
			map.SetBounds(new PPos(2, 2), new PPos(size + 1, size + 1));
			map.PlayerDefinitions = new MapPlayers(map.Rules, 0).ToMiniYaml();
			for (var y = 0; y < logical.Height; y++)
				for (var x = 0; x < logical.Width; x++)
					for (var frame = 0; frame < 4; frame++)
						map.Tiles[new CPos(2 + 2 * x + frame % 2, 2 + 2 * y + frame / 2)] =
							new TerrainTile(logical.TemplateIds[y * logical.Width + x], (byte)frame);
			return map;
		}

		public static void ValidateSemantics(Map map, RmgLogicalMap logical)
		{
			var width = logical.Width * 2;
			if (map.MapSize.X != width + 4 || map.MapSize.Y != width + 4 ||
				map.Bounds.Left != 2 || map.Bounds.Top != 2 || map.Bounds.Width != width || map.Bounds.Height != width)
				throw new InvalidDataException("Comparison map dimensions changed.");
			for (var y = 0; y < width; y++)
				for (var x = 0; x < width; x++)
				{
					var cell = new CPos(x + 2, y + 2);
					var expected = TerrainComparison.Native(logical, x, y).ToString();
					if (map.GetTerrainInfo(cell).Type != expected || map.Height[cell] != 0)
						throw new InvalidDataException($"Native terrain disagreement at {cell}; expected {expected}.");
				}
		}

		// This experiment checks local sites only. Full placement/combat policy is Stage 3.
		public static bool LocalSiteIsValid(Map map, string actorType, CPos anchor)
		{
			if (!map.Rules.Actors.TryGetValue(actorType, out var actor)) return false;
			bool InBounds(CPos cell) => map.Bounds.Contains(cell.X, cell.Y);
			var footprint = actor.TraitInfos<BuildingInfo>().SelectMany(info => info.Tiles(anchor)).ToArray();
			return footprint.Length > 0 && footprint.All(cell => InBounds(cell) && map.GetTerrainInfo(cell).Type == "Clear") &&
				LocalExitsAreValid(map, actorType, anchor);
		}

		public static bool LocalExitsAreValid(Map map, string actorType, CPos anchor) =>
			map.Rules.Actors[actorType].TraitInfos<ExitInfo>().All(exit =>
				map.Bounds.Contains((anchor + exit.ExitCell).X, (anchor + exit.ExitCell).Y) &&
				map.GetTerrainInfo(anchor + exit.ExitCell).Type != "Water");

		public static void SelfTest(ModData modData, string fixtureDirectory = null)
		{
			static void Require(bool condition, string label)
			{
				if (!condition) throw new InvalidDataException($"Comparison self-test failed: {label}");
				Console.WriteLine($"{label}: PASS");
			}

			var settings = new TerrainComparisonSettings(836781492055UL, 128, TerrainConstruction.Fields, TerrainComplexity.Standard);
			foreach (var method in Enum.GetValues<TerrainConstruction>())
			{
				var first = TerrainComparison.Generate(modData, settings with { Method = method });
				var repeat = TerrainComparison.Generate(modData, settings with { Method = method });
				Require(first.Map.TemplateIds.SequenceEqual(repeat.Map.TemplateIds) &&
					first.Map.NativeTerrainIntents.SequenceEqual(repeat.Map.NativeTerrainIntents), $"{method}-deterministic-templates-and-semantics");
			}

			var logical = new RmgLogicalMap(64, 64);
			for (var y = 0; y < 64; y++)
				for (var x = 29; x <= 34; x++) logical.Obstacles[y * 64 + x] = true;
			var before = (bool[])logical.Obstacles.Clone();
			TerrainComparison.NormalizeWater(logical, new double[64 * 64]);
			Require(before.SequenceEqual(logical.Obstacles), "disconnected-fixture-no-water-carving");
			var profile = RmgProfile.Load(modData, RmgTopologyPreset.NaturalTerrainV10);
			RmgShorelineMaterializer.Materialize(logical, profile, new RmgGenerationSettings
			{
				Seed = 991, GeneratorVersion = 10, MapSize = 128, TopologyPreset = RmgTopologyPreset.NaturalTerrainV10
			});
			Require((int)TerrainComparison.Measure(TerrainComparison.NativeBytes(logical), 128)["dry_components"] == 2,
				"disconnected-fixture-two-native-land-components");
			using var map = CreateMap(modData, logical);
			ValidateSemantics(map, logical);
			foreach (var actorType in profile.NeutralColonyActors)
				Require(LocalSiteIsValid(map, actorType, new CPos(20, 20)) && LocalSiteIsValid(map, actorType, new CPos(90, 90)),
					$"{actorType}-local-sites-on-separate-components");
			var colony = profile.NeutralColonyActors[0];
			var anchor = new CPos(20, 20);
			var actorInfo = map.Rules.Actors[colony];
			var footprint = actorInfo.TraitInfos<BuildingInfo>().SelectMany(info => info.Tiles(anchor)).ToArray();
			var badCell = footprint[0];
			var savedTile = map.Tiles[badCell];
			var waterTile = map.Tiles[new CPos(64, 64)];
			map.Tiles[badCell] = waterTile;
			Require(!LocalSiteIsValid(map, colony, anchor), "invalid-footprint-rejected");
			var rejected = false;
			try { ValidateSemantics(map, logical); }
			catch (InvalidDataException) { rejected = true; }
			Require(rejected, "incorrect-native-semantics-rejected");
			map.Tiles[badCell] = savedTile;
			var exitCell = actorInfo.TraitInfos<ExitInfo>().Select(exit => anchor + exit.ExitCell).First();
			savedTile = map.Tiles[exitCell];
			map.Tiles[exitCell] = waterTile;
			Require(!LocalExitsAreValid(map, colony, anchor), "blocked-production-exit-rejected");
			map.Tiles[exitCell] = savedTile;

			var actors = new List<MiniYamlNode>();
			foreach (var type in profile.NeutralColonyActors)
			{
				var actor = new ActorReference(type) { new LocationInit(new CPos(20 + actors.Count, 30)), new OwnerInit("Creeps") };
				actors.Add(new MiniYamlNode($"Actor{actors.Count}", actor.Save()));
			}

			actors.Add(new MiniYamlNode("PlayerColony", new ActorReference(colony) { new LocationInit(anchor), new OwnerInit("Multi0") }.Save()));
			actors.Add(new MiniYamlNode("Spawn", new ActorReference("mpspawn") { new LocationInit(anchor), new OwnerInit("Neutral") }.Save()));
			Require(NeutralColonyPreview.Read(new[] { new MiniYamlNode("Actors", new MiniYaml(null, actors)) }).Length == profile.NeutralColonyActors.Length,
				"preview-all-neutral-species-excludes-players-and-spawns");
			actors.RemoveAt(0);
			Require(NeutralColonyPreview.Read(new[] { new MiniYamlNode("Actors", new MiniYaml(null, actors)) }).Length == profile.NeutralColonyActors.Length - 1,
				"preview-actual-reduced-count");
			Require(NeutralColonyPreview.Read(Array.Empty<MiniYamlNode>()).Length == 0, "preview-empty-map-clears-markers");

			if (fixtureDirectory != null)
			{
				Directory.CreateDirectory(fixtureDirectory);
				var fixturePath = Path.Combine(fixtureDirectory, "disconnected-local-sites.oramap");
				if (File.Exists(fixturePath)) throw new IOException("Retain the previous disconnected fixture.");
				map.PlayerDefinitions = new MapPlayers(map.Rules, 2).ToMiniYaml();
				foreach (var site in new[] { new CPos(20, 20), new CPos(90, 90) })
					map.ActorDefinitions.Add(new MiniYamlNode($"Colony{map.ActorDefinitions.Count}",
						new ActorReference(colony) { new LocationInit(site), new OwnerInit("Creeps") }.Save()));
				foreach (var site in new[] { new CPos(20, 90), new CPos(90, 20) })
					map.ActorDefinitions.Add(new MiniYamlNode($"Spawn{map.ActorDefinitions.Count}",
						new ActorReference("mpspawn") { new LocationInit(site), new OwnerInit("Neutral") }.Save()));
				using (var package = ZipFileLoader.Create(fixturePath)) map.Save(package);
				using var folder = new Folder(fixtureDirectory);
				using var saved = folder.OpenPackage(Path.GetFileName(fixturePath), modData.ModFiles);
				using var reloaded = new Map(modData, saved);
				ValidateSemantics(reloaded, logical);
				using var yaml = saved.GetStream("map.yaml");
				var savedColonies = NeutralColonyPreview.Read(MiniYaml.FromStream(yaml, "map.yaml"));
				Require(savedColonies.SequenceEqual(new[] { new CPos(20, 20), new CPos(90, 90) }) &&
					savedColonies.All(site => LocalSiteIsValid(reloaded, colony, site)), "saved-disconnected-colonies-and-preview-locations");
				foreach (var starting in modData.DefaultRules.Actors[SystemActors.World].TraitInfos<StartingUnitsInfo>()
					.Where(info => !string.IsNullOrEmpty(info.BaseActor)))
					Require(new[] { new CPos(20, 90), new CPos(90, 20) }.All(site =>
						LocalSiteIsValid(reloaded, starting.BaseActor, site + starting.BaseActorOffset)),
						$"saved-{starting.BaseActor}-starting-offset-and-local-exits");
				File.WriteAllText(Path.Combine(fixtureDirectory, "fixture-policy.json"), new JObject
				{
					["experiment_id"] = TerrainComparisonSettings.ExperimentId,
					["cross_map_accessibility"] = "NOT_REQUIRED",
					["dry_components"] = 2, ["actual_neutral_colonies"] = savedColonies.Length,
					["saved_native_semantics"] = "PASS", ["saved_local_sites_and_exits"] = "PASS",
					["combat_separation_validation"] = "DEFERRED_TO_PLAYER_PLACEMENT_STAGE",
					["runtime_unit_production"] = "NOT_TESTED",
					["package_sha256"] = TerrainComparison.Hash(File.ReadAllBytes(fixturePath))
				}.ToString() + Environment.NewLine);
			}
		}

		sealed class TextureRenderer
		{
			readonly FrameCache frames;
			readonly ITemplatedTerrainInfo terrain;
			readonly Color[] palette;
			public int CellPixels { get; }

			public TextureRenderer(ModData modData)
			{
				frames = new FrameCache(modData.DefaultFileSystem, modData.SpriteLoaders);
				terrain = (ITemplatedTerrainInfo)modData.DefaultTerrainInfo["NORMAL"];
				CellPixels = Frames(terrain.Templates.Values.First().Id)[0].Size.Width;
				palette = new Color[Palette.Size];
				using var stream = modData.DefaultFileSystem.Open("OpenSA.PAL");
				for (var i = 0; i < palette.Length; i++)
				{
					var r = stream.ReadUInt8(); var g = stream.ReadUInt8(); var b = stream.ReadUInt8(); var a = stream.ReadUInt8();
					palette[i] = Color.FromArgb(a == 4 ? 255 : 0, r, g, b);
				}
			}

			ISpriteFrame[] Frames(ushort id)
			{
				var images = terrain.Templates[id] switch
				{
					CustomTerrainTemplateInfo custom => custom.Images,
					DefaultTerrainTemplateInfo standard => standard.Images,
					_ => throw new InvalidDataException("Unknown terrain template type.")
				};
				return frames[images.Single()];
			}

			public void Write(Map map, string path, int nativeX, int nativeY, int nativeSide, int stride)
			{
				var side = nativeSide * CellPixels / stride;
				var pixels = new byte[side * side];
				for (var y = 0; y < side; y++)
					for (var x = 0; x < side; x++)
					{
						var px = x * stride;
						var py = y * stride;
						var tile = map.Tiles[new CPos(2 + nativeX + px / CellPixels, 2 + nativeY + py / CellPixels)];
						var frame = Frames(tile.Type)[tile.Index];
						if (frame.Type != SpriteFrameType.Indexed8 || frame.Size.Width != CellPixels || frame.Size.Height != CellPixels)
							throw new InvalidDataException("Comparison texture renderer needs square indexed native frames.");
						pixels[y * side + x] = frame.Data[py % CellPixels * CellPixels + px % CellPixels];
					}

				new Png(pixels, SpriteFrameType.Indexed8, side, side, palette).Save(path);
			}
		}
	}
}
