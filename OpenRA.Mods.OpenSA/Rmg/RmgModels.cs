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
using Newtonsoft.Json.Linq;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public enum RmgSymmetry
	{
		MirrorHorizontal,
		MirrorVertical,
		Rotate180
	}

	public enum RmgArchetype
	{
		Open,
		CentralContest
	}

	public enum RmgTopologyPreset
	{
		Off,
		Mixed,
		Shoreline,
		LandDetails,
		LandCover,
		BattlefieldLayout,
		ParameterizedBattlefield,
		CoherentWater,
		NaturalTerrain,
		NaturalTerrainV10,
		NaturalRegions
	}

	public enum RmgLayoutFamily
	{
		StructuredCompetitive,
		NaturalLandscape,
		ArtificialBattlefield,
		NaturalLandscapePvp
	}

	public enum RmgParameterLevel
	{
		Low,
		Standard,
		High,
		Extreme,
		Ultra
	}

	public readonly struct RmgPoint : IEquatable<RmgPoint>
	{
		public int X { get; }
		public int Y { get; }

		public RmgPoint(int x, int y)
		{
			X = x;
			Y = y;
		}

		public int ManhattanDistance(RmgPoint other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);
		public int ChebyshevDistance(RmgPoint other) => Math.Max(Math.Abs(X - other.X), Math.Abs(Y - other.Y));
		public bool Equals(RmgPoint other) => X == other.X && Y == other.Y;
		public override bool Equals(object obj) => obj is RmgPoint other && Equals(other);
		public override int GetHashCode() => HashCode.Combine(X, Y);
		public override string ToString() => $"{X},{Y}";
		public static bool operator ==(RmgPoint left, RmgPoint right) => left.Equals(right);
		public static bool operator !=(RmgPoint left, RmgPoint right) => !left.Equals(right);
	}

	public sealed class RmgGenerationSettings
	{
		public string Tileset { get; set; } = "NORMAL";
		public ulong Seed { get; set; }
		public int MapSize { get; set; } = 128;
		public int PlayerCount { get; set; } = 2;
		public RmgSymmetry Symmetry { get; set; } = RmgSymmetry.MirrorHorizontal;
		public RmgArchetype Archetype { get; set; } = RmgArchetype.Open;
		public int NeutralColonyCount { get; set; } = 10;
		public int GeneratorVersion { get; set; } = 1;
		public RmgTopologyPreset TopologyPreset { get; set; } = RmgTopologyPreset.Off;
		public RmgParameterLevel WaterAmount { get; set; } = RmgParameterLevel.Standard;
		public Reassessment.TerrainComplexity TerrainComplexity { get; set; } = Reassessment.TerrainComplexity.Standard;
		public RmgParameterLevel TacticalTerrain { get; set; } = RmgParameterLevel.Standard;
		public RmgLayoutFamily LayoutFamily { get; set; } = RmgLayoutFamily.ArtificialBattlefield;
		public bool OriginalSurfaceRelations { get; set; } = true;
		public bool PreventColonyOverlapping { get; set; } = true;
		public RmgColonyWeights NeutralColonyWeights { get; set; } = new();
		public int[] StartingColonyShares { get; set; } = Array.Empty<int>();
		public RmgColonyOwnershipMode StartingColonyMode { get; set; } = RmgColonyOwnershipMode.ClosestToSpawn;
		public int MirroringAxes { get; set; }
		public int EffectiveNeutralColonyCount => GeneratorVersion is 15 or 16 or 17 && NeutralColonyWeights.Total == 0 ? 0 : NeutralColonyCount;
		public RmgPlayerSettingsResolution PlayerSettingsResolution { get; set; }

		public string Canonical(RmgProfile profile)
		{
			var fields = new List<string>
			{
				$"profile={profile.ProfileId}",
				$"configuration={profile.ConfigurationVersion}",
				$"generator={GeneratorVersion}",
				$"seed={Seed}",
				$"players={PlayerCount}",
				$"symmetry={Symmetry}",
				$"archetype={Archetype}",
				$"colonies={NeutralColonyCount}"
			};
			if (GeneratorVersion is 11 or 12 or 13 or 14 or 15 or 16 or 17)
				fields.RemoveAll(field => field.StartsWith("symmetry=", StringComparison.Ordinal) || field.StartsWith("archetype=", StringComparison.Ordinal));
			if (GeneratorVersion >= 2)
				fields.Add($"topology={TopologyPreset}");
			if (GeneratorVersion >= 7)
			{
				fields.Add($"water={RmgPlayerSettingsContract.ParameterLevelName(WaterAmount)}");
				fields.Add($"{(GeneratorVersion is 11 or 12 or 13 or 14 or 15 or 16 or 17 ? "gravel-moss-amount" : "tactical-terrain")}={RmgPlayerSettingsContract.ParameterLevelName(TacticalTerrain)}");
			}

			if (GeneratorVersion is 11 or 12 or 13 or 14 or 15 or 16 or 17)
				fields.Add($"terrain-complexity={RmgPlayerSettingsContract.ComplexityDisplayName(TerrainComplexity, GeneratorVersion is 13 or 14 or 15 or 16 or 17)}");

			if (GeneratorVersion >= 8)
				fields.Add($"layout-family={RmgPlayerSettingsContract.LayoutFamilyName(LayoutFamily)}");
			if (GeneratorVersion >= 9)
				fields.Add($"original-surface-relations={OriginalSurfaceRelations.ToString().ToLowerInvariant()}");

			if (GeneratorVersion is 14 or 15 or 16 or 17)
				fields.Add($"prevent-colony-overlapping={PreventColonyOverlapping.ToString().ToLowerInvariant()}");

			if (GeneratorVersion is 15 or 16 or 17)
				fields.Add($"neutral-colony-weights={NeutralColonyWeights.ToJson().ToString(Newtonsoft.Json.Formatting.None)}");

			if (GeneratorVersion is 16 or 17)
				fields.Add($"starting-colony-shares={string.Join(",", StartingColonyShares)}");

			if (GeneratorVersion is 16 or 17 && StartingColonyMode != RmgColonyOwnershipMode.ClosestToSpawn)
				fields.Add($"starting-colony-mode={RmgColonyOwnership.ModeName(StartingColonyMode)}");

			if (GeneratorVersion == 17) fields.Add($"mirroring-axes={MirroringAxes}");

			if (Tileset != "NORMAL") fields.Add($"tileset={Tileset}");

			if (MapSize != 128)
				fields.Add($"size={MapSize},{MapSize}");

			return string.Join("\n", fields);
		}
	}

	public sealed class RmgProfile
	{
		public string ProfileId { get; private set; }
		public int ConfigurationVersion { get; private set; }
		public int GeneratorVersion { get; private set; }
		public string Tileset { get; private set; }
		public int PlayableWidth { get; private set; }
		public int PlayableHeight { get; private set; }
		public int CordonWidth { get; private set; }
		public int LogicalWidth { get; private set; }
		public int LogicalHeight { get; private set; }
		public int MinimumRouteWidthNative { get; private set; }
		public int StartRegionRadiusNative { get; private set; }
		public ushort[] ClearTemplateIds { get; private set; }
		public ushort[] ClearLandDetailTemplateIds { get; private set; }
		public ushort[] BlockedTemplateIds { get; private set; }
		public ushort[] OpenWaterDetailTemplateIds { get; private set; }
		public string[] NeutralColonyActors { get; private set; }
		public string SpawnActor { get; private set; }
		public string SpawnOwner { get; private set; }
		public string ColonyOwner { get; private set; }
		public int ObstacleDensity { get; private set; }
		public int VegetationDensity { get; private set; }
		public int ShorelineDecorationPercent { get; private set; }
		public int OpenWaterDetailPercent { get; private set; }
		public int ClearLandDetailPercent { get; private set; }
		public int RockLandPercent { get; private set; }
		public int VegetationLandPercent { get; private set; }
		public int RockDetailPercent { get; private set; }
		public int VegetationDetailPercent { get; private set; }
		public int LandCoverTolerancePercent { get; private set; }
		public int BattlefieldFlankRadiusLogical { get; private set; }
		public int TacticalLandAnchorOrbitCount { get; private set; }
		public string[] SoilDecorationActors { get; private set; }
		public string[] RockDecorationActors { get; private set; }
		public string[] VegetationDecorationActors { get; private set; }
		public string[] BlockingDecorationActors { get; private set; }
		public int LandDecorationPerThousand { get; private set; }
		public int MinimumLandDecorationSectors { get; private set; }
		public int OpenObstacleDensityTarget { get; private set; }
		public int OpenObstacleDensityMinimum { get; private set; }
		public int OpenObstacleDensityMaximum { get; private set; }
		public int CentralObstacleDensityTarget { get; private set; }
		public int CentralObstacleDensityMinimum { get; private set; }
		public int CentralObstacleDensityMaximum { get; private set; }
		public int MajorRouteWidthNative { get; private set; }
		public int ChokepointWidthNative { get; private set; }
		public int ChokepointLengthMinimumNative { get; private set; }
		public int ChokepointLengthMaximumNative { get; private set; }
		public int ObstacleRegionMinimumLogical { get; private set; }
		public int ObstacleRegionMaximumLogical { get; private set; }
		public int MaximumTopologyAttempts { get; private set; }
		public int MaximumRepairOperations { get; private set; }
		public int MaximumRepairCellsLogical { get; private set; }
		public int ColonyCombatSafetyBufferNative { get; private set; }
		public int LowWaterDensityAdjustment { get; private set; }
		public int HighWaterDensityAdjustment { get; private set; }
		public int LowRockLandPercent { get; private set; }
		public int LowVegetationLandPercent { get; private set; }
		public int LowTacticalLandAnchorOrbitCount { get; private set; }
		public int HighRockLandPercent { get; private set; }
		public int HighVegetationLandPercent { get; private set; }
		public int HighTacticalLandAnchorOrbitCount { get; private set; }
		public RmgColonyCombatRules ColonyCombatRules { get; private set; }
		public RmgDirtPlacementRules DirtPlacementRules { get; private set; }
		public bool UsesShorelineMaterialization => GeneratorVersion >= 3;
		public bool UsesClearLandDetails => GeneratorVersion >= 4;
		public bool UsesLandCover => GeneratorVersion >= 5;
		public bool UsesBattlefieldLayout => GeneratorVersion >= 6 && GeneratorVersion <= 8;
		public bool UsesTerrainDecorations => GeneratorVersion >= 6;
		public bool UsesParameterizedBattlefield => GeneratorVersion >= 7;
		public bool UsesCoherentWaterMorphology => GeneratorVersion == 8;
		public bool UsesNaturalTerrainMorphologyV10 => GeneratorVersion == 10;
		public bool UsesRegionsTerrain => GeneratorVersion is 11 or 12 or 13 or 14 or 15 or 16 or 17;
		public bool UsesNaturalTerrainMorphology => GeneratorVersion is 9 or 10 or 11 or 12 or 13 or 14 or 15 or 16 or 17;

		public int ObstacleDensityTarget(RmgArchetype archetype, RmgParameterLevel waterAmount)
		{
			var standard = archetype == RmgArchetype.Open ? OpenObstacleDensityTarget : CentralObstacleDensityTarget;
			return standard + WaterDensityAdjustment(waterAmount);
		}

		public (int Minimum, int Maximum) ObstacleDensityRange(RmgArchetype archetype, RmgParameterLevel waterAmount)
		{
			var standard = archetype == RmgArchetype.Open ?
				(OpenObstacleDensityMinimum, OpenObstacleDensityMaximum) :
				(CentralObstacleDensityMinimum, CentralObstacleDensityMaximum);
			var adjustment = WaterDensityAdjustment(waterAmount);
			return (standard.Item1 + adjustment, standard.Item2 + adjustment);
		}

		public int RockLandPercentFor(RmgParameterLevel tacticalTerrain) => tacticalTerrain switch
		{
			RmgParameterLevel.Low when UsesParameterizedBattlefield => LowRockLandPercent,
			RmgParameterLevel.High when UsesParameterizedBattlefield => HighRockLandPercent,
			RmgParameterLevel.Extreme when GeneratorVersion is 14 or 15 or 16 or 17 => 24,
			RmgParameterLevel.Ultra when GeneratorVersion is 14 or 15 or 16 or 17 => 36,
			_ => RockLandPercent
		};

		public int VegetationLandPercentFor(RmgParameterLevel tacticalTerrain) => tacticalTerrain switch
		{
			RmgParameterLevel.Low when UsesParameterizedBattlefield => LowVegetationLandPercent,
			RmgParameterLevel.High when UsesParameterizedBattlefield => HighVegetationLandPercent,
			RmgParameterLevel.Extreme when GeneratorVersion is 14 or 15 or 16 or 17 => 16,
			RmgParameterLevel.Ultra when GeneratorVersion is 14 or 15 or 16 or 17 => 25,
			_ => VegetationLandPercent
		};

		public int TacticalLandAnchorOrbitCountFor(RmgParameterLevel tacticalTerrain) => tacticalTerrain switch
		{
			RmgParameterLevel.Low when UsesParameterizedBattlefield => LowTacticalLandAnchorOrbitCount,
			RmgParameterLevel.High when UsesParameterizedBattlefield => HighTacticalLandAnchorOrbitCount,
			_ => TacticalLandAnchorOrbitCount
		};

		int WaterDensityAdjustment(RmgParameterLevel waterAmount) => waterAmount switch
		{
			RmgParameterLevel.Low when UsesParameterizedBattlefield => LowWaterDensityAdjustment,
			RmgParameterLevel.High when UsesParameterizedBattlefield => HighWaterDensityAdjustment,
			_ => 0
		};

		public static RmgProfile Load(ModData modData, RmgGenerationSettings settings)
		{
			if (settings.TopologyPreset == RmgTopologyPreset.NaturalRegions && (settings.MapSize is 128 or 256 || (settings.MapSize is 64 or 512 && settings.GeneratorVersion is 16 or 17)))
			{
				var profile = Load(modData, $"sa|rmg/normal-natural-regions-v{(settings.GeneratorVersion is 12 or 13 or 14 or 15 or 16 or 17 ? settings.GeneratorVersion : 11)}{(settings.MapSize == 128 ? string.Empty : $"-{settings.MapSize}")}.yaml");
				if (!RmgBiome.IsSupported(settings.Tileset) || (settings.Tileset != "NORMAL" && settings.GeneratorVersion is not (16 or 17)))
					throw new ArgumentException("Additional tilesets require Regions V16.");
				if (settings.Tileset != "NORMAL")
				{
					profile.Tileset = settings.Tileset;
					profile.ProfileId = settings.Tileset.ToLowerInvariant() + profile.ProfileId[6..];
					var decorations = RmgBiome.Decorations(settings.Tileset);
					profile.SoilDecorationActors = decorations.Take(2).ToArray();
					profile.RockDecorationActors = new[] { decorations[2] };
					profile.VegetationDecorationActors = new[] { decorations[3] };
					profile.Validate();
				}

				return profile;
			}

			if (settings.MapSize == 128)
				return Load(modData, settings.TopologyPreset);
			if (settings.MapSize == 256 && settings.TopologyPreset == RmgTopologyPreset.NaturalTerrainV10)
				return Load(modData, "sa|rmg/normal-natural-landscape-v10-256.yaml");
			throw new ArgumentException("Requested size is not supported by this generator profile. Regions V16 supports 64, 128, 256 and 512.");
		}

		public static RmgProfile Load(ModData modData, RmgTopologyPreset topologyPreset) =>
			Load(modData, topologyPreset switch
			{
				RmgTopologyPreset.NaturalRegions => "sa|rmg/normal-natural-regions-v11.yaml",
				RmgTopologyPreset.Mixed => "sa|rmg/normal-water-blocking-v2.yaml",
				RmgTopologyPreset.Shoreline => "sa|rmg/normal-water-shoreline-v3.yaml",
				RmgTopologyPreset.LandDetails => "sa|rmg/normal-land-details-v4.yaml",
				RmgTopologyPreset.LandCover => "sa|rmg/normal-land-cover-v5.yaml",
				RmgTopologyPreset.BattlefieldLayout => "sa|rmg/normal-battlefield-layout-v6.yaml",
				RmgTopologyPreset.ParameterizedBattlefield => "sa|rmg/normal-parameterized-battlefield-v7.yaml",
				RmgTopologyPreset.CoherentWater => "sa|rmg/normal-coherent-water-v8.yaml",
				RmgTopologyPreset.NaturalTerrain => "sa|rmg/normal-natural-landscape-v9.yaml",
				RmgTopologyPreset.NaturalTerrainV10 => "sa|rmg/normal-natural-landscape-v10.yaml",
				_ => "sa|rmg/normal-clear-v1.yaml"
			});

		public static RmgProfile Load(ModData modData, string path = "sa|rmg/normal-clear-v1.yaml")
		{
			using var stream = modData.DefaultFileSystem.Open(path);
			var nodes = MiniYaml.FromStream(stream, path).ToDictionary(n => n.Key, n => n.Value, StringComparer.Ordinal);

			T Get<T>(string key)
			{
				if (!nodes.TryGetValue(key, out var value))
					throw new InvalidOperationException($"RMG profile is missing required field '{key}'.");

				return FieldLoader.GetValue<T>(key, value.Value);
			}

			T GetOptional<T>(string key, T fallback) =>
				nodes.TryGetValue(key, out var value) ? FieldLoader.GetValue<T>(key, value.Value) : fallback;

			var profile = new RmgProfile
			{
				ProfileId = Get<string>(nameof(ProfileId)),
				ConfigurationVersion = Get<int>(nameof(ConfigurationVersion)),
				GeneratorVersion = Get<int>(nameof(GeneratorVersion)),
				Tileset = Get<string>(nameof(Tileset)),
				PlayableWidth = Get<int>(nameof(PlayableWidth)),
				PlayableHeight = Get<int>(nameof(PlayableHeight)),
				CordonWidth = Get<int>(nameof(CordonWidth)),
				LogicalWidth = Get<int>(nameof(LogicalWidth)),
				LogicalHeight = Get<int>(nameof(LogicalHeight)),
				MinimumRouteWidthNative = Get<int>(nameof(MinimumRouteWidthNative)),
				StartRegionRadiusNative = Get<int>(nameof(StartRegionRadiusNative)),
				ClearTemplateIds = Get<int[]>(nameof(ClearTemplateIds)).Select(i => checked((ushort)i)).ToArray(),
				ClearLandDetailTemplateIds = GetOptional(nameof(ClearLandDetailTemplateIds), Array.Empty<int>()).Select(i => checked((ushort)i)).ToArray(),
				BlockedTemplateIds = GetOptional(nameof(BlockedTemplateIds), Array.Empty<int>()).Select(i => checked((ushort)i)).ToArray(),
				OpenWaterDetailTemplateIds = GetOptional(nameof(OpenWaterDetailTemplateIds), Array.Empty<int>()).Select(i => checked((ushort)i)).ToArray(),
				NeutralColonyActors = Get<string[]>(nameof(NeutralColonyActors)),
				SpawnActor = Get<string>(nameof(SpawnActor)),
				SpawnOwner = Get<string>(nameof(SpawnOwner)),
				ColonyOwner = Get<string>(nameof(ColonyOwner)),
				ObstacleDensity = Get<int>(nameof(ObstacleDensity)),
				VegetationDensity = Get<int>(nameof(VegetationDensity)),
				ShorelineDecorationPercent = GetOptional(nameof(ShorelineDecorationPercent), 0),
				OpenWaterDetailPercent = GetOptional(nameof(OpenWaterDetailPercent), 0),
				ClearLandDetailPercent = GetOptional(nameof(ClearLandDetailPercent), 0),
				RockLandPercent = GetOptional(nameof(RockLandPercent), 0),
				VegetationLandPercent = GetOptional(nameof(VegetationLandPercent), 0),
				RockDetailPercent = GetOptional(nameof(RockDetailPercent), 0),
				VegetationDetailPercent = GetOptional(nameof(VegetationDetailPercent), 0),
				LandCoverTolerancePercent = GetOptional(nameof(LandCoverTolerancePercent), 0),
				BattlefieldFlankRadiusLogical = GetOptional(nameof(BattlefieldFlankRadiusLogical), 0),
				TacticalLandAnchorOrbitCount = GetOptional(nameof(TacticalLandAnchorOrbitCount), 0),
				SoilDecorationActors = GetOptional(nameof(SoilDecorationActors), Array.Empty<string>()),
				RockDecorationActors = GetOptional(nameof(RockDecorationActors), Array.Empty<string>()),
				VegetationDecorationActors = GetOptional(nameof(VegetationDecorationActors), Array.Empty<string>()),
				BlockingDecorationActors = GetOptional(nameof(BlockingDecorationActors), Array.Empty<string>()),
				LandDecorationPerThousand = GetOptional(nameof(LandDecorationPerThousand), 0),
				MinimumLandDecorationSectors = GetOptional(nameof(MinimumLandDecorationSectors), 0),
				OpenObstacleDensityTarget = GetOptional(nameof(OpenObstacleDensityTarget), 0),
				OpenObstacleDensityMinimum = GetOptional(nameof(OpenObstacleDensityMinimum), 0),
				OpenObstacleDensityMaximum = GetOptional(nameof(OpenObstacleDensityMaximum), 0),
				CentralObstacleDensityTarget = GetOptional(nameof(CentralObstacleDensityTarget), 0),
				CentralObstacleDensityMinimum = GetOptional(nameof(CentralObstacleDensityMinimum), 0),
				CentralObstacleDensityMaximum = GetOptional(nameof(CentralObstacleDensityMaximum), 0),
				MajorRouteWidthNative = GetOptional(nameof(MajorRouteWidthNative), 0),
				ChokepointWidthNative = GetOptional(nameof(ChokepointWidthNative), 0),
				ChokepointLengthMinimumNative = GetOptional(nameof(ChokepointLengthMinimumNative), 0),
				ChokepointLengthMaximumNative = GetOptional(nameof(ChokepointLengthMaximumNative), 0),
				ObstacleRegionMinimumLogical = GetOptional(nameof(ObstacleRegionMinimumLogical), 0),
				ObstacleRegionMaximumLogical = GetOptional(nameof(ObstacleRegionMaximumLogical), 0),
				MaximumTopologyAttempts = GetOptional(nameof(MaximumTopologyAttempts), 0),
				MaximumRepairOperations = GetOptional(nameof(MaximumRepairOperations), 0),
				MaximumRepairCellsLogical = GetOptional(nameof(MaximumRepairCellsLogical), 0),
				ColonyCombatSafetyBufferNative = GetOptional(nameof(ColonyCombatSafetyBufferNative), 0),
				LowWaterDensityAdjustment = GetOptional(nameof(LowWaterDensityAdjustment), 0),
				HighWaterDensityAdjustment = GetOptional(nameof(HighWaterDensityAdjustment), 0),
				LowRockLandPercent = GetOptional(nameof(LowRockLandPercent), 0),
				LowVegetationLandPercent = GetOptional(nameof(LowVegetationLandPercent), 0),
				LowTacticalLandAnchorOrbitCount = GetOptional(nameof(LowTacticalLandAnchorOrbitCount), 0),
				HighRockLandPercent = GetOptional(nameof(HighRockLandPercent), 0),
				HighVegetationLandPercent = GetOptional(nameof(HighVegetationLandPercent), 0),
				HighTacticalLandAnchorOrbitCount = GetOptional(nameof(HighTacticalLandAnchorOrbitCount), 0)
			};

			profile.Validate();
			if (profile.GeneratorVersion >= 2)
				profile.ColonyCombatRules = RmgColonyCombatRules.Load(modData, profile.NeutralColonyActors,
					profile.ColonyCombatSafetyBufferNative);
			if (profile.UsesNaturalTerrainMorphologyV10)
				profile.DirtPlacementRules = RmgDirtPlacementRules.Load(modData, profile.NeutralColonyActors);
			return profile;
		}

		void Validate()
		{
			if (UsesRegionsTerrain)
			{
				var suffix = PlayableWidth == 128 ? string.Empty : $"-{PlayableWidth}";
				if (ProfileId != $"{Tileset.ToLowerInvariant()}-natural-regions-v{GeneratorVersion}" + suffix || ConfigurationVersion != (GeneratorVersion == 13 ? 4 : 1) ||
					!RmgBiome.IsSupported(Tileset) || (Tileset != "NORMAL" && GeneratorVersion is not (16 or 17)) || (PlayableWidth is not (128 or 256) && !(PlayableWidth is 64 or 512 && GeneratorVersion is 16 or 17)) || PlayableHeight != PlayableWidth ||
					LogicalWidth * 2 != PlayableWidth || LogicalHeight != LogicalWidth || CordonWidth != 2 ||
					ClearTemplateIds.Length == 0 || BlockedTemplateIds.Length == 0 || NeutralColonyActors.Length != 5 ||
					ColonyCombatSafetyBufferNative != 1 || LandDecorationPerThousand != 3)
					throw new InvalidOperationException("Regions profile does not match its geometry, actors, or local placement contract.");
				return;
			}

			var version1 = ProfileId == "normal-clear-v1" && ConfigurationVersion == 1 && GeneratorVersion == 1;
			var version2 = ProfileId == "normal-water-blocking-v2" && ConfigurationVersion == 3 && GeneratorVersion == 2;
			var version3 = ProfileId == "normal-water-shoreline-v3" && ConfigurationVersion == 1 && GeneratorVersion == 3;
			var version4 = ProfileId == "normal-land-details-v4" && ConfigurationVersion == 1 && GeneratorVersion == 4;
			var version5 = ProfileId == "normal-land-cover-v5" && ConfigurationVersion == 1 && GeneratorVersion == 5;
			var version6 = ProfileId == "normal-battlefield-layout-v6" && ConfigurationVersion == 1 && GeneratorVersion == 6;
			var version7 = ProfileId == "normal-parameterized-battlefield-v7" && ConfigurationVersion == 1 && GeneratorVersion == 7;
			var version8 = ProfileId == "normal-coherent-water-v8" && ConfigurationVersion == 1 && GeneratorVersion == 8;
			var version9 = ProfileId == "normal-natural-landscape-v9" && ConfigurationVersion == 1 && GeneratorVersion == 9;
			var largeNatural = ProfileId == "normal-natural-landscape-v10-256" && ConfigurationVersion == 1 && GeneratorVersion == 10;
			var version10 = (ProfileId == "normal-natural-landscape-v10" || largeNatural) && ConfigurationVersion == 1 && GeneratorVersion == 10;
			if (!version1 && !version2 && !version3 && !version4 && !version5 && !version6 && !version7 && !version8 && !version9 && !version10)
				throw new InvalidOperationException("Only frozen Generator Versions 1 through 9 and opt-in Version 10 layout-family profiles are supported.");
			var size = largeNatural ? 256 : 128;
			if (Tileset != "NORMAL" || PlayableWidth != size || PlayableHeight != size || CordonWidth != 2 || LogicalWidth != size / 2 || LogicalHeight != size / 2)
				throw new InvalidOperationException("The Version 1 geometry or tileset was changed without a contract revision.");
			if (version1 && (ObstacleDensity != 0 || VegetationDensity != 0))
				throw new InvalidOperationException("Version 1 is Clear-only: obstacle and vegetation density must be zero.");
			if (ClearTemplateIds.Length == 0 || NeutralColonyActors.Length == 0)
				throw new InvalidOperationException("The RMG profile must declare clear templates and neutral colony actors.");
			if ((version2 || version3 || version4 || version5 || version6 || version7 || version8 || version9 || version10) && (BlockedTemplateIds.Length == 0 || VegetationDensity != 0 ||
				MinimumRouteWidthNative != 5 || MajorRouteWidthNative != 9 || ChokepointWidthNative != 3 ||
				(!(version9 || version10) && (OpenObstacleDensityTarget != 12 || OpenObstacleDensityMinimum != 10 || OpenObstacleDensityMaximum != 14 ||
				CentralObstacleDensityTarget != 16 || CentralObstacleDensityMinimum != 14 || CentralObstacleDensityMaximum != 18 ||
				ObstacleRegionMinimumLogical != 8)) || MaximumTopologyAttempts != 4 ||
				MaximumRepairOperations != 8 || MaximumRepairCellsLogical != 64 || ColonyCombatSafetyBufferNative != 1))
				throw new InvalidOperationException("The blocking-topology or combat-space constants do not match the accepted contract.");
			if ((!version1 && !version8 && !version9 && !version10 && ObstacleRegionMaximumLogical != 64) || ((version8 || version9 || version10) && ObstacleRegionMaximumLogical != (largeNatural ? 4096 : 1024)))
				throw new InvalidOperationException("Obstacle-region capacity does not match the selected frozen layout-family contract.");
			if (version2 && (OpenWaterDetailTemplateIds.Length != 0 || ShorelineDecorationPercent != 0 || OpenWaterDetailPercent != 0))
				throw new InvalidOperationException("The frozen Version 2 profile cannot enable Version 3 visual decoration.");
			if ((version3 || version4 || version5 || version6 || version7 || version8 || version9 || version10) && (!OpenWaterDetailTemplateIds.SequenceEqual(NormalWaterTransitionCatalogue.OpenWaterDetailTemplateIds) ||
				ShorelineDecorationPercent != 16 || OpenWaterDetailPercent != 8))
				throw new InvalidOperationException("The Version 3 shoreline policy must use the audited 16% shoreline decoration, " +
					"8% open-Water detail, and fixed NORMAL detail templates 24, 25, and 27.");
			if (!version4 && !version5 && !version6 && !version7 && !version8 && !version9 && !version10 && (ClearLandDetailTemplateIds.Length != 0 || ClearLandDetailPercent != 0))
				throw new InvalidOperationException("Generator Versions 1 through 3 cannot enable Phase 6B Clear land details.");
			if ((version4 || version5 || version6 || version7 || version8 || version9 || version10) && (!ClearLandDetailTemplateIds.SequenceEqual(new ushort[] { 61, 62 }) || ClearLandDetailPercent != 4))
				throw new InvalidOperationException("Generator Versions 4 through 10 must use the audited four-percent Clear detail policy and fixed NORMAL templates 61 and 62.");
			if (!version5 && !version6 && !version7 && !version8 && !version9 && !version10 && (RockLandPercent != 0 || VegetationLandPercent != 0 || RockDetailPercent != 0 ||
				VegetationDetailPercent != 0 || LandCoverTolerancePercent != 0))
				throw new InvalidOperationException("Generator Versions 1 through 4 cannot enable Phase 6C slow land cover.");
			if (version5 && (RockLandPercent != 14 || VegetationLandPercent != 8 || RockDetailPercent != 2 ||
				VegetationDetailPercent != 3 || LandCoverTolerancePercent != 2))
				throw new InvalidOperationException("Generator Version 5 must use the frozen 14% Rock, 8% Vegetation, 2% Rock-detail, 3% Vegetation-detail, and two-point tolerance policy.");
			if ((version6 || version7 || version8 || version9 || version10) && (RockLandPercent != 14 || VegetationLandPercent != 8 || RockDetailPercent != 2 ||
				VegetationDetailPercent != 3 || LandCoverTolerancePercent != 2))
				throw new InvalidOperationException("Generator Versions 6 through 10 must preserve the Version 5 Standard coverage and detail-selection rates.");
			if ((version6 || version7 || version8 || version9 || version10) && (BattlefieldFlankRadiusLogical != 3 || TacticalLandAnchorOrbitCount != 3 ||
				!SoilDecorationActors.SequenceEqual(new[] { "plant_flower", "rmg_plant_broad_leaf_grass" }) ||
				!RockDecorationActors.SequenceEqual(new[] { "rmg_plant_brown_mushroom" }) ||
				!VegetationDecorationActors.SequenceEqual(new[] { "rmg_plant_toad_stool" }) ||
				BlockingDecorationActors.Length != 0 ||
				LandDecorationPerThousand != 3 || MinimumLandDecorationSectors != 12))
				throw new InvalidOperationException("Versions 6 through 10 must use the accepted role and terrain-specific decoration policy.");
			if ((version7 || version8 || version9 || version10) && (LowWaterDensityAdjustment != -4 || HighWaterDensityAdjustment != 4 ||
				LowRockLandPercent != 10 || LowVegetationLandPercent != 5 || LowTacticalLandAnchorOrbitCount != 2 ||
				HighRockLandPercent != 18 || HighVegetationLandPercent != 11 || HighTacticalLandAnchorOrbitCount != 5))
				throw new InvalidOperationException("Version 7 through 10 parameter profiles do not match the accepted Phase 8A Low/Standard/High contract.");
			if (!version7 && !version8 && !version9 && !version10 && (LowWaterDensityAdjustment != 0 || HighWaterDensityAdjustment != 0 || LowRockLandPercent != 0 ||
				LowVegetationLandPercent != 0 || LowTacticalLandAnchorOrbitCount != 0 || HighRockLandPercent != 0 ||
				HighVegetationLandPercent != 0 || HighTacticalLandAnchorOrbitCount != 0))
				throw new InvalidOperationException("Generator Versions 1 through 6 cannot enable Version 7 parameter profiles.");
		}
	}

	public sealed record RmgGraphNode(string Id, string Role, RmgPoint Location);
	public sealed record RmgGraphEdge(string Id, string From, string To, int RouteId);
	public sealed record RmgActorPlan(string Type, string Owner, string Role, RmgPoint LogicalLocation, int EquivalenceGroup, int NativeFrame = 0);
	public sealed record RmgObstacleRegion(int Id, int SymmetryOrbit, int CellCount);
	public sealed record RmgChokepoint(string Id, int RouteId, int SymmetryOrbit, RmgPoint From, RmgPoint To, int LengthNative, int WidthNative);
	public sealed record RmgRepairRecord(int Index, string Type, string Reason, int TargetId, RmgPoint[] ChangedCells);

	public sealed class RmgLogicalMap
	{
		public JObject RegionsReport { get; set; }
		public int Width { get; }
		public int Height { get; }
		public ushort[] TemplateIds { get; }
		public RmgNativeTerrainIntent[] NativeTerrainIntents { get; }
		public RmgShorelineRole[] ShorelineRoles { get; }
		public int ShorelineUnsupportedNeighborhoodCount { get; set; }
		public bool[] RockEnvelopeLattice { get; }
		public bool[] VegetationLattice { get; }
		public int LandCoverLandNativeCount { get; set; }
		public int LandCoverAllowedLatticeCount { get; set; }
		public int LandCoverEnvelopeCapacityNativeCount { get; set; }
		public int LandCoverVegetationCapacityNativeCount { get; set; }
		public int LandCoverRockTargetNativeCount { get; set; }
		public int LandCoverVegetationTargetNativeCount { get; set; }
		public int LandCoverRockNativeCount { get; set; }
		public int LandCoverVegetationNativeCount { get; set; }
		public int LandCoverClearRockTransitionStampCount { get; set; }
		public int LandCoverRockVegetationTransitionStampCount { get; set; }
		public int LandCoverRockInteriorStampCount { get; set; }
		public int LandCoverVegetationInteriorStampCount { get; set; }
		public int LandCoverRockDetailStampCount { get; set; }
		public int LandCoverVegetationDetailStampCount { get; set; }
		public int ClearLandDetailEligibleCount { get; set; }
		public int ClearLandDetailExcludedProtectedCount { get; set; }
		public int ClearLandDetailTargetCount { get; set; }
		public int ClearLandDetailSelectedCount { get; set; }
		public int ClearLandDetailSymmetrySideACount { get; set; }
		public int ClearLandDetailSymmetrySideBCount { get; set; }
		public RmgBattlefieldRole[] BattlefieldRoles { get; }
		public List<RmgPoint> BattlefieldTacticalAnchors { get; } = new();
		public int BattlefieldTacticalAnchorOrbitCount { get; set; }
		public bool ChokepointTargetReduced { get; set; }
		public int LandDecorationRequestedCount { get; set; }
		public int LandDecorationTargetCount { get; set; }
		public int LandDecorationSelectedCount { get; set; }
		public int LandDecorationSectorCount { get; set; }
		public int LandDecorationClearTargetCount { get; set; }
		public int LandDecorationRockTargetCount { get; set; }
		public int LandDecorationVegetationTargetCount { get; set; }
		public int[] RegionIds { get; }
		public int[] RouteIds { get; }
		public bool[] StartReservations { get; }
		public bool[] StructureReservations { get; }
		public bool[] Obstacles { get; }
		public ulong[] RouteMasks { get; }
		public int[] ObstacleRegionIds { get; }
		public int[] ChokepointIds { get; }
		public bool[] StrategicRegions { get; }
		public bool[] RepairChanges { get; }
		public int[] NaturalRockPriorities { get; }
		public int[] NaturalVegetationPriorities { get; }
		public string NaturalTerrainVariantId { get; set; }
		public bool NaturalOriginalSurfaceRelations { get; set; }
		public bool NaturalSurfacesFrozen { get; set; }
		public int NaturalRelocatedStartCount { get; set; }
		public int NaturalReplacedColonyCount { get; set; }
		public int NaturalForbiddenSurfaceAdjacencyCount { get; set; }
		public int NaturalPrototypeWaterCount { get; set; }
		public int NaturalPrototypeInteriorWaterCount { get; set; }
		public int NaturalProjectedWaterCount { get; set; }
		public int NaturalProjectedInteriorWaterCount { get; set; }
		public int NaturalPreRouteWaterCount { get; set; }
		public int NaturalPreRouteInteriorWaterCount { get; set; }
		public int NaturalColonyClearedWaterCount { get; set; }
		public int NaturalRouteClearedWaterCount { get; set; }
		public List<RmgPoint> Starts { get; } = new();
		public List<RmgGraphNode> GraphNodes { get; } = new();
		public List<RmgGraphEdge> GraphEdges { get; } = new();
		public List<RmgActorPlan> Actors { get; } = new();
		public List<RmgObstacleRegion> ObstacleRegions { get; } = new();
		public List<RmgChokepoint> Chokepoints { get; } = new();
		public List<RmgRepairRecord> Repairs { get; } = new();
		public int RepairCount { get; set; }
		public int RetryCount { get; set; }
		public int ColonySearchNodes { get; set; }

		public RmgLogicalMap(int width, int height)
		{
			Width = width;
			Height = height;
			TemplateIds = new ushort[width * height];
			NativeTerrainIntents = new RmgNativeTerrainIntent[width * height * 4];
			ShorelineRoles = new RmgShorelineRole[width * height];
			RockEnvelopeLattice = new bool[(width + 1) * (height + 1)];
			VegetationLattice = new bool[(width + 1) * (height + 1)];
			RegionIds = Enumerable.Repeat(-1, width * height).ToArray();
			RouteIds = Enumerable.Repeat(-1, width * height).ToArray();
			StartReservations = new bool[width * height];
			StructureReservations = new bool[width * height];
			Obstacles = new bool[width * height];
			RouteMasks = new ulong[width * height];
			ObstacleRegionIds = Enumerable.Repeat(-1, width * height).ToArray();
			ChokepointIds = Enumerable.Repeat(-1, width * height).ToArray();
			StrategicRegions = new bool[width * height];
			RepairChanges = new bool[width * height];
			BattlefieldRoles = new RmgBattlefieldRole[width * height];
			NaturalRockPriorities = new int[(width + 1) * (height + 1)];
			NaturalVegetationPriorities = new int[(width + 1) * (height + 1)];
		}

		public int Index(RmgPoint p) => p.Y * Width + p.X;
		public bool Contains(RmgPoint p) => p.X >= 0 && p.X < Width && p.Y >= 0 && p.Y < Height;
	}

	public sealed record RmgValidationIssue(string Code, string Message)
	{
		public JObject ToJson() => new() { ["code"] = Code, ["message"] = Message };
	}

	public sealed class RmgValidationReport
	{
		public List<RmgValidationIssue> HardFailures { get; } = new();
		public List<RmgValidationIssue> Warnings { get; } = new();
		public Dictionary<string, double> Metrics { get; } = new(StringComparer.Ordinal);
		public bool Accepted => HardFailures.Count == 0;

		public JObject ToJson()
		{
			return new JObject
			{
				["accepted"] = Accepted,
				["hard_failures"] = new JArray(HardFailures.Select(i => i.ToJson())),
				["warnings"] = new JArray(Warnings.Select(i => i.ToJson())),
				["metrics"] = new JObject(Metrics.Select(kv => new JProperty(kv.Key, kv.Value)))
			};
		}
	}

	public sealed class RmgGenerationResult
	{
		public RmgGenerationSettings Settings { get; init; }
		public RmgProfile Profile { get; init; }
		public RmgLogicalMap Map { get; init; }
		public RmgValidationReport Validation { get; init; }
		public string LogicalHash { get; init; }
		public string ActorHash { get; init; }
		public string GraphHash { get; init; }
	}

	public sealed class RmgGenerationRejectedException : InvalidOperationException
	{
		public string RejectionCode { get; }

		public RmgGenerationRejectedException(string rejectionCode, string message, Exception innerException = null)
			: base(message, innerException)
		{
			RejectionCode = rejectionCode;
		}
	}
}
