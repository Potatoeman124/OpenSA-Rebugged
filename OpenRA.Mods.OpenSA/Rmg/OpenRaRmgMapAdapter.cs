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
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using OpenRA.FileSystem;
using OpenRA.Mods.Common.Lint;
using OpenRA.Mods.Common.Terrain;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public sealed class RmgPackageResult
	{
		public RmgGenerationResult Generation { get; init; }
		public string OutputPath { get; init; }
		public string EngineUid { get; init; }
		public string CanonicalMapHash { get; init; }
		public RmgNativeMovementValidationResult NativeMovementValidation { get; init; }
		public JObject Report { get; init; }
	}

	public sealed class RmgPackageValidationResult
	{
		public string EngineUid { get; init; }
		public string CanonicalMapHash { get; init; }
		public RmgNativeMovementValidationResult NativeMovementValidation { get; init; }
		public string[] YamlLintErrors { get; init; }
		public string[] YamlLintWarnings { get; init; }
		public int PlayablePlayers { get; init; }
		public int SpawnActors { get; init; }
		public int NeutralColonies { get; init; }

		public JObject ToJson()
		{
			return new JObject
			{
				["package_reload"] = "passed",
				["rules_sequences_initialization"] = "passed",
				["map_yaml_lint"] = YamlLintErrors.Length == 0 ? "passed" : "failed",
				["map_yaml_lint_errors"] = new JArray(YamlLintErrors),
				["map_yaml_lint_warnings"] = new JArray(YamlLintWarnings),
				["playable_players"] = PlayablePlayers,
				["spawn_actors"] = SpawnActors,
				["neutral_colonies"] = NeutralColonies,
				["world_initialization"] = "not-run: World constructor is internal to OpenRA.Game and requires live lobby/order/renderer state"
			};
		}
	}

	public sealed class RmgPackageValidationException : Exception
	{
		public RmgPackageValidationResult Validation { get; }

		public RmgPackageValidationException(string message, RmgPackageValidationResult validation)
			: base(message)
		{
			Validation = validation;
		}
	}

	public static class OpenRaRmgMapAdapter
	{
		public static RmgPackageResult GenerateAndSave(ModData modData, RmgProfile profile, RmgGenerationSettings settings, string outputPath,
			bool overwrite, RmgMovementValidationMode movementValidationMode = RmgMovementValidationMode.Both)
		{
			Game.ModData = modData;
			outputPath = Path.GetFullPath(outputPath);
			var outputDirectory = Path.GetDirectoryName(outputPath);
			if (string.IsNullOrEmpty(outputDirectory))
				throw new InvalidOperationException("The output path does not have a parent directory.");

			if (!outputPath.EndsWith(".oramap", StringComparison.OrdinalIgnoreCase))
				throw new ArgumentException("The output file must use the .oramap extension.");
			if (File.Exists(outputPath) && !overwrite)
				throw new IOException($"Output already exists: {outputPath}. Pass --overwrite to replace it.");

			Directory.CreateDirectory(outputDirectory);
			ValidateProfileAgainstModData(modData, profile);

			var generation = RmgGenerator.Generate(profile, settings);
			var repeat = RmgGenerator.Generate(profile, settings);
			if (generation.LogicalHash != repeat.LogicalHash || generation.ActorHash != repeat.ActorHash || generation.GraphHash != repeat.GraphHash)
				throw new InvalidOperationException("Same-process repeatability validation failed for the selected seed and settings.");
			if (!generation.Validation.Accepted)
				throw new InvalidOperationException("Generation failed hard validation: " + string.Join("; ", generation.Validation.HardFailures.Select(f => $"{f.Code}: {f.Message}")));

			var temporaryPath = Path.Combine(outputDirectory, $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
			try
			{
				MaterializeAndSave(modData, generation, temporaryPath);
				var packageValidation = ReloadAndValidate(modData, generation, temporaryPath, movementValidationMode);
				if (packageValidation.YamlLintErrors.Length > 0)
					throw new RmgPackageValidationException(
						"Generated map failed YAML lint: " + string.Join("; ", packageValidation.YamlLintErrors), packageValidation);
				if (packageValidation.NativeMovementValidation != null && !packageValidation.NativeMovementValidation.Accepted)
					throw new RmgPackageValidationException(
						"Generated map failed native movement validation: " + string.Join("; ",
							packageValidation.NativeMovementValidation.HardFailures.Select(f => $"{f.Code}: {f.Message}")),
						packageValidation);

				if (File.Exists(outputPath))
					File.Delete(outputPath);
				File.Move(temporaryPath, outputPath);

				var report = BuildReport(generation, outputPath, packageValidation, movementValidationMode);
				return new RmgPackageResult
				{
					Generation = generation,
					OutputPath = outputPath,
					EngineUid = packageValidation.EngineUid,
					CanonicalMapHash = packageValidation.CanonicalMapHash,
					NativeMovementValidation = packageValidation.NativeMovementValidation,
					Report = report
				};
			}
			finally
			{
				if (File.Exists(temporaryPath))
					File.Delete(temporaryPath);
			}
		}

		static void ValidateProfileAgainstModData(ModData modData, RmgProfile profile)
		{
			if (!modData.DefaultTerrainInfo.TryGetValue(profile.Tileset, out var terrainInfo))
				throw new InvalidDataException($"Tileset {profile.Tileset} is not available.");
			if (terrainInfo is not ITemplatedTerrainInfo templated)
				throw new InvalidDataException($"Tileset {profile.Tileset} is not template-based.");

			foreach (var id in profile.ClearTemplateIds)
			{
				if (!templated.Templates.TryGetValue(id, out var template))
					throw new InvalidDataException($"Configured Clear template {id} does not exist in {profile.Tileset}.");
				if (template.Size.X != 2 || template.Size.Y != 2 || template.TilesCount != 4)
					throw new InvalidDataException($"Configured template {id} is not a complete 2x2 macro template.");

				for (var frame = 0; frame < template.TilesCount; frame++)
				{
					var tile = template[frame];
					if (tile == null || !string.Equals(terrainInfo.TerrainTypes[tile.TerrainType].Type, "Clear", StringComparison.OrdinalIgnoreCase))
						throw new InvalidDataException($"Configured template {id}, frame {frame} is not homogeneous Clear terrain.");
				}
			}

			foreach (var actor in profile.NeutralColonyActors.Append(profile.SpawnActor))
				if (!modData.DefaultRules.Actors.ContainsKey(actor))
					throw new InvalidDataException($"Configured RMG actor '{actor}' is not defined by the mod rules.");
		}

		static void MaterializeAndSave(ModData modData, RmgGenerationResult generation, string outputPath)
		{
			var profile = generation.Profile;
			var storedWidth = profile.PlayableWidth + 2 * profile.CordonWidth;
			var storedHeight = profile.PlayableHeight + 2 * profile.CordonWidth;
			var terrainInfo = modData.DefaultTerrainInfo[profile.Tileset];
			using var map = new Map(modData, terrainInfo, storedWidth, storedHeight)
			{
				RequiresMod = modData.Manifest.Id,
				Title = $"OpenSA RMG {ArchetypeName(generation.Settings.Archetype)} {generation.Settings.Seed}",
				Author = $"OpenSA RMG v{generation.Settings.GeneratorVersion}",
				Visibility = MapVisibility.Lobby,
				Categories = new[] { "Conquest" }
			};

			var topLeft = new PPos(profile.CordonWidth, profile.CordonWidth);
			var bottomRight = new PPos(profile.CordonWidth + profile.PlayableWidth - 1, profile.CordonWidth + profile.PlayableHeight - 1);
			map.SetBounds(topLeft, bottomRight);

			for (var logicalY = 0; logicalY < profile.LogicalHeight; logicalY++)
				for (var logicalX = 0; logicalX < profile.LogicalWidth; logicalX++)
				{
					var logical = new RmgPoint(logicalX, logicalY);
					var template = generation.Map.TemplateIds[generation.Map.Index(logical)];
					var nativeX = profile.CordonWidth + 2 * logicalX;
					var nativeY = profile.CordonWidth + 2 * logicalY;
					for (var frame = 0; frame < 4; frame++)
						map.Tiles[new CPos(nativeX + frame % 2, nativeY + frame / 2)] = new TerrainTile(template, (byte)frame);
				}

			map.PlayerDefinitions = new MapPlayers(map.Rules, generation.Settings.PlayerCount).ToMiniYaml();
			foreach (var plan in generation.Map.Actors)
			{
				var location = ToNative(plan.LogicalLocation, profile);
				var actor = new ActorReference(plan.Type)
				{
					new LocationInit(location),
					new OwnerInit(plan.Owner)
				};
				map.ActorDefinitions.Add(new MiniYamlNode($"Actor{map.ActorDefinitions.Count}", actor.Save()));
			}

			using var package = ZipFileLoader.Create(outputPath);
			map.Save(package);
		}

		static RmgPackageValidationResult ReloadAndValidate(ModData modData, RmgGenerationResult generation, string path,
			RmgMovementValidationMode movementValidationMode)
		{
			var directory = Path.GetDirectoryName(path);
			using var folder = new Folder(directory);
			using var package = folder.OpenPackage(Path.GetFileName(path), modData.ModFiles);
			if (package == null)
				throw new InvalidDataException("The saved .oramap could not be reopened as an OpenRA package.");

			using var reloaded = new Map(modData, package);
			var profile = generation.Profile;
			if (reloaded.MapFormat != Map.CurrentMapFormat || reloaded.RequiresMod != modData.Manifest.Id || reloaded.Tileset != profile.Tileset)
				throw new InvalidDataException("Reloaded map metadata does not match the Generator Version 1 contract.");
			if (reloaded.MapSize.X != profile.PlayableWidth + 2 * profile.CordonWidth || reloaded.MapSize.Y != profile.PlayableHeight + 2 * profile.CordonWidth)
				throw new InvalidDataException("Reloaded map storage dimensions do not match the generated dimensions.");
			if (reloaded.Bounds.Left != profile.CordonWidth || reloaded.Bounds.Top != profile.CordonWidth || reloaded.Bounds.Width != profile.PlayableWidth || reloaded.Bounds.Height != profile.PlayableHeight)
				throw new InvalidDataException("Reloaded playable bounds do not match the generated bounds.");

			var players = new MapPlayers(reloaded.PlayerDefinitions).Players.Values.Count(p => p.Playable);
			if (players != generation.Settings.PlayerCount)
				throw new InvalidDataException($"Reloaded map has {players} playable players; expected {generation.Settings.PlayerCount}.");

			var actorCounts = ReadActorCounts(reloaded);
			if (!actorCounts.TryGetValue(profile.SpawnActor, out var spawnCount) || spawnCount != generation.Settings.PlayerCount)
				throw new InvalidDataException("Reloaded map does not contain exactly one mpspawn per player.");
			var colonyCount = profile.NeutralColonyActors.Sum(type => actorCounts.TryGetValue(type, out var count) ? count : 0);
			if (colonyCount != generation.Settings.NeutralColonyCount)
				throw new InvalidDataException("Reloaded map neutral-colony count differs from the generation plan.");

			for (var y = reloaded.Bounds.Top; y < reloaded.Bounds.Bottom; y++)
				for (var x = reloaded.Bounds.Left; x < reloaded.Bounds.Right; x++)
					if (!string.Equals(reloaded.GetTerrainInfo(new CPos(x, y)).Type, "Clear", StringComparison.OrdinalIgnoreCase))
						throw new InvalidDataException($"Reloaded native cell {x},{y} is not Clear terrain.");

			var (lintErrors, lintWarnings) = RunMapLint(modData, reloaded);
			var nativeMovement = movementValidationMode == RmgMovementValidationMode.Proxy ? null : NativeMovementValidator.Validate(reloaded, generation);
			return new RmgPackageValidationResult
			{
				EngineUid = Map.ComputeUID(package),
				CanonicalMapHash = CanonicalMapHash(package),
				NativeMovementValidation = nativeMovement,
				YamlLintErrors = lintErrors,
				YamlLintWarnings = lintWarnings,
				PlayablePlayers = players,
				SpawnActors = spawnCount,
				NeutralColonies = colonyCount
			};
		}

		static (string[] Errors, string[] Warnings) RunMapLint(ModData modData, Map map)
		{
			var errors = new List<string>();
			var warnings = new List<string>();
			if (map.InvalidCustomRules)
				errors.Add(map.InvalidCustomRulesException.ToString());
			else
				foreach (var passType in modData.ObjectCreator.GetTypesImplementing<ILintMapPass>().OrderBy(t => t.FullName, StringComparer.Ordinal))
				{
					try
					{
						var pass = (ILintMapPass)modData.ObjectCreator.CreateBasic(passType);
						pass.Run(errors.Add, warnings.Add, modData, map);
					}
					catch (Exception e)
					{
						errors.Add($"{passType.FullName} failed with exception: {e}");
					}
				}

			return (errors.ToArray(), warnings.ToArray());
		}

		static Dictionary<string, int> ReadActorCounts(Map map)
		{
			var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			foreach (var definition in map.ActorDefinitions)
			{
				var reference = new ActorReference(definition.Value.Value, definition.Value.ToDictionary());
				counts.TryGetValue(reference.Type, out var count);
				counts[reference.Type] = count + 1;
			}

			return counts;
		}

		static string CanonicalMapHash(IReadOnlyPackage package)
		{
			using var yaml = package.GetStream("map.yaml");
			using var binary = package.GetStream("map.bin");
			var yamlBytes = yaml.ReadAllBytes();
			var binaryBytes = binary.ReadAllBytes();
			var combined = new byte[yamlBytes.Length + binaryBytes.Length];
			Buffer.BlockCopy(yamlBytes, 0, combined, 0, yamlBytes.Length);
			Buffer.BlockCopy(binaryBytes, 0, combined, yamlBytes.Length, binaryBytes.Length);
			return Convert.ToHexString(SHA256.HashData(combined)).ToLowerInvariant();
		}

		static JObject BuildReport(RmgGenerationResult generation, string outputPath, RmgPackageValidationResult packageValidation,
			RmgMovementValidationMode movementValidationMode)
		{
			var settings = generation.Settings;
			return new JObject
			{
				["schema_version"] = 2,
				["generator_version"] = settings.GeneratorVersion,
				["configuration_id"] = generation.Profile.ProfileId,
				["configuration_version"] = generation.Profile.ConfigurationVersion,
				["seed"] = settings.Seed.ToString(),
				["players"] = settings.PlayerCount,
				["symmetry"] = SymmetryName(settings.Symmetry),
				["archetype"] = ArchetypeName(settings.Archetype),
				["neutral_colonies"] = settings.NeutralColonyCount,
				["output"] = outputPath,
				["logical_hash_sha256"] = generation.LogicalHash,
				["actor_hash_sha256"] = generation.ActorHash,
				["graph_hash_sha256"] = generation.GraphHash,
				["canonical_map_yaml_bin_sha256"] = packageValidation.CanonicalMapHash,
				["engine_uid_sha1"] = packageValidation.EngineUid,
				["obstacle_stage"] = "validated-zero-density-no-op",
				["validation"] = generation.Validation.ToJson(),
				["movement_validation"] = new JObject
				{
					["mode"] = MovementValidationName(movementValidationMode),
					["proxy"] = new JObject
					{
						["accepted"] = generation.Validation.Metrics["native_proxy_reachable_starts"] == settings.PlayerCount,
						["reachable_starts"] = generation.Validation.Metrics["native_proxy_reachable_starts"],
						["passable_cells"] = generation.Validation.Metrics["native_proxy_passable_cells"],
						["maximum_start_distance"] = generation.Validation.Metrics["native_proxy_max_start_distance"]
					},
					["native"] = packageValidation.NativeMovementValidation?.ToJson()
				},
				["package_validation"] = packageValidation.ToJson()
			};
		}

		public static CPos ToNative(RmgPoint point, RmgProfile profile) =>
			new(profile.CordonWidth + 2 * point.X, profile.CordonWidth + 2 * point.Y);

		public static string SymmetryName(RmgSymmetry symmetry) => symmetry switch
		{
			RmgSymmetry.MirrorHorizontal => "horizontal",
			RmgSymmetry.MirrorVertical => "vertical",
			RmgSymmetry.Rotate180 => "rotational",
			_ => throw new ArgumentOutOfRangeException(nameof(symmetry))
		};

		public static string ArchetypeName(RmgArchetype archetype) => archetype switch
		{
			RmgArchetype.Open => "open",
			RmgArchetype.CentralContest => "central-contest",
			_ => throw new ArgumentOutOfRangeException(nameof(archetype))
		};

		public static string MovementValidationName(RmgMovementValidationMode mode) => mode switch
		{
			RmgMovementValidationMode.Proxy => "proxy",
			RmgMovementValidationMode.Native => "native",
			RmgMovementValidationMode.Both => "both",
			_ => throw new ArgumentOutOfRangeException(nameof(mode))
		};
	}
}
