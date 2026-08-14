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
using System.Text;
using Newtonsoft.Json.Linq;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public static partial class RmgGenerator
	{
		sealed record BlockingRoutePlan(int RouteId, RmgPoint[] Centerline);

		static RmgGenerationResult GenerateBlockingTopology(RmgProfile profile, RmgGenerationSettings settings)
		{
			var map = new RmgLogicalMap(profile.LogicalWidth, profile.LogicalHeight);
			GenerateStartsAndTopology(map, profile, settings);
			foreach (var start in map.Starts)
				ReserveSquare(map.StartReservations, map, start, profile.StartRegionRadiusNative / 2);

			var routes = ReserveBlockingRoutes(map, profile, settings);
			MarkStrategicRegions(map);
			ClearBlockingStage(map);
			var startingObstacleOrbit = settings.Archetype == RmgArchetype.CentralContest ?
				CreateChokepointOrbit(map, profile, settings, routes, 0) : 0;
			PlaceColonies(map, profile, settings, true, 3, true);
			ExpandColonyJunctions(map, profile, settings);
			EnforceChokepointRouteCuts(map);
			foreach (var colony in map.Actors.Where(a => a.Owner == profile.ColonyOwner))
				ReserveNeutralColonyBlockingClearance(map, colony.LogicalLocation);
			MarkStrategicRegions(map);
			GenerateBlockingObstacleStage(map, profile, settings, startingObstacleOrbit);
			ApplyBlockingRepairs(map, profile);
			AssignRegions(map);
			MaterializeBlockingTerrain(map, profile, settings);

			var validation = ValidateBlockingTopology(map, profile, settings);
			return new RmgGenerationResult
			{
				Settings = settings,
				Profile = profile,
				Map = map,
				Validation = validation,
				LogicalHash = HashBlockingLogicalMap(map),
				ActorHash = HashActors(map),
				GraphHash = HashBlockingGraph(map)
			};
		}

		static void ReserveNeutralColonyBlockingClearance(RmgLogicalMap map, RmgPoint anchor)
		{
			// All supported neutral colonies use a 6x6 native coverage box anchored at their
			// serialized location. A 2x2 Water macro-cell first reaches the frozen distance-five
			// boundary at logical offsets -3 and +5, so reserve the exact intervening envelope.
			for (var dy = -2; dy <= 4; dy++)
				for (var dx = -2; dx <= 4; dx++)
				{
					var point = new RmgPoint(anchor.X + dx, anchor.Y + dy);
					if (map.Contains(point))
						map.StructureReservations[map.Index(point)] = true;
				}
		}

		static void EnforceChokepointRouteCuts(RmgLogicalMap map)
		{
			for (var chokeIndex = 0; chokeIndex < map.Chokepoints.Count; chokeIndex++)
			{
				var choke = map.Chokepoints[chokeIndex];
				var horizontal = choke.From.Y == choke.To.Y;
				var minimum = horizontal ? Math.Min(choke.From.X, choke.To.X) : Math.Min(choke.From.Y, choke.To.Y);
				var maximum = horizontal ? Math.Max(choke.From.X, choke.To.X) : Math.Max(choke.From.Y, choke.To.Y);
				var bit = 1UL << choke.RouteId;
				for (var y = 0; y < map.Height; y++)
					for (var x = 0; x < map.Width; x++)
					{
						var coordinate = horizontal ? x : y;
						if (coordinate < minimum || coordinate > maximum)
							continue;
						var index = map.Index(new RmgPoint(x, y));
						if (map.ChokepointIds[index] == chokeIndex)
							continue;
						map.RouteMasks[index] &= ~bit;
						map.RouteIds[index] = FirstRoute(map.RouteMasks[index]);
					}
			}
		}

		static List<BlockingRoutePlan> ReserveBlockingRoutes(RmgLogicalMap map, RmgProfile profile, RmgGenerationSettings settings)
		{
			var nodes = map.GraphNodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
			var routes = new List<BlockingRoutePlan>();
			var assigned = new HashSet<int>();
			foreach (var edge in map.GraphEdges.OrderBy(e => e.RouteId))
			{
				if (!assigned.Add(edge.RouteId))
					continue;

				var from = nodes[edge.From].Location;
				var to = nodes[edge.To].Location;
				var path = BuildBlockingCenterline(from, to, edge.RouteId, settings, map).ToArray();
				routes.Add(new BlockingRoutePlan(edge.RouteId, path));
				var partnerEdge = FindSymmetryEdge(map, edge, settings.Symmetry);
				if (partnerEdge.RouteId != edge.RouteId)
				{
					assigned.Add(partnerEdge.RouteId);
					routes.Add(new BlockingRoutePlan(partnerEdge.RouteId,
						path.Select(p => Transform(p, settings.Symmetry, map.Width, map.Height)).ToArray()));
				}
			}

			// A five-macro semantic reservation materializes as the contracted major route
			// width nine. Turns, endpoint shoulders, and colony detours are padded separately.
			var logicalRadius = settings.Archetype == RmgArchetype.Open ? 2 : 1;
			foreach (var route in routes.OrderBy(r => r.RouteId))
				foreach (var point in route.Centerline)
					for (var dy = -logicalRadius; dy <= logicalRadius; dy++)
						for (var dx = -logicalRadius; dx <= logicalRadius; dx++)
						{
							var widened = new RmgPoint(point.X + dx, point.Y + dy);
							if (!map.Contains(widened))
								continue;
							var index = map.Index(widened);
							map.RouteMasks[index] |= 1UL << route.RouteId;
							if (map.RouteIds[index] < 0)
								map.RouteIds[index] = route.RouteId;
						}

			// A nominal six-cell corridor has effective width five on a straight segment, but an
			// unpadded right-angle turn collapses to width three under the native clearance metric.
			// Pad only the turns; straight normal routes and the authored constriction stay exact.
			foreach (var route in routes)
				for (var i = 1; i < route.Centerline.Length - 1; i++)
				{
					var previous = route.Centerline[i - 1];
					var point = route.Centerline[i];
					var next = route.Centerline[i + 1];
					var firstDirection = (X: point.X - previous.X, Y: point.Y - previous.Y);
					var secondDirection = (X: next.X - point.X, Y: next.Y - point.Y);
					if (firstDirection == secondDirection)
						continue;
					for (var dy = -logicalRadius - 1; dy <= logicalRadius + 1; dy++)
						for (var dx = -logicalRadius - 1; dx <= logicalRadius + 1; dx++)
						{
							var padded = new RmgPoint(point.X + dx, point.Y + dy);
							if (!map.Contains(padded))
								continue;
							var index = map.Index(padded);
							map.RouteMasks[index] |= 1UL << route.RouteId;
							if (map.RouteIds[index] < 0)
								map.RouteIds[index] = route.RouteId;
						}
				}

			if (settings.Archetype == RmgArchetype.Open)
				foreach (var route in routes)
					foreach (var point in route.Centerline.Take(8))
						for (var dy = -3; dy <= 3; dy++)
							for (var dx = -3; dx <= 3; dx++)
							{
								var shoulder = new RmgPoint(point.X + dx, point.Y + dy);
								if (!map.Contains(shoulder))
									continue;
								var index = map.Index(shoulder);
								map.RouteMasks[index] |= 1UL << route.RouteId;
								if (map.RouteIds[index] < 0)
									map.RouteIds[index] = route.RouteId;
							}

			return routes;
		}

		static IEnumerable<RmgPoint> BuildBlockingCenterline(RmgPoint from, RmgPoint to, int routeId,
			RmgGenerationSettings settings, RmgLogicalMap map)
		{
			var hubIndex = routeId % 2;
			var waypoints = new List<RmgPoint> { from };
			if (settings.Archetype == RmgArchetype.CentralContest && (routeId == 0 || routeId == 4))
			{
				// Route zero owns a stable choke-ready shoulder outside every start, colony,
				// and hub clearance. The second canonical start uses a separated parallel
				// lane so its route cannot turn that shoulder into a strategic junction.
				var rotational = settings.Symmetry == RmgSymmetry.Rotate180;
				var lane = routeId == 0 ? rotational ? 30 : 29 : rotational ? 38 : 37;
				switch (settings.Symmetry)
				{
					case RmgSymmetry.MirrorHorizontal:
						waypoints.Add(new RmgPoint(lane, from.Y));
						waypoints.Add(new RmgPoint(lane, to.Y));
						break;
					case RmgSymmetry.MirrorVertical:
						waypoints.Add(new RmgPoint(from.X, lane));
						waypoints.Add(new RmgPoint(to.X, lane));
						break;
					case RmgSymmetry.Rotate180:
						if (routeId == 0)
						{
							// Approach the choke from the start side of its cut, then cross once
							// between the start and hub. This prevents an alternate segment of the
							// same named route from being removed by the authored cut.
							waypoints.Add(new RmgPoint(lane, 18));
							waypoints.Add(new RmgPoint(lane, to.Y));
						}
						else
						{
							waypoints.Add(new RmgPoint(lane, from.Y));
							waypoints.Add(new RmgPoint(lane, to.Y));
						}

						break;
				}

				waypoints.Add(to);
				return RasterizeWaypoints(waypoints);
			}

			switch (settings.Symmetry)
			{
				case RmgSymmetry.MirrorHorizontal:
				{
					var towardCenter = from.X < map.Width / 2 ? 1 : -1;
					var laneOffset = hubIndex == 0 ? 13 * towardCenter : -7 * towardCenter;
					var laneX = Math.Clamp(from.X + laneOffset, 7, map.Width - 8);
					waypoints.Add(new RmgPoint(laneX, from.Y));
					waypoints.Add(new RmgPoint(laneX, to.Y));
					break;
				}
				case RmgSymmetry.MirrorVertical:
				{
					var towardCenter = from.Y < map.Height / 2 ? 1 : -1;
					var laneOffset = hubIndex == 0 ? 13 * towardCenter : -7 * towardCenter;
					var laneY = Math.Clamp(from.Y + laneOffset, 7, map.Height - 8);
					waypoints.Add(new RmgPoint(from.X, laneY));
					waypoints.Add(new RmgPoint(to.X, laneY));
					break;
				}
				case RmgSymmetry.Rotate180:
				{
					// Give each start two separated vertical lanes. The symmetry partner is derived
					// by transformation, so this remains exactly rotational while avoiding the long
					// overlapping horizontal bands produced by the Version 1 straight-line skeleton.
					var towardCenter = from.X < map.Width / 2 ? 1 : -1;
					var laneOffset = hubIndex == 0 ? 13 * towardCenter : -7 * towardCenter;
					var laneX = Math.Clamp(from.X + laneOffset, 7, map.Width - 8);
					waypoints.Add(new RmgPoint(laneX, from.Y));
					waypoints.Add(new RmgPoint(laneX, to.Y));
					break;
				}
			}

			waypoints.Add(to);
			return RasterizeWaypoints(waypoints);
		}

		static IEnumerable<RmgPoint> RasterizeWaypoints(IReadOnlyList<RmgPoint> waypoints)
		{
			var result = new List<RmgPoint>();
			for (var i = 0; i < waypoints.Count - 1; i++)
				foreach (var point in RasterizeLine(waypoints[i], waypoints[i + 1]))
					if (result.Count == 0 || result[^1] != point)
						result.Add(point);
			return result;
		}

		static RmgGraphEdge FindSymmetryEdge(RmgLogicalMap map, RmgGraphEdge edge, RmgSymmetry symmetry)
		{
			var nodes = map.GraphNodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
			var from = Transform(nodes[edge.From].Location, symmetry, map.Width, map.Height);
			var to = Transform(nodes[edge.To].Location, symmetry, map.Width, map.Height);
			return map.GraphEdges.Single(candidate => nodes[candidate.From].Location == from && nodes[candidate.To].Location == to);
		}

		static void MarkStrategicRegions(RmgLogicalMap map)
		{
			foreach (var hub in map.GraphNodes.Where(n => n.Role == "hub"))
				ReserveSquare(map.StrategicRegions, map, hub.Location, 6);
			for (var i = 0; i < map.RouteMasks.Length; i++)
				if (BitCount(map.RouteMasks[i]) > 1)
					map.StrategicRegions[i] = true;
			foreach (var colony in map.Actors.Where(a => a.Role == "central-contest"))
				ReserveSquare(map.StrategicRegions, map, colony.LogicalLocation, 4);
		}

		static void ExpandColonyJunctions(RmgLogicalMap map, RmgProfile profile, RmgGenerationSettings settings)
		{
			var routesBeforeExpansion = (ulong[])map.RouteMasks.Clone();
			var expandedJunctions = new HashSet<RmgPoint>();
			foreach (var start in map.Starts)
				ExpandOrbit(start);
			foreach (var colony in map.Actors.Where(a => a.Owner == profile.ColonyOwner))
				ExpandOrbit(colony.LogicalLocation);

			void ExpandOrbit(RmgPoint center)
			{
				if (!expandedJunctions.Add(center))
					return;

				var partner = Transform(center, settings.Symmetry, map.Width, map.Height);
				expandedJunctions.Add(partner);
				var routeMask = RouteMaskNear(center);
				if (!partner.Equals(center))
					routeMask |= TransformRouteMask(RouteMaskNear(partner));
				if (routeMask == 0)
					return;

				var partnerRouteMask = TransformRouteMask(routeMask);
				if (partner.Equals(center))
					Expand(center, routeMask | partnerRouteMask);
				else
				{
					Expand(center, routeMask);
					Expand(partner, partnerRouteMask);
				}
			}

			ulong RouteMaskNear(RmgPoint center)
			{
				ulong routeMask = 0;
				for (var dy = -2; dy <= 4; dy++)
					for (var dx = -2; dx <= 4; dx++)
					{
						var point = new RmgPoint(center.X + dx, center.Y + dy);
						if (map.Contains(point))
							routeMask |= routesBeforeExpansion[map.Index(point)];
					}

				return routeMask;
			}

			ulong TransformRouteMask(ulong routeMask)
			{
				ulong transformed = 0;
				foreach (var edge in map.GraphEdges)
					if ((routeMask & (1UL << edge.RouteId)) != 0)
						transformed |= 1UL << FindSymmetryEdge(map, edge, settings.Symmetry).RouteId;
				return transformed;
			}

			void Expand(RmgPoint center, ulong routeMask)
			{
				const int WindowRadius = 9;
				var routeDilation = settings.Archetype == RmgArchetype.Open ? 4 : 3;
				for (var dy = -WindowRadius; dy <= WindowRadius; dy++)
					for (var dx = -WindowRadius; dx <= WindowRadius; dx++)
					{
						var point = new RmgPoint(center.X + dx, center.Y + dy);
						if (!map.Contains(point))
							continue;
						if (!RouteWithinDilation(point))
							continue;
						var index = map.Index(point);
						if (map.Obstacles[index])
							continue;
						var expansionMask = routeMask;
						foreach (var choke in map.Chokepoints)
							if (DistanceToSegment(point, choke.From, choke.To) <= 4)
								expansionMask &= ~(1UL << choke.RouteId);
						if (expansionMask == 0)
							continue;
						map.RouteMasks[index] |= expansionMask;
						if (map.RouteIds[index] < 0)
							map.RouteIds[index] = FirstRoute(expansionMask);
					}

				bool RouteWithinDilation(RmgPoint point)
				{
					for (var nearbyY = point.Y - routeDilation; nearbyY <= point.Y + routeDilation; nearbyY++)
						for (var nearbyX = point.X - routeDilation; nearbyX <= point.X + routeDilation; nearbyX++)
						{
							var nearby = new RmgPoint(nearbyX, nearbyY);
							if (map.Contains(nearby) && (routesBeforeExpansion[map.Index(nearby)] & routeMask) != 0)
								return true;
						}

					return false;
				}
			}

			static int DistanceToSegment(RmgPoint point, RmgPoint from, RmgPoint to)
			{
				var minimumX = Math.Min(from.X, to.X);
				var maximumX = Math.Max(from.X, to.X);
				var minimumY = Math.Min(from.Y, to.Y);
				var maximumY = Math.Max(from.Y, to.Y);
				var dx = point.X < minimumX ? minimumX - point.X : point.X > maximumX ? point.X - maximumX : 0;
				var dy = point.Y < minimumY ? minimumY - point.Y : point.Y > maximumY ? point.Y - maximumY : 0;
				return Math.Max(dx, dy);
			}
		}

		static void GenerateBlockingObstacleStage(RmgLogicalMap map, RmgProfile profile, RmgGenerationSettings settings,
			int startingObstacleOrbit)
		{
			Exception lastFailure = null;
			var baselineObstacles = (bool[])map.Obstacles.Clone();
			var baselineRegionIds = (int[])map.ObstacleRegionIds.Clone();
			var baselineRegions = map.ObstacleRegions.ToArray();
			for (var attempt = 0; attempt < profile.MaximumTopologyAttempts; attempt++)
			{
				Array.Copy(baselineObstacles, map.Obstacles, map.Obstacles.Length);
				Array.Copy(baselineRegionIds, map.ObstacleRegionIds, map.ObstacleRegionIds.Length);
				map.ObstacleRegions.Clear();
				map.ObstacleRegions.AddRange(baselineRegions);
				map.Repairs.Clear();
				Array.Clear(map.RepairChanges, 0, map.RepairChanges.Length);
				map.RepairCount = 0;
				try
				{
					GenerateSymmetricObstacleRegions(map, profile, settings, attempt, startingObstacleOrbit);
					map.RetryCount = attempt;
					return;
				}
				catch (InvalidOperationException e)
				{
					lastFailure = e;
				}
			}

			throw new RmgGenerationRejectedException("TOPOLOGY_ATTEMPTS_EXHAUSTED",
				$"Blocking topology exhausted {profile.MaximumTopologyAttempts} deterministic attempts. Last failure: {lastFailure?.Message}",
				lastFailure);
		}

		static void ClearBlockingStage(RmgLogicalMap map)
		{
			Array.Clear(map.Obstacles, 0, map.Obstacles.Length);
			Array.Fill(map.ObstacleRegionIds, -1);
			Array.Fill(map.ChokepointIds, -1);
			Array.Clear(map.RepairChanges, 0, map.RepairChanges.Length);
			map.ObstacleRegions.Clear();
			map.Chokepoints.Clear();
			map.Repairs.Clear();
			map.RepairCount = 0;
		}

		static int CreateChokepointOrbit(RmgLogicalMap map, RmgProfile profile, RmgGenerationSettings settings,
			IReadOnlyList<BlockingRoutePlan> routes, int obstacleOrbit)
		{
			var nodes = map.GraphNodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
			var straightCandidates = 0;
			var symmetryRejected = 0;
			var startRejected = 0;
			var colonyRejected = 0;
			var reservationRejected = 0;
			var routeOverlapRejected = 0;
			var routeDiagnostics = new Dictionary<int, int[]>();
			foreach (var route in routes.OrderBy(r => r.RouteId))
			{
				var routeCounts = new int[6];
				routeDiagnostics.Add(route.RouteId, routeCounts);
				var edge = map.GraphEdges.Single(e => e.RouteId == route.RouteId);
				var partnerEdge = FindSymmetryEdge(map, edge, settings.Symmetry);
				if (route.RouteId > partnerEdge.RouteId)
					continue;

				var points = route.Centerline;
				for (var first = 3; first + 5 <= points.Length; first++)
				{
					var segment = points.Skip(first).Take(2).ToArray();
					var shoulderRun = points.Skip(first - 3).Take(8).ToArray();
					var horizontal = shoulderRun.All(p => p.Y == shoulderRun[0].Y) &&
						shoulderRun.Select(p => p.X).Distinct().Count() == shoulderRun.Length;
					var vertical = shoulderRun.All(p => p.X == shoulderRun[0].X) &&
						shoulderRun.Select(p => p.Y).Distinct().Count() == shoulderRun.Length;
					if (!horizontal && !vertical)
						continue;
					straightCandidates++;
					routeCounts[0]++;
					var wallA = new HashSet<RmgPoint>();
					var wallB = new HashSet<RmgPoint>();
					var aperture = new HashSet<RmgPoint>();
					foreach (var p in segment)
					{
						if (horizontal)
						{
							aperture.Add(p);
							aperture.Add(new RmgPoint(p.X, p.Y + 1));
						}
						else
						{
							aperture.Add(p);
							aperture.Add(new RmgPoint(p.X + 1, p.Y));
						}
					}
					var wallLine = horizontal ?
						Enumerable.Range(Math.Min(segment[0].X, segment[1].X) - 1, 4)
							.Select(x => new RmgPoint(x, segment[0].Y)) :
						Enumerable.Range(Math.Min(segment[0].Y, segment[1].Y) - 1, 4)
							.Select(y => new RmgPoint(segment[0].X, y));
					foreach (var p in wallLine)
						if (horizontal)
						{
							wallA.Add(new RmgPoint(p.X, p.Y - 2));
							wallA.Add(new RmgPoint(p.X, p.Y - 1));
							wallB.Add(new RmgPoint(p.X, p.Y + 2));
							wallB.Add(new RmgPoint(p.X, p.Y + 3));
						}
						else
						{
							wallA.Add(new RmgPoint(p.X - 2, p.Y));
							wallA.Add(new RmgPoint(p.X - 1, p.Y));
							wallB.Add(new RmgPoint(p.X + 2, p.Y));
							wallB.Add(new RmgPoint(p.X + 3, p.Y));
						}

					var walls = wallA.Concat(wallB).ToArray();
					var partnerWallA = wallA.Select(p => Transform(p, settings.Symmetry, map.Width, map.Height)).ToHashSet();
					var partnerWallB = wallB.Select(p => Transform(p, settings.Symmetry, map.Width, map.Height)).ToHashSet();
					var partnerAperture = aperture.Select(p => Transform(p, settings.Symmetry, map.Width, map.Height)).ToHashSet();
					if (MinimumDistance(aperture, partnerAperture) < 8 ||
						MinimumDistance(wallA, partnerWallA) < 3 ||
						MinimumDistance(wallB, partnerWallB) < 3)
					{
						symmetryRejected++;
						routeCounts[1]++;
						continue;
					}
					if (GenericStartAnchorDistance(aperture.Concat(partnerAperture), map) < 24)
					{
						startRejected++;
						routeCounts[2]++;
						continue;
					}
					if (GenericColonyCoverageDistance(aperture.Concat(partnerAperture), map, profile) < 10)
					{
						colonyRejected++;
						routeCounts[3]++;
						continue;
					}
					if (walls.Any(p => !map.Contains(p) || map.StartReservations[map.Index(p)] ||
						map.StructureReservations[map.Index(p)] || map.StrategicRegions[map.Index(p)]))
					{
						reservationRejected++;
						routeCounts[4]++;
						continue;
					}
					var allowedBits = (1UL << route.RouteId) | (1UL << partnerEdge.RouteId);
					if (walls.Concat(walls.Select(p => Transform(p, settings.Symmetry, map.Width, map.Height)))
						.Any(p => (map.RouteMasks[map.Index(p)] & ~allowedBits) != 0))
					{
						routeOverlapRejected++;
						routeCounts[5]++;
						continue;
					}

					foreach (var wall in walls.Concat(walls.Select(p => Transform(p, settings.Symmetry, map.Width, map.Height))).Distinct())
					{
						var index = map.Index(wall);
						map.RouteMasks[index] &= ~allowedBits;
						map.RouteIds[index] = FirstRoute(map.RouteMasks[index]);
					}

					obstacleOrbit = AddSymmetricRegion(map, settings, wallA, obstacleOrbit);
					obstacleOrbit = AddSymmetricRegion(map, settings, wallB, obstacleOrbit);
					var chokeIndex = map.Chokepoints.Count;
					foreach (var point in aperture)
						map.ChokepointIds[map.Index(point)] = chokeIndex;
					foreach (var point in partnerAperture)
						map.ChokepointIds[map.Index(point)] = chokeIndex + 1;
					map.Chokepoints.Add(new RmgChokepoint($"choke-{chokeIndex}", route.RouteId, 0,
						segment[0], segment[^1], 4, profile.ChokepointWidthNative));
					map.Chokepoints.Add(new RmgChokepoint($"choke-{chokeIndex + 1}", partnerEdge.RouteId, 0,
						Transform(segment[0], settings.Symmetry, map.Width, map.Height),
						Transform(segment[^1], settings.Symmetry, map.Width, map.Height), 4, profile.ChokepointWidthNative));
					return obstacleOrbit;
				}
			}

			var routeRejections = string.Join(',', routeDiagnostics.OrderBy(p => p.Key).Select(p =>
				$"{p.Key}:straight={p.Value[0]}/symmetry={p.Value[1]}/start={p.Value[2]}/colony={p.Value[3]}/reservation={p.Value[4]}/overlap={p.Value[5]}"));
			throw new RmgGenerationRejectedException("CHOKEPOINT_PLACEMENT",
				$"No route segment satisfies the frozen chokepoint placement envelope. " +
				$"straight={straightCandidates}, symmetry={symmetryRejected}, start={startRejected}, colony={colonyRejected}, " +
				$"reservation={reservationRejected}, route-overlap={routeOverlapRejected}. " +
				$"nodes={string.Join(',', map.GraphNodes.OrderBy(n => n.Id, StringComparer.Ordinal).Select(n => $"{n.Id}:{n.Location}"))}; " +
				$"routes={string.Join(',', routes.OrderBy(r => r.RouteId).Select(r => $"{r.RouteId}:{r.Centerline.Length}"))}; " +
				$"route-rejections={routeRejections}.");
		}

		static void GenerateSymmetricObstacleRegions(RmgLogicalMap map, RmgProfile profile, RmgGenerationSettings settings,
			int attempt, int startingOrbit)
		{
			var random = DeterministicRandom.ForStream(settings, profile, $"topology-attempt-{attempt}");
			var target = map.Obstacles.Length * profile.ObstacleDensityTarget(settings.Archetype) / 100;
			var orbit = startingOrbit;
			var initiallyEligibleCells = Enumerable.Range(0, map.Obstacles.Length)
				.Count(index => ObstacleCellEligible(map, new RmgPoint(index % map.Width, index / map.Width)));
			var compactFallback = attempt == profile.MaximumTopologyAttempts - 1;
			for (var placementAttempt = 0; !compactFallback && placementAttempt < 4096 && map.Obstacles.Count(x => x) < target; placementAttempt++)
			{
				var remaining = target - map.Obstacles.Count(x => x);
				if (remaining < 2 * profile.ObstacleRegionMinimumLogical)
					break;
				var desired = Math.Min(profile.ObstacleRegionMaximumLogical,
					Math.Min(24 + random.NextInt(25), remaining / 2));
				var seed = new RmgPoint(random.NextInt(3, map.Width - 3), random.NextInt(3, map.Height - 3));
				var partner = Transform(seed, settings.Symmetry, map.Width, map.Height);
				if (!IsCanonical(seed, partner, map.Width))
					continue;

				var region = GrowRegion(map, settings, random, seed, desired);
				if (region.Count < profile.ObstacleRegionMinimumLogical)
					continue;
				try
				{
					orbit = AddSymmetricRegion(map, settings, region, orbit);
				}
				catch (InvalidOperationException)
				{
					// A rejected candidate consumes its deterministic stream values but does not mutate the map.
				}
			}

			var range = profile.ObstacleDensityRange(settings.Archetype);
			var minimumTarget = (int)Math.Ceiling(map.Obstacles.Length * range.Minimum / 100D);
			var fillRandom = DeterministicRandom.ForStream(settings, profile, $"topology-fill-{attempt}");
			for (var pass = 0; pass < 4 && map.Obstacles.Count(x => x) < minimumTarget; pass++)
				for (var y = 2; y < map.Height - 2 && map.Obstacles.Count(x => x) < minimumTarget; y++)
					for (var x = 2; x < map.Width - 2 && map.Obstacles.Count(x => x) < minimumTarget; x++)
					{
						var seed = new RmgPoint(x, y);
						var partner = Transform(seed, settings.Symmetry, map.Width, map.Height);
						if (!IsCanonical(seed, partner, map.Width) || !ObstacleCellEligible(map, seed) ||
							!ObstacleCellEligible(map, partner))
							continue;

						var remaining = minimumTarget - map.Obstacles.Count(x => x);
						var desired = Math.Max(profile.ObstacleRegionMinimumLogical,
							Math.Min(compactFallback ? profile.ObstacleRegionMaximumLogical : 16, (remaining + 1) / 2));
						var region = compactFallback ?
							GrowCompactRegion(map, settings, seed, desired) :
							GrowRegion(map, settings, fillRandom, seed, desired);
						if (region.Count < profile.ObstacleRegionMinimumLogical)
							continue;

						try
						{
							orbit = AddSymmetricRegion(map, settings, region, orbit);
						}
						catch (InvalidOperationException)
						{
							// Continue the stable scan; all frozen eligibility and connectivity
							// checks still apply to every deterministic fill candidate.
						}
					}

			var density = 100D * map.Obstacles.Count(x => x) / map.Obstacles.Length;
			if (density < range.Minimum || density > range.Maximum)
				throw new InvalidOperationException($"Attempt {attempt} produced obstacle density {density:F3}%, outside {range.Minimum}-{range.Maximum}%; " +
					$"initially-eligible={initiallyEligibleCells}, minimum-target={minimumTarget}.");
		}

		static HashSet<RmgPoint> GrowCompactRegion(RmgLogicalMap map, RmgGenerationSettings settings,
			RmgPoint seed, int desired)
		{
			var region = new HashSet<RmgPoint>();
			var queued = new HashSet<RmgPoint> { seed };
			var frontier = new Queue<RmgPoint>();
			frontier.Enqueue(seed);
			while (frontier.Count > 0 && region.Count < desired)
			{
				var point = frontier.Dequeue();
				if (!ObstacleCellEligible(map, point))
					continue;
				var partner = Transform(point, settings.Symmetry, map.Width, map.Height);
				if (point == partner || !ObstacleCellEligible(map, partner))
					continue;
				region.Add(point);
				foreach (var neighbor in FourNeighbors(point).OrderBy(p => p.Y).ThenBy(p => p.X))
					if (map.Contains(neighbor) && queued.Add(neighbor))
						frontier.Enqueue(neighbor);
			}

			return region;
		}

		static HashSet<RmgPoint> GrowRegion(RmgLogicalMap map, RmgGenerationSettings settings, DeterministicRandom random,
			RmgPoint seed, int desired)
		{
			var region = new HashSet<RmgPoint>();
			var frontier = new List<RmgPoint> { seed };
			while (frontier.Count > 0 && region.Count < desired)
			{
				var selected = random.NextInt(frontier.Count);
				var point = frontier[selected];
				frontier.RemoveAt(selected);
				if (region.Contains(point) || !ObstacleCellEligible(map, point))
					continue;
				var partner = Transform(point, settings.Symmetry, map.Width, map.Height);
				if (point == partner || !ObstacleCellEligible(map, partner))
					continue;
				region.Add(point);
				foreach (var neighbor in FourNeighbors(point).OrderBy(_ => random.NextUInt64()))
					if (!region.Contains(neighbor) && map.Contains(neighbor))
						frontier.Add(neighbor);
			}
			return region;
		}

		static int AddSymmetricRegion(RmgLogicalMap map, RmgGenerationSettings settings, IEnumerable<RmgPoint> source, int orbit)
		{
			var first = source.ToHashSet();
			var second = first.Select(p => Transform(p, settings.Symmetry, map.Width, map.Height)).ToHashSet();
			if (first.Count == 0 || first.Overlaps(second) || !Connected4(first) || !Connected4(second))
				throw new InvalidOperationException("Obstacle candidate is not a separated connected symmetry pair.");
			if (MinimumDistance(first, second) < 3 || first.Concat(second).Any(p => !ObstacleCellEligible(map, p)))
				throw new InvalidOperationException("Obstacle candidate violates reservations or the two-cell region separation ring.");

			var firstId = map.ObstacleRegions.Count;
			foreach (var point in first)
			{
				var index = map.Index(point);
				map.Obstacles[index] = true;
				map.ObstacleRegionIds[index] = firstId;
			}
			map.ObstacleRegions.Add(new RmgObstacleRegion(firstId, orbit, first.Count));
			var secondId = map.ObstacleRegions.Count;
			foreach (var point in second)
			{
				var index = map.Index(point);
				map.Obstacles[index] = true;
				map.ObstacleRegionIds[index] = secondId;
			}
			map.ObstacleRegions.Add(new RmgObstacleRegion(secondId, orbit, second.Count));
			if (ConnectedComponents(map, blocked: false).Count != 1)
			{
				foreach (var point in first.Concat(second))
				{
					var index = map.Index(point);
					map.Obstacles[index] = false;
					map.ObstacleRegionIds[index] = -1;
				}
				map.ObstacleRegions.RemoveAt(map.ObstacleRegions.Count - 1);
				map.ObstacleRegions.RemoveAt(map.ObstacleRegions.Count - 1);
				throw new InvalidOperationException("Obstacle candidate would create a terrain-only OPEN island.");
			}
			return orbit + 1;
		}

		static bool ObstacleCellEligible(RmgLogicalMap map, RmgPoint point)
		{
			if (!map.Contains(point))
				return false;
			var index = map.Index(point);
			if (point.X < 2 || point.Y < 2 || point.X >= map.Width - 2 || point.Y >= map.Height - 2 ||
				map.StartReservations[index] || map.StructureReservations[index] || map.StrategicRegions[index] ||
				map.RouteMasks[index] != 0 || map.ChokepointIds[index] >= 0 || map.Obstacles[index])
				return false;

			foreach (var nearby in map.ObstacleRegions)
			{
				for (var y = Math.Max(0, point.Y - 2); y <= Math.Min(map.Height - 1, point.Y + 2); y++)
					for (var x = Math.Max(0, point.X - 2); x <= Math.Min(map.Width - 1, point.X + 2); x++)
						if (map.ObstacleRegionIds[map.Index(new RmgPoint(x, y))] == nearby.Id)
							return false;
			}
			return true;
		}

		internal static void ApplyBlockingRepairs(RmgLogicalMap map, RmgProfile profile)
		{
			var groups = new[]
			{
				(Type: "clear-route", Reason: "obstacle overlaps RESERVED_ROUTE", Match: new Func<int, bool>(i => map.RouteMasks[i] != 0)),
				(Type: "clear-start-buffer", Reason: "obstacle overlaps START_CLEARANCE", Match: new Func<int, bool>(i => map.StartReservations[i])),
				(Type: "clear-colony-exit", Reason: "obstacle overlaps COLONY_CLEARANCE", Match: new Func<int, bool>(i => map.StructureReservations[i])),
				(Type: "reconnect-edge", Reason: "obstacle overlaps STRATEGIC_REGION", Match: new Func<int, bool>(i => map.StrategicRegions[i]))
			};

			foreach (var group in groups)
			{
				var changed = Enumerable.Range(0, map.Obstacles.Length).Where(i => map.Obstacles[i] && group.Match(i)).ToArray();
				if (changed.Length == 0)
					continue;
				if (map.Repairs.Count >= profile.MaximumRepairOperations || map.RepairChanges.Count(x => x) + changed.Length > profile.MaximumRepairCellsLogical)
					throw new InvalidOperationException("Blocking topology exceeded the frozen repair budget.");
				foreach (var index in changed)
				{
					map.Obstacles[index] = false;
					map.ObstacleRegionIds[index] = -1;
					map.RepairChanges[index] = true;
				}
				map.Repairs.Add(new RmgRepairRecord(map.Repairs.Count, group.Type, group.Reason, -1,
					changed.Select(i => new RmgPoint(i % map.Width, i / map.Width)).ToArray()));
			}
			map.RepairCount = map.Repairs.Count;
			RebuildObstacleRegionMetadata(map);
		}

		static void RebuildObstacleRegionMetadata(RmgLogicalMap map)
		{
			map.ObstacleRegions.Clear();
			Array.Fill(map.ObstacleRegionIds, -1);
			var visited = new bool[map.Obstacles.Length];
			for (var i = 0; i < map.Obstacles.Length; i++)
			{
				if (!map.Obstacles[i] || visited[i])
					continue;
				var cells = Flood4(map, i, blocked: true, visited);
				var id = map.ObstacleRegions.Count;
				foreach (var cell in cells)
					map.ObstacleRegionIds[cell] = id;
				map.ObstacleRegions.Add(new RmgObstacleRegion(id, -1, cells.Count));
			}
		}

		static void MaterializeBlockingTerrain(RmgLogicalMap map, RmgProfile profile, RmgGenerationSettings settings)
		{
			var random = DeterministicRandom.ForStream(settings, profile, "terrain-variants");
			for (var y = 0; y < map.Height; y++)
				for (var x = 0; x < map.Width; x++)
				{
					var point = new RmgPoint(x, y);
					var partner = Transform(point, settings.Symmetry, map.Width, map.Height);
					if (!IsCanonical(point, partner, map.Width))
						continue;
					var templates = map.Obstacles[map.Index(point)] ? profile.BlockedTemplateIds : profile.ClearTemplateIds;
					var template = templates[random.NextInt(templates.Length)];
					map.TemplateIds[map.Index(point)] = template;
					map.TemplateIds[map.Index(partner)] = template;
				}
		}

		static RmgValidationReport ValidateBlockingTopology(RmgLogicalMap map, RmgProfile profile, RmgGenerationSettings settings)
		{
			var report = new RmgValidationReport();
			void Hard(string code, string message) => report.HardFailures.Add(new RmgValidationIssue(code, message));
			var openTemplates = profile.ClearTemplateIds.ToHashSet();
			var blockedTemplates = profile.BlockedTemplateIds.ToHashSet();
			for (var i = 0; i < map.TemplateIds.Length; i++)
			{
				if (map.Obstacles[i] != blockedTemplates.Contains(map.TemplateIds[i]))
					Hard("TERRAIN_SEMANTIC_MISMATCH", $"Logical cell {i} obstacle state does not match template {map.TemplateIds[i]}.");
				if (!map.Obstacles[i] && !openTemplates.Contains(map.TemplateIds[i]))
					Hard("TERRAIN_TEMPLATE", $"Logical OPEN cell {i} uses non-Clear template {map.TemplateIds[i]}.");
				if (map.Obstacles[i] && (map.RouteMasks[i] != 0 || map.StartReservations[i] || map.StructureReservations[i] || map.StrategicRegions[i]))
					Hard("RESERVATION_OVERLAP", $"BLOCKED logical cell {i} overlaps a protected semantic layer.");
				if (map.Obstacles[i] && map.ObstacleRegionIds[i] < 0)
					Hard("OBSTACLE_REGION_ID", $"BLOCKED logical cell {i} has no canonical region ID.");
			}

			for (var y = 0; y < map.Height; y++)
				for (var x = 0; x < map.Width; x++)
				{
					var point = new RmgPoint(x, y);
					var partner = Transform(point, settings.Symmetry, map.Width, map.Height);
					if (map.Obstacles[map.Index(point)] != map.Obstacles[map.Index(partner)] ||
						map.TemplateIds[map.Index(point)] != map.TemplateIds[map.Index(partner)] ||
						(map.RouteMasks[map.Index(point)] != 0) != (map.RouteMasks[map.Index(partner)] != 0))
						Hard("TOPOLOGY_SYMMETRY", $"Semantic topology at {point} differs from symmetry partner {partner}.");
				}

			var components = ConnectedComponents(map, blocked: true);
			foreach (var component in components)
				if (component.Count < profile.ObstacleRegionMinimumLogical || component.Count > profile.ObstacleRegionMaximumLogical)
					Hard("OBSTACLE_REGION_SIZE", $"Obstacle region has {component.Count} cells; expected {profile.ObstacleRegionMinimumLogical}-{profile.ObstacleRegionMaximumLogical}.");
			for (var a = 0; a < components.Count; a++)
				for (var b = a + 1; b < components.Count; b++)
					if (MinimumDistance(components[a].Select(IndexPoint).ToHashSet(), components[b].Select(IndexPoint).ToHashSet()) < 3)
						Hard("OBSTACLE_REGION_SEPARATION", $"Obstacle regions {a} and {b} do not preserve two complete OPEN cells between them.");

			var openComponents = ConnectedComponents(map, blocked: false);
			if (openComponents.Count != 1)
				Hard("OPEN_COMPONENTS", $"Terrain-only OPEN mask has {openComponents.Count} connected components; expected one.");
			if (map.Starts.Count != settings.PlayerCount || map.Actors.Count(a => a.Type == profile.SpawnActor) != settings.PlayerCount)
				Hard("START_COUNT", "Start anchors or mpspawn actors do not match the requested player count.");
			if (map.Actors.Count(a => a.Owner == profile.ColonyOwner) != settings.NeutralColonyCount)
				Hard("COLONY_COUNT", "Neutral-colony count differs from the requested setting.");
			foreach (var start in map.Starts)
			{
				var outgoing = map.GraphEdges.Count(e => map.GraphNodes.Single(n => n.Id == e.From).Location == start);
				if (outgoing < 2)
					Hard("START_ROUTE_EXITS", $"Start {start} has only {outgoing} named strategic exits.");
			}

			var expectedChokes = settings.Archetype == RmgArchetype.CentralContest ? 2 : 0;
			if (map.Chokepoints.Count != expectedChokes)
				Hard("CHOKEPOINT_ORBIT", $"Expected {expectedChokes} chokepoint segments but found {map.Chokepoints.Count}.");
			foreach (var choke in map.Chokepoints)
			{
				if (choke.WidthNative != profile.ChokepointWidthNative || choke.LengthNative < profile.ChokepointLengthMinimumNative ||
					choke.LengthNative > profile.ChokepointLengthMaximumNative)
					Hard("CHOKEPOINT_DIMENSIONS", $"{choke.Id} violates the frozen width/length envelope.");
				if (map.Starts.Any(s => choke.From.ChebyshevDistance(s) < 12 || choke.To.ChebyshevDistance(s) < 12))
					Hard("CHOKEPOINT_START_DISTANCE", $"{choke.Id} is less than 24 native cells from a start anchor.");
			}
			foreach (var colony in map.Actors.Where(a => a.Owner == profile.ColonyOwner))
				if (CandidateChokepointDistance(map, colony.LogicalLocation) < 10)
					Hard("CHOKEPOINT_COLONY_DISTANCE", $"A chokepoint is less than 10 native cells from colony coverage at {colony.LogicalLocation}.");

			var density = 100D * map.Obstacles.Count(x => x) / map.Obstacles.Length;
			var densityRange = profile.ObstacleDensityRange(settings.Archetype);
			if (density < densityRange.Minimum || density > densityRange.Maximum)
				Hard("OBSTACLE_DENSITY", $"Obstacle density {density:F3}% is outside {densityRange.Minimum}-{densityRange.Maximum}%.");
			if (map.Repairs.Count > profile.MaximumRepairOperations || map.RepairChanges.Count(x => x) > profile.MaximumRepairCellsLogical)
				Hard("REPAIR_BUDGET", "Bounded repairs exceeded the frozen operation or changed-cell budget.");

			var colonies = map.Actors.Where(a => a.Owner == profile.ColonyOwner).ToArray();
			ValidateColonyCombatSpace(map, profile, report);
			var assignments = new int[settings.PlayerCount];
			foreach (var colony in colonies)
			{
				var nearest = Enumerable.Range(0, map.Starts.Count).OrderBy(i => colony.LogicalLocation.ManhattanDistance(map.Starts[i])).ThenBy(i => i).First();
				assignments[nearest]++;
			}
			var (reachableStarts, passableCells, maximumStartDistance) = NativeConnectivityProxy(map, profile);
			if (reachableStarts != map.Starts.Count)
				Hard("NATIVE_CONNECTIVITY_PROXY", $"The obstacle-aware 3x3 proxy reaches {reachableStarts}/{map.Starts.Count} starts.");
			report.Metrics["player_count"] = settings.PlayerCount;
			report.Metrics["neutral_colony_count"] = colonies.Length;
			report.Metrics["colony_assignment_spread"] = assignments.Max() - assignments.Min();
			report.Metrics["minimum_colony_separation_logical"] = colonies.Length < 2 ? 0 :
				colonies.SelectMany((a, i) => colonies.Skip(i + 1).Select(b => a.LogicalLocation.ChebyshevDistance(b.LogicalLocation))).Min();
			report.Metrics["route_cell_count"] = map.RouteMasks.Count(r => r != 0);
			report.Metrics["obstacle_cell_count"] = map.Obstacles.Count(x => x);
			report.Metrics["obstacle_density_percent"] = density;
			report.Metrics["obstacle_region_count"] = components.Count;
			report.Metrics["chokepoint_segment_count"] = map.Chokepoints.Count;
			report.Metrics["chokepoint_orbit_count"] = map.Chokepoints.Select(c => c.SymmetryOrbit).Distinct().Count();
			report.Metrics["minimum_reserved_route_width_native"] = settings.Archetype == RmgArchetype.Open ? profile.MajorRouteWidthNative : profile.MinimumRouteWidthNative;
			report.Metrics["native_proxy_reachable_starts"] = reachableStarts;
			report.Metrics["native_proxy_passable_cells"] = passableCells;
			report.Metrics["native_proxy_max_start_distance"] = maximumStartDistance;
			report.Metrics["repair_count"] = map.Repairs.Count;
			report.Metrics["repair_changed_cells"] = map.RepairChanges.Count(x => x);
			report.Metrics["retry_count"] = map.RetryCount;
			return report;

			RmgPoint IndexPoint(int index) => new(index % map.Width, index / map.Width);
		}

		static List<List<int>> ConnectedComponents(RmgLogicalMap map, bool blocked)
		{
			var components = new List<List<int>>();
			var visited = new bool[map.Obstacles.Length];
			for (var i = 0; i < map.Obstacles.Length; i++)
				if (!visited[i] && map.Obstacles[i] == blocked)
					components.Add(Flood4(map, i, blocked, visited));
			return components;
		}

		static List<int> Flood4(RmgLogicalMap map, int start, bool blocked, bool[] visited)
		{
			var result = new List<int>();
			var queue = new Queue<int>();
			visited[start] = true;
			queue.Enqueue(start);
			while (queue.Count > 0)
			{
				var current = queue.Dequeue();
				result.Add(current);
				var point = new RmgPoint(current % map.Width, current / map.Width);
				foreach (var neighbor in FourNeighbors(point))
				{
					if (!map.Contains(neighbor))
						continue;
					var index = map.Index(neighbor);
					if (!visited[index] && map.Obstacles[index] == blocked)
					{
						visited[index] = true;
						queue.Enqueue(index);
					}
				}
			}
			return result;
		}

		static IEnumerable<RmgPoint> FourNeighbors(RmgPoint point)
		{
			yield return new RmgPoint(point.X - 1, point.Y);
			yield return new RmgPoint(point.X + 1, point.Y);
			yield return new RmgPoint(point.X, point.Y - 1);
			yield return new RmgPoint(point.X, point.Y + 1);
		}

		static bool Connected4(HashSet<RmgPoint> cells)
		{
			if (cells.Count == 0)
				return false;
			var visited = new HashSet<RmgPoint>();
			var queue = new Queue<RmgPoint>();
			queue.Enqueue(cells.First());
			visited.Add(cells.First());
			while (queue.Count > 0)
				foreach (var neighbor in FourNeighbors(queue.Dequeue()))
					if (cells.Contains(neighbor) && visited.Add(neighbor))
						queue.Enqueue(neighbor);
			return visited.Count == cells.Count;
		}

		static int MinimumDistance(HashSet<RmgPoint> first, HashSet<RmgPoint> second) =>
			first.SelectMany(a => second.Select(b => a.ChebyshevDistance(b))).DefaultIfEmpty(int.MaxValue).Min();

		static int GenericColonyCoverageDistance(IEnumerable<RmgPoint> aperture, RmgLogicalMap map, RmgProfile profile)
		{
			var apertureCells = aperture.SelectMany(point =>
				from dy in Enumerable.Range(0, 2)
				from dx in Enumerable.Range(0, 2)
				select new RmgPoint(2 * point.X + dx, 2 * point.Y + dy)).ToArray();
			return apertureCells.SelectMany(cell => map.Actors.Where(a => a.Owner == profile.ColonyOwner).SelectMany(colony =>
				from dy in Enumerable.Range(0, 6)
				from dx in Enumerable.Range(0, 6)
				select cell.ChebyshevDistance(new RmgPoint(2 * colony.LogicalLocation.X + dx, 2 * colony.LogicalLocation.Y + dy))))
				.DefaultIfEmpty(int.MaxValue).Min();
		}

		static int GenericStartAnchorDistance(IEnumerable<RmgPoint> aperture, RmgLogicalMap map)
		{
			return aperture.SelectMany(point =>
				from dy in Enumerable.Range(0, 2)
				from dx in Enumerable.Range(0, 2)
				let cell = new RmgPoint(2 * point.X + dx, 2 * point.Y + dy)
				from start in map.Starts
				select cell.ChebyshevDistance(new RmgPoint(2 * start.X, 2 * start.Y)))
				.DefaultIfEmpty(int.MaxValue).Min();
		}

		static int FirstRoute(ulong mask)
		{
			if (mask == 0)
				return -1;
			for (var i = 0; i < 64; i++)
				if ((mask & (1UL << i)) != 0)
					return i;
			return -1;
		}

		static int BitCount(ulong value)
		{
			var count = 0;
			while (value != 0)
			{
				value &= value - 1;
				count++;
			}
			return count;
		}

		static string HashBlockingLogicalMap(RmgLogicalMap map)
		{
			var text = new StringBuilder();
			for (var i = 0; i < map.TemplateIds.Length; i++)
				text.Append(map.TemplateIds[i]).Append(',').Append(map.RegionIds[i]).Append(',').Append(map.RouteIds[i]).Append(',')
					.Append(map.RouteMasks[i]).Append(',').Append(map.ObstacleRegionIds[i]).Append(',').Append(map.ChokepointIds[i]).Append(',')
					.Append(map.StartReservations[i] ? '1' : '0').Append(map.StructureReservations[i] ? '1' : '0')
					.Append(map.StrategicRegions[i] ? '1' : '0').Append(map.Obstacles[i] ? '1' : '0')
					.Append(map.RepairChanges[i] ? '1' : '0').Append('\n');
			foreach (var repair in map.Repairs)
				text.Append("R|").Append(repair.Index).Append('|').Append(repair.Type).Append('|').Append(repair.Reason).Append('|')
					.Append(repair.TargetId).Append('|').Append(string.Join(';', repair.ChangedCells.Select(p => p.ToString()))).Append('\n');
			return Sha256(text.ToString());
		}

		static string HashBlockingGraph(RmgLogicalMap map)
		{
			var baseLines = map.GraphNodes.OrderBy(n => n.Id, StringComparer.Ordinal).Select(n => $"N|{n.Id}|{n.Role}|{n.Location}")
				.Concat(map.GraphEdges.OrderBy(e => e.RouteId).Select(e => $"E|{e.Id}|{e.From}|{e.To}|{e.RouteId}"));
			var chokes = map.Chokepoints.OrderBy(c => c.Id, StringComparer.Ordinal)
				.Select(c => $"C|{c.Id}|{c.RouteId}|{c.SymmetryOrbit}|{c.From}|{c.To}|{c.LengthNative}|{c.WidthNative}");
			return Sha256(string.Join("\n", baseLines.Concat(chokes)));
		}

		public static JObject BlockingDebugLayers(RmgLogicalMap map)
		{
			JArray Layer(Func<int, char> value) => new(Enumerable.Range(0, map.Height)
				.Select(y => new string(Enumerable.Range(0, map.Width).Select(x => value(y * map.Width + x)).ToArray())));
			return new JObject
			{
				["legend"] = new JObject
				{
					["topology"] = ". OPEN, # BLOCKED",
					["routes"] = ". none, R reserved, J strategic junction",
					["clearances"] = ". none, S start, C colony, B both",
					["chokes_repairs"] = ". none, K choke aperture, X repaired cell"
				},
				["topology"] = Layer(i => map.Obstacles[i] ? '#' : '.'),
				["routes"] = Layer(i => map.StrategicRegions[i] ? 'J' : map.RouteMasks[i] != 0 ? 'R' : '.'),
				["named_routes"] = new JObject(map.GraphEdges.OrderBy(edge => edge.RouteId).Select(edge =>
					new JProperty(edge.Id, Layer(i => (map.RouteMasks[i] & (1UL << edge.RouteId)) != 0 ? 'R' : '.')))),
				["clearances"] = Layer(i => map.StartReservations[i] && map.StructureReservations[i] ? 'B' : map.StartReservations[i] ? 'S' : map.StructureReservations[i] ? 'C' : '.'),
				["chokes_repairs"] = Layer(i => map.RepairChanges[i] ? 'X' : map.ChokepointIds[i] >= 0 ? 'K' : '.')
			};
		}
	}
}
