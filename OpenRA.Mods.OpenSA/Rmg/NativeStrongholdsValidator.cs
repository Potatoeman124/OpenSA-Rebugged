#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public static partial class NativeMovementValidator
	{
		static void ValidateStrongholds(Map map, RmgGenerationResult generation, Grid occupied, Grid source,
			StartingUnitsInfo[] species, ActorFootprint[] colonies, RmgNativeMovementValidationResult result)
		{
			var settings = generation.Settings; var starts = NativeStartingPositions(generation).ToArray();
			var access = starts.Select(start => AccessCells(occupied, species.SelectMany(s => Footprint(map, s.BaseActor, start + s.BaseActorOffset).Coverage)))
				.Concat(colonies.Select(c => AccessCells(occupied, c.Coverage))).ToArray();
			var connected = CommonComponent(Components(occupied), access) >= 0;
			if (!connected) result.HardFailures.Add(new RmgValidationIssue("STRONGHOLDS_ACCESS", "A stronghold or colony is inaccessible after actual footprints are placed."));
			var terrain = TerrainOnlyGrid(source);
			var distances = DividedLandsGeometry.WaterDistances(terrain.Passable.Select(p => !p).ToArray(), settings.MapSize);
			var shore = colonies.Select(c => c.Blocked.Min(p => distances[source.Index(p)]) - 1).ToArray();
			if (shore.Any(d => d < 2)) result.HardFailures.Add(new RmgValidationIssue("STRONGHOLDS_SHORE", "A colony leaves fewer than two cells between its blocking footprint and water."));
			var geometry = new StrongholdsGeometry(settings, generation.Profile);
			var blockedRoads = 0;
			for (var y = 0; y < settings.MapSize; y++)
				for (var x = 0; x < settings.MapSize; x++)
				{
					if (geometry.RoadDistance(x, y) > 2 || starts.Any(p => Math.Max(Math.Abs(x + source.Left - p.X), Math.Abs(y + source.Top - p.Y)) <= 8)) continue;
					if (!occupied.Passable[y * settings.MapSize + x]) blockedRoads++;
				}

			if (blockedRoads > 0) result.HardFailures.Add(new RmgValidationIssue("STRONGHOLDS_ENTRANCES", $"{blockedRoads} cells in reserved entrance paths are blocked."));
			result.RegionsPolicy["strongholds_connected"] = connected;
			result.RegionsPolicy["strongholds_blocked_entrance_cells"] = blockedRoads;
			result.RegionsPolicy["strongholds_shore_clearances_native"] = new JArray(shore);
			result.RegionsPolicy["strongholds_minimum_shore_clearance_native"] = shore.Length == 0 ? null : new JValue(shore.Min());
			result.RegionsPolicy["symmetry_requirement"] = "NOT_REQUIRED";
		}
	}
}
