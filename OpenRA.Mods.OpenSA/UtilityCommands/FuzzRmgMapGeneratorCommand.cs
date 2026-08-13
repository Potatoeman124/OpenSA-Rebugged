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
		const int MaximumGateAAttemptMultiplier = 10;

		static readonly IReadOnlyDictionary<int, int[]> ColonyCounts = new Dictionary<int, int[]>
		{
			[2] = new[] { 8, 10, 14, 20 },
			[4] = new[] { 12, 16, 20, 24 }
		};

		enum RuntimeSampleOutcome { Accepted, Rejected, Failed }

		string IUtilityCommand.Name => "--fuzz-sa-map-generator";
		bool IUtilityCommand.ValidateArguments(string[] args) => args.Length >= 1;

		[Desc("REPORT.json", "[--seed-start N]", "[--gate-a-count N]", "[--mixed-count N]",
			"[--colony-count-campaign N]", "[--movement-validation proxy|native|both]",
			"[--topology off|mixed]", "[--runtime-sample-rate N]", "[--preserve-failures]", "[--overwrite]",
			"Run deterministic RMG self-tests, legal colony-count campaigns, and bounded package/native samples.")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			try
			{
				var options = Parse(args);
				var reportPath = Path.GetFullPath(options.ReportPath);
				if (File.Exists(reportPath) && !options.Overwrite)
					throw new IOException($"Report already exists: {reportPath}. Pass --overwrite to replace it.");

				var reportDirectory = Path.GetDirectoryName(reportPath);
				var sampleDirectory = Path.Combine(reportDirectory, "runtime-samples");
				var failureDirectory = Path.Combine(reportDirectory, "failures");
				var profile = RmgProfile.Load(utility.ModData, options.TopologyPreset);
				var selfTestFailures = RmgGenerator.RunSelfTests(profile)
					.Concat(NativeMovementValidator.RunSelfTests())
					.ToList();
				var failures = new JArray();
				var failureReasons = new Dictionary<string, int>(StringComparer.Ordinal);
				var rejections = new JArray();
				var rejectionReasons = new Dictionary<string, int>(StringComparer.Ordinal);
				var runtimeSamples = new JArray();
				var stopwatch = Stopwatch.StartNew();
				var total = 0;
				var accepted = 0;
				var logicalHardInvalidCases = 0;
				var acceptedHardInvalidCases = 0;
				var generationExceptionCases = 0;
				var blockingFailureCases = 0;
				var maximumMilliseconds = 0L;
				var worstColonySpread = 0D;
				var minimumColonySeparation = double.MaxValue;
				var runtimeSampled = 0;
				var runtimeAccepted = 0;
				var runtimeRejected = 0;
				var proxyFalseNegativeCells = 0L;
				var proxyFalsePositiveCells = 0L;
				var topologyResultDisagreements = 0;
				var overallResultDisagreements = 0;
				var minimumNativeRouteWidth = int.MaxValue;
				var caseMilliseconds = new List<double>();
				var generationMilliseconds = new List<double>();
				var retryCounts = new List<double>();
				var repairCounts = new List<double>();
				var packagePerformance = new List<RmgPackagePerformance>();
				var forcedBoundaryRuntimeSamples = 0;

				foreach (var selfTestFailure in selfTestFailures)
					RecordFailure("SELF_TEST", selfTestFailure, null, null);

				var campaignSummaries = new JArray();
				foreach (var players in new[] { 2, 4 })
					foreach (var symmetry in Enum.GetValues<RmgSymmetry>())
						foreach (var archetype in Enum.GetValues<RmgArchetype>())
						{
							var beforeFailures = failures.Count;
							var beforeRejections = rejections.Count;
							var beforeAccepted = accepted;
							var attempted = 0;
							while (attempted < options.GateACount ||
								(accepted - beforeAccepted < options.GateACount && attempted < options.GateACount * MaximumGateAAttemptMultiplier))
							{
								RunCase(options.SeedStart + (ulong)attempted, players, players == 2 ? 10 : 16,
									symmetry, archetype, attempted < options.GateACount ? "gate-a" : "gate-a-continuation");
								attempted++;
							}

							if (accepted - beforeAccepted < options.GateACount)
								RecordFailure("GATE_A_ACCEPTANCE",
									$"Only {accepted - beforeAccepted}/{options.GateACount} required accepted cases were found in {attempted} attempts.",
									null, null, "gate-a");

							campaignSummaries.Add(CampaignSummary("gate-a", players, players == 2 ? 10 : 16,
								symmetry, archetype, attempted, beforeAccepted, beforeFailures, beforeRejections,
								options.GateACount));
						}

				var colonyProfile = 0;
				foreach (var players in new[] { 2, 4 })
					foreach (var colonyCount in ColonyCounts[players])
						foreach (var symmetry in Enum.GetValues<RmgSymmetry>())
							foreach (var archetype in Enum.GetValues<RmgArchetype>())
							{
								var beforeFailures = failures.Count;
								var beforeRejections = rejections.Count;
								var beforeAccepted = accepted;
								var profileSeed = options.SeedStart + 1000000UL + (ulong)(colonyProfile++ * options.ColonyCountCampaign);
								var needsBoundaryRuntimeSample = true;
								for (var i = 0; i < options.ColonyCountCampaign; i++)
									if (RunCase(profileSeed + (ulong)i, players, colonyCount, symmetry, archetype,
										"colony-count", needsBoundaryRuntimeSample))
										needsBoundaryRuntimeSample = false;

								campaignSummaries.Add(CampaignSummary("colony-count", players, colonyCount,
									symmetry, archetype, options.ColonyCountCampaign, beforeAccepted, beforeFailures, beforeRejections));
							}

				var mixedBeforeFailures = failures.Count;
				var mixedBeforeRejections = rejections.Count;
				var mixedBeforeAccepted = accepted;
				var symmetries = Enum.GetValues<RmgSymmetry>();
				var archetypes = Enum.GetValues<RmgArchetype>();
				for (var i = 0; i < options.MixedCount; i++)
				{
					var players = i % 2 == 0 ? 2 : 4;
					var counts = ColonyCounts[players];
					RunCase(options.SeedStart + 100000UL + (ulong)i, players, counts[i % counts.Length],
						symmetries[i % symmetries.Length], archetypes[i / symmetries.Length % archetypes.Length], "mixed");
				}

				campaignSummaries.Add(new JObject
				{
					["campaign"] = "mixed",
					["cases"] = options.MixedCount,
					["accepted"] = accepted - mixedBeforeAccepted,
					["rejections"] = rejections.Count - mixedBeforeRejections,
					["failures"] = failures.Count - mixedBeforeFailures
				});

				stopwatch.Stop();
				var report = new JObject
				{
					["schema_version"] = 4,
					["generator_version"] = profile.GeneratorVersion,
					["configuration_id"] = profile.ProfileId,
					["configuration_version"] = profile.ConfigurationVersion,
					["topology_preset"] = OpenRaRmgMapAdapter.TopologyName(options.TopologyPreset),
					["seed_start"] = options.SeedStart.ToString(),
					["movement_validation"] = OpenRaRmgMapAdapter.MovementValidationName(options.MovementValidationMode),
					["runtime_sample_rate"] = options.RuntimeSampleRate,
					["preserve_failures"] = options.PreserveFailures,
					["self_tests_passed"] = selfTestFailures.Count == 0,
					["self_test_failures"] = new JArray(selfTestFailures),
					["legal_colony_counts"] = new JObject(
						ColonyCounts.Select(kv => new JProperty($"{kv.Key}p", new JArray(kv.Value)))),
					["total_cases"] = total,
					["accepted_cases"] = accepted,
					["unaccepted_cases"] = total - accepted,
					["expected_generation_rejections"] = rejections.Count,
					["blocking_failure_cases"] = blockingFailureCases,
					["logical_hard_invalid_cases"] = logicalHardInvalidCases,
					["accepted_hard_invalid_cases"] = acceptedHardInvalidCases,
					["generation_exception_cases"] = generationExceptionCases,
					["acceptance_rate_percent"] = total == 0 ? 0 : Math.Round(100D * accepted / total, 6),
					["rejection_rate_percent"] = total == 0 ? 0 : Math.Round(100D * rejections.Count / total, 6),
					["blocking_failure_rate_percent"] = total == 0 ? 0 : Math.Round(100D * blockingFailureCases / total, 6),
					["same_seed_hash_mismatches"] = FailureCount("DETERMINISM"),
					["elapsed_milliseconds"] = stopwatch.ElapsedMilliseconds,
					["maximum_case_milliseconds"] = maximumMilliseconds,
					["worst_colony_assignment_spread"] = worstColonySpread,
					["minimum_colony_separation_logical"] = minimumColonySeparation == double.MaxValue ? null : minimumColonySeparation,
					["retry_statistics"] = new JObject
					{
						["distribution"] = Distribution(retryCounts),
						["maps_requiring_retry"] = retryCounts.Count(value => value > 0),
						["rejected_attempts_before_acceptance"] = retryCounts.Sum()
					},
					["repair_statistics"] = new JObject
					{
						["distribution"] = Distribution(repairCounts),
						["maps_requiring_repair"] = repairCounts.Count(value => value > 0),
						["total_repair_operations"] = repairCounts.Sum()
					},
					["performance"] = new JObject
					{
						["logical_generation_ms"] = Distribution(generationMilliseconds),
						["complete_fuzz_case_ms"] = Distribution(caseMilliseconds),
						["package_total_ms"] = Distribution(packagePerformance.Select(value => value.TotalMilliseconds)),
						["materialization_and_save_ms"] = Distribution(packagePerformance.Select(value => value.MaterializationSaveMilliseconds)),
						["package_reload_and_metadata_ms"] = Distribution(packagePerformance.Select(value => value.PackageReloadMetadataMilliseconds)),
						["yaml_lint_ms"] = Distribution(packagePerformance.Select(value => value.YamlLintMilliseconds)),
						["native_movement_validation_ms"] = Distribution(packagePerformance.Select(value => value.NativeMovementValidationMilliseconds)),
						["identity_hash_ms"] = Distribution(packagePerformance.Select(value => value.IdentityHashMilliseconds)),
						["note"] = "Logical generation samples count each deterministic invocation. Package totals include the adapter's internal logical repeatability generation."
					},
					["runtime_package_campaign"] = new JObject
					{
						["sampled_cases"] = runtimeSampled,
						["forced_boundary_samples"] = forcedBoundaryRuntimeSamples,
						["accepted_cases"] = runtimeAccepted,
						["rejected_candidates"] = runtimeRejected,
						["package_hash_mismatches"] = FailureCount("PACKAGE_DETERMINISM"),
						["yaml_lint_failures"] = FailureCount("YAML_LINT"),
						["proxy_false_negative_cells"] = proxyFalseNegativeCells,
						["proxy_false_positive_cells"] = proxyFalsePositiveCells,
						["topology_result_disagreements"] = topologyResultDisagreements,
						["overall_result_disagreements"] = overallResultDisagreements,
						["minimum_native_route_width"] = minimumNativeRouteWidth == int.MaxValue ? null : minimumNativeRouteWidth,
						["samples"] = runtimeSamples
					},
					["campaigns"] = campaignSummaries,
					["rejection_reason_counts"] = new JObject(rejectionReasons.Select(kv => new JProperty(kv.Key, kv.Value))),
					["rejected_seeds"] = rejections,
					["failure_reason_counts"] = new JObject(failureReasons.Select(kv => new JProperty(kv.Key, kv.Value))),
					["failing_seeds"] = failures
				};

				Directory.CreateDirectory(reportDirectory);
				File.WriteAllText(reportPath, report.ToString(Formatting.Indented) + Environment.NewLine);
				Console.WriteLine($"RMG fuzz report: {reportPath}");
				Console.WriteLine($"Accepted {accepted}/{total} generated cases with {rejections.Count} bounded rejections and {blockingFailureCases} blocking case failures; " +
					$"self-tests: {(selfTestFailures.Count == 0 ? "pass" : "fail")}; package/native samples: {runtimeAccepted}/{runtimeSampled}.");
				Environment.ExitCode = failures.Count == 0 ? 0 : 4;

				bool RunCase(ulong seed, int players, int colonyCount, RmgSymmetry symmetry, RmgArchetype archetype,
					string campaign, bool forceRuntimeSample = false)
				{
					total++;
					var settings = new RmgGenerationSettings
					{
						Seed = seed,
						PlayerCount = players,
						NeutralColonyCount = colonyCount,
						Symmetry = symmetry,
						Archetype = archetype,
						GeneratorVersion = profile.GeneratorVersion,
						TopologyPreset = options.TopologyPreset
					};
					var caseTimer = Stopwatch.StartNew();
					try
					{
						var generationTimer = Stopwatch.StartNew();
						var first = RmgGenerator.Generate(profile, settings);
						generationTimer.Stop();
						generationMilliseconds.Add(generationTimer.Elapsed.TotalMilliseconds);
						generationTimer.Restart();
						var second = RmgGenerator.Generate(profile, settings);
						generationTimer.Stop();
						generationMilliseconds.Add(generationTimer.Elapsed.TotalMilliseconds);
						if (first.LogicalHash != second.LogicalHash || first.ActorHash != second.ActorHash || first.GraphHash != second.GraphHash)
						{
							blockingFailureCases++;
							RecordFailure("DETERMINISM", "Same-seed hashes differ.", settings, first, campaign);
							return false;
						}

						if (!first.Validation.Accepted)
						{
							blockingFailureCases++;
							logicalHardInvalidCases++;
							foreach (var failure in first.Validation.HardFailures)
								RecordFailure(failure.Code, failure.Message, settings, first, campaign);
							return false;
						}

						retryCounts.Add(first.Map.RetryCount);
						repairCounts.Add(first.Map.RepairCount);
						var shouldRunRuntimeSample = options.RuntimeSampleRate > 0 &&
							(forceRuntimeSample || total % options.RuntimeSampleRate == 0);
						if (shouldRunRuntimeSample)
						{
							var runtimeOutcome = RunRuntimeSample(settings, first, campaign, forceRuntimeSample);
							if (runtimeOutcome != RuntimeSampleOutcome.Accepted)
							{
								if (runtimeOutcome == RuntimeSampleOutcome.Failed)
									blockingFailureCases++;
								return false;
							}
						}

						accepted++;
						worstColonySpread = Math.Max(worstColonySpread, first.Validation.Metrics["colony_assignment_spread"]);
						minimumColonySeparation = Math.Min(minimumColonySeparation, first.Validation.Metrics["minimum_colony_separation_logical"]);
						return true;
					}
					catch (RmgGenerationRejectedException e)
					{
						RecordRejection(e.RejectionCode, e.Message, settings, campaign);
						return false;
					}
					catch (Exception e)
					{
						blockingFailureCases++;
						generationExceptionCases++;
						RecordFailure("EXCEPTION", e.Message, settings, null, campaign);
						return false;
					}
					finally
					{
						caseTimer.Stop();
						caseMilliseconds.Add(caseTimer.Elapsed.TotalMilliseconds);
						maximumMilliseconds = Math.Max(maximumMilliseconds, caseTimer.ElapsedMilliseconds);
					}
				}

				RuntimeSampleOutcome RunRuntimeSample(RmgGenerationSettings settings, RmgGenerationResult generation, string campaign,
					bool forcedBoundary)
				{
					runtimeSampled++;
					Directory.CreateDirectory(sampleDirectory);
					var stem = $"{campaign}-{settings.PlayerCount}p-{settings.NeutralColonyCount}c-{OpenRaRmgMapAdapter.SymmetryName(settings.Symmetry)}-{OpenRaRmgMapAdapter.ArchetypeName(settings.Archetype)}-{settings.Seed}";
					var firstPath = Path.Combine(sampleDirectory, stem + ".oramap");
					var repeatPath = Path.Combine(sampleDirectory, stem + "-repeat.oramap");
					var passed = false;
					try
					{
						var first = OpenRaRmgMapAdapter.GenerateAndSave(utility.ModData, profile, settings, firstPath, true, options.MovementValidationMode);
						var repeat = OpenRaRmgMapAdapter.GenerateAndSave(utility.ModData, profile, settings, repeatPath, true, options.MovementValidationMode);
						packagePerformance.Add(first.Performance);
						packagePerformance.Add(repeat.Performance);
						if (first.CanonicalMapHash != repeat.CanonicalMapHash || first.EngineUid != repeat.EngineUid)
						{
							RecordFailure("PACKAGE_DETERMINISM", "Repeated package UID or canonical map hash differs.", settings, generation, campaign,
								firstPath, first.NativeMovementValidation);
							return RuntimeSampleOutcome.Failed;
						}

						var native = first.NativeMovementValidation;
						if (options.MovementValidationMode != RmgMovementValidationMode.Proxy && native == null)
						{
							RecordFailure("NATIVE_VALIDATION_MISSING", "Native movement validation was requested but no result was returned.",
								settings, generation, campaign, firstPath);
							return RuntimeSampleOutcome.Failed;
						}

						if (native != null)
						{
							proxyFalseNegativeCells += native.ProxyFalseNegativeCells;
							proxyFalsePositiveCells += native.ProxyFalsePositiveCells;
							if (native.ProxyAccepted != native.NativeStartConnectivityAccepted)
								topologyResultDisagreements++;
							if (native.ProxyAccepted != native.Accepted)
								overallResultDisagreements++;
							minimumNativeRouteWidth = Math.Min(minimumNativeRouteWidth, native.MinimumUsableRouteWidth);
						}

						runtimeSamples.Add(new JObject
						{
							["campaign"] = campaign,
							["seed"] = settings.Seed.ToString(),
							["players"] = settings.PlayerCount,
							["neutral_colonies"] = settings.NeutralColonyCount,
							["symmetry"] = OpenRaRmgMapAdapter.SymmetryName(settings.Symmetry),
							["archetype"] = OpenRaRmgMapAdapter.ArchetypeName(settings.Archetype),
							["forced_boundary_sample"] = forcedBoundary,
							["logical_hash"] = generation.LogicalHash,
							["actor_hash"] = generation.ActorHash,
							["graph_hash"] = generation.GraphHash,
							["engine_uid"] = first.EngineUid,
							["canonical_map_hash"] = first.CanonicalMapHash,
							["yaml_lint"] = "passed",
							["native_accepted"] = native?.Accepted,
							["native_minimum_route_width"] = native?.MinimumUsableRouteWidth,
							["retry_count"] = generation.Map.RetryCount,
							["repair_count"] = generation.Map.RepairCount,
							["obstacle_density_percent"] = generation.Validation.Metrics["obstacle_density_percent"],
							["proxy_false_negative_cells"] = native?.ProxyFalseNegativeCells,
							["proxy_false_positive_cells"] = native?.ProxyFalsePositiveCells,
							["performance"] = first.Performance.ToJson()
						});
						runtimeAccepted++;
						if (forcedBoundary)
							forcedBoundaryRuntimeSamples++;
						passed = true;
						return RuntimeSampleOutcome.Accepted;
					}
					catch (RmgPackageValidationException e)
					{
						var native = e.Validation.NativeMovementValidation;
						packagePerformance.Add(e.Validation.Performance);
						if (e.Validation.YamlLintErrors.Length == 0 && native != null && !native.Accepted)
						{
							runtimeRejected++;
							var nativeDebugPath = PreserveFailureMap(settings, stem);
							RecordNativeRejection("NATIVE_CONFORMANCE", e.Message, settings, generation,
								campaign, nativeDebugPath, native, e.Validation);
							return RuntimeSampleOutcome.Rejected;
						}

						var code = e.Validation.YamlLintErrors.Length > 0 ? "YAML_LINT" : "NATIVE_MOVEMENT";
						var preservedPath = PreserveFailureMap(settings, stem);
						RecordFailure(code, e.Message, settings, generation, campaign, preservedPath, native, e.Validation);
						return RuntimeSampleOutcome.Failed;
					}
					catch (Exception e)
					{
						var preservedPath = PreserveFailureMap(settings, stem);
						RecordFailure("RUNTIME_SAMPLE", e.Message, settings, generation, campaign, preservedPath);
						return RuntimeSampleOutcome.Failed;
					}
					finally
					{
						if (passed || !options.PreserveFailures)
						{
							if (File.Exists(firstPath))
								File.Delete(firstPath);
							if (File.Exists(repeatPath))
								File.Delete(repeatPath);
						}
					}
				}

				string PreserveFailureMap(RmgGenerationSettings settings, string stem)
				{
					if (!options.PreserveFailures)
						return null;

					try
					{
						Directory.CreateDirectory(failureDirectory);
						var path = Path.Combine(failureDirectory, stem + ".oramap");
						OpenRaRmgMapAdapter.GenerateAndSave(utility.ModData, profile, settings, path, true, RmgMovementValidationMode.Proxy);
						return path;
					}
					catch
					{
						return null;
					}
				}

				JObject CampaignSummary(string campaign, int players, int colonyCount, RmgSymmetry symmetry,
					RmgArchetype archetype, int cases, int beforeAccepted, int beforeFailures, int beforeRejections,
					int? requiredAcceptances = null)
				{
					return new JObject
					{
						["campaign"] = campaign,
						["players"] = players,
						["neutral_colonies"] = colonyCount,
						["symmetry"] = OpenRaRmgMapAdapter.SymmetryName(symmetry),
						["archetype"] = OpenRaRmgMapAdapter.ArchetypeName(archetype),
						["cases"] = cases,
						["initial_consecutive_cases"] = requiredAcceptances,
						["required_acceptances"] = requiredAcceptances,
						["accepted"] = accepted - beforeAccepted,
						["rejections"] = rejections.Count - beforeRejections,
						["failures"] = failures.Count - beforeFailures
					};
				}

				void RecordRejection(string code, string message, RmgGenerationSettings settings, string campaign)
				{
					rejectionReasons.TryGetValue(code, out var count);
					rejectionReasons[code] = count + 1;
					rejections.Add(new JObject
					{
						["campaign"] = campaign,
						["code"] = code,
						["message"] = message,
						["seed"] = settings.Seed.ToString(),
						["players"] = settings.PlayerCount,
						["neutral_colonies"] = settings.NeutralColonyCount,
						["symmetry"] = OpenRaRmgMapAdapter.SymmetryName(settings.Symmetry),
						["archetype"] = OpenRaRmgMapAdapter.ArchetypeName(settings.Archetype),
						["topology_preset"] = OpenRaRmgMapAdapter.TopologyName(settings.TopologyPreset),
						["reproduce"] =
							$"powershell -ExecutionPolicy Bypass -File .\\scripts\\rmg\\Invoke-MapGenerator.ps1 -Seed {settings.Seed} -Players {settings.PlayerCount} -NeutralColonies {settings.NeutralColonyCount} -Symmetry {OpenRaRmgMapAdapter.SymmetryName(settings.Symmetry)} -Archetype {OpenRaRmgMapAdapter.ArchetypeName(settings.Archetype)} -Topology {OpenRaRmgMapAdapter.TopologyName(settings.TopologyPreset)} -MovementValidation both -Overwrite"
					});
				}

				void RecordNativeRejection(string code, string message, RmgGenerationSettings settings,
					RmgGenerationResult generation, string campaign, string mapPath,
					RmgNativeMovementValidationResult native, RmgPackageValidationResult package)
				{
					RecordRejection(code, message, settings, campaign);
					var rejection = (JObject)rejections.Last;
					rejection["logical_hash"] = generation.LogicalHash;
					rejection["actor_hash"] = generation.ActorHash;
					rejection["graph_hash"] = generation.GraphHash;
					rejection["proxy"] = generation.Validation.ToJson();
					rejection["native"] = native.ToJson();
					rejection["engine_uid"] = package.EngineUid;
					rejection["canonical_map_hash"] = package.CanonicalMapHash;
					rejection["package_validation"] = package.ToJson();
					if (!string.IsNullOrEmpty(mapPath))
						rejection["debug_proxy_map_path"] = mapPath;
				}

				void RecordFailure(string code, string message, RmgGenerationSettings settings, RmgGenerationResult generation,
					string campaign = "self-test", string mapPath = null, RmgNativeMovementValidationResult native = null,
					RmgPackageValidationResult package = null)
				{
					failureReasons.TryGetValue(code, out var count);
					failureReasons[code] = count + 1;
					var failure = new JObject
					{
						["campaign"] = campaign,
						["code"] = code,
						["message"] = message,
						["mover_class"] = "unit"
					};
					if (settings != null)
					{
						failure["generator_version"] = settings.GeneratorVersion;
						failure["configuration_version"] = profile.ConfigurationVersion;
						failure["seed"] = settings.Seed.ToString();
						failure["players"] = settings.PlayerCount;
						failure["neutral_colonies"] = settings.NeutralColonyCount;
						failure["symmetry"] = OpenRaRmgMapAdapter.SymmetryName(settings.Symmetry);
						failure["archetype"] = OpenRaRmgMapAdapter.ArchetypeName(settings.Archetype);
						failure["topology_preset"] = OpenRaRmgMapAdapter.TopologyName(settings.TopologyPreset);
						failure["reproduce"] =
							$"powershell -ExecutionPolicy Bypass -File .\\scripts\\rmg\\Invoke-MapGenerator.ps1 -Seed {settings.Seed} -Players {settings.PlayerCount} -NeutralColonies {settings.NeutralColonyCount} -Symmetry {OpenRaRmgMapAdapter.SymmetryName(settings.Symmetry)} -Archetype {OpenRaRmgMapAdapter.ArchetypeName(settings.Archetype)} -Topology {OpenRaRmgMapAdapter.TopologyName(settings.TopologyPreset)} -MovementValidation both -Overwrite";
					}

					if (generation != null)
					{
						failure["logical_hash"] = generation.LogicalHash;
						failure["actor_hash"] = generation.ActorHash;
						failure["graph_hash"] = generation.GraphHash;
						failure["proxy"] = generation.Validation.ToJson();
					}

					if (native != null)
						failure["native"] = native.ToJson();
					if (package != null)
					{
						failure["engine_uid"] = package.EngineUid;
						failure["canonical_map_hash"] = package.CanonicalMapHash;
						failure["package_validation"] = package.ToJson();
					}

					if (!string.IsNullOrEmpty(mapPath))
						failure["map_path"] = mapPath;
					failures.Add(failure);
				}

				int FailureCount(string code) => failureReasons.TryGetValue(code, out var count) ? count : 0;

				JObject Distribution(IEnumerable<double> values)
				{
					var sorted = values.OrderBy(value => value).ToArray();
					return new JObject
					{
						["samples"] = sorted.Length,
						["mean"] = sorted.Length == 0 ? null : Math.Round(sorted.Average(), 3),
						["p95"] = Percentile(sorted, 0.95),
						["p99"] = Percentile(sorted, 0.99),
						["maximum"] = sorted.Length == 0 ? null : Math.Round(sorted[^1], 3)
					};
				}

				double? Percentile(double[] sorted, double percentile)
				{
					if (sorted.Length == 0)
						return null;

					var index = Math.Max(0, (int)Math.Ceiling(percentile * sorted.Length) - 1);
					return Math.Round(sorted[index], 3);
				}
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
				throw new ArgumentException("Usage: --fuzz-sa-map-generator REPORT.json [options]");

			var options = new Options { ReportPath = args[1] };
			for (var i = 2; i < args.Length; i++)
			{
				if (args[i] == "--overwrite")
				{
					options.Overwrite = true;
					continue;
				}

				if (args[i] == "--preserve-failures")
				{
					options.PreserveFailures = true;
					continue;
				}

				if (i + 1 >= args.Length)
					throw new ArgumentException($"Missing value for {args[i]}.");
				var name = args[i];
				var value = args[++i];
				switch (name)
				{
					case "--seed-start":
						if (!ulong.TryParse(value, out var seedStart))
							throw new ArgumentException("--seed-start must be an unsigned integer.");
						options.SeedStart = seedStart;
						break;
					case "--gate-a-count":
						options.GateACount = PositiveInt(name, value);
						break;
					case "--mixed-count":
						options.MixedCount = PositiveInt(name, value);
						break;
					case "--colony-count-campaign":
						options.ColonyCountCampaign = PositiveInt(name, value);
						break;
					case "--runtime-sample-rate":
						options.RuntimeSampleRate = NonNegativeInt(name, value);
						break;
					case "--movement-validation":
						options.MovementValidationMode = GenerateRmgMapCommand.ParseMovementValidation(value);
						break;
					case "--topology":
						options.TopologyPreset = GenerateRmgMapCommand.ParseTopology(value);
						break;
					default:
						throw new ArgumentException($"Unknown option: {name}");
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

		static int NonNegativeInt(string name, string text)
		{
			if (!int.TryParse(text, out var value) || value < 0)
				throw new ArgumentException($"{name} must be a non-negative integer.");
			return value;
		}

		sealed class Options
		{
			public string ReportPath { get; init; }
			public ulong SeedStart { get; set; } = 1;
			public int GateACount { get; set; } = 100;
			public int MixedCount { get; set; } = 1000;
			public int ColonyCountCampaign { get; set; } = 25;
			public int RuntimeSampleRate { get; set; } = 100;
			public RmgMovementValidationMode MovementValidationMode { get; set; } = RmgMovementValidationMode.Both;
			public RmgTopologyPreset TopologyPreset { get; set; } = RmgTopologyPreset.Off;
			public bool PreserveFailures { get; set; }
			public bool Overwrite { get; set; }
		}
	}
}
