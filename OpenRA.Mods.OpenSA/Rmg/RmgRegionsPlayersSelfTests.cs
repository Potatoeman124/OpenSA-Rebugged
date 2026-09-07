#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public static partial class RmgGenerator
	{
		public static IReadOnlyList<string> RunRegionsPlayersSelfTests(ModData modData)
		{
			Game.ModData = modData;
			var failures = new List<string>();
			void Check(bool condition, string message) { if (!condition) failures.Add(message); }
			void Reject(Action action, string label)
			{
				try { action(); failures.Add(label); }
				catch (ArgumentException) { }
			}

			var baseline = new RmgPlayerSettings
			{
				SchemaVersion = 9, Seed = 397716241463670640,
				LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscape, MapSize = 256, PlayerCount = 4
			}.ToJson();
			RmgGenerationSettings Resolve(JObject json) => RmgPlayerSettingsContract.Resolve(RmgPlayerSettingsContract.Parse(json)).Normalized;
			foreach (var size in new[] { 128, 256 })
				foreach (var players in Enumerable.Range(1, 8))
					foreach (var density in new[] { "sparse", "standard", "dense", "extreme", "ultra" })
					{
						var json = (JObject)baseline.DeepClone();
						json["size"] = $"{size},{size}"; json["players"] = players; json["neutral_colony_density"] = density;
						var settings = Resolve(json);
						var profile = RmgProfile.Load(modData, settings);
						ValidateSettings(profile, settings);
						var expected = density switch { "sparse" => 4 + 2 * players, "standard" => 4 + 3 * players,
							"dense" => 16 + 2 * players, "extreme" => 24 + 4 * players, _ => 40 + 6 * players };
						Check(settings.NeutralColonyCount == expected * (size == 256 ? 3 : 1), "V15 density scaling failed.");
						Check(settings.GeneratorVersion == 15 && settings.Canonical(profile) == Resolve(settings.PlayerSettingsResolution.Requested.ToJson()).Canonical(profile), "V15 JSON round trip failed.");
					}

			foreach (var players in new[] { 0, 9, -1, int.MaxValue })
			{
				var json = (JObject)baseline.DeepClone(); json["players"] = players;
				Reject(() => Resolve(json), "Invalid player count accepted.");
			}

			foreach (var schema in Enumerable.Range(3, 6))
			{
				var json = new RmgPlayerSettings { SchemaVersion = schema, PlayerCount = 1, LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscape }.ToJson();
				Reject(() => Resolve(json), "Frozen schema accepted solo.");
				json["players"] = 4; json["neutral_colony_weights"] = new RmgColonyWeights().ToJson();
				Reject(() => Resolve(json), "Frozen schema accepted weights.");
			}

			foreach (var bad in new JToken[]
			{
				JValue.CreateNull(), new JArray(1, 2), new JObject(),
				JObject.Parse("{ants: -1, beetles: 100, scorpions: 100, spiders: 100, wasps: 100}"),
				JObject.Parse("{ants: 1001, beetles: 100, scorpions: 100, spiders: 100, wasps: 100}"),
				JObject.Parse("{ants: 1.5, beetles: 100, scorpions: 100, spiders: 100, wasps: 100}"),
				JObject.Parse("{ants: '1', beetles: 100, scorpions: 100, spiders: 100, wasps: 100}"),
				JObject.Parse("{ants: 1, beetles: 100, scorpions: 100, spiders: 100, wasps: 100, typo: 1}")
			})
				Reject(() => RmgColonyWeights.Parse(bad), "Malformed weights accepted.");
			Reject(() => new RmgColonyWeights(-1).Validate(), "Negative in-memory weight accepted.");
			Reject(() => RmgPlayerSettingsContract.Resolve(new RmgPlayerSettings { NeutralColonyWeights = new(0) }), "Old direct settings accepted custom weights.");
			var weighted = new RmgColonyWeights(0, 3, 0, 1, 2);
			var tickets = Enumerable.Range(0, weighted.Total).Select(weighted.ActorForTicket).ToArray();
			Check(tickets.SequenceEqual(new[] { "beetles_colony", "beetles_colony", "beetles_colony", "spiders_colony", "wasps_colony", "wasps_colony" }), "Weight ticket boundaries changed.");
			Reject(() => weighted.ActorForTicket(6), "Out-of-range ticket accepted.");
			Reject(() => new RmgColonyWeights(0, 0, 0, 0, 0).ActorForTicket(0), "Empty pool accepted a ticket.");

			RmgGenerationResult Make(int players, int size, ulong seed, RmgColonyWeights weights, bool prevent, bool crowded = false)
			{
				var json = (JObject)baseline.DeepClone(); json["players"] = players; json["size"] = $"{size},{size}";
				json["seed"] = seed.ToString(System.Globalization.CultureInfo.InvariantCulture);
				json["neutral_colony_weights"] = weights.ToJson(); json["prevent_colony_overlapping"] = prevent;
				if (crowded) json["neutral_colony_density"] = "ultra";
				var settings = Resolve(json);
				var result = Generate(RmgProfile.Load(modData, settings), settings);
				Check(result.Map.Starts.Count == players && result.Validation.HardFailures.Count == 0, $"Invalid {players}-player map.");
				return result;
			}

			foreach (var size in new[] { 128, 256 })
				foreach (var players in Enumerable.Range(1, 8))
					foreach (var seed in new[] { 0UL, 397716241463670640UL })
						Make(players, size, seed, new(), true);
			var control = Make(4, 256, 397716241463670640, new(), true);
			foreach (var weights in new[]
			{
				new RmgColonyWeights(0, 0, 0, 0, 0), new(1000, 0, 0, 0, 0), new(0, 1000, 0, 0, 0),
				new(0, 0, 1000, 0, 0), new(0, 0, 0, 1000, 0), new(0, 0, 0, 0, 1000), new(45, 45, 0, 10, 0)
			})
			{
				var result = Make(4, 256, 397716241463670640, weights, true);
				Check(control.Map.Starts.SequenceEqual(result.Map.Starts) && control.Map.NativeTerrainIntents.SequenceEqual(result.Map.NativeTerrainIntents), "Weights moved starts or terrain.");
				var allowed = RmgColonyWeights.Keys.Where((_, i) => weights.Values[i] != 0).Select(key => key + "_colony").ToHashSet();
				Check(result.Map.Actors.Where(a => a.Role == "neutral-colony").All(a => allowed.Contains(a.Type)), "Excluded colony generated.");
				Check(weights.Total != 0 || (result.Map.Actors.All(a => a.Role != "neutral-colony") && result.Validation.Warnings.Count == 0), "Empty weights did not disable colonies cleanly.");
				var repeat = Make(4, 256, 397716241463670640, weights, true);
				Check(result.LogicalHash == repeat.LogicalHash && result.ActorHash == repeat.ActorHash, "V15 repeatability failed.");
			}

			var strict = Make(4, 128, 397716241463670640, weighted, true, true);
			var relaxed = Make(4, 128, 397716241463670640, weighted, false, true);
			var prefix = strict.Map.Actors.Where(a => a.Role is "start" or "neutral-colony").ToArray();
			Check(prefix.SequenceEqual(relaxed.Map.Actors.Where(a => a.Role is "start" or "neutral-colony").Take(prefix.Length)), "Relaxed spacing replaced strict colonies.");
			Check((int)relaxed.Map.RegionsReport["neutral_colonies_fallback"] > 0, "Weighted fallback was not exercised.");
			Check(relaxed.Map.Actors.Where(a => a.Role == "neutral-colony").All(a => a.Type is "beetles_colony" or "spiders_colony" or "wasps_colony"), "Weighted fallback placed excluded type.");
			return failures;
		}
	}
}
