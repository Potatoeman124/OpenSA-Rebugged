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
		public static IReadOnlyList<string> RunRegionsOwnershipSelfTests(ModData modData)
		{
			Game.ModData = modData;
			var failures = new List<string>();
			void Check(bool ok, string message) { if (!ok) failures.Add(message); }
			void Reject(Action action, string message)
			{
				try { action(); failures.Add(message); }
				catch (ArgumentException) { }
			}
			Check(RmgColonyOwnership.Allocate(10, new[] { 0, 10, 50 }).SequenceEqual(new[] { 0, 1, 5 }), "Below-100 example failed.");
			Check(RmgColonyOwnership.Allocate(10, new[] { 40, 40, 80 }).SequenceEqual(new[] { 3, 2, 5 }), "Weighted example failed.");
			Check(RmgColonyOwnership.Allocate(2, new[] { 33, 33, 33 }).SequenceEqual(new[] { 1, 1, 0 }), "Rounding overflow or unstable tie.");
			Check(RmgColonyOwnership.Allocate(3, new[] { 0, 10, 50 }).SequenceEqual(new[] { 0, 0, 2 }), "Rounded global budget failed.");
			foreach (var n in Enumerable.Range(0, 129))
				foreach (var a in new[] { 0, 1, 10, 33, 50, 99, 100 })
					foreach (var b in new[] { 0, 1, 10, 33, 50, 99, 100 })
					{
						var shares = new[] { a, b, 0 };
						var counts = RmgColonyOwnership.Allocate(n, shares);
						Check(counts.Sum() == (n * Math.Min(a + b, 100) + 99) / 100 && counts[2] == 0 && counts.All(c => c >= 0), "Allocation conservation failed.");
						var owners = RmgColonyOwnership.Assign(Enumerable.Range(0, n).Select(i => new RmgPoint(i, 0)).ToArray(),
							new RmgPoint[3], shares, RmgColonyOwnershipMode.Random, (ulong)n);
						Check(Enumerable.Range(0, 3).All(p => owners.Count(o => o == p) == counts[p]) &&
							owners.Count(o => o == -1) == n - counts.Sum(), "Random assignment violated ownership quotas.");
					}
			var positions = new[] { new RmgPoint(1, 0), new RmgPoint(9, 0), new RmgPoint(5, 0) };
			var starts = new[] { new RmgPoint(0, 0), new RmgPoint(10, 0) };
			Check(RmgColonyOwnership.Assign(positions, starts, new[] { 33, 33 }).SequenceEqual(new[] { 0, 1, -1 }), "Closest-site assignment failed.");
			Check(RmgColonyOwnership.Assign(positions, starts.Reverse().ToArray(), new[] { 33, 33 }).SequenceEqual(new[] { 1, 0, -1 }), "Swapped starts did not move ownership.");
			var randomSites = Enumerable.Range(0, 40).Select(i => new RmgPoint(i * 3, i % 7)).ToArray();
			var randomShares = new[] { 40, 40 };
			var layouts = new HashSet<string>();
			foreach (var seed in new ulong[] { 0, 1, 748797295927410807, ulong.MaxValue })
			{
				var owners = RmgColonyOwnership.Assign(randomSites, starts, randomShares, RmgColonyOwnershipMode.Random, seed);
				Check(owners.SequenceEqual(RmgColonyOwnership.Assign(randomSites, starts, randomShares, RmgColonyOwnershipMode.Random, seed)), "Random assignment is not repeatable.");
				Check(owners.SequenceEqual(RmgColonyOwnership.Assign(randomSites, starts.Reverse().ToArray(), randomShares, RmgColonyOwnershipMode.Random, seed)), "Random ownership depends on spawn distance.");
				layouts.Add(string.Join(",", owners));
			}
			Check(layouts.Count == 4, "Distinct ownership seeds did not change random selection.");
			Reject(() => RmgColonyOwnership.Assign(positions, starts, new[] { 50, 50 }, (RmgColonyOwnershipMode)99), "Invalid assignment mode accepted.");
			foreach (var schema in new[] { 8, 9 })
			{
				Reject(() => RmgPlayerSettingsContract.Resolve(new RmgPlayerSettings { SchemaVersion = schema, MapSize = 64, LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscape }), "Old schema accepted 64.");
				Reject(() => RmgPlayerSettingsContract.Resolve(new RmgPlayerSettings { SchemaVersion = schema, StartingColonyShares = new[] { 0, 0 }, LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscape }), "Old schema accepted ownership.");
				Reject(() => RmgPlayerSettingsContract.Resolve(new RmgPlayerSettings { SchemaVersion = schema, StartingColonyMode = RmgColonyOwnershipMode.Random, LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscape }), "Old schema accepted random ownership.");
			}
			foreach (var schema in Enumerable.Range(1, 9))
				Reject(() => RmgPlayerSettingsContract.Resolve(new RmgPlayerSettings { SchemaVersion = schema, MapSize = 512, LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscape }), "Old schema accepted 512.");
			foreach (var family in new[] { RmgPlayerLayoutFamily.StructuredCompetitive, RmgPlayerLayoutFamily.ArtificialBattlefield })
				Reject(() => RmgPlayerSettingsContract.Resolve(new RmgPlayerSettings { SchemaVersion = 10, MapSize = 512, LayoutFamily = family }), "Historical layout accepted 512.");
			foreach (var shares in new JToken[] { JValue.CreateNull(), new JArray(1), new JArray(-1, 0), new JArray(101, 0), new JArray(1.5, 0), new JArray("1", 0) })
				Reject(() => RmgColonyOwnership.ParseShares(shares, 2), "Invalid ownership JSON accepted.");
			foreach (var size in new[] { 64, 128, 256, 512 })
				foreach (var players in Enumerable.Range(1, 8))
				{
					var requested = new RmgPlayerSettings { SchemaVersion = 10, MapSize = size, PlayerCount = players,
						LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscape, StartingColonyShares = Enumerable.Repeat(33, players).ToArray() };
					if (size == 64 && players > 4)
					{
						Reject(() => RmgPlayerSettingsContract.Resolve(requested), "64 accepted more than four players.");
						var direct = RmgPlayerSettingsContract.Resolve(new RmgPlayerSettings { SchemaVersion = 10, MapSize = 64,
							LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscape }).Normalized;
						direct.PlayerCount = players;
						direct.StartingColonyShares = new int[players];
						Reject(() => ValidateSettings(RmgProfile.Load(modData, direct), direct), "Direct generation bypassed small-map player cap.");
						continue;
					}
					var settings = RmgPlayerSettingsContract.Resolve(requested).Normalized;
					var profile = RmgProfile.Load(modData, settings);
					ValidateSettings(profile, settings);
					if (size == 512)
					{
						Check(profile.PlayableWidth == 512 && profile.LogicalWidth == 256 && settings.NeutralColonyCount == (4 + 3 * players) * 9, "512 profile or Balanced colony scaling failed.");
						settings.GeneratorVersion = 15;
						Reject(() => RmgProfile.Load(modData, settings), "Historical direct generator accepted 512.");
						settings.GeneratorVersion = 16;
					}

					Check(settings.Canonical(profile) == RmgPlayerSettingsContract.Resolve(RmgPlayerSettingsContract.Parse(requested.ToJson())).Normalized.Canonical(profile), "V16 round trip failed.");
				}
			Reject(() => RmgPlayerSettingsContract.Resolve(new RmgPlayerSettings { SchemaVersion = 10, LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscape, NeutralColonyWeights = new(101) }), "V16 accepted weight above 100.");
			var oldWeights = RmgPlayerSettingsContract.Resolve(new RmgPlayerSettings { SchemaVersion = 9, LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscape, NeutralColonyWeights = new(1000) });
			Check(oldWeights.Normalized.NeutralColonyWeights.Ants == 1000, "V15 weight limit changed.");
			var small = RmgPlayerSettingsContract.Resolve(new RmgPlayerSettings { SchemaVersion = 10, MapSize = 64, PlayerCount = 1, Seed = 397716241463670640,
				LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscape }).Normalized;
			var result = Generate(RmgProfile.Load(modData, small), small);
			Check(result.Map.Width == 32 && result.Map.Starts.Count == 1 && result.Validation.HardFailures.Count == 0, "64x64 solo generation failed.");
			var json = new RmgPlayerSettings { SchemaVersion = 10, PlayerCount = 3, MapSize = 256, Seed = 397716241463670640,
				LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscape }.ToJson();
			var first = RmgPlayerSettingsContract.Resolve(RmgPlayerSettingsContract.Parse(json)).Normalized;
			var aResult = Generate(RmgProfile.Load(modData, first), first);
			json["starting_colony_shares"] = new JArray(0, 10, 50);
			var changed = RmgPlayerSettingsContract.Resolve(RmgPlayerSettingsContract.Parse(json)).Normalized;
			var bResult = Generate(RmgProfile.Load(modData, changed), changed);
			Check(aResult.LogicalHash == bResult.LogicalHash && aResult.ActorHash == bResult.ActorHash, "Ownership changed generated terrain or placement.");
			var closestCanonical = changed.Canonical(RmgProfile.Load(modData, changed));
			var closestJson = RmgPlayerSettingsContract.Parse(json).ToJson().ToString();
			json["starting_colony_mode"] = "closest-to-spawn";
			Check(RmgPlayerSettingsContract.Parse(json).ToJson().ToString() == closestJson, "Explicit closest mode changed existing serialized settings.");
			json["starting_colony_mode"] = "random";
			var randomSettings = RmgPlayerSettingsContract.Resolve(RmgPlayerSettingsContract.Parse(json)).Normalized;
			var randomProfile = RmgProfile.Load(modData, randomSettings);
			Check(randomSettings.StartingColonyMode == RmgColonyOwnershipMode.Random && randomSettings.Canonical(randomProfile) != closestCanonical, "Random mode missing from map identity.");
			Check(RmgPlayerSettingsContract.Resolve(RmgPlayerSettingsContract.Parse(RmgPlayerSettingsContract.Parse(json).ToJson())).Normalized.Canonical(randomProfile) == randomSettings.Canonical(randomProfile), "Random settings round trip failed.");
			var randomResult = Generate(randomProfile, randomSettings);
			Check(randomResult.LogicalHash == bResult.LogicalHash && randomResult.ActorHash == bResult.ActorHash, "Random mode changed terrain or colony placement.");
			foreach (var invalid in new JToken[] { JValue.CreateNull(), new JValue(1), new JValue(true), new JValue("nearest"), new JArray("random") })
			{
				json["starting_colony_mode"] = invalid;
				Reject(() => RmgPlayerSettingsContract.Parse(json), "Invalid ownership mode JSON accepted.");
			}
			json["starting_colony_mode"] = "random";
			json["schema_version"] = 9;
			Reject(() => RmgPlayerSettingsContract.Parse(json), "Old JSON schema accepted ownership mode.");
			randomSettings.StartingColonyMode = (RmgColonyOwnershipMode)99;
			Reject(() => ValidateSettings(randomProfile, randomSettings), "Direct generator accepted invalid ownership mode.");
			json["schema_version"] = 10;
			json["tileset"] = "NORMAL";
			Check(!RmgPlayerSettingsContract.Parse(json).ToJson().ContainsKey("tileset"), "Explicit NORMAL altered the saved default settings.");
			foreach (var tileset in RmgBiome.Tilesets)
			{
				RmgBiome.ValidateCatalogue(modData, tileset);
				json["tileset"] = tileset.ToLowerInvariant();
				var requestedBiome = RmgPlayerSettingsContract.Parse(json);
				var biome = RmgPlayerSettingsContract.Resolve(requestedBiome).Normalized;
				var biomeProfile = RmgProfile.Load(modData, biome);
				Check(biome.Tileset == tileset && biomeProfile.Tileset == tileset, "Tileset did not reach the selected profile.");
				Check(biome.Canonical(biomeProfile) == RmgPlayerSettingsContract.Resolve(RmgPlayerSettingsContract.Parse(requestedBiome.ToJson())).Normalized.Canonical(biomeProfile), "Biome round trip failed.");
				var generated = Generate(biomeProfile, biome);
				Check(generated.LogicalHash == randomResult.LogicalHash && generated.Map.Starts.SequenceEqual(randomResult.Map.Starts) &&
					generated.Map.Actors.Where(a => a.Role != "decoration-passable").SequenceEqual(randomResult.Map.Actors.Where(a => a.Role != "decoration-passable")), "Changing biome moved terrain, starts or colonies.");
				Check(generated.Map.Actors.Where(a => a.Role == "decoration-passable").All(a => RmgBiome.Decorations(tileset).Contains(a.Type)), "Biome contains foreign decorations.");
			}

			foreach (var invalid in new JToken[] { JValue.CreateNull(), new JValue(1), new JValue("ICE"), new JArray("DESERT") })
			{
				json["tileset"] = invalid;
				Reject(() => RmgPlayerSettingsContract.Parse(json), "Invalid tileset JSON accepted.");
			}

			json["tileset"] = "DESERT";
			json["schema_version"] = 9;
			Reject(() => RmgPlayerSettingsContract.Parse(json), "Historical schema accepted a biome.");
			Reject(() => RmgPlayerSettingsContract.Resolve(new RmgPlayerSettings { SchemaVersion = 10, Tileset = "DESERT", LayoutFamily = RmgPlayerLayoutFamily.StructuredCompetitive }), "Historical layout accepted a biome.");
			return failures;
		}
	}
}
