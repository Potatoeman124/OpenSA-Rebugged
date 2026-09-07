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
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.OpenSA.Rmg.Reassessment;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public static partial class NativeMovementValidator
	{
		static RmgNativeMovementValidationResult ValidateRegions(Map map, RmgGenerationResult generation)
		{
			var profile = generation.Profile;
			var settings = generation.Settings;
			var locomotors = map.Rules.Actors[SystemActors.World].TraitInfos<LocomotorInfo>().ToArray();
			var ground = locomotors.Single(l => l.Name == GroundLocomotorName);
			var grid = BuildGrid(map, ground, out var actors, out var transitCells);
			var starts = map.Rules.Actors[SystemActors.World].TraitInfos<StartingUnitsInfo>()
				.Where(s => !string.IsNullOrEmpty(s.BaseActor)).ToArray();
			var colonies = actors.Where(a => profile.NeutralColonyActors.Contains(a.Type)).ToArray();
			var startCoverage = new HashSet<CPos>();
			var startBlocked = new HashSet<CPos>();
			foreach (var point in generation.Map.Starts)
				foreach (var start in starts)
				{
					var footprint = Footprint(map, start.BaseActor, OpenRaRmgMapAdapter.ToNative(point, profile) + start.BaseActorOffset);
					startCoverage.UnionWith(footprint.Coverage);
					startBlocked.UnionWith(footprint.Blocked);
				}

			var withStarts = grid.Clone();
			foreach (var cell in startBlocked)
				withStarts.Block(cell, "starting-colony-union");
			var exits = CountProductionExitFailures(map, withStarts, generation, starts, colonies, out var exitDetails);
			var costsAccepted = ValidateLandCoverCostContract(ground, out var costMessage);
			var invalidStart = 0;
			var invalidColony = 0;
			bool Invalid(CPos cell) => !grid.Contains(cell) ||
				!ground.TerrainSpeeds.ContainsKey(map.GetTerrainInfo(cell).Type) ||
				(settings.OriginalSurfaceRelations && map.GetTerrainInfo(cell).Type != "Clear");
			invalidStart = startCoverage.Count(Invalid);
			invalidColony = colonies.SelectMany(c => c.Coverage.Append(c.Location)).Count(Invalid);
			var overlaps = startCoverage.Count(cell => colonies.Any(c => c.Coverage.Contains(cell)));
			for (var i = 0; i < colonies.Length; i++)
				for (var j = i + 1; j < colonies.Length; j++)
					overlaps += colonies[i].Coverage.Intersect(colonies[j].Coverage).Count();

			var semantics = 0;
			var native = new byte[settings.MapSize * settings.MapSize];
			for (var y = 0; y < settings.MapSize; y++)
				for (var x = 0; x < settings.MapSize; x++)
				{
					var cell = new CPos(x + profile.CordonWidth, y + profile.CordonWidth);
					var actual = map.GetTerrainInfo(cell).Type;
					var expected = TerrainComparison.Native(generation.Map, x, y);
					if (actual != expected.ToString() || map.Height[cell] != 0)
						semantics++;
					native[y * settings.MapSize + x] = actual switch { "Clear" => 0, "Water" => 1, "Rock" => 2, "Vegetation" => 3, _ => 255 };
				}

			var actualActors = map.ActorDefinitions.Select(node =>
				new ActorReference(node.Value.Value, node.Value.ToDictionary())).ToArray();
			var actorMismatch = actualActors.Length != generation.Map.Actors.Count;
			foreach (var plan in generation.Map.Actors)
			{
				var anchor = OpenRaRmgMapAdapter.ToNative(plan.LogicalLocation, profile);
				var expected = new CPos(anchor.X + plan.NativeFrame % 2, anchor.Y + plan.NativeFrame / 2);
				if (!actualActors.Any(actor => actor.Type == plan.Type && actor.GetOrDefault<LocationInit>()?.Value == expected &&
					actor.GetOrDefault<OwnerInit>()?.InternalName == plan.Owner))
					actorMismatch = true;
			}

			var contacts = TerrainComparison.CountForbiddenContacts(native, settings.MapSize);
			var result = new RmgNativeMovementValidationResult
			{
				ValidatorName = "openra-native-regions-local-v1",
				RegionsPolicy = new JObject(),
				TerrainSemanticMismatchCells = semantics,
				ProductionExitFailures = exits,
				NonDirtStartCells = invalidStart,
				NonDirtColonyCells = invalidColony,
				DirtPlacementEnforced = settings.OriginalSurfaceRelations
			};
			void Require(bool ok, string code, string message)
			{
				if (!ok)
					result.HardFailures.Add(new RmgValidationIssue(code, message));
			}

			Require(costsAccepted, "NATIVE_COSTS", costMessage);
			Require(exits == 0, "LOCAL_PRODUCTION_EXITS", $"{exits} exits are blocked or outside playable bounds.");
			Require(invalidStart == 0 && invalidColony == 0, "LOCAL_FOOTPRINT", $"{invalidStart} start cells and {invalidColony} colony cells violate local terrain requirements.");
			Require(overlaps == 0, "LOCAL_OVERLAP", $"{overlaps} occupied footprint cells overlap.");
			Require(semantics == 0, "NATIVE_SEMANTICS", $"{semantics} native cells differ from generated semantics or height.");
			Require(!actorMismatch, "NATIVE_ACTORS", "Saved actors differ from the validated placement plan.");
			Require(!settings.OriginalSurfaceRelations || contacts == 0, "ORIGINAL_SURFACE_CONTACT", $"{contacts} forbidden native surface contacts.");
			result.RegionsPolicy["validator"] = result.ValidatorName;
			result.RegionsPolicy["accepted"] = result.Accepted;
			result.RegionsPolicy["hard_failures"] = new JArray(result.HardFailures.Select(f => f.ToJson()));
			result.RegionsPolicy["accessibility_requirement"] = "NOT_REQUIRED";
			result.RegionsPolicy["strategic_routes_requirement"] = "NOT_REQUIRED";
			result.RegionsPolicy["flying_unit_availability_requirement"] = "NOT_REQUIRED";
			result.RegionsPolicy["original_surface_dirt_placement_enforced"] = settings.OriginalSurfaceRelations;
			result.RegionsPolicy["native_semantic_mismatches"] = semantics;
			result.RegionsPolicy["forbidden_surface_contacts"] = contacts;
			result.RegionsPolicy["invalid_start_cells"] = invalidStart;
			result.RegionsPolicy["invalid_colony_cells"] = invalidColony;
			result.RegionsPolicy["footprint_overlap_cells"] = overlaps;
			result.RegionsPolicy["production_exit_failures"] = exits;
			result.RegionsPolicy["production_exit_details"] = exitDetails;
			result.RegionsPolicy["land_cover_cost_contract_accepted"] = costsAccepted;
			result.RegionsPolicy["terrain_costs"] = new JObject(ground.TerrainSpeeds.Select(kv =>
				new JProperty(kv.Key, new JObject { ["speed_percent"] = kv.Value.Speed, ["pathing_cost"] = kv.Value.Cost })));
			result.RegionsPolicy["terrain_passable_cells"] = grid.TerrainPassableCells;
			result.RegionsPolicy["static_blocked_cells"] = grid.StaticBlockedCells;
			result.RegionsPolicy["transit_only_cells"] = transitCells;
			result.RegionsPolicy["starting_base_variants_checked"] = starts.Length;
			result.RegionsPolicy["neutral_colonies"] = colonies.Length;
			result.RegionsPolicy["world_initialization"] = "NOT_RUN_STATIC_VALIDATOR";
			return result;
		}
	}
}
