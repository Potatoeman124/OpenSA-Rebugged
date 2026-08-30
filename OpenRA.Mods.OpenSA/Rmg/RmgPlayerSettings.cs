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
		ArtificialBattlefield
	}

	public enum RmgPlayerColonyDensity
	{
		Preset,
		Sparse,
		Standard,
		Dense
	}

	public enum RmgPlayerParameterLevel
	{
		Preset,
		Low,
		Standard,
		High
	}

	public sealed class RmgPlayerSettings
	{
		public int SchemaVersion { get; init; } = RmgPlayerSettingsContract.SchemaVersion;
		public RmgPlayerPreset Preset { get; init; } = RmgPlayerPreset.Balanced;
		public ulong Seed { get; init; }
		public int PlayerCount { get; init; } = 2;
		public RmgPlayerSymmetry Symmetry { get; init; } = RmgPlayerSymmetry.Automatic;
		public RmgPlayerLayout Layout { get; init; } = RmgPlayerLayout.Preset;
		public RmgPlayerLayoutFamily LayoutFamily { get; init; } = RmgPlayerLayoutFamily.Preset;
		public RmgPlayerColonyDensity NeutralColonyDensity { get; init; } = RmgPlayerColonyDensity.Preset;
		public RmgPlayerParameterLevel WaterAmount { get; init; } = RmgPlayerParameterLevel.Preset;
		public RmgPlayerParameterLevel TacticalTerrain { get; init; } = RmgPlayerParameterLevel.Preset;

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
				json["tactical_terrain"] = RmgPlayerSettingsContract.PlayerParameterLevelName(TacticalTerrain);
			}

			if (SchemaVersion >= 3)
				json["layout_family"] = RmgPlayerSettingsContract.PlayerLayoutFamilyName(LayoutFamily);

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

		public JObject ToJson() => new()
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
				["tileset"] = "NORMAL",
				["size"] = "128,128"
			},
			["overrides"] = new JArray(Overrides),
			["warnings"] = new JArray()
		};
	}

	public static class RmgPlayerSettingsContract
	{
		public const int SchemaVersion = 3;
		public const int MinimumSchemaVersion = 1;

		static readonly HashSet<string> AllowedFields = new(new[]
		{
			"schema_version",
			"preset",
			"seed",
			"players",
			"symmetry",
			"layout",
			"layout_family",
			"neutral_colony_density",
			"water_amount",
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
			if (schemaVersion < MinimumSchemaVersion || schemaVersion > SchemaVersion)
				throw new ArgumentException($"Player settings schema_version must be from {MinimumSchemaVersion} through {SchemaVersion}.");
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

			var seedText = RequiredText(json, "seed");
			if (!ulong.TryParse(seedText, NumberStyles.None, CultureInfo.InvariantCulture, out var seed))
				throw new ArgumentException("Player settings seed must be an unsigned integer encoded as a JSON string or integer.");

			return new RmgPlayerSettings
			{
				SchemaVersion = schemaVersion,
				Preset = ParsePreset(RequiredText(json, "preset")),
				Seed = seed,
				PlayerCount = RequiredInt(json, "players"),
				Symmetry = ParseSymmetry(OptionalText(json, "symmetry", "automatic")),
				Layout = ParseLayout(OptionalText(json, "layout", "preset")),
				LayoutFamily = schemaVersion >= 3 ?
					ParsePlayerLayoutFamily(OptionalText(json, "layout_family", "preset")) :
					RmgPlayerLayoutFamily.Preset,
				NeutralColonyDensity = ParseColonyDensity(OptionalText(json, "neutral_colony_density", "preset")),
				WaterAmount = schemaVersion >= 2 ?
					ParsePlayerParameterLevel(OptionalText(json, "water_amount", "preset"), "water_amount") :
					RmgPlayerParameterLevel.Preset,
				TacticalTerrain = schemaVersion >= 2 ?
					ParsePlayerParameterLevel(OptionalText(json, "tactical_terrain", "preset"), "tactical_terrain") :
					RmgPlayerParameterLevel.Preset
			};
		}

		public static RmgPlayerSettingsResolution Resolve(RmgPlayerSettings requested)
		{
			if (requested.SchemaVersion < MinimumSchemaVersion || requested.SchemaVersion > SchemaVersion)
				throw new ArgumentException($"Player settings schema_version must be from {MinimumSchemaVersion} through {SchemaVersion}.");
			if (requested.PlayerCount != 2 && requested.PlayerCount != 4)
				throw new ArgumentException("Player settings players must be 2 or 4.");

			var archetype = requested.Layout switch
			{
				RmgPlayerLayout.OpenFields => RmgArchetype.Open,
				RmgPlayerLayout.ContestedCenter => RmgArchetype.CentralContest,
				_ => PresetArchetype(requested.Preset)
			};
			var colonies = requested.NeutralColonyDensity == RmgPlayerColonyDensity.Preset ?
				PresetColonies(requested.Preset, requested.PlayerCount) :
				DensityColonies(requested.NeutralColonyDensity, requested.PlayerCount);
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
			if (layoutFamily == RmgLayoutFamily.NaturalLandscape)
				throw new ArgumentException("Natural Landscape is planned until a genuinely organic terrain-generation contract is implemented.");
			var version = layoutFamily == RmgLayoutFamily.StructuredCompetitive ? 8 :
				requested.SchemaVersion >= 2 ? 7 : 6;
			var normalized = new RmgGenerationSettings
			{
				Seed = requested.Seed,
				PlayerCount = requested.PlayerCount,
				Symmetry = symmetry,
				Archetype = archetype,
				NeutralColonyCount = colonies,
				GeneratorVersion = version,
				TopologyPreset = version switch
				{
					8 => RmgTopologyPreset.CoherentWater,
					7 => RmgTopologyPreset.ParameterizedBattlefield,
					_ => RmgTopologyPreset.BattlefieldLayout
				},
				WaterAmount = waterAmount,
				LayoutFamily = layoutFamily,
				TacticalTerrain = tacticalTerrain
			};
			var overrides = new List<string>();
			if (requested.Layout != RmgPlayerLayout.Preset)
				overrides.Add("layout");
			if (requested.LayoutFamily != RmgPlayerLayoutFamily.Preset)
				overrides.Add("layout_family");
			if (requested.NeutralColonyDensity != RmgPlayerColonyDensity.Preset)
				overrides.Add("neutral_colony_density");
			if (requested.WaterAmount != RmgPlayerParameterLevel.Preset)
				overrides.Add("water_amount");
			if (requested.TacticalTerrain != RmgPlayerParameterLevel.Preset)
				overrides.Add("tactical_terrain");
			if (requested.Symmetry != RmgPlayerSymmetry.Automatic)
				overrides.Add("symmetry");

			var resolution = new RmgPlayerSettingsResolution(requested, normalized, overrides);
			normalized.PlayerSettingsResolution = resolution;
			return resolution;
		}

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
			ExpectRejected("planned natural landscape", () => Resolve(new RmgPlayerSettings
			{
				Seed = 1,
				PlayerCount = 2,
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
			RmgPlayerLayoutFamily.StructuredCompetitive => "structured-competitive",
			RmgPlayerLayoutFamily.ArtificialBattlefield => "artificial-battlefield",
			_ => throw new ArgumentOutOfRangeException(nameof(value))
		};

		public static string LayoutFamilyName(RmgLayoutFamily value) => value switch
		{
			RmgLayoutFamily.NaturalLandscape => "natural-landscape",
			RmgLayoutFamily.StructuredCompetitive => "structured-competitive",
			RmgLayoutFamily.ArtificialBattlefield => "artificial-battlefield",
			_ => throw new ArgumentOutOfRangeException(nameof(value))
		};

		public static string LayoutFamilyDisplayName(RmgLayoutFamily value) => value switch
		{
			RmgLayoutFamily.NaturalLandscape => "Natural Landscape",
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
			_ => throw new ArgumentOutOfRangeException(nameof(value))
		};

		public static string PlayerParameterLevelName(RmgPlayerParameterLevel value) => value switch
		{
			RmgPlayerParameterLevel.Preset => "preset",
			RmgPlayerParameterLevel.Low => "low",
			RmgPlayerParameterLevel.Standard => "standard",
			RmgPlayerParameterLevel.High => "high",
			_ => throw new ArgumentOutOfRangeException(nameof(value))
		};

		public static string ParameterLevelName(RmgParameterLevel value) => value switch
		{
			RmgParameterLevel.Low => "low",
			RmgParameterLevel.Standard => "standard",
			RmgParameterLevel.High => "high",
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
			"structured-competitive" => RmgPlayerLayoutFamily.StructuredCompetitive,
			"artificial-battlefield" => RmgPlayerLayoutFamily.ArtificialBattlefield,
			_ => throw new ArgumentException("Player settings layout_family must be preset, natural-landscape, structured-competitive, or artificial-battlefield.")
		};

		static RmgPlayerColonyDensity ParseColonyDensity(string value) => value.ToLowerInvariant() switch
		{
			"preset" => RmgPlayerColonyDensity.Preset,
			"sparse" => RmgPlayerColonyDensity.Sparse,
			"standard" => RmgPlayerColonyDensity.Standard,
			"dense" => RmgPlayerColonyDensity.Dense,
			_ => throw new ArgumentException("Player settings neutral_colony_density must be preset, sparse, standard, or dense.")
		};

		static RmgPlayerParameterLevel ParsePlayerParameterLevel(string value, string field) => value.ToLowerInvariant() switch
		{
			"preset" => RmgPlayerParameterLevel.Preset,
			"low" => RmgPlayerParameterLevel.Low,
			"standard" => RmgPlayerParameterLevel.Standard,
			"high" => RmgPlayerParameterLevel.High,
			_ => throw new ArgumentException($"Player settings {field} must be preset, low, standard, or high.")
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
			_ => throw new ArgumentOutOfRangeException(nameof(requested))
		};

		static int DensityColonies(RmgPlayerColonyDensity density, int players) => density switch
		{
			RmgPlayerColonyDensity.Sparse => players == 2 ? 8 : 12,
			RmgPlayerColonyDensity.Standard => players == 2 ? 10 : 16,
			RmgPlayerColonyDensity.Dense => players == 2 ? 20 : 24,
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
			$"generator={settings.GeneratorVersion}",
			$"topology={settings.TopologyPreset}",
			$"layout-family={settings.LayoutFamily}",
			$"seed={settings.Seed}",
			$"players={settings.PlayerCount}",
			$"symmetry={settings.Symmetry}",
			$"archetype={settings.Archetype}",
			$"colonies={settings.NeutralColonyCount}",
			$"water={settings.WaterAmount}",
			$"tactical-terrain={settings.TacticalTerrain}"
		});

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
	}
}
