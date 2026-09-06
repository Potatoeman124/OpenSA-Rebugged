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
using Newtonsoft.Json;
using OpenRA.Mods.OpenSA.Rmg;

namespace OpenRA.Mods.OpenSA.UtilityCommands
{
	sealed class GenerateRmgMapCommand : IUtilityCommand
	{
		string IUtilityCommand.Name => "--generate-sa-map";
		bool IUtilityCommand.ValidateArguments(string[] args) => args.Length >= 1;

		[Desc("OUTPUT.oramap", "--seed N | --player-settings FILE", "[--players 2|4]", "[--size 128,128|256,256]", "[--symmetry horizontal|vertical|rotational]", "[--archetype open|central-contest]", "[--topology off|mixed|shoreline|land-details|land-cover|battlefield-layout|parameterized-battlefield|coherent-water|natural-terrain|natural-terrain-v10]", "[--neutral-colonies N]", "[--water-amount low|standard|high]", "[--tactical-terrain low|standard|high]", "[--movement-validation proxy|native|both]", "[--report FILE]", "[--verify-repeatability]", "[--overwrite]", "Generate a deterministic OpenSA skirmish map. Schema 3 preserves 128x128 layouts; schema 4 adds 256x256 for experimental Version 10 Natural Landscape only.")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			try
			{
				var options = Parse(args);
				var profile = RmgProfile.Load(utility.ModData, options.Settings);
				var result = OpenRaRmgMapAdapter.GenerateAndSave(utility.ModData, profile, options.Settings, options.OutputPath,
					options.Overwrite, options.MovementValidationMode, options.VerifyRepeatability);
				var reportPath = options.ReportPath ?? options.OutputPath + ".report.json";
				var reportDirectory = Path.GetDirectoryName(Path.GetFullPath(reportPath));
				if (!string.IsNullOrEmpty(reportDirectory))
					Directory.CreateDirectory(reportDirectory);
				File.WriteAllText(reportPath, result.Report.ToString(Formatting.Indented));

				Console.WriteLine($"Generated: {result.OutputPath}");
				Console.WriteLine($"Report: {Path.GetFullPath(reportPath)}");
				Console.WriteLine($"Logical SHA-256: {result.Generation.LogicalHash}");
				Console.WriteLine($"OpenRA UID: {result.EngineUid}");
				Environment.ExitCode = 0;
			}
			catch (CommandLineException e)
			{
				Console.Error.WriteLine(e.Message);
				Environment.ExitCode = 1;
			}
			catch (ArgumentException e)
			{
				Console.Error.WriteLine(e.Message);
				Environment.ExitCode = 2;
			}
			catch (InvalidOperationException e)
			{
				Console.Error.WriteLine(e.Message);
				Environment.ExitCode = 4;
			}
			catch (RmgPackageValidationException e)
			{
				Console.Error.WriteLine(e.Message);
				if (e.Validation.NativeMovementValidation != null)
					Console.Error.WriteLine(e.Validation.NativeMovementValidation.ToJson().ToString(Formatting.Indented));
				Environment.ExitCode = 5;
			}
			catch (InvalidDataException e)
			{
				Console.Error.WriteLine(e.Message);
				Environment.ExitCode = 5;
			}
			catch (IOException e)
			{
				Console.Error.WriteLine(e.Message);
				Environment.ExitCode = 3;
			}
			catch (Exception e)
			{
				Console.Error.WriteLine(e);
				Environment.ExitCode = 6;
			}
		}

