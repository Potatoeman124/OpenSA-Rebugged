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
using Newtonsoft.Json.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public enum RmgMovementValidationMode
	{
		Proxy,
		Native,
		Both
	}

	public sealed class RmgNativeMovementValidationResult
	{
		public const string ValidatorName = "openra-static-ground-v1";

		public string MoverClass { get; init; }
		public string MoverImplementation { get; init; }
		public JArray MoverClasses { get; init; }
		public JObject TerrainCosts { get; init; }
		public int TerrainPassableCells { get; init; }
		public int StaticBlockedCells { get; init; }
		public int TransitOnlyCells { get; init; }
		public int StartingColonyBlockedCells { get; init; }
		public string[] StartingColonyActors { get; init; }
		public int StartRegions { get; init; }
		public int ReachableStartRegions { get; init; }
		public int NeutralColonies { get; init; }
		public int ReachableNeutralColonies { get; init; }
		public int RequiredRoutes { get; init; }
		public int TraversableRoutes { get; init; }
		public int MinimumUsableRouteWidth { get; init; }
		public bool ProxyAccepted { get; init; }
		public bool NativeStartConnectivityAccepted { get; init; }
		public int ProxyFalseNegativeCells { get; init; }
		public int ProxyFalsePositiveCells { get; init; }
		public JArray PassabilityDisagreements { get; init; }
		public JArray RouteMeasurements { get; init; }
		public JArray FailureDetails { get; init; }
		public List<RmgValidationIssue> HardFailures { get; } = new();
		public List<RmgValidationIssue> Warnings { get; } = new();
		public bool Accepted => HardFailures.Count == 0;

		public JObject ToJson()
		{
			return new JObject
			{
				["validator"] = ValidatorName,
				["scope"] = "ground layer, static actors, and worst-case starting-colony footprints",
				["mover_class"] = MoverClass,
				["mover_implementation"] = MoverImplementation,
				["mover_classes"] = MoverClasses,
				["terrain_costs"] = TerrainCosts,
				["accepted"] = Accepted,
				["hard_failures"] = new JArray(HardFailures.Select(f => f.ToJson())),
				["warnings"] = new JArray(Warnings.Select(w => w.ToJson())),
				["metrics"] = new JObject
				{
					["terrain_passable_cells"] = TerrainPassableCells,
					["static_blocked_cells"] = StaticBlockedCells,
					["transit_only_cells"] = TransitOnlyCells,
					["starting_colony_blocked_cells"] = StartingColonyBlockedCells,
					["start_regions"] = StartRegions,
					["reachable_start_regions"] = ReachableStartRegions,
					["neutral_colonies"] = NeutralColonies,
					["reachable_neutral_colonies"] = ReachableNeutralColonies,
					["required_routes"] = RequiredRoutes,
					["traversable_routes"] = TraversableRoutes,
					["minimum_usable_route_width_native"] = MinimumUsableRouteWidth
				},
				["starting_colony_actors"] = new JArray(StartingColonyActors),
				["route_scope"] = "abstract graph node-to-node reachability; Version 1 route reservations do not constrain terrain",
				["route_measurements"] = RouteMeasurements,
				["proxy_comparison"] = new JObject
				{
					["proxy_accepted"] = ProxyAccepted,
					["native_start_connectivity_accepted"] = NativeStartConnectivityAccepted,
					["topology_result_disagreement"] = ProxyAccepted != NativeStartConnectivityAccepted,
					["overall_result_disagreement"] = ProxyAccepted != Accepted,
					["proxy_false_negative_cells"] = ProxyFalseNegativeCells,
					["proxy_false_positive_cells"] = ProxyFalsePositiveCells,
					["sample_cells"] = PassabilityDisagreements
				},
				["failure_details"] = FailureDetails,
				["limitations"] = new JArray(
					"Does not instantiate a live World, whose engine constructor is internal and requires lobby, order-manager, renderer, and player state.",
					"Dynamic mobile actors, temporary blockers, crushing relationships, lane bias, and custom movement layers are outside this static map gate.",
					"All possible starting-colony footprints are combined at every start. This is conservative relative to any real faction assignment.")
			};
		}
	}

	public static class NativeMovementValidator
	{
		const string GroundLocomotorName = "unit";
		const int MaximumDisagreementSamples = 32;

		static readonly CVec[] Directions =
		{
			new(-1, -1), new(0, -1), new(1, -1), new(-1, 0),
			new(1, 0), new(-1, 1), new(0, 1), new(1, 1)
		};

		public static RmgNativeMovementValidationResult Validate(Map map, RmgGenerationResult generation)
		{
			var worldInfo = map.Rules.Actors[SystemActors.World];
			var locomotors = worldInfo.TraitInfos<LocomotorInfo>()
				.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
				.ToArray();
			var groundLocomotor = locomotors.Single(l => l.Name.Equals(GroundLocomotorName, StringComparison.OrdinalIgnoreCase));
			var baseGrid = BuildGrid(map, groundLocomotor, out var actors, out var transitOnlyCells);
			var startingUnits = worldInfo.TraitInfos<StartingUnitsInfo>()
				.Where(s => !string.IsNullOrEmpty(s.BaseActor))
				.OrderBy(s => s.BaseActor, StringComparer.OrdinalIgnoreCase)
				.ThenBy(s => s.BaseActorOffset.X)
				.ThenBy(s => s.BaseActorOffset.Y)
				.ToArray();

			var withStarts = baseGrid.Clone();
			var startingBlocked = new HashSet<CPos>();
			foreach (var start in generation.Map.Starts)
			{
				var nativeStart = OpenRaRmgMapAdapter.ToNative(start, generation.Profile);
				foreach (var startingUnit in startingUnits)
				{
					var footprint = Footprint(map, startingUnit.BaseActor, nativeStart + startingUnit.BaseActorOffset);
					startingBlocked.UnionWith(footprint.Blocked);
				}
			}

			foreach (var cell in startingBlocked)
				withStarts.Block(cell, "starting-colony-union");

			var components = Components(withStarts);
			var startAccess = generation.Map.Starts.Select(start =>
			{
				var nativeStart = OpenRaRmgMapAdapter.ToNative(start, generation.Profile);
				var localCoverage = startingUnits
					.SelectMany(s => Footprint(map, s.BaseActor, nativeStart + s.BaseActorOffset).Coverage)
					.ToHashSet();
				return AccessCells(withStarts, localCoverage.Count > 0 ? localCoverage : new[] { nativeStart });
			}).ToArray();

			var commonComponent = CommonComponent(components, startAccess);
			var reachableStarts = commonComponent < 0 ? 0 : startAccess.Count(cells => cells.Any(c => components.Label(c) == commonComponent));
			var nativeStartsAccepted = reachableStarts == generation.Map.Starts.Count;

			var colonyTypes = generation.Profile.NeutralColonyActors.ToHashSet(StringComparer.OrdinalIgnoreCase);
			var colonies = actors.Where(a => colonyTypes.Contains(a.Type)).ToArray();
			var reachableColonies = commonComponent < 0 ? 0 : colonies.Count(c =>
				AccessCells(withStarts, c.Coverage).Any(cell => components.Label(cell) == commonComponent));

			var clearance = Clearance(withStarts);
			var nodes = generation.Map.GraphNodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
			var routeMeasurements = new JArray();
			var traversableRoutes = 0;
			var minimumRouteWidth = int.MaxValue;
			foreach (var edge in generation.Map.GraphEdges.OrderBy(e => e.RouteId))
			{
				var from = nodes[edge.From];
				var to = nodes[edge.To];
				var fromRadius = from.Role == "start" ? generation.Profile.StartRegionRadiusNative : 4;
				var toRadius = to.Role == "start" ? generation.Profile.StartRegionRadiusNative : 4;
				var sources = RegionCells(withStarts, OpenRaRmgMapAdapter.ToNative(from.Location, generation.Profile), fromRadius);
				var targets = RegionCells(withStarts, OpenRaRmgMapAdapter.ToNative(to.Location, generation.Profile), toRadius);
				var traversable = CanConnect(withStarts, sources, targets, null, 1);
				var usableWidth = WidestPathWidth(withStarts, clearance, sources, targets);
				if (traversable)
					traversableRoutes++;
				minimumRouteWidth = Math.Min(minimumRouteWidth, usableWidth);
				routeMeasurements.Add(new JObject
				{
					["edge"] = edge.Id,
					["route_id"] = edge.RouteId,
					["from"] = edge.From,
					["to"] = edge.To,
					["from_cell"] = Point(OpenRaRmgMapAdapter.ToNative(from.Location, generation.Profile)),
					["to_cell"] = Point(OpenRaRmgMapAdapter.ToNative(to.Location, generation.Profile)),
					["traversable"] = traversable,
					["widest_path_native"] = usableWidth,
					["meets_configured_width"] = usableWidth >= generation.Profile.MinimumRouteWidthNative
				});
			}

			if (minimumRouteWidth == int.MaxValue)
				minimumRouteWidth = 0;

			var proxyMask = BuildProxyMask(generation);
			var falseNegatives = 0;
			var falsePositives = 0;
			var falseNegativeSamples = 0;
			var falsePositiveSamples = 0;
			var disagreementSamples = new JArray();
			for (var localY = 0; localY < baseGrid.Height; localY++)
				for (var localX = 0; localX < baseGrid.Width; localX++)
				{
					var index = localY * baseGrid.Width + localX;
					var nativePassable = baseGrid.Passable[index];
					var proxyPassable = proxyMask[index];
					if (nativePassable == proxyPassable)
						continue;

					var classification = nativePassable ? "proxy false negative" : "proxy false positive";
					if (nativePassable)
						falseNegatives++;
					else
						falsePositives++;

					var canSample = nativePassable
						? falseNegativeSamples < MaximumDisagreementSamples / 2
						: falsePositiveSamples < MaximumDisagreementSamples / 2;
					if (canSample)
					{
						var cell = new CPos(baseGrid.Left + localX, baseGrid.Top + localY);
						disagreementSamples.Add(new JObject
						{
							["classification"] = classification,
							["cell"] = Point(cell),
							["proxy_passable"] = proxyPassable,
							["native_passable"] = nativePassable,
							["native_reason"] = baseGrid.Reasons[index] ?? "passable"
						});
						if (nativePassable)
							falseNegativeSamples++;
						else
							falsePositiveSamples++;
					}
				}

			var proxyAccepted = generation.Validation.Metrics.TryGetValue("native_proxy_reachable_starts", out var proxyReachable) &&
				(int)proxyReachable == generation.Map.Starts.Count;
			var failureDetails = new JArray();
			var result = new RmgNativeMovementValidationResult
			{
				MoverClass = groundLocomotor.Name,
				MoverImplementation = groundLocomotor.GetType().FullName,
				MoverClasses = new JArray(locomotors.Select(LocomotorJson)),
				TerrainCosts = new JObject(groundLocomotor.TerrainSpeeds.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
					.Select(kv => new JProperty(kv.Key, new JObject { ["speed_percent"] = kv.Value.Speed, ["pathing_cost"] = kv.Value.Cost }))),
				TerrainPassableCells = baseGrid.TerrainPassableCells,
				StaticBlockedCells = baseGrid.StaticBlockedCells,
				TransitOnlyCells = transitOnlyCells,
				StartingColonyBlockedCells = startingBlocked.Count(withStarts.Contains),
				StartingColonyActors = startingUnits.Select(s => s.BaseActor).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
				StartRegions = generation.Map.Starts.Count,
				ReachableStartRegions = reachableStarts,
				NeutralColonies = colonies.Length,
				ReachableNeutralColonies = reachableColonies,
				RequiredRoutes = generation.Map.GraphEdges.Count,
				TraversableRoutes = traversableRoutes,
				MinimumUsableRouteWidth = minimumRouteWidth,
				ProxyAccepted = proxyAccepted,
				NativeStartConnectivityAccepted = nativeStartsAccepted,
				ProxyFalseNegativeCells = falseNegatives,
				ProxyFalsePositiveCells = falsePositives,
				PassabilityDisagreements = disagreementSamples,
				RouteMeasurements = routeMeasurements,
				FailureDetails = failureDetails
			};

			if (startingUnits.Length == 0)
				Hard("NATIVE_STARTING_UNITS", "No StartingUnitsInfo base actors were available for start-footprint validation.");
			if (!nativeStartsAccepted)
			{
				Hard("NATIVE_START_CONNECTIVITY", $"Engine-grounded static validation reaches {reachableStarts}/{generation.Map.Starts.Count} start regions.");
				for (var i = 0; i < startAccess.Length; i++)
					if (commonComponent < 0 || !startAccess[i].Any(c => components.Label(c) == commonComponent))
						failureDetails.Add(new JObject { ["kind"] = "start", ["start"] = i, ["cell"] = Point(OpenRaRmgMapAdapter.ToNative(generation.Map.Starts[i], generation.Profile)) });
			}

			if (reachableColonies != colonies.Length)
			{
				Hard("NATIVE_COLONY_REACHABILITY", $"Engine-grounded static validation reaches {reachableColonies}/{colonies.Length} neutral colonies from the common start component.");
				foreach (var colony in colonies)
					if (commonComponent < 0 || !AccessCells(withStarts, colony.Coverage).Any(c => components.Label(c) == commonComponent))
						failureDetails.Add(new JObject { ["kind"] = "neutral-colony", ["actor"] = colony.Type, ["cell"] = Point(colony.Location) });
			}

			if (traversableRoutes != generation.Map.GraphEdges.Count)
				Hard("NATIVE_ROUTE_CONNECTIVITY", $"Engine-grounded static validation reaches {traversableRoutes}/{generation.Map.GraphEdges.Count} strategic graph targets.");
			if (minimumRouteWidth < generation.Profile.MinimumRouteWidthNative)
				Hard("NATIVE_ROUTE_WIDTH", $"Minimum widest-path clearance is {minimumRouteWidth} native cells; configured minimum is {generation.Profile.MinimumRouteWidthNative}.");
			if (falsePositives > 0)
				result.Warnings.Add(new RmgValidationIssue("PROXY_FALSE_POSITIVE_CELLS", $"The legacy proxy marks {falsePositives} engine-blocked cells passable. Native validation remains authoritative."));
			if (falseNegatives > 0)
				result.Warnings.Add(new RmgValidationIssue("PROXY_FALSE_NEGATIVE_CELLS", $"The legacy proxy conservatively blocks {falseNegatives} engine-passable cells."));

			return result;

			void Hard(string code, string message) => result.HardFailures.Add(new RmgValidationIssue(code, message));
		}

		public static IReadOnlyList<string> RunSelfTests()
		{
			var failures = new List<string>();
			var open = Grid.Synthetic(15, 15, true);
			var startA = new CPos(3, 7);
			var startB = new CPos(11, 7);
			var target = new CPos(7, 7);
			if (!CanConnect(open, new[] { startA }, new[] { startB }, null, 1))
				failures.Add("Native validator rejected a known-passable topology.");

			var disconnected = open.Clone();
			for (var y = 0; y < disconnected.Height; y++)
				disconnected.Block(new CPos(7, y), "synthetic-barrier");
			if (CanConnect(disconnected, new[] { startA }, new[] { startB }, null, 1))
				failures.Add("Native validator accepted a deliberately disconnected topology.");
			if (CanConnect(disconnected, new[] { startA }, new[] { target + new CVec(2, 0) }, null, 1))
				failures.Add("Native validator failed to reject an unreachable colony target.");

			var colonyGrid = open.Clone();
			var colonyCoverage = new HashSet<CPos>();
			for (var y = 6; y <= 8; y++)
				for (var x = 6; x <= 8; x++)
				{
					var cell = new CPos(x, y);
					colonyCoverage.Add(cell);
					colonyGrid.Block(cell, "synthetic-colony");
				}

			if (!CanConnect(colonyGrid, new[] { startA }, AccessCells(colonyGrid, colonyCoverage), null, 1))
				failures.Add("Native validator rejected reachable colony access cells.");

			var narrow = Grid.Synthetic(15, 15, false);
			for (var y = 6; y <= 8; y++)
				for (var x = 0; x < narrow.Width; x++)
					narrow.Unblock(new CPos(x, y));
			var narrowWidth = WidestPathWidth(narrow, Clearance(narrow), new[] { new CPos(1, 7) }, new[] { new CPos(13, 7) });
			if (narrowWidth >= 5)
				failures.Add("Native route-width test accepted a three-cell corridor as five cells wide.");

			var proxy = Enumerable.Repeat(true, 4).ToArray();
			var native = Enumerable.Repeat(true, 4).ToArray();
			proxy[0] = false;
			native[1] = false;
			var comparisonA = CompareMasks(proxy, native);
			var comparisonB = CompareMasks(proxy, native);
			if (comparisonA != (1, 1) || comparisonA != comparisonB)
				failures.Add("Proxy/native disagreement reporting is not complete and reproducible.");

			return failures;
		}

		static Grid BuildGrid(Map map, LocomotorInfo locomotor, out List<ActorFootprint> actors, out int transitOnlyCells)
		{
			var grid = new Grid(map.Bounds.Left, map.Bounds.Top, map.Bounds.Width, map.Bounds.Height);
			for (var y = grid.Top; y < grid.Top + grid.Height; y++)
				for (var x = grid.Left; x < grid.Left + grid.Width; x++)
				{
					var cell = new CPos(x, y);
					var index = grid.Index(cell);
					var terrain = map.GetTerrainInfo(cell).Type;
					grid.Terrain[index] = terrain;
					grid.HeightLayer[index] = map.Height[cell];
					if (locomotor.TerrainSpeeds.TryGetValue(terrain, out var speed))
					{
						grid.Passable[index] = true;
						grid.Cost[index] = speed.Cost;
						grid.TerrainPassableCells++;
					}
					else
						grid.Reasons[index] = $"terrain:{terrain}";
				}

			actors = new List<ActorFootprint>();
			var transit = new HashSet<CPos>();
			foreach (var definition in map.ActorDefinitions)
			{
				var reference = new ActorReference(definition.Value.Value, definition.Value.ToDictionary());
				var location = reference.GetOrDefault<LocationInit>()?.Value;
				if (location == null || !map.Rules.Actors.TryGetValue(reference.Type, out var actorInfo) || actorInfo.HasTraitInfo<IPositionableInfo>())
					continue;

				var footprint = Footprint(actorInfo, reference.Type, location.Value);
				actors.Add(footprint);
				transit.UnionWith(footprint.TransitOnly);
				foreach (var cell in footprint.Blocked)
					grid.Block(cell, $"actor:{reference.Type}", true);
			}

			transitOnlyCells = transit.Count(grid.Contains);
			return grid;
		}

		static ActorFootprint Footprint(Map map, string actorType, CPos location)
		{
			if (!map.Rules.Actors.TryGetValue(actorType, out var actorInfo))
				return new ActorFootprint(actorType, location, new HashSet<CPos> { location }, new HashSet<CPos> { location }, new HashSet<CPos>());
			return Footprint(actorInfo, actorType, location);
		}

		static ActorFootprint Footprint(ActorInfo actorInfo, string actorType, CPos location)
		{
			var coverage = new HashSet<CPos>();
			var blocked = new HashSet<CPos>();
			var transitOnly = new HashSet<CPos>();
			foreach (var building in actorInfo.TraitInfos<BuildingInfo>())
			{
				coverage.UnionWith(building.Tiles(location));
				blocked.UnionWith(building.OccupiedTiles(location));
				transitOnly.UnionWith(building.TransitOnlyTiles(location));
			}

			if (coverage.Count == 0)
				foreach (var occupySpace in actorInfo.TraitInfos<IOccupySpaceInfo>())
				{
					var occupied = occupySpace.OccupiedCells(actorInfo, location).Keys;
					coverage.UnionWith(occupied);
					blocked.UnionWith(occupied);
				}

			blocked.ExceptWith(transitOnly);
			return new ActorFootprint(actorType, location, coverage, blocked, transitOnly);
		}

		static bool[] BuildProxyMask(RmgGenerationResult generation)
		{
			var width = generation.Profile.PlayableWidth;
			var height = generation.Profile.PlayableHeight;
			var passable = Enumerable.Repeat(true, width * height).ToArray();
			foreach (var colony in generation.Map.Actors.Where(a => a.Owner == generation.Profile.ColonyOwner))
			{
				var anchorX = 2 * colony.LogicalLocation.X;
				var anchorY = 2 * colony.LogicalLocation.Y;
				for (var y = anchorY - 3; y <= anchorY + 4; y++)
					for (var x = anchorX - 3; x <= anchorX + 4; x++)
						if (x >= 0 && x < width && y >= 0 && y < height)
							passable[y * width + x] = false;
			}

			return passable;
		}

		static (int FalseNegatives, int FalsePositives) CompareMasks(bool[] proxy, bool[] native)
		{
			var falseNegatives = 0;
			var falsePositives = 0;
			for (var i = 0; i < proxy.Length; i++)
			{
				if (!proxy[i] && native[i])
					falseNegatives++;
				else if (proxy[i] && !native[i])
					falsePositives++;
			}

			return (falseNegatives, falsePositives);
		}

		static ComponentMap Components(Grid grid)
		{
			var labels = Enumerable.Repeat(-1, grid.CellCount).ToArray();
			var sizes = new List<int>();
			var queue = new Queue<CPos>();
			for (var i = 0; i < grid.CellCount; i++)
			{
				if (!grid.Passable[i] || labels[i] >= 0)
					continue;

				var label = sizes.Count;
				var size = 0;
				var start = grid.Cell(i);
				labels[i] = label;
				queue.Enqueue(start);
				while (queue.Count > 0)
				{
					var current = queue.Dequeue();
					size++;
					foreach (var neighbor in grid.Neighbors(current))
					{
						var index = grid.Index(neighbor);
						if (!grid.Passable[index] || labels[index] >= 0)
							continue;
						labels[index] = label;
						queue.Enqueue(neighbor);
					}
				}

				sizes.Add(size);
			}

			return new ComponentMap(grid, labels, sizes);
		}

		static int CommonComponent(ComponentMap components, CPos[][] accessSets)
		{
			if (accessSets.Length == 0)
				return -1;

			var common = accessSets[0].Select(components.Label).Where(l => l >= 0).ToHashSet();
			foreach (var access in accessSets.Skip(1))
				common.IntersectWith(access.Select(components.Label).Where(l => l >= 0));
			return common.OrderByDescending(label => components.Sizes[label]).ThenBy(label => label).FirstOrDefault(-1);
		}

		static CPos[] AccessCells(Grid grid, IEnumerable<CPos> coverage)
		{
			var footprint = coverage.Where(grid.Contains).ToHashSet();
			var candidates = new HashSet<CPos>(footprint.Where(grid.IsPassable));
			foreach (var cell in footprint)
				foreach (var direction in Directions)
				{
					var candidate = cell + direction;
					if (grid.IsPassable(candidate))
						candidates.Add(candidate);
				}

			return candidates.OrderBy(c => c.Y).ThenBy(c => c.X).ToArray();
		}

		static CPos[] RegionCells(Grid grid, CPos center, int radius)
		{
			var cells = new List<CPos>();
			for (var dy = -radius; dy <= radius; dy++)
				for (var dx = -radius; dx <= radius; dx++)
				{
					if (dx * dx + dy * dy > radius * radius)
						continue;
					var cell = center + new CVec(dx, dy);
					if (grid.IsPassable(cell))
						cells.Add(cell);
				}

			return cells.ToArray();
		}

		static int[] Clearance(Grid grid)
		{
			var distance = Enumerable.Repeat(int.MaxValue, grid.CellCount).ToArray();
			var queue = new Queue<CPos>();
			for (var i = 0; i < grid.CellCount; i++)
			{
				var cell = grid.Cell(i);
				var localX = cell.X - grid.Left;
				var localY = cell.Y - grid.Top;
				if (!grid.Passable[i])
				{
					distance[i] = 0;
					queue.Enqueue(cell);
				}
				else if (localX == 0 || localY == 0 || localX == grid.Width - 1 || localY == grid.Height - 1)
				{
					distance[i] = 1;
					queue.Enqueue(cell);
				}
			}

			while (queue.Count > 0)
			{
				var current = queue.Dequeue();
				foreach (var direction in Directions)
				{
					var neighbor = current + direction;
					if (!grid.Contains(neighbor))
						continue;
					var currentIndex = grid.Index(current);
					var neighborIndex = grid.Index(neighbor);
					if (distance[neighborIndex] <= distance[currentIndex] + 1)
						continue;
					distance[neighborIndex] = distance[currentIndex] + 1;
					queue.Enqueue(neighbor);
				}
			}

			return distance;
		}

		static int WidestPathWidth(Grid grid, int[] clearance, IEnumerable<CPos> sources, IEnumerable<CPos> targets)
		{
			var sourceArray = sources.Where(grid.IsPassable).Distinct().ToArray();
			var targetArray = targets.Where(grid.IsPassable).Distinct().ToArray();
			if (sourceArray.Length == 0 || targetArray.Length == 0)
				return 0;

			var maximumClearance = Math.Min(
				sourceArray.Max(c => clearance[grid.Index(c)]),
				targetArray.Max(c => clearance[grid.Index(c)]));
			for (var minimumClearance = maximumClearance; minimumClearance >= 1; minimumClearance--)
				if (CanConnect(grid, sourceArray, targetArray, clearance, minimumClearance))
					return 2 * minimumClearance - 1;
			return 0;
		}

		static bool CanConnect(Grid grid, IEnumerable<CPos> sources, IEnumerable<CPos> targets, int[] clearance, int minimumClearance)
		{
			var targetIndexes = targets.Where(grid.Contains).Select(grid.Index).ToHashSet();
			var visited = new bool[grid.CellCount];
			var queue = new Queue<CPos>();
			foreach (var source in sources.Where(grid.Contains).Distinct())
			{
				var index = grid.Index(source);
				if (!grid.Passable[index] || (clearance != null && clearance[index] < minimumClearance))
					continue;
				visited[index] = true;
				queue.Enqueue(source);
			}

			while (queue.Count > 0)
			{
				var current = queue.Dequeue();
				if (targetIndexes.Contains(grid.Index(current)))
					return true;
				foreach (var neighbor in grid.Neighbors(current))
				{
					var index = grid.Index(neighbor);
					if (visited[index] || !grid.Passable[index] || (clearance != null && clearance[index] < minimumClearance))
						continue;
					visited[index] = true;
					queue.Enqueue(neighbor);
				}
			}

			return false;
		}

		static JObject LocomotorJson(LocomotorInfo locomotor)
		{
			return new JObject
			{
				["name"] = locomotor.Name,
				["implementation"] = locomotor.GetType().FullName,
				["shares_cell"] = locomotor.SharesCell,
				["disable_domain_passability_check"] = locomotor.DisableDomainPassabilityCheck,
				["terrain_types"] = new JArray(locomotor.TerrainSpeeds.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
			};
		}

		static JObject Point(CPos point) => new() { ["x"] = point.X, ["y"] = point.Y };

		sealed record ActorFootprint(string Type, CPos Location, HashSet<CPos> Coverage, HashSet<CPos> Blocked, HashSet<CPos> TransitOnly);

		sealed class ComponentMap
		{
			readonly Grid grid;
			readonly int[] labels;
			public IReadOnlyList<int> Sizes { get; }

			public ComponentMap(Grid grid, int[] labels, IReadOnlyList<int> sizes)
			{
				this.grid = grid;
				this.labels = labels;
				Sizes = sizes;
			}

			public int Label(CPos cell) => grid.Contains(cell) ? labels[grid.Index(cell)] : -1;
		}

		sealed class Grid
		{
			public int Left { get; }
			public int Top { get; }
			public int Width { get; }
			public int Height { get; }
			public bool[] Passable { get; }
			public short[] Cost { get; }
			public byte[] HeightLayer { get; }
			public string[] Terrain { get; }
			public string[] Reasons { get; }
			public int TerrainPassableCells { get; set; }
			public int StaticBlockedCells { get; set; }
			public int CellCount => Width * Height;

			public Grid(int left, int top, int width, int height)
			{
				Left = left;
				Top = top;
				Width = width;
				Height = height;
				Passable = new bool[CellCount];
				Cost = new short[CellCount];
				HeightLayer = new byte[CellCount];
				Terrain = new string[CellCount];
				Reasons = new string[CellCount];
			}

			public static Grid Synthetic(int width, int height, bool passable)
			{
				var grid = new Grid(0, 0, width, height);
				Array.Fill(grid.Passable, passable);
				if (passable)
					grid.TerrainPassableCells = grid.CellCount;
				return grid;
			}

			public Grid Clone()
			{
				var clone = new Grid(Left, Top, Width, Height)
				{
					TerrainPassableCells = TerrainPassableCells,
					StaticBlockedCells = StaticBlockedCells
				};
				Array.Copy(Passable, clone.Passable, CellCount);
				Array.Copy(Cost, clone.Cost, CellCount);
				Array.Copy(HeightLayer, clone.HeightLayer, CellCount);
				Array.Copy(Terrain, clone.Terrain, CellCount);
				Array.Copy(Reasons, clone.Reasons, CellCount);
				return clone;
			}

			public bool Contains(CPos cell) => cell.X >= Left && cell.X < Left + Width && cell.Y >= Top && cell.Y < Top + Height;
			public int Index(CPos cell) => (cell.Y - Top) * Width + cell.X - Left;
			public CPos Cell(int index) => new(Left + index % Width, Top + index / Width);
			public bool IsPassable(CPos cell) => Contains(cell) && Passable[Index(cell)];

			public void Block(CPos cell, string reason, bool staticActor = false)
			{
				if (!Contains(cell))
					return;
				var index = Index(cell);
				if (Passable[index] && staticActor)
					StaticBlockedCells++;
				Passable[index] = false;
				Reasons[index] = reason;
			}

			public void Unblock(CPos cell)
			{
				if (!Contains(cell))
					return;
				Passable[Index(cell)] = true;
			}

			public IEnumerable<CPos> Neighbors(CPos cell)
			{
				var sourceHeight = HeightLayer[Index(cell)];
				foreach (var direction in Directions)
				{
					var neighbor = cell + direction;
					if (Contains(neighbor) && Math.Abs(HeightLayer[Index(neighbor)] - sourceHeight) <= 1)
						yield return neighbor;
				}
			}
		}
	}
}
