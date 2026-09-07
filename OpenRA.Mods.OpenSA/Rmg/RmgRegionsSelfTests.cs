#region Copyright & License Information
/*
 * Copyright The OpenSA Developers (see CREDITS)
 * This file is part of OpenSA, under the GNU General Public License, version 3 or later.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OpenRA.Mods.OpenSA.Rmg.Reassessment;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public static partial class RmgGenerator
	{
		public static IReadOnlyList<string> RunRegionsSelfTests(ModData modData)
		{
			Game.ModData = modData;
			var failures = new List<string>();
			void Check(bool condition, string message)
			{
				if (!condition) failures.Add(message);
			}

			foreach (var size in new[] { 128, 256 })
				foreach (var complexity in new[] { TerrainComplexity.Low, TerrainComplexity.Standard, TerrainComplexity.High })
				{
					var requested = new RmgPlayerSettings
					{
						SchemaVersion = 5, Seed = 7300001, MapSize = size, PlayerCount = 4,
						LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscape, TerrainComplexity = complexity
					};
					var settings = RmgPlayerSettingsContract.Resolve(requested).Normalized;
					var repeat = RmgPlayerSettingsContract.Resolve(RmgPlayerSettingsContract.Parse(requested.ToJson())).Normalized;
					var profile = RmgProfile.Load(modData, settings);
					Check(settings.GeneratorVersion == 11 && settings.Canonical(profile) == repeat.Canonical(profile),
						$"Regions schema round trip failed: {size}/{complexity}.");
					Check(settings.TerrainComplexity == complexity, "Regions complexity was lost during settings resolution.");
				}

			var json = new RmgPlayerSettings { SchemaVersion = 5, LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscape }.ToJson();
			foreach (var (field, value) in new[] { ("tactical_terrain", "standard"), ("terrain_complexity", "unknown") })
			{
				var invalid = (JObject)json.DeepClone();
				invalid[field] = value;
				var rejected = false;
				try { RmgPlayerSettingsContract.Parse(invalid); }
				catch (ArgumentException) { rejected = true; }
				Check(rejected, $"Invalid schema 5 field/value was accepted: {field}.");
			}

			// Deliberately disconnected, capacity-limited existing land. No route carving is allowed.
			var fixtureSettings = RmgPlayerSettingsContract.Resolve(new RmgPlayerSettings
			{
				SchemaVersion = 5, Seed = 9112026, PlayerCount = 2,
				LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscape, NeutralColonyDensity = RmgPlayerColonyDensity.Sparse
			}).Normalized;
			var fixtureProfile = RmgProfile.Load(modData, fixtureSettings);
			var logical = new RmgLogicalMap(64, 64);
			for (var y = 0; y < 64; y++)
				for (var x = 0; x < 64; x++)
				{
					var dry = (x >= 6 && x < 18 && y >= 6 && y < 18) || (x >= 46 && x < 58 && y >= 46 && y < 58);
					var index = y * 64 + x;
					logical.TemplateIds[index] = (ushort)(dry ? 32 : 9);
					logical.Obstacles[index] = !dry;
					for (var frame = 0; frame < 4; frame++)
						logical.NativeTerrainIntents[4 * index + frame] = dry ? RmgNativeTerrainIntent.Clear : RmgNativeTerrainIntent.Water;
				}

			var frozen = TerrainComparison.Hash(TerrainComparison.NativeBytes(logical));
			var result = CompleteRegions(fixtureProfile, fixtureSettings, new TerrainComparisonResult { Map = logical, Report = new JObject() });
			Check(result.Validation.Accepted && result.Map.Starts.Count == 2, "Disconnected fixture lost valid starts.");
			Check(result.Map.Actors.Count(a => a.Owner == fixtureProfile.ColonyOwner) < fixtureSettings.NeutralColonyCount &&
				result.Validation.Warnings.Any(w => w.Code == "NEUTRAL_CAPACITY"), "Capacity-limited fixture did not report a neutral shortfall.");
			Check(frozen == TerrainComparison.Hash(TerrainComparison.NativeBytes(logical)), "Placement repainted the disconnected fixture.");
			using var map = TerrainComparisonExport.CreateMap(modData, logical);
			map.PlayerDefinitions = new MapPlayers(map.Rules, 2).ToMiniYaml();
			foreach (var actor in logical.Actors)
			{
				var anchor = OpenRaRmgMapAdapter.ToNative(actor.LogicalLocation, fixtureProfile);
				var reference = new ActorReference(actor.Type)
				{
					new LocationInit(new CPos(anchor.X + actor.NativeFrame % 2, anchor.Y + actor.NativeFrame / 2)),
					new OwnerInit(actor.Owner)
				};
				map.ActorDefinitions.Add(new MiniYamlNode($"Actor{map.ActorDefinitions.Count}", reference.Save()));
			}

			var valid = NativeMovementValidator.Validate(map, result);
			Check(valid.Accepted && (string)valid.ToJson()["accessibility_requirement"] == "NOT_REQUIRED",
				"Native Regions validator rejected locally valid disconnected land.");
			var cell = OpenRaRmgMapAdapter.ToNative(logical.Starts[0], fixtureProfile);
			var before = map.Tiles[cell];
			map.Tiles[cell] = new TerrainTile(9, 0);
			var corrupt = NativeMovementValidator.Validate(map, result);
			Check(!corrupt.Accepted && corrupt.HardFailures.Any(f => f.Code == "NATIVE_SEMANTICS"),
				"Native Regions validator accepted corrupted terrain.");
			map.Tiles[cell] = before;
			var definition = map.ActorDefinitions[0];
			map.ActorDefinitions.RemoveAt(0);
			var missingActor = NativeMovementValidator.Validate(map, result);
			Check(!missingActor.Accepted && missingActor.HardFailures.Any(f => f.Code == "NATIVE_ACTORS"),
				"Native Regions validator accepted a missing saved start.");
			map.ActorDefinitions.Insert(0, definition);
			Check(NativeMovementValidator.Validate(map, result).Accepted, "Restoring the fixture did not restore native acceptance.");
			return failures;
		}
	}
}
