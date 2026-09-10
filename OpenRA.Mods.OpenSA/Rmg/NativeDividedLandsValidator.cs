#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public static partial class NativeMovementValidator
	{
		static void ValidateDividedLands(Map map, RmgGenerationResult generation, Grid occupied, Grid source,
			StartingUnitsInfo[] species, ActorFootprint[] colonies, RmgNativeMovementValidationResult result)
		{
			var settings = generation.Settings; var starts = NativeStartingPositions(generation).ToArray();
			var access = starts.Select(start => AccessCells(occupied, species.SelectMany(s => Footprint(map, s.BaseActor, start + s.BaseActorOffset).Coverage)))
				.Concat(colonies.Select(c => AccessCells(occupied, c.Coverage))).ToArray();
			var components = Components(occupied);
			var homes = access.Take(starts.Length).Select(a => a.Select(components.Label).Where(l => l >= 0)
				.OrderByDescending(l => components.Sizes[l]).FirstOrDefault(-1)).ToArray();
			var local = homes.All(h => h >= 0) && access.Skip(starts.Length).All(a => a.Any(c => homes.Contains(components.Label(c))));
			var connected = CommonComponent(components, access) >= 0;
			var topology = settings.LandCrossings == RmgLandCrossings.None ? homes.Distinct().Count() == starts.Length : connected;
			if (!local || !topology) result.HardFailures.Add(new RmgValidationIssue("DIVIDED_LANDS_ACCESS", "Home colonies or configured border crossings are inaccessible after actual footprints are placed."));

			// Start actors occupy their anchors; use an accessible cell in each actual home component.
			var homeCells = access.Take(starts.Length).Select((a, i) => a.FirstOrDefault(c => components.Label(c) == homes[i]))
				.Select(p => new RmgPoint(p.X - occupied.Left, p.Y - occupied.Top)).ToArray();
			if (local && topology)
				try { result.RegionsPolicy["divided_lands_occupied_topology"] = DividedLandsTopology.ValidateGround(occupied.Passable, settings, homeCells); }
				catch (RmgGenerationRejectedException e) { result.HardFailures.Add(new RmgValidationIssue("DIVIDED_LANDS_OCCUPIED_CROSSINGS", e.Message)); }

			var waterDistances = DividedLandsGeometry.WaterDistances(TerrainOnlyGrid(source).Passable.Select(p => !p).ToArray(), settings.MapSize);
			var shoreClearances = colonies.Select(c => c.Blocked.Min(p => waterDistances[source.Index(p)]) - 1).ToArray();
			if (shoreClearances.Any(d => d < 2)) result.HardFailures.Add(new RmgValidationIssue("DIVIDED_LANDS_SHORE_CLEARANCE", "A colony leaves fewer than two native cells between its footprint and water."));
			result.RegionsPolicy["divided_lands_colony_shore_clearance_native"] = new JArray(shoreClearances);
			result.RegionsPolicy["divided_lands_minimum_shore_clearance_native"] = shoreClearances.Length == 0 ? null : new JValue(shoreClearances.Min());

			var terrain = TerrainOnlyGrid(source); var profiles = new List<long[]>();
			foreach (var start in starts)
			{
				var distances = BattlefieldDistances(terrain, start);
				profiles.Add(colonies.GroupBy(c => c.Type).OrderBy(g => g.Key, StringComparer.Ordinal)
					.SelectMany(group => group.Select(c => distances[terrain.Index(c.Location)]).OrderBy(d => d))
					.Concat(starts.Where(s => s != start).Select(s => distances[terrain.Index(s)]).OrderBy(d => d)).ToArray());
			}

			var parity = profiles.All(p => p.SequenceEqual(profiles[0]) && (settings.LandCrossings == RmgLandCrossings.None || p.All(d => d != long.MaxValue)));
			if (!parity) result.HardFailures.Add(new RmgValidationIssue("DIVIDED_LANDS_PARITY", "Players have unequal typed colony opportunities or terrain travel costs."));
			result.RegionsPolicy["divided_lands_home_access"] = local;
			result.RegionsPolicy["divided_lands_all_connected"] = connected;
			result.RegionsPolicy["divided_lands_home_components"] = homes.Distinct().Count();
			result.RegionsPolicy["divided_lands_weighted_pool_parity"] = parity;
			result.RegionsPolicy["divided_lands_weighted_profile"] = new JArray(profiles[0]);
		}
	}
}
