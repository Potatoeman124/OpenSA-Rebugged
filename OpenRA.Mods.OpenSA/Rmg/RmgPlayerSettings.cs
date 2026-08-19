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

	public enum RmgPlayerColonyDensity
	{
		Preset,
		Sparse,
		Standard,
		Dense
	}

	public sealed class RmgPlayerSettings
	{
		public int SchemaVersion { get; init; } = RmgPlayerSettingsContract.SchemaVersion;
		public RmgPlayerPreset Preset { get; init; } = RmgPlayerPreset.Balanced;
		public ulong Seed { get; init; }
		public int PlayerCount { get; init; } = 2;
		public RmgPlayerSymmetry Symmetry { get; init; } = RmgPlayerSymmetry.Automatic;
		public RmgPlayerLayout Layout { get; init; } = RmgPlayerLayout.Preset;
		public RmgPlayerColonyDensity NeutralColonyDensity { get; init; } = RmgPlayerColonyDensity.Preset;

		public JObject ToJson() => new()
		{
			["schema_version"] = SchemaVersion,
			["preset"] = RmgPlayerSettingsContract.PresetName(Preset),
			["seed"] = Seed.ToString(CultureInfo.InvariantCulture),
			["players"] = PlayerCount,
			["symmetry"] = RmgPlayerSettingsContract.SymmetryName(Symmetry),
			["layout"] = RmgPlayerSettingsContract.LayoutName(Layout),
			["neutral_colony_density"] = RmgPlayerSettingsContract.ColonyDensityName(NeutralColonyDensity)
		};
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
				["seed"] = Normalized.Seed.ToString(CultureInfo.InvariantCulture),
				["players"] = Normalized.PlayerCount,
				["symmetry"] = OpenRaRmgMapAdapter.SymmetryName(Normalized.Symmetry),
				["archetype"] = OpenRaRmgMapAdapter.ArchetypeName(Normalized.Archetype),
				["neutral_colonies"] = Normalized.NeutralColonyCount,
				["tileset"] = "NORMAL",
				["size"] = "128,128"
			},
			["overrides"] = new JArray(Overrides),
			["warnings"] = new JArray()
		};
	}

	public static class RmgPlayerSettingsContract
	{
		public const int SchemaVersion = 1;

		static readonly HashSet<string> AllowedFields = new(new[]
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
			if (schemaVersion != SchemaVersion)
				throw new ArgumentException($"Player settings schema_version must be {SchemaVersion}.");

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
				NeutralColonyDensity = ParseColonyDensity(OptionalText(json, "neutral_colony_density", "preset"))
			};
		}

		public static RmgPlayerSettingsResolution Resolve(RmgPlayerSettings requested)
		{
			if (requested.SchemaVersion != SchemaVersion)
				throw new ArgumentException($"Player settings schema_version must be {SchemaVersion}.");
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
			var normalized = new RmgGenerationSettings
			{
				Seed = requested.Seed,
				PlayerCount = requested.PlayerCount,
				Symmetry = symmetry,
				Archetype = archetype,
				NeutralColonyCount = colonies,
				GeneratorVersion = 6,
				TopologyPreset = RmgTopologyPreset.BattlefieldLayout
			};
			var overrides = new List<string>();
			if (requested.Layout != RmgPlayerLayout.Preset)
				overrides.Add("layout");
			if (requested.NeutralColonyDensity != RmgPlayerColonyDensity.Preset)
				overrides.Add("neutral_colony_density");
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
				failures.Add("Player-settings JSON round trip changed normalized generator settings.");
			if (first.Normalized.GeneratorVersion != 6 || first.Normalized.TopologyPreset != RmgTopologyPreset.BattlefieldLayout ||
				first.Normalized.Archetype != RmgArchetype.CentralContest || first.Normalized.NeutralColonyCount != 10)
				failures.Add("Balanced player preset did not resolve to the accepted Version 6 baseline.");

			var open = Resolve(new RmgPlayerSettings
			{
				Preset = RmgPlayerPreset.OpenConflict,
				Seed = 7300002,
				PlayerCount = 4
			});
			if (open.Normalized.Archetype != RmgArchetype.Open || open.Normalized.NeutralColonyCount != 12)
				failures.Add("Open Conflict preset did not resolve to open layout and sparse four-player colonies.");

			var tactical = Resolve(new RmgPlayerSettings
			{
				Preset = RmgPlayerPreset.TacticalCrossroads,
				Seed = 7300003,
				PlayerCount = 4
			});
			if (tactical.Normalized.Archetype != RmgArchetype.CentralContest || tactical.Normalized.NeutralColonyCount != 20)
				failures.Add("Tactical Crossroads preset did not resolve to contested layout and dense four-player colonies.");

			var customized = Resolve(new RmgPlayerSettings
			{
				Preset = RmgPlayerPreset.TacticalCrossroads,
				Seed = 7300003,
				PlayerCount = 2,
				Layout = RmgPlayerLayout.OpenFields,
				NeutralColonyDensity = RmgPlayerColonyDensity.Dense,
				Symmetry = RmgPlayerSymmetry.Rotational
			});
			if (customized.Normalized.Archetype != RmgArchetype.Open || customized.Normalized.NeutralColonyCount != 20 ||
				customized.Normalized.Symmetry != RmgSymmetry.Rotate180 || customized.Overrides.Count != 3)
				failures.Add("Explicit player-setting overrides were not normalized correctly.");

			ExpectRejected("unsupported preset", () => Parse(JObject.Parse(
				"{\"schema_version\":1,\"preset\":\"narrow-passages\",\"seed\":\"1\",\"players\":2}")));
			ExpectRejected("unknown safety control", () => Parse(JObject.Parse(
				"{\"schema_version\":1,\"preset\":\"balanced\",\"seed\":\"1\",\"players\":2,\"disable_fairness\":true}")));
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

		public static string ColonyDensityName(RmgPlayerColonyDensity value) => value switch
		{
			RmgPlayerColonyDensity.Preset => "preset",
			RmgPlayerColonyDensity.Sparse => "sparse",
			RmgPlayerColonyDensity.Standard => "standard",
			RmgPlayerColonyDensity.Dense => "dense",
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

		static RmgPlayerColonyDensity ParseColonyDensity(string value) => value.ToLowerInvariant() switch
		{
			"preset" => RmgPlayerColonyDensity.Preset,
			"sparse" => RmgPlayerColonyDensity.Sparse,
			"standard" => RmgPlayerColonyDensity.Standard,
			"dense" => RmgPlayerColonyDensity.Dense,
			_ => throw new ArgumentException("Player settings neutral_colony_density must be preset, sparse, standard, or dense.")
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

		static int DensityColonies(RmgPlayerColonyDensity density, int players) => density switch
		{
			RmgPlayerColonyDensity.Sparse => players == 2 ? 8 : 12,
			RmgPlayerColonyDensity.Standard => players == 2 ? 10 : 16,
			RmgPlayerColonyDensity.Dense => players == 2 ? 20 : 24,
			_ => throw new ArgumentOutOfRangeException(nameof(density))
		};

		static RmgSymmetry AutomaticSymmetry(RmgPlayerSettings requested)
		{
			var canonical = $"schema={SchemaVersion}\nseed={requested.Seed}\npreset={PresetName(requested.Preset)}\nplayers={requested.PlayerCount}\nstream=automatic-symmetry";
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
			$"seed={settings.Seed}",
			$"players={settings.PlayerCount}",
			$"symmetry={settings.Symmetry}",
			$"archetype={settings.Archetype}",
			$"colonies={settings.NeutralColonyCount}"
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
