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
		static void ValidateChaos(Map map, RmgGenerationResult generation, Grid occupied, Grid source,
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
			if (!guaranteed) result.HardFailures.Add(new RmgValidationIssue("CHAOS_WASPS", "Every finished Chaos land component must have a neutral Wasps nest excluded from starting ownership."));
			var starts = NativeStartingPositions(generation).ToArray();
			var access = starts.Select(start => (Island: islands.Label(start), Cells: AccessCells(occupied, species.SelectMany(s => Footprint(map, s.BaseActor, start + s.BaseActorOffset).Coverage))))
				.Concat(colonies.Select(c => (Island: islands.Label(c.Location), Cells: AccessCells(occupied, c.Coverage)))).ToArray();
			var reachable = access.All(a => islandNests.TryGetValue(a.Island, out var nest) && nest.Any(n => CommonComponent(walk, new[] { a.Cells, AccessCells(occupied, n.Coverage) }) >= 0));
			if (!reachable) result.HardFailures.Add(new RmgValidationIssue("CHAOS_NEST_ACCESS", "A start or colony cannot reach its island's Wasps nest after footprints are placed."));
			var distance = DividedLandsGeometry.WaterDistances(terrain.Passable.Select(p => !p).ToArray(), generation.Settings.MapSize);
			var shore = colonies.Select(c => c.Blocked.Min(p => distance[source.Index(p)]) - 1).ToArray();
			if (shore.Any(d => d < 2)) result.HardFailures.Add(new RmgValidationIssue("CHAOS_SHORE", "A colony leaves fewer than two dry cells beside water."));
			var settings = generation.Settings; var biomeCells = new int[4]; var badTemplates = 0;
			for (var y = 0; y < settings.MapSize; y++)
				for (var x = 0; x < settings.MapSize; x++)
				{
					var tile = map.Tiles[new CPos(x + generation.Profile.CordonWidth, y + generation.Profile.CordonWidth)];
					var band = tile.Type / 256;
					if (band >= 4 || tile.Type % 256 > 100 || tile.Index > 3) badTemplates++;
					else biomeCells[band]++;
				}

			var mixed = settings.ChaosBiomes != RmgChaosBiomes.Single;
			if (badTemplates != 0 || (mixed ? map.Tileset != "CHAOS" || biomeCells.Any(n => n == 0) : map.Tileset != settings.Tileset || biomeCells.Skip(1).Any(n => n != 0)))
				result.HardFailures.Add(new RmgValidationIssue("CHAOS_BIOMES", "Saved terrain does not match the selected single or mixed biome mode."));
			result.RegionsPolicy["chaos_tileset"] = map.Tileset;
			result.RegionsPolicy["chaos_biome_cells"] = new JArray(biomeCells);
			result.RegionsPolicy["chaos_island_areas"] = new JArray(islands.Sizes);
			result.RegionsPolicy["chaos_mandatory_nests"] = new JArray(nests.Select(n => new JArray(n.Location.X - generation.Profile.CordonWidth, n.Location.Y - generation.Profile.CordonWidth)));
			result.RegionsPolicy["chaos_islands"] = islands.Sizes.Count;
			result.RegionsPolicy["chaos_mandatory_neutral_nests"] = nests.Length;
			result.RegionsPolicy["chaos_nest_access"] = reachable;
			result.RegionsPolicy["chaos_shore_clearances_native"] = new JArray(shore);
			result.RegionsPolicy["symmetry_requirement"] = "NOT_REQUIRED";
		}
	}
}
