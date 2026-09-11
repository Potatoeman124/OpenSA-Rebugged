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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public enum RmgPlayerPreset
	{
		Balanced,
		OpenConflict,
		TacticalCrossroads
	}

	public enum RmgPlayerSymmetry
	{
		Automatic,
		Horizontal,
		Vertical,
		Rotational
	}

	public enum RmgPlayerLayout
	{
		Preset,
		OpenFields,
		ContestedCenter
	}

	public enum RmgPlayerLayoutFamily
	{
		Preset,
		NaturalLandscape,
		StructuredCompetitive,
		ArtificialBattlefield,
		NaturalLandscapePvp,
		Crossroads,
		Ring,
		DividedLands,
		Strongholds,
		Labyrinth,
		Archipelago
	}

	public enum RmgPlayerColonyDensity
	{
		Preset,
		Sparse,
		Standard,
		Dense,
		Extreme,
		Ultra
	}

	public enum RmgPlayerParameterLevel
	{
		Preset,
		Low,
		Standard,
		High,
		Extreme,
		Ultra
	}

	public sealed class RmgPlayerSettings
	{
		// Keep the 128x128 default and its seed mapping frozen at schema 3.
		public int SchemaVersion { get; init; } = 3;
		public int MirroringAxes { get; init; }
		public RmgBattlefieldBlockShape BlockShape { get; init; } = RmgBattlefieldBlockShape.CutCorners;
		public RmgBattlefieldLaneWidth LaneWidth { get; init; } = RmgBattlefieldLaneWidth.Standard;
		public bool GenerateCastles { get; init; } = true;
		public bool RespectStartingSafeArea { get; init; } = true;
		public bool OwnStartingStronghold { get; init; }
		public RmgLabyrinthRoutes ExtraRoutes { get; init; } = RmgLabyrinthRoutes.Standard;
		public RmgIslandAmount IslandAmount { get; init; } = RmgIslandAmount.Standard;
		public RmgIslandSize IslandSize { get; init; } = RmgIslandSize.Standard;
		public bool IsArchipelago => SchemaVersion >= 19 && LayoutFamily == RmgPlayerLayoutFamily.Archipelago;
		public bool IsLabyrinth => SchemaVersion >= 18 && LayoutFamily == RmgPlayerLayoutFamily.Labyrinth;
		public bool IsStrongholds => SchemaVersion >= 16 && LayoutFamily == RmgPlayerLayoutFamily.Strongholds;
		public bool IsDividedLands => SchemaVersion >= 15 && LayoutFamily == RmgPlayerLayoutFamily.DividedLands;
		public RmgLandCrossings LandCrossings { get; init; } = RmgLandCrossings.One;
		public bool IsRing => SchemaVersion >= 14 && LayoutFamily == RmgPlayerLayoutFamily.Ring;
		public RmgRingShape RingShape { get; init; } = RmgRingShape.Round;
		public bool IsCrossroads => SchemaVersion >= 13 && LayoutFamily == RmgPlayerLayoutFamily.Crossroads;
		public RmgCrossroadsConnections SideConnections { get; init; } = RmgCrossroadsConnections.Standard;
		public bool IsPlannedBattlefield => SchemaVersion >= 12 && LayoutFamily == RmgPlayerLayoutFamily.ArtificialBattlefield;
		public bool UsesModernTerrain => LayoutFamily is RmgPlayerLayoutFamily.NaturalLandscape or RmgPlayerLayoutFamily.NaturalLandscapePvp || IsPlannedBattlefield || IsCrossroads || IsRing || IsDividedLands || IsStrongholds || IsLabyrinth || IsArchipelago;
		public int MapSize { get; init; } = 128;
		public RmgPlayerPreset Preset { get; init; } = RmgPlayerPreset.Balanced;
		public string Tileset { get; init; } = "NORMAL";
		public ulong Seed { get; init; }
		public int PlayerCount { get; init; } = 2;
		public RmgPlayerSymmetry Symmetry { get; init; } = RmgPlayerSymmetry.Automatic;
		public RmgPlayerLayout Layout { get; init; } = RmgPlayerLayout.Preset;
		public RmgPlayerLayoutFamily LayoutFamily { get; init; } = RmgPlayerLayoutFamily.Preset;
		public RmgPlayerColonyDensity NeutralColonyDensity { get; init; } = RmgPlayerColonyDensity.Preset;
		public RmgPlayerParameterLevel WaterAmount { get; init; } = RmgPlayerParameterLevel.Preset;
		public Reassessment.TerrainComplexity TerrainComplexity { get; init; } = Reassessment.TerrainComplexity.Standard;
		public RmgPlayerParameterLevel TacticalTerrain { get; init; } = RmgPlayerParameterLevel.Preset;
		public bool OriginalSurfaceRelations { get; init; } = true;
		public bool PreventColonyOverlapping { get; init; } = true;
		public RmgColonyWeights NeutralColonyWeights { get; init; } = new();
		public int[] StartingColonyShares { get; init; } = Array.Empty<int>();
		public RmgColonyOwnershipMode StartingColonyMode { get; init; } = RmgColonyOwnershipMode.ClosestToSpawn;

		public JObject ToJson()
		{
			var json = new JObject
			{
				["schema_version"] = SchemaVersion,
				["preset"] = RmgPlayerSettingsContract.PresetName(Preset),
				["seed"] = Seed.ToString(CultureInfo.InvariantCulture),
				["players"] = PlayerCount,
				["symmetry"] = RmgPlayerSettingsContract.SymmetryName(Symmetry),
				["layout"] = RmgPlayerSettingsContract.LayoutName(Layout),
				["neutral_colony_density"] = RmgPlayerSettingsContract.ColonyDensityName(NeutralColonyDensity)
			};
			if (SchemaVersion >= 2)
			{
				json["water_amount"] = RmgPlayerSettingsContract.PlayerParameterLevelName(WaterAmount);
				json[SchemaVersion >= 5 ? "gravel_moss_amount" : "tactical_terrain"] = RmgPlayerSettingsContract.PlayerParameterLevelName(TacticalTerrain);
			}

			if (SchemaVersion >= 3)
			{
				json["layout_family"] = RmgPlayerSettingsContract.PlayerLayoutFamilyName(LayoutFamily);
				if (UsesModernTerrain)
					json["original_surface_relations"] = OriginalSurfaceRelations;
			}

			if (SchemaVersion >= 5 && UsesModernTerrain)
				json["terrain_complexity"] = RmgPlayerSettingsContract.ComplexityName(TerrainComplexity, SchemaVersion >= 7);

			if (SchemaVersion >= 8 && UsesModernTerrain)
				json["prevent_colony_overlapping"] = PreventColonyOverlapping;

			if (SchemaVersion >= 9 && UsesModernTerrain)
				json["neutral_colony_weights"] = NeutralColonyWeights.ToJson();

			if (SchemaVersion >= 10 && UsesModernTerrain)
				json["starting_colony_shares"] = new JArray(StartingColonyShares.Length == 0 ? new int[PlayerCount] : StartingColonyShares);

			if (SchemaVersion >= 10 && Tileset != "NORMAL") json["tileset"] = Tileset;

			// Omit the default so existing V16 settings and generated package identities stay identical.
			if (SchemaVersion >= 10 && UsesModernTerrain && StartingColonyMode != RmgColonyOwnershipMode.ClosestToSpawn)
				json["starting_colony_mode"] = RmgColonyOwnership.ModeName(StartingColonyMode);

			if (LayoutFamily == RmgPlayerLayoutFamily.NaturalLandscapePvp) json["mirroring_axes"] = MirroringAxes;

			if (IsPlannedBattlefield) { json["block_shape"] = RmgBattlefieldParameters.Name(BlockShape); json["lane_width"] = RmgBattlefieldParameters.Name(LaneWidth); }

			if (SchemaVersion >= 17 && !RespectStartingSafeArea) json["respect_starting_safe_area"] = false;
			if (SchemaVersion >= 17 && OwnStartingStronghold) json["own_starting_stronghold"] = true;

			if (IsArchipelago) { json["island_amount"] = RmgArchipelagoParameters.Name(IslandAmount); json["island_size"] = RmgArchipelagoParameters.Name(IslandSize); }
			if (IsLabyrinth) { json["passage_width"] = RmgBattlefieldParameters.Name(LaneWidth); json["extra_routes"] = RmgLabyrinthParameters.Name(ExtraRoutes); }
			if (IsStrongholds) json["generate_castles"] = GenerateCastles;

			if (IsDividedLands) { json["land_crossings"] = RmgDividedLandsParameters.Name(LandCrossings); json["crossing_width"] = RmgBattlefieldParameters.Name(LaneWidth); }

			if (IsRing) { json["ring_shape"] = RmgRingParameters.Name(RingShape); json["ring_width"] = RmgBattlefieldParameters.Name(LaneWidth); }

			if (IsCrossroads) { json["approach_width"] = RmgBattlefieldParameters.Name(LaneWidth); json["side_connections"] = RmgCrossroadsParameters.Name(SideConnections); }

			if (SchemaVersion >= 4)
				json["size"] = $"{MapSize},{MapSize}";

			return json;
		}
	}

	public sealed class RmgPlayerSettingsResolution
	{
		public RmgPlayerSettings Requested { get; }
		public RmgGenerationSettings Normalized { get; }
		public IReadOnlyList<string> Overrides { get; }

		public RmgPlayerSettingsResolution(RmgPlayerSettings requested, RmgGenerationSettings normalized,
			IReadOnlyList<string> overrides)
		{
			Requested = requested;
			Normalized = normalized;
			Overrides = overrides;
		}

		public JObject ToJson()
		{
			var json = new JObject
			{
			["schema_version"] = Requested.SchemaVersion,
			["preset"] = RmgPlayerSettingsContract.PresetName(Requested.Preset),
			["requested"] = Requested.ToJson(),
			["normalized"] = new JObject
			{
				["generator_version"] = Normalized.GeneratorVersion,
				["topology"] = OpenRaRmgMapAdapter.TopologyName(Normalized.TopologyPreset),
				["layout_family"] = RmgPlayerSettingsContract.LayoutFamilyName(Normalized.LayoutFamily),
				["seed"] = Normalized.Seed.ToString(CultureInfo.InvariantCulture),
				["players"] = Normalized.PlayerCount,
				["symmetry"] = OpenRaRmgMapAdapter.SymmetryName(Normalized.Symmetry),
				["archetype"] = OpenRaRmgMapAdapter.ArchetypeName(Normalized.Archetype),
				["neutral_colonies"] = Normalized.NeutralColonyCount,
				["water_amount"] = RmgPlayerSettingsContract.ParameterLevelName(Normalized.WaterAmount),
				["tactical_terrain"] = RmgPlayerSettingsContract.ParameterLevelName(Normalized.TacticalTerrain),
				["original_surface_relations"] = Normalized.OriginalSurfaceRelations,
				["tileset"] = Normalized.Tileset,
				["size"] = $"{Normalized.MapSize},{Normalized.MapSize}"
			},
			["overrides"] = new JArray(Overrides),
			["warnings"] = new JArray()
			};
			if (Normalized.GeneratorVersion is 11 or 12 or 13 or 14 or 15 or 16 or 17 or 18 or 19 or 20 or 21 or 22 or 23 or 24)
			{
				var normalized = (JObject)json["normalized"];
				normalized.Remove("tactical_terrain");
				normalized.Remove("symmetry");
				normalized.Remove("archetype");
				normalized["gravel_moss_amount"] = RmgPlayerSettingsContract.ParameterLevelName(Normalized.TacticalTerrain);
				normalized["terrain_complexity"] = RmgPlayerSettingsContract.ComplexityName(Normalized.TerrainComplexity, Normalized.GeneratorVersion is 13 or 14 or 15 or 16 or 17 or 18 or 19 or 20 or 21 or 22 or 23 or 24);
			}
			if (Normalized.GeneratorVersion is 14 or 15 or 16 or 17 or 18 or 19 or 20 or 21 or 22 or 23 or 24)
				json["normalized"]["prevent_colony_overlapping"] = Normalized.PreventColonyOverlapping;
			if (Normalized.GeneratorVersion is 15 or 16 or 17 or 18 or 19 or 20 or 21 or 22 or 23 or 24)
				json["normalized"]["neutral_colony_weights"] = Normalized.NeutralColonyWeights.ToJson();
			if (Normalized.GeneratorVersion is 16 or 17 or 18 or 19 or 20 or 21 or 22 or 23 or 24)
				json["normalized"]["starting_colony_shares"] = new JArray(Normalized.StartingColonyShares);
			if (Normalized.GeneratorVersion is 16 or 17 or 18 or 19 or 20 or 21 or 22 or 23 or 24 && Normalized.StartingColonyMode != RmgColonyOwnershipMode.ClosestToSpawn)
				json["normalized"]["starting_colony_mode"] = RmgColonyOwnership.ModeName(Normalized.StartingColonyMode);
			if (Normalized.GeneratorVersion is 17 or 18 or 19 or 20 or 21) json["normalized"]["mirroring_axes"] = Normalized.MirroringAxes;
			if (!Normalized.RespectStartingSafeArea) json["normalized"]["respect_starting_safe_area"] = false;
			if (Normalized.OwnStartingStronghold) json["normalized"]["own_starting_stronghold"] = true;
			if (Normalized.GeneratorVersion == 24) { json["normalized"]["island_amount"] = RmgArchipelagoParameters.Name(Normalized.IslandAmount); json["normalized"]["island_size"] = RmgArchipelagoParameters.Name(Normalized.IslandSize); }
			if (Normalized.GeneratorVersion == 23) { json["normalized"]["passage_width"] = RmgBattlefieldParameters.Name(Normalized.LaneWidth); json["normalized"]["extra_routes"] = RmgLabyrinthParameters.Name(Normalized.ExtraRoutes); }
			if (Normalized.GeneratorVersion == 22) json["normalized"]["generate_castles"] = Normalized.GenerateCastles;
			if (Normalized.GeneratorVersion == 21) { json["normalized"]["land_crossings"] = RmgDividedLandsParameters.Name(Normalized.LandCrossings); json["normalized"]["crossing_width"] = RmgBattlefieldParameters.Name(Normalized.LaneWidth); }
			if (Normalized.GeneratorVersion == 20) { json["normalized"]["ring_shape"] = RmgRingParameters.Name(Normalized.RingShape); json["normalized"]["ring_width"] = RmgBattlefieldParameters.Name(Normalized.LaneWidth); }
			if (Normalized.GeneratorVersion == 19) { json["normalized"]["approach_width"] = RmgBattlefieldParameters.Name(Normalized.LaneWidth); json["normalized"]["side_connections"] = RmgCrossroadsParameters.Name(Normalized.SideConnections); }
			if (Normalized.GeneratorVersion == 18) { json["normalized"]["block_shape"] = RmgBattlefieldParameters.Name(Normalized.BlockShape); json["normalized"]["lane_width"] = RmgBattlefieldParameters.Name(Normalized.LaneWidth); }
			return json;
		}
	}

	public static class RmgPlayerSettingsContract
	{
		public const int SchemaVersion = 19;
		public const int MinimumSchemaVersion = 1;

		static readonly HashSet<string> AllowedFields = new(new[]
		{
			"island_amount", "island_size",
			"schema_version",
			"tileset",
			"size",
			"preset",
			"seed",
			"players",
			"symmetry",
			"layout",
			"layout_family",
			"mirroring_axes",
			"block_shape",
			"approach_width",
			"side_connections",
			"passage_width",
			"extra_routes",
			"generate_castles",
			"respect_starting_safe_area",
			"own_starting_stronghold",
			"land_crossings",
			"crossing_width",
			"ring_shape",
			"ring_width",
			"lane_width",
			"original_surface_relations",
			"prevent_colony_overlapping",
			"neutral_colony_weights",
			"starting_colony_shares",
			"starting_colony_mode",
			"neutral_colony_density",
			"water_amount",
			"gravel_moss_amount",
			"terrain_complexity",
			"tactical_terrain"
		}, StringComparer.Ordinal);

		static readonly HashSet<string> Schema2Fields = new(new[]
		{
			"schema_version",
			"preset",
			"seed",
			"players",
			"symmetry",
			"layout",
			"neutral_colony_density",
			"water_amount",
			"tactical_terrain"
		}, StringComparer.Ordinal);

		static readonly HashSet<string> Schema1Fields = new(new[]
		{
			"schema_version",
			"preset",
			"seed",
			"players",
			"symmetry",
			"layout",
			"neutral_colony_density"
		}, StringComparer.Ordinal);

		public static RmgPlayerSettingsResolution Load(string path)
		{
			try
			{
				return Resolve(Parse(JObject.Parse(File.ReadAllText(path))));
			}
			catch (JsonException e)
			{
				throw new ArgumentException($"Player settings file is not valid JSON: {e.Message}", e);
			}
		}

		public static RmgPlayerSettings Parse(JObject json)
		{
			var unknown = json.Properties().Select(property => property.Name)
				.Where(name => !AllowedFields.Contains(name)).OrderBy(name => name).ToArray();
			if (unknown.Length > 0)
				throw new ArgumentException($"Unknown player settings field(s): {string.Join(", ", unknown)}.");

			var schemaVersion = RequiredInt(json, "schema_version");
			var archipelago = schemaVersion >= 19 && OptionalText(json, "layout_family", "preset") == "archipelago";
			if (!archipelago && (json.ContainsKey("island_amount") || json.ContainsKey("island_size"))) throw new ArgumentException("Island Amount and Island Size require schema 19 and Archipelago.");
			var labyrinth = schemaVersion >= 18 && OptionalText(json, "layout_family", "preset") == "labyrinth";
			if (!labyrinth && (json.ContainsKey("passage_width") || json.ContainsKey("extra_routes"))) throw new ArgumentException("Passage Width and Extra Routes require schema 18 and Labyrinth.");
			var strongholds = schemaVersion >= 16 && OptionalText(json, "layout_family", "preset") == "strongholds";
			if (!strongholds && json.ContainsKey("generate_castles")) throw new ArgumentException("Generate Castles requires schema 16 and Strongholds.");
			var divided = schemaVersion >= 15 && OptionalText(json, "layout_family", "preset") == "divided-lands";
			if (!divided && (json.ContainsKey("land_crossings") || json.ContainsKey("crossing_width")))
				throw new ArgumentException("Land Crossings and Crossing Width require schema 15 and Divided Lands.");
			var ring = schemaVersion >= 14 && OptionalText(json, "layout_family", "preset") == "ring";
			if (!ring && (json.ContainsKey("ring_shape") || json.ContainsKey("ring_width")))
				throw new ArgumentException("Ring Shape and Ring Width require schema 14 and Ring.");
			var artificial = schemaVersion >= 12 && OptionalText(json, "layout_family", "preset") == "artificial-battlefield";
			var crossroads = schemaVersion >= 13 && OptionalText(json, "layout_family", "preset") == "crossroads";
			if (!crossroads && (json.ContainsKey("approach_width") || json.ContainsKey("side_connections")))
				throw new ArgumentException("Approach Width and Side Connections require schema 13 and Crossroads.");
			var modern = archipelago || labyrinth || strongholds || divided || ring || artificial || crossroads || OptionalText(json, "layout_family", "preset") is "natural-landscape" or "natural-landscape-pvp";
			if (json.ContainsKey("respect_starting_safe_area") && (schemaVersion < 17 || !modern))
				throw new ArgumentException("respect_starting_safe_area requires schema 17 and a current layout.");
			if (json.ContainsKey("own_starting_stronghold") && (schemaVersion < 17 || !strongholds))
				throw new ArgumentException("own_starting_stronghold requires schema 17 and Strongholds.");
			if (!artificial && (json.ContainsKey("block_shape") || json.ContainsKey("lane_width")))
				throw new ArgumentException("Block Shape and Lane Width require schema 12 and Artificial Battlefield.");
			if (json.ContainsKey("mirroring_axes") && (schemaVersion < 11 || OptionalText(json, "layout_family", "preset") != "natural-landscape-pvp"))
				throw new ArgumentException("mirroring_axes requires schema 11 and Natural Landscape PVP.");
			if (schemaVersion < MinimumSchemaVersion || schemaVersion > SchemaVersion)
				throw new ArgumentException($"Player settings schema_version must be from {MinimumSchemaVersion} through {SchemaVersion}.");
			if (json.ContainsKey("tileset") && (schemaVersion < 10 || !modern))
				throw new ArgumentException("tileset requires schema 10 and Natural Landscape.");
			if (json.ContainsKey("starting_colony_mode") && (schemaVersion < 10 ||
				!modern))
				throw new ArgumentException("starting_colony_mode requires schema 10 and Natural Landscape.");
			if (json.ContainsKey("starting_colony_shares") && (schemaVersion < 10 ||
				!modern))
				throw new ArgumentException("starting_colony_shares requires schema 10 and Natural Landscape.");
			if (json.ContainsKey("neutral_colony_weights") && (schemaVersion < 9 ||
				!modern))
				throw new ArgumentException("neutral_colony_weights requires schema 9 and Natural Landscape.");
			if (json.ContainsKey("prevent_colony_overlapping") && (schemaVersion < 8 ||
				!modern))
				throw new ArgumentException("prevent_colony_overlapping requires schema 8 and Natural Landscape.");
			if (schemaVersion < 5 && (json.ContainsKey("gravel_moss_amount") || json.ContainsKey("terrain_complexity")))
				throw new ArgumentException("Gravel/moss amount and terrain complexity require schema 5.");
			if (schemaVersion >= 5 && json.ContainsKey("tactical_terrain"))
				throw new ArgumentException("Schema 5 uses gravel_moss_amount instead of tactical_terrain.");
			if (schemaVersion >= 5 && json.ContainsKey("terrain_complexity") &&
				!modern)
				throw new ArgumentException("Terrain Complexity requires Natural Landscape.");
			if (schemaVersion == 2)
			{
				var schema2Unknown = json.Properties().Select(property => property.Name)
					.Where(name => !Schema2Fields.Contains(name)).OrderBy(name => name).ToArray();
				if (schema2Unknown.Length > 0)
					throw new ArgumentException($"Player settings schema_version 2 does not support field(s): {string.Join(", ", schema2Unknown)}.");
			}

			if (schemaVersion == 1)
			{
				var schema1Unknown = json.Properties().Select(property => property.Name)
					.Where(name => !Schema1Fields.Contains(name)).OrderBy(name => name).ToArray();
				if (schema1Unknown.Length > 0)
					throw new ArgumentException($"Player settings schema_version 1 does not support field(s): {string.Join(", ", schema1Unknown)}.");
			}

			if (schemaVersion < 4 && json["size"] != null)
				throw new ArgumentException("Explicit map size requires player settings schema 4.");

			var seedText = RequiredText(json, "seed");
			if (!ulong.TryParse(seedText, NumberStyles.None, CultureInfo.InvariantCulture, out var seed))
				throw new ArgumentException("Player settings seed must be an unsigned integer encoded as a JSON string or integer.");

			if (schemaVersion < 8 || !modern)
				foreach (var field in new[] { "water_amount", "gravel_moss_amount", "tactical_terrain", "neutral_colony_density" })
					if (OptionalText(json, field, "preset") is "extreme" or "ultra")
						throw new ArgumentException("Extreme/Ultra quantities require schema 8 and Natural Landscape.");

			var layoutFamily = schemaVersion >= 3 ?
				ParsePlayerLayoutFamily(OptionalText(json, "layout_family", "preset")) :
				RmgPlayerLayoutFamily.Preset;
			if (json["original_surface_relations"] != null && !modern)
				throw new ArgumentException("Player setting 'original_surface_relations' applies only to Natural Landscape.");

			return new RmgPlayerSettings
			{
				SchemaVersion = schemaVersion,
				IslandAmount = RmgArchipelagoParameters.ParseAmount(OptionalText(json, "island_amount", "standard")),
				IslandSize = RmgArchipelagoParameters.ParseSize(OptionalText(json, "island_size", "standard")),
				GenerateCastles = OptionalBool(json, "generate_castles", true),
				RespectStartingSafeArea = OptionalBool(json, "respect_starting_safe_area", true),
				OwnStartingStronghold = OptionalBool(json, "own_starting_stronghold", false),
				BlockShape = RmgBattlefieldParameters.ParseShape(OptionalText(json, "block_shape", "cut-corners")),
				ExtraRoutes = RmgLabyrinthParameters.Parse(OptionalText(json, "extra_routes", "standard")),
				LaneWidth = RmgBattlefieldParameters.ParseLane(OptionalText(json, labyrinth ? "passage_width" : divided ? "crossing_width" : ring ? "ring_width" : crossroads ? "approach_width" : "lane_width", "standard")),
				LandCrossings = RmgDividedLandsParameters.Parse(OptionalText(json, "land_crossings", "one")),
				RingShape = RmgRingParameters.Parse(OptionalText(json, "ring_shape", "round")),
				SideConnections = RmgCrossroadsParameters.Parse(OptionalText(json, "side_connections", "standard")),
				MirroringAxes = json.ContainsKey("mirroring_axes") ? RequiredInt(json, "mirroring_axes") : layoutFamily == RmgPlayerLayoutFamily.NaturalLandscapePvp ? 1 : 0,
				Tileset = RmgBiome.Parse(OptionalText(json, "tileset", "NORMAL")),
				MapSize = schemaVersion >= 4 ? ParseMapSize(OptionalText(json, "size", "128,128"), schemaVersion >= 10) : 128,
				Preset = ParsePreset(RequiredText(json, "preset")),
				Seed = seed,
				PlayerCount = RequiredInt(json, "players"),
				Symmetry = ParseSymmetry(OptionalText(json, "symmetry", "automatic")),
				Layout = ParseLayout(OptionalText(json, "layout", "preset")),
				LayoutFamily = layoutFamily,
				NeutralColonyDensity = ParseColonyDensity(OptionalText(json, "neutral_colony_density", "preset")),
				WaterAmount = schemaVersion >= 2 ?
					ParsePlayerParameterLevel(OptionalText(json, "water_amount", "preset"), "water_amount") :
					RmgPlayerParameterLevel.Preset,
				TacticalTerrain = schemaVersion >= 2 ?
					ParsePlayerParameterLevel(OptionalText(json, schemaVersion >= 5 ? "gravel_moss_amount" : "tactical_terrain", "preset"), "gravel_moss_amount") :
					RmgPlayerParameterLevel.Preset,
				TerrainComplexity = ParseComplexity(OptionalText(json, "terrain_complexity", schemaVersion >= 7 ? "medium" : "standard"), schemaVersion),
				OriginalSurfaceRelations = OptionalBool(json, "original_surface_relations", true),
				PreventColonyOverlapping = OptionalBool(json, "prevent_colony_overlapping", true),
				NeutralColonyWeights = json.ContainsKey("neutral_colony_weights") ? RmgColonyWeights.Parse(json["neutral_colony_weights"], schemaVersion >= 10 ? 100 : 1000) : new(),
				StartingColonyMode = RmgColonyOwnership.ParseMode(OptionalText(json, "starting_colony_mode", "closest-to-spawn")),
				StartingColonyShares = json.ContainsKey("starting_colony_shares") ? RmgColonyOwnership.ParseShares(json["starting_colony_shares"], RequiredInt(json, "players")) : Array.Empty<int>()
			};
		}

		public static RmgPlayerSettingsResolution Resolve(RmgPlayerSettings requested)
		{
			if (requested.SchemaVersion < MinimumSchemaVersion || requested.SchemaVersion > SchemaVersion)
				throw new ArgumentException($"Player settings schema_version must be from {MinimumSchemaVersion} through {SchemaVersion}.");
			if (!RmgBiome.IsSupported(requested.Tileset) || (requested.Tileset != "NORMAL" &&
				(requested.SchemaVersion < 10 || !requested.UsesModernTerrain)))
				throw new ArgumentException("Additional tilesets require schema 10 and Natural Landscape.");
			if (requested.IsPlannedBattlefield && requested.PlayerCount is not (2 or 4 or 8)) throw new ArgumentException("Artificial Battlefield supports 2, 4 or 8 players.");
			RmgBattlefieldParameters.ValidateOptions(requested);
			RmgCrossroadsParameters.ValidateOptions(requested);
			RmgRingParameters.ValidateOptions(requested);
			RmgDividedLandsParameters.ValidateOptions(requested);
			RmgLabyrinthParameters.ValidateOptions(requested);
			RmgArchipelagoParameters.ValidateOptions(requested);
			if (requested.LayoutFamily == RmgPlayerLayoutFamily.Strongholds && !requested.IsStrongholds) throw new ArgumentException("Strongholds requires schema 16.");
			if (!requested.IsStrongholds && !requested.GenerateCastles) throw new ArgumentException("Generate Castles applies only to Strongholds.");
			if (!requested.RespectStartingSafeArea && (requested.SchemaVersion < 17 || !requested.UsesModernTerrain))
				throw new ArgumentException("Starting safe area options require schema 17 and a current layout.");
			if (requested.OwnStartingStronghold && (requested.SchemaVersion < 17 || !requested.IsStrongholds))
				throw new ArgumentException("Starting stronghold ownership requires schema 17 and Strongholds.");
			var pvp = requested.LayoutFamily == RmgPlayerLayoutFamily.NaturalLandscapePvp;
			if (pvp && requested.SchemaVersion < 11) throw new ArgumentException("Natural Landscape PVP requires schema 11.");
			if (!pvp && requested.MirroringAxes != 0) throw new ArgumentException("Mirroring axes apply only to Natural Landscape PVP.");
			if (pvp) RmgMirroring.Validate(requested.MirroringAxes, requested.MapSize, requested.PlayerCount);
			var expandedPlayers = requested.SchemaVersion >= 9 && requested.UsesModernTerrain;
			if (expandedPlayers ? requested.PlayerCount < 1 || requested.PlayerCount > 8 : requested.PlayerCount != 2 && requested.PlayerCount != 4)
				throw new ArgumentException(expandedPlayers ? "Player settings players must be from 1 through 8." : "Player settings players must be 2 or 4.");
			if (requested.NeutralColonyWeights == null)
				throw new ArgumentException("Neutral colony weights must be an object.");
			requested.NeutralColonyWeights.Validate(requested.SchemaVersion >= 10 ? 100 : 1000);
			RmgColonyOwnership.ValidateShares(requested.StartingColonyShares, requested.PlayerCount);
			if (!Enum.IsDefined(requested.StartingColonyMode) || (requested.StartingColonyMode != RmgColonyOwnershipMode.ClosestToSpawn && (requested.SchemaVersion < 10 || !expandedPlayers)))
				throw new ArgumentException("Starting colony mode requires schema 10, Natural Landscape and a valid choice.");
			if (requested.StartingColonyShares.Length != 0 && (requested.SchemaVersion < 10 || !expandedPlayers))
				throw new ArgumentException("Starting colony shares require schema 10 and Natural Landscape.");
			if (!expandedPlayers && requested.NeutralColonyWeights != new RmgColonyWeights())
				throw new ArgumentException("Neutral colony weights require schema 9 and Natural Landscape.");

			if ((requested.MapSize != 128 && requested.MapSize != 256 && !(requested.MapSize is 64 or 512 && requested.SchemaVersion >= 10)) ||
				(requested.MapSize != 128 && (requested.SchemaVersion < 4 || !requested.UsesModernTerrain)))
				throw new ArgumentException("64x64 and 512x512 require schema 10 and Natural Landscape; 256x256 requires schema 4 and Natural Landscape. Historical layouts support 128x128.");

			if (requested.MapSize == 64 && requested.PlayerCount > 4)
				throw new ArgumentException("64x64 supports 1 through 4 players.");

			var archetype = requested.Layout switch
			{
				RmgPlayerLayout.OpenFields => RmgArchetype.Open,
				RmgPlayerLayout.ContestedCenter => RmgArchetype.CentralContest,
				_ => PresetArchetype(requested.Preset)
			};
			var colonies = requested.NeutralColonyDensity == RmgPlayerColonyDensity.Preset ?
				PresetColonies(requested.Preset, requested.PlayerCount) :
				DensityColonies(requested.NeutralColonyDensity, requested.PlayerCount);
			if (expandedPlayers)
			{
				// Interpolate/extrapolate the accepted two- and four-player targets.
				var two = requested.NeutralColonyDensity == RmgPlayerColonyDensity.Preset ? PresetColonies(requested.Preset, 2) : DensityColonies(requested.NeutralColonyDensity, 2);
				var four = requested.NeutralColonyDensity == RmgPlayerColonyDensity.Preset ? PresetColonies(requested.Preset, 4) : DensityColonies(requested.NeutralColonyDensity, 4);
				colonies = two + (requested.PlayerCount - 2) * (four - two) / 2;
			}
			// Larger geography supports more objectives without multiplying native combat clearances.
			if (requested.MapSize == 512)
				colonies *= 9;
			else if (requested.MapSize == 256)
				colonies *= 3;
			else if (requested.MapSize == 64)
				colonies = (colonies + 3) / 4;
			if (requested.IsLabyrinth) colonies = (colonies + 3) / 4;
			var symmetry = requested.Symmetry switch
			{
				RmgPlayerSymmetry.Horizontal => RmgSymmetry.MirrorHorizontal,
				RmgPlayerSymmetry.Vertical => RmgSymmetry.MirrorVertical,
				RmgPlayerSymmetry.Rotational => RmgSymmetry.Rotate180,
				_ => AutomaticSymmetry(requested)
			};
			var waterAmount = ResolveParameterLevel(requested.WaterAmount, PresetWaterAmount(requested.Preset));
			var tacticalTerrain = ResolveParameterLevel(requested.TacticalTerrain, PresetTacticalTerrain(requested.Preset));
			var layoutFamily = requested.SchemaVersion >= 3 ?
				ResolveLayoutFamily(requested.LayoutFamily, PresetLayoutFamily(requested.Preset)) :
				RmgLayoutFamily.ArtificialBattlefield;
			var version = layoutFamily switch
			{
				RmgLayoutFamily.ArtificialBattlefield when requested.IsPlannedBattlefield => 18,
				RmgLayoutFamily.Crossroads => 19,
				RmgLayoutFamily.Ring => 20,
				RmgLayoutFamily.DividedLands => 21,
				RmgLayoutFamily.Strongholds => 22,
				RmgLayoutFamily.Labyrinth => 23,
				RmgLayoutFamily.Archipelago => 24,
				RmgLayoutFamily.NaturalLandscapePvp => 17,
				RmgLayoutFamily.NaturalLandscape => requested.SchemaVersion >= 10 ? 16 : requested.SchemaVersion >= 9 ? 15 : requested.SchemaVersion >= 8 ? 14 : requested.SchemaVersion >= 7 ? 13 : requested.SchemaVersion >= 6 ? 12 : requested.SchemaVersion >= 5 ? 11 : 10,
				RmgLayoutFamily.StructuredCompetitive => 8,
				_ => requested.SchemaVersion >= 2 ? 7 : 6
			};
			if (version is 11 or 12 or 13 or 14 or 15 or 16 or 17 or 18 or 19 or 20 or 21 or 22 or 23 or 24 && (requested.Layout != RmgPlayerLayout.Preset || requested.Symmetry != RmgPlayerSymmetry.Automatic))
				throw new ArgumentException("Regions has no Battlefield Plan or symmetry setting; use preset layout and automatic symmetry.");
			if (!Enum.IsDefined(requested.TerrainComplexity) ||
				(requested.TerrainComplexity > Reassessment.TerrainComplexity.High && version is not (13 or 14 or 15 or 16 or 17 or 18 or 19 or 20 or 21 or 22 or 23 or 24)))
				throw new ArgumentException("Extended Terrain Complexity requires schema 7 and Natural Landscape.");
			if (!Enum.IsDefined(requested.WaterAmount) || !Enum.IsDefined(requested.TacticalTerrain) ||
				!Enum.IsDefined(requested.NeutralColonyDensity) || (version is not (14 or 15 or 16 or 17 or 18 or 19 or 20 or 21 or 22 or 23 or 24) &&
				(requested.WaterAmount > RmgPlayerParameterLevel.High || requested.TacticalTerrain > RmgPlayerParameterLevel.High ||
				requested.NeutralColonyDensity > RmgPlayerColonyDensity.Dense || !requested.PreventColonyOverlapping)))
				throw new ArgumentException("Extreme/Ultra quantities and relaxed colony spacing require schema 8 and Natural Landscape.");
			var normalized = new RmgGenerationSettings
			{
				Seed = requested.Seed,
				Tileset = requested.Tileset,
				MapSize = requested.MapSize,
				PlayerCount = requested.PlayerCount,
				Symmetry = version is 11 or 12 or 13 or 14 or 15 or 16 or 17 or 18 or 19 or 20 or 21 or 22 or 23 or 24 ? RmgSymmetry.MirrorHorizontal : symmetry,
				Archetype = version is 11 or 12 or 13 or 14 or 15 or 16 or 17 or 18 or 19 or 20 or 21 or 22 or 23 or 24 ? RmgArchetype.Open : archetype,
				NeutralColonyCount = colonies,
				GeneratorVersion = version,
				GenerateCastles = requested.GenerateCastles,
				ExtraRoutes = requested.ExtraRoutes,
				RespectStartingSafeArea = requested.RespectStartingSafeArea,
				OwnStartingStronghold = requested.OwnStartingStronghold,
				IslandAmount = requested.IslandAmount, IslandSize = requested.IslandSize,
				MirroringAxes = requested.IsPlannedBattlefield || requested.IsCrossroads || requested.IsRing || requested.IsDividedLands ? RmgBattlefieldParameters.Axes(requested.PlayerCount) : requested.MirroringAxes,
				LandCrossings = requested.LandCrossings, RingShape = requested.RingShape, BlockShape = requested.BlockShape, LaneWidth = requested.LaneWidth, SideConnections = requested.SideConnections,
				TopologyPreset = version switch
				{
					18 => RmgTopologyPreset.PlannedBattlefield,
					19 => RmgTopologyPreset.Crossroads,
					20 => RmgTopologyPreset.Ring,
					21 => RmgTopologyPreset.DividedLands,
					22 => RmgTopologyPreset.Strongholds,
					23 => RmgTopologyPreset.Labyrinth,
					24 => RmgTopologyPreset.Archipelago,
					11 or 12 or 13 or 14 or 15 or 16 or 17 => RmgTopologyPreset.NaturalRegions,
					10 => RmgTopologyPreset.NaturalTerrainV10,
					9 => RmgTopologyPreset.NaturalTerrain,
					8 => RmgTopologyPreset.CoherentWater,
					7 => RmgTopologyPreset.ParameterizedBattlefield,
					_ => RmgTopologyPreset.BattlefieldLayout
				},
				WaterAmount = waterAmount,
				LayoutFamily = layoutFamily,
				TacticalTerrain = tacticalTerrain,
				TerrainComplexity = requested.TerrainComplexity,
				OriginalSurfaceRelations = requested.OriginalSurfaceRelations,
				PreventColonyOverlapping = requested.PreventColonyOverlapping,
				NeutralColonyWeights = requested.NeutralColonyWeights,
				StartingColonyMode = requested.StartingColonyMode,
				StartingColonyShares = requested.SchemaVersion >= 10 && expandedPlayers ?
					(requested.StartingColonyShares.Length == 0 ? new int[requested.PlayerCount] : (int[])requested.StartingColonyShares.Clone()) : Array.Empty<int>()
			};
			var overrides = new List<string>();
			if (requested.Tileset != "NORMAL") overrides.Add("tileset");
			if (requested.MapSize != 128)
				overrides.Add("size");
			if (requested.Layout != RmgPlayerLayout.Preset)
				overrides.Add("layout");
			if (requested.LayoutFamily != RmgPlayerLayoutFamily.Preset)
				overrides.Add("layout_family");
			if (requested.NeutralColonyDensity != RmgPlayerColonyDensity.Preset)
				overrides.Add("neutral_colony_density");
			if (requested.WaterAmount != RmgPlayerParameterLevel.Preset)
				overrides.Add("water_amount");
			if (requested.TacticalTerrain != RmgPlayerParameterLevel.Preset)
				overrides.Add(version is 11 or 12 or 13 or 14 or 15 or 16 or 17 or 18 or 19 or 20 or 21 or 22 or 23 or 24 ? "gravel_moss_amount" : "tactical_terrain");
			if (requested.Symmetry != RmgPlayerSymmetry.Automatic)
				overrides.Add("symmetry");

			if (version is 11 or 12 or 13 or 14 or 15 or 16 or 17 or 18 or 19 or 20 or 21 or 22 or 23 or 24)
				overrides.Add("terrain_complexity");
			if (version is 14 or 15 or 16 or 17 or 18 or 19 or 20 or 21 or 22 or 23 or 24 && !requested.PreventColonyOverlapping)
				overrides.Add("prevent_colony_overlapping");
			if (version is 15 or 16 or 17 or 18 or 19 or 20 or 21 or 22 or 23 or 24 && requested.NeutralColonyWeights != new RmgColonyWeights())
				overrides.Add("neutral_colony_weights");
			if (version is 16 or 17 or 18 or 19 or 20 or 21 or 22 or 23 or 24 && normalized.StartingColonyShares.Any(value => value != 0))
				overrides.Add("starting_colony_shares");
			if (version is 16 or 17 or 18 or 19 or 20 or 21 or 22 or 23 or 24 && requested.StartingColonyMode != RmgColonyOwnershipMode.ClosestToSpawn)
				overrides.Add("starting_colony_mode");
			if (!requested.RespectStartingSafeArea) overrides.Add("respect_starting_safe_area");
			if (requested.IsArchipelago) { overrides.Add("island_amount"); overrides.Add("island_size"); }
			if (requested.OwnStartingStronghold) overrides.Add("own_starting_stronghold");
			if (version == 22 && !requested.GenerateCastles) overrides.Add("generate_castles");
			var resolution = new RmgPlayerSettingsResolution(requested, normalized, overrides);
			normalized.PlayerSettingsResolution = resolution;
			return resolution;
		}

		public static string ComplexityName(Reassessment.TerrainComplexity value, bool extended) => value switch
		{
			Reassessment.TerrainComplexity.Low => extended ? "small" : "low",
			Reassessment.TerrainComplexity.Standard => extended ? "medium" : "standard",
			Reassessment.TerrainComplexity.High => "high",
			Reassessment.TerrainComplexity.Extreme when extended => "extreme",
			Reassessment.TerrainComplexity.Ultra when extended => "ultra",
			_ => throw new ArgumentException("Unsupported Terrain Complexity.")
		};

		public static string ComplexityDisplayName(Reassessment.TerrainComplexity value, bool extended) =>
			extended && value == Reassessment.TerrainComplexity.Low ? "Small" :
			extended && value == Reassessment.TerrainComplexity.Standard ? "Medium" : value.ToString();

		static Reassessment.TerrainComplexity ParseComplexity(string text, int schema) => text switch
		{
			"small" when schema >= 7 => Reassessment.TerrainComplexity.Low,
			"medium" when schema >= 7 => Reassessment.TerrainComplexity.Standard,
			"extreme" when schema >= 7 => Reassessment.TerrainComplexity.Extreme,
			"ultra" when schema >= 7 => Reassessment.TerrainComplexity.Ultra,
			"low" when schema < 7 => Reassessment.TerrainComplexity.Low,
			"standard" when schema < 7 => Reassessment.TerrainComplexity.Standard,
			"high" => Reassessment.TerrainComplexity.High,
			_ => throw new ArgumentException(schema >= 7 ? "terrain_complexity must be small, medium, high, extreme, or ultra." :
				"terrain_complexity must be low, standard, or high.")
		};

		public static IReadOnlyList<string> RunSelfTests()
		{
			var failures = new List<string>();
			var balanced = new RmgPlayerSettings
			{
				Seed = 7300001,
				PlayerCount = 2
			};
			var first = Resolve(balanced);
			var repeat = Resolve(Parse(balanced.ToJson()));
			if (NormalizedSignature(first.Normalized) != NormalizedSignature(repeat.Normalized))
				failures.Add("Player-settings schema 3 JSON round trip changed normalized generator settings.");
			if (first.Normalized.GeneratorVersion != 8 ||
				first.Normalized.TopologyPreset != RmgTopologyPreset.CoherentWater ||
				first.Normalized.LayoutFamily != RmgLayoutFamily.StructuredCompetitive ||
				first.Normalized.Archetype != RmgArchetype.CentralContest ||
				first.Normalized.NeutralColonyCount != 10 ||
				first.Normalized.WaterAmount != RmgParameterLevel.Standard ||
				first.Normalized.TacticalTerrain != RmgParameterLevel.Standard)
				failures.Add("Balanced player preset did not resolve to the Version 8 Structured Competitive baseline.");

			var artificial = Resolve(new RmgPlayerSettings
			{
				SchemaVersion = 3,
				LayoutFamily = RmgPlayerLayoutFamily.ArtificialBattlefield,
				Seed = 7300001,
				PlayerCount = 2
			});
			if (artificial.Normalized.GeneratorVersion != 7 ||
				artificial.Normalized.TopologyPreset != RmgTopologyPreset.ParameterizedBattlefield ||
				artificial.Normalized.LayoutFamily != RmgLayoutFamily.ArtificialBattlefield)
				failures.Add("Explicit Artificial Battlefield schema 3 settings no longer resolve to frozen Version 7.");

			var schema2 = Resolve(new RmgPlayerSettings
			{
				SchemaVersion = 2,
				Seed = 7300001,
				PlayerCount = 2
			});
			if (schema2.Normalized.GeneratorVersion != 7 ||
				schema2.Normalized.TopologyPreset != RmgTopologyPreset.ParameterizedBattlefield ||
				schema2.Normalized.LayoutFamily != RmgLayoutFamily.ArtificialBattlefield)
				failures.Add("Player-settings schema 2 no longer resolves to the frozen Version 7 baseline.");

			var legacy = new RmgPlayerSettings
			{
				SchemaVersion = 1,
				Seed = 7300001,
				PlayerCount = 2
			};
			var legacyRoundTrip = Resolve(Parse(legacy.ToJson()));
			if (legacyRoundTrip.Normalized.GeneratorVersion != 6 ||
				legacyRoundTrip.Normalized.TopologyPreset != RmgTopologyPreset.BattlefieldLayout ||
				legacyRoundTrip.Normalized.WaterAmount != RmgParameterLevel.Standard ||
				legacyRoundTrip.Normalized.TacticalTerrain != RmgParameterLevel.Standard)
				failures.Add("Player-settings schema 1 no longer resolves to the frozen Version 6 baseline.");

			var open = Resolve(new RmgPlayerSettings
			{
				Preset = RmgPlayerPreset.OpenConflict,
				Seed = 7300002,
				PlayerCount = 4
			});
			if (open.Normalized.Archetype != RmgArchetype.Open || open.Normalized.NeutralColonyCount != 12 ||
				open.Normalized.WaterAmount != RmgParameterLevel.Low ||
				open.Normalized.TacticalTerrain != RmgParameterLevel.Low)
				failures.Add("Open Conflict preset did not resolve to open, sparse, Low-Water, Low-tactical settings.");

			var tactical = Resolve(new RmgPlayerSettings
			{
				Preset = RmgPlayerPreset.TacticalCrossroads,
				Seed = 7300003,
				PlayerCount = 4
			});
			if (tactical.Normalized.Archetype != RmgArchetype.CentralContest ||
				tactical.Normalized.NeutralColonyCount != 20 ||
				tactical.Normalized.WaterAmount != RmgParameterLevel.Standard ||
				tactical.Normalized.TacticalTerrain != RmgParameterLevel.High)
				failures.Add("Tactical Crossroads preset did not resolve to contested, dense, Standard-Water, High-tactical settings.");

			var customized = Resolve(new RmgPlayerSettings
			{
				Preset = RmgPlayerPreset.TacticalCrossroads,
				Seed = 7300003,
				PlayerCount = 2,
				Layout = RmgPlayerLayout.OpenFields,
				NeutralColonyDensity = RmgPlayerColonyDensity.Dense,
				WaterAmount = RmgPlayerParameterLevel.High,
				TacticalTerrain = RmgPlayerParameterLevel.Low,
				Symmetry = RmgPlayerSymmetry.Rotational
			});
			if (customized.Normalized.Archetype != RmgArchetype.Open ||
				customized.Normalized.NeutralColonyCount != 20 ||
				customized.Normalized.WaterAmount != RmgParameterLevel.High ||
				customized.Normalized.TacticalTerrain != RmgParameterLevel.Low ||
				customized.Normalized.Symmetry != RmgSymmetry.Rotate180 ||
				customized.Overrides.Count != 5)
				failures.Add("Explicit schema 2 player-setting overrides were not normalized correctly.");

			var automaticRepeat = Resolve(Parse(balanced.ToJson()));
			if (first.Normalized.Symmetry != automaticRepeat.Normalized.Symmetry)
				failures.Add("Automatic symmetry is not deterministic for identical schema 2 settings.");

			ExpectRejected("unsupported preset", () => Parse(JObject.Parse(
				"{\"schema_version\":2,\"preset\":\"narrow-passages\",\"seed\":\"1\",\"players\":2}")));
			ExpectRejected("unknown safety control", () => Parse(JObject.Parse(
				"{\"schema_version\":2,\"preset\":\"balanced\",\"seed\":\"1\",\"players\":2,\"disable_fairness\":true}")));
			ExpectRejected("schema 2 field in schema 1", () => Parse(JObject.Parse(
				"{\"schema_version\":1,\"preset\":\"balanced\",\"seed\":\"1\",\"players\":2,\"water_amount\":\"high\"}")));
			ExpectRejected("schema 3 field in schema 2", () => Parse(JObject.Parse(
				"{\"schema_version\":2,\"preset\":\"balanced\",\"seed\":\"1\",\"players\":2,\"layout_family\":\"natural-landscape\"}")));
			var naturalRequest = Parse(JObject.Parse(
				"{\"schema_version\":3,\"preset\":\"balanced\",\"seed\":\"1\",\"players\":2,\"layout_family\":\"natural-landscape\",\"original_surface_relations\":false}"));
			if (naturalRequest.OriginalSurfaceRelations || naturalRequest.ToJson().Value<bool>("original_surface_relations"))
				failures.Add("Natural Landscape original-surface-relations setting did not round trip.");
			ExpectRejected("Natural-only surface relation setting on Structured Competitive", () => Parse(JObject.Parse(
				"{\"schema_version\":3,\"preset\":\"balanced\",\"seed\":\"1\",\"players\":2,\"layout_family\":\"structured-competitive\",\"original_surface_relations\":true}")));
			var natural = Resolve(new RmgPlayerSettings
			{
				Seed = 1,
				PlayerCount = 2,
				LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscape,
				OriginalSurfaceRelations = false
			});
			if (natural.Normalized.GeneratorVersion != 10 ||
				natural.Normalized.TopologyPreset != RmgTopologyPreset.NaturalTerrainV10 ||
				natural.Normalized.LayoutFamily != RmgLayoutFamily.NaturalLandscape ||
				natural.Normalized.OriginalSurfaceRelations)
				failures.Add("Natural Landscape did not resolve to the experimental Version 10 contract.");
			// Schema 4 opts into size support without changing old presets or seed mapping.
			if (balanced.SchemaVersion != 3 || balanced.ToJson()["size"] != null ||
				first.Normalized.MapSize != 128)
				failures.Add("Legacy default settings no longer preserve the 128x128 contract.");
			foreach (var players in new[] { 2, 4 })
				foreach (var density in new[] { RmgPlayerColonyDensity.Sparse,
					RmgPlayerColonyDensity.Standard, RmgPlayerColonyDensity.Dense })
				{
					var largeRequest = new RmgPlayerSettings
					{
						SchemaVersion = 4,
						MapSize = 256,
						Seed = 7300001,
						PlayerCount = players,
						LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscape,
						NeutralColonyDensity = density
					};
					var large = Resolve(largeRequest);
					var roundTrip = Resolve(Parse(largeRequest.ToJson()));
					var small = Resolve(new RmgPlayerSettings
					{
						PlayerCount = players,
						LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscape,
						NeutralColonyDensity = density
					});
					if (large.Normalized.MapSize != 256 ||
						large.Normalized.NeutralColonyCount != small.Normalized.NeutralColonyCount * 3 ||
						NormalizedSignature(large.Normalized) != NormalizedSignature(roundTrip.Normalized) ||
						!large.Overrides.Contains("size"))
						failures.Add($"256x256 settings failed the {players}-player {density} round-trip/count contract.");
				}
			foreach (var schema in new[] { 1, 2, 3 })
			{
				var oldJson = new RmgPlayerSettings { SchemaVersion = schema }.ToJson();
				oldJson["size"] = "256,256";
				ExpectRejected($"size in schema {schema}", () => Parse(oldJson));
			}
			foreach (var family in new[] { RmgPlayerLayoutFamily.Preset,
				RmgPlayerLayoutFamily.ArtificialBattlefield, RmgPlayerLayoutFamily.StructuredCompetitive })
				ExpectRejected($"256x256 {family}", () => Resolve(new RmgPlayerSettings
				{
					SchemaVersion = 4, MapSize = 256, LayoutFamily = family
				}));
			foreach (var size in new[] { "64,64", "128,256", "512,512", "256" })
				ExpectRejected($"size {size}", () => ParseMapSize(size));
			foreach (var players in new[] { 1, 3, 8 })
				ExpectRejected($"256x256 with {players} players", () => Resolve(new RmgPlayerSettings
				{
					SchemaVersion = 4, MapSize = 256, PlayerCount = players,
					LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscape
				}));
			return failures;

			void ExpectRejected(string label, Action action)
			{
				try
				{
					action();
					failures.Add($"Player settings accepted {label}.");
				}
				catch (ArgumentException)
				{
				}
			}
		}

		public static string PresetName(RmgPlayerPreset value) => value switch
		{
			RmgPlayerPreset.Balanced => "balanced",
			RmgPlayerPreset.OpenConflict => "open-conflict",
			RmgPlayerPreset.TacticalCrossroads => "tactical-crossroads",
			_ => throw new ArgumentOutOfRangeException(nameof(value))
		};

		public static string PresetDisplayName(RmgPlayerPreset value) => value switch
		{
			RmgPlayerPreset.Balanced => "Balanced",
			RmgPlayerPreset.OpenConflict => "Open Conflict",
			RmgPlayerPreset.TacticalCrossroads => "Tactical Crossroads",
			_ => throw new ArgumentOutOfRangeException(nameof(value))
		};

		public static string SymmetryName(RmgPlayerSymmetry value) => value switch
		{
			RmgPlayerSymmetry.Automatic => "automatic",
			RmgPlayerSymmetry.Horizontal => "horizontal",
			RmgPlayerSymmetry.Vertical => "vertical",
			RmgPlayerSymmetry.Rotational => "rotational",
			_ => throw new ArgumentOutOfRangeException(nameof(value))
		};

		public static string LayoutName(RmgPlayerLayout value) => value switch
		{
			RmgPlayerLayout.Preset => "preset",
			RmgPlayerLayout.OpenFields => "open-fields",
			RmgPlayerLayout.ContestedCenter => "contested-center",
			_ => throw new ArgumentOutOfRangeException(nameof(value))
		};

		public static string PlayerLayoutFamilyName(RmgPlayerLayoutFamily value) => value switch
		{
			RmgPlayerLayoutFamily.Preset => "preset",
			RmgPlayerLayoutFamily.NaturalLandscape => "natural-landscape",
			RmgPlayerLayoutFamily.NaturalLandscapePvp => "natural-landscape-pvp",
			RmgPlayerLayoutFamily.Crossroads => "crossroads",
			RmgPlayerLayoutFamily.Ring => "ring",
			RmgPlayerLayoutFamily.DividedLands => "divided-lands",
			RmgPlayerLayoutFamily.Strongholds => "strongholds",
			RmgPlayerLayoutFamily.Labyrinth => "labyrinth",
			RmgPlayerLayoutFamily.Archipelago => "archipelago",
			RmgPlayerLayoutFamily.StructuredCompetitive => "structured-competitive",
			RmgPlayerLayoutFamily.ArtificialBattlefield => "artificial-battlefield",
			_ => throw new ArgumentOutOfRangeException(nameof(value))
		};

		public static string LayoutFamilyName(RmgLayoutFamily value) => value switch
		{
			RmgLayoutFamily.NaturalLandscape => "natural-landscape",
			RmgLayoutFamily.NaturalLandscapePvp => "natural-landscape-pvp",
			RmgLayoutFamily.Crossroads => "crossroads",
			RmgLayoutFamily.Ring => "ring",
			RmgLayoutFamily.DividedLands => "divided-lands",
			RmgLayoutFamily.Strongholds => "strongholds",
			RmgLayoutFamily.Labyrinth => "labyrinth",
			RmgLayoutFamily.Archipelago => "archipelago",
			RmgLayoutFamily.StructuredCompetitive => "structured-competitive",
			RmgLayoutFamily.ArtificialBattlefield => "artificial-battlefield",
			_ => throw new ArgumentOutOfRangeException(nameof(value))
		};

		public static string LayoutFamilyDisplayName(RmgLayoutFamily value) => value switch
		{
			RmgLayoutFamily.NaturalLandscape => "Natural Landscape",
			RmgLayoutFamily.NaturalLandscapePvp => "Natural Landscape PVP",
			RmgLayoutFamily.Crossroads => "Crossroads",
			RmgLayoutFamily.Ring => "Ring",
			RmgLayoutFamily.DividedLands => "Divided Lands",
			RmgLayoutFamily.Strongholds => "Strongholds",
			RmgLayoutFamily.Labyrinth => "Labyrinth",
			RmgLayoutFamily.Archipelago => "Archipelago",
			RmgLayoutFamily.StructuredCompetitive => "Structured Competitive",
			RmgLayoutFamily.ArtificialBattlefield => "Artificial Battlefield",
			_ => throw new ArgumentOutOfRangeException(nameof(value))
		};

		public static string ColonyDensityName(RmgPlayerColonyDensity value) => value switch
		{
			RmgPlayerColonyDensity.Preset => "preset",
			RmgPlayerColonyDensity.Sparse => "sparse",
			RmgPlayerColonyDensity.Standard => "standard",
			RmgPlayerColonyDensity.Dense => "dense",
			RmgPlayerColonyDensity.Extreme => "extreme",
			RmgPlayerColonyDensity.Ultra => "ultra",
			_ => throw new ArgumentOutOfRangeException(nameof(value))
		};

		public static string PlayerParameterLevelName(RmgPlayerParameterLevel value) => value switch
		{
			RmgPlayerParameterLevel.Preset => "preset",
			RmgPlayerParameterLevel.Low => "low",
			RmgPlayerParameterLevel.Standard => "standard",
			RmgPlayerParameterLevel.High => "high",
			RmgPlayerParameterLevel.Extreme => "extreme",
			RmgPlayerParameterLevel.Ultra => "ultra",
			_ => throw new ArgumentOutOfRangeException(nameof(value))
		};

		public static string ParameterLevelName(RmgParameterLevel value) => value switch
		{
			RmgParameterLevel.Low => "low",
			RmgParameterLevel.Standard => "standard",
			RmgParameterLevel.High => "high",
			RmgParameterLevel.Extreme => "extreme",
			RmgParameterLevel.Ultra => "ultra",
			_ => throw new ArgumentOutOfRangeException(nameof(value))
		};

		static RmgPlayerPreset ParsePreset(string value) => value.ToLowerInvariant() switch
		{
			"balanced" => RmgPlayerPreset.Balanced,
			"open-conflict" => RmgPlayerPreset.OpenConflict,
			"tactical-crossroads" => RmgPlayerPreset.TacticalCrossroads,
			"narrow-passages" => throw new ArgumentException("The Narrow Passages preset is deferred until its dedicated layout contract is implemented."),
			_ => throw new ArgumentException("Player settings preset must be balanced, open-conflict, or tactical-crossroads.")
		};

		static RmgPlayerSymmetry ParseSymmetry(string value) => value.ToLowerInvariant() switch
		{
			"automatic" => RmgPlayerSymmetry.Automatic,
			"horizontal" => RmgPlayerSymmetry.Horizontal,
			"vertical" => RmgPlayerSymmetry.Vertical,
			"rotational" => RmgPlayerSymmetry.Rotational,
			_ => throw new ArgumentException("Player settings symmetry must be automatic, horizontal, vertical, or rotational.")
		};

		static RmgPlayerLayout ParseLayout(string value) => value.ToLowerInvariant() switch
		{
			"preset" => RmgPlayerLayout.Preset,
			"open-fields" => RmgPlayerLayout.OpenFields,
			"contested-center" => RmgPlayerLayout.ContestedCenter,
			"narrow-passages" => throw new ArgumentException("The Narrow Passages layout is deferred until its dedicated layout contract is implemented."),
			_ => throw new ArgumentException("Player settings layout must be preset, open-fields, or contested-center.")
		};

		static RmgPlayerLayoutFamily ParsePlayerLayoutFamily(string value) => value.ToLowerInvariant() switch
		{
			"preset" => RmgPlayerLayoutFamily.Preset,
			"natural-landscape" => RmgPlayerLayoutFamily.NaturalLandscape,
			"natural-landscape-pvp" => RmgPlayerLayoutFamily.NaturalLandscapePvp,
			"crossroads" => RmgPlayerLayoutFamily.Crossroads,
			"ring" => RmgPlayerLayoutFamily.Ring,
			"divided-lands" => RmgPlayerLayoutFamily.DividedLands,
			"strongholds" => RmgPlayerLayoutFamily.Strongholds,
			"labyrinth" => RmgPlayerLayoutFamily.Labyrinth,
			"archipelago" => RmgPlayerLayoutFamily.Archipelago,
			"structured-competitive" => RmgPlayerLayoutFamily.StructuredCompetitive,
			"artificial-battlefield" => RmgPlayerLayoutFamily.ArtificialBattlefield,
			_ => throw new ArgumentException("Player settings layout_family must be preset, natural-landscape, natural-landscape-pvp, structured-competitive, artificial-battlefield, crossroads, ring, divided-lands, strongholds, or labyrinth.")
		};

		static RmgPlayerColonyDensity ParseColonyDensity(string value) => value.ToLowerInvariant() switch
		{
			"preset" => RmgPlayerColonyDensity.Preset,
			"sparse" => RmgPlayerColonyDensity.Sparse,
			"standard" => RmgPlayerColonyDensity.Standard,
			"dense" => RmgPlayerColonyDensity.Dense,
			"extreme" => RmgPlayerColonyDensity.Extreme,
			"ultra" => RmgPlayerColonyDensity.Ultra,
			_ => throw new ArgumentException("Player settings neutral_colony_density must be preset, sparse, standard, dense, extreme, or ultra.")
		};

		static RmgPlayerParameterLevel ParsePlayerParameterLevel(string value, string field) => value.ToLowerInvariant() switch
		{
			"preset" => RmgPlayerParameterLevel.Preset,
			"low" => RmgPlayerParameterLevel.Low,
			"standard" => RmgPlayerParameterLevel.Standard,
			"high" => RmgPlayerParameterLevel.High,
			"extreme" => RmgPlayerParameterLevel.Extreme,
			"ultra" => RmgPlayerParameterLevel.Ultra,
			_ => throw new ArgumentException($"Player settings {field} must be preset, low, standard, high, extreme, or ultra.")
		};

		static RmgArchetype PresetArchetype(RmgPlayerPreset preset) => preset switch
		{
			RmgPlayerPreset.OpenConflict => RmgArchetype.Open,
			_ => RmgArchetype.CentralContest
		};

		static int PresetColonies(RmgPlayerPreset preset, int players) => preset switch
		{
			RmgPlayerPreset.OpenConflict => players == 2 ? 8 : 12,
			RmgPlayerPreset.TacticalCrossroads => players == 2 ? 14 : 20,
			_ => players == 2 ? 10 : 16
		};

		static RmgLayoutFamily PresetLayoutFamily(RmgPlayerPreset preset) => preset switch
		{
			RmgPlayerPreset.Balanced => RmgLayoutFamily.StructuredCompetitive,
			_ => RmgLayoutFamily.ArtificialBattlefield
		};

		static RmgLayoutFamily ResolveLayoutFamily(RmgPlayerLayoutFamily requested, RmgLayoutFamily preset) => requested switch
		{
			RmgPlayerLayoutFamily.Preset => preset,
			RmgPlayerLayoutFamily.NaturalLandscape => RmgLayoutFamily.NaturalLandscape,
			RmgPlayerLayoutFamily.NaturalLandscapePvp => RmgLayoutFamily.NaturalLandscapePvp,
			RmgPlayerLayoutFamily.Crossroads => RmgLayoutFamily.Crossroads,
			RmgPlayerLayoutFamily.Ring => RmgLayoutFamily.Ring,
			RmgPlayerLayoutFamily.DividedLands => RmgLayoutFamily.DividedLands,
			RmgPlayerLayoutFamily.Strongholds => RmgLayoutFamily.Strongholds,
			RmgPlayerLayoutFamily.Labyrinth => RmgLayoutFamily.Labyrinth,
			RmgPlayerLayoutFamily.Archipelago => RmgLayoutFamily.Archipelago,
			RmgPlayerLayoutFamily.StructuredCompetitive => RmgLayoutFamily.StructuredCompetitive,
			RmgPlayerLayoutFamily.ArtificialBattlefield => RmgLayoutFamily.ArtificialBattlefield,
			_ => throw new ArgumentOutOfRangeException(nameof(requested))
		};

		static RmgParameterLevel PresetWaterAmount(RmgPlayerPreset preset) => preset switch
		{
			RmgPlayerPreset.OpenConflict => RmgParameterLevel.Low,
			_ => RmgParameterLevel.Standard
		};

		static RmgParameterLevel PresetTacticalTerrain(RmgPlayerPreset preset) => preset switch
		{
			RmgPlayerPreset.OpenConflict => RmgParameterLevel.Low,
			RmgPlayerPreset.TacticalCrossroads => RmgParameterLevel.High,
			_ => RmgParameterLevel.Standard
		};

		static RmgParameterLevel ResolveParameterLevel(RmgPlayerParameterLevel requested, RmgParameterLevel preset) => requested switch
		{
			RmgPlayerParameterLevel.Preset => preset,
			RmgPlayerParameterLevel.Low => RmgParameterLevel.Low,
			RmgPlayerParameterLevel.Standard => RmgParameterLevel.Standard,
			RmgPlayerParameterLevel.High => RmgParameterLevel.High,
			RmgPlayerParameterLevel.Extreme => RmgParameterLevel.Extreme,
			RmgPlayerParameterLevel.Ultra => RmgParameterLevel.Ultra,
			_ => throw new ArgumentOutOfRangeException(nameof(requested))
		};

		static int DensityColonies(RmgPlayerColonyDensity density, int players) => density switch
		{
			RmgPlayerColonyDensity.Sparse => players == 2 ? 8 : 12,
			RmgPlayerColonyDensity.Standard => players == 2 ? 10 : 16,
			RmgPlayerColonyDensity.Dense => players == 2 ? 20 : 24,
			RmgPlayerColonyDensity.Extreme => players == 2 ? 32 : 40,
			RmgPlayerColonyDensity.Ultra => players == 2 ? 52 : 64,
			_ => throw new ArgumentOutOfRangeException(nameof(density))
		};

		static RmgSymmetry AutomaticSymmetry(RmgPlayerSettings requested)
		{
			var layoutFamily = requested.SchemaVersion >= 3 ?
				$"\nlayout-family={PlayerLayoutFamilyName(requested.LayoutFamily)}" : string.Empty;
			var canonical = $"schema={requested.SchemaVersion}\nseed={requested.Seed}\npreset={PresetName(requested.Preset)}\nplayers={requested.PlayerCount}{layoutFamily}\nstream=automatic-symmetry";
			var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
			var options = new[]
			{
				RmgSymmetry.MirrorHorizontal, RmgSymmetry.MirrorVertical, RmgSymmetry.Rotate180
			};
			return options[hash[0] % options.Length];
		}

		static string NormalizedSignature(RmgGenerationSettings settings) => string.Join("\n", new[]
		{
			$"size={settings.MapSize}",
			$"generator={settings.GeneratorVersion}",
			$"topology={settings.TopologyPreset}",
			$"layout-family={settings.LayoutFamily}",
			$"seed={settings.Seed}",
			$"players={settings.PlayerCount}",
			$"symmetry={settings.Symmetry}",
			$"archetype={settings.Archetype}",
			$"colonies={settings.NeutralColonyCount}",
			$"water={settings.WaterAmount}",
			$"tactical-terrain={settings.TacticalTerrain}",
			$"original-surface-relations={settings.OriginalSurfaceRelations}"
		});

		public static int ParseMapSize(string value, bool allowV16Sizes = false) => value switch
		{
			"64,64" when allowV16Sizes => 64,
			"512,512" when allowV16Sizes => 512,
			"128,128" => 128,
			"256,256" => 256,
			_ => throw new ArgumentException(allowV16Sizes ? "Map size must be 64,64, 128,128, 256,256 or 512,512." : "Map size must be 128,128 or 256,256.")
		};

		static int RequiredInt(JObject json, string name)
		{
			var token = json[name] ?? throw new ArgumentException($"Player settings field '{name}' is required.");
			if (token.Type != JTokenType.Integer || !int.TryParse(token.ToString(), NumberStyles.Integer,
				CultureInfo.InvariantCulture, out var value))
				throw new ArgumentException($"Player settings field '{name}' must be an integer.");
			return value;
		}

		static string RequiredText(JObject json, string name)
		{
			var token = json[name] ?? throw new ArgumentException($"Player settings field '{name}' is required.");
			if (token.Type != JTokenType.String && token.Type != JTokenType.Integer)
				throw new ArgumentException($"Player settings field '{name}' must be a string or integer.");
			var value = token.ToString();
			if (string.IsNullOrWhiteSpace(value))
				throw new ArgumentException($"Player settings field '{name}' cannot be empty.");
			return value;
		}

		static string OptionalText(JObject json, string name, string fallback) =>
			json[name] == null ? fallback : RequiredText(json, name);

		static bool OptionalBool(JObject json, string name, bool fallback)
		{
			var token = json[name];
			if (token == null)
				return fallback;
			if (token.Type != JTokenType.Boolean)
				throw new ArgumentException($"Player settings field '{name}' must be a JSON boolean.");
			return token.Value<bool>();
		}
	}
}
