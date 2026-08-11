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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenRA.Mods.OpenSA.Rmg;

namespace OpenRA.Mods.OpenSA.UtilityCommands
{
	sealed class FuzzRmgMapGeneratorCommand : IUtilityCommand
	{
		string IUtilityCommand.Name => "--fuzz-sa-map-generator";
		bool IUtilityCommand.ValidateArguments(string[] args) => args.Length >= 1;

		[Desc("REPORT.json", "[--seed-start N]", "[--gate-a-count N]", "[--mixed-count N]", "[--overwrite]", "Run deterministic RMG self-tests and seed campaigns without saving accepted maps.")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			try
			{
				var options = Parse(args);
				var reportPath = Path.GetFullPath(options.ReportPath);
				if (File.Exists(reportPath) && !options.Overwrite)
					throw new IOException($"Report already exists: {reportPath}. Pass --overwrite to replace it.");

				var profile = RmgProfile.Load(utility.ModData);
				var selfTestFailures = RmgGenerator.RunSelfTests(profile);
				var failures = new JArray();
				var failureReasons = new Dictionary<string, int>(StringComparer.Ordinal);
				var stopwatch = Stopwatch.StartNew();
				var total = 0;
				var accepted = 0;
				var maximumMilliseconds = 0L;
				var worstColonySpread = 0D;
				var minimumColonySeparation = double.MaxValue;

				foreach (var selfTestFailure in selfTestFailures)
					RecordFailure("SELF_TEST", selfTestFailure, null);

				var campaignSummaries = new JArray();
				foreach (var players in new[] { 2, 4 })
					foreach (var symmetry in Enum.GetValues<RmgSymmetry>())
						foreach (var archetype in Enum.GetValues<RmgArchetype>())
						{
							var beforeFailures = failures.Count;
							var beforeAccepted = accepted;
							for (var i = 0; i < options.GateACount; i++)
								RunCase(options.SeedStart + (ulong)i, players, symmetry, archetype, "gate-a");

							campaignSummaries.Add(new JObject
							{
								["campaign"] = "gate-a",
								["players"] = players,
								["symmetry"] = OpenRaRmgMapAdapter.SymmetryName(symmetry),
								["archetype"] = OpenRaRmgMapAdapter.ArchetypeName(archetype),
								["cases"] = options.GateACount,
								["accepted"] = accepted - beforeAccepted,
								["failures"] = failures.Count - beforeFailures
							});
						}

				var mixedBeforeFailures = failures.Count;
				var mixedBeforeAccepted = accepted;
				var symmetries = Enum.GetValues<RmgSymmetry>();
				var archetypes = Enum.GetValues<RmgArchetype>();
				for (var i = 0; i < options.MixedCount; i++)
				{
					var players = i % 2 == 0 ? 2 : 4;
					RunCase(options.SeedStart + 100000UL + (ulong)i, players, symmetries[i % symmetries.Length], archetypes[i / symmetries.Length % archetypes.Length], "mixed");
				}

				campaignSummaries.Add(new JObject
				{
					["campaign"] = "mixed",
					["cases"] = options.MixedCount,
					["accepted"] = accepted - mixedBeforeAccepted,
					["failures"] = failures.Count - mixedBeforeFailures
				});

				stopwatch.Stop();
				var report = new JObject
				{
					["schema_version"] = 1,
					["generator_version"] = profile.GeneratorVersion,
					["configuration_id"] = profile.ProfileId,
					["seed_start"] = options.SeedStart.ToString(),
					["self_tests_passed"] = selfTestFailures.Count == 0,
					["total_cases"] = total,
					["accepted_cases"] = accepted,
					["hard_invalid_cases"] = total - accepted,
					["same_seed_hash_mismatches"] = FailureCount("DETERMINISM"),
					["elapsed_milliseconds"] = stopwatch.ElapsedMilliseconds,
					["maximum_case_milliseconds"] = maximumMilliseconds,
					["worst_colony_assignment_spread"] = worstColonySpread,
					["minimum_colony_separation_logical"] = minimumColonySeparation == double.MaxValue ? null : minimumColonySeparation,
					["campaigns"] = campaignSummaries,
					["failure_reason_counts"] = new JObject(failureReasons.Select(kv => new JProperty(kv.Key, kv.Value))),
					["failing_seeds"] = failures
				};

				Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
				File.WriteAllText(reportPath, report.ToString(Formatting.Indented));
				Console.WriteLine($"RMG fuzz report: {reportPath}");
				Console.WriteLine($"Accepted {accepted}/{total} generated cases; self-tests: {(selfTestFailures.Count == 0 ? "pass" : "fail")}.");
				Environment.ExitCode = failures.Count == 0 ? 0 : 4;

				void RunCase(ulong seed, int players, RmgSymmetry symmetry, RmgArchetype archetype, string campaign)
				{
					total++;
					var settings = new RmgGenerationSettings
					{
						Seed = seed,
						PlayerCount = players,
						NeutralColonyCount = players == 2 ? 10 : 16,
						Symmetry = symmetry,
						Archetype = archetype
					};
					var caseTimer = Stopwatch.StartNew();
					try
					{
						var first = RmgGenerator.Generate(profile, settings);
						var second = RmgGenerator.Generate(profile, settings);
						if (first.LogicalHash != second.LogicalHash || first.ActorHash != second.ActorHash || first.GraphHash != second.GraphHash)
						{
							RecordFailure("DETERMINISM", "Same-seed hashes differ.", settings, campaign);
							return;
						}

						if (!first.Validation.Accepted)
						{
							foreach (var failure in first.Validation.HardFailures)
								RecordFailure(failure.Code, failure.Message, settings, campaign);
							return;
						}

						accepted++;
						worstColonySpread = Math.Max(worstColonySpread, first.Validation.Metrics["colony_assignment_spread"]);
						minimumColonySeparation = Math.Min(minimumColonySeparation, first.Validation.Metrics["minimum_colony_separation_logical"]);
					}
					catch (Exception e)
					{
						RecordFailure("EXCEPTION", e.Message, settings, campaign);
					}
					finally
					{
						caseTimer.Stop();
						maximumMilliseconds = Math.Max(maximumMilliseconds, caseTimer.ElapsedMilliseconds);
					}
				}

				void RecordFailure(string code, string message, RmgGenerationSettings settings, string campaign = "self-test")
				{
					failureReasons.TryGetValue(code, out var count);
					failureReasons[code] = count + 1;
					var failure = new JObject { ["campaign"] = campaign, ["code"] = code, ["message"] = message };
					if (settings != null)
					{
						failure["seed"] = settings.Seed.ToString();
						failure["players"] = settings.PlayerCount;
						failure["symmetry"] = OpenRaRmgMapAdapter.SymmetryName(settings.Symmetry);
						failure["archetype"] = OpenRaRmgMapAdapter.ArchetypeName(settings.Archetype);
					}

					failures.Add(failure);
				}

				int FailureCount(string code) => failureReasons.TryGetValue(code, out var count) ? count : 0;
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
				throw new ArgumentException("Usage: --fuzz-sa-map-generator REPORT.json [--seed-start N] [--gate-a-count N] [--mixed-count N] [--overwrite]");

			var options = new Options { ReportPath = args[1] };
			for (var i = 2; i < args.Length; i++)
			{
				if (args[i] == "--overwrite")
				{
					options.Overwrite = true;
					continue;
				}

				if (i + 1 >= args.Length)
					throw new ArgumentException($"Missing value for {args[i]}.");
				var value = args[++i];
				switch (args[i - 1])
				{
					case "--seed-start":
						if (!ulong.TryParse(value, out var seedStart))
							throw new ArgumentException("--seed-start must be an unsigned integer.");
						options.SeedStart = seedStart;
						break;
					case "--gate-a-count":
						options.GateACount = PositiveInt("--gate-a-count", value);
						break;
					case "--mixed-count":
						options.MixedCount = PositiveInt("--mixed-count", value);
						break;
					default:
						throw new ArgumentException($"Unknown option: {args[i - 1]}");
				}
			}

			return options;
		}

		static int PositiveInt(string name, string text)
		{
			if (!int.TryParse(text, out var value) || value < 1)
				throw new ArgumentException($"{name} must be a positive integer.");
			return value;
		}

		sealed class Options
		{
			public string ReportPath { get; init; }
			public ulong SeedStart { get; set; } = 1;
			public int GateACount { get; set; } = 100;
			public int MixedCount { get; set; } = 1000;
			public bool Overwrite { get; set; }
		}
	}
}
