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
		const int CoherentWaterMarginLogical = 4;
		const int CoherentWaterMaximumOrbitCount = 4;
		const int CoherentWaterFieldAttempts = 6;

		static int SeedCoherentWaterRegions(RmgLogicalMap map, RmgProfile profile, RmgGenerationSettings settings,
			int target, int orbit)
		{
			var initialWater = map.Obstacles.Count(value => value);
			orbit = SeedDominantSelfSymmetricLake(map, profile, settings, target, orbit);
			var dominantLakePlaced = map.Obstacles.Count(value => value) - initialWater >=
				(int)Math.Ceiling((target - initialWater) * 0.50D);
			for (var coherentOrbit = 0;
				coherentOrbit < CoherentWaterMaximumOrbitCount && map.Obstacles.Count(value => value) < target;
				coherentOrbit++)
			{
				var remaining = target - map.Obstacles.Count(value => value);
				if (remaining < 2 * profile.ObstacleRegionMinimumLogical)
					break;

				// The first pair deliberately owns most of the Water budget. Exact symmetry limits
				// one connected body's share to roughly one half, so 84% for the first pair gives
				// each dominant lake about 42% of the final Water while leaving a bounded completion pair.
				var pairShare = coherentOrbit == 0 && !dominantLakePlaced ? 0.84D : 1D;
				var desired = Math.Clamp((int)Math.Ceiling(remaining * pairShare / 2D),
					profile.ObstacleRegionMinimumLogical, profile.ObstacleRegionMaximumLogical);
				var minimumAccepted = coherentOrbit == 0 && !dominantLakePlaced ?
					Math.Max(profile.ObstacleRegionMinimumLogical,
						(int)Math.Ceiling((target - initialWater) * 0.20D)) :
					profile.ObstacleRegionMinimumLogical;
				var placed = false;
				for (var fieldAttempt = 0; fieldAttempt < CoherentWaterFieldAttempts && !placed; fieldAttempt++)
				{
					var field = BuildCoherentWaterField(map, profile, settings, coherentOrbit, fieldAttempt);
					var tie = DeterministicRandom.ForStream(settings, profile,
						$"natural-water-seeds-{coherentOrbit}-{fieldAttempt}");
					var seeds = Enumerable.Range(WaterInteriorMarginLogical,
							map.Height - 2 * WaterInteriorMarginLogical - 1)
						.SelectMany(y => Enumerable.Range(WaterInteriorMarginLogical,
							map.Width - 2 * WaterInteriorMarginLogical - 1)
							.Select(x => new RmgPoint(x, y)))
						.Where(point =>
						{
							var partner = Transform(point, settings.Symmetry, map.Width, map.Height);
							return IsCanonical(point, partner, map.Width) &&
								CoherentWaterCellEligible(map, point) &&
								CoherentWaterCellEligible(map, partner);
						})
						.Select(point => (
							Point: point,
							Score: field[map.Index(point)],
							Center: Math.Min(
								Math.Min(point.X, map.Width - 1 - point.X),
								Math.Min(point.Y, map.Height - 1 - point.Y)),
							Tie: tie.NextUInt64()))
						.OrderByDescending(candidate => candidate.Score)
						.ThenByDescending(candidate => candidate.Center)
						.ThenBy(candidate => candidate.Tie)
						.Take(32)
						.ToArray();

					foreach (var (point, _, _, _) in seeds)
					{
						var region = GrowCoherentWaterRegion(map, settings, field, point, desired);
						if (region.Count < minimumAccepted)
							continue;

						try
						{
							orbit = AddSymmetricRegion(map, settings, region, orbit);
							placed = true;
							break;
						}
						catch (InvalidOperationException)
						{
							// Stable candidate order continues. AddSymmetricRegion rolls back rejected
							// candidates, so no failed candidate can perturb later deterministic attempts.
						}
					}
				}

				if (!placed)
				{
					// A route, colony reservation, or earlier lake can partition one coherent
					// field. Consume the next independent field within the fixed orbit budget.
					continue;
				}
			}

			var achieved = map.Obstacles.Count(value => value);
			var absoluteMinimum = (int)Math.Ceiling(map.Obstacles.Length * 0.06D);
			if (achieved < absoluteMinimum)
				throw new RmgGenerationRejectedException("NATURAL_WATER_PLACEMENT",
					$"Structured Competitive Water reached {achieved} logical cells; the absolute safety floor is " +
					$"{absoluteMinimum} after {CoherentWaterMaximumOrbitCount} bounded lake orbits.");

			return orbit;
		}

		static int SeedDominantSelfSymmetricLake(RmgLogicalMap map, RmgProfile profile,
			RmgGenerationSettings settings, int target, int orbit)
		{
			var existing = map.Obstacles.Count(value => value);
			var desired = Math.Min(profile.ObstacleRegionMaximumLogical,
				(int)Math.Ceiling((target - existing) * 0.72D));
			var minimumAccepted = Math.Max(profile.ObstacleRegionMinimumLogical,
				(int)Math.Ceiling((target - existing) * 0.50D));
			if (desired < profile.ObstacleRegionMinimumLogical)
				return orbit;

			for (var attempt = 0; attempt < CoherentWaterFieldAttempts; attempt++)
			{
				var field = BuildCoherentWaterField(map, profile, settings, -1, attempt);
				var tie = DeterministicRandom.ForStream(settings, profile,
					$"natural-water-central-seeds-{attempt}");
				var seeds = CoherentWaterAxisSeeds(map, settings)
					.Where(point => CoherentWaterBlock(point).All(candidate =>
						CoherentWaterCellEligible(map, candidate)))
					.Select(point => (Point: point,
						Score: CoherentWaterBlock(point).Average(candidate => field[map.Index(candidate)]),
						Tie: tie.NextUInt64()))
					.OrderByDescending(candidate => candidate.Score)
					.ThenBy(candidate => candidate.Tie)
					.Take(32);
				foreach (var (point, _, _) in seeds)
				{
					var region = GrowSelfSymmetricCoherentWaterRegion(map, settings, field, point, desired);
					if (region.Count < minimumAccepted)
						continue;

					try
					{
						return AddSelfSymmetricCoherentWaterRegion(map, settings, region, orbit);
					}
					catch (InvalidOperationException)
					{
						// Continue the bounded deterministic axis scan.
					}
				}
			}

			return orbit;
		}

		static IEnumerable<RmgPoint> CoherentWaterAxisSeeds(RmgLogicalMap map, RmgGenerationSettings settings)
		{
			var centerX = map.Width / 2 - 1;
			var centerY = map.Height / 2 - 1;
			return settings.Symmetry switch
			{
				RmgSymmetry.MirrorHorizontal => Enumerable.Range(WaterInteriorMarginLogical,
					map.Width - 2 * WaterInteriorMarginLogical - 1)
					.SelectMany(x => new[] { new RmgPoint(x, centerY - 1), new RmgPoint(x, centerY) }),
				RmgSymmetry.MirrorVertical => Enumerable.Range(WaterInteriorMarginLogical,
					map.Height - 2 * WaterInteriorMarginLogical - 1)
					.SelectMany(y => new[] { new RmgPoint(centerX - 1, y), new RmgPoint(centerX, y) }),
				_ => new[]
				{
					new RmgPoint(centerX - 1, centerY - 1),
					new RmgPoint(centerX, centerY - 1),
					new RmgPoint(centerX - 1, centerY),
					new RmgPoint(centerX, centerY)
				}
			};
		}

		static HashSet<RmgPoint> GrowSelfSymmetricCoherentWaterRegion(RmgLogicalMap map,
			RmgGenerationSettings settings, double[] field, RmgPoint seed, int desired)
		{
			var region = new HashSet<RmgPoint>();
			var frontier = new HashSet<RmgPoint> { seed };
			while (frontier.Count > 0 && region.Count < desired)
			{
				var candidates = frontier.Select(topLeft =>
					{
						var block = CoherentWaterBlock(topLeft);
						var orbitBlock = block.Concat(block.Select(point =>
							Transform(point, settings.Symmetry, map.Width, map.Height))).Distinct().ToArray();
						var added = orbitBlock.Where(point => !region.Contains(point)).ToArray();
						var valid = added.Length > 0 && region.Count + added.Length <= desired &&
							CoherentWaterShapeSupported(region, orbitBlock) &&
							added.All(point => CoherentWaterCellEligible(map, point));
						var score = valid ? orbitBlock.Average(point => field[map.Index(point)]) : double.MinValue;
						var sharedEdges = valid ? orbitBlock.Sum(point => FourNeighbors(point).Count(region.Contains)) : 0;
						return (TopLeft: topLeft, Added: added, Valid: valid, Score: score + 0.06D * sharedEdges);
					})
					.Where(candidate => candidate.Valid)
					.OrderByDescending(candidate => candidate.Score)
					.ThenBy(candidate => candidate.TopLeft.Y)
					.ThenBy(candidate => candidate.TopLeft.X)
					.ToArray();
				if (candidates.Length == 0)
					break;

				var (topLeft, added, _, _) = candidates[0];
				frontier.Remove(topLeft);
				foreach (var point in added)
					region.Add(point);
				QueueAround(topLeft);
				var (minimumX, minimumY) = added
					.Select(point => Transform(point, settings.Symmetry, map.Width, map.Height))
					.Aggregate((MinimumX: map.Width, MinimumY: map.Height), (minimum, point) =>
						(Math.Min(minimum.MinimumX, point.X), Math.Min(minimum.MinimumY, point.Y)));
				QueueAround(new RmgPoint(minimumX, minimumY));
			}

			return region;

			void QueueAround(RmgPoint topLeft)
			{
				frontier.Add(new RmgPoint(topLeft.X - 1, topLeft.Y));
				frontier.Add(new RmgPoint(topLeft.X + 1, topLeft.Y));
				frontier.Add(new RmgPoint(topLeft.X, topLeft.Y - 1));
				frontier.Add(new RmgPoint(topLeft.X, topLeft.Y + 1));
			}
		}

		static int AddSelfSymmetricCoherentWaterRegion(RmgLogicalMap map, RmgGenerationSettings settings,
			HashSet<RmgPoint> region, int orbit)
		{
			var transformed = region.Select(point =>
				Transform(point, settings.Symmetry, map.Width, map.Height)).ToHashSet();
			if (!region.SetEquals(transformed) || !Connected4(region) ||
				!ShorelineShapeIsSupported(region) || region.Any(point => !ObstacleCellEligible(map, point)))
				throw new InvalidOperationException("Natural lake is not a connected renderable self-symmetric region.");

			var regionId = map.ObstacleRegions.Count;
			foreach (var point in region)
			{
				var index = map.Index(point);
				map.Obstacles[index] = true;
				map.ObstacleRegionIds[index] = regionId;
			}

			map.ObstacleRegions.Add(new RmgObstacleRegion(regionId, orbit, region.Count));
			if (ConnectedComponents(map, blocked: false).Count != 1)
			{
				foreach (var point in region)
				{
					var index = map.Index(point);
					map.Obstacles[index] = false;
					map.ObstacleRegionIds[index] = -1;
				}

				map.ObstacleRegions.RemoveAt(map.ObstacleRegions.Count - 1);
				throw new InvalidOperationException("Natural lake would create a terrain-only OPEN island.");
			}

			return orbit + 1;
		}

		static HashSet<RmgPoint> GrowCoherentWaterRegion(RmgLogicalMap map, RmgGenerationSettings settings,
			double[] field, RmgPoint seed, int desired)
		{
			var region = new HashSet<RmgPoint>();
			var frontier = new HashSet<RmgPoint> { seed };
			while (frontier.Count > 0 && region.Count < desired)
			{
				var candidates = frontier.Select(topLeft =>
					{
						var block = CoherentWaterBlock(topLeft);
						var added = block.Where(point => !region.Contains(point)).ToArray();
						var overlapsPartner = block.Any(point =>
						{
							var partner = Transform(point, settings.Symmetry, map.Width, map.Height);
							return region.Contains(partner) || block.Contains(partner);
						});
						var valid = added.Length > 0 && region.Count + added.Length <= desired &&
							!overlapsPartner && CoherentWaterShapeSupported(region, block) &&
							added.All(point =>
								CoherentWaterCellEligible(map, point) &&
								CoherentWaterCellEligible(map,
									Transform(point, settings.Symmetry, map.Width, map.Height)));
						var fieldScore = valid ? block.Average(point => field[map.Index(point)]) : double.MinValue;
						var sharedEdges = valid ? block.Sum(point => FourNeighbors(point).Count(region.Contains)) : 0;
						var distance = Math.Abs(topLeft.X - seed.X) + Math.Abs(topLeft.Y - seed.Y);
						return (TopLeft: topLeft, Added: added, Valid: valid,
							Score: fieldScore + 0.06D * sharedEdges - 0.0015D * distance);
					})
					.Where(candidate => candidate.Valid)
					.OrderByDescending(candidate => candidate.Score)
					.ThenBy(candidate => candidate.TopLeft.Y)
					.ThenBy(candidate => candidate.TopLeft.X)
					.ToArray();
				if (candidates.Length == 0)
					break;

				var (topLeft, added, _, _) = candidates[0];
				frontier.Remove(topLeft);
				foreach (var point in added)
					region.Add(point);
				frontier.Add(new RmgPoint(topLeft.X - 1, topLeft.Y));
				frontier.Add(new RmgPoint(topLeft.X + 1, topLeft.Y));
				frontier.Add(new RmgPoint(topLeft.X, topLeft.Y - 1));
				frontier.Add(new RmgPoint(topLeft.X, topLeft.Y + 1));
			}

			var transformed = region.Select(point =>
				Transform(point, settings.Symmetry, map.Width, map.Height)).ToHashSet();
			return region.Count > 0 && MinimumDistance(region, transformed) >= 3 &&
				ShorelineShapeIsSupported(region) && ShorelineShapeIsSupported(transformed) ?
				region : new HashSet<RmgPoint>();
		}

		static bool CoherentWaterShapeSupported(HashSet<RmgPoint> region, RmgPoint[] block)
		{
			bool Contains(RmgPoint point) => region.Contains(point) || block.Contains(point);
			var affected = block.SelectMany(point =>
				Enumerable.Range(-1, 3).SelectMany(dy =>
					Enumerable.Range(-1, 3).Select(dx =>
						new RmgPoint(point.X + dx, point.Y + dy))))
				.Where(Contains).Distinct();
			foreach (var point in affected)
			{
				var cardinalMask = 0;
				if (!Contains(new RmgPoint(point.X, point.Y - 1)))
					cardinalMask |= 1;
				if (!Contains(new RmgPoint(point.X + 1, point.Y)))
					cardinalMask |= 2;
				if (!Contains(new RmgPoint(point.X, point.Y + 1)))
					cardinalMask |= 4;
				if (!Contains(new RmgPoint(point.X - 1, point.Y)))
					cardinalMask |= 8;
				var diagonalClearCount = new[]
					{
						new RmgPoint(point.X - 1, point.Y - 1),
						new RmgPoint(point.X + 1, point.Y - 1),
						new RmgPoint(point.X + 1, point.Y + 1),
						new RmgPoint(point.X - 1, point.Y + 1)
					}.Count(diagonal => !Contains(diagonal));
				var supported = cardinalMask == 0 ? diagonalClearCount <= 1 :
					cardinalMask is 1 or 2 or 3 or 4 or 6 or 8 or 9 or 12;
				if (!supported)
					return false;
			}

			return true;
		}

		static bool CoherentWaterCellEligible(RmgLogicalMap map, RmgPoint point)
		{
			if (point.X < CoherentWaterMarginLogical || point.Y < CoherentWaterMarginLogical ||
				point.X >= map.Width - CoherentWaterMarginLogical ||
				point.Y >= map.Height - CoherentWaterMarginLogical)
				return false;

			return ObstacleCellEligible(map, point);
		}

		static RmgPoint[] CoherentWaterBlock(RmgPoint topLeft) => new[]
		{
			new RmgPoint(topLeft.X, topLeft.Y),
			new RmgPoint(topLeft.X + 1, topLeft.Y),
			new RmgPoint(topLeft.X, topLeft.Y + 1),
			new RmgPoint(topLeft.X + 1, topLeft.Y + 1)
		};

		static double[] BuildCoherentWaterField(RmgLogicalMap map, RmgProfile profile,
			RmgGenerationSettings settings, int orbit, int attempt)
		{
			var field = new double[map.Width * map.Height];
			AddOctave(16, 0.55D);
			AddOctave(8, 0.30D);
			AddOctave(4, 0.15D);
			var interiorBias = settings.WaterAmount switch
			{
				RmgParameterLevel.Low => 0.06D,
				RmgParameterLevel.Standard => 0.14D,
				_ => 0.28D
			};
			for (var index = 0; index < field.Length; index++)
			{
				var x = index % map.Width;
				var y = index / map.Width;
				var edgeDistance = Math.Min(Math.Min(x, map.Width - 1 - x),
					Math.Min(y, map.Height - 1 - y));
				var interiorWeight = Math.Clamp((edgeDistance - CoherentWaterMarginLogical) /
					(double)(WaterInteriorMarginLogical - CoherentWaterMarginLogical), 0D, 1D);
				field[index] += interiorBias * interiorWeight;
			}

			return field;

			void AddOctave(int scale, double weight)
			{
				var random = DeterministicRandom.ForStream(settings, profile,
					$"natural-water-field-{orbit}-{attempt}-{scale}");
				var gridWidth = map.Width / scale + 3;
				var gridHeight = map.Height / scale + 3;
				var lattice = new double[gridWidth * gridHeight];
				for (var i = 0; i < lattice.Length; i++)
					lattice[i] = random.NextUInt64() / (double)ulong.MaxValue;

				for (var y = 0; y < map.Height; y++)
					for (var x = 0; x < map.Width; x++)
					{
						var gridX = x / scale;
						var gridY = y / scale;
						var fractionX = Smooth(x % scale / (double)scale);
						var fractionY = Smooth(y % scale / (double)scale);
						var top = Lerp(lattice[gridY * gridWidth + gridX],
							lattice[gridY * gridWidth + gridX + 1], fractionX);
						var bottom = Lerp(lattice[(gridY + 1) * gridWidth + gridX],
							lattice[(gridY + 1) * gridWidth + gridX + 1], fractionX);
						field[map.Index(new RmgPoint(x, y))] += weight * Lerp(top, bottom, fractionY);
					}
			}
		}

		static double Smooth(double value) => value * value * (3D - 2D * value);
		static double Lerp(double first, double second, double amount) => first + (second - first) * amount;

		static void ValidateCoherentWaterAttempt(RmgLogicalMap map, RmgProfile profile,
			RmgGenerationSettings settings, int attempt)
		{
			var density = 100D * map.Obstacles.Count(value => value) / map.Obstacles.Length;
			var (_, maximum) = profile.ObstacleDensityRange(settings.Archetype, settings.WaterAmount);
			if (density < 6D || density > maximum)
				throw new ObstacleDensityTargetMissException(
					$"Natural attempt {attempt} produced Water density {density:F3}%, outside the " +
					$"6-{maximum}% safe envelope.");

			var (interiorDensity, _, coveredSectors, _) = WaterInteriorMetrics(map);
			var (minimumInteriorDensity, minimumInteriorSectors) =
				WaterInteriorMinimum(settings.WaterAmount, true);
			if (interiorDensity < minimumInteriorDensity || coveredSectors < minimumInteriorSectors)
				throw new ObstacleDensityTargetMissException(
					$"Natural attempt {attempt} produced battlefield-interior Water {interiorDensity:F3}% " +
					$"across {coveredSectors}/16 sectors; at least {minimumInteriorDensity}% across " +
					$"{minimumInteriorSectors}/16 sectors is required.");

			var components = ConnectedComponents(map, blocked: true);
			var total = components.Sum(component => component.Count);
			var largestShare = total == 0 ? 0D : 100D * components.Max(component => component.Count) / total;
			var smallThreshold = (int)Math.Ceiling(map.Obstacles.Length * 0.005D);
			var smallShare = total == 0 ? 0D : 100D * components
				.Where(component => component.Count < smallThreshold)
				.Sum(component => component.Count) / total;
			if (components.Count > 12 || largestShare < 20D || smallShare > 15D)
				throw new ObstacleDensityTargetMissException(
					$"Natural attempt {attempt} produced {components.Count} Water bodies, " +
					$"{largestShare:F2}% largest-body share, and {smallShare:F2}% small-body share.");
		}
	}
}
