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
using OpenRA.Mods.OpenSA.Traits.World;
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
		public string ValidatorName { get; init; } = "openra-static-ground-v1";
		public string RouteScope { get; init; }

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
		public int MinimumStartEscapeSectors { get; init; }
		public int TerrainSemanticMismatchCells { get; init; }
		public int ProductionExitFailures { get; init; }
		public bool WaspSupportContractAccepted { get; init; }
		public int MinimumChokepointStartDistance { get; init; }
		public int MinimumChokepointColonyDistance { get; init; }
		public int MinimumChokepointSeparation { get; init; }
		public int MinimumBlockedStartCoverageDistance { get; init; }
		public int MinimumBlockedNeutralColonyCoverageDistance { get; init; }
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
					["minimum_usable_route_width_native"] = MinimumUsableRouteWidth,
					["minimum_start_escape_sectors_r12"] = MinimumStartEscapeSectors,
					["terrain_semantic_mismatch_cells"] = TerrainSemanticMismatchCells,
					["production_exit_failures"] = ProductionExitFailures,
					["wasp_support_contract_accepted"] = WaspSupportContractAccepted,
					["minimum_chokepoint_start_distance_native"] = MinimumChokepointStartDistance,
					["minimum_chokepoint_colony_distance_native"] = MinimumChokepointColonyDistance,
					["minimum_chokepoint_separation_native"] = MinimumChokepointSeparation,
					["minimum_blocked_start_coverage_distance_native"] = MinimumBlockedStartCoverageDistance,
					["minimum_blocked_neutral_colony_coverage_distance_native"] = MinimumBlockedNeutralColonyCoverageDistance
				},
				["starting_colony_actors"] = new JArray(StartingColonyActors),
				["route_scope"] = RouteScope,
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
			var startingCoverage = new HashSet<CPos>();
			foreach (var start in generation.Map.Starts)
			{
				var nativeStart = OpenRaRmgMapAdapter.ToNative(start, generation.Profile);
				foreach (var startingUnit in startingUnits)
				{
					var footprint = Footprint(map, startingUnit.BaseActor, nativeStart + startingUnit.BaseActorOffset);
					startingCoverage.UnionWith(footprint.Coverage);
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
			var blockedCells = Enumerable.Range(0, baseGrid.CellCount)
				.Where(i => string.Equals(baseGrid.Terrain[i], "Water", StringComparison.OrdinalIgnoreCase))
				.Select(baseGrid.Cell)
				.ToArray();
			var blockedStartDistance = MinimumCellSetDistance(blockedCells, startingCoverage);
			var blockedNeutralColonyDistance = MinimumCellSetDistance(blockedCells, colonies.SelectMany(c => c.Coverage));
			var reachableColonies = commonComponent < 0 ? 0 : colonies.Count(c =>
				AccessCells(withStarts, c.Coverage).Any(cell => components.Label(cell) == commonComponent));

			var clearance = Clearance(withStarts);
			var nodes = generation.Map.GraphNodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
			var routeMeasurements = new JArray();
			var traversableRoutes = 0;
			var minimumRouteWidth = int.MaxValue;
			var routeWidthFailures = 0;
			foreach (var edge in generation.Map.GraphEdges.OrderBy(e => e.RouteId))
			{
				var from = nodes[edge.From];
				var to = nodes[edge.To];
				var strategicRadius = generation.Profile.GeneratorVersion >= 2 ? 12 : 4;
				var fromRadius = from.Role == "start" ? generation.Profile.StartRegionRadiusNative : strategicRadius;
				var toRadius = to.Role == "start" ? generation.Profile.StartRegionRadiusNative : strategicRadius;
				var sources = RegionCells(withStarts, OpenRaRmgMapAdapter.ToNative(from.Location, generation.Profile), fromRadius);
				var targets = RegionCells(withStarts, OpenRaRmgMapAdapter.ToNative(to.Location, generation.Profile), toRadius);
				var routeGrid = generation.Profile.GeneratorVersion >= 2 ?
					BuildNamedRouteGrid(withStarts, generation, edge.RouteId, from, to) : withStarts;
				var routeClearance = generation.Profile.GeneratorVersion >= 2 ? Clearance(routeGrid) : clearance;
				var traversable = CanConnect(routeGrid, sources, targets, null, 1);
				var widthSources = generation.Profile.GeneratorVersion >= 2 ?
					NamedRouteRingCells(routeGrid, generation, edge.RouteId,
						OpenRaRmgMapAdapter.ToNative(from.Location, generation.Profile), fromRadius) : sources;
				var widthTargets = generation.Profile.GeneratorVersion >= 2 ?
					NamedRouteRingCells(routeGrid, generation, edge.RouteId,
						OpenRaRmgMapAdapter.ToNative(to.Location, generation.Profile), toRadius) : targets;
				var widthSourceArray = widthSources.Where(routeGrid.IsPassable).Distinct().ToArray();
				var widthTargetArray = widthTargets.Where(routeGrid.IsPassable).Distinct().ToArray();
				var sourceMaximumClearance = widthSourceArray.Select(cell => routeClearance[routeGrid.Index(cell)]).DefaultIfEmpty(0).Max();
				var targetMaximumClearance = widthTargetArray.Select(cell => routeClearance[routeGrid.Index(cell)]).DefaultIfEmpty(0).Max();
				var usableWidth = WidestPathWidth(routeGrid, routeClearance, widthSourceArray, widthTargetArray);
				var bottleneckCells = WidestPathBottleneckCells(routeGrid, routeClearance, widthSourceArray, widthTargetArray, usableWidth);
				var expectedWidth = generation.Profile.MinimumRouteWidthNative;
				var choke = generation.Map.Chokepoints.FirstOrDefault(c => c.RouteId == edge.RouteId);
				var measuredWidth = usableWidth;
				var apertureWidth = 0;
				var chokeIsSeparator = true;
				if (choke != null)
				{
					expectedWidth = generation.Profile.ChokepointWidthNative;
					var widthGrid = BuildNamedRouteGrid(withStarts, generation, edge.RouteId, from, to, false);
					var chokeClearance = Clearance(widthGrid);
					var aperture = ChokepointCells(widthGrid, generation, choke);
					apertureWidth = aperture.Length == 0 ? 0 : aperture.Max(cell => 2 * chokeClearance[widthGrid.Index(cell)] - 1);
					var shoulders = ChokepointShoulderCells(widthGrid, generation, choke);
					measuredWidth = WidestPathWidth(widthGrid, chokeClearance, shoulders.Before, shoulders.After);
					var withoutAperture = widthGrid.Clone();
					foreach (var cell in aperture)
						withoutAperture.Block(cell, $"chokepoint-separator:{choke.Id}");
					chokeIsSeparator = !CanConnect(withoutAperture, shoulders.Before, shoulders.After, null, 1);
				}
				else if (generation.Profile.GeneratorVersion >= 2 && generation.Settings.Archetype == RmgArchetype.Open)
					expectedWidth = generation.Profile.MajorRouteWidthNative;
				var widthAccepted = choke != null ?
					apertureWidth == expectedWidth && measuredWidth == expectedWidth && chokeIsSeparator :
					measuredWidth >= expectedWidth;
				if (!widthAccepted)
					routeWidthFailures++;
				if (traversable)
					traversableRoutes++;
				minimumRouteWidth = Math.Min(minimumRouteWidth, measuredWidth);
				routeMeasurements.Add(new JObject
				{
					["edge"] = edge.Id,
					["route_id"] = edge.RouteId,
					["from"] = edge.From,
					["to"] = edge.To,
					["from_cell"] = Point(OpenRaRmgMapAdapter.ToNative(from.Location, generation.Profile)),
					["to_cell"] = Point(OpenRaRmgMapAdapter.ToNative(to.Location, generation.Profile)),
					["traversable"] = traversable,
					["widest_path_native"] = measuredWidth,
					["unconstrained_widest_path_native"] = usableWidth,
					["source_maximum_width_native"] = sourceMaximumClearance == 0 ? 0 : 2 * sourceMaximumClearance - 1,
					["target_maximum_width_native"] = targetMaximumClearance == 0 ? 0 : 2 * targetMaximumClearance - 1,
					["widest_path_bottleneck_cells"] = bottleneckCells,
					["aperture_width_native"] = choke == null ? null : apertureWidth,
					["expected_width_native"] = expectedWidth,
					["contains_intentional_choke"] = choke != null,
					["chokepoint_is_separator"] = choke == null ? null : chokeIsSeparator,
					["meets_configured_width"] = widthAccepted
				});
			}

			if (minimumRouteWidth == int.MaxValue)
				minimumRouteWidth = 0;

			var semanticMismatches = CountTerrainSemanticMismatches(baseGrid, generation, out var semanticMismatchSamples);
			var escapeSectors = generation.Map.Starts.Select(start =>
				EscapeSectorCount(withStarts, components, OpenRaRmgMapAdapter.ToNative(start, generation.Profile), 12)).ToArray();
			var minimumEscapeSectors = escapeSectors.DefaultIfEmpty(0).Min();
			var productionExitFailures = CountProductionExitFailures(map, withStarts, generation, startingUnits, colonies, out var productionExitDetails);
			var waspContractAccepted = ValidateWaspContract(locomotors, out var waspContractMessage);
			var chokeDistances = ChokepointDistances(generation, colonies);

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
				ValidatorName = generation.Profile.GeneratorVersion >= 2 ? "openra-static-ground-v2" : "openra-static-ground-v1",
				RouteScope = generation.Profile.GeneratorVersion >= 2 ?
					"each named edge constrained to its own RESERVED_ROUTE cells plus endpoint strategic regions" :
					"abstract graph node-to-node reachability; Version 1 route reservations do not constrain terrain",
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
				MinimumStartEscapeSectors = minimumEscapeSectors,
				TerrainSemanticMismatchCells = semanticMismatches,
				ProductionExitFailures = productionExitFailures,
				WaspSupportContractAccepted = waspContractAccepted,
				MinimumChokepointStartDistance = chokeDistances.Start,
				MinimumChokepointColonyDistance = chokeDistances.Colony,
				MinimumChokepointSeparation = chokeDistances.Separation,
				MinimumBlockedStartCoverageDistance = blockedStartDistance,
				MinimumBlockedNeutralColonyCoverageDistance = blockedNeutralColonyDistance,
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
			if (generation.Profile.GeneratorVersion == 1 && minimumRouteWidth < generation.Profile.MinimumRouteWidthNative)
				Hard("NATIVE_ROUTE_WIDTH", $"Minimum widest-path clearance is {minimumRouteWidth} native cells; configured minimum is {generation.Profile.MinimumRouteWidthNative}.");
			if (generation.Profile.GeneratorVersion >= 2 && routeWidthFailures > 0)
				Hard("NATIVE_ROUTE_WIDTH", $"{routeWidthFailures} named routes violate their configured normal, major, or exact chokepoint width.");
			if (generation.Profile.GeneratorVersion >= 2 && semanticMismatches > 0)
			{
				Hard("NATIVE_TERRAIN_SEMANTICS", $"{semanticMismatches} native cells disagree with the logical OPEN/BLOCKED map.");
				foreach (var detail in semanticMismatchSamples)
					failureDetails.Add(detail);
			}

			if (generation.Profile.GeneratorVersion >= 2 && minimumEscapeSectors < 6)
				Hard("NATIVE_START_EXIT_SECTORS", $"Minimum start exit coverage is {minimumEscapeSectors}/8 sectors at radius 12; required minimum is 6/8.");
			if (generation.Profile.GeneratorVersion >= 2 && productionExitFailures > 0)
			{
				Hard("NATIVE_PRODUCTION_EXITS", $"{productionExitFailures} starting or neutral colony production exits do not reach OPEN ground.");
				foreach (var detail in productionExitDetails)
					failureDetails.Add(detail);
			}

			if (generation.Profile.GeneratorVersion >= 2 && blockedStartDistance < 7)
				Hard("NATIVE_START_BLOCKER_DISTANCE", $"Minimum Water/start-colony coverage distance is {blockedStartDistance} native cells; required minimum is 7.");
			if (generation.Profile.GeneratorVersion >= 2 && blockedNeutralColonyDistance < 5)
				Hard("NATIVE_NEUTRAL_COLONY_BLOCKER_DISTANCE", $"Minimum Water/neutral-colony coverage distance is {blockedNeutralColonyDistance} native cells; required minimum is 5.");
			if (generation.Profile.GeneratorVersion >= 2 && !waspContractAccepted)
				Hard("WASP_SUPPORT_CONTRACT", waspContractMessage);
			if (generation.Profile.GeneratorVersion >= 2 && generation.Map.Chokepoints.Count > 0)
			{
				if (chokeDistances.Start < 24)
					Hard("NATIVE_CHOKEPOINT_START_DISTANCE", $"Minimum chokepoint/start-anchor distance is {chokeDistances.Start} native cells; required minimum is 24.");
				if (chokeDistances.Colony < 10)
					Hard("NATIVE_CHOKEPOINT_COLONY_DISTANCE", $"Minimum chokepoint/colony-coverage distance is {chokeDistances.Colony} native cells; required minimum is 10.");
				if (chokeDistances.Separation < 16)
					Hard("NATIVE_CHOKEPOINT_SEPARATION", $"Minimum chokepoint-segment separation is {chokeDistances.Separation} native cells; required minimum is 16.");
			}

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

		static Grid BuildNamedRouteGrid(Grid source, RmgGenerationResult generation, int routeId, RmgGraphNode from, RmgGraphNode to,
			bool includeEndpointRegions = true)
		{
			var route = source.Clone();
			var bit = 1UL << routeId;
			var fromCenter = OpenRaRmgMapAdapter.ToNative(from.Location, generation.Profile);
			var toCenter = OpenRaRmgMapAdapter.ToNative(to.Location, generation.Profile);
			var fromRadius = from.Role == "start" ? generation.Profile.StartRegionRadiusNative : 12;
			var toRadius = to.Role == "start" ? generation.Profile.StartRegionRadiusNative : 12;
			for (var i = 0; i < route.CellCount; i++)
			{
				var cell = route.Cell(i);
				var logicalX = (cell.X - route.Left) / 2;
				var logicalY = (cell.Y - route.Top) / 2;
				var logical = new RmgPoint(logicalX, logicalY);
				var inCorridor = generation.Map.Contains(logical) &&
					(generation.Map.RouteMasks[generation.Map.Index(logical)] & bit) != 0;
				var inFrom = includeEndpointRegions &&
					Math.Max(Math.Abs(cell.X - fromCenter.X), Math.Abs(cell.Y - fromCenter.Y)) <= fromRadius;
				var inTo = includeEndpointRegions &&
					Math.Max(Math.Abs(cell.X - toCenter.X), Math.Abs(cell.Y - toCenter.Y)) <= toRadius;
				if (!inCorridor && !inFrom && !inTo)
					route.Block(cell, $"outside-route:{routeId}");
			}

			return route;
		}

		static CPos[] NamedRouteRingCells(Grid grid, RmgGenerationResult generation, int routeId, CPos center, int radius)
		{
			var bit = 1UL << routeId;
			var result = new List<CPos>();
			for (var i = 0; i < grid.CellCount; i++)
			{
				var cell = grid.Cell(i);
				var distance = Math.Max(Math.Abs(cell.X - center.X), Math.Abs(cell.Y - center.Y));
				if (distance < radius || distance > radius + 3 || !grid.IsPassable(cell))
					continue;
				var logical = new RmgPoint((cell.X - grid.Left) / 2, (cell.Y - grid.Top) / 2);
				if (generation.Map.Contains(logical) &&
					(generation.Map.RouteMasks[generation.Map.Index(logical)] & bit) != 0)
					result.Add(cell);
			}

			return result.ToArray();
		}

		static CPos[] ChokepointCells(Grid grid, RmgGenerationResult generation, RmgChokepoint choke)
		{
			var chokeIndex = generation.Map.Chokepoints.IndexOf(choke);
			var result = new List<CPos>();
			for (var i = 0; i < grid.CellCount; i++)
			{
				var cell = grid.Cell(i);
				var logical = new RmgPoint((cell.X - grid.Left) / 2, (cell.Y - grid.Top) / 2);
				if (generation.Map.Contains(logical) &&
					generation.Map.ChokepointIds[generation.Map.Index(logical)] == chokeIndex &&
					grid.IsPassable(cell))
					result.Add(cell);
			}

			return result.ToArray();
		}

		static (CPos[] Before, CPos[] After) ChokepointShoulderCells(Grid grid, RmgGenerationResult generation,
			RmgChokepoint choke)
		{
			var horizontal = choke.From.Y == choke.To.Y;
			var minimum = horizontal ? Math.Min(choke.From.X, choke.To.X) : Math.Min(choke.From.Y, choke.To.Y);
			var maximum = horizontal ? Math.Max(choke.From.X, choke.To.X) : Math.Max(choke.From.Y, choke.To.Y);
			var bit = 1UL << choke.RouteId;
			var before = new List<CPos>();
			var after = new List<CPos>();
			for (var i = 0; i < grid.CellCount; i++)
			{
				var cell = grid.Cell(i);
				if (!grid.IsPassable(cell))
					continue;
				var logical = new RmgPoint((cell.X - grid.Left) / 2, (cell.Y - grid.Top) / 2);
				if (!generation.Map.Contains(logical) ||
					(generation.Map.RouteMasks[generation.Map.Index(logical)] & bit) == 0)
					continue;
				var coordinate = horizontal ? logical.X : logical.Y;
				if (coordinate == minimum - 1)
					before.Add(cell);
				else if (coordinate == maximum + 1)
					after.Add(cell);
			}

			return (before.ToArray(), after.ToArray());
		}

		static int CountTerrainSemanticMismatches(Grid grid, RmgGenerationResult generation, out JArray samples)
		{
			samples = new JArray();
			if (generation.Profile.GeneratorVersion < 2)
				return 0;

			var mismatches = 0;
			for (var logicalY = 0; logicalY < generation.Map.Height; logicalY++)
				for (var logicalX = 0; logicalX < generation.Map.Width; logicalX++)
				{
					var logical = new RmgPoint(logicalX, logicalY);
					var logicalIndex = generation.Map.Index(logical);
					for (var dy = 0; dy < 2; dy++)
						for (var dx = 0; dx < 2; dx++)
						{
							var cell = new CPos(grid.Left + 2 * logicalX + dx, grid.Top + 2 * logicalY + dy);
							var frame = 2 * dy + dx;
							var expected = generation.Profile.UsesShorelineMaterialization ?
								generation.Map.NativeTerrainIntents[4 * logicalIndex + frame].ToString() :
								generation.Map.Obstacles[logicalIndex] ? "Water" : "Clear";
							var actual = grid.Terrain[grid.Index(cell)];
							if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
								continue;
							mismatches++;
							if (samples.Count < MaximumDisagreementSamples)
								samples.Add(new JObject
								{
									["kind"] = "terrain-semantic-mismatch",
									["cell"] = Point(cell),
									["logical_cell"] = new JObject { ["x"] = logicalX, ["y"] = logicalY },
									["expected"] = expected,
									["actual"] = actual
								});
						}
				}

			return mismatches;
		}

		static int EscapeSectorCount(Grid grid, ComponentMap components, CPos center, int radius)
		{
			var centerLabel = components.Label(center);
			if (centerLabel < 0)
				return 0;

			var sectors = new bool[8];
			for (var dy = -radius; dy <= radius; dy++)
				for (var dx = -radius; dx <= radius; dx++)
				{
					var distance = Math.Sqrt(dx * dx + dy * dy);
					if (distance < radius - 1 || distance > radius + 0.5)
						continue;
					var cell = center + new CVec(dx, dy);
					if (!grid.Contains(cell) || components.Label(cell) != centerLabel)
						continue;
					var angle = Math.Atan2(dy, dx) + Math.PI;
					sectors[Math.Min(7, (int)(angle / (Math.PI / 4)))] = true;
				}

			return sectors.Count(x => x);
		}

		static int CountProductionExitFailures(Map map, Grid grid, RmgGenerationResult generation,
			IReadOnlyCollection<StartingUnitsInfo> startingUnits, IReadOnlyCollection<ActorFootprint> neutralColonies,
			out JArray details)
		{
			var localDetails = new JArray();
			var failures = 0;
			foreach (var start in generation.Map.Starts)
			{
				var nativeStart = OpenRaRmgMapAdapter.ToNative(start, generation.Profile);
				foreach (var startingUnit in startingUnits)
					Check(startingUnit.BaseActor, nativeStart + startingUnit.BaseActorOffset, "starting-colony");
			}

			foreach (var colony in neutralColonies)
				Check(colony.Type, colony.Location, "neutral-colony");
			details = localDetails;
			return failures;

			void Check(string actorType, CPos location, string role)
			{
				if (!map.Rules.Actors.TryGetValue(actorType, out var actorInfo))
				return;
				var exits = actorInfo.TraitInfos<ExitInfo>().ToArray();
				if (exits.Length == 0)
				return;
				foreach (var exit in exits)
				{
					var exitCell = location + exit.ExitCell;
					if (grid.IsPassable(exitCell))
						continue;
					failures++;
					if (localDetails.Count < MaximumDisagreementSamples)
						localDetails.Add(new JObject
						{
							["kind"] = "production-exit",
							["role"] = role,
							["actor"] = actorType,
							["anchor"] = Point(location),
							["exit_cell"] = Point(exitCell),
							["reason"] = grid.Contains(exitCell) ? grid.Reasons[grid.Index(exitCell)] ?? "blocked" : "outside-playable-bounds"
						});
				}
			}
		}

		static bool ValidateWaspContract(IEnumerable<LocomotorInfo> locomotors, out string message)
		{
			var wasps = locomotors.OfType<WaspLocomotorInfo>().ToArray();
			if (wasps.Length != 1)
			{
				message = $"Expected one WaspLocomotorInfo but found {wasps.Length}.";
				return false;
			}

			var wasp = wasps[0];
			var required = new[] { "Clear", "Rock", "Vegetation", "Water", "Air" };
			foreach (var terrain in required)
				if (!wasp.TerrainSpeeds.TryGetValue(terrain, out var speed) || speed.Speed != 100)
				{
					message = $"Wasp locomotor does not define {terrain} at speed 100.";
					return false;
				}

			if (!wasp.DisableDomainPassabilityCheck || wasp.TransitionCost != 0 || wasp.TransitionTerrainTypes.Count != 0)
			{
				message = "Wasp domain or transition semantics drifted from the frozen support-access contract.";
				return false;
			}

			message = "passed";
			return true;
		}

		static (int Start, int Colony, int Separation) ChokepointDistances(
			RmgGenerationResult generation, IReadOnlyCollection<ActorFootprint> colonies)
		{
			if (generation.Map.Chokepoints.Count == 0)
				return (0, 0, 0);

			var nativeSegments = new List<HashSet<CPos>>();
			for (var chokeIndex = 0; chokeIndex < generation.Map.Chokepoints.Count; chokeIndex++)
			{
				var cells = new HashSet<CPos>();
				for (var i = 0; i < generation.Map.ChokepointIds.Length; i++)
				{
					if (generation.Map.ChokepointIds[i] != chokeIndex)
						continue;
					var logicalX = i % generation.Map.Width;
					var logicalY = i / generation.Map.Width;
					for (var dy = 0; dy < 2; dy++)
						for (var dx = 0; dx < 2; dx++)
							cells.Add(new CPos(
								generation.Profile.CordonWidth + 2 * logicalX + dx,
								generation.Profile.CordonWidth + 2 * logicalY + dy));
				}

				nativeSegments.Add(cells);
			}

			var starts = generation.Map.Starts.Select(start => OpenRaRmgMapAdapter.ToNative(start, generation.Profile)).ToArray();
			var startDistance = nativeSegments.SelectMany(segment => segment.SelectMany(cell =>
				starts.Select(start => ChebyshevDistance(cell, start)))).DefaultIfEmpty(int.MaxValue).Min();
			var colonyDistance = nativeSegments.SelectMany(segment => segment.SelectMany(cell =>
				colonies.SelectMany(colony => colony.Coverage.Select(coverage => ChebyshevDistance(cell, coverage)))))
				.DefaultIfEmpty(int.MaxValue).Min();
			var separation = int.MaxValue;
			for (var first = 0; first < nativeSegments.Count; first++)
				for (var second = first + 1; second < nativeSegments.Count; second++)
					separation = Math.Min(separation, nativeSegments[first].SelectMany(a =>
						nativeSegments[second].Select(b => ChebyshevDistance(a, b))).DefaultIfEmpty(int.MaxValue).Min());
			return (startDistance, colonyDistance, separation);
		}

		static int ChebyshevDistance(CPos first, CPos second) =>
			Math.Max(Math.Abs(first.X - second.X), Math.Abs(first.Y - second.Y));

		static int MinimumCellSetDistance(IEnumerable<CPos> first, IEnumerable<CPos> second)
		{
			var firstCells = first.ToArray();
			var secondCells = second.ToArray();
			if (firstCells.Length == 0 || secondCells.Length == 0)
				return 0;
			return firstCells.Min(a => secondCells.Min(b => ChebyshevDistance(a, b)));
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
			if (generation.Profile.GeneratorVersion >= 2)
				for (var logicalY = 0; logicalY < generation.Map.Height; logicalY++)
					for (var logicalX = 0; logicalX < generation.Map.Width; logicalX++)
					{
						var logical = new RmgPoint(logicalX, logicalY);
						var logicalIndex = generation.Map.Index(logical);
						for (var frame = 0; frame < 4; frame++)
						{
							var water = generation.Profile.UsesShorelineMaterialization ?
								generation.Map.NativeTerrainIntents[4 * logicalIndex + frame] == RmgNativeTerrainIntent.Water :
								generation.Map.Obstacles[logicalIndex];
							if (water)
								passable[(2 * logicalY + frame / 2) * width + 2 * logicalX + frame % 2] = false;
						}
					}

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

		static JArray WidestPathBottleneckCells(Grid grid, int[] clearance, IEnumerable<CPos> sources,
			IEnumerable<CPos> targets, int width)
		{
			if (width <= 0)
				return new JArray();

			var minimumClearance = (width + 1) / 2;
			var targetIndexes = targets.Where(grid.Contains).Select(grid.Index).ToHashSet();
			var previous = Enumerable.Repeat(-2, grid.CellCount).ToArray();
			var capacity = new int[grid.CellCount];
			var queue = new PriorityQueue<int, int>();
			foreach (var source in sources.Where(grid.Contains).Distinct())
			{
				var index = grid.Index(source);
				if (!grid.Passable[index] || clearance[index] < minimumClearance || clearance[index] <= capacity[index])
					continue;
				capacity[index] = clearance[index];
				previous[index] = -1;
				queue.Enqueue(index, -capacity[index]);
			}

			var reached = -1;
			while (queue.Count > 0 && reached < 0)
			{
				queue.TryDequeue(out var currentIndex, out var priority);
				if (-priority != capacity[currentIndex])
					continue;
				if (targetIndexes.Contains(currentIndex))
				{
					reached = currentIndex;
					break;
				}

				foreach (var neighbor in grid.Neighbors(grid.Cell(currentIndex)))
				{
					var index = grid.Index(neighbor);
					if (!grid.Passable[index] || clearance[index] < minimumClearance)
						continue;
					var candidate = Math.Min(capacity[currentIndex], clearance[index]);
					if (candidate <= capacity[index])
						continue;
					capacity[index] = candidate;
					previous[index] = currentIndex;
					queue.Enqueue(index, -candidate);
				}
			}

			var bottlenecks = new List<CPos>();
			while (reached >= 0)
			{
				if (clearance[reached] == minimumClearance)
					bottlenecks.Add(grid.Cell(reached));
				reached = previous[reached];
			}

			return new JArray(bottlenecks.AsEnumerable().Reverse().Take(16).Select(cell =>
			{
				var reasons = new HashSet<string>(StringComparer.Ordinal);
				for (var dy = -minimumClearance; dy <= minimumClearance; dy++)
					for (var dx = -minimumClearance; dx <= minimumClearance; dx++)
					{
						if (Math.Abs(dx) + Math.Abs(dy) != minimumClearance)
							continue;
						var nearby = cell + new CVec(dx, dy);
						if (!grid.Contains(nearby))
							reasons.Add("outside-grid");
						else if (!grid.IsPassable(nearby))
							reasons.Add(grid.Reasons[grid.Index(nearby)] ?? "blocked");
					}

				return new JObject
				{
					["cell"] = Point(cell),
					["nearest_blockers"] = new JArray(reasons.OrderBy(reason => reason, StringComparer.Ordinal))
				};
			}));
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