		static Options Parse(string[] args)
		{
			if (args.Length < 2)
				throw new CommandLineException("Usage: --generate-sa-map OUTPUT.oramap (--seed N [options] | --player-settings FILE)");

			var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			for (var i = 2; i < args.Length; i++)
			{
				if (!args[i].StartsWith("--", StringComparison.Ordinal))
					throw new CommandLineException($"Unexpected argument: {args[i]}");
				if (args[i] == "--overwrite" || args[i] == "--verbose" || args[i] == "--verify-repeatability")
				{
					flags.Add(args[i]);
					continue;
				}

				if (i + 1 >= args.Length)
					throw new CommandLineException($"Missing value for {args[i]}.");
				values[args[i]] = args[++i];
			}

			var known = new HashSet<string>(new[] { "--seed", "--player-settings", "--players", "--tileset", "--size", "--symmetry", "--archetype", "--topology", "--neutral-colonies", "--water-amount", "--tactical-terrain", "--generator-version", "--movement-validation", "--report" }, StringComparer.OrdinalIgnoreCase);
			foreach (var key in values.Keys)
				if (!known.Contains(key))
					throw new CommandLineException($"Unknown option: {key}");

			if (values.TryGetValue("--player-settings", out var playerSettingsPath))
			{
				var incompatible = new[]
				{
					"--seed", "--players", "--tileset", "--size", "--symmetry", "--archetype",
					"--topology", "--neutral-colonies", "--water-amount", "--tactical-terrain", "--generator-version"
				}
				.Where(values.ContainsKey).ToArray();
				if (incompatible.Length > 0)
					throw new CommandLineException($"--player-settings cannot be combined with normalized generator option(s): {string.Join(", ", incompatible)}.");

				var resolution = RmgPlayerSettingsContract.Load(playerSettingsPath);
				return new Options
				{
					OutputPath = args[1],
					ReportPath = values.TryGetValue("--report", out var playerReport) ? playerReport : null,
					Overwrite = flags.Contains("--overwrite"),
					VerifyRepeatability = flags.Contains("--verify-repeatability"),
					MovementValidationMode = ParseMovementValidation(values.TryGetValue("--movement-validation", out var playerMovementValidation) ? playerMovementValidation : "both"),
					Settings = resolution.Normalized
				};
			}

			if (!values.TryGetValue("--seed", out var seedText) || !ulong.TryParse(seedText, out var seed))
				throw new CommandLineException("Either --seed or --player-settings is required; --seed must be an unsigned integer.");
			var players = ParseInt(values, "--players", 2);
			var mapSize = RmgPlayerSettingsContract.ParseMapSize(values.TryGetValue("--size", out var size) ? size : "128,128");
			var colonies = ParseInt(values, "--neutral-colonies", (players == 2 ? 10 : 16) * (mapSize == 256 ? 3 : 1));
			var topology = ParseTopology(values.TryGetValue("--topology", out var topologyValue) ? topologyValue : "off");
			var generatorVersion = topology switch
			{
				RmgTopologyPreset.Mixed => 2,
				RmgTopologyPreset.Shoreline => 3,
				RmgTopologyPreset.LandDetails => 4,
				RmgTopologyPreset.BattlefieldLayout => 6,
				RmgTopologyPreset.ParameterizedBattlefield => 7,
				RmgTopologyPreset.CoherentWater => 8,
				RmgTopologyPreset.NaturalTerrain => 9,
				RmgTopologyPreset.NaturalTerrainV10 => 10,
				RmgTopologyPreset.LandCover => 5,
				_ => 1
			};
			if (values.TryGetValue("--generator-version", out var versionText) &&
				(!int.TryParse(versionText, out var requestedVersion) || requestedVersion != generatorVersion))
				throw new ArgumentException($"--generator-version must be {generatorVersion} when --topology is {TopologyName(topology)}.");
			if (values.TryGetValue("--tileset", out var tileset) && !string.Equals(tileset, "NORMAL", StringComparison.OrdinalIgnoreCase))
				throw new ArgumentException("The RMG supports only the NORMAL tileset.");
			if (mapSize == 256 && topology != RmgTopologyPreset.NaturalTerrainV10)
				throw new ArgumentException("256x256 is supported only with natural-terrain-v10.");

			return new Options
			{
				OutputPath = args[1],
				ReportPath = values.TryGetValue("--report", out var report) ? report : null,
				Overwrite = flags.Contains("--overwrite"),
				VerifyRepeatability = flags.Contains("--verify-repeatability"),
				MovementValidationMode = ParseMovementValidation(values.TryGetValue("--movement-validation", out var movementValidation) ? movementValidation : "both"),
				Settings = new RmgGenerationSettings
				{
					Seed = seed,
					MapSize = mapSize,
					PlayerCount = players,
					NeutralColonyCount = colonies,
					GeneratorVersion = generatorVersion,
					TopologyPreset = topology,
					Symmetry = ParseSymmetry(values.TryGetValue("--symmetry", out var symmetry) ? symmetry : "horizontal"),
					Archetype = ParseArchetype(values.TryGetValue("--archetype", out var archetype) ? archetype : "open"),
					WaterAmount = ParseParameterLevel(values.TryGetValue("--water-amount", out var waterAmount) ? waterAmount : "standard"),
					TacticalTerrain = ParseParameterLevel(values.TryGetValue("--tactical-terrain", out var tacticalTerrain) ? tacticalTerrain : "standard"),
					LayoutFamily = topology switch
					{
						RmgTopologyPreset.NaturalTerrain or RmgTopologyPreset.NaturalTerrainV10 => RmgLayoutFamily.NaturalLandscape,
						RmgTopologyPreset.CoherentWater => RmgLayoutFamily.StructuredCompetitive,
						_ => RmgLayoutFamily.ArtificialBattlefield
					}
				}
			};
		}

