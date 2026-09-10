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
		static void ValidateLabyrinth(Map map, RmgGenerationResult generation, Grid occupied, Grid source,
			StartingUnitsInfo[] species, ActorFootprint[] colonies, RmgNativeMovementValidationResult result)
		{
			var starts = NativeStartingPositions(generation).ToArray();
			var access = starts.Select(start => AccessCells(occupied, species.SelectMany(s => Footprint(map, s.BaseActor, start + s.BaseActorOffset).Coverage)))
				.Concat(colonies.Select(c => AccessCells(occupied, c.Coverage))).ToArray();
			var connected = CommonComponent(Components(occupied), access) >= 0;
			if (!connected) result.HardFailures.Add(new RmgValidationIssue("LABYRINTH_ACCESS", "A labyrinth starting area or colony is inaccessible after placement."));
			var terrain = TerrainOnlyGrid(source);
			var distances = DividedLandsGeometry.WaterDistances(terrain.Passable.Select(p => !p).ToArray(), generation.Settings.MapSize);
			var shore = colonies.Select(c => c.Blocked.Min(p => distances[source.Index(p)]) - 1).ToArray();
			if (shore.Any(d => d < 2)) result.HardFailures.Add(new RmgValidationIssue("LABYRINTH_SHORE", "A labyrinth colony leaves fewer than two cells beside water."));
			var geometry = new LabyrinthGeometry(generation.Settings, generation.Profile); var blocked = 0;
			for (var i = 0; i < geometry.PassageDistance.Length; i++)
				if (geometry.PassageDistance[i] <= geometry.HalfWidth && !terrain.Passable[i]) blocked++;
			if (blocked > 0) result.HardFailures.Add(new RmgValidationIssue("LABYRINTH_PASSAGES", $"{blocked} planned passage cells are blocked by water."));
			result.RegionsPolicy["labyrinth_connected"] = connected;
			result.RegionsPolicy["labyrinth_blocked_passage_cells"] = blocked;
			result.RegionsPolicy["labyrinth_shore_clearances_native"] = new JArray(shore);
			result.RegionsPolicy["symmetry_requirement"] = "NOT_REQUIRED";
		}
	}
}
