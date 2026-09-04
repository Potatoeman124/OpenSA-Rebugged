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
using OpenRA.Mods.OpenSA.Rmg.NaturalPrototype;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public static partial class RmgGenerator
	{
		static RmgGenerationResult GenerateNaturalLandscape(RmgProfile profile, RmgGenerationSettings settings)
		{
			var map = new RmgLogicalMap(profile.LogicalWidth, profile.LogicalHeight)
			{
				NaturalOriginalSurfaceRelations = settings.OriginalSurfaceRelations
			};
			GenerateStartsAndTopology(map, profile, settings);
			foreach (var start in map.Starts)
				ReserveSquare(map.StartReservations, map, start, profile.StartRegionRadiusNative / 2);

			// Natural terrain is established before gameplay corridors. Only the compact
			// contested hubs are protected from the initial terrain projection.
			MarkStrategicRegions(map, true);

			var variant = (settings.Seed & 1UL) == 0 ?
				NaturalTerrainPrototypeVariant.CorrelatedFieldBaseline :
				NaturalTerrainPrototypeVariant.CorrelatedFieldWithBasinPotential;
			var candidate = NaturalTerrainPrototypeGenerator.Generate(new NaturalTerrainPrototypeSettings
			{
				RootSeed = settings.Seed,
				CandidateIndex = 0,
				Variant = variant,
				OriginalSurfaceRelations = settings.OriginalSurfaceRelations,
				WaterTargetOverride = settings.WaterAmount switch
				{
					RmgParameterLevel.Low => .16,
					RmgParameterLevel.Standard => .20,
					_ => .24
				},
				RockTargetOverride = settings.TacticalTerrain switch
				{
					RmgParameterLevel.Low => .10,
					RmgParameterLevel.Standard => .14,
					_ => .18
				},
				VegetationTargetOverride = settings.TacticalTerrain switch
				{
					RmgParameterLevel.Low => .05,
					RmgParameterLevel.Standard => .08,
					_ => .11
				}
			});

			var waterPriorities = ProjectNaturalTerrain(map, candidate);
			NormalizeNaturalWater(map, profile, settings, waterPriorities);
			map.NaturalPreRouteWaterCount = map.Obstacles.Count(value => value);
			map.NaturalPreRouteInteriorWaterCount = Enumerable.Range(0, map.Obstacles.Length)
				.Count(index => map.Obstacles[index] &&
					WaterInteriorSector(map, new RmgPoint(index % map.Width, index / map.Width)) >= 0);

			ReserveNaturalRoutes(map, profile, settings);
			MarkStrategicRegions(map, true);
			NormalizeNaturalWater(map, profile, settings, waterPriorities);

			PlaceNaturalColonies(map, profile, settings);
			MarkStrategicRegions(map, true);
			NormalizeNaturalWater(map, profile, settings, waterPriorities);
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
				LogicalHash = HashBlockingLogicalMap(map, profile),
				ActorHash = HashActors(map),
				GraphHash = HashBlockingGraph(map)
			};
		}

		static int[] ProjectNaturalTerrain(RmgLogicalMap map, NaturalTerrainPrototypeCandidate candidate)
		{
			if (candidate.Semantic.Length != 4 * map.Width * map.Height)
				throw new InvalidOperationException("Natural prototype dimensions do not match the playable NORMAL map.");

			var waterDecision = candidate.Fields["water-decision"];
			var rockDecision = candidate.Fields["rock-decision"];
			var vegetationDecision = candidate.Fields["vegetation-decision"];
			var waterPriority = new int[map.Obstacles.Length];
			for (var y = 0; y < map.Height; y++)
				for (var x = 0; x < map.Width; x++)
				{
					var point = new RmgPoint(x, y);
					var index = map.Index(point);
					var waterCount = 0;
					var priority = 0D;
					for (var frameY = 0; frameY < 2; frameY++)
						for (var frameX = 0; frameX < 2; frameX++)
						{
							var native = (2 * y + frameY) * NaturalTerrainPrototypeSettings.Width + 2 * x + frameX;
							if (candidate.Semantic[native] == (byte)NaturalTerrainSemantic.Water)
								waterCount++;
							priority += waterDecision[native];
						}

					waterPriority[index] = (int)Math.Round(100000D * priority / 4D);
					var prototypeWater = waterCount >= 2;
					if (prototypeWater)
					{
						map.NaturalPrototypeWaterCount++;
						if (WaterInteriorSector(map, point) >= 0)
							map.NaturalPrototypeInteriorWaterCount++;
					}

					map.Obstacles[index] = prototypeWater && !NaturalProtected(map, index);
				}

			var latticeWidth = map.Width + 1;
			for (var y = 0; y <= map.Height; y++)
				for (var x = 0; x <= map.Width; x++)
				{
					var nativeX = Math.Min(NaturalTerrainPrototypeSettings.Width - 1, 2 * x);
					var nativeY = Math.Min(NaturalTerrainPrototypeSettings.Height - 1, 2 * y);
					var native = nativeY * NaturalTerrainPrototypeSettings.Width + nativeX;
					var semantic = (NaturalTerrainSemantic)candidate.Semantic[native];
					var index = y * latticeWidth + x;
					map.NaturalRockPriorities[index] =
						(semantic is NaturalTerrainSemantic.Rock or NaturalTerrainSemantic.Vegetation ? 1000000 : 0) +
						(int)Math.Round(100000D * rockDecision[native]);
					map.NaturalVegetationPriorities[index] =
						(semantic == NaturalTerrainSemantic.Vegetation ? 1000000 : 0) +
						(int)Math.Round(100000D * vegetationDecision[native]);
				}

			map.NaturalTerrainVariantId = candidate.Settings.VariantId;
			map.NaturalForbiddenSurfaceAdjacencyCount = candidate.ForbiddenSurfaceAdjacencyCount;
			map.NaturalProjectedWaterCount = map.Obstacles.Count(value => value);
			map.NaturalProjectedInteriorWaterCount = Enumerable.Range(0, map.Obstacles.Length)
				.Count(index => map.Obstacles[index] &&
					WaterInteriorSector(map, new RmgPoint(index % map.Width, index / map.Width)) >= 0);
			return waterPriority;
		}

		static void ReserveNaturalRoutes(RmgLogicalMap map, RmgProfile profile, RmgGenerationSettings settings)
		{
			var nodes = map.GraphNodes.ToDictionary(node => node.Id, StringComparer.Ordinal);

			// Five logical cells provide a stable native-width corridor even where
			// shoreline transition templates narrow the passable aperture.
			var logicalRadius = 2;
			var clearedWater = new HashSet<int>();
			foreach (var edge in map.GraphEdges.OrderBy(edge => edge.RouteId))
			{
				var from = nodes[edge.From].Location;
				var to = nodes[edge.To].Location;
				var centerline = FindLeastDamageRoute(from, to, edge.RouteId).ToArray();
				for (var i = 0; i < centerline.Length; i++)
				{
					var radius = logicalRadius;
					if (i > 0 && i + 1 < centerline.Length)
					{
						var incoming = (X: centerline[i].X - centerline[i - 1].X,
							Y: centerline[i].Y - centerline[i - 1].Y);
						var outgoing = (X: centerline[i + 1].X - centerline[i].X,
							Y: centerline[i + 1].Y - centerline[i].Y);
						if (incoming != outgoing)
							radius++;
					}

					for (var dy = -radius; dy <= radius; dy++)
						for (var dx = -radius; dx <= radius; dx++)
						{
							var point = new RmgPoint(centerline[i].X + dx, centerline[i].Y + dy);
							if (!map.Contains(point))
								continue;

							var index = map.Index(point);
							map.RouteMasks[index] |= 1UL << edge.RouteId;
							if (map.RouteIds[index] < 0)
								map.RouteIds[index] = edge.RouteId;
							if (map.Obstacles[index])
							{
								map.Obstacles[index] = false;
								map.ObstacleRegionIds[index] = -1;
								clearedWater.Add(index);
							}
						}
				}
			}

			map.NaturalRouteClearedWaterCount = clearedWater.Count;
			RebuildObstacleRegionMetadata(map);

			IEnumerable<RmgPoint> FindLeastDamageRoute(RmgPoint start, RmgPoint target, int routeId)
			{
				const int DirectionCount = 5;
				const int StartDirection = 4;
				const long WaterPenalty = 1000000L;
				const long TurnPenalty = 3000L;
				const long StepPenalty = 100L;
				var directions = new[]
				{
					new RmgPoint(1, 0),
					new RmgPoint(0, 1),
					new RmgPoint(-1, 0),
					new RmgPoint(0, -1)
				};
				var directDistance = start.ManhattanDistance(target);
				var maximumDetourDistance = directDistance + 24;
				var ownStartRadius = profile.StartRegionRadiusNative / 2;
				var stateCount = map.Obstacles.Length * DirectionCount;
				var distances = Enumerable.Repeat(long.MaxValue, stateCount).ToArray();
				var previous = Enumerable.Repeat(-1, stateCount).ToArray();
				var visited = new bool[stateCount];
				var queue = new PriorityQueue<int, long>();
				var random = DeterministicRandom.ForStream(settings, profile, $"natural-route-{routeId}");
				var startState = map.Index(start) * DirectionCount + StartDirection;
				distances[startState] = 0;
				queue.Enqueue(startState, startState);
				var targetState = -1;
				while (queue.Count > 0)
				{
					var state = queue.Dequeue();
					if (visited[state])
						continue;
					visited[state] = true;

					var cellIndex = state / DirectionCount;
					var incomingDirection = state % DirectionCount;
					var point = new RmgPoint(cellIndex % map.Width, cellIndex / map.Width);
					if (point == target)
					{
						targetState = state;
						break;
					}

					for (var direction = 0; direction < directions.Length; direction++)
					{
						var next = new RmgPoint(point.X + directions[direction].X,
							point.Y + directions[direction].Y);
						if (next.X < logicalRadius || next.Y < logicalRadius ||
							next.X >= map.Width - logicalRadius || next.Y >= map.Height - logicalRadius ||
							next.ManhattanDistance(start) + next.ManhattanDistance(target) > maximumDetourDistance)
							continue;

						var waterCells = 0;
						var existingRouteCells = 0;
						var overlapsStructure = false;
						var overlapsOtherStart = false;
						for (var dy = -logicalRadius; dy <= logicalRadius; dy++)
							for (var dx = -logicalRadius; dx <= logicalRadius; dx++)
							{
								var footprintPoint = new RmgPoint(next.X + dx, next.Y + dy);
								var footprintIndex = map.Index(footprintPoint);
								if (map.StructureReservations[footprintIndex])
									overlapsStructure = true;
								if (map.StartReservations[footprintIndex] &&
									footprintPoint.ChebyshevDistance(start) > ownStartRadius)
									overlapsOtherStart = true;
								if (map.Obstacles[footprintIndex])
									waterCells++;
								if (map.RouteMasks[footprintIndex] != 0)
									existingRouteCells++;
							}

						if (overlapsStructure || overlapsOtherStart)
							continue;

						var nextState = map.Index(next) * DirectionCount + direction;
						var turn = incomingDirection != StartDirection && incomingDirection != direction;
						var stepCost = StepPenalty + waterCells * WaterPenalty +
							(turn ? TurnPenalty : 0) - Math.Min(50, existingRouteCells * 2) +
							random.NextInt(17);
						var candidateDistance = distances[state] + stepCost;
						if (candidateDistance >= distances[nextState])
							continue;

						distances[nextState] = candidateDistance;
						previous[nextState] = state;
						queue.Enqueue(nextState, candidateDistance * stateCount + nextState);
					}
				}

				if (targetState < 0)
					throw new RmgGenerationRejectedException("NATURAL_ROUTE_PLACEMENT",
						$"No terrain-aware route could connect graph edge {routeId}.");

				var reverse = new List<RmgPoint>();
				for (var state = targetState; state >= 0; state = previous[state])
				{
					var cellIndex = state / DirectionCount;
					reverse.Add(new RmgPoint(cellIndex % map.Width, cellIndex / map.Width));
				}

				reverse.Reverse();
				return reverse;
			}
		}

		static void PlaceNaturalColonies(RmgLogicalMap map, RmgProfile profile, RmgGenerationSettings settings)
		{
			var minimumColonyCount = MinimumAdaptiveColonyCount(profile, settings);
			var requestedRounds = settings.NeutralColonyCount / settings.PlayerCount;
			var minimumRounds = (minimumColonyCount + settings.PlayerCount - 1) / settings.PlayerCount;
			var searchNodes = 0;
			const int MaximumSearchNodes = 8192;
			const int CandidateLimit = 192;
			var typeRandom = DeterministicRandom.ForStream(settings, profile, "natural-colony-types");
			var completedRounds = 0;
			var stopReason = "requested target reached";
			for (var round = 0; round < requestedRounds; round++)
			{
				var actorCount = map.Actors.Count;
				var role = round < 2 ? "near-start" : round % 2 == 0 ? "side-route" : "peripheral";
				var actorType = profile.NeutralColonyActors[typeRandom.NextInt(profile.NeutralColonyActors.Length)];
				if (!PlaceForPlayer(0))
				{
					map.Actors.RemoveRange(actorCount, map.Actors.Count - actorCount);
					stopReason = searchNodes > MaximumSearchNodes ?
						$"search exceeded {MaximumSearchNodes} nodes during round {round + 1}" :
						$"round {round + 1} had no complete player-balanced combat-safe placement";
					break;
				}

				var proposedClearance = NaturalColonyClearance();
				var proposedInteriorClearance = proposedClearance.Count(index =>
					WaterInteriorSector(map, new RmgPoint(index % map.Width, index / map.Width)) >= 0);
				var totalClearanceLimit = (int)Math.Ceiling(map.NaturalPrototypeWaterCount * .30D);
				var interiorClearanceLimit = (int)Math.Ceiling(map.NaturalPrototypeInteriorWaterCount * .30D);
				if (proposedClearance.Count > totalClearanceLimit ||
					proposedInteriorClearance > interiorClearanceLimit)
				{
					map.Actors.RemoveRange(actorCount, map.Actors.Count - actorCount);
					stopReason = $"round {round + 1} would clear {proposedClearance.Count}/{totalClearanceLimit} total " +
						$"and {proposedInteriorClearance}/{interiorClearanceLimit} interior prototype Water cells";
					break;
				}

				completedRounds++;

				bool PlaceForPlayer(int playerIndex)
				{
					if (playerIndex == map.Starts.Count)
						return true;

					var target = map.Starts[playerIndex];
					var request = new ColonyRequest(role, new[] { target }, round);
					var tieRandom = DeterministicRandom.ForStream(settings, profile,
						$"natural-colony-{round}-{playerIndex}");
					var candidates = Enumerable.Range(7, map.Height - 14)
						.SelectMany(y => Enumerable.Range(7, map.Width - 14).Select(x => new RmgPoint(x, y)))
						.Where(point => NearestStart(map, point) == target)
						.Select(point => (Point: point,
							Score: ColonyScore(map, new[] { point }, request) -
								1000000L * NaturalColonyWaterCost(point),
							Tie: tieRandom.NextInt(int.MaxValue)))
						.OrderByDescending(candidate => candidate.Score)
						.ThenBy(candidate => candidate.Tie)
						.ThenBy(candidate => candidate.Point.Y)
						.ThenBy(candidate => candidate.Point.X)
						.Where(candidate => NaturalColonyLocationIsValid(actorType, candidate.Point))
						.Take(CandidateLimit)
						.ToArray();
					foreach (var (point, _, _) in candidates)
					{
						if (++searchNodes > MaximumSearchNodes)
							return false;

						map.Actors.Add(new RmgActorPlan(actorType, profile.ColonyOwner, role, point, round));
						if (PlaceForPlayer(playerIndex + 1))
							return true;
						map.Actors.RemoveAt(map.Actors.Count - 1);
					}

					return false;
				}
			}

			map.ColonySearchNodes = searchNodes;
			if (completedRounds < minimumRounds)
				throw new RmgGenerationRejectedException("COLONY_PLACEMENT",
					$"Natural terrain safely supports only {completedRounds * settings.PlayerCount} colonies; " +
					$"the adaptive minimum is {minimumColonyCount}. Stop reason: {stopReason}.");

			var clearedWater = NaturalColonyClearance();
			foreach (var index in clearedWater)
			{
				map.Obstacles[index] = false;
				map.ObstacleRegionIds[index] = -1;
			}

			foreach (var actor in map.Actors.Where(actor => actor.Owner == profile.ColonyOwner))
				ReserveSquare(map.StructureReservations, map, actor.LogicalLocation, 4);

			map.NaturalColonyClearedWaterCount = clearedWater.Count;
			RebuildObstacleRegionMetadata(map);

			HashSet<int> NaturalColonyClearance()
			{
				var result = new HashSet<int>();
				foreach (var actor in map.Actors.Where(actor => actor.Owner == profile.ColonyOwner))
					for (var dy = -4; dy <= 4; dy++)
						for (var dx = -4; dx <= 4; dx++)
						{
							var index = map.Index(new RmgPoint(actor.LogicalLocation.X + dx,
								actor.LogicalLocation.Y + dy));
							if (map.Obstacles[index])
								result.Add(index);
						}

				return result;
			}

			int NaturalColonyWaterCost(RmgPoint point)
			{
				var water = 0;
				for (var dy = -4; dy <= 4; dy++)
					for (var dx = -4; dx <= 4; dx++)
						if (map.Obstacles[map.Index(new RmgPoint(point.X + dx, point.Y + dy))])
							water++;
				return water;
			}

			bool NaturalColonyLocationIsValid(string actorType, RmgPoint point)
			{
				if (point.X < 4 || point.Y < 4 || point.X >= map.Width - 4 || point.Y >= map.Height - 4 ||
					map.GraphNodes.Where(node => node.Role == "hub")
						.Any(node => point.ChebyshevDistance(node.Location) < 8))
					return false;
				for (var dy = -1; dy <= 3; dy++)
					for (var dx = -1; dx <= 3; dx++)
						if (map.RouteMasks[map.Index(new RmgPoint(point.X + dx, point.Y + dy))] != 0)
							return false;
				return ColonyCombatSpaceIsValid(map, profile, actorType, point);
			}
		}

		static void NormalizeNaturalWater(RmgLogicalMap map, RmgProfile profile,
			RmgGenerationSettings settings, int[] waterPriorities)
		{
			RemoveSmallWaterBodies();
			NormalizeShorelineNeighborhoods(map, null);
			ReconnectOpenLand();
			RemoveSmallWaterBodies();
			NormalizeShorelineNeighborhoods(map, null);
			RebuildObstacleRegionMetadata(map);
			CompleteNaturalWaterDensity(map, profile, settings, waterPriorities);

			RmgPoint IndexPoint(int index) => new(index % map.Width, index / map.Width);

			void RemoveSmallWaterBodies()
			{
				foreach (var component in ConnectedComponents(map, blocked: true))
					if (component.Count < profile.ObstacleRegionMinimumLogical)
						foreach (var index in component)
							map.Obstacles[index] = false;
			}

			void ReconnectOpenLand()
			{
				for (var pass = 0; pass < 8; pass++)
				{
					var components = ConnectedComponents(map, blocked: false);
					if (components.Count <= 1)
						return;

					var main = components.OrderByDescending(component =>
						component.Count(index => map.StartReservations[index] || map.StructureReservations[index] ||
							map.RouteMasks[index] != 0 || map.StrategicRegions[index]))
						.ThenByDescending(component => component.Count)
						.First();
					foreach (var island in components.Where(component => !ReferenceEquals(component, main)))
					{
						var (first, second, _) = island.SelectMany(a => main.Select(b => (A: a, B: b,
								Distance: IndexPoint(a).ManhattanDistance(IndexPoint(b)))))
							.OrderBy(entry => entry.Distance).ThenBy(entry => entry.A).ThenBy(entry => entry.B).First();
						foreach (var point in RasterizeLine(IndexPoint(first), IndexPoint(second)))
							for (var dy = -1; dy <= 1; dy++)
								for (var dx = -1; dx <= 1; dx++)
								{
									var widened = new RmgPoint(point.X + dx, point.Y + dy);
									if (map.Contains(widened))
										map.Obstacles[map.Index(widened)] = false;
								}
					}

					NormalizeShorelineNeighborhoods(map, null);
				}

				if (ConnectedComponents(map, blocked: false).Count > 1)
					throw new RmgGenerationRejectedException("NATURAL_OPEN_CONNECTIVITY",
						"Natural terrain normalization could not reconnect all playable land.");
			}
		}

		static void CompleteNaturalWaterDensity(RmgLogicalMap map, RmgProfile profile,
			RmgGenerationSettings settings, int[] waterPriorities)
		{
			var desiredPercent = profile.ObstacleDensityTarget(settings.Archetype, settings.WaterAmount);
			var desiredTarget = (int)Math.Ceiling(map.Obstacles.Length * desiredPercent / 100D);
			var (_, maximumPercent) = profile.ObstacleDensityRange(settings.Archetype, settings.WaterAmount);
			var maximumTarget = (int)Math.Floor(map.Obstacles.Length * maximumPercent / 100D);
			var (interiorMinimumDensity, interiorMinimumSectors) = WaterInteriorMinimum(settings.WaterAmount, true);
			var rejectedCandidates = new HashSet<int>();
			RebuildObstacleRegionMetadata(map);
			for (var operation = 0; operation < map.Obstacles.Length; operation++)
			{
				var (interiorDensity, _, interiorCoveredSectors, _) = WaterInteriorMetrics(map);
				if (map.Obstacles.Count(value => value) >= desiredTarget &&
					interiorDensity >= interiorMinimumDensity && interiorCoveredSectors >= interiorMinimumSectors)
					break;

				RmgPoint[] bestAdded = null;
				var bestPriority = int.MinValue;
				var bestTieBreak = int.MaxValue;
				var bestRegionId = -1;
				foreach (var region in map.ObstacleRegions)
				{
					var cells = Enumerable.Range(0, map.ObstacleRegionIds.Length)
						.Where(index => map.ObstacleRegionIds[index] == region.Id)
						.Select(IndexPoint).ToHashSet();
					var candidates = cells.SelectMany(point => new[]
					{
						new RmgPoint(point.X - 1, point.Y - 1),
						new RmgPoint(point.X - 1, point.Y),
						new RmgPoint(point.X, point.Y - 1),
						point
					}).Distinct().OrderBy(point => point.Y).ThenBy(point => point.X);
					foreach (var topLeft in candidates)
					{
						var tieBreak = map.Index(topLeft);
						if (rejectedCandidates.Contains(tieBreak))
							continue;

						var block = new[]
						{
							new RmgPoint(topLeft.X, topLeft.Y),
							new RmgPoint(topLeft.X + 1, topLeft.Y),
							new RmgPoint(topLeft.X, topLeft.Y + 1),
							new RmgPoint(topLeft.X + 1, topLeft.Y + 1)
						};
						var added = block.Where(point => !cells.Contains(point)).Distinct().ToArray();
						if (added.Length == 0 || map.Obstacles.Count(value => value) + added.Length > maximumTarget ||
							cells.Count + added.Length > profile.ObstacleRegionMaximumLogical ||
							added.Any(point => !ObstacleExtensionCellEligible(map, point, region.Id)))
							continue;

						var candidate = cells.Concat(added).ToHashSet();
						if (!ShorelineShapeIsSupported(candidate))
							continue;

						var priority = added.Sum(point => waterPriorities[map.Index(point)]) +
							1000000 * added.Count(point => WaterInteriorSector(map, point) >= 0);
						if (priority > bestPriority || (priority == bestPriority && tieBreak < bestTieBreak))
						{
							bestAdded = added;
							bestPriority = priority;
							bestTieBreak = tieBreak;
							bestRegionId = region.Id;
						}
					}
				}

				if (bestAdded == null)
				{
					if (TrySeedNaturalWaterRegion())
						continue;
					break;
				}

				foreach (var point in bestAdded)
				{
					var index = map.Index(point);
					map.Obstacles[index] = true;
					map.ObstacleRegionIds[index] = bestRegionId;
				}

				if (ConnectedComponents(map, blocked: false).Count != 1)
				{
					foreach (var point in bestAdded)
					{
						var index = map.Index(point);
						map.Obstacles[index] = false;
						map.ObstacleRegionIds[index] = -1;
					}

					rejectedCandidates.Add(bestTieBreak);
				}
				else
					map.ObstacleRegions[bestRegionId] = map.ObstacleRegions[bestRegionId] with
					{
						CellCount = map.ObstacleRegions[bestRegionId].CellCount + bestAdded.Length
					};
			}

			RebuildObstacleRegionMetadata(map);

			RmgPoint IndexPoint(int index) => new(index % map.Width, index / map.Width);

			bool TrySeedNaturalWaterRegion()
			{
				const int SeedSize = 4;
				var currentWater = map.Obstacles.Count(value => value);
				if (currentWater + SeedSize * SeedSize > maximumTarget)
					return false;

				var candidates = new List<(RmgPoint[] Cells, int Priority, int Tie)>();
				for (var y = 2; y <= map.Height - SeedSize - 2; y++)
					for (var x = 2; x <= map.Width - SeedSize - 2; x++)
					{
						var cells = Enumerable.Range(0, SeedSize)
							.SelectMany(dy => Enumerable.Range(0, SeedSize)
								.Select(dx => new RmgPoint(x + dx, y + dy))).ToArray();
						if (cells.Any(point => !ObstacleExtensionCellEligible(map, point, -1)) ||
							!ShorelineShapeIsSupported(cells.ToHashSet()))
							continue;
						var priority = cells.Sum(point => waterPriorities[map.Index(point)]) +
							1000000 * cells.Count(point => WaterInteriorSector(map, point) >= 0);
						candidates.Add((cells, priority, map.Index(cells[0])));
					}

				foreach (var (cells, _, _) in candidates.OrderByDescending(candidate => candidate.Priority)
					.ThenBy(candidate => candidate.Tie))
				{
					foreach (var point in cells)
						map.Obstacles[map.Index(point)] = true;
					if (ConnectedComponents(map, blocked: false).Count == 1)
					{
						RebuildObstacleRegionMetadata(map);
						return true;
					}

					foreach (var point in cells)
						map.Obstacles[map.Index(point)] = false;
				}

				return false;
			}
		}

		static void NormalizeShorelineNeighborhoods(RmgLogicalMap map, int[] waterPriority)
		{
			for (var repair = 0; repair < map.Obstacles.Length; repair++)
			{
				var unsupported = Enumerable.Range(0, map.Obstacles.Length)
					.Where(index => map.Obstacles[index])
					.Select(index => (Index: index, Point: IndexPoint(index)))
					.Where(entry => RmgShorelineMaterializer.Classify(map, entry.Point, out _, out _) ==
						RmgShorelineRole.Unsupported)
					.OrderBy(entry => waterPriority == null ? entry.Index : waterPriority[entry.Index])
					.ThenBy(entry => entry.Index)
					.Select(entry => entry.Index)
					.DefaultIfEmpty(-1)
					.First();
				if (unsupported < 0)
					return;

				map.Obstacles[unsupported] = false;
			}

			throw new RmgGenerationRejectedException("NATURAL_SHORELINE_NORMALIZATION",
				"Natural Water morphology exceeded the bounded shoreline-normalization budget.");

			RmgPoint IndexPoint(int index) => new(index % map.Width, index / map.Width);
		}

		static bool NaturalProtected(RmgLogicalMap map, int index) =>
			map.StartReservations[index] || map.StructureReservations[index] ||
			map.RouteMasks[index] != 0 || map.StrategicRegions[index];
	}
}
