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
		static void ValidatePlannedBattlefield(Map map, RmgGenerationResult generation, Grid occupied, Grid source,
			StartingUnitsInfo[] species, ActorFootprint[] colonies, RmgNativeMovementValidationResult result)
		{
			var starts = NativeStartingPositions(generation).ToArray();
			var access = starts.Select(start => AccessCells(occupied, species.SelectMany(s => Footprint(map, s.BaseActor, start + s.BaseActorOffset).Coverage)))
				.Concat(colonies.Select(c => AccessCells(occupied, c.Coverage))).ToArray();
			var components = Components(occupied);
			var common = CommonComponent(components, access);
			if (common < 0) result.HardFailures.Add(new RmgValidationIssue("BATTLEFIELD_ACCESS", "Player plazas and colony sites do not share a ground-access component after actual footprints are placed."));
			if (generation.Settings.GeneratorVersion == 20)
			{
				var origin = common < 0 ? -1 : occupied.Index(access[0].First(c => components.Label(c) == common));
				var loop = RingTopology.HasGroundLoop(occupied.Passable, occupied.Width, occupied.Height, origin);
				result.RegionsPolicy["ring_ground_loop_after_actors"] = loop;
				if (!loop) result.HardFailures.Add(new RmgValidationIssue("RING_LOOP_BLOCKED", "Actual colony footprints interrupt the complete ground loop around the central lake."));
			}

			var terrain = TerrainOnlyGrid(source);
			var profiles = new List<long[]>();
			foreach (var start in starts)
			{
				var distances = BattlefieldDistances(terrain, start);
				profiles.Add(colonies.GroupBy(c => c.Type).OrderBy(g => g.Key, StringComparer.Ordinal)
					.SelectMany(group => group.Select(c => distances[terrain.Index(c.Location)]).OrderBy(d => d))
					.Concat(starts.Where(s => s != start).Select(s => distances[terrain.Index(s)]).OrderBy(d => d)).ToArray());
			}

			var parity = profiles.All(p => p.All(d => d != long.MaxValue) && p.SequenceEqual(profiles[0]));
			if (!parity) result.HardFailures.Add(new RmgValidationIssue("BATTLEFIELD_WEIGHTED_PARITY", "Symmetry-equivalent players have unequal or unreachable terrain travel costs to the typed colony pool or opponents."));
			result.RegionsPolicy["battlefield_ground_access"] = common >= 0;
			result.RegionsPolicy["battlefield_connected_objectives"] = common >= 0 ? access.Length : 0;
			result.RegionsPolicy["battlefield_weighted_pool_parity"] = parity;
			result.RegionsPolicy["battlefield_weighted_profile"] = new JArray(profiles[0]);
			result.RegionsPolicy["battlefield_fairness_scope"] = "Static terrain costs and typed opportunities; faction abilities, teams, hostiles and ownership remain lobby choices.";
		}

		static long[] BattlefieldDistances(Grid grid, CPos start)
		{
			var distance = Enumerable.Repeat(long.MaxValue, grid.CellCount).ToArray();
			var queue = new PriorityQueue<int, long>();
			var origin = grid.Index(start); distance[origin] = 0; queue.Enqueue(origin, 0);
			while (queue.TryDequeue(out var index, out var priority))
			{
				if (priority != distance[index]) continue;
				var current = grid.Cell(index);
				foreach (var neighbor in grid.Neighbors(current))
				{
					var next = grid.Index(neighbor);
					if (!grid.Passable[next] || grid.Cost[next] <= 0) continue;
					var cost = current.X != neighbor.X && current.Y != neighbor.Y ? (grid.Cost[next] * 141L + 50) / 100 : grid.Cost[next];
					var candidate = priority + cost;
					if (candidate >= distance[next]) continue;
					distance[next] = candidate; queue.Enqueue(next, candidate);
				}
			}

			return distance;
		}
	}
}
