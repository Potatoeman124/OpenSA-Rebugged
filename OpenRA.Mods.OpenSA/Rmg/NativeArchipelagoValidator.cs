#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System.Linq;
using Newtonsoft.Json.Linq;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public static partial class NativeMovementValidator
	{
		static void ValidateArchipelago(Map map, RmgGenerationResult generation, Grid occupied, Grid source,
			StartingUnitsInfo[] species, ActorFootprint[] colonies, RmgNativeMovementValidationResult result)
		{
			var terrain = TerrainOnlyGrid(source); var islands = Components(terrain); var walk = Components(occupied);
			var mandatory = generation.Map.Actors.Select((a, i) => (a, i)).Where(p => p.a.MandatoryNeutral).ToArray();
			var definitions = map.ActorDefinitions.ToDictionary(n => n.Key, n => new ActorReference(n.Value.Value, n.Value.ToDictionary()));
			var nests = mandatory.Select(p => Footprint(map, "wasps_colony", definitions["Actor" + p.i].Get<LocationInit>().Value)).ToArray();
			var islandNests = nests.GroupBy(n => islands.Label(n.Location)).ToDictionary(g => g.Key, g => g.ToArray());
			var rule = map.Rules.Actors[SystemActors.World].TraitInfo<Traits.World.RmgStartingColonyOwnershipInfo>();
			var guaranteed = islandNests.Count == islands.Sizes.Count && islandNests.All(p => p.Key >= 0 && p.Value.Length == 1) &&
				mandatory.All(p => p.a.Type == "wasps_colony" && p.a.Owner == "Creeps" && !rule.ColonyActorNames.Contains("Actor" + p.i));
			if (!guaranteed) result.HardFailures.Add(new RmgValidationIssue("ARCHIPELAGO_WASPS", "Every finished island must have an independent neutral Wasps nest excluded from starting ownership."));
			var starts = NativeStartingPositions(generation).ToArray();
			var access = starts.Select(start => (Island: islands.Label(start), Cells: AccessCells(occupied, species.SelectMany(s => Footprint(map, s.BaseActor, start + s.BaseActorOffset).Coverage))))
				.Concat(colonies.Select(c => (Island: islands.Label(c.Location), Cells: AccessCells(occupied, c.Coverage)))).ToArray();
			var reachable = access.All(a => islandNests.TryGetValue(a.Island, out var nest) && nest.Any(n => CommonComponent(walk, new[] { a.Cells, AccessCells(occupied, n.Coverage) }) >= 0));
			if (!reachable) result.HardFailures.Add(new RmgValidationIssue("ARCHIPELAGO_NEST_ACCESS", "A start or colony cannot reach its island's Wasps nest after footprints are placed."));
			var distance = DividedLandsGeometry.WaterDistances(terrain.Passable.Select(p => !p).ToArray(), generation.Settings.MapSize);
			var shore = colonies.Select(c => c.Blocked.Min(p => distance[source.Index(p)]) - 1).ToArray();
			if (shore.Any(d => d < 2)) result.HardFailures.Add(new RmgValidationIssue("ARCHIPELAGO_SHORE", "A colony leaves fewer than two dry cells beside water."));
			result.RegionsPolicy["archipelago_islands"] = islands.Sizes.Count;
			result.RegionsPolicy["archipelago_mandatory_neutral_nests"] = nests.Length;
			result.RegionsPolicy["archipelago_nest_access"] = reachable;
			result.RegionsPolicy["archipelago_shore_clearances_native"] = new JArray(shore);
			result.RegionsPolicy["symmetry_requirement"] = "NOT_REQUIRED";
		}
	}
}
