#region Copyright & License Information
/*
 * Copyright The OpenSA Developers (see CREDITS)
 * This file is part of OpenSA, under the GNU General Public License, version 3 or later.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.OpenSA.Rmg.Reassessment;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public static partial class RmgGenerator
	{
		public static IReadOnlyList<string> RunRegionsOptionsSelfTests(ModData modData)
		{
			Game.ModData = modData;
			var failures = new List<string>();
			void Check(bool condition, string message)
			{
				if (!condition) failures.Add(message);
			}

			var baseline = new RmgPlayerSettings
			{
				SchemaVersion = 8, Seed = 397716241463670640, PlayerCount = 4,
				LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscape
			}.ToJson();
			baseline.Remove("prevent_colony_overlapping");
			Check(RmgPlayerSettingsContract.Resolve(RmgPlayerSettingsContract.Parse(baseline)).Normalized.PreventColonyOverlapping,
				"V14 missing overlap prevention must default to true.");
			var identities = new HashSet<string>();
			foreach (var water in new[] { "low", "standard", "high", "extreme", "ultra" })
				foreach (var surface in new[] { "low", "standard", "high", "extreme", "ultra" })
					foreach (var density in new[] { "sparse", "standard", "dense", "extreme", "ultra" })
						foreach (var prevent in new[] { true, false })
						{
							var json = (Newtonsoft.Json.Linq.JObject)baseline.DeepClone();
							json["water_amount"] = water;
							json["gravel_moss_amount"] = surface;
							json["neutral_colony_density"] = density;
							json["prevent_colony_overlapping"] = prevent;
							var requested = RmgPlayerSettingsContract.Parse(json);
							var settings = RmgPlayerSettingsContract.Resolve(requested).Normalized;
							var profile = RmgProfile.Load(modData, settings);
							ValidateSettings(profile, settings);
							var roundTrip = RmgPlayerSettingsContract.Resolve(RmgPlayerSettingsContract.Parse(requested.ToJson())).Normalized;
							Check(settings.GeneratorVersion == 14 && settings.PreventColonyOverlapping == prevent &&
								settings.Canonical(profile) == roundTrip.Canonical(profile), "V14 settings round trip failed.");
							Check(identities.Add(settings.Canonical(profile)), "V14 controls share a canonical identity.");
						}

			void MustReject(Action action, string label)
			{
				try { action(); failures.Add(label); }
				catch (ArgumentException) { }
			}

			foreach (var schema in Enumerable.Range(1, 7))
				foreach (var field in new[] { "water_amount", "gravel_moss_amount", "neutral_colony_density", "prevent_colony_overlapping" })
				{
					var json = new RmgPlayerSettings { SchemaVersion = schema,
						LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscape }.ToJson();
					json[field] = field == "prevent_colony_overlapping" ? (Newtonsoft.Json.Linq.JToken)false : "ultra";
					MustReject(() => RmgPlayerSettingsContract.Parse(json), $"Schema {schema} accepted V14 field/value {field}.");
				}

			foreach (var family in new[] { RmgPlayerLayoutFamily.ArtificialBattlefield, RmgPlayerLayoutFamily.StructuredCompetitive })
				MustReject(() => RmgPlayerSettingsContract.Resolve(new RmgPlayerSettings { SchemaVersion = 8,
					LayoutFamily = family, WaterAmount = RmgPlayerParameterLevel.Ultra }), "Historical family accepted Ultra water.");
			MustReject(() => RmgPlayerSettingsContract.Resolve(new RmgPlayerSettings { SchemaVersion = 7,
				LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscape, PreventColonyOverlapping = false }), "V13 accepted relaxed spacing.");
			var badBoolean = (Newtonsoft.Json.Linq.JObject)baseline.DeepClone();
			badBoolean["prevent_colony_overlapping"] = "false";
			MustReject(() => RmgPlayerSettingsContract.Parse(badBoolean), "Overlap setting accepted a string instead of a boolean.");

			foreach (var size in new[] { 128, 256 })
				foreach (var players in new[] { 2, 4 })
					foreach (var level in new[] { RmgPlayerColonyDensity.Extreme, RmgPlayerColonyDensity.Ultra })
					{
						var settings = RmgPlayerSettingsContract.Resolve(new RmgPlayerSettings { SchemaVersion = 8,
							MapSize = size, PlayerCount = players, LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscape,
							NeutralColonyDensity = level }).Normalized;
						var expected = level == RmgPlayerColonyDensity.Extreme ? (players == 2 ? 32 : 40) : (players == 2 ? 52 : 64);
						Check(settings.NeutralColonyCount == expected * (size == 256 ? 3 : 1), "V14 density scaling changed.");
						ValidateSettings(RmgProfile.Load(modData, settings), settings);
					}

			RmgGenerationResult Make(int schema, int size, bool original, bool prevent, bool crowded)
			{
				var settings = RmgPlayerSettingsContract.Resolve(new RmgPlayerSettings
				{
					SchemaVersion = schema, Seed = 397716241463670640, MapSize = size, PlayerCount = 4,
					LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscape, OriginalSurfaceRelations = original,
					PreventColonyOverlapping = prevent, TerrainComplexity = TerrainComplexity.Ultra,
					WaterAmount = crowded ? RmgPlayerParameterLevel.High : RmgPlayerParameterLevel.Standard,
					TacticalTerrain = RmgPlayerParameterLevel.High,
					NeutralColonyDensity = crowded ? RmgPlayerColonyDensity.Ultra : RmgPlayerColonyDensity.Standard
				}).Normalized;
				return Generate(RmgProfile.Load(modData, settings), settings);
			}

			foreach (var size in new[] { 128, 256 })
				foreach (var original in new[] { true, false })
				{
					var old = Make(7, size, original, true, false);
					var current = Make(8, size, original, true, false);
					var relaxed = Make(8, size, original, false, false);
					Check(old.LogicalHash == current.LogicalHash && old.ActorHash == current.ActorHash && old.GraphHash == current.GraphHash,
						$"V14 changed accepted V13 generation: {size}/{original}.");
					Check(current.ActorHash == relaxed.ActorHash, $"Unneeded fallback changed colonies or doodads: {size}/{original}.");

					var strict = Make(8, size, original, true, true);
					var crowded = Make(8, size, original, false, true);
					var repeat = Make(8, size, original, false, true);
					var strictColonies = strict.Map.Actors.Where(a => a.Role == "neutral-colony").ToArray();
					var colonies = crowded.Map.Actors.Where(a => a.Role == "neutral-colony").ToArray();
					Check(crowded.Validation.Accepted && strict.Validation.Accepted, "V14 spacing policy rejected valid placement.");
					Check(crowded.LogicalHash == repeat.LogicalHash && crowded.ActorHash == repeat.ActorHash, "V14 fallback is nondeterministic.");
					Check(strict.Map.NativeTerrainIntents.SequenceEqual(crowded.Map.NativeTerrainIntents) &&
						strict.Map.TemplateIds.SequenceEqual(crowded.Map.TemplateIds) && strict.Map.Starts.SequenceEqual(crowded.Map.Starts),
						"Fallback changed terrain or player starts.");
					Check(strictColonies.SequenceEqual(colonies.Take(strictColonies.Length)), "Fallback moved existing colonies.");
					Check(colonies.Length > strictColonies.Length && colonies.Length <= crowded.Settings.NeutralColonyCount,
						$"Crowded test did not exercise shortfall recovery: {size}/{original}.");
					var sites = new RegionsSites(modData, crowded.Map, original);
					foreach (var start in crowded.Map.Starts) sites.ReserveStart(start);
					foreach (var colony in colonies)
					{
						Check(sites.ColonyFits(colony.Type, colony.LogicalLocation), "Fallback blocked a footprint or exit.");
						Check(crowded.Map.Starts.All(start => crowded.Profile.ColonyCombatRules.CombatSpaceIsSafeFromAnyStartingActor(
							colony.Type, colony.LogicalLocation, start)), "Fallback entered a player's combat/production space.");
						sites.ReserveColony(colony.Type, colony.LogicalLocation);
					}

					// Independently enumerate every legal site/type to check the first greedy choice.
					if (size == 128 && colonies.Length > strictColonies.Length)
					{
						var available = new RegionsSites(modData, strict.Map, original);
						foreach (var start in strict.Map.Starts) available.ReserveStart(start);
						foreach (var colony in strictColonies) available.ReserveColony(colony.Type, colony.LogicalLocation);
						(int, long, int) Penalty(string type, RmgPoint point)
						{
							var depths = strictColonies.Select(other => Math.Max(0, -strict.Profile.ColonyCombatRules.CombatSpaceMarginNative(
								type, point, other.Type, other.LogicalLocation))).ToArray();
							return (depths.Max(), depths.Sum(d => (long)d * d), depths.Count(d => d > 0));
						}

						var selected = colonies[strictColonies.Length];
						var selectedPenalty = Penalty(selected.Type, selected.LogicalLocation);
						for (var y = 0; y < strict.Map.Height; y++)
							for (var x = 0; x < strict.Map.Width; x++)
								foreach (var type in strict.Profile.NeutralColonyActors)
								{
									var point = new RmgPoint(x, y);
									if (available.ColonyFits(type, point) && strict.Map.Starts.All(start =>
										strict.Profile.ColonyCombatRules.CombatSpaceIsSafeFromAnyStartingActor(type, point, start)))
										Check(selectedPenalty.CompareTo(Penalty(type, point)) <= 0, "Fallback skipped a smaller overlap penalty.");
								}
					}

					var strictReport = new RmgValidationReport();
					ValidateColonyCombatSpace(crowded.Map, crowded.Profile, strictReport);
					Check(!strictReport.Accepted, "Strict validator accepted the relaxed fixture's neutral overlaps.");
					crowded.Map.Actors.Add(new RmgActorPlan("ants_colony", crowded.Profile.ColonyOwner, "neutral-colony", crowded.Map.Starts[0], 999));
					var unsafeReport = new RmgValidationReport();
					ValidateColonyCombatSpace(crowded.Map, crowded.Profile, unsafeReport, true);
					Check(!unsafeReport.Accepted, "Relaxed validator accepted a colony on a player start.");
				}

			return failures;
		}
	}
}
