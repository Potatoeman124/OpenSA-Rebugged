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
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public static partial class RmgGenerator
	{
		sealed record ColonyRequest(string Role, RmgPoint[] Targets, int TypeGroup);
		sealed record ColonyOrbitCandidate(RmgPoint[] Orbit, long Score);

		public static IReadOnlyList<string> RunSelfTests(RmgProfile profile)
		{
			var failures = new List<string>();
			if (profile.UsesNaturalTerrainMorphologyV10)
				failures.AddRange(RunNaturalV10VisualSelfTests(profile));
			var points = new[] { new RmgPoint(0, 0), new RmgPoint(7, 19), new RmgPoint(31, 32), new RmgPoint(63, 63) };
			foreach (var symmetry in Enum.GetValues<RmgSymmetry>())
				foreach (var point in points)
				{
					var transformed = Transform(point, symmetry, profile.LogicalWidth, profile.LogicalHeight);
					if (transformed.X < 0 || transformed.X >= profile.LogicalWidth || transformed.Y < 0 || transformed.Y >= profile.LogicalHeight)
						failures.Add($"Transform {symmetry} moved {point} out of bounds.");
					if (Transform(transformed, symmetry, profile.LogicalWidth, profile.LogicalHeight) != point)
						failures.Add($"Transform {symmetry} is not an involution for {point}.");
				}

			var settings = new RmgGenerationSettings
			{
				Seed = 45006,
				PlayerCount = 2,
				NeutralColonyCount = 10,
				Symmetry = RmgSymmetry.Rotate180,
				Archetype = RmgArchetype.CentralContest,
				GeneratorVersion = profile.GeneratorVersion,
				LayoutFamily = profile.UsesNaturalTerrainMorphology ? RmgLayoutFamily.NaturalLandscape :
					profile.UsesCoherentWaterMorphology ?
						RmgLayoutFamily.StructuredCompetitive : RmgLayoutFamily.ArtificialBattlefield,
				TopologyPreset = profile.GeneratorVersion switch
				{
					2 => RmgTopologyPreset.Mixed,
					3 => RmgTopologyPreset.Shoreline,
					4 => RmgTopologyPreset.LandDetails,
					5 => RmgTopologyPreset.LandCover,
					6 => RmgTopologyPreset.BattlefieldLayout,
					7 => RmgTopologyPreset.ParameterizedBattlefield,
					8 => RmgTopologyPreset.CoherentWater,
					9 => RmgTopologyPreset.NaturalTerrain,
					10 => RmgTopologyPreset.NaturalTerrainV10,
					_ => RmgTopologyPreset.Off
				}
			};
			var first = Generate(profile, settings);
			var second = Generate(profile, settings);
			if (first.LogicalHash != second.LogicalHash || first.ActorHash != second.ActorHash || first.GraphHash != second.GraphHash)
				failures.Add("Same-seed deterministic hash self-test failed.");

			if (profile.UsesClearLandDetails)
			{
				var details = profile.ClearLandDetailTemplateIds.ToHashSet();
				var selected = first.Map.TemplateIds.Select((template, index) => (template, index))
					.Where(entry => details.Contains(entry.template)).ToArray();
				if (selected.Length != first.Map.ClearLandDetailTargetCount ||
					selected.Length != first.Map.ClearLandDetailSelectedCount)
					failures.Add("Clear-land-detail selection did not meet its exact rounded target.");
				if (selected.Any(entry => RmgClearLandDetailMaterializer.IsProtected(first.Map, entry.index, profile)))
					failures.Add("Clear-land-detail selection overlapped a protected semantic layer.");
				if (selected.Any(entry => Enumerable.Range(0, 4).Any(frame =>
					first.Map.NativeTerrainIntents[4 * entry.index + frame] != RmgNativeTerrainIntent.Clear)))
					failures.Add("Clear-land-detail selection changed native terrain intent.");
				if (!profile.UsesNaturalTerrainMorphologyV10 &&
					Math.Abs(first.Map.ClearLandDetailSymmetrySideACount - first.Map.ClearLandDetailSymmetrySideBCount) > 1)
					failures.Add("Clear-land-detail selection is not count-balanced across symmetry sides.");
			}

			if (profile.UsesLandCover)
			{
				failures.AddRange(NormalLandTransitionCatalogue.RunSelfTests());
				failures.AddRange(RmgLandCoverMaterializer.RunSelfTests());
			}

			if (profile.UsesBattlefieldLayout)
			{
				if (profile.UsesParameterizedBattlefield)
					failures.AddRange(RmgPlayerSettingsContract.RunSelfTests());

				if (profile.GeneratorVersion == 6)
				{
					try
					{
					var reportedSeedSettings = new RmgGenerationSettings
					{
						Seed = 5058340853825067450,
						PlayerCount = 4,
						NeutralColonyCount = 24,
						Symmetry = RmgSymmetry.Rotate180,
						Archetype = RmgArchetype.Open,
						GeneratorVersion = 6,
						TopologyPreset = RmgTopologyPreset.BattlefieldLayout
					};
					var reportedFirst = Generate(profile, reportedSeedSettings);
					var reportedRepeat = Generate(profile, reportedSeedSettings);
					if (!reportedFirst.Validation.Accepted)
						failures.Add("Reported UI seed regression did not pass hard topology validation.");
					if (reportedFirst.LogicalHash != reportedRepeat.LogicalHash ||
						reportedFirst.ActorHash != reportedRepeat.ActorHash ||
						reportedFirst.GraphHash != reportedRepeat.GraphHash)
						failures.Add("Reported UI seed regression is not deterministic.");
					}
					catch (Exception e)
					{
						failures.Add($"Reported UI seed regression was rejected: {e.Message}");
					}

					var adaptiveRegressions = new[]
					{
						(
							Name: "density-6385527573119186284",
							Settings: new RmgGenerationSettings
							{
								Seed = 6385527573119186284,
								PlayerCount = 4,
								NeutralColonyCount = 16,
								Symmetry = RmgSymmetry.MirrorVertical,
								Archetype = RmgArchetype.CentralContest,
								GeneratorVersion = 6,
								TopologyPreset = RmgTopologyPreset.BattlefieldLayout
							},
							Warning: "OBSTACLE_DENSITY_TARGET_MISSED",
							ExpectedColonies: 16),
						(
							Name: "density-16385527573119186284",
							Settings: new RmgGenerationSettings
							{
								Seed = 16385527573119186284,
								PlayerCount = 4,
								NeutralColonyCount = 24,
								Symmetry = RmgSymmetry.Rotate180,
								Archetype = RmgArchetype.CentralContest,
								GeneratorVersion = 6,
								TopologyPreset = RmgTopologyPreset.BattlefieldLayout
							},
							Warning: "OBSTACLE_DENSITY_TARGET_MISSED",
							ExpectedColonies: 24),
						(
							Name: "colonies-15782917902311806871",
							Settings: new RmgGenerationSettings
							{
								Seed = 15782917902311806871,
								PlayerCount = 4,
								NeutralColonyCount = 24,
								Symmetry = RmgSymmetry.MirrorVertical,
								Archetype = RmgArchetype.CentralContest,
								GeneratorVersion = 6,
								TopologyPreset = RmgTopologyPreset.BattlefieldLayout
							},
							Warning: "COLONY_TARGET_REDUCED",
							ExpectedColonies: 16)
					};
					foreach (var regression in adaptiveRegressions)
						ValidateAdaptiveRegression(regression.Name, regression.Settings, regression.Warning, regression.ExpectedColonies);
				}

				if (profile.GeneratorVersion == 7)
					ValidateParameterizedBattlefield();
				else if (profile.UsesCoherentWaterMorphology)
					ValidateStructuredCompetitiveV8();

				if (first.Map.BattlefieldRoles.Any(role => role == RmgBattlefieldRole.None))
					failures.Add("Battlefield-role planner left unclassified logical cells.");
				for (var i = 0; i < first.Map.BattlefieldRoles.Length; i++)
				{
					var point = new RmgPoint(i % first.Map.Width, i / first.Map.Width);
					var partner = Transform(point, settings.Symmetry, first.Map.Width, first.Map.Height);
					if (first.Map.BattlefieldRoles[i] != first.Map.BattlefieldRoles[first.Map.Index(partner)])
						failures.Add($"Battlefield role at {point} differs from its symmetry partner.");
				}

				var slowTactical = Enumerable.Range(0, first.Map.BattlefieldRoles.Length).Count(index =>
					RmgBattlefieldRolePlanner.IsTactical(first.Map.BattlefieldRoles[index]) && Enumerable.Range(0, 4)
						.Any(frame => RmgLandCoverMaterializer.IsSlow(first.Map.NativeTerrainIntents[4 * index + frame])));
				if (slowTactical == 0)
					failures.Add("Role-aware land cover did not place slow terrain in a tactical role.");
				var decorations = first.Map.Actors.Where(RmgTerrainDecorationGenerator.IsDecoration).ToArray();
				if (decorations.Length != first.Map.LandDecorationTargetCount || decorations.Any(actor =>
					!RmgTerrainDecorationGenerator.ActorsForTerrain(profile,
						RmgTerrainDecorationGenerator.TerrainAt(first.Map, actor)).Contains(actor.Type)))
					failures.Add("Terrain-specific decoration selection does not match the Version 6 profile target.");
				if (first.Map.LandDecorationSectorCount < profile.MinimumLandDecorationSectors)
					failures.Add("Land decorations do not meet the minimum broad-sector coverage.");
				if (decorations.Any(RmgTerrainDecorationGenerator.IsBlocking))
					failures.Add("Version 6 materialized a blocking RMG land decoration.");
				if (first.Map.TemplateIds.Contains((ushort)93))
					failures.Add("Version 6 materialized defective square-edged Vegetation detail template 93.");
			}

			void ValidateParameterizedBattlefield()
			{
				var levels = new[] { RmgParameterLevel.Low, RmgParameterLevel.Standard, RmgParameterLevel.High };
				foreach (var waterAmount in levels)
					foreach (var tacticalTerrain in levels)
					{
						var parameterSettings = new RmgGenerationSettings
						{
							Seed = 8100001,
							PlayerCount = 2,
							NeutralColonyCount = 10,
							Symmetry = RmgSymmetry.Rotate180,
							Archetype = RmgArchetype.CentralContest,
							GeneratorVersion = 7,
							TopologyPreset = RmgTopologyPreset.ParameterizedBattlefield,
							WaterAmount = waterAmount,
							TacticalTerrain = tacticalTerrain
						};
						try
						{
							var parameterFirst = Generate(profile, parameterSettings);
							var parameterRepeat = Generate(profile, parameterSettings);
							if (!parameterFirst.Validation.Accepted)
								failures.Add($"Version 7 Water={waterAmount}, Tactical={tacticalTerrain} did not pass hard validation.");
							if (parameterFirst.LogicalHash != parameterRepeat.LogicalHash ||
								parameterFirst.ActorHash != parameterRepeat.ActorHash ||
								parameterFirst.GraphHash != parameterRepeat.GraphHash)
								failures.Add($"Version 7 Water={waterAmount}, Tactical={tacticalTerrain} is not deterministic.");
						}
						catch (Exception e)
						{
							failures.Add($"Version 7 Water={waterAmount}, Tactical={tacticalTerrain} was rejected: {e.Message}");
						}
					}

				var reportedHighWaterRegressions = new[]
				{
					(Seed: 5722426127237601134UL, Symmetry: RmgSymmetry.Rotate180),
					(Seed: 16542743543062672902UL, Symmetry: RmgSymmetry.MirrorVertical)
				};
				foreach (var (seed, symmetry) in reportedHighWaterRegressions)
					try
					{
						var result = Generate(profile, new RmgGenerationSettings
						{
							Seed = seed,
							PlayerCount = 4,
							NeutralColonyCount = 24,
							Symmetry = symmetry,
							Archetype = RmgArchetype.CentralContest,
							GeneratorVersion = 7,
							TopologyPreset = RmgTopologyPreset.ParameterizedBattlefield,
							WaterAmount = RmgParameterLevel.High,
							TacticalTerrain = RmgParameterLevel.High
						});
						var totalWater = result.Validation.Metrics["obstacle_density_percent"];
						var interiorWater = result.Validation.Metrics["water_interior_density_percent"];
						var coveredSectors = result.Validation.Metrics["water_interior_covered_sector_count"];
						if (!result.Validation.Accepted || totalWater < 18D || interiorWater < 10D || coveredSectors < 12D)
							failures.Add($"Reported High-Water seed {seed} did not meet the total/interior Water contract.");
					}
					catch (Exception e)
					{
						failures.Add($"Reported High-Water seed {seed} was rejected: {e.Message}");
					}

				if (!(profile.ObstacleDensityTarget(RmgArchetype.Open, RmgParameterLevel.Low) <
					profile.ObstacleDensityTarget(RmgArchetype.Open, RmgParameterLevel.Standard) &&
					profile.ObstacleDensityTarget(RmgArchetype.Open, RmgParameterLevel.Standard) <
					profile.ObstacleDensityTarget(RmgArchetype.Open, RmgParameterLevel.High)))
					failures.Add("Version 7 Water Amount targets are not strictly ordered Low, Standard, High.");
				if (!(profile.RockLandPercentFor(RmgParameterLevel.Low) <
					profile.RockLandPercentFor(RmgParameterLevel.Standard) &&
					profile.RockLandPercentFor(RmgParameterLevel.Standard) <
					profile.RockLandPercentFor(RmgParameterLevel.High)))
					failures.Add("Version 7 Rock targets are not strictly ordered Low, Standard, High.");
				if (!(profile.VegetationLandPercentFor(RmgParameterLevel.Low) <
					profile.VegetationLandPercentFor(RmgParameterLevel.Standard) &&
					profile.VegetationLandPercentFor(RmgParameterLevel.Standard) <
					profile.VegetationLandPercentFor(RmgParameterLevel.High)))
					failures.Add("Version 7 Vegetation targets are not strictly ordered Low, Standard, High.");
			}

			void ValidateStructuredCompetitiveV8()
			{
				var results = new List<RmgGenerationResult>();
				var cases = new[]
				{
					(Seed: 8300001UL, Players: 2, Symmetry: RmgSymmetry.MirrorHorizontal, Archetype: RmgArchetype.Open, Water: RmgParameterLevel.Standard),
					(Seed: 8300002UL, Players: 4, Symmetry: RmgSymmetry.MirrorVertical, Archetype: RmgArchetype.CentralContest, Water: RmgParameterLevel.Standard),
					(Seed: 8300003UL, Players: 2, Symmetry: RmgSymmetry.Rotate180, Archetype: RmgArchetype.CentralContest, Water: RmgParameterLevel.High),
					(Seed: 8300004UL, Players: 4, Symmetry: RmgSymmetry.MirrorHorizontal, Archetype: RmgArchetype.Open, Water: RmgParameterLevel.High),
					(Seed: 8300005UL, Players: 2, Symmetry: RmgSymmetry.MirrorVertical, Archetype: RmgArchetype.Open, Water: RmgParameterLevel.Low),
					(Seed: 8300006UL, Players: 4, Symmetry: RmgSymmetry.Rotate180, Archetype: RmgArchetype.CentralContest, Water: RmgParameterLevel.High)
				};
				foreach (var (seed, players, symmetry, archetype, water) in cases)
					try
					{
						var structuredSettings = new RmgGenerationSettings
						{
							Seed = seed,
							PlayerCount = players,
							NeutralColonyCount = players == 2 ? 10 : 16,
							Symmetry = symmetry,
							Archetype = archetype,
							GeneratorVersion = 8,
							TopologyPreset = RmgTopologyPreset.CoherentWater,
							LayoutFamily = RmgLayoutFamily.StructuredCompetitive,
							WaterAmount = water,
							TacticalTerrain = RmgParameterLevel.Standard
						};
						var structured = Generate(profile, structuredSettings);
						var repeat = Generate(profile, structuredSettings);
						results.Add(structured);
						if (!structured.Validation.Accepted)
							failures.Add($"Version 8 Structured Competitive seed {seed} did not pass hard validation.");
						if (structured.LogicalHash != repeat.LogicalHash || structured.ActorHash != repeat.ActorHash ||
							structured.GraphHash != repeat.GraphHash)
							failures.Add($"Version 8 Structured Competitive seed {seed} is not deterministic.");
						if (structured.Validation.Metrics["water_body_count"] > 12D ||
							structured.Validation.Metrics["water_largest_body_share_percent"] < 20D ||
							structured.Validation.Metrics["water_small_body_share_percent"] > 15D)
							failures.Add($"Version 8 Structured Competitive seed {seed} missed its morphology envelope.");
					}
					catch (Exception e)
					{
						failures.Add($"Version 8 Structured Competitive seed {seed} was rejected: {e.Message}");
					}

				if (results.Count == cases.Length)
				{
					var orderedLargest = results.Select(result =>
						result.Validation.Metrics["water_largest_body_share_percent"]).OrderBy(value => value).ToArray();
					var medianLargest = (orderedLargest[orderedLargest.Length / 2 - 1] +
						orderedLargest[orderedLargest.Length / 2]) / 2D;
					if (medianLargest < 35D)
						failures.Add($"Version 8 Structured Competitive comparison corpus has only {medianLargest:F2}% median largest-body share.");
				}
			}

			void ValidateAdaptiveRegression(string name, RmgGenerationSettings regressionSettings,
				string expectedWarning, int expectedColonies)
			{
				try
				{
					var regressionFirst = Generate(profile, regressionSettings);
					var regressionRepeat = Generate(profile, regressionSettings);
					if (!regressionFirst.Validation.Accepted)
						failures.Add($"{name} did not pass hard topology validation.");
					if (!regressionFirst.Validation.Warnings.Any(warning => warning.Code == expectedWarning))
						failures.Add($"{name} did not report expected warning {expectedWarning}.");
					var colonies = regressionFirst.Map.Actors.Count(actor => actor.Owner == profile.ColonyOwner);
					if (colonies != expectedColonies)
						failures.Add($"{name} placed {colonies} neutral colonies; expected {expectedColonies}.");
					if (regressionFirst.LogicalHash != regressionRepeat.LogicalHash ||
						regressionFirst.ActorHash != regressionRepeat.ActorHash ||
						regressionFirst.GraphHash != regressionRepeat.GraphHash)
						failures.Add($"{name} is not deterministic.");
				}
				catch (Exception e)
				{
					failures.Add($"{name} was rejected: {e.Message}");
				}
			}

			first.Map.TemplateIds[0] = ushort.MaxValue;
			if (Validate(first.Map, profile, settings).Accepted)
				failures.Add("Hard validator accepted a deliberately invalid terrain template.");

			if (profile.GeneratorVersion >= 2)
			{
				if (profile.UsesShorelineMaterialization)
				{
					failures.AddRange(NormalWaterTransitionCatalogue.RunSelfTests());
					failures.AddRange(RmgShorelineMaterializer.RunSelfTests());
				}

				var combatRules = profile.ColonyCombatRules;
				if (combatRules.MaximumAttackRangeNative != 18 || combatRules.SafetyBufferNative != 1)
					failures.Add("Combat-space rules did not derive the expected 18-cell maximum turret range and one-cell buffer.");

				var origin = new RmgPoint(10, 10);
				if (combatRules.CombatSpaceIsSafe("scorpions_colony", origin, "ants_colony", new RmgPoint(19, 10)))
					failures.Add("Combat-space rules accepted colonies inside the Scorpions turret range plus safety buffer.");
				if (!combatRules.CombatSpaceIsSafe("scorpions_colony", origin, "ants_colony", new RmgPoint(20, 10)))
					failures.Add("Combat-space rules rejected colony centers outside both turret envelopes.");

				var start = new RmgPoint(20, 10);
				if (combatRules.CombatSpaceIsSafeFromAnyStartingActor("scorpions_colony", new RmgPoint(30, 10), start))
					failures.Add("Combat-space rules accepted a neutral turret covering a possible player's production path.");
				if (!combatRules.CombatSpaceIsSafeFromAnyStartingActor("scorpions_colony", new RmgPoint(32, 10), start))
					failures.Add("Combat-space rules rejected a neutral colony beyond every possible player's production envelope.");

				var repairMap = Generate(profile, settings).Map;
				var reserved = Array.FindIndex(repairMap.RouteMasks, route => route != 0);
				repairMap.Obstacles[reserved] = true;
				repairMap.ObstacleRegionIds[reserved] = 999;
				ApplyBlockingRepairs(repairMap, profile);
				if (repairMap.Obstacles[reserved] || repairMap.Repairs.Count == 0)
					failures.Add("Bounded repair self-test did not clear a route obstruction and record the operation.");

				var overBudgetMap = Generate(profile, settings).Map;
				var overBudget = Enumerable.Range(0, overBudgetMap.RouteMasks.Length)
					.Where(i => overBudgetMap.RouteMasks[i] != 0)
					.Take(profile.MaximumRepairCellsLogical + 1)
					.ToArray();
				if (overBudget.Length <= profile.MaximumRepairCellsLogical)
					failures.Add("Repair-budget rejection self-test could not construct an over-budget route obstruction.");
				else
				{
					foreach (var index in overBudget)
					{
						overBudgetMap.Obstacles[index] = true;
						overBudgetMap.ObstacleRegionIds[index] = 999;
					}

					try
					{
						ApplyBlockingRepairs(overBudgetMap, profile);
						failures.Add("Bounded repair self-test accepted an obstruction larger than the frozen changed-cell budget.");
					}
					catch (InvalidOperationException)
					{
						// Expected: an over-budget repair rejects the topology instead of silently changing it.
					}
				}
			}

			return failures;
		}

		public static RmgGenerationResult Generate(RmgProfile profile, RmgGenerationSettings settings)
		{
			ValidateSettings(profile, settings);
			if (profile.UsesRegionsTerrain)
				return GenerateRegions(profile, settings);
			if (profile.UsesNaturalTerrainMorphologyV10)
				return GenerateNaturalLandscapeV10(profile, settings);
			if (profile.UsesNaturalTerrainMorphology)
				return GenerateNaturalLandscape(profile, settings);
			if (profile.GeneratorVersion >= 2)
				return GenerateBlockingTopology(profile, settings);

			var map = new RmgLogicalMap(profile.LogicalWidth, profile.LogicalHeight);

			GenerateStartsAndTopology(map, profile, settings);
			ReserveRoutes(map);
			GenerateObstacles(map, profile);
			PlaceColonies(map, profile, settings);
			ApplyBoundedRepairs(map);
			AssignRegions(map);
			MaterializeTerrainVariants(map, profile, settings);

			var validation = Validate(map, profile, settings);
			return new RmgGenerationResult
			{
				Settings = settings,
				Profile = profile,
				Map = map,
				Validation = validation,
				LogicalHash = HashLogicalMap(map),
				ActorHash = HashActors(map),
				GraphHash = HashGraph(map)
			};
		}

		public static RmgPoint Transform(RmgPoint point, RmgSymmetry symmetry, int width = 64, int height = 64)
		{
			return symmetry switch
			{
				RmgSymmetry.MirrorHorizontal => new RmgPoint(point.X, height - 1 - point.Y),
				RmgSymmetry.MirrorVertical => new RmgPoint(width - 1 - point.X, point.Y),
				RmgSymmetry.Rotate180 => new RmgPoint(width - 1 - point.X, height - 1 - point.Y),
				_ => throw new ArgumentOutOfRangeException(nameof(symmetry))
			};
		}

		static void ValidateSettings(RmgProfile profile, RmgGenerationSettings settings)
		{
			if (settings.MapSize != profile.PlayableWidth || settings.MapSize != profile.PlayableHeight)
				throw new ArgumentException("Requested map size does not match the generation profile.");
			if (!RmgBiome.IsSupported(settings.Tileset) || settings.Tileset != profile.Tileset ||
				(settings.Tileset != "NORMAL" && (settings.GeneratorVersion != 16 || !profile.UsesRegionsTerrain)))
				throw new ArgumentException("RMG tileset and profile must match the supported Regions contract.");
			if (settings.GeneratorVersion != profile.GeneratorVersion)
				throw new ArgumentException($"Generator Version {settings.GeneratorVersion} is not supported by profile {profile.ProfileId}.");
			if (profile.UsesRegionsTerrain && (settings.TopologyPreset != RmgTopologyPreset.NaturalRegions ||
				settings.LayoutFamily != RmgLayoutFamily.NaturalLandscape || (!Enum.IsDefined(settings.TerrainComplexity) ||
				(settings.GeneratorVersion is not (13 or 14 or 15 or 16) && settings.TerrainComplexity > Reassessment.TerrainComplexity.High))))
				throw new ArgumentException("Regions requires Natural Landscape and a valid Terrain Complexity.");
			if (profile.GeneratorVersion == 1 && settings.TopologyPreset != RmgTopologyPreset.Off)
				throw new ArgumentException("Generator Version 1 requires TopologyPreset=off.");
			if (profile.GeneratorVersion == 2 && settings.TopologyPreset != RmgTopologyPreset.Mixed)
				throw new ArgumentException("Generator Version 2 requires TopologyPreset=mixed.");
			if (profile.GeneratorVersion == 3 && settings.TopologyPreset != RmgTopologyPreset.Shoreline)
				throw new ArgumentException("Generator Version 3 requires TopologyPreset=shoreline.");
			if (profile.GeneratorVersion == 4 && settings.TopologyPreset != RmgTopologyPreset.LandDetails)
				throw new ArgumentException("Generator Version 4 requires TopologyPreset=land-details.");
			if (profile.GeneratorVersion == 5 && settings.TopologyPreset != RmgTopologyPreset.LandCover)
				throw new ArgumentException("Generator Version 5 requires TopologyPreset=land-cover.");
			if (profile.GeneratorVersion == 6 && settings.TopologyPreset != RmgTopologyPreset.BattlefieldLayout)
				throw new ArgumentException("Generator Version 6 requires TopologyPreset=battlefield-layout.");
			if (profile.GeneratorVersion == 7 && settings.TopologyPreset != RmgTopologyPreset.ParameterizedBattlefield)
				throw new ArgumentException("Generator Version 7 requires TopologyPreset=parameterized-battlefield.");
			if (profile.GeneratorVersion == 8 && settings.TopologyPreset != RmgTopologyPreset.CoherentWater)
				throw new ArgumentException("Generator Version 8 requires TopologyPreset=coherent-water.");
			if (profile.GeneratorVersion == 9 && settings.TopologyPreset != RmgTopologyPreset.NaturalTerrain)
				throw new ArgumentException("Generator Version 9 requires TopologyPreset=natural-terrain.");
			if (profile.GeneratorVersion == 10 && settings.TopologyPreset != RmgTopologyPreset.NaturalTerrainV10)
				throw new ArgumentException("Generator Version 10 requires TopologyPreset=natural-terrain-v10.");
			if (profile.GeneratorVersion == 8 && settings.LayoutFamily != RmgLayoutFamily.StructuredCompetitive)
				throw new ArgumentException("Generator Version 8 requires LayoutFamily=structured-competitive.");
			if ((profile.GeneratorVersion == 9 || profile.GeneratorVersion == 10) &&
				settings.LayoutFamily != RmgLayoutFamily.NaturalLandscape)
				throw new ArgumentException($"Generator Version {profile.GeneratorVersion} requires LayoutFamily=natural-landscape.");
			if (profile.GeneratorVersion < 8 && settings.LayoutFamily != RmgLayoutFamily.ArtificialBattlefield)
				throw new ArgumentException($"Generator Version {settings.GeneratorVersion} requires LayoutFamily=artificial-battlefield.");
			if (profile.GeneratorVersion < 7 && (settings.WaterAmount != RmgParameterLevel.Standard ||
				settings.TacticalTerrain != RmgParameterLevel.Standard))
				throw new ArgumentException($"Generator Version {settings.GeneratorVersion} supports only Standard Water and tactical terrain.");
			if (!Enum.IsDefined(settings.WaterAmount) || !Enum.IsDefined(settings.TacticalTerrain) ||
				(settings.GeneratorVersion is not (14 or 15 or 16) && (settings.WaterAmount > RmgParameterLevel.High ||
				settings.TacticalTerrain > RmgParameterLevel.High || !settings.PreventColonyOverlapping)))
				throw new ArgumentException("Extended quantities and relaxed spacing require Regions V14.");
			if (settings.NeutralColonyWeights == null)
				throw new ArgumentException("Neutral colony weights must be an object.");
			settings.NeutralColonyWeights.Validate(settings.GeneratorVersion == 16 ? 100 : 1000);
			RmgColonyOwnership.ValidateShares(settings.StartingColonyShares, settings.PlayerCount);
			if (!Enum.IsDefined(settings.StartingColonyMode) || (settings.GeneratorVersion != 16 && settings.StartingColonyMode != RmgColonyOwnershipMode.ClosestToSpawn))
				throw new ArgumentException("Starting colony mode requires Regions V16 and a valid choice.");
			if (settings.GeneratorVersion != 16 && settings.StartingColonyShares.Length != 0)
				throw new ArgumentException("Starting colony shares require Regions V16.");
			if (settings.GeneratorVersion is 15 or 16)
			{
				if (settings.PlayerCount < 1 || settings.PlayerCount > 8)
					throw new ArgumentException("Regions V15 and V16 support 1 through 8 players.");
				if (settings.MapSize == 64 && settings.PlayerCount > 4)
					throw new ArgumentException("64x64 supports 1 through 4 players.");
				var multiplier = settings.MapSize == 512 ? 9 : settings.MapSize == 256 ? 3 : 1;
				var minimum = (4 + 2 * settings.PlayerCount) * multiplier;
				var maximum = (40 + 6 * settings.PlayerCount) * multiplier;
				if (settings.MapSize == 64)
				{
					minimum = (minimum + 3) / 4;
					maximum = (maximum + 3) / 4;
				}
				if (settings.NeutralColonyCount < minimum || settings.NeutralColonyCount > maximum)
					throw new ArgumentException($"Regions colony target must be {minimum}-{maximum}.");
				return;
			}
			if (settings.NeutralColonyWeights != new RmgColonyWeights())
				throw new ArgumentException("Neutral colony weights require Regions V15.");
			if (settings.PlayerCount != 2 && settings.PlayerCount != 4)
				throw new ArgumentException($"Generator Version {settings.GeneratorVersion} supports exactly two or four players.");
			if (settings.GeneratorVersion == 14)
			{
				var multiplier = settings.MapSize == 256 ? 3 : 1;
				var minimum = (settings.PlayerCount == 2 ? 8 : 12) * multiplier;
				var maximum = (settings.PlayerCount == 2 ? 52 : 64) * multiplier;
				if (settings.NeutralColonyCount < minimum || settings.NeutralColonyCount > maximum ||
					settings.NeutralColonyCount % settings.PlayerCount != 0)
					throw new ArgumentException($"Regions V14 colony target must be {minimum}-{maximum}, divisible by player count.");
				return;
			}
			if (settings.PlayerCount == 2 && (settings.NeutralColonyCount < (settings.MapSize == 256 ? 24 : 8) || settings.NeutralColonyCount > (settings.MapSize == 256 ? 60 : 20) || settings.NeutralColonyCount % 2 != 0))
				throw new ArgumentException(settings.MapSize == 256 ? "Large two-player maps require an even neutral-colony target from 24 through 60." : "Two-player maps require an even neutral-colony count from 8 through 20.");
			if (settings.PlayerCount == 4 && (settings.NeutralColonyCount < (settings.MapSize == 256 ? 36 : 12) || settings.NeutralColonyCount > (settings.MapSize == 256 ? 72 : 24) || settings.NeutralColonyCount % 4 != 0))
				throw new ArgumentException(settings.MapSize == 256 ? "Large four-player maps require a neutral-colony target from 36 through 72, divisible by four." : "Four-player maps require a neutral-colony count from 12 through 24, divisible by four.");
		}

		static void GenerateStartsAndTopology(RmgLogicalMap map, RmgProfile profile, RmgGenerationSettings settings)
		{
			var random = DeterministicRandom.ForStream(settings, profile, "topology");
			RmgPoint Jitter(int x, int y) => new(x * (map.Width / 64) + random.NextInt(-2, 3), y * (map.Height / 64) + random.NextInt(-2, 3));

			if (settings.PlayerCount == 2)
			{
				var first = settings.Symmetry switch
				{
					RmgSymmetry.MirrorHorizontal => Jitter(20, 12),
					RmgSymmetry.MirrorVertical => Jitter(12, 20),
					RmgSymmetry.Rotate180 => Jitter(15, 18),
					_ => throw new ArgumentOutOfRangeException(nameof(settings), settings.Symmetry, "Unsupported symmetry value.")
				};
				AddOrbit(map.Starts, first, settings.Symmetry, map.Width, map.Height);
			}
			else
			{
				switch (settings.Symmetry)
				{
					case RmgSymmetry.MirrorHorizontal:
						AddOrbit(map.Starts, Jitter(14, 12), settings.Symmetry, map.Width, map.Height);
						AddOrbit(map.Starts, Jitter(45, 12), settings.Symmetry, map.Width, map.Height);
						break;
					case RmgSymmetry.MirrorVertical:
						AddOrbit(map.Starts, Jitter(12, 14), settings.Symmetry, map.Width, map.Height);
						AddOrbit(map.Starts, Jitter(12, 45), settings.Symmetry, map.Width, map.Height);
						break;
					case RmgSymmetry.Rotate180:
						AddOrbit(map.Starts, Jitter(14, 16), settings.Symmetry, map.Width, map.Height);
						AddOrbit(map.Starts, Jitter(45, 14), settings.Symmetry, map.Width, map.Height);
						break;
				}
			}

			for (var i = 0; i < map.Starts.Count; i++)
			{
				var start = map.Starts[i];
				map.GraphNodes.Add(new RmgGraphNode($"start-{i}", "start", start));
				map.Actors.Add(new RmgActorPlan(profile.SpawnActor, profile.SpawnOwner, "start", start, i / 2));
				ReserveSquare(map.StartReservations, map, start, 3);
			}

			var hub = new RmgPoint(map.Width / 2 - 2, map.Height / 2 - 1);
			var hubs = new List<RmgPoint>();
			AddOrbit(hubs, hub, settings.Symmetry, map.Width, map.Height);
			for (var i = 0; i < hubs.Count; i++)
				map.GraphNodes.Add(new RmgGraphNode($"hub-{i}", "hub", hubs[i]));

			var routeId = 0;
			for (var start = 0; start < map.Starts.Count; start++)
				for (var hubIndex = 0; hubIndex < hubs.Count; hubIndex++)
					map.GraphEdges.Add(new RmgGraphEdge($"edge-{routeId}", $"start-{start}", $"hub-{hubIndex}", routeId++));
		}

		static void AddOrbit(List<RmgPoint> destination, RmgPoint point, RmgSymmetry symmetry, int width, int height)
		{
			destination.Add(point);
			var transformed = Transform(point, symmetry, width, height);
			if (transformed != point)
				destination.Add(transformed);
		}

		static void ReserveRoutes(RmgLogicalMap map)
		{
			var nodes = map.GraphNodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
			foreach (var edge in map.GraphEdges)
			{
				var from = nodes[edge.From].Location;
				var to = nodes[edge.To].Location;
				foreach (var point in RasterizeLine(from, to))
				{
					for (var dy = -1; dy <= 1; dy++)
						for (var dx = -1; dx <= 1; dx++)
						{
							var widened = new RmgPoint(point.X + dx, point.Y + dy);
							if (map.Contains(widened) && map.RouteIds[map.Index(widened)] == -1)
								map.RouteIds[map.Index(widened)] = edge.RouteId;
						}
				}
			}
		}

		static IEnumerable<RmgPoint> RasterizeLine(RmgPoint from, RmgPoint to)
		{
			var x = from.X;
			var y = from.Y;
			var dx = Math.Abs(to.X - from.X);
			var sx = from.X < to.X ? 1 : -1;
			var dy = -Math.Abs(to.Y - from.Y);
			var sy = from.Y < to.Y ? 1 : -1;
			var error = dx + dy;

			while (true)
			{
				yield return new RmgPoint(x, y);
				if (x == to.X && y == to.Y)
					yield break;

				var doubled = 2 * error;
				if (doubled >= dy)
				{
					error += dy;
					x += sx;
				}

				if (doubled <= dx)
				{
					error += dx;
					y += sy;
				}
			}
		}

		static void GenerateObstacles(RmgLogicalMap map, RmgProfile profile)
		{
			if (profile.ObstacleDensity != 0 || profile.VegetationDensity != 0)
				throw new InvalidOperationException("Blocking or rough terrain is not authorized by Generator Version 1.");

			// This explicit no-op is the Version 1 obstacle stage. The arrays remain present so a later
			// contract can add materialization without changing the topology and validation interfaces.
			Array.Clear(map.Obstacles, 0, map.Obstacles.Length);
		}

		static void ApplyBoundedRepairs(RmgLogicalMap map)
		{
			// The Clear-only profile has no generated terrain obstruction to repair. Keep this explicit
			// stage and its counters stable so later profile versions can add bounded repair streams.
			map.RepairCount = 0;
			map.RetryCount = 0;
		}

		static void PlaceColonies(RmgLogicalMap map, RmgProfile profile, RmgGenerationSettings settings,
			bool allowCentralRouteOverlap = true, int routeClearanceRadius = 2, bool allowAnyRouteOverlap = false)
		{
			var allowTargetReduction = profile.GeneratorVersion >= 6;
			var maximumSearchNodes = profile.UsesParameterizedBattlefield ? 1024 : allowTargetReduction ? 256 : 10000;
			const int CandidateLimitPerRequest = 64;
			var initialActorCount = map.Actors.Count;
			var minimumColonyCount = MinimumAdaptiveColonyCount(profile, settings);
			var bestFallback = Array.Empty<RmgActorPlan>();
			var searchNodes = 0;
			var exhaustedBudget = false;
			var requests = BuildColonyRequests(map, settings);
			var actorTypes = new string[requests.Count];
			var typeByGroup = new Dictionary<int, string>();
			var random = DeterministicRandom.ForStream(settings, profile, "colonies");
			for (var i = 0; i < requests.Count; i++)
			{
				var request = requests[i];
				if (!typeByGroup.TryGetValue(request.TypeGroup, out var actorType))
				{
					actorType = profile.NeutralColonyActors[random.NextInt(profile.NeutralColonyActors.Length)];
					typeByGroup.Add(request.TypeGroup, actorType);
				}

				actorTypes[i] = actorType;
			}

			if (!TryPlaceRequest(0))
			{
				map.Actors.RemoveRange(initialActorCount, map.Actors.Count - initialActorCount);
				if (!allowTargetReduction || bestFallback.Length < minimumColonyCount)
					throw new RmgGenerationRejectedException("COLONY_PLACEMENT",
						exhaustedBudget ? $"Unable to place the colony layout within the bounded {maximumSearchNodes}-node search." :
						"No combat-safe colony layout satisfies the requested roles and symmetry.");

				map.Actors.AddRange(bestFallback);
			}

			map.ColonySearchNodes = searchNodes;

			foreach (var actor in map.Actors.Skip(initialActorCount))
				ReserveSquare(map.StructureReservations, map, actor.LogicalLocation, 2);

			bool TryPlaceRequest(int requestIndex)
			{
				CaptureFallback();
				if (requestIndex == requests.Count)
					return true;

				var request = requests[requestIndex];
				var actorType = actorTypes[requestIndex];
				var candidates = SelectColonyOrbits(map, profile, settings, request, actorType,
					allowCentralRouteOverlap, routeClearanceRadius, allowAnyRouteOverlap, CandidateLimitPerRequest);
				foreach (var orbit in candidates)
				{
					if (++searchNodes > maximumSearchNodes)
					{
						exhaustedBudget = true;
						return false;
					}

					var actorCount = map.Actors.Count;
					foreach (var point in orbit)
						map.Actors.Add(new RmgActorPlan(actorType, profile.ColonyOwner, request.Role, point, request.TypeGroup));
					if (TryPlaceRequest(requestIndex + 1))
						return true;

					map.Actors.RemoveRange(actorCount, map.Actors.Count - actorCount);
				}

				return false;
			}

			void CaptureFallback()
			{
				if (!allowTargetReduction)
					return;

				var placed = map.Actors.Count - initialActorCount;
				if (placed < minimumColonyCount || placed >= settings.NeutralColonyCount ||
					placed % settings.PlayerCount != 0 || placed <= bestFallback.Length)
					return;

				bestFallback = map.Actors.Skip(initialActorCount).ToArray();
			}
		}

		static int MinimumAdaptiveColonyCount(RmgProfile profile, RmgGenerationSettings settings) =>
			profile.UsesNaturalTerrainMorphology ? settings.PlayerCount :
				profile.UsesCoherentWaterMorphology ? settings.PlayerCount * 2 :
				profile.UsesParameterizedBattlefield && settings.WaterAmount == RmgParameterLevel.High ?
				settings.PlayerCount * 2 :
				settings.PlayerCount == 2 ? 8 : 12;

		static List<ColonyRequest> BuildColonyRequests(RmgLogicalMap map, RmgGenerationSettings settings)
		{
			var requests = new List<ColonyRequest>();
			var startPairs = map.Starts.Chunk(2).Select(c => c.ToArray()).ToArray();
			var typeGroup = 0;

			for (var layer = 0; layer < 2; layer++)
			{
				foreach (var pair in startPairs)
					requests.Add(new ColonyRequest("near-start", pair, typeGroup));
				typeGroup++;
			}

			var requiredOrbits = settings.NeutralColonyCount / 2;
			var groupSize = settings.PlayerCount / 2;
			var roleIndex = 0;
			while (requests.Count < requiredOrbits)
			{
				var role = roleIndex++ % 2 == 0 ? "side-route" : "peripheral";
				for (var i = 0; i < groupSize && requests.Count < requiredOrbits; i++)
					requests.Add(new ColonyRequest(role, startPairs[i % startPairs.Length], typeGroup));
				typeGroup++;
			}

			return requests;
		}

		static IReadOnlyList<RmgPoint[]> SelectColonyOrbits(RmgLogicalMap map, RmgProfile profile, RmgGenerationSettings settings,
			ColonyRequest request, string actorType, bool allowCentralRouteOverlap, int routeClearanceRadius,
			bool allowAnyRouteOverlap, int candidateLimit)
		{
			var candidates = new List<ColonyOrbitCandidate>();
			for (var y = 7; y < map.Height - 7; y++)
				for (var x = 7; x < map.Width - 7; x++)
					Consider(new RmgPoint(x, y));

			return candidates
				.OrderByDescending(candidate => candidate.Score)
				.ThenBy(candidate => candidate.Orbit[0].Y)
				.ThenBy(candidate => candidate.Orbit[0].X)
				.Take(candidateLimit)
				.Select(candidate => candidate.Orbit)
				.ToArray();

			void Consider(RmgPoint candidate)
			{
				var transformed = Transform(candidate, settings.Symmetry, map.Width, map.Height);
				if (candidate == transformed || !IsCanonical(candidate, transformed, map.Width))
					return;

				var orbit = new[] { candidate, transformed };
				if (request.Role == "near-start" && request.Targets.Length > 0 && orbit.Any(p => !request.Targets.Contains(NearestStart(map, p))))
					return;
				var routeOverlap = allowAnyRouteOverlap || (request.Role == "central-contest" && allowCentralRouteOverlap);
				var minimumColonySeparation = allowAnyRouteOverlap ? 6 : 5;
				var orbitCombatSpaceIsValid = profile.GeneratorVersion >= 2 ?
					profile.ColonyCombatRules.CombatSpaceIsSafe(actorType, orbit[0], actorType, orbit[1]) :
					orbit[0].ChebyshevDistance(orbit[1]) >= minimumColonySeparation;
				if (!orbit.All(p => ColonyLocationIsValid(map, profile, actorType, p, routeOverlap,
					routeClearanceRadius, minimumColonySeparation)) || !orbitCombatSpaceIsValid)
					return;

				var score = ColonyScore(map, orbit, request);
				if (allowAnyRouteOverlap)
					score -= 10000000L * orbit.Sum(p => RouteOverlapCells(map, p, routeClearanceRadius));
				candidates.Add(new ColonyOrbitCandidate(orbit, score));
			}
		}

		static int RouteOverlapCells(RmgLogicalMap map, RmgPoint point, int routeClearanceRadius)
		{
			var minimumFootprintOffset = routeClearanceRadius == 3 ? -1 : -routeClearanceRadius;
			var count = 0;
			for (var dy = minimumFootprintOffset; dy <= routeClearanceRadius; dy++)
				for (var dx = minimumFootprintOffset; dx <= routeClearanceRadius; dx++)
				{
					var footprint = new RmgPoint(point.X + dx, point.Y + dy);
					if (map.Contains(footprint) && map.RouteMasks[map.Index(footprint)] != 0)
						count++;
				}

			return count;
		}

		static RmgPoint NearestStart(RmgLogicalMap map, RmgPoint point)
		{
			return map.Starts.Select((start, index) => (Start: start, Index: index))
				.OrderBy(entry => point.ManhattanDistance(entry.Start))
				.ThenBy(entry => entry.Index)
				.First().Start;
		}

		static bool IsCanonical(RmgPoint point, RmgPoint transformed, int width)
		{
			return point.Y * width + point.X < transformed.Y * width + transformed.X;
		}

		static bool ColonyLocationIsValid(RmgLogicalMap map, RmgProfile profile, string actorType, RmgPoint point,
			bool allowRouteOverlap, int routeClearanceRadius, int minimumColonySeparation)
		{
			if (!map.Contains(point))
				return false;
			if (profile.GeneratorVersion >= 2)
			{
				if (!ColonyCombatSpaceIsValid(map, profile, actorType, point))
					return false;
			}
			else if (map.Starts.Any(start => start.ChebyshevDistance(point) < 6))
				return false;
			if (map.Chokepoints.Count > 0 && CandidateChokepointDistance(map, point) < 10)
				return false;
			if (map.Obstacles.Any(x => x))
				for (var dy = -4; dy <= 4; dy++)
					for (var dx = -4; dx <= 4; dx++)
					{
						var clearance = new RmgPoint(point.X + dx, point.Y + dy);
						if (!map.Contains(clearance) || map.Obstacles[map.Index(clearance)])
							return false;
					}

			if (profile.GeneratorVersion < 2 && map.Actors.Where(a => a.Role != "start")
				.Any(a => a.LogicalLocation.ChebyshevDistance(point) < minimumColonySeparation))
				return false;

			var minimumFootprintOffset = routeClearanceRadius == 3 ? -1 : -routeClearanceRadius;
			for (var dy = minimumFootprintOffset; dy <= routeClearanceRadius; dy++)
				for (var dx = minimumFootprintOffset; dx <= routeClearanceRadius; dx++)
				{
					var footprint = new RmgPoint(point.X + dx, point.Y + dy);
					if (!map.Contains(footprint) || map.Obstacles[map.Index(footprint)] ||
						(!allowRouteOverlap && map.RouteIds[map.Index(footprint)] >= 0))
						return false;
				}

			return true;
		}

		static int CandidateChokepointDistance(RmgLogicalMap map, RmgPoint colony)
		{
			var distance = int.MaxValue;
			for (var i = 0; i < map.ChokepointIds.Length; i++)
			{
				if (map.ChokepointIds[i] < 0)
					continue;
				var chokeX = i % map.Width;
				var chokeY = i / map.Width;
				for (var chokeDy = 0; chokeDy < 2; chokeDy++)
					for (var chokeDx = 0; chokeDx < 2; chokeDx++)
						for (var colonyDy = 0; colonyDy < 6; colonyDy++)
							for (var colonyDx = 0; colonyDx < 6; colonyDx++)
								distance = Math.Min(distance, Math.Max(
									Math.Abs(2 * chokeX + chokeDx - (2 * colony.X + colonyDx)),
									Math.Abs(2 * chokeY + chokeDy - (2 * colony.Y + colonyDy))));
			}

			return distance;
		}

		static long ColonyScore(RmgLogicalMap map, RmgPoint[] orbit, ColonyRequest request)
		{
			var center = new RmgPoint((map.Width - 1) / 2, (map.Height - 1) / 2);
			var centerDistance = orbit.Sum(p => p.ManhattanDistance(center));
			var nearestStart = orbit.Sum(p => map.Starts.Min(s => p.ManhattanDistance(s)));
			var nearestExisting = orbit.Sum(p => map.Actors.Where(a => a.Role != "start")
				.Select(a => p.ManhattanDistance(a.LogicalLocation)).DefaultIfEmpty(24).Min());

			return request.Role switch
			{
				"near-start" => -Math.Abs(nearestStart - 16) * 1000L - TargetDistance(orbit, request.Targets) * 50L + nearestExisting * 1000L,
				"central-contest" => -centerDistance * 1000L - TargetDistance(orbit, request.Targets) * 1200L + nearestExisting * 1000L,
				"side-route" => -Math.Abs(centerDistance - 38) * 500L - TargetDistance(orbit, request.Targets) * 400L + nearestExisting * 1000L,
				"peripheral" => -Math.Abs(orbit.Sum(EdgeDistance) - 16) * 500L - TargetDistance(orbit, request.Targets) * 400L + nearestExisting * 1000L,
				_ => nearestExisting
			};

			int EdgeDistance(RmgPoint p) => Math.Min(Math.Min(p.X, map.Width - 1 - p.X), Math.Min(p.Y, map.Height - 1 - p.Y));
		}

		static int TargetDistance(IEnumerable<RmgPoint> points, IReadOnlyCollection<RmgPoint> targets)
		{
			if (targets.Count == 0)
				return 0;

			return points.Sum(p => targets.Min(t => p.ManhattanDistance(t)));
		}

		static void ReserveSquare(bool[] layer, RmgLogicalMap map, RmgPoint center, int radius)
		{
			for (var dy = -radius; dy <= radius; dy++)
				for (var dx = -radius; dx <= radius; dx++)
				{
					var point = new RmgPoint(center.X + dx, center.Y + dy);
					if (map.Contains(point))
						layer[map.Index(point)] = true;
				}
		}

		static void AssignRegions(RmgLogicalMap map)
		{
			for (var y = 0; y < map.Height; y++)
				for (var x = 0; x < map.Width; x++)
				{
					var point = new RmgPoint(x, y);
					var bestRegion = 0;
					var bestDistance = int.MaxValue;
					for (var i = 0; i < map.Starts.Count; i++)
					{
						var distance = point.ManhattanDistance(map.Starts[i]);
						if (distance < bestDistance)
						{
							bestDistance = distance;
							bestRegion = i;
						}
					}

					map.RegionIds[map.Index(point)] = bestRegion;
				}
		}

		static void MaterializeTerrainVariants(RmgLogicalMap map, RmgProfile profile, RmgGenerationSettings settings)
		{
			var random = DeterministicRandom.ForStream(settings, profile, "terrain-variants");
			for (var y = 0; y < map.Height; y++)
				for (var x = 0; x < map.Width; x++)
				{
					var point = new RmgPoint(x, y);
					var index = map.Index(point);
					if (map.TemplateIds[index] != 0)
						continue;

					var template = profile.ClearTemplateIds[random.NextInt(profile.ClearTemplateIds.Length)];
					map.TemplateIds[index] = template;
					map.TemplateIds[map.Index(Transform(point, settings.Symmetry, map.Width, map.Height))] = template;
				}
		}

		static RmgValidationReport Validate(RmgLogicalMap map, RmgProfile profile, RmgGenerationSettings settings)
		{
			if (profile.GeneratorVersion >= 2)
				return ValidateBlockingTopology(map, profile, settings);

			var report = new RmgValidationReport();
			void Hard(string code, string message) => report.HardFailures.Add(new RmgValidationIssue(code, message));

			if (map.Starts.Count != settings.PlayerCount)
				Hard("START_COUNT", $"Expected {settings.PlayerCount} starts but found {map.Starts.Count}.");

			var startSet = map.Starts.ToHashSet();
			foreach (var start in map.Starts)
			{
				if (!startSet.Contains(Transform(start, settings.Symmetry, map.Width, map.Height)))
					Hard("START_SYMMETRY", $"Start {start} lacks its symmetry partner.");
				if (start.X * 2 < profile.StartRegionRadiusNative || start.Y * 2 < profile.StartRegionRadiusNative ||
					(map.Width - 1 - start.X) * 2 < profile.StartRegionRadiusNative || (map.Height - 1 - start.Y) * 2 < profile.StartRegionRadiusNative)
					Hard("START_APRON", $"Start {start} violates the native-cell start-region radius.");
				if (map.RouteIds[map.Index(start)] < 0)
					Hard("START_ROUTE", $"Start {start} is not connected to a reserved strategic route.");
			}

			var allowedTemplates = profile.ClearTemplateIds.ToHashSet();
			for (var i = 0; i < map.TemplateIds.Length; i++)
			{
				if (!allowedTemplates.Contains(map.TemplateIds[i]))
					Hard("TERRAIN_TEMPLATE", $"Logical cell {i} uses template {map.TemplateIds[i]}, which is not in the Clear-only allow-list.");
				if (map.Obstacles[i])
					Hard("OBSTACLE_CONTRACT", $"Logical cell {i} is blocked despite the Version 1 zero-obstacle contract.");
			}

			for (var y = 0; y < map.Height; y++)
				for (var x = 0; x < map.Width; x++)
				{
					var point = new RmgPoint(x, y);
					var transformed = Transform(point, settings.Symmetry, map.Width, map.Height);
					if (map.TemplateIds[map.Index(point)] != map.TemplateIds[map.Index(transformed)])
						Hard("TERRAIN_SYMMETRY", $"Terrain at {point} differs from its symmetry partner {transformed}.");
					if (map.RouteIds[map.Index(point)] >= 0 != map.RouteIds[map.Index(transformed)] >= 0)
						Hard("ROUTE_SYMMETRY", $"Route reservation at {point} differs from its symmetry partner {transformed}.");
				}

			var spawns = map.Actors.Where(a => a.Type == profile.SpawnActor).ToArray();
			var colonies = map.Actors.Where(a => a.Owner == profile.ColonyOwner).ToArray();
			if (spawns.Length != settings.PlayerCount)
				Hard("SPAWN_ACTORS", $"Expected {settings.PlayerCount} spawn actors but found {spawns.Length}.");
			if (colonies.Length != settings.NeutralColonyCount)
				Hard("COLONY_COUNT", $"Expected {settings.NeutralColonyCount} neutral colonies but found {colonies.Length}.");

			foreach (var actor in map.Actors)
			{
				var partner = Transform(actor.LogicalLocation, settings.Symmetry, map.Width, map.Height);
				if (!map.Actors.Any(a => a.Type == actor.Type && a.Owner == actor.Owner && a.Role == actor.Role && a.LogicalLocation == partner))
					Hard("ACTOR_SYMMETRY", $"Actor {actor.Type} at {actor.LogicalLocation} lacks an equivalent symmetry partner.");
			}

			for (var i = 0; i < colonies.Length; i++)
				for (var j = i + 1; j < colonies.Length; j++)
					if (colonies[i].LogicalLocation.ChebyshevDistance(colonies[j].LogicalLocation) < 5)
						Hard("COLONY_OVERLAP", $"Colonies at {colonies[i].LogicalLocation} and {colonies[j].LogicalLocation} have overlapping safety envelopes.");

			var colonyAssignments = new int[settings.PlayerCount];
			foreach (var colony in colonies)
			{
				var nearest = Enumerable.Range(0, map.Starts.Count)
					.OrderBy(i => colony.LogicalLocation.ManhattanDistance(map.Starts[i]))
					.ThenBy(i => i)
					.First();
				colonyAssignments[nearest]++;
			}

			var center = new RmgPoint((map.Width - 1) / 2, (map.Height - 1) / 2);
			var centralCount = colonies.Count(c => c.LogicalLocation.ManhattanDistance(center) <= 16);
			var (reachableStarts, passableCells, maximumStartDistance) = NativeConnectivityProxy(map, profile);
			if (reachableStarts != map.Starts.Count)
				Hard("NATIVE_CONNECTIVITY", $"The native 3x3 obstruction proxy reaches {reachableStarts}/{map.Starts.Count} starts.");

			const int ReservedRouteWidthNative = 6;
			if (profile.MinimumRouteWidthNative > ReservedRouteWidthNative)
				Hard("ROUTE_WIDTH", $"Reserved route width {ReservedRouteWidthNative} is below the required {profile.MinimumRouteWidthNative} native cells.");

			report.Metrics["player_count"] = settings.PlayerCount;
			report.Metrics["neutral_colony_count"] = colonies.Length;
			report.Metrics["colony_assignment_spread"] = colonyAssignments.Max() - colonyAssignments.Min();
			report.Metrics["central_colony_count"] = centralCount;
			report.Metrics["route_cell_count"] = map.RouteIds.Count(r => r >= 0);
			report.Metrics["obstacle_cell_count"] = map.Obstacles.Count(o => o);
			report.Metrics["minimum_colony_separation_logical"] = colonies.Length < 2 ? 0 :
				colonies.SelectMany((a, i) => colonies.Skip(i + 1).Select(b => a.LogicalLocation.ChebyshevDistance(b.LogicalLocation))).Min();
			report.Metrics["minimum_reserved_route_width_native"] = ReservedRouteWidthNative;
			report.Metrics["native_proxy_reachable_starts"] = reachableStarts;
			report.Metrics["native_proxy_passable_cells"] = passableCells;
			report.Metrics["native_proxy_max_start_distance"] = maximumStartDistance;
			report.Metrics["repair_count"] = map.RepairCount;
			report.Metrics["retry_count"] = map.RetryCount;

			if (settings.Archetype == RmgArchetype.CentralContest && centralCount == 0)
				Hard("CENTRAL_CONTEST", "The central-contest archetype did not place a colony in the central scoring zone.");
			if (colonyAssignments.Max() - colonyAssignments.Min() > 2)
				Hard("COLONY_BALANCE", $"Nearest-start colony assignment spread is {colonyAssignments.Max() - colonyAssignments.Min()}, above the Version 1 tolerance of 2.");

			foreach (var startIndex in Enumerable.Range(0, map.Starts.Count))
			{
				var outgoing = map.GraphEdges.Count(e => e.From == $"start-{startIndex}");
				if (outgoing < 2)
					Hard("EDGE_DISJOINT_GRAPH", $"Start {startIndex} has only {outgoing} independent strategic-graph edges.");
			}

			return report;
		}

		static (int ReachableStarts, int PassableCells, int MaximumStartDistance) NativeConnectivityProxy(RmgLogicalMap map, RmgProfile profile)
		{
			var width = profile.PlayableWidth;
			var height = profile.PlayableHeight;
			var blocked = new bool[width * height];
			if (profile.GeneratorVersion >= 2)
				for (var logicalY = 0; logicalY < map.Height; logicalY++)
					for (var logicalX = 0; logicalX < map.Width; logicalX++)
					{
						var logical = new RmgPoint(logicalX, logicalY);
						var logicalIndex = map.Index(logical);
						for (var frame = 0; frame < 4; frame++)
						{
							var water = profile.UsesShorelineMaterialization ? map.NativeTerrainIntents[4 * logicalIndex + frame] == RmgNativeTerrainIntent.Water :
								map.Obstacles[logicalIndex];
							if (water)
								blocked[(2 * logicalY + frame / 2) * width + 2 * logicalX + frame % 2] = true;
						}
					}

			foreach (var colony in map.Actors.Where(a => a.Owner == profile.ColonyOwner))
			{
				var anchorX = 2 * colony.LogicalLocation.X;
				var anchorY = 2 * colony.LogicalLocation.Y;

				// A conservative 6x6 colony envelope expanded by one native cell models a 3x3 mover.
				for (var y = anchorY - 3; y <= anchorY + 4; y++)
					for (var x = anchorX - 3; x <= anchorX + 4; x++)
						if (x >= 0 && x < width && y >= 0 && y < height)
							blocked[y * width + x] = true;
			}

			var distances = Enumerable.Repeat(-1, width * height).ToArray();
			var firstStart = map.Starts[0];
			var firstIndex = 2 * firstStart.Y * width + 2 * firstStart.X;
			var queue = new Queue<int>();
			distances[firstIndex] = 0;
			queue.Enqueue(firstIndex);
			var directions = new[]
			{
				(-1, -1), (0, -1), (1, -1),
				(-1, 0), (1, 0),
				(-1, 1), (0, 1), (1, 1)
			};

			while (queue.Count > 0)
			{
				var current = queue.Dequeue();
				var currentX = current % width;
				var currentY = current / width;
				foreach (var (dx, dy) in directions)
				{
					var x = currentX + dx;
					var y = currentY + dy;
					if (x < 0 || x >= width || y < 0 || y >= height)
						continue;

					var next = y * width + x;
					if (blocked[next] || distances[next] >= 0)
						continue;

					distances[next] = distances[current] + 1;
					queue.Enqueue(next);
				}
			}

			var reachableStarts = 0;
			var maximumStartDistance = 0;
			foreach (var start in map.Starts)
			{
				var distance = distances[2 * start.Y * width + 2 * start.X];
				if (distance >= 0)
				{
					reachableStarts++;
					maximumStartDistance = Math.Max(maximumStartDistance, distance);
				}
			}

			return (reachableStarts, blocked.Count(cell => !cell), maximumStartDistance);
		}

		static string HashLogicalMap(RmgLogicalMap map)
		{
			var text = new StringBuilder();
			for (var i = 0; i < map.TemplateIds.Length; i++)
				text.Append(map.TemplateIds[i]).Append(',').Append(map.RegionIds[i]).Append(',').Append(map.RouteIds[i]).Append(',')
					.Append(map.StartReservations[i] ? '1' : '0').Append(map.StructureReservations[i] ? '1' : '0').Append(map.Obstacles[i] ? '1' : '0').Append('\n');
			return Sha256(text.ToString());
		}

		static string HashActors(RmgLogicalMap map)
		{
			var text = string.Join("\n", map.Actors.OrderBy(a => a.Type, StringComparer.Ordinal).ThenBy(a => a.Owner, StringComparer.Ordinal)
				.ThenBy(a => a.LogicalLocation.Y).ThenBy(a => a.LogicalLocation.X).ThenBy(a => a.NativeFrame)
				.Select(a => $"{a.Type}|{a.Owner}|{a.Role}|{a.LogicalLocation.X},{a.LogicalLocation.Y}|{a.EquivalenceGroup}" +
					(a.NativeFrame == 0 ? string.Empty : $"|frame={a.NativeFrame}")));
			return Sha256(text);
		}

		static string HashGraph(RmgLogicalMap map)
		{
			var nodes = map.GraphNodes.OrderBy(n => n.Id, StringComparer.Ordinal).Select(n => $"N|{n.Id}|{n.Role}|{n.Location.X},{n.Location.Y}");
			var edges = map.GraphEdges.OrderBy(e => e.Id, StringComparer.Ordinal).Select(e => $"E|{e.Id}|{e.From}|{e.To}|{e.RouteId}");
			return Sha256(string.Join("\n", nodes.Concat(edges)));
		}

		static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
	}
}
