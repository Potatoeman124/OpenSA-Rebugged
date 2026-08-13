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
using System.Security.Cryptography;
using System.Text;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public static partial class RmgGenerator
	{
		sealed record ColonyRequest(string Role, RmgPoint[] Targets, int TypeGroup);

		public static IReadOnlyList<string> RunSelfTests(RmgProfile profile)
		{
			var failures = new List<string>();
			var points = new[] { new RmgPoint(0, 0), new RmgPoint(7, 19), new RmgPoint(31, 32), new RmgPoint(63, 63) };
			foreach (var symmetry in Enum.GetValues<RmgSymmetry>())
				foreach (var point in points)
				{
					var transformed = Transform(point, symmetry, profile.LogicalWidth, profile.LogicalHeight);
					if (transformed.X < 0 || transformed.X >= profile.LogicalWidth || transformed.Y < 0 || transformed.Y >= profile.LogicalHeight)
						failures.Add($"Transform {symmetry} moved {point} out of bounds.");
					if (Transform(transformed, symmetry, profile.LogicalWidth, profile.LogicalHeight) != point)
						failures.Add($"Transform {symmetry} is not an involution for {point}.");
				}

			var settings = new RmgGenerationSettings
			{
				Seed = 45006,
				PlayerCount = 2,
				NeutralColonyCount = 10,
				Symmetry = RmgSymmetry.Rotate180,
				Archetype = RmgArchetype.CentralContest,
				GeneratorVersion = profile.GeneratorVersion,
				TopologyPreset = profile.GeneratorVersion == 2 ? RmgTopologyPreset.Mixed : RmgTopologyPreset.Off
			};
			var first = Generate(profile, settings);
			var second = Generate(profile, settings);
			if (first.LogicalHash != second.LogicalHash || first.ActorHash != second.ActorHash || first.GraphHash != second.GraphHash)
				failures.Add("Same-seed deterministic hash self-test failed.");

			first.Map.TemplateIds[0] = ushort.MaxValue;
			if (Validate(first.Map, profile, settings).Accepted)
				failures.Add("Hard validator accepted a deliberately invalid terrain template.");

			if (profile.GeneratorVersion == 2)
			{
				var repairMap = Generate(profile, settings).Map;
				var reserved = Array.FindIndex(repairMap.RouteMasks, route => route != 0);
				repairMap.Obstacles[reserved] = true;
				repairMap.ObstacleRegionIds[reserved] = 999;
				ApplyBlockingRepairs(repairMap, profile);
				if (repairMap.Obstacles[reserved] || repairMap.Repairs.Count == 0)
					failures.Add("Bounded repair self-test did not clear a route obstruction and record the operation.");

				var overBudgetMap = Generate(profile, settings).Map;
				var overBudget = Enumerable.Range(0, overBudgetMap.RouteMasks.Length)
					.Where(i => overBudgetMap.RouteMasks[i] != 0)
					.Take(profile.MaximumRepairCellsLogical + 1)
					.ToArray();
				if (overBudget.Length <= profile.MaximumRepairCellsLogical)
					failures.Add("Repair-budget rejection self-test could not construct an over-budget route obstruction.");
				else
				{
					foreach (var index in overBudget)
					{
						overBudgetMap.Obstacles[index] = true;
						overBudgetMap.ObstacleRegionIds[index] = 999;
					}

					try
					{
						ApplyBlockingRepairs(overBudgetMap, profile);
						failures.Add("Bounded repair self-test accepted an obstruction larger than the frozen changed-cell budget.");
					}
					catch (InvalidOperationException)
					{
						// Expected: an over-budget repair rejects the topology instead of silently changing it.
					}
				}
			}

			return failures;
		}

		public static RmgGenerationResult Generate(RmgProfile profile, RmgGenerationSettings settings)
		{
			ValidateSettings(profile, settings);
			if (profile.GeneratorVersion == 2)
				return GenerateBlockingTopology(profile, settings);

			var map = new RmgLogicalMap(profile.LogicalWidth, profile.LogicalHeight);

			GenerateStartsAndTopology(map, profile, settings);
			ReserveRoutes(map);
			GenerateObstacles(map, profile);
			PlaceColonies(map, profile, settings);
			ApplyBoundedRepairs(map);
			AssignRegions(map);
			MaterializeTerrainVariants(map, profile, settings);

			var validation = Validate(map, profile, settings);
			return new RmgGenerationResult
			{
				Settings = settings,
				Profile = profile,
				Map = map,
				Validation = validation,
				LogicalHash = HashLogicalMap(map),
				ActorHash = HashActors(map),
				GraphHash = HashGraph(map)
			};
		}

		public static RmgPoint Transform(RmgPoint point, RmgSymmetry symmetry, int width = 64, int height = 64)
		{
			return symmetry switch
			{
				RmgSymmetry.MirrorHorizontal => new RmgPoint(point.X, height - 1 - point.Y),
				RmgSymmetry.MirrorVertical => new RmgPoint(width - 1 - point.X, point.Y),
				RmgSymmetry.Rotate180 => new RmgPoint(width - 1 - point.X, height - 1 - point.Y),
				_ => throw new ArgumentOutOfRangeException(nameof(symmetry))
			};
		}

		static void ValidateSettings(RmgProfile profile, RmgGenerationSettings settings)
		{
			if (settings.GeneratorVersion != profile.GeneratorVersion)
				throw new ArgumentException($"Generator Version {settings.GeneratorVersion} is not supported by profile {profile.ProfileId}.");
			if (profile.GeneratorVersion == 1 && settings.TopologyPreset != RmgTopologyPreset.Off)
				throw new ArgumentException("Generator Version 1 requires TopologyPreset=off.");
			if (profile.GeneratorVersion == 2 && settings.TopologyPreset != RmgTopologyPreset.Mixed)
				throw new ArgumentException("Generator Version 2 requires TopologyPreset=mixed.");
			if (settings.PlayerCount != 2 && settings.PlayerCount != 4)
				throw new ArgumentException($"Generator Version {settings.GeneratorVersion} supports exactly two or four players.");
			if (settings.PlayerCount == 2 && (settings.NeutralColonyCount < 8 || settings.NeutralColonyCount > 20 || settings.NeutralColonyCount % 2 != 0))
				throw new ArgumentException("Two-player maps require an even neutral-colony count from 8 through 20.");
			if (settings.PlayerCount == 4 && (settings.NeutralColonyCount < 12 || settings.NeutralColonyCount > 24 || settings.NeutralColonyCount % 4 != 0))
				throw new ArgumentException("Four-player maps require a neutral-colony count from 12 through 24, divisible by four.");
		}

		static void GenerateStartsAndTopology(RmgLogicalMap map, RmgProfile profile, RmgGenerationSettings settings)
		{
			var random = DeterministicRandom.ForStream(settings, profile, "topology");
			RmgPoint Jitter(int x, int y) => new(x + random.NextInt(-2, 3), y + random.NextInt(-2, 3));

			if (settings.PlayerCount == 2)
			{
				var first = settings.Symmetry switch
				{
					RmgSymmetry.MirrorHorizontal => Jitter(20, 12),
					RmgSymmetry.MirrorVertical => Jitter(12, 20),
					RmgSymmetry.Rotate180 => Jitter(15, 18),
					_ => throw new ArgumentOutOfRangeException(nameof(settings), settings.Symmetry, "Unsupported symmetry value.")
				};
				AddOrbit(map.Starts, first, settings.Symmetry, map.Width, map.Height);
			}
			else
			{
				switch (settings.Symmetry)
				{
					case RmgSymmetry.MirrorHorizontal:
						AddOrbit(map.Starts, Jitter(14, 12), settings.Symmetry, map.Width, map.Height);
						AddOrbit(map.Starts, Jitter(45, 12), settings.Symmetry, map.Width, map.Height);
						break;
					case RmgSymmetry.MirrorVertical:
						AddOrbit(map.Starts, Jitter(12, 14), settings.Symmetry, map.Width, map.Height);
						AddOrbit(map.Starts, Jitter(12, 45), settings.Symmetry, map.Width, map.Height);
						break;
					case RmgSymmetry.Rotate180:
						AddOrbit(map.Starts, Jitter(14, 16), settings.Symmetry, map.Width, map.Height);
						AddOrbit(map.Starts, Jitter(45, 14), settings.Symmetry, map.Width, map.Height);
						break;
				}
			}

			for (var i = 0; i < map.Starts.Count; i++)
			{
				var start = map.Starts[i];
				map.GraphNodes.Add(new RmgGraphNode($"start-{i}", "start", start));
				map.Actors.Add(new RmgActorPlan(profile.SpawnActor, profile.SpawnOwner, "start", start, i / 2));
				ReserveSquare(map.StartReservations, map, start, 3);
			}

			var hub = new RmgPoint(30, 31);
			var hubs = new List<RmgPoint>();
			AddOrbit(hubs, hub, settings.Symmetry, map.Width, map.Height);
			for (var i = 0; i < hubs.Count; i++)
				map.GraphNodes.Add(new RmgGraphNode($"hub-{i}", "hub", hubs[i]));

			var routeId = 0;
			for (var start = 0; start < map.Starts.Count; start++)
				for (var hubIndex = 0; hubIndex < hubs.Count; hubIndex++)
					map.GraphEdges.Add(new RmgGraphEdge($"edge-{routeId}", $"start-{start}", $"hub-{hubIndex}", routeId++));
		}

		static void AddOrbit(List<RmgPoint> destination, RmgPoint point, RmgSymmetry symmetry, int width, int height)
		{
			destination.Add(point);
			var transformed = Transform(point, symmetry, width, height);
			if (transformed != point)
				destination.Add(transformed);
		}

		static void ReserveRoutes(RmgLogicalMap map)
		{
			var nodes = map.GraphNodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
			foreach (var edge in map.GraphEdges)
			{
				var from = nodes[edge.From].Location;
				var to = nodes[edge.To].Location;
				foreach (var point in RasterizeLine(from, to))
				{
					for (var dy = -1; dy <= 1; dy++)
						for (var dx = -1; dx <= 1; dx++)
						{
							var widened = new RmgPoint(point.X + dx, point.Y + dy);
							if (map.Contains(widened) && map.RouteIds[map.Index(widened)] == -1)
								map.RouteIds[map.Index(widened)] = edge.RouteId;
						}
				}
			}
		}

		static IEnumerable<RmgPoint> RasterizeLine(RmgPoint from, RmgPoint to)
		{
			var x = from.X;
			var y = from.Y;
			var dx = Math.Abs(to.X - from.X);
			var sx = from.X < to.X ? 1 : -1;
			var dy = -Math.Abs(to.Y - from.Y);
			var sy = from.Y < to.Y ? 1 : -1;
			var error = dx + dy;

			while (true)
			{
				yield return new RmgPoint(x, y);
				if (x == to.X && y == to.Y)
					yield break;

				var doubled = 2 * error;
				if (doubled >= dy)
				{
					error += dy;
					x += sx;
				}

				if (doubled <= dx)
				{
					error += dx;
					y += sy;
				}
			}
		}

		static void GenerateObstacles(RmgLogicalMap map, RmgProfile profile)
		{
			if (profile.ObstacleDensity != 0 || profile.VegetationDensity != 0)
				throw new InvalidOperationException("Blocking or rough terrain is not authorized by Generator Version 1.");

			// This explicit no-op is the Version 1 obstacle stage. The arrays remain present so a later
			// contract can add materialization without changing the topology and validation interfaces.
			Array.Clear(map.Obstacles, 0, map.Obstacles.Length);
		}

		static void ApplyBoundedRepairs(RmgLogicalMap map)
		{
			// The Clear-only profile has no generated terrain obstruction to repair. Keep this explicit
			// stage and its counters stable so later profile versions can add bounded repair streams.
			map.RepairCount = 0;
			map.RetryCount = 0;
		}

		static void PlaceColonies(RmgLogicalMap map, RmgProfile profile, RmgGenerationSettings settings,
			bool allowCentralRouteOverlap = true, int routeClearanceRadius = 2, bool allowAnyRouteOverlap = false)
		{
			var random = DeterministicRandom.ForStream(settings, profile, "colonies");
			var requests = BuildColonyRequests(map, settings);
			var typeByGroup = new Dictionary<int, string>();
			foreach (var request in requests)
			{
				if (!typeByGroup.TryGetValue(request.TypeGroup, out var actorType))
				{
					actorType = profile.NeutralColonyActors[random.NextInt(profile.NeutralColonyActors.Length)];
					typeByGroup.Add(request.TypeGroup, actorType);
				}

				var orbit = SelectColonyOrbit(map, settings, request, random, allowCentralRouteOverlap,
					routeClearanceRadius, allowAnyRouteOverlap);
				foreach (var point in orbit)
				{
					map.Actors.Add(new RmgActorPlan(actorType, profile.ColonyOwner, request.Role, point, request.TypeGroup));
					ReserveSquare(map.StructureReservations, map, point, 2);
				}
			}
		}

		static List<ColonyRequest> BuildColonyRequests(RmgLogicalMap map, RmgGenerationSettings settings)
		{
			var requests = new List<ColonyRequest>();
			var startPairs = map.Starts.Chunk(2).Select(c => c.ToArray()).ToArray();
			var typeGroup = 0;

			for (var layer = 0; layer < 2; layer++)
			{
				foreach (var pair in startPairs)
					requests.Add(new ColonyRequest("near-start", pair, typeGroup));
				typeGroup++;
			}

			var requiredOrbits = settings.NeutralColonyCount / 2;
			var groupSize = settings.PlayerCount / 2;
			if (settings.Archetype == RmgArchetype.CentralContest && requests.Count + groupSize <= requiredOrbits)
			{
				for (var i = 0; i < groupSize; i++)
					requests.Add(new ColonyRequest("central-contest", startPairs[i % startPairs.Length], typeGroup));
				typeGroup++;
			}

			var roleIndex = 0;
			while (requests.Count < requiredOrbits)
			{
				var role = roleIndex++ % 2 == 0 ? "side-route" : "peripheral";
				for (var i = 0; i < groupSize && requests.Count < requiredOrbits; i++)
					requests.Add(new ColonyRequest(role, startPairs[i % startPairs.Length], typeGroup));
				typeGroup++;
			}

			return requests;
		}

		static RmgPoint[] SelectColonyOrbit(RmgLogicalMap map, RmgGenerationSettings settings, ColonyRequest request,
			DeterministicRandom random, bool allowCentralRouteOverlap, int routeClearanceRadius, bool allowAnyRouteOverlap)
		{
			RmgPoint[] best = null;
			var bestScore = long.MinValue;
			for (var attempt = 0; attempt < 768; attempt++)
			{
				var candidate = new RmgPoint(random.NextInt(7, map.Width - 7), random.NextInt(7, map.Height - 7));
				Consider(candidate);
			}

			if (best == null)
				for (var y = 7; y < map.Height - 7; y++)
					for (var x = 7; x < map.Width - 7; x++)
						Consider(new RmgPoint(x, y));

			if (best == null)
				throw new RmgGenerationRejectedException("COLONY_PLACEMENT",
					$"Unable to place a valid {request.Role} colony orbit.");

			return best;

			void Consider(RmgPoint candidate)
			{
				var transformed = Transform(candidate, settings.Symmetry, map.Width, map.Height);
				if (candidate == transformed || !IsCanonical(candidate, transformed, map.Width))
					return;

				var orbit = new[] { candidate, transformed };
				if (request.Role == "central-contest" && orbit.Any(p => p.X < 24 || p.X >= 40 || p.Y < 24 || p.Y >= 40))
					return;
				if (request.Targets.Length > 0 && orbit.Any(p => !request.Targets.Contains(NearestStart(map, p))))
					return;
				var routeOverlap = allowAnyRouteOverlap || (request.Role == "central-contest" && allowCentralRouteOverlap);
				var minimumColonySeparation = allowAnyRouteOverlap ? 6 : 5;
				if (!orbit.All(p => ColonyLocationIsValid(map, p, routeOverlap, routeClearanceRadius, minimumColonySeparation)) ||
					orbit[0].ChebyshevDistance(orbit[1]) < minimumColonySeparation)
					return;

				var score = ColonyScore(map, orbit, request) + random.NextInt(100);
				if (allowAnyRouteOverlap)
					score -= 10000000L * orbit.Sum(p => RouteOverlapCells(map, p, routeClearanceRadius));
				if (score > bestScore)
				{
					bestScore = score;
					best = orbit;
				}
			}
		}

		static int RouteOverlapCells(RmgLogicalMap map, RmgPoint point, int routeClearanceRadius)
		{
			var minimumFootprintOffset = routeClearanceRadius == 3 ? -1 : -routeClearanceRadius;
			var count = 0;
			for (var dy = minimumFootprintOffset; dy <= routeClearanceRadius; dy++)
				for (var dx = minimumFootprintOffset; dx <= routeClearanceRadius; dx++)
				{
					var footprint = new RmgPoint(point.X + dx, point.Y + dy);
					if (map.Contains(footprint) && map.RouteMasks[map.Index(footprint)] != 0)
						count++;
				}

			return count;
		}

		static RmgPoint NearestStart(RmgLogicalMap map, RmgPoint point)
		{
			return map.Starts.Select((start, index) => (Start: start, Index: index))
				.OrderBy(entry => point.ManhattanDistance(entry.Start))
				.ThenBy(entry => entry.Index)
				.First().Start;
		}

		static bool IsCanonical(RmgPoint point, RmgPoint transformed, int width)
		{
			return point.Y * width + point.X < transformed.Y * width + transformed.X;
		}

		static bool ColonyLocationIsValid(RmgLogicalMap map, RmgPoint point, bool allowRouteOverlap,
			int routeClearanceRadius, int minimumColonySeparation)
		{
			if (!map.Contains(point) || map.Starts.Any(s => s.ChebyshevDistance(point) < 6))
				return false;
			if (map.Chokepoints.Count > 0 && CandidateChokepointDistance(map, point) < 10)
				return false;
			if (map.Obstacles.Any(x => x))
				for (var dy = -4; dy <= 4; dy++)
					for (var dx = -4; dx <= 4; dx++)
					{
						var clearance = new RmgPoint(point.X + dx, point.Y + dy);
						if (!map.Contains(clearance) || map.Obstacles[map.Index(clearance)])
							return false;
					}

			if (map.Actors.Where(a => a.Role != "start")
				.Any(a => a.LogicalLocation.ChebyshevDistance(point) < minimumColonySeparation))
				return false;

			var minimumFootprintOffset = routeClearanceRadius == 3 ? -1 : -routeClearanceRadius;
			for (var dy = minimumFootprintOffset; dy <= routeClearanceRadius; dy++)
				for (var dx = minimumFootprintOffset; dx <= routeClearanceRadius; dx++)
				{
					var footprint = new RmgPoint(point.X + dx, point.Y + dy);
					if (!map.Contains(footprint) || map.Obstacles[map.Index(footprint)] ||
						(!allowRouteOverlap && map.RouteIds[map.Index(footprint)] >= 0))
						return false;
				}

			return true;
		}

		static int CandidateChokepointDistance(RmgLogicalMap map, RmgPoint colony)
		{
			var distance = int.MaxValue;
			for (var i = 0; i < map.ChokepointIds.Length; i++)
			{
				if (map.ChokepointIds[i] < 0)
					continue;
				var chokeX = i % map.Width;
				var chokeY = i / map.Width;
				for (var chokeDy = 0; chokeDy < 2; chokeDy++)
					for (var chokeDx = 0; chokeDx < 2; chokeDx++)
						for (var colonyDy = 0; colonyDy < 6; colonyDy++)
							for (var colonyDx = 0; colonyDx < 6; colonyDx++)
								distance = Math.Min(distance, Math.Max(
									Math.Abs(2 * chokeX + chokeDx - (2 * colony.X + colonyDx)),
									Math.Abs(2 * chokeY + chokeDy - (2 * colony.Y + colonyDy))));
			}

			return distance;
		}

		static long ColonyScore(RmgLogicalMap map, RmgPoint[] orbit, ColonyRequest request)
		{
			var center = new RmgPoint((map.Width - 1) / 2, (map.Height - 1) / 2);
			var centerDistance = orbit.Sum(p => p.ManhattanDistance(center));
			var nearestStart = orbit.Sum(p => map.Starts.Min(s => p.ManhattanDistance(s)));
			var nearestExisting = orbit.Sum(p => map.Actors.Where(a => a.Role != "start")
				.Select(a => p.ManhattanDistance(a.LogicalLocation)).DefaultIfEmpty(24).Min());

			return request.Role switch
			{
				"near-start" => -Math.Abs(nearestStart - 16) * 1000L - TargetDistance(orbit, request.Targets) * 50L + nearestExisting * 10L,
				"central-contest" => -centerDistance * 1000L - TargetDistance(orbit, request.Targets) * 1200L + nearestExisting * 10L,
				"side-route" => -Math.Abs(centerDistance - 38) * 500L - TargetDistance(orbit, request.Targets) * 400L + nearestExisting * 20L,
				"peripheral" => -Math.Abs(orbit.Sum(EdgeDistance) - 16) * 500L - TargetDistance(orbit, request.Targets) * 400L + nearestExisting * 20L,
				_ => nearestExisting
			};

			int EdgeDistance(RmgPoint p) => Math.Min(Math.Min(p.X, map.Width - 1 - p.X), Math.Min(p.Y, map.Height - 1 - p.Y));
		}

		static int TargetDistance(IEnumerable<RmgPoint> points, IReadOnlyCollection<RmgPoint> targets)
		{
			if (targets.Count == 0)
				return 0;

			return points.Sum(p => targets.Min(t => p.ManhattanDistance(t)));
		}

		static void ReserveSquare(bool[] layer, RmgLogicalMap map, RmgPoint center, int radius)
		{
			for (var dy = -radius; dy <= radius; dy++)
				for (var dx = -radius; dx <= radius; dx++)
				{
					var point = new RmgPoint(center.X + dx, center.Y + dy);
					if (map.Contains(point))
						layer[map.Index(point)] = true;
				}
		}

		static void AssignRegions(RmgLogicalMap map)
		{
			for (var y = 0; y < map.Height; y++)
				for (var x = 0; x < map.Width; x++)
				{
					var point = new RmgPoint(x, y);
					var bestRegion = 0;
					var bestDistance = int.MaxValue;
					for (var i = 0; i < map.Starts.Count; i++)
					{
						var distance = point.ManhattanDistance(map.Starts[i]);
						if (distance < bestDistance)
						{
							bestDistance = distance;
							bestRegion = i;
						}
					}

					map.RegionIds[map.Index(point)] = bestRegion;
				}
		}

		static void MaterializeTerrainVariants(RmgLogicalMap map, RmgProfile profile, RmgGenerationSettings settings)
		{
			var random = DeterministicRandom.ForStream(settings, profile, "terrain-variants");
			for (var y = 0; y < map.Height; y++)
				for (var x = 0; x < map.Width; x++)
				{
					var point = new RmgPoint(x, y);
					var index = map.Index(point);
					if (map.TemplateIds[index] != 0)
						continue;

					var template = profile.ClearTemplateIds[random.NextInt(profile.ClearTemplateIds.Length)];
					map.TemplateIds[index] = template;
					map.TemplateIds[map.Index(Transform(point, settings.Symmetry, map.Width, map.Height))] = template;
				}
		}

		static RmgValidationReport Validate(RmgLogicalMap map, RmgProfile profile, RmgGenerationSettings settings)
		{
			if (profile.GeneratorVersion == 2)
				return ValidateBlockingTopology(map, profile, settings);

			var report = new RmgValidationReport();
			void Hard(string code, string message) => report.HardFailures.Add(new RmgValidationIssue(code, message));

			if (map.Starts.Count != settings.PlayerCount)
				Hard("START_COUNT", $"Expected {settings.PlayerCount} starts but found {map.Starts.Count}.");

			var startSet = map.Starts.ToHashSet();
			foreach (var start in map.Starts)
			{
				if (!startSet.Contains(Transform(start, settings.Symmetry, map.Width, map.Height)))
					Hard("START_SYMMETRY", $"Start {start} lacks its symmetry partner.");
				if (start.X * 2 < profile.StartRegionRadiusNative || start.Y * 2 < profile.StartRegionRadiusNative ||
					(map.Width - 1 - start.X) * 2 < profile.StartRegionRadiusNative || (map.Height - 1 - start.Y) * 2 < profile.StartRegionRadiusNative)
					Hard("START_APRON", $"Start {start} violates the native-cell start-region radius.");
				if (map.RouteIds[map.Index(start)] < 0)
					Hard("START_ROUTE", $"Start {start} is not connected to a reserved strategic route.");
			}

			var allowedTemplates = profile.ClearTemplateIds.ToHashSet();
			for (var i = 0; i < map.TemplateIds.Length; i++)
			{
				if (!allowedTemplates.Contains(map.TemplateIds[i]))
					Hard("TERRAIN_TEMPLATE", $"Logical cell {i} uses template {map.TemplateIds[i]}, which is not in the Clear-only allow-list.");
				if (map.Obstacles[i])
					Hard("OBSTACLE_CONTRACT", $"Logical cell {i} is blocked despite the Version 1 zero-obstacle contract.");
			}

			for (var y = 0; y < map.Height; y++)
				for (var x = 0; x < map.Width; x++)
				{
					var point = new RmgPoint(x, y);
					var transformed = Transform(point, settings.Symmetry, map.Width, map.Height);
					if (map.TemplateIds[map.Index(point)] != map.TemplateIds[map.Index(transformed)])
						Hard("TERRAIN_SYMMETRY", $"Terrain at {point} differs from its symmetry partner {transformed}.");
					if (map.RouteIds[map.Index(point)] >= 0 != map.RouteIds[map.Index(transformed)] >= 0)
						Hard("ROUTE_SYMMETRY", $"Route reservation at {point} differs from its symmetry partner {transformed}.");
				}

			var spawns = map.Actors.Where(a => a.Type == profile.SpawnActor).ToArray();
			var colonies = map.Actors.Where(a => a.Owner == profile.ColonyOwner).ToArray();
			if (spawns.Length != settings.PlayerCount)
				Hard("SPAWN_ACTORS", $"Expected {settings.PlayerCount} spawn actors but found {spawns.Length}.");
			if (colonies.Length != settings.NeutralColonyCount)
				Hard("COLONY_COUNT", $"Expected {settings.NeutralColonyCount} neutral colonies but found {colonies.Length}.");

			foreach (var actor in map.Actors)
			{
				var partner = Transform(actor.LogicalLocation, settings.Symmetry, map.Width, map.Height);
				if (!map.Actors.Any(a => a.Type == actor.Type && a.Owner == actor.Owner && a.Role == actor.Role && a.LogicalLocation == partner))
					Hard("ACTOR_SYMMETRY", $"Actor {actor.Type} at {actor.LogicalLocation} lacks an equivalent symmetry partner.");
			}

			for (var i = 0; i < colonies.Length; i++)
				for (var j = i + 1; j < colonies.Length; j++)
					if (colonies[i].LogicalLocation.ChebyshevDistance(colonies[j].LogicalLocation) < 5)
						Hard("COLONY_OVERLAP", $"Colonies at {colonies[i].LogicalLocation} and {colonies[j].LogicalLocation} have overlapping safety envelopes.");

			var colonyAssignments = new int[settings.PlayerCount];
			foreach (var colony in colonies)
			{
				var nearest = Enumerable.Range(0, map.Starts.Count)
					.OrderBy(i => colony.LogicalLocation.ManhattanDistance(map.Starts[i]))
					.ThenBy(i => i)
					.First();
				colonyAssignments[nearest]++;
			}

			var center = new RmgPoint((map.Width - 1) / 2, (map.Height - 1) / 2);
			var centralCount = colonies.Count(c => c.LogicalLocation.ManhattanDistance(center) <= 16);
			var (reachableStarts, passableCells, maximumStartDistance) = NativeConnectivityProxy(map, profile);
			if (reachableStarts != map.Starts.Count)
				Hard("NATIVE_CONNECTIVITY", $"The native 3x3 obstruction proxy reaches {reachableStarts}/{map.Starts.Count} starts.");

			const int ReservedRouteWidthNative = 6;
			if (profile.MinimumRouteWidthNative > ReservedRouteWidthNative)
				Hard("ROUTE_WIDTH", $"Reserved route width {ReservedRouteWidthNative} is below the required {profile.MinimumRouteWidthNative} native cells.");

			report.Metrics["player_count"] = settings.PlayerCount;
			report.Metrics["neutral_colony_count"] = colonies.Length;
			report.Metrics["colony_assignment_spread"] = colonyAssignments.Max() - colonyAssignments.Min();
			report.Metrics["central_colony_count"] = centralCount;
			report.Metrics["route_cell_count"] = map.RouteIds.Count(r => r >= 0);
			report.Metrics["obstacle_cell_count"] = map.Obstacles.Count(o => o);
			report.Metrics["minimum_colony_separation_logical"] = colonies.Length < 2 ? 0 :
				colonies.SelectMany((a, i) => colonies.Skip(i + 1).Select(b => a.LogicalLocation.ChebyshevDistance(b.LogicalLocation))).Min();
			report.Metrics["minimum_reserved_route_width_native"] = ReservedRouteWidthNative;
			report.Metrics["native_proxy_reachable_starts"] = reachableStarts;
			report.Metrics["native_proxy_passable_cells"] = passableCells;
			report.Metrics["native_proxy_max_start_distance"] = maximumStartDistance;
			report.Metrics["repair_count"] = map.RepairCount;
			report.Metrics["retry_count"] = map.RetryCount;

			if (settings.Archetype == RmgArchetype.CentralContest && centralCount == 0)
				Hard("CENTRAL_CONTEST", "The central-contest archetype did not place a colony in the central scoring zone.");
			if (colonyAssignments.Max() - colonyAssignments.Min() > 2)
				Hard("COLONY_BALANCE", $"Nearest-start colony assignment spread is {colonyAssignments.Max() - colonyAssignments.Min()}, above the Version 1 tolerance of 2.");

			foreach (var startIndex in Enumerable.Range(0, map.Starts.Count))
			{
				var outgoing = map.GraphEdges.Count(e => e.From == $"start-{startIndex}");
				if (outgoing < 2)
					Hard("EDGE_DISJOINT_GRAPH", $"Start {startIndex} has only {outgoing} independent strategic-graph edges.");
			}

			return report;
		}

		static (int ReachableStarts, int PassableCells, int MaximumStartDistance) NativeConnectivityProxy(RmgLogicalMap map, RmgProfile profile)
		{
			var width = profile.PlayableWidth;
			var height = profile.PlayableHeight;
			var blocked = new bool[width * height];
			if (profile.GeneratorVersion >= 2)
				for (var logicalY = 0; logicalY < map.Height; logicalY++)
					for (var logicalX = 0; logicalX < map.Width; logicalX++)
					{
						var logical = new RmgPoint(logicalX, logicalY);
						if (!map.Obstacles[map.Index(logical)])
							continue;
						for (var dy = 0; dy < 2; dy++)
							for (var dx = 0; dx < 2; dx++)
								blocked[(2 * logicalY + dy) * width + 2 * logicalX + dx] = true;
					}

			foreach (var colony in map.Actors.Where(a => a.Owner == profile.ColonyOwner))
			{
				var anchorX = 2 * colony.LogicalLocation.X;
				var anchorY = 2 * colony.LogicalLocation.Y;

				// A conservative 6x6 colony envelope expanded by one native cell models a 3x3 mover.
				for (var y = anchorY - 3; y <= anchorY + 4; y++)
					for (var x = anchorX - 3; x <= anchorX + 4; x++)
						if (x >= 0 && x < width && y >= 0 && y < height)
							blocked[y * width + x] = true;
			}

			var distances = Enumerable.Repeat(-1, width * height).ToArray();
			var firstStart = map.Starts[0];
			var firstIndex = 2 * firstStart.Y * width + 2 * firstStart.X;
			var queue = new Queue<int>();
			distances[firstIndex] = 0;
			queue.Enqueue(firstIndex);
			var directions = new[]
			{
				(-1, -1), (0, -1), (1, -1),
				(-1, 0), (1, 0),
				(-1, 1), (0, 1), (1, 1)
			};

			while (queue.Count > 0)
			{
				var current = queue.Dequeue();
				var currentX = current % width;
				var currentY = current / width;
				foreach (var (dx, dy) in directions)
				{
					var x = currentX + dx;
					var y = currentY + dy;
					if (x < 0 || x >= width || y < 0 || y >= height)
						continue;

					var next = y * width + x;
					if (blocked[next] || distances[next] >= 0)
						continue;

					distances[next] = distances[current] + 1;
					queue.Enqueue(next);
				}
			}

			var reachableStarts = 0;
			var maximumStartDistance = 0;
			foreach (var start in map.Starts)
			{
				var distance = distances[2 * start.Y * width + 2 * start.X];
				if (distance >= 0)
				{
					reachableStarts++;
					maximumStartDistance = Math.Max(maximumStartDistance, distance);
				}
			}

			return (reachableStarts, blocked.Count(cell => !cell), maximumStartDistance);
		}

		static string HashLogicalMap(RmgLogicalMap map)
		{
			var text = new StringBuilder();
			for (var i = 0; i < map.TemplateIds.Length; i++)
				text.Append(map.TemplateIds[i]).Append(',').Append(map.RegionIds[i]).Append(',').Append(map.RouteIds[i]).Append(',')
					.Append(map.StartReservations[i] ? '1' : '0').Append(map.StructureReservations[i] ? '1' : '0').Append(map.Obstacles[i] ? '1' : '0').Append('\n');
			return Sha256(text.ToString());
		}

		static string HashActors(RmgLogicalMap map)
		{
			var text = string.Join("\n", map.Actors.OrderBy(a => a.Type, StringComparer.Ordinal).ThenBy(a => a.Owner, StringComparer.Ordinal)
				.ThenBy(a => a.LogicalLocation.Y).ThenBy(a => a.LogicalLocation.X)
				.Select(a => $"{a.Type}|{a.Owner}|{a.Role}|{a.LogicalLocation.X},{a.LogicalLocation.Y}|{a.EquivalenceGroup}"));
			return Sha256(text);
		}

		static string HashGraph(RmgLogicalMap map)
		{
			var nodes = map.GraphNodes.OrderBy(n => n.Id, StringComparer.Ordinal).Select(n => $"N|{n.Id}|{n.Role}|{n.Location.X},{n.Location.Y}");
			var edges = map.GraphEdges.OrderBy(e => e.Id, StringComparer.Ordinal).Select(e => $"E|{e.Id}|{e.From}|{e.To}|{e.RouteId}");
			return Sha256(string.Join("\n", nodes.Concat(edges)));
		}

		static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
	}
}
