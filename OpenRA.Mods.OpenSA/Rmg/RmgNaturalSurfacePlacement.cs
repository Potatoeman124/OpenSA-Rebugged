#region Copyright & License Information
/*
 * Copyright The OpenSA Developers (see CREDITS)
 * This file is part of OpenSA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public static partial class RmgGenerator
	{
		static void FitNaturalV10PlacementsToSurfaces(RmgLogicalMap map, RmgProfile profile,
			RmgGenerationSettings settings)
		{
			var terrain = (RmgNativeTerrainIntent[])map.NativeTerrainIntents.Clone();
			var templates = (ushort[])map.TemplateIds.Clone();
			var water = (bool[])map.Obstacles.Clone();
			var provisionalStarts = map.Starts.ToArray();
			var provisionalColonies = map.Actors.Where(a => a.Owner == profile.ColonyOwner).ToArray();
			map.NaturalSurfacesFrozen = true;

			// Discard provisional gameplay anchors, never the geography they were projected onto.
			// The full materialized surface is now authoritative for all final placement.
			map.Actors.RemoveAll(actor => actor.Owner == profile.ColonyOwner || actor.Type == profile.SpawnActor);
			Array.Clear(map.StartReservations, 0, map.StartReservations.Length);
			Array.Clear(map.StructureReservations, 0, map.StructureReservations.Length);
			Array.Clear(map.RouteMasks, 0, map.RouteMasks.Length);
			Array.Fill(map.RouteIds, -1);
			Array.Clear(map.StrategicRegions, 0, map.StrategicRegions.Length);

			// Route-center clearance is conservative (five logical cells) but invisible:
			// routes may cross gravel/moss and may never convert Water to land.
			var routeCenters = new bool[map.Obstacles.Length];
			for (var y = 2; y < map.Height - 2; y++)
				for (var x = 2; x < map.Width - 2; x++)
					routeCenters[y * map.Width + x] = Enumerable.Range(-2, 5).All(dy =>
						Enumerable.Range(-2, 5).All(dx => !map.Obstacles[(y + dy) * map.Width + x + dx]));
			var routeComponent = LargestNaturalV10RouteComponent(routeCenters, map.Width);
			var waterDistance = NaturalV10NativeWaterDistance(map);
			var rules = profile.DirtPlacementRules;
			// Old territories are preferences, not constraints: a river or a moss field may
			// consume one territory's dirt. Search all reachable dirt, nearest old anchor first.
			var candidates = provisionalStarts.Select(original =>
				Enumerable.Range(8, map.Height - 16)
					.SelectMany(y => Enumerable.Range(8, map.Width - 16).Select(x => new RmgPoint(x, y)))
					.Where(point => routeComponent.Contains(map.Index(point)) &&
						rules.StartFits(map, point, waterDistance))
					.OrderBy(point => point.ManhattanDistance(original))
					.ThenBy(point => point.Y).ThenBy(point => point.X).Take(192).ToArray()).ToArray();
			var selected = new RmgPoint[provisionalStarts.Length];
			var assigned = new HashSet<int>();
			var searchNodes = 0;
			if (!ChooseStarts())
				throw new RmgGenerationRejectedException("NATURAL_DIRT_START_PLACEMENT",
					"No bounded combat-safe start assignment fits the existing dirt and Water clearance. " +
					$"Ranked candidates: {string.Join("/", candidates.Select(sites => sites.Length))}; " +
					$"route component: {routeComponent.Count}; all-territory dirt sites: {routeComponent.Count(index => rules.StartFits(map, new RmgPoint(index % map.Width, index / map.Width), waterDistance))}.");

			map.Starts.Clear();
			map.Starts.AddRange(selected);
			for (var i = 0; i < selected.Length; i++)
			{
				var index = map.GraphNodes.FindIndex(node => node.Id == $"start-{i}");
				map.GraphNodes[index] = map.GraphNodes[index] with { Location = selected[i] };
				map.Actors.Add(new RmgActorPlan(profile.SpawnActor, profile.SpawnOwner, "start", selected[i], i / 2));
				ReserveSquare(map.StartReservations, map, selected[i], 3);
			}

			// Keep each hub near its old anchor while making it reachable on unchanged land.
			var occupiedHubs = new HashSet<RmgPoint>();
			for (var i = 0; i < map.GraphNodes.Count; i++)
			{
				var node = map.GraphNodes[i];
				if (node.Role != "hub")
					continue;
				var hubCandidates = routeComponent.Select(index => new RmgPoint(index % map.Width, index / map.Width))
					.Where(point => !occupiedHubs.Contains(point) && map.Starts.All(start => point.ChebyshevDistance(start) >= 8))
					.OrderBy(point => point.ManhattanDistance(node.Location)).ThenBy(point => point.Y).ThenBy(point => point.X).ToArray();
				if (hubCandidates.Length == 0)
					throw new RmgGenerationRejectedException("NATURAL_DIRT_HUB_PLACEMENT", "No existing dry strategic hub fits the final starts.");
				map.GraphNodes[i] = node with { Location = hubCandidates[0] };
				occupiedHubs.Add(hubCandidates[0]);
			}

			ReserveNaturalRoutes(map, profile, settings);
			PlaceNaturalColonies(map, profile, settings);
			MarkNaturalV10StrategicRegions(map);
			AssignRegions(map);
			map.NaturalRelocatedStartCount = selected.Where((point, i) => point != provisionalStarts[i]).Count();
			map.NaturalReplacedColonyCount = provisionalColonies.Count(old => !map.Actors.Contains(old));
			AssertNaturalV10SurfaceAuthority(map, terrain, templates, water);

			bool ChooseStarts()
			{
				if (assigned.Count == selected.Length)
					return true;
				var next = Enumerable.Range(0, selected.Length).Where(i => !assigned.Contains(i))
					.Select(i => (Index: i, Sites: candidates[i].Where(point => assigned.All(j =>
						profile.ColonyCombatRules.StartingCombatSpaceMarginNative(point, selected[j]) >= 0)).ToArray()))
					.OrderBy(entry => entry.Sites.Length).ThenBy(entry => entry.Index).First();
				assigned.Add(next.Index);
				foreach (var point in next.Sites)
				{
					if (++searchNodes > 8192)
						break;
					selected[next.Index] = point;
					if (ChooseStarts())
						return true;
				}
				assigned.Remove(next.Index);
				return false;
			}
		}

		static void AssertNaturalV10SurfaceAuthority(RmgLogicalMap map, RmgNativeTerrainIntent[] terrain,
			ushort[] templates, bool[] water)
		{
			if (!map.NativeTerrainIntents.SequenceEqual(terrain) || !map.TemplateIds.SequenceEqual(templates) ||
				!map.Obstacles.SequenceEqual(water))
				throw new RmgGenerationRejectedException("NATURAL_SURFACE_AUTHORITY",
					"Final gameplay placement modified the authoritative generated terrain.");
		}

		static HashSet<int> LargestNaturalV10RouteComponent(bool[] centers, int width)
		{
			var seen = new bool[centers.Length];
			var largest = new HashSet<int>();
			for (var index = 0; index < centers.Length; index++)
			{
				if (!centers[index] || seen[index])
					continue;
				var component = new HashSet<int>();
				var queue = new Queue<int>();
				queue.Enqueue(index);
				seen[index] = true;
				while (queue.Count > 0)
				{
					var cell = queue.Dequeue();
					component.Add(cell);
					foreach (var next in new[] { cell - 1, cell + 1, cell - width, cell + width })
						if (next >= 0 && next < centers.Length && !seen[next] && centers[next] &&
							Math.Abs(next % width - cell % width) + Math.Abs(next / width - cell / width) == 1)
						{
							seen[next] = true;
							queue.Enqueue(next);
						}
				}
				if (component.Count > largest.Count)
					largest = component;
			}
			return largest;
		}

		static int[] NaturalV10NativeWaterDistance(RmgLogicalMap map)
		{
			var width = map.Width * 2;
			var height = map.Height * 2;
			var distance = Enumerable.Repeat(int.MaxValue, width * height).ToArray();
			var queue = new Queue<int>();
			for (var y = 0; y < height; y++)
				for (var x = 0; x < width; x++)
					if (x == 0 || y == 0 || x == width - 1 || y == height - 1 ||
						map.NativeTerrainIntents[4 * ((y / 2) * map.Width + x / 2) + (y % 2) * 2 + x % 2] == RmgNativeTerrainIntent.Water)
					{
						distance[y * width + x] = 0;
						queue.Enqueue(y * width + x);
					}
			while (queue.Count > 0)
			{
				var cell = queue.Dequeue();
				for (var dy = -1; dy <= 1; dy++)
					for (var dx = -1; dx <= 1; dx++)
					{
						var x = cell % width + dx;
						var y = cell / width + dy;
						if (x < 0 || y < 0 || x >= width || y >= height || distance[y * width + x] <= distance[cell] + 1)
							continue;
						distance[y * width + x] = distance[cell] + 1;
						queue.Enqueue(y * width + x);
					}
			}
			return distance;
		}
	}
}
