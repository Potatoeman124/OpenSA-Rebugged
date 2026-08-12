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
using Newtonsoft.Json;
using OpenRA.Mods.OpenSA.Rmg;

namespace OpenRA.Mods.OpenSA.UtilityCommands
{
	sealed class GenerateRmgMapCommand : IUtilityCommand
	{
		string IUtilityCommand.Name => "--generate-sa-map";
		bool IUtilityCommand.ValidateArguments(string[] args) => args.Length >= 1;

		[Desc("OUTPUT.oramap", "--seed N", "[--players 2|4]", "[--symmetry horizontal|vertical|rotational]", "[--archetype open|central-contest]", "[--neutral-colonies N]", "[--movement-validation proxy|native|both]", "[--report FILE]", "[--overwrite]", "Generate a deterministic OpenSA skirmish map.")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			try
			{
				var options = Parse(args);
				var profile = RmgProfile.Load(utility.ModData);
				var result = OpenRaRmgMapAdapter.GenerateAndSave(utility.ModData, profile, options.Settings, options.OutputPath,
					options.Overwrite, options.MovementValidationMode);
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
				throw new CommandLineException("Usage: --generate-sa-map OUTPUT.oramap --seed N [options]");

			var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			for (var i = 2; i < args.Length; i++)
			{
				if (!args[i].StartsWith("--", StringComparison.Ordinal))
					throw new CommandLineException($"Unexpected argument: {args[i]}");
				if (args[i] == "--overwrite" || args[i] == "--verbose")
				{
					flags.Add(args[i]);
					continue;
				}

				if (i + 1 >= args.Length)
					throw new CommandLineException($"Missing value for {args[i]}.");
				values[args[i]] = args[++i];
			}

			var known = new HashSet<string>(new[] { "--seed", "--players", "--tileset", "--size", "--symmetry", "--archetype", "--neutral-colonies", "--generator-version", "--movement-validation", "--report" }, StringComparer.OrdinalIgnoreCase);
			foreach (var key in values.Keys)
				if (!known.Contains(key))
					throw new CommandLineException($"Unknown option: {key}");

			if (!values.TryGetValue("--seed", out var seedText) || !ulong.TryParse(seedText, out var seed))
				throw new CommandLineException("--seed is required and must be an unsigned integer.");
			var players = ParseInt(values, "--players", 2);
			var colonies = ParseInt(values, "--neutral-colonies", players == 2 ? 10 : 16);
			var generatorVersion = ParseInt(values, "--generator-version", 1);
			if (values.TryGetValue("--tileset", out var tileset) && !string.Equals(tileset, "NORMAL", StringComparison.OrdinalIgnoreCase))
				throw new ArgumentException("Generator Version 1 supports only the NORMAL tileset.");
			if (values.TryGetValue("--size", out var size) && size != "128,128")
				throw new ArgumentException("Generator Version 1 supports only --size 128,128.");

			return new Options
			{
				OutputPath = args[1],
				ReportPath = values.TryGetValue("--report", out var report) ? report : null,
				Overwrite = flags.Contains("--overwrite"),
				MovementValidationMode = ParseMovementValidation(values.TryGetValue("--movement-validation", out var movementValidation) ? movementValidation : "both"),
				Settings = new RmgGenerationSettings
				{
					Seed = seed,
					PlayerCount = players,
					NeutralColonyCount = colonies,
					GeneratorVersion = generatorVersion,
					Symmetry = ParseSymmetry(values.TryGetValue("--symmetry", out var symmetry) ? symmetry : "horizontal"),
					Archetype = ParseArchetype(values.TryGetValue("--archetype", out var archetype) ? archetype : "open")
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
