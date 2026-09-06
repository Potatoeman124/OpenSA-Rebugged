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
using System.Diagnostics;
using System.Linq;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public static partial class RmgGenerator
	{
		enum NaturalV10Morphology
		{
			LakeDistrict,
			CoastalShelf,
			RiverValley,
			InlandSea,
			Wetlands
		}

		sealed class NaturalV10Terrain
		{
			public string MorphologyId { get; init; }
			public int CandidateIndex { get; init; }
			public double PreliminaryScore { get; set; }
			public double[] WaterPriorities { get; init; }
			public double[] RockPriorities { get; init; }
			public double[] VegetationPriorities { get; init; }
		}

		static IReadOnlyList<string> RunNaturalV10VisualSelfTests(RmgProfile profile)
		{
			var failures = new List<string>();
			var found = new HashSet<string>();
			for (ulong seed = 0; seed < 100 && found.Count < 5; seed++)
			{
				var family = NaturalV10FamilyForSeed(seed);
				if (!found.Add(family))
					continue;
				var settings = new RmgGenerationSettings { Seed = seed };
				for (var retry = 0; retry < 12; retry++)
					if (BuildNaturalV10Terrain(profile, settings, retry).MorphologyId != family)
						failures.Add($"V10.1 retry {retry} changed family for seed {seed}.");
			}
			if (found.Count != 5)
				failures.Add("V10.1 seed mapping did not cover all five families.");

			var box = new bool[64 * 64];
			var oval = new bool[64 * 64];
			for (var y = 0; y < 64; y++)
				for (var x = 0; x < 64; x++)
				{
					box[y * 64 + x] = x >= 14 && x < 50 && y >= 20 && y < 44;
					oval[y * 64 + x] = (x - 32D) * (x - 32D) / 324D + (y - 32D) * (y - 32D) / 144D < 1D;
				}
			var boxMetrics = RmgNaturalVisualMetrics.Measure(box, 64, 64);
			var ovalMetrics = RmgNaturalVisualMetrics.Measure(oval, 64, 64);
			if (boxMetrics.Risk <= ovalMetrics.Risk || boxMetrics.LongestRunNative != 72D)
				failures.Add("V10.1 boundary metrics did not distinguish a rectangle from a curved shape.");
			if (RmgNaturalVisualMetrics.Measure(new bool[4096], 64, 64).Risk != 0D ||
				RmgNaturalVisualMetrics.Measure(Enumerable.Repeat(true, 4096).ToArray(), 64, 64).LongestRunNative != 0D)
				failures.Add("V10.1 boundary metrics counted empty terrain or the map frame as shoreline.");
			var largeExitMap = new RmgLogicalMap(128, 128);
			var exitAnchor = new RmgPoint(64, 64);
			if (!NaturalLargeStartHasOpenExitRing(largeExitMap, exitAnchor))
				failures.Add("Large start-exit preflight rejected open terrain.");
			Array.Fill(largeExitMap.NativeTerrainIntents, RmgNativeTerrainIntent.Water);
			var exitTerrain = (RmgNativeTerrainIntent[])largeExitMap.NativeTerrainIntents.Clone();
			if (NaturalLargeStartHasOpenExitRing(largeExitMap, exitAnchor) ||
				!exitTerrain.SequenceEqual(largeExitMap.NativeTerrainIntents))
				failures.Add("Large start-exit preflight accepted or modified a blocked exit ring.");
			failures.AddRange(profile.DirtPlacementRules.RunSelfTests(profile));
			var authorityMap = new RmgLogicalMap(8, 8);
			var authorityTerrain = (RmgNativeTerrainIntent[])authorityMap.NativeTerrainIntents.Clone();
			var authorityTemplates = (ushort[])authorityMap.TemplateIds.Clone();
			var authorityWater = (bool[])authorityMap.Obstacles.Clone();
			AssertNaturalV10SurfaceAuthority(authorityMap, authorityTerrain, authorityTemplates, authorityWater);
			foreach (var mutate in new Action[]
			{
				() => authorityMap.NativeTerrainIntents[0] = RmgNativeTerrainIntent.Rock,
				() => authorityMap.TemplateIds[0] = 1,
				() => authorityMap.Obstacles[0] = true
			})
			{
				mutate();
				try
				{
					AssertNaturalV10SurfaceAuthority(authorityMap, authorityTerrain, authorityTemplates, authorityWater);
					failures.Add("Surface authority guard accepted modified terrain.");
				}
				catch (RmgGenerationRejectedException)
				{
					// Expected: each independent terrain representation is immutable during placement.
				}
				Array.Copy(authorityTerrain, authorityMap.NativeTerrainIntents, authorityTerrain.Length);
				Array.Copy(authorityTemplates, authorityMap.TemplateIds, authorityTemplates.Length);
				Array.Copy(authorityWater, authorityMap.Obstacles, authorityWater.Length);
			}
			var placementRegression = Generate(profile, new RmgGenerationSettings
			{
				Seed = 975197840522651550UL,
				PlayerCount = 4,
				NeutralColonyCount = 16,
				Symmetry = RmgSymmetry.Rotate180,
				Archetype = RmgArchetype.CentralContest,
				GeneratorVersion = 10,
				TopologyPreset = RmgTopologyPreset.NaturalTerrainV10,
				LayoutFamily = RmgLayoutFamily.NaturalLandscape,
				OriginalSurfaceRelations = true
			});
			if (!placementRegression.Validation.Accepted || !placementRegression.Map.NaturalSurfacesFrozen)
				failures.Add("Dirt-placement regression could not search outside the provisional start territories.");
			return failures;
		}

		// Public for offline family coverage; generation and the UI use the same seed mapping.
		public static string NaturalV10FamilyForSeed(ulong seed) =>
			NaturalV10FamilyName((NaturalV10Morphology)(V10Mix(seed, 0x4D4F5250484F4C4FUL) % 5UL));

		static string NaturalV10FamilyName(NaturalV10Morphology morphology) => morphology switch
		{
			NaturalV10Morphology.LakeDistrict => "lake-district",
			NaturalV10Morphology.CoastalShelf => "coastal-shelf",
			NaturalV10Morphology.RiverValley => "river-valley",
			NaturalV10Morphology.InlandSea => "inland-sea",
			NaturalV10Morphology.Wetlands => "wetlands",
			_ => throw new ArgumentOutOfRangeException(nameof(morphology))
		};

		static RmgGenerationResult GenerateNaturalLandscapeV10(RmgProfile profile, RmgGenerationSettings settings)
		{
			var preparationTimer = Stopwatch.StartNew();
			var attemptTimings = new Dictionary<string, double>();
			const int CandidateCount = 12;
			var terrains = Enumerable.Range(0, CandidateCount)
				.Select(index => BuildNaturalV10Terrain(profile, settings, index)).ToArray();
			foreach (var terrain in terrains)
			{
				var projected = PrepareNaturalV10Map(profile, settings);
				ProjectNaturalV10Terrain(projected, profile, settings, terrain);
				NormalizeShorelineNeighborhoods(projected, null);
				terrain.PreliminaryScore = RmgNaturalVisualMetrics.Measure(projected.Obstacles,
					projected.Width, projected.Height).Risk;
				// A fragmented dry field is likely to need visible cuts when gameplay is embedded.
				terrain.PreliminaryScore += 4D * Math.Max(0, ConnectedComponents(projected, blocked: false).Count - 1);
			}

			preparationTimer.Stop();
			RmgGenerationResult best = null;
			RmgGenerationResult lastRejected = null;
			RmgGenerationRejectedException lastFailure = null;
			var bestScore = double.PositiveInfinity;
			var accepted = 0;
			var attempted = 0;
			foreach (var terrain in terrains.OrderBy(candidate => candidate.PreliminaryScore)
				.ThenBy(candidate => candidate.CandidateIndex))
			{
				attempted++;
				var attemptTimer = Stopwatch.StartNew();
				try
				{
					var candidate = GenerateNaturalLandscapeV10Candidate(profile, settings, terrain);
					if (!candidate.Validation.Accepted)
					{
						lastRejected = candidate;
						continue;
					}

					accepted++;
					var score = candidate.Validation.Metrics["natural_visual_risk"];
					if (score < bestScore)
					{
						best = candidate;
						bestScore = score;
					}

					// Compare several mechanically valid results, with a bounded full-generation cost.
					if (accepted == 3)
						break;
				}
				catch (RmgGenerationRejectedException e)
				{
					lastFailure = e;
				}
				finally
				{
					attemptTimings[$"natural_candidate_{terrain.CandidateIndex}_ms"] = attemptTimer.Elapsed.TotalMilliseconds;
				}
			}

			if (best != null)
			{
				best.Validation.Metrics["natural_preparation_ms"] = preparationTimer.Elapsed.TotalMilliseconds;
				best.Validation.Metrics["natural_all_attempts_ms"] = attemptTimings.Values.Sum();
				foreach (var (name, milliseconds) in attemptTimings)
					best.Validation.Metrics[name] = Math.Round(milliseconds, 3);
				best.Validation.Metrics["natural_candidates_ranked"] = CandidateCount;
				best.Validation.Metrics["natural_candidates_attempted"] = attempted;
				best.Validation.Metrics["natural_candidates_accepted"] = accepted;
				return best;
			}

			if (lastRejected != null)
				return lastRejected;

			throw new RmgGenerationRejectedException("NATURAL_V10_CANDIDATES",
				$"All {CandidateCount} deterministic {NaturalV10FamilyForSeed(settings.Seed)} candidates were rejected. Last failure: " +
				(lastFailure?.Message ?? "unknown"));
		}

		static RmgLogicalMap PrepareNaturalV10Map(RmgProfile profile, RmgGenerationSettings settings)
		{
			var map = new RmgLogicalMap(profile.LogicalWidth, profile.LogicalHeight)
			{
				NaturalOriginalSurfaceRelations = settings.OriginalSurfaceRelations
			};
			GenerateStartsAndTopology(map, profile, settings);
			foreach (var start in map.Starts)
				ReserveNaturalV10Disc(map.StartReservations, map, start, 8);
			MarkNaturalV10StrategicRegions(map);
			return map;
		}

		static RmgGenerationResult GenerateNaturalLandscapeV10Candidate(RmgProfile profile,
			RmgGenerationSettings settings, NaturalV10Terrain terrain)
		{
			var stageTimer = Stopwatch.StartNew();
			var stageMilliseconds = new Dictionary<string, double>();
			void Stage(string name)
			{
				if (settings.MapSize == 256)
					stageMilliseconds[name] = stageTimer.Elapsed.TotalMilliseconds;
				stageTimer.Restart();
			}
			var map = PrepareNaturalV10Map(profile, settings);
			map.RetryCount = terrain.CandidateIndex;

			var waterPriorities = ProjectNaturalV10Terrain(map, profile, settings, terrain);
			var projectedWater = (bool[])map.Obstacles.Clone();
			NormalizeNaturalWater(map, profile, settings, waterPriorities);
			Stage("initial_water");
			map.NaturalPreRouteWaterCount = map.Obstacles.Count(value => value);
			map.NaturalPreRouteInteriorWaterCount = Enumerable.Range(0, map.Obstacles.Length)
				.Count(index => map.Obstacles[index] &&
					WaterInteriorSector(map, new RmgPoint(index % map.Width, index / map.Width)) >= 0);

			// Embed adaptive colonies before routing so their physical footprints become
			// constraints for least-damage pathfinding. V10 never clears Water for them.
			PlaceNaturalColonies(map, profile, settings, initialRound: 0, maximumRoundExclusive: 1);
			MarkNaturalV10StrategicRegions(map);
			ReserveNaturalRoutes(map, profile, settings);
			MarkNaturalV10StrategicRegions(map);
			NormalizeNaturalWater(map, profile, settings, waterPriorities);

			Stage("initial_colonies_and_routes");

			// Optional colony rounds are fitted only after the safety network exists.
			// If a later balanced round cannot fit, the adaptive placer keeps the
			// already-valid lower colony count instead of altering terrain or routes.
			PlaceNaturalColonies(map, profile, settings, initialRound: 1);
			MarkNaturalV10StrategicRegions(map);
			NormalizeNaturalWater(map, profile, settings, waterPriorities);
			ApplyBlockingRepairs(map, profile);
			NormalizeShorelineNeighborhoods(map, waterPriorities);
			RebuildObstacleRegionMetadata(map);
			AssignRegions(map);
			Stage("remaining_colonies_and_water");
			if (settings.OriginalSurfaceRelations)
			{
				RmgShorelineMaterializer.Materialize(map, profile, settings);
				RmgLandCoverMaterializer.Materialize(map, profile, settings);
				Stage("surface_materialization");
				FitNaturalV10PlacementsToSurfaces(map, profile, settings);
				Stage("terrain_first_placement");
				// Cosmetic dirt details and actors cannot change surface relations.
				RmgClearLandDetailMaterializer.Materialize(map, profile, settings);
				Stage("clear_details");
				RmgTerrainDecorationGenerator.Materialize(map, profile, settings);
				Stage("terrain_decorations");
			}
			else
			{
				MaterializeBlockingTerrain(map, profile, settings);
				Stage("combined_materialization");
			}

			var validation = ValidateBlockingTopology(map, profile, settings);
			Stage("logical_validation");
			validation.Metrics["natural_surface_authority_enforced"] = map.NaturalSurfacesFrozen ? 1 : 0;
			if (map.NaturalSurfacesFrozen)
			{
				// The equality guard must pass before a candidate can report these zeroes.
				validation.Metrics["placement_changed_native_surface_cells"] = 0;
				validation.Metrics["placement_changed_terrain_templates"] = 0;
				validation.Metrics["placement_changed_water_cells"] = 0;
				validation.Metrics["relocated_player_starts"] = map.NaturalRelocatedStartCount;
				validation.Metrics["replaced_provisional_colonies"] = map.NaturalReplacedColonyCount;
			}
			var changed = Enumerable.Range(0, map.Obstacles.Length).Count(i => projectedWater[i] != map.Obstacles[i]);
			var alterationPercent = 100D * changed / Math.Max(1, map.NaturalPrototypeWaterCount);
			var nativeWidth = map.Width * 2;
			var water = new bool[map.NativeTerrainIntents.Length];
			var gravel = new bool[water.Length];
			var moss = new bool[water.Length];
			for (var y = 0; y < map.Height * 2; y++)
				for (var x = 0; x < nativeWidth; x++)
				{
					var intent = map.NativeTerrainIntents[4 * ((y / 2) * map.Width + x / 2) + (y % 2) * 2 + x % 2];
					water[y * nativeWidth + x] = intent == RmgNativeTerrainIntent.Water;
					gravel[y * nativeWidth + x] = intent is RmgNativeTerrainIntent.Rock or RmgNativeTerrainIntent.Vegetation;
					moss[y * nativeWidth + x] = intent == RmgNativeTerrainIntent.Vegetation;
				}

			var waterShape = RmgNaturalVisualMetrics.Measure(water, nativeWidth, map.Height * 2);
			var rockShape = RmgNaturalVisualMetrics.Measure(gravel, nativeWidth, map.Height * 2);
			var mossShape = RmgNaturalVisualMetrics.Measure(moss, nativeWidth, map.Height * 2);
			waterShape.Report(validation.Metrics, "water");
			rockShape.Report(validation.Metrics, "gravel");
			mossShape.Report(validation.Metrics, "moss");
			validation.Metrics["natural_preliminary_visual_risk"] = terrain.PreliminaryScore;
			validation.Metrics["natural_projection_changed_percent"] = alterationPercent;
			validation.Metrics["natural_visual_risk"] = waterShape.Risk + .25D * rockShape.Risk +
				.15D * mossShape.Risk + .5D * alterationPercent;
			var result = new RmgGenerationResult
			{
				Settings = settings,
				Profile = profile,
				Map = map,
				Validation = validation,
				LogicalHash = HashBlockingLogicalMap(map, profile),
				ActorHash = HashActors(map),
				GraphHash = HashBlockingGraph(map)
			};
			Stage("visual_metrics_and_hashes");
			foreach (var (name, milliseconds) in stageMilliseconds)
				validation.Metrics[$"large_candidate_{name}_ms"] = Math.Round(milliseconds, 3);
			return result;
		}

		static void MarkNaturalV10StrategicRegions(RmgLogicalMap map)
		{
			foreach (var hub in map.GraphNodes.Where(node => node.Role == "hub"))
				ReserveNaturalV10Disc(map.StrategicRegions, map, hub.Location, 2);
			for (var index = 0; index < map.RouteMasks.Length; index++)
				if (BitCount(map.RouteMasks[index]) > 1)
					map.StrategicRegions[index] = true;
		}

		static void ReserveNaturalV10Disc(bool[] layer, RmgLogicalMap map, RmgPoint center, int radius)
		{
			for (var dy = -radius; dy <= radius; dy++)
				for (var dx = -radius; dx <= radius; dx++)
				{
					if (dx * dx + dy * dy > radius * radius)
						continue;

					var point = new RmgPoint(center.X + dx, center.Y + dy);
					if (map.Contains(point))
						layer[map.Index(point)] = true;
				}
		}

		static NaturalV10Terrain BuildNaturalV10Terrain(RmgProfile profile, RmgGenerationSettings settings, int candidateIndex)
		{
			var width = profile.LogicalWidth;
			var height = profile.LogicalHeight;
			var count = width * height;
			var large = profile.PlayableWidth == 256;
			var terrainSeed = candidateIndex == 0 ? settings.Seed :
				V10Mix(settings.Seed, 0x43414E4449444154UL + (ulong)candidateIndex);
			var morphology = (NaturalV10Morphology)(V10Mix(settings.Seed, 0x4D4F5250484F4C4FUL) % 5UL);
			var shapeRandom = new DeterministicRandom(V10Mix(terrainSeed, 0x534841504553UL));
			var warpXSeed = V10Mix(terrainSeed, 0x5741525058UL);
			var warpYSeed = V10Mix(terrainSeed, 0x5741525059UL);
			var broadSeed = V10Mix(terrainSeed, 0x42524F4144UL);
			var detailSeed = V10Mix(terrainSeed, 0x44455441494CUL);
			var microSeed = V10Mix(terrainSeed, 0x4D4943524FUL);
			var geologySeed = V10Mix(terrainSeed, 0x47454F4C4F4759UL);
			var moistureSeed = V10Mix(terrainSeed, 0x4D4F495354555245UL);
			var water = new double[count];

			var basinCount = morphology switch
			{
				NaturalV10Morphology.LakeDistrict => 2 + shapeRandom.NextInt(3),
				NaturalV10Morphology.Wetlands => 3 + shapeRandom.NextInt(3),
				_ => 1
			};
			if (large && morphology is NaturalV10Morphology.LakeDistrict or NaturalV10Morphology.Wetlands)
				basinCount *= 2;
			var basins = new (double X, double Y, double RadiusX, double RadiusY, double Angle, double Weight)[basinCount];
			for (var i = 0; i < basins.Length; i++)
			{
				var major = morphology == NaturalV10Morphology.InlandSea ?
					23D + 8D * V10Unit(shapeRandom) :
					11D + 11D * V10Unit(shapeRandom);
				if (large)
					major *= morphology == NaturalV10Morphology.InlandSea ? 1.75D : 1.35D;
				var aspect = 1.05D + 1.15D * V10Unit(shapeRandom);
				basins[i] = (
					-4D + (width + 8D) * V10Unit(shapeRandom),
					-4D + (height + 8D) * V10Unit(shapeRandom),
					major,
					major / aspect,
					Math.PI * V10Unit(shapeRandom),
					.82D + .34D * V10Unit(shapeRandom));
			}

			var geologyRandom = new DeterministicRandom(V10Mix(terrainSeed, 0x47454F424153494EUL));
			var geologyBasins = new (double X, double Y, double RadiusX, double RadiusY, double Angle, double Weight)[
				(2 + geologyRandom.NextInt(3)) * (large ? 2 : 1)];
			for (var i = 0; i < geologyBasins.Length; i++)
			{
				var major = (14D + 10D * V10Unit(geologyRandom)) * (large ? 1.25D : 1D);
				var aspect = 1.15D + 1.1D * V10Unit(geologyRandom);
				geologyBasins[i] = (
					-8D + (width + 16D) * V10Unit(geologyRandom),
					-8D + (height + 16D) * V10Unit(geologyRandom),
					major,
					major / aspect,
					Math.PI * V10Unit(geologyRandom),
					.90D + .25D * V10Unit(geologyRandom));
			}

			var coastAngle = 2D * Math.PI * V10Unit(shapeRandom);
			var coastPhase = 2D * Math.PI * V10Unit(shapeRandom);
			var coastBend = (5D + 7D * V10Unit(shapeRandom)) * (large ? 1.5D : 1D);
			var riverHorizontal = shapeRandom.NextInt(2) == 0;
			var riverBase = .38D + .24D * V10Unit(shapeRandom);
			var riverAmplitude = (8D + 6D * V10Unit(shapeRandom)) * (large ? 1.5D : 1D);
			var riverPhase = 2D * Math.PI * V10Unit(shapeRandom);
			var riverPeriod = (28D + 22D * V10Unit(shapeRandom)) * (large ? 1.35D : 1D);

			for (var y = 0; y < height; y++)
				for (var x = 0; x < width; x++)
				{
					var index = y * width + x;
					var warpX = 6D * V10FractalNoise(warpXSeed, x, y, 15D, 3, .52D);
					var warpY = 6D * V10FractalNoise(warpYSeed, x, y, 15D, 3, .52D);
					var px = x + warpX;
					var py = y + warpY;
					var broad = V10FractalNoise(broadSeed, px, py, 15D, 4, .53D);
					var detail = V10FractalNoise(detailSeed, px, py, 5.5D, 3, .50D);
					var micro = V10FractalNoise(microSeed, px, py, 2.8D, 2, .48D);
					var basin = basins.Max(entry => V10BasinScore(entry, px, py));

					water[index] = morphology switch
					{
						NaturalV10Morphology.LakeDistrict =>
							basin + .48D * broad + .18D * detail + .05D * micro,
						NaturalV10Morphology.CoastalShelf =>
							V10CoastDepth(coastAngle, coastPhase, coastBend, px, py, width, height) +
								3.5D * broad + 1.8D * detail + .6D * micro,
						NaturalV10Morphology.RiverValley =>
							V10RiverScore(riverHorizontal, px, py, width, height, riverBase,
								riverAmplitude, riverPhase, riverPeriod, broad, detail) + .7D * micro,
						NaturalV10Morphology.InlandSea =>
							basin + .52D * broad + .18D * detail + .05D * micro,
						NaturalV10Morphology.Wetlands =>
							.72D * broad + .48D * basin + .24D * detail + .08D * micro,
						_ => throw new ArgumentOutOfRangeException(nameof(morphology), morphology, "Unsupported V10 natural morphology.")
					};
				}

			var normalizedWater = V10Normalize(water);
			var latticeWidth = width + 1;
			var latticeHeight = height + 1;
			var rock = new double[latticeWidth * latticeHeight];
			var vegetation = new double[rock.Length];
			for (var y = 0; y < latticeHeight; y++)
				for (var x = 0; x < latticeWidth; x++)
				{
					var index = y * latticeWidth + x;
					var localWater = normalizedWater[Math.Min(height - 1, y) * width + Math.Min(width - 1, x)];
					var gx = x + 7D * V10FractalNoise(V10Mix(geologySeed, 3UL), x, y, 18D, 3, .5D);
					var gy = y + 7D * V10FractalNoise(V10Mix(geologySeed, 4UL), x, y, 18D, 3, .5D);
					var geologicalRegion = geologyBasins.Max(entry => V10BasinScore(entry, gx, gy));
					var geology = .92D * geologicalRegion +
						.48D * V10FractalNoise(geologySeed, x, y, 15D, 4, .55D) +
						.18D * V10FractalNoise(V10Mix(geologySeed, 2UL), x, y, 5.5D, 2, .52D);
					var moisture = .58D * V10FractalNoise(moistureSeed, x, y, 14D, 4, .55D) +
						.24D * V10FractalNoise(V10Mix(moistureSeed, 2UL), x, y, 5D, 2, .52D) +
						.30D * localWater;

					// Moss is selected from the core of the same broad geological field
					// that selects Gravel. The materializer still enforces legal NORMAL
					// transitions, but the intended Gravel is a landform rather than a
					// one-cell compliance halo around unrelated Moss specks.
					rock[index] = geology;
					vegetation[index] = geology + .32D * moisture - .12D * V10FractalNoise(
						V10Mix(geologySeed, 5UL), gx, gy, 11D, 3, .5D);
				}

			return new NaturalV10Terrain
			{
				CandidateIndex = candidateIndex,
				MorphologyId = NaturalV10FamilyName(morphology),
				WaterPriorities = water,
				RockPriorities = rock,
				VegetationPriorities = vegetation
			};
		}

		static int[] ProjectNaturalV10Terrain(RmgLogicalMap map, RmgProfile profile,
			RmgGenerationSettings settings, NaturalV10Terrain terrain)
		{
			if (terrain.WaterPriorities.Length != map.Obstacles.Length ||
				terrain.RockPriorities.Length != map.NaturalRockPriorities.Length ||
				terrain.VegetationPriorities.Length != map.NaturalVegetationPriorities.Length)
				throw new InvalidOperationException("Natural V10 terrain fields do not match the logical map geometry.");

			var desiredPercent = profile.ObstacleDensityTarget(settings.Archetype, settings.WaterAmount);
			var target = (int)Math.Round(map.Obstacles.Length * desiredPercent / 100D);
			var eligible = Enumerable.Range(0, map.Obstacles.Length)
				.Where(index => !NaturalProtected(map, index))
				.ToArray();
			if (eligible.Length < target)
				throw new RmgGenerationRejectedException("NATURAL_V10_WATER_CAPACITY",
					$"Only {eligible.Length} terrain-field cells are eligible for {target} requested Water cells.");

			// A continuous broad bias brings a coast into play without clipping it against
			// the rectangular measurement window. The same priorities survive all repairs.
			var normalized = V10Normalize(terrain.WaterPriorities);
			var priorities = new double[normalized.Length];
			HashSet<int> prototypeWater = null;
			for (var pass = 0; pass <= 12; pass++)
			{
				for (var i = 0; i < priorities.Length; i++)
				{
					var x = (i % map.Width - (map.Width - 1) * .5D) / (map.Width * .38D);
					var y = (i / map.Width - (map.Height - 1) * .5D) / (map.Height * .38D);
					priorities[i] = normalized[i] + pass * .06D * Math.Exp(-(x * x + y * y));
				}
				prototypeWater = eligible.OrderByDescending(i => priorities[i]).ThenBy(i => i).Take(target).ToHashSet();
				var inside = prototypeWater.Count(i =>
					WaterInteriorSector(map, new RmgPoint(i % map.Width, i / map.Width)) >= 0);
				if (inside >= target * .55D)
					break;
			}

			var waterPriorities = new int[map.Obstacles.Length];
			for (var index = 0; index < map.Obstacles.Length; index++)
			{
				var point = new RmgPoint(index % map.Width, index / map.Width);
				waterPriorities[index] = V10Priority(priorities[index]);
				if (prototypeWater.Contains(index))
				{
					map.NaturalPrototypeWaterCount++;
					if (WaterInteriorSector(map, point) >= 0)
						map.NaturalPrototypeInteriorWaterCount++;
				}

				map.Obstacles[index] = prototypeWater.Contains(index) && !NaturalProtected(map, index);
			}

			for (var index = 0; index < map.NaturalRockPriorities.Length; index++)
			{
				map.NaturalRockPriorities[index] = V10Priority(terrain.RockPriorities[index]);
				map.NaturalVegetationPriorities[index] = V10Priority(terrain.VegetationPriorities[index]);
			}

			map.NaturalTerrainVariantId = terrain.CandidateIndex == 0 ? $"v10-{terrain.MorphologyId}" :
				$"v10-{terrain.MorphologyId}-candidate-{terrain.CandidateIndex + 1}";
			map.NaturalForbiddenSurfaceAdjacencyCount = 0;
			map.NaturalProjectedWaterCount = map.Obstacles.Count(value => value);
			map.NaturalProjectedInteriorWaterCount = Enumerable.Range(0, map.Obstacles.Length)
				.Count(index => map.Obstacles[index] &&
					WaterInteriorSector(map, new RmgPoint(index % map.Width, index / map.Width)) >= 0);
			return waterPriorities;
		}

		static int V10Priority(double value) =>
			(int)Math.Round(Math.Clamp(value, -10D, 10D) * 100000D);

		static double V10BasinScore(
			(double X, double Y, double RadiusX, double RadiusY, double Angle, double Weight) basin,
			double x, double y)
		{
			var dx = x - basin.X;
			var dy = y - basin.Y;
			var cos = Math.Cos(basin.Angle);
			var sin = Math.Sin(basin.Angle);
			var u = (cos * dx + sin * dy) / basin.RadiusX;
			var v = (-sin * dx + cos * dy) / basin.RadiusY;
			return basin.Weight * (1D - Math.Sqrt(u * u + v * v));
		}

		static double V10CoastDepth(double angle, double phase, double bend,
			double x, double y, int width, int height)
		{
			var dx = x - width * .5D;
			var dy = y - height * .5D;
			var along = Math.Cos(angle) * dx + Math.Sin(angle) * dy;
			var across = -Math.Sin(angle) * dx + Math.Cos(angle) * dy;
			var frequency = 2D * Math.PI / (width * 1.35D);
			var curve = bend * Math.Sin(along * frequency + phase) +
				.35D * bend * Math.Sin(along * frequency * 2.3D + phase * .7D);
			var slope = bend * frequency * Math.Cos(along * frequency + phase) +
				.805D * bend * frequency * Math.Cos(along * frequency * 2.3D + phase * .7D);
			return (across - width * .18D - curve) / Math.Sqrt(1D + slope * slope);
		}

		static double V10RiverScore(bool horizontal, double x, double y, int width, int height,
			double riverBase, double amplitude, double phase, double period, double broad, double detail)
		{
			var along = horizontal ? x : y;
			var across = horizontal ? y : x;
			var acrossSize = horizontal ? height : width;
			var center = acrossSize * riverBase +
				amplitude * Math.Sin(2D * Math.PI * along / period + phase) +
				3.2D * broad;
			var secondaryMeander = 2.4D * Math.Sin(2D * Math.PI * along / (period * .47D) + phase * .61D);
			var widthModulation = 2.5D + .9D * (detail + 1D);
			return widthModulation - Math.Abs(across - center - secondaryMeander) + .65D * broad;
		}

		static double[] V10Normalize(double[] source)
		{
			var minimum = source.Min();
			var maximum = source.Max();
			var range = maximum - minimum;
			return source.Select(value => range > 0D ? (value - minimum) / range : 0D).ToArray();
		}

		static double V10FractalNoise(ulong seed, double x, double y,
			double scale, int octaves, double persistence)
		{
			var total = 0D;
			var amplitude = 1D;
			var normalizer = 0D;
			for (var octave = 0; octave < octaves; octave++)
			{
				total += amplitude * V10ValueNoise(V10Mix(seed, (ulong)octave + 1UL), x / scale, y / scale);
				normalizer += amplitude;
				amplitude *= persistence;
				scale *= .5D;
			}

			return total / normalizer;
		}

		static double V10ValueNoise(ulong seed, double x, double y)
		{
			var x0 = (int)Math.Floor(x);
			var y0 = (int)Math.Floor(y);
			var tx = V10Fade(x - x0);
			var ty = V10Fade(y - y0);
			var a = V10Sample(seed, x0, y0);
			var b = V10Sample(seed, x0 + 1, y0);
			var c = V10Sample(seed, x0, y0 + 1);
			var d = V10Sample(seed, x0 + 1, y0 + 1);
			return V10Lerp(V10Lerp(a, b, tx), V10Lerp(c, d, tx), ty);
		}

		static double V10Sample(ulong seed, int x, int y)
		{
			var value = V10Mix(seed,
				unchecked((ulong)(long)x) * 0x9E3779B97F4A7C15UL ^
				unchecked((ulong)(long)y) * 0xC2B2AE3D27D4EB4FUL);
			return 2D * ((value >> 11) * (1D / (1UL << 53))) - 1D;
		}

		static ulong V10Mix(ulong seed, ulong value)
		{
			var z = seed + value + 0x9E3779B97F4A7C15UL;
			z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
			z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
			return z ^ (z >> 31);
		}

		static double V10Unit(DeterministicRandom random) =>
			(random.NextUInt64() >> 11) * (1D / (1UL << 53));

		static double V10Fade(double value) =>
			value * value * value * (value * (value * 6D - 15D) + 10D);

		static double V10Lerp(double a, double b, double value) =>
			a + (b - a) * value;
	}
}