		static int ParseInt(Dictionary<string, string> values, string key, int fallback)
		{
			if (!values.TryGetValue(key, out var text))
				return fallback;
			if (!int.TryParse(text, out var result))
				throw new CommandLineException($"{key} must be an integer.");
			return result;
		}

		public static RmgSymmetry ParseSymmetry(string value) => value.ToLowerInvariant() switch
		{
			"horizontal" => RmgSymmetry.MirrorHorizontal,
			"vertical" => RmgSymmetry.MirrorVertical,
			"rotational" => RmgSymmetry.Rotate180,
			_ => throw new ArgumentException("Symmetry must be horizontal, vertical, or rotational.")
		};

		public static RmgArchetype ParseArchetype(string value) => value.ToLowerInvariant() switch
		{
			"open" => RmgArchetype.Open,
			"central-contest" => RmgArchetype.CentralContest,
			_ => throw new ArgumentException("Archetype must be open or central-contest.")
		};

		public static RmgParameterLevel ParseParameterLevel(string value) => value.ToLowerInvariant() switch
		{
			"low" => RmgParameterLevel.Low,
			"standard" => RmgParameterLevel.Standard,
			"high" => RmgParameterLevel.High,
			_ => throw new ArgumentException("Parameter level must be low, standard, or high.")
		};

		public static RmgTopologyPreset ParseTopology(string value) => value.ToLowerInvariant() switch
		{
			"off" => RmgTopologyPreset.Off,
			"mixed" => RmgTopologyPreset.Mixed,
			"shoreline" => RmgTopologyPreset.Shoreline,
			"land-details" => RmgTopologyPreset.LandDetails,
			"battlefield-layout" => RmgTopologyPreset.BattlefieldLayout,
			"parameterized-battlefield" => RmgTopologyPreset.ParameterizedBattlefield,
			"coherent-water" => RmgTopologyPreset.CoherentWater,
			"natural-terrain" => RmgTopologyPreset.NaturalTerrain,
			"natural-terrain-v10" => RmgTopologyPreset.NaturalTerrainV10,
			"land-cover" => RmgTopologyPreset.LandCover,
			_ => throw new ArgumentException("Topology must be off, mixed, shoreline, land-details, land-cover, battlefield-layout, parameterized-battlefield, coherent-water, natural-terrain, or natural-terrain-v10.")
		};

		public static string TopologyName(RmgTopologyPreset value) => value switch
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
			_ => throw new ArgumentOutOfRangeException(nameof(value))
		};

		public static RmgMovementValidationMode ParseMovementValidation(string value) => value.ToLowerInvariant() switch
		{
			"proxy" => RmgMovementValidationMode.Proxy,
			"native" => RmgMovementValidationMode.Native,
			"both" => RmgMovementValidationMode.Both,
			_ => throw new ArgumentException("Movement validation must be proxy, native, or both.")
		};

		sealed class Options
		{
			public string OutputPath { get; init; }
			public string ReportPath { get; init; }
			public bool Overwrite { get; init; }
			public bool VerifyRepeatability { get; init; }
			public RmgMovementValidationMode MovementValidationMode { get; init; }
			public RmgGenerationSettings Settings { get; init; }
		}

		public sealed class CommandLineException : Exception
		{
			public CommandLineException(string message)
				: base(message) { }
		}
	}
}
