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
using System.Diagnostics;
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
		public RmgPackagePerformance Performance { get; init; }
		public JObject Report { get; init; }
	}

	public sealed class RmgPackagePerformance
	{
		public double ProfileValidationMilliseconds { get; set; }
		public double LogicalGenerationMilliseconds { get; set; }
		public double MaterializationSaveMilliseconds { get; set; }
		public double PackageReloadMetadataMilliseconds { get; set; }
		public double YamlLintMilliseconds { get; set; }
		public double NativeMovementValidationMilliseconds { get; set; }
		public double IdentityHashMilliseconds { get; set; }
		public double TotalMilliseconds { get; set; }

		public JObject ToJson()
		{
			return new JObject
			{
				["profile_validation_ms"] = Rounded(ProfileValidationMilliseconds),
				["logical_generation_and_repeat_ms"] = Rounded(LogicalGenerationMilliseconds),
				["materialization_and_save_ms"] = Rounded(MaterializationSaveMilliseconds),
				["package_reload_and_metadata_ms"] = Rounded(PackageReloadMetadataMilliseconds),
				["yaml_lint_ms"] = Rounded(YamlLintMilliseconds),
				["native_movement_validation_ms"] = Rounded(NativeMovementValidationMilliseconds),
				["identity_hash_ms"] = Rounded(IdentityHashMilliseconds),
				["total_ms"] = Rounded(TotalMilliseconds)
			};
		}

		static double Rounded(double value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);
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
		public RmgPackagePerformance Performance { get; init; }

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
				["performance"] = Performance?.ToJson(),
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
			var totalTimer = Stopwatch.StartNew();
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
			var profileTimer = Stopwatch.StartNew();
			ValidateProfileAgainstModData(modData, profile);
			profileTimer.Stop();

			var generationTimer = Stopwatch.StartNew();
			var generation = RmgGenerator.Generate(profile, settings);
			var repeat = RmgGenerator.Generate(profile, settings);
			generationTimer.Stop();
			if (generation.LogicalHash != repeat.LogicalHash || generation.ActorHash != repeat.ActorHash || generation.GraphHash != repeat.GraphHash)
				throw new InvalidOperationException("Same-process repeatability validation failed for the selected seed and settings.");
			if (!generation.Validation.Accepted)
				throw new InvalidOperationException("Generation failed hard validation: " + string.Join("; ", generation.Validation.HardFailures.Select(f => $"{f.Code}: {f.Message}")));

			var temporaryPath = Path.Combine(outputDirectory, $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
			try
			{
				var materializationTimer = Stopwatch.StartNew();
				MaterializeAndSave(modData, generation, temporaryPath);
				materializationTimer.Stop();
				var packageValidation = ReloadAndValidate(modData, generation, temporaryPath, movementValidationMode);
				packageValidation.Performance.ProfileValidationMilliseconds = profileTimer.Elapsed.TotalMilliseconds;
				packageValidation.Performance.LogicalGenerationMilliseconds = generationTimer.Elapsed.TotalMilliseconds;
				packageValidation.Performance.MaterializationSaveMilliseconds = materializationTimer.Elapsed.TotalMilliseconds;
				packageValidation.Performance.TotalMilliseconds = totalTimer.Elapsed.TotalMilliseconds;
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
				totalTimer.Stop();
				packageValidation.Performance.TotalMilliseconds = totalTimer.Elapsed.TotalMilliseconds;

				var report = BuildReport(generation, outputPath, packageValidation, movementValidationMode);
				return new RmgPackageResult
				{
					Generation = generation,
					OutputPath = outputPath,
					EngineUid = packageValidation.EngineUid,
					CanonicalMapHash = packageValidation.CanonicalMapHash,
					NativeMovementValidation = packageValidation.NativeMovementValidation,
					Performance = packageValidation.Performance,
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

			ValidateTemplates(profile.ClearTemplateIds, "Clear");
			if (profile.UsesClearLandDetails)
				ValidateTemplates(profile.ClearLandDetailTemplateIds, "Clear");
			if (profile.GeneratorVersion >= 2)
				ValidateTemplates(profile.BlockedTemplateIds, "Water");
			if (profile.UsesShorelineMaterialization)
			{
				ValidateTemplates(profile.OpenWaterDetailTemplateIds, "Water");
				foreach (var transition in NormalWaterTransitionCatalogue.Entries)
					ValidateTransition(transition);
				if (profile.UsesLandCover)
					foreach (var landTemplate in NormalLandTransitionCatalogue.Entries)
						ValidateLandTemplate(landTemplate);
			}

			void ValidateTemplates(IEnumerable<ushort> templateIds, string expectedTerrain)
			{
				foreach (var id in templateIds)
				{
					if (!templated.Templates.TryGetValue(id, out var template))
						throw new InvalidDataException($"Configured {expectedTerrain} template {id} does not exist in {profile.Tileset}.");
					if (template.Size.X != 2 || template.Size.Y != 2 || template.TilesCount != 4)
						throw new InvalidDataException($"Configured template {id} is not a complete 2x2 macro template.");

					for (var frame = 0; frame < template.TilesCount; frame++)
					{
						var tile = template[frame];
						if (tile == null || !string.Equals(terrainInfo.TerrainTypes[tile.TerrainType].Type, expectedTerrain, StringComparison.OrdinalIgnoreCase))
							throw new InvalidDataException($"Configured template {id}, frame {frame} is not homogeneous {expectedTerrain} terrain.");
					}
				}
			}

			void ValidateTransition(NormalWaterTransition transition)
			{
				if (!templated.Templates.TryGetValue(transition.TemplateId, out var template))
					throw new InvalidDataException($"Audited NORMAL transition {transition.TemplateId} does not exist in the active tileset.");
				if (template.Size.X != 2 || template.Size.Y != 2 || template.TilesCount != 4)
					throw new InvalidDataException($"Audited NORMAL transition {transition.TemplateId} is not a complete 2x2 macro template.");

				for (var frame = 0; frame < 4; frame++)
				{
					var tile = template[frame];
					var expected = transition.NativeTerrain[frame].ToString();
					var actual = tile == null ? "missing" : terrainInfo.TerrainTypes[tile.TerrainType].Type;
					if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
						throw new InvalidDataException($"Audited NORMAL transition {transition.TemplateId}, frame {frame} is {actual}; expected {expected}.");
				}
			}

			void ValidateLandTemplate(NormalLandTemplate landTemplate)
			{
				if (!templated.Templates.TryGetValue(landTemplate.TemplateId, out var template))
					throw new InvalidDataException($"Audited NORMAL land template {landTemplate.TemplateId} does not exist in the active tileset.");
				if (template.Size.X != 2 || template.Size.Y != 2 || template.TilesCount != 4)
					throw new InvalidDataException($"Audited NORMAL land template {landTemplate.TemplateId} is not a complete 2x2 macro template.");

				for (var frame = 0; frame < 4; frame++)
				{
					var tile = template[frame];
					var expected = landTemplate.NativeTerrain[frame].ToString();
					var actual = tile == null ? "missing" : terrainInfo.TerrainTypes[tile.TerrainType].Type;
					if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
						throw new InvalidDataException($"Audited NORMAL land template {landTemplate.TemplateId}, frame {frame} is {actual}; expected {expected}.");
				}
			}

			foreach (var actor in profile.NeutralColonyActors.Append(profile.SpawnActor)
				.Concat(profile.SoilDecorationActors)
				.Concat(profile.RockDecorationActors)
				.Concat(profile.VegetationDecorationActors)
				.Distinct())
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
				Title = generation.Settings.PlayerSettingsResolution != null ?
					generation.Settings.GeneratorVersion >= 7 ?
						$"OpenSA RMG {RmgPlayerSettingsContract.PresetDisplayName(generation.Settings.PlayerSettingsResolution.Requested.Preset)} {RmgPlayerSettingsContract.LayoutFamilyDisplayName(generation.Settings.LayoutFamily)} W-{generation.Settings.WaterAmount} T-{generation.Settings.TacticalTerrain} {generation.Settings.Seed}" :
						$"OpenSA RMG {RmgPlayerSettingsContract.PresetDisplayName(generation.Settings.PlayerSettingsResolution.Requested.Preset)} {generation.Settings.Seed}" :
					generation.Profile.GeneratorVersion == 1 ?
						$"OpenSA RMG {ArchetypeName(generation.Settings.Archetype)} {generation.Settings.Seed}" :
						$"OpenSA RMG {ArchetypeName(generation.Settings.Archetype)} {TopologyName(generation.Settings.TopologyPreset)} {generation.Settings.Seed}",
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
				var logicalLocation = ToNative(plan.LogicalLocation, profile);
				var location = new CPos(logicalLocation.X + plan.NativeFrame % 2,
					logicalLocation.Y + plan.NativeFrame / 2);
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
			var reloadTimer = Stopwatch.StartNew();
			var directory = Path.GetDirectoryName(path);
			using var folder = new Folder(directory);
			using var package = folder.OpenPackage(Path.GetFileName(path), modData.ModFiles);
			if (package == null)
				throw new InvalidDataException("The saved .oramap could not be reopened as an OpenRA package.");

			using var reloaded = new Map(modData, package);
			var profile = generation.Profile;
			if (reloaded.MapFormat != Map.CurrentMapFormat || reloaded.RequiresMod != modData.Manifest.Id || reloaded.Tileset != profile.Tileset)
				throw new InvalidDataException($"Reloaded map metadata does not match the Generator Version {profile.GeneratorVersion} contract.");
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
			var plannedColonyCount = generation.Map.Actors.Count(actor => actor.Owner == profile.ColonyOwner);
			if (colonyCount != plannedColonyCount)
				throw new InvalidDataException("Reloaded map neutral-colony count differs from the generation plan.");

			for (var logicalY = 0; logicalY < profile.LogicalHeight; logicalY++)
				for (var logicalX = 0; logicalX < profile.LogicalWidth; logicalX++)
				{
					var logical = new RmgPoint(logicalX, logicalY);
					var logicalIndex = generation.Map.Index(logical);
					for (var dy = 0; dy < 2; dy++)
						for (var dx = 0; dx < 2; dx++)
						{
							var cell = new CPos(profile.CordonWidth + 2 * logicalX + dx, profile.CordonWidth + 2 * logicalY + dy);
							var frame = 2 * dy + dx;
							var expectedTerrain = profile.UsesShorelineMaterialization ?
								generation.Map.NativeTerrainIntents[4 * logicalIndex + frame].ToString() :
								generation.Map.Obstacles[logicalIndex] ? "Water" : "Clear";
							var actualTerrain = reloaded.GetTerrainInfo(cell).Type;
							if (!string.Equals(actualTerrain, expectedTerrain, StringComparison.OrdinalIgnoreCase))
								throw new InvalidDataException($"Reloaded native cell {cell} is {actualTerrain}; native terrain intent requires {expectedTerrain}.");
							if (reloaded.Height[cell] != 0)
								throw new InvalidDataException($"Reloaded native cell {cell} has height {reloaded.Height[cell]}; the RMG contract requires height zero.");
						}
				}

			reloadTimer.Stop();

			var lintTimer = Stopwatch.StartNew();
			var (lintErrors, lintWarnings) = RunMapLint(modData, reloaded);
			lintTimer.Stop();
			var nativeTimer = Stopwatch.StartNew();
			var nativeMovement = movementValidationMode == RmgMovementValidationMode.Proxy ? null : NativeMovementValidator.Validate(reloaded, generation);
			nativeTimer.Stop();
			var identityTimer = Stopwatch.StartNew();
			var engineUid = Map.ComputeUID(package);
			var canonicalMapHash = CanonicalMapHash(package);
			identityTimer.Stop();
			return new RmgPackageValidationResult
			{
				EngineUid = engineUid,
				CanonicalMapHash = canonicalMapHash,
				NativeMovementValidation = nativeMovement,
				YamlLintErrors = lintErrors,
				YamlLintWarnings = lintWarnings,
				PlayablePlayers = players,
				SpawnActors = spawnCount,
				NeutralColonies = colonyCount,
				Performance = new RmgPackagePerformance
				{
					PackageReloadMetadataMilliseconds = reloadTimer.Elapsed.TotalMilliseconds,
					YamlLintMilliseconds = lintTimer.Elapsed.TotalMilliseconds,
					NativeMovementValidationMilliseconds = nativeTimer.Elapsed.TotalMilliseconds,
					IdentityHashMilliseconds = identityTimer.Elapsed.TotalMilliseconds
				}
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
			var report = new JObject
			{
				["schema_version"] = generation.Profile.GeneratorVersion >= 10 ? 12 : generation.Profile.GeneratorVersion >= 9 ? 11 : generation.Profile.GeneratorVersion >= 8 ? 10 : generation.Profile.GeneratorVersion >= 7 ? 9 : generation.Profile.GeneratorVersion >= 6 ? 8 : generation.Profile.GeneratorVersion >= 5 ? 6 : generation.Profile.GeneratorVersion >= 4 ? 5 : generation.Profile.GeneratorVersion >= 3 ? 4 : generation.Profile.GeneratorVersion >= 2 ? 3 : 2,
				["generator_version"] = settings.GeneratorVersion,
				["configuration_id"] = generation.Profile.ProfileId,
				["configuration_version"] = generation.Profile.ConfigurationVersion,
				["seed"] = settings.Seed.ToString(),
				["players"] = settings.PlayerCount,
				["symmetry"] = SymmetryName(settings.Symmetry),
				["archetype"] = ArchetypeName(settings.Archetype),
				["topology_preset"] = TopologyName(settings.TopologyPreset),
				["layout_family"] = RmgPlayerSettingsContract.LayoutFamilyName(settings.LayoutFamily),
				["neutral_colonies"] = generation.Map.Actors.Count(actor => actor.Owner == generation.Profile.ColonyOwner),
				["neutral_colonies_requested"] = settings.NeutralColonyCount,
				["output"] = outputPath,
				["player_settings"] = settings.PlayerSettingsResolution?.ToJson(),
				["logical_hash_sha256"] = generation.LogicalHash,
				["actor_hash_sha256"] = generation.ActorHash,
				["graph_hash_sha256"] = generation.GraphHash,
				["canonical_map_yaml_bin_sha256"] = packageValidation.CanonicalMapHash,
				["engine_uid_sha1"] = packageValidation.EngineUid,
				["obstacle_stage"] = generation.Profile.GeneratorVersion switch
				{
					1 => "validated-zero-density-no-op",
					2 => "normal-water-blocking-v2",
					3 => "normal-water-shoreline-v3",
					4 => "normal-water-shoreline-v3+clear-land-details-v1",
					5 => "normal-land-cover-v1",
					6 => "normal-land-cover-v1+battlefield-layout-v1",
					7 => "normal-land-cover-v1+parameterized-battlefield-v1",
					8 => "normal-land-cover-v1+coherent-water-v1",
					9 => "natural-v9-terrain-prototype-step2+playable-safety-projection-v1",
					_ => settings.OriginalSurfaceRelations ?
						"natural-v10.1-authoritative-surface-placement-v4" :
						"natural-v10.1-ranked-curved-terrain-v2+playable-safety-projection-v1"
				},
				["blocking_topology"] = generation.Profile.GeneratorVersion == 1 ? null : new JObject
				{
					["enabled"] = true,
					["water_amount"] = RmgPlayerSettingsContract.ParameterLevelName(settings.WaterAmount),
					["layout_family"] = RmgPlayerSettingsContract.LayoutFamilyName(settings.LayoutFamily),
					["water_body_count"] = generation.Validation.Metrics["water_body_count"],
					["largest_body_share_percent"] = generation.Validation.Metrics["water_largest_body_share_percent"],
					["small_body_share_percent"] = generation.Validation.Metrics["water_small_body_share_percent"],
					["obstacle_density_target_percent"] = generation.Profile.ObstacleDensityTarget(settings.Archetype, settings.WaterAmount),
					["obstacle_density_minimum_percent"] = generation.Profile.ObstacleDensityRange(settings.Archetype, settings.WaterAmount).Minimum,
					["obstacle_density_maximum_percent"] = generation.Profile.ObstacleDensityRange(settings.Archetype, settings.WaterAmount).Maximum,
					["obstacle_density_percent"] = generation.Validation.Metrics["obstacle_density_percent"],
					["interior_margin_logical"] = 8,
					["interior_density_percent"] = generation.Validation.Metrics["water_interior_density_percent"],
					["interior_share_percent"] = generation.Validation.Metrics["water_interior_share_percent"],
					["interior_covered_sectors"] = generation.Validation.Metrics["water_interior_covered_sector_count"],
					["interior_minimum_density_percent"] = generation.Validation.Metrics["water_interior_minimum_density_percent"],
					["interior_minimum_sectors"] = generation.Validation.Metrics["water_interior_minimum_sector_count"],
					["chokepoint_frequency"] = generation.Map.Chokepoints.Count > 0 ? "one-symmetry-orbit" : "none",
					["chokepoint_segments_requested"] = generation.Profile.UsesNaturalTerrainMorphology ? 0 :
						settings.Archetype == RmgArchetype.CentralContest ? 2 : 0,
					["chokepoint_segments_achieved"] = generation.Map.Chokepoints.Count,
					["route_openness"] = generation.Profile.UsesNaturalTerrainMorphology ? "natural-safe-corridors" :
						settings.Archetype == RmgArchetype.Open ? "major" : "normal-with-route-constriction",
					["shoreline_mode"] = generation.Profile.UsesShorelineMaterialization ? "normal-transition-catalogue-v2" : "homogeneous-hard-seam-v1",
					["visual_shoreline_complete"] = generation.Profile.UsesShorelineMaterialization,
					["unsupported_shoreline_neighborhoods"] = generation.Profile.UsesShorelineMaterialization ? generation.Map.ShorelineUnsupportedNeighborhoodCount : null,
					["shoreline_role_counts"] = generation.Profile.UsesShorelineMaterialization ? new JObject(generation.Map.ShorelineRoles
						.Where(role => role != RmgShorelineRole.None).GroupBy(role => role).OrderBy(group => group.Key)
						.Select(group => new JProperty(group.Key.ToString(), group.Count()))) : null,
					["water_template_usage"] = generation.Profile.UsesShorelineMaterialization ? new JObject(generation.Map.TemplateIds
						.Where((template, index) => generation.Map.Obstacles[index]).GroupBy(template => template).OrderBy(group => group.Key)
						.Select(group => new JProperty(group.Key.ToString(), group.Count()))) : null,
					["shoreline_decoration_percent"] = generation.Profile.UsesShorelineMaterialization ? generation.Profile.ShorelineDecorationPercent : null,
					["open_water_detail_percent"] = generation.Profile.UsesShorelineMaterialization ? generation.Profile.OpenWaterDetailPercent : null,
					["shoreline_decorated_cells"] = generation.Profile.UsesShorelineMaterialization ? generation.Validation.Metrics["shoreline_decorated_cell_count"] : null,
					["open_water_detail_cells"] = generation.Profile.UsesShorelineMaterialization ? generation.Validation.Metrics["open_water_detail_cell_count"] : null,
					["native_water_cells"] = generation.Profile.UsesShorelineMaterialization ? generation.Map.NativeTerrainIntents.Count(intent => intent == RmgNativeTerrainIntent.Water) : null,
					["native_clear_cells"] = generation.Profile.UsesShorelineMaterialization ? generation.Map.NativeTerrainIntents.Count(intent => intent == RmgNativeTerrainIntent.Clear) : null,
					["native_rock_cells"] = generation.Profile.UsesLandCover ? generation.Map.NativeTerrainIntents.Count(intent => intent == RmgNativeTerrainIntent.Rock) : null,
					["native_vegetation_cells"] = generation.Profile.UsesLandCover ? generation.Map.NativeTerrainIntents.Count(intent => intent == RmgNativeTerrainIntent.Vegetation) : null,
					["repair_log"] = new JArray(generation.Map.Repairs.Select(repair => new JObject
					{
						["index"] = repair.Index,
						["type"] = repair.Type,
						["reason"] = repair.Reason,
						["target_id"] = repair.TargetId,
						["changed_cells"] = new JArray(repair.ChangedCells.Select(point => new JObject { ["x"] = point.X, ["y"] = point.Y }))
					})),
					["debug_layers"] = RmgGenerator.BlockingDebugLayers(generation.Map)
				},
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
				["package_validation"] = packageValidation.ToJson(),
				["performance"] = packageValidation.Performance.ToJson()
			};

			if (generation.Profile.UsesClearLandDetails)
				report["land_details"] = new JObject
				{
					["enabled"] = true,
					["native_terrain"] = "Clear",
					["target_percent"] = generation.Profile.ClearLandDetailPercent,
					["eligible_clear_stamps"] = generation.Validation.Metrics["clear_land_detail_eligible_stamp_count"],
					["excluded_protected_clear_stamps"] = generation.Validation.Metrics["clear_land_detail_excluded_protected_stamp_count"],
					["selected_detail_stamps"] = generation.Validation.Metrics["clear_land_detail_selected_stamp_count"],
					["achieved_percent"] = generation.Validation.Metrics["clear_land_detail_achieved_percent"],
					["symmetry_side_a_count"] = generation.Validation.Metrics["clear_land_detail_symmetry_side_a_count"],
					["symmetry_side_b_count"] = generation.Validation.Metrics["clear_land_detail_symmetry_side_b_count"],
					["selection_sha256"] = RmgClearLandDetailMaterializer.SelectionHash(generation.Map, generation.Profile),
					["template_usage"] = new JObject(generation.Map.TemplateIds
						.Where(template => generation.Profile.ClearLandDetailTemplateIds.Contains(template))
						.GroupBy(template => template).OrderBy(group => group.Key)
						.Select(group => new JProperty(group.Key.ToString(), group.Count())))
				};

			if (generation.Profile.UsesLandCover)
				report["land_cover"] = new JObject
				{
					["enabled"] = true,
					["selection_sha256"] = RmgLandCoverMaterializer.SelectionHash(generation.Map),
					["land_native_cells"] = generation.Map.LandCoverLandNativeCount,
					["allowed_lattice_points"] = generation.Map.LandCoverAllowedLatticeCount,
					["tactical_terrain"] = RmgPlayerSettingsContract.ParameterLevelName(settings.TacticalTerrain),
					["rock_requested_percent"] = generation.Profile.RockLandPercentFor(settings.TacticalTerrain),
					["vegetation_requested_percent"] = generation.Profile.VegetationLandPercentFor(settings.TacticalTerrain),
					["tolerance_percent"] = generation.Profile.LandCoverTolerancePercent,
					["rock_requested_native_cells"] = generation.Validation.Metrics["rock_land_requested_native_cells"],
					["vegetation_requested_native_cells"] = generation.Validation.Metrics["vegetation_land_requested_native_cells"],
					["rock_effective_target_native_cells"] = generation.Map.LandCoverRockTargetNativeCount,
					["vegetation_effective_target_native_cells"] = generation.Map.LandCoverVegetationTargetNativeCount,
					["envelope_capacity_native_cells"] = generation.Map.LandCoverEnvelopeCapacityNativeCount,
					["vegetation_core_capacity_native_cells"] = generation.Map.LandCoverVegetationCapacityNativeCount,
					["rock_native_cells"] = generation.Map.LandCoverRockNativeCount,
					["rock_shortfall_native_cells"] = generation.Validation.Metrics["rock_land_shortfall_native_cells"],
					["vegetation_shortfall_native_cells"] = generation.Validation.Metrics["vegetation_land_shortfall_native_cells"],
					["vegetation_native_cells"] = generation.Map.LandCoverVegetationNativeCount,
					["rock_achieved_percent"] = generation.Validation.Metrics["rock_land_achieved_percent"],
					["vegetation_achieved_percent"] = generation.Validation.Metrics["vegetation_land_achieved_percent"],
					["clear_rock_transition_stamps"] = generation.Map.LandCoverClearRockTransitionStampCount,
					["rock_vegetation_transition_stamps"] = generation.Map.LandCoverRockVegetationTransitionStampCount,
					["rock_interior_stamps"] = generation.Map.LandCoverRockInteriorStampCount,
					["vegetation_interior_stamps"] = generation.Map.LandCoverVegetationInteriorStampCount,
					["rock_detail_stamps"] = generation.Map.LandCoverRockDetailStampCount,
					["vegetation_detail_stamps"] = generation.Map.LandCoverVegetationDetailStampCount,
					["template_usage"] = new JObject(generation.Map.TemplateIds
						.Where(template => NormalLandTransitionCatalogue.TryGet(template, out var entry) && entry.Permitted)
						.GroupBy(template => template).OrderBy(group => group.Key)
						.Select(group => new JProperty(group.Key.ToString(), group.Count())))
				};

			if (generation.Profile.UsesBattlefieldLayout)
				report["battlefield_layout"] = new JObject
				{
					["enabled"] = true,
					["policy"] = "role-aware-movement-terrain-v1",
					["protected_clear_policy"] = "start-and-structure-reservations",
					["role_counts"] = new JObject(Enum.GetValues<RmgBattlefieldRole>().Select(role =>
						new JProperty(role.ToString(), generation.Map.BattlefieldRoles.Count(value => value == role)))),
					["role_slow_native_cells"] = new JObject(Enum.GetValues<RmgBattlefieldRole>().Select(role =>
						new JProperty(role.ToString(), generation.Validation.Metrics[$"battlefield_role_{role.ToString().ToLowerInvariant()}_slow_native_cells"]))),
					["tactical_slow_native_cells"] = generation.Validation.Metrics["battlefield_tactical_slow_native_cells"],
					["central_half_slow_native_cells"] = generation.Validation.Metrics["battlefield_central_half_slow_native_cells"],
					["tactical_anchor_orbits_requested"] = generation.Profile.TacticalLandAnchorOrbitCountFor(settings.TacticalTerrain),
					["tactical_anchor_orbits"] = generation.Map.BattlefieldTacticalAnchorOrbitCount,
					["tactical_anchor_lattice_points"] = new JArray(generation.Map.BattlefieldTacticalAnchors
						.OrderBy(point => point.Y).ThenBy(point => point.X)
						.Select(point => new JObject { ["x"] = point.X, ["y"] = point.Y })),
					["land_decorations"] = new JObject
					{
						["policy"] = "terrain-specific-native-symmetry-v2",
						["soil_actors"] = new JArray(generation.Profile.SoilDecorationActors),
						["rock_actors"] = new JArray(generation.Profile.RockDecorationActors),
						["vegetation_actors"] = new JArray(generation.Profile.VegetationDecorationActors),
						["blocking_actors"] = new JArray(generation.Profile.BlockingDecorationActors),
						["requested_count"] = generation.Map.LandDecorationRequestedCount,
						["target_count"] = generation.Map.LandDecorationTargetCount,
						["selected_count"] = generation.Map.LandDecorationSelectedCount,
						["covered_sectors"] = generation.Map.LandDecorationSectorCount,
						["sector_grid"] = "4x4",
						["minimum_covered_sectors"] = generation.Profile.MinimumLandDecorationSectors,
						["clear_target_count"] = generation.Map.LandDecorationClearTargetCount,
						["rock_target_count"] = generation.Map.LandDecorationRockTargetCount,
						["vegetation_target_count"] = generation.Map.LandDecorationVegetationTargetCount,
						["passable_selected_count"] = generation.Validation.Metrics["land_decoration_passable_count"],
						["blocking_selected_count"] = generation.Validation.Metrics["land_decoration_blocking_count"],
						["selection_sha256"] = RmgTerrainDecorationGenerator.SelectionHash(generation.Map),
						["actor_usage"] = new JObject(generation.Map.Actors.Where(RmgTerrainDecorationGenerator.IsDecoration)
							.GroupBy(actor => actor.Type).OrderBy(group => group.Key)
							.Select(group => new JProperty(group.Key, group.Count())))
					}
				};

			if (generation.Profile.UsesNaturalTerrainMorphology)
				report["natural_landscape"] = new JObject
				{
					["enabled"] = true,
					["status"] = "experimental-playable",
					["prototype"] = generation.Profile.UsesNaturalTerrainMorphologyV10 ?
						(generation.Settings.OriginalSurfaceRelations ? "natural-v10.1-authoritative-surface-placement-v4" : "natural-v10.1-ranked-curved-terrain-v2") :
						NaturalPrototype.NaturalTerrainPrototypeSettings.PrototypeId,
					["morphology"] = generation.Profile.UsesNaturalTerrainMorphologyV10 ?
						"NATURAL_MULTI_FAMILY_V2" :
						NaturalPrototype.NaturalTerrainPrototypeSettings.MorphologyId,
					["variant"] = generation.Map.NaturalTerrainVariantId,
					["original_surface_relations"] = settings.OriginalSurfaceRelations,
					["prototype_forbidden_surface_adjacencies"] = generation.Map.NaturalForbiddenSurfaceAdjacencyCount,
					["terrain_symmetry"] = "independent-from-player-start-symmetry",
					["gameplay_safety"] = "terrain-first least-damage routes, adaptive colonies, and native movement validation",
					["prototype_water_cells"] = generation.Map.NaturalPrototypeWaterCount,
					["prototype_interior_water_cells"] = generation.Map.NaturalPrototypeInteriorWaterCount,
					["projected_water_cells"] = generation.Map.NaturalProjectedWaterCount,
					["projected_interior_water_cells"] = generation.Map.NaturalProjectedInteriorWaterCount,
					["pre_route_water_cells"] = generation.Map.NaturalPreRouteWaterCount,
					["pre_route_interior_water_cells"] = generation.Map.NaturalPreRouteInteriorWaterCount,
					["colony_cleared_water_cells"] = generation.Map.NaturalColonyClearedWaterCount,
					["route_cleared_water_cells"] = generation.Map.NaturalRouteClearedWaterCount
				};

			return report;
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

		public static string TopologyName(RmgTopologyPreset topology) => topology switch
		{
			RmgTopologyPreset.Off => "off",
			RmgTopologyPreset.Mixed => "mixed",
			RmgTopologyPreset.Shoreline => "shoreline",
			RmgTopologyPreset.LandDetails => "land-details",
			RmgTopologyPreset.LandCover => "land-cover",
			RmgTopologyPreset.BattlefieldLayout => "battlefield-layout",
			RmgTopologyPreset.ParameterizedBattlefield => "parameterized-battlefield",
			RmgTopologyPreset.CoherentWater => "coherent-water",
			RmgTopologyPreset.NaturalTerrain => "natural-terrain",
			RmgTopologyPreset.NaturalTerrainV10 => "natural-terrain-v10",
			_ => throw new ArgumentOutOfRangeException(nameof(topology))
		};
	}
}
