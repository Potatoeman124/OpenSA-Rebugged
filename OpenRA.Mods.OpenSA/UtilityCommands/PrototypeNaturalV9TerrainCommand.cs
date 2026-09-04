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
using System.Globalization;
using System.IO;
using OpenRA.Mods.OpenSA.Rmg.NaturalPrototype;

namespace OpenRA.Mods.OpenSA.UtilityCommands
{
	sealed class PrototypeNaturalV9TerrainCommand : IUtilityCommand
	{
		string IUtilityCommand.Name => "--prototype-natural-v9-terrain";
		bool IUtilityCommand.ValidateArguments(string[] args) => args.Length >= 1;

		[Desc("OUTPUT_DIRECTORY", "--root-seed N", "--candidate-index N", "--variant correlated-field-baseline|correlated-field-with-basin-potential", "[--original-surface-relations true|false]", "[--overwrite]", "[--self-test]", "Export one deterministic NORMAL 128x128 Natural V9 terrain-only prototype candidate. This command does not create a playable map.")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			try
			{
				var options = Parse(args);
				if (options.SelfTest)
				{
					var failures = NaturalTerrainPrototypeGenerator.RunSelfTests();
					if (failures.Count > 0)
						throw new InvalidOperationException("Natural V9 Step 2 self-tests failed:\n" + string.Join("\n", failures));
					Console.WriteLine("Natural V9 Step 2 terrain prototype self-tests passed.");
				}

				var stopwatch = Stopwatch.StartNew();
				var candidate = NaturalTerrainPrototypeGenerator.Generate(new NaturalTerrainPrototypeSettings
				{
					RootSeed = options.RootSeed,
					CandidateIndex = options.CandidateIndex,
					Variant = options.Variant,
					OriginalSurfaceRelations = options.OriginalSurfaceRelations
				});
				var generationMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
				stopwatch.Restart();
				var manifest = NaturalTerrainPrototypeExporter.Export(candidate, options.OutputDirectory, options.Overwrite);
				var exportMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
				Console.WriteLine($"Exported terrain-only candidate: {Path.GetFullPath(options.OutputDirectory)}");
				Console.WriteLine($"Semantic SHA-256: {manifest["semantic_sha256"]}");
				Console.WriteLine("Generation milliseconds: " + generationMilliseconds.ToString("F3", CultureInfo.InvariantCulture));
				Console.WriteLine("Export milliseconds: " + exportMilliseconds.ToString("F3", CultureInfo.InvariantCulture));
				Environment.ExitCode = 0;
			}
			catch (Exception e) when (e is ArgumentException || e is IOException || e is InvalidOperationException)
			{
				Console.Error.WriteLine(e.Message);
				Environment.ExitCode = 1;
			}
		}

		static Options Parse(string[] args)
		{
			if (args.Length < 2)
				throw new ArgumentException("Usage: --prototype-natural-v9-terrain OUTPUT_DIRECTORY --root-seed N --candidate-index N --variant ID [--original-surface-relations true|false] [--overwrite] [--self-test]");
			var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			var overwrite = false;
			var selfTest = false;
			for (var i = 2; i < args.Length; i++)
			{
				if (args[i] == "--overwrite")
				{
					overwrite = true;
					continue;
				}

				if (args[i] == "--self-test")
				{
					selfTest = true;
					continue;
				}

				if (i + 1 >= args.Length)
					throw new ArgumentException($"Missing value for {args[i]}.");
				values[args[i]] = args[++i];
			}

			foreach (var key in values.Keys)
				if (key != "--root-seed" && key != "--candidate-index" && key != "--variant" && key != "--original-surface-relations")
					throw new ArgumentException($"Unknown option: {key}");
			if (!values.TryGetValue("--root-seed", out var rootText) || !ulong.TryParse(rootText, out var rootSeed))
				throw new ArgumentException("--root-seed must be an unsigned integer.");
			if (!values.TryGetValue("--candidate-index", out var indexText) || !int.TryParse(indexText, out var candidateIndex) || candidateIndex < 0)
				throw new ArgumentException("--candidate-index must be a non-negative integer.");
			if (!values.TryGetValue("--variant", out var variantText))
				throw new ArgumentException("--variant is required.");
			var variant = variantText switch
			{
				"correlated-field-baseline" => NaturalTerrainPrototypeVariant.CorrelatedFieldBaseline,
				"correlated-field-with-basin-potential" => NaturalTerrainPrototypeVariant.CorrelatedFieldWithBasinPotential,
				_ => throw new ArgumentException("--variant must be correlated-field-baseline or correlated-field-with-basin-potential.")
			};
			var originalSurfaceRelations = true;
			if (values.TryGetValue("--original-surface-relations", out var relationsText) &&
				!bool.TryParse(relationsText, out originalSurfaceRelations))
				throw new ArgumentException("--original-surface-relations must be true or false.");

			return new Options
			{
				OutputDirectory = args[1],
				RootSeed = rootSeed,
				CandidateIndex = candidateIndex,
				Variant = variant,
				OriginalSurfaceRelations = originalSurfaceRelations,
				Overwrite = overwrite,
				SelfTest = selfTest
			};
		}

		sealed class Options
		{
			public string OutputDirectory { get; init; }
			public ulong RootSeed { get; init; }
			public int CandidateIndex { get; init; }
			public NaturalTerrainPrototypeVariant Variant { get; init; }
			public bool OriginalSurfaceRelations { get; init; }
			public bool Overwrite { get; init; }
			public bool SelfTest { get; init; }
		}
	}
}
