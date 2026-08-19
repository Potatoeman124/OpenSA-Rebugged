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
	public static class RmgLandCoverMaterializer
	{
		static readonly (int X, int Y)[] Cardinal = { (0, -1), (1, 0), (0, 1), (-1, 0) };

		public static void Materialize(RmgLogicalMap map, RmgProfile profile, RmgGenerationSettings settings)
		{
			if (!profile.UsesLandCover)
				return;

			var latticeWidth = map.Width + 1;
			var latticeHeight = map.Height + 1;
			var landNativeCount = map.NativeTerrainIntents.Count(intent => intent != RmgNativeTerrainIntent.Water);
			var allowedEnvelope = new bool[latticeWidth * latticeHeight];
			for (var y = 0; y < latticeHeight; y++)
				for (var x = 0; x < latticeWidth; x++)
					allowedEnvelope[y * latticeWidth + x] = SlowPointAllowed(map, profile, x, y);

			EnforceSymmetricCapacity(allowedEnvelope, latticeWidth, latticeHeight, settings.Symmetry);
			var allowedVegetationCore = new bool[allowedEnvelope.Length];
			for (var y = 0; y < latticeHeight; y++)
				for (var x = 0; x < latticeWidth; x++)
				{
					var index = y * latticeWidth + x;
					allowedVegetationCore[index] = allowedEnvelope[index] &&
						Clearance(allowedEnvelope, latticeWidth, latticeHeight, new RmgPoint(x, y), 1);
				}

			var rolePriorities = profile.UsesBattlefieldLayout ? BuildLatticeRolePriorities(map, latticeWidth, latticeHeight) : null;

			var requestedVegetationTarget = RoundedPercent(landNativeCount, profile.VegetationLandPercent);
			var vegetationCapacity = WeightedCount(allowedVegetationCore, latticeWidth, latticeHeight, map.Width, map.Height);
			var vegetationTarget = Math.Min(requestedVegetationTarget, vegetationCapacity);
			var vegetation = GenerateMask(allowedVegetationCore, latticeWidth, latticeHeight, vegetationTarget,
				settings.Symmetry, DeterministicRandom.ForStream(settings, profile, "terrain-land-cover-vegetation"), 3,
				rolePriorities);
			var requiredEnvelope = Dilate(vegetation, allowedEnvelope, latticeWidth, latticeHeight, 1);
			if (profile.UsesBattlefieldLayout)
				AddTacticalAnchors(map, profile, settings, requiredEnvelope, allowedEnvelope, rolePriorities, latticeWidth, latticeHeight);
			var requestedRockTarget = RoundedPercent(landNativeCount, profile.RockLandPercent);
			var envelopeCapacity = WeightedCount(allowedEnvelope, latticeWidth, latticeHeight, map.Width, map.Height);
			var combinedTarget = Math.Min(requestedRockTarget + vegetationTarget, envelopeCapacity);
			var envelope = GenerateMask(allowedEnvelope, latticeWidth, latticeHeight, combinedTarget,
				settings.Symmetry, DeterministicRandom.ForStream(settings, profile, "terrain-land-cover-rock-envelope"), 3,
				rolePriorities, requiredEnvelope);
			var envelopeNativeCount = WeightedCount(envelope, latticeWidth, latticeHeight, map.Width, map.Height);
			var vegetationNativeCount = WeightedCount(vegetation, latticeWidth, latticeHeight, map.Width, map.Height);
			var rockNativeCount = envelopeNativeCount - vegetationNativeCount;
			var rockTarget = Math.Max(0, combinedTarget - vegetationTarget);
			var tolerance = Math.Max(8, RoundedPercent(landNativeCount, profile.LandCoverTolerancePercent));
			if (Math.Abs(rockNativeCount - rockTarget) > tolerance || Math.Abs(vegetationNativeCount - vegetationTarget) > tolerance)
				throw new RmgGenerationRejectedException("LAND_COVER_CAPACITY",
					$"Protected Clear zones produced Rock/Vegetation counts {rockNativeCount}/{vegetationNativeCount}; " +
					$"effective targets are {rockTarget}/{vegetationTarget}, requested targets are " +
					$"{requestedRockTarget}/{requestedVegetationTarget}, capacities are {envelopeCapacity}/{vegetationCapacity}, " +
					$"and tolerance is {tolerance} native cells.");

			var rockInteriors = new List<int>();
			var vegetationInteriors = new List<int>();
			for (var y = 0; y < map.Height; y++)
				for (var x = 0; x < map.Width; x++)
				{
					var envelopeMask = StampMask(envelope, latticeWidth, x, y);
					var vegetationMask = StampMask(vegetation, latticeWidth, x, y);
					if (vegetationMask != 0 && envelopeMask != 15)
						throw new RmgGenerationRejectedException("LAND_COVER_DIRECT_CLEAR_VEGETATION",
							$"Logical stamp {x},{y} contains Vegetation outside a homogeneous Rock envelope.");
					var index = map.Index(new RmgPoint(x, y));
					if (vegetationMask == 15)
						vegetationInteriors.Add(index);
					else if (envelopeMask == 15 && vegetationMask == 0)
						rockInteriors.Add(index);
				}

			var detailRandom = DeterministicRandom.ForStream(settings, profile, "terrain-land-cover-details");
			var rockDetails = SelectDetails(rockInteriors, profile.RockDetailPercent, detailRandom);
			var vegetationDetails = SelectDetails(vegetationInteriors, profile.VegetationDetailPercent, detailRandom);
			var variantRandom = DeterministicRandom.ForStream(settings, profile, "terrain-land-cover-variants");
			var clearRockTransitions = 0;
			var rockVegetationTransitions = 0;
			for (var y = 0; y < map.Height; y++)
				for (var x = 0; x < map.Width; x++)
				{
					var index = map.Index(new RmgPoint(x, y));
					var envelopeMask = StampMask(envelope, latticeWidth, x, y);
					var vegetationMask = StampMask(vegetation, latticeWidth, x, y);
					if (vegetationMask != 0)
					{
						NormalLandTemplate template;

						// Template 93 has homogeneous Vegetation semantics but a square Rock-colored visual border.
						// Keep the frozen V5 choice, but use seamless Vegetation interior 78 for revised V6 detail stamps.
						if (vegetationMask == 15 && vegetationDetails.Contains(index))
							template = Required((ushort)(profile.UsesBattlefieldLayout ? 78 : 93));
						else
						{
							var candidates = NormalLandTransitionCatalogue.ForMask(RmgLandTemplateBank.RockVegetation, vegetationMask);
							template = candidates[variantRandom.NextInt(candidates.Count)];
						}

						Assign(map, index, template);
						if (vegetationMask != 15)
							rockVegetationTransitions++;
					}
					else if (envelopeMask != 0)
					{
						NormalLandTemplate template;
						if (envelopeMask == 15 && rockDetails.Contains(index))
							template = Required(94);
						else
						{
							var candidates = NormalLandTransitionCatalogue.ForMask(RmgLandTemplateBank.ClearRock, envelopeMask);
							template = candidates[variantRandom.NextInt(candidates.Count)];
						}

						Assign(map, index, template);
						if (envelopeMask != 15)
							clearRockTransitions++;
					}
				}

			Array.Copy(envelope, map.RockEnvelopeLattice, envelope.Length);
			Array.Copy(vegetation, map.VegetationLattice, vegetation.Length);
			map.LandCoverLandNativeCount = landNativeCount;
			map.LandCoverAllowedLatticeCount = allowedEnvelope.Count(allowed => allowed);
			map.LandCoverEnvelopeCapacityNativeCount = envelopeCapacity;
			map.LandCoverVegetationCapacityNativeCount = vegetationCapacity;
			map.LandCoverRockTargetNativeCount = rockTarget;
			map.LandCoverVegetationTargetNativeCount = vegetationTarget;
			map.LandCoverRockNativeCount = rockNativeCount;
			map.LandCoverVegetationNativeCount = vegetationNativeCount;
			map.LandCoverClearRockTransitionStampCount = clearRockTransitions;
			map.LandCoverRockVegetationTransitionStampCount = rockVegetationTransitions;
			map.LandCoverRockInteriorStampCount = rockInteriors.Count;
			map.LandCoverVegetationInteriorStampCount = vegetationInteriors.Count;
			map.LandCoverRockDetailStampCount = rockDetails.Count;
			map.LandCoverVegetationDetailStampCount = vegetationDetails.Count;
		}

		public static bool IsSlow(RmgNativeTerrainIntent intent) =>
			intent == RmgNativeTerrainIntent.Rock || intent == RmgNativeTerrainIntent.Vegetation;

		public static string SelectionHash(RmgLogicalMap map)
		{
			var text = new StringBuilder();
			for (var i = 0; i < map.RockEnvelopeLattice.Length; i++)
				if (map.RockEnvelopeLattice[i] || map.VegetationLattice[i])
					text.Append(i).Append(':').Append(map.RockEnvelopeLattice[i] ? 'R' : '-')
						.Append(map.VegetationLattice[i] ? 'V' : '-').Append('\n');
			for (var i = 0; i < map.TemplateIds.Length; i++)
				if (NormalLandTransitionCatalogue.TryGet(map.TemplateIds[i], out var template) && template.Permitted)
					text.Append('T').Append(i).Append(':').Append(template.TemplateId).Append('\n');
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
		}

		public static IReadOnlyList<string> RunSelfTests()
		{
			var failures = new List<string>();
			var width = 65;
			var height = 65;
			foreach (var symmetry in Enum.GetValues<RmgSymmetry>())
				foreach (var point in new[] { new RmgPoint(0, 0), new RmgPoint(17, 31), new RmgPoint(32, 32), new RmgPoint(64, 64) })
				{
					var partner = TransformLattice(point, symmetry, width, height);
					if (TransformLattice(partner, symmetry, width, height) != point)
						failures.Add($"Land-cover lattice transform {symmetry} is not an involution for {point}.");
				}

			if (NormalLandTransitionCatalogue.RoleForMask(6) != RmgLandTransitionRole.Unsupported ||
				NormalLandTransitionCatalogue.RoleForMask(9) != RmgLandTransitionRole.Unsupported)
				failures.Add("Diagonal marching-square masks are not rejected by the land-cover contract.");
			return failures;
		}

		static bool SlowPointAllowed(RmgLogicalMap map, RmgProfile profile, int latticeX, int latticeY)
		{
			foreach (var (logicalIndex, frame, nativeX, nativeY) in Occurrences(map, latticeX, latticeY))
			{
				var protectedClear = profile.UsesBattlefieldLayout ?
					RmgBattlefieldRolePlanner.MustRemainClear(map.BattlefieldRoles[logicalIndex]) :
					RmgClearLandDetailMaterializer.IsProtected(map, logicalIndex);
				if (protectedClear ||
					map.NativeTerrainIntents[4 * logicalIndex + frame] != RmgNativeTerrainIntent.Clear)
					return false;

				for (var dy = -1; dy <= 1; dy++)
					for (var dx = -1; dx <= 1; dx++)
					{
						var x = nativeX + dx;
						var y = nativeY + dy;
						if (x < 0 || x >= 2 * map.Width || y < 0 || y >= 2 * map.Height)
							continue;
						if (NativeIntent(map, x, y) == RmgNativeTerrainIntent.Water)
							return false;
					}
			}

			return true;
		}

		static IEnumerable<(int LogicalIndex, int Frame, int NativeX, int NativeY)> Occurrences(
			RmgLogicalMap map, int latticeX, int latticeY)
		{
			var xOccurrences = new List<(int Stamp, int FrameX)>();
			var yOccurrences = new List<(int Stamp, int FrameY)>();
			if (latticeX < map.Width)
				xOccurrences.Add((latticeX, 0));
			if (latticeX > 0)
				xOccurrences.Add((latticeX - 1, 1));
			if (latticeY < map.Height)
				yOccurrences.Add((latticeY, 0));
			if (latticeY > 0)
				yOccurrences.Add((latticeY - 1, 1));
			foreach (var (stamp, frameX) in xOccurrences)
				foreach (var (yStamp, yFrame) in yOccurrences)
				{
					var logical = map.Index(new RmgPoint(stamp, yStamp));
					var frame = 2 * yFrame + frameX;
					yield return (logical, frame, 2 * stamp + frameX, 2 * yStamp + yFrame);
				}
		}

		static RmgNativeTerrainIntent NativeIntent(RmgLogicalMap map, int nativeX, int nativeY)
		{
			var logical = map.Index(new RmgPoint(nativeX / 2, nativeY / 2));
			return map.NativeTerrainIntents[4 * logical + 2 * (nativeY & 1) + (nativeX & 1)];
		}

		static void EnforceSymmetricCapacity(bool[] allowed, int width, int height, RmgSymmetry symmetry)
		{
			for (var y = 0; y < height; y++)
				for (var x = 0; x < width; x++)
				{
					var point = new RmgPoint(x, y);
					var partner = TransformLattice(point, symmetry, width, height);
					if (!allowed[y * width + x] || !allowed[partner.Y * width + partner.X])
					{
						allowed[y * width + x] = false;
						allowed[partner.Y * width + partner.X] = false;
					}
				}
		}

		static int[] BuildLatticeRolePriorities(RmgLogicalMap map, int width, int height)
		{
			var priorities = new int[width * height];
			for (var y = 0; y < height; y++)
				for (var x = 0; x < width; x++)
				{
					var rolePriority = Occurrences(map, x, y)
						.Select(occurrence => RmgBattlefieldRolePlanner.Priority(map.BattlefieldRoles[occurrence.LogicalIndex]))
						.DefaultIfEmpty(0).Max();
					var borderDepth = Math.Min(Math.Min(x, width - 1 - x), Math.Min(y, height - 1 - y));
					priorities[y * width + x] = 100 * rolePriority + borderDepth;
				}

			return priorities;
		}

		static void AddTacticalAnchors(RmgLogicalMap map, RmgProfile profile, RmgGenerationSettings settings,
			bool[] required, bool[] allowed, int[] priorities, int width, int height)
		{
			var random = DeterministicRandom.ForStream(settings, profile, "terrain-land-cover-tactical-anchors");
			var candidates = Enumerable.Range(0, allowed.Length).Where(index => allowed[index] && priorities[index] >= 200)
				.Select(index => new RmgPoint(index % width, index / width))
				.Where(point => Canonical(point, TransformLattice(point, settings.Symmetry, width, height), width, strict: false) &&
					Clearance(allowed, width, height, point, 2)).ToList();
			Shuffle(candidates, random);
			candidates = candidates.OrderByDescending(point =>
				point.X >= width / 4 && point.X < 3 * width / 4 && point.Y >= height / 4 && point.Y < 3 * height / 4)
				.ThenByDescending(point => priorities[point.Y * width + point.X]).ToList();

			var selectedOrbits = 0;
			foreach (var minimumDistance in new[] { 8, 6, 4, 2 })
			{
				if (selectedOrbits >= profile.TacticalLandAnchorOrbitCount)
					break;
				foreach (var candidate in candidates)
				{
					if (selectedOrbits >= profile.TacticalLandAnchorOrbitCount)
						break;
					var partner = TransformLattice(candidate, settings.Symmetry, width, height);
					if (map.BattlefieldTacticalAnchors.Any(anchor =>
						anchor.ChebyshevDistance(candidate) < minimumDistance || anchor.ChebyshevDistance(partner) < minimumDistance))
						continue;
					var addition = new[] { candidate, partner }.Distinct()
						.Where(point => !required[point.Y * width + point.X]).ToArray();
					if (addition.Length > 0 && !TryAdd(required, allowed, addition, width, height, settings.Symmetry, out _))
						continue;
					map.BattlefieldTacticalAnchors.AddRange(new[] { candidate, partner }.Distinct());
					selectedOrbits++;
				}
			}

			map.BattlefieldTacticalAnchorOrbitCount = selectedOrbits;

			if (selectedOrbits == 0)
				throw new RmgGenerationRejectedException("LAND_COVER_TACTICAL_ANCHORS",
					$"No tactical slow-terrain anchor orbit fits the protected-clear and Water-separation constraints; target is {profile.TacticalLandAnchorOrbitCount}.");
		}

		static bool[] GenerateMask(bool[] allowed, int width, int height, int targetWeight, RmgSymmetry symmetry,
			DeterministicRandom random, int seedOrbitCount, int[] priorityScores = null, bool[] requiredMask = null)
		{
			var selected = requiredMask == null ? new bool[allowed.Length] : requiredMask.ToArray();
			if (selected.Length != allowed.Length || selected.Where((value, index) => value && !allowed[index]).Any() ||
				!MasksSupported(selected, width, height))
				throw new RmgGenerationRejectedException("LAND_COVER_REQUIRED_MASK",
					"The required nested land-cover mask is outside capacity or contains an unsupported diagonal transition.");
			var seeds = Enumerable.Range(0, selected.Length).Where(index => selected[index])
				.Select(index => new RmgPoint(index % width, index / width)).ToList();
			var candidates = Enumerable.Range(0, allowed.Length)
				.Where(index => allowed[index])
				.Select(index => new RmgPoint(index % width, index / width))
				.Where(point => Canonical(point, TransformLattice(point, symmetry, width, height), width, strict: true) &&
					Clearance(allowed, width, height, point, 2))
				.ToList();
			Shuffle(candidates, random);
			if (priorityScores != null)
				candidates = candidates.OrderByDescending(point => priorityScores[point.Y * width + point.X]).ToList();
			var reseedCandidates = Enumerable.Range(0, allowed.Length)
				.Where(index => allowed[index])
				.Select(index => new RmgPoint(index % width, index / width))
				.Where(point => Canonical(point, TransformLattice(point, symmetry, width, height), width, strict: false)).ToList();
			Shuffle(reseedCandidates, random);
			if (priorityScores != null)
				reseedCandidates = reseedCandidates.OrderByDescending(point => priorityScores[point.Y * width + point.X]).ToList();
			foreach (var candidate in candidates)
			{
				if (seeds.Count >= 2 * seedOrbitCount)
					break;
				var partner = TransformLattice(candidate, symmetry, width, height);
				if (seeds.Any(seed => seed.ChebyshevDistance(candidate) < 10 || seed.ChebyshevDistance(partner) < 10))
					continue;
				var addition = new[] { candidate, partner }.Distinct().ToArray();
				if (!TryAdd(selected, allowed, addition, width, height, symmetry, out _))
					continue;
				seeds.AddRange(addition);
			}

			if (seeds.Count == 0)
				foreach (var candidate in Enumerable.Range(0, allowed.Length).Where(index => allowed[index])
					.Select(index => new RmgPoint(index % width, index / width)))
				{
					var partner = TransformLattice(candidate, symmetry, width, height);
					if (!Canonical(candidate, partner, width, strict: false) ||
						!TryAdd(selected, allowed, new[] { candidate, partner }.Distinct().ToArray(), width, height, symmetry, out _))
						continue;
					seeds.Add(candidate);
					break;
				}

			var currentWeight = WeightedCount(selected, width, height, width - 1, height - 1);
			while (currentWeight < targetWeight)
			{
				var frontier = new HashSet<RmgPoint>();
				for (var index = 0; index < selected.Length; index++)
				{
					if (!selected[index])
						continue;
					var point = new RmgPoint(index % width, index / width);
					foreach (var (dx, dy) in Cardinal)
					{
						var neighbor = new RmgPoint(point.X + dx, point.Y + dy);
						if (neighbor.X < 0 || neighbor.X >= width || neighbor.Y < 0 || neighbor.Y >= height ||
							selected[neighbor.Y * width + neighbor.X] || !allowed[neighbor.Y * width + neighbor.X])
							continue;
						var partner = TransformLattice(neighbor, symmetry, width, height);
						frontier.Add(Canonical(neighbor, partner, width, strict: false) ? neighbor : partner);
					}
				}

				var ordered = frontier.ToList();
				Shuffle(ordered, random);
				if (priorityScores == null)
					ordered = ordered.OrderByDescending(point => NeighborScore(selected, width, height, point,
						TransformLattice(point, symmetry, width, height))).ToList();
				else
					ordered = ordered.OrderByDescending(point => priorityScores[point.Y * width + point.X])
						.ThenByDescending(point => NeighborScore(selected, width, height, point,
							TransformLattice(point, symmetry, width, height))).ToList();
				var progressed = false;
				foreach (var candidate in ordered)
				{
					var partner = TransformLattice(candidate, symmetry, width, height);
					var addition = new[] { candidate, partner }.Distinct()
						.Where(point => !selected[point.Y * width + point.X]).ToArray();
					var delta = addition.Sum(point => PointWeight(point, width - 1, height - 1));
					if (delta == 0 || Math.Abs(currentWeight + delta - targetWeight) > Math.Abs(currentWeight - targetWeight))
						continue;
					if (!TryAdd(selected, allowed, addition, width, height, symmetry, out var addedWeight))
						continue;
					currentWeight += addedWeight;
					progressed = true;
					break;
				}

				if (progressed)
					continue;

				foreach (var candidate in reseedCandidates)
				{
					var partner = TransformLattice(candidate, symmetry, width, height);
					var addition = new[] { candidate, partner }.Distinct()
						.Where(point => !selected[point.Y * width + point.X]).ToArray();
					var delta = addition.Sum(point => PointWeight(point, width - 1, height - 1));
					if (delta == 0 || Math.Abs(currentWeight + delta - targetWeight) > Math.Abs(currentWeight - targetWeight) ||
						!TryAdd(selected, allowed, addition, width, height, symmetry, out var addedWeight))
						continue;
					currentWeight += addedWeight;
					progressed = true;
					break;
				}

				if (!progressed)
					break;
			}

			return selected;
		}

		static bool TryAdd(bool[] selected, bool[] allowed, IReadOnlyCollection<RmgPoint> addition, int width, int height,
			RmgSymmetry symmetry, out int addedWeight)
		{
			var changed = new HashSet<RmgPoint>();
			var pendingStamps = new HashSet<RmgPoint>();
			var accumulatedWeight = 0;
			void QueueNeighborhood(RmgPoint point)
			{
				for (var dy = -1; dy <= 0; dy++)
					for (var dx = -1; dx <= 0; dx++)
					{
						var x = point.X + dx;
						var y = point.Y + dy;
						if (x >= 0 && x < width - 1 && y >= 0 && y < height - 1)
							pendingStamps.Add(new RmgPoint(x, y));
					}
			}

			bool AddOrbit(RmgPoint point)
			{
				var orbit = new[] { point, TransformLattice(point, symmetry, width, height) }.Distinct().ToArray();
				if (orbit.Any(candidate => !allowed[candidate.Y * width + candidate.X]))
					return false;
				foreach (var candidate in orbit)
					if (!selected[candidate.Y * width + candidate.X])
					{
						selected[candidate.Y * width + candidate.X] = true;
						changed.Add(candidate);
						accumulatedWeight += PointWeight(candidate, width - 1, height - 1);
						QueueNeighborhood(candidate);
					}

				return true;
			}

			foreach (var point in addition)
				if (!AddOrbit(point))
				{
					foreach (var candidate in changed)
						selected[candidate.Y * width + candidate.X] = false;
					addedWeight = 0;
					return false;
				}

			while (pendingStamps.Count > 0)
			{
				var stampPoint = pendingStamps.OrderBy(point => point.Y).ThenBy(point => point.X).First();
				pendingStamps.Remove(stampPoint);
				var stamp = StampMask(selected, width, stampPoint.X, stampPoint.Y);
				var fillOptions = stamp switch
				{
					6 => new[] { stampPoint, new RmgPoint(stampPoint.X + 1, stampPoint.Y + 1) },
					9 => new[] { new RmgPoint(stampPoint.X + 1, stampPoint.Y), new RmgPoint(stampPoint.X, stampPoint.Y + 1) },
					_ => null
				};

				if (fillOptions == null)
					continue;
				var filled = fillOptions
					.OrderByDescending(point => NeighborScore(selected, width, height, point))
					.ThenBy(point => point.Y).ThenBy(point => point.X)
					.Any(AddOrbit);
				if (filled)
					continue;
				foreach (var candidate in changed)
					selected[candidate.Y * width + candidate.X] = false;
				addedWeight = 0;
				return false;
			}

			addedWeight = accumulatedWeight;
			return true;
		}

		static bool MasksSupported(bool[] mask, int width, int height)
		{
			for (var y = 0; y < height - 1; y++)
				for (var x = 0; x < width - 1; x++)
				{
					var stamp = StampMask(mask, width, x, y);
					if (stamp == 6 || stamp == 9)
						return false;
				}

			return true;
		}

		static int StampMask(bool[] mask, int width, int x, int y) =>
			(mask[y * width + x] ? 1 : 0) |
			(mask[y * width + x + 1] ? 2 : 0) |
			(mask[(y + 1) * width + x] ? 4 : 0) |
			(mask[(y + 1) * width + x + 1] ? 8 : 0);

		static bool[] Dilate(bool[] source, bool[] allowed, int width, int height, int radius)
		{
			var result = new bool[source.Length];
			for (var index = 0; index < source.Length; index++)
			{
				if (!source[index])
					continue;
				var center = new RmgPoint(index % width, index / width);
				for (var dy = -radius; dy <= radius; dy++)
					for (var dx = -radius; dx <= radius; dx++)
					{
						var x = center.X + dx;
						var y = center.Y + dy;
						if (x < 0 || x >= width || y < 0 || y >= height || !allowed[y * width + x])
							throw new RmgGenerationRejectedException("LAND_COVER_ENVELOPE_RING",
								$"Vegetation core at {center} cannot retain a complete Rock envelope ring.");
						result[y * width + x] = true;
					}
			}

			if (!MasksSupported(result, width, height))
				throw new RmgGenerationRejectedException("LAND_COVER_UNSUPPORTED_MASK", "Rock-envelope dilation produced an unsupported diagonal mask.");
			return result;
		}

		static HashSet<int> SelectDetails(IReadOnlyCollection<int> candidates, int percent, DeterministicRandom random)
		{
			var shuffled = candidates.ToList();
			Shuffle(shuffled, random);
			return shuffled.Take(RoundedPercent(shuffled.Count, percent)).ToHashSet();
		}

		static NormalLandTemplate Required(ushort id)
		{
			if (!NormalLandTransitionCatalogue.TryGet(id, out var template))
				throw new InvalidOperationException($"Audited NORMAL land template {id} is missing from the catalogue.");
			return template;
		}

		static void Assign(RmgLogicalMap map, int index, NormalLandTemplate template)
		{
			map.TemplateIds[index] = template.TemplateId;
			for (var frame = 0; frame < 4; frame++)
				map.NativeTerrainIntents[4 * index + frame] = template.NativeTerrain[frame];
		}

		static int WeightedCount(bool[] mask, int latticeWidth, int latticeHeight, int mapWidth, int mapHeight)
		{
			var count = 0;
			for (var y = 0; y < latticeHeight; y++)
				for (var x = 0; x < latticeWidth; x++)
					if (mask[y * latticeWidth + x])
						count += PointWeight(new RmgPoint(x, y), mapWidth, mapHeight);
			return count;
		}

		static int PointWeight(RmgPoint point, int mapWidth, int mapHeight)
		{
			var x = (point.X > 0 ? 1 : 0) + (point.X < mapWidth ? 1 : 0);
			var y = (point.Y > 0 ? 1 : 0) + (point.Y < mapHeight ? 1 : 0);
			return x * y;
		}

		static int NeighborScore(bool[] selected, int width, int height, params RmgPoint[] points)
		{
			var score = 0;
			foreach (var point in points.Distinct())
				foreach (var (dx, dy) in Cardinal)
				{
					var x = point.X + dx;
					var y = point.Y + dy;
					if (x >= 0 && x < width && y >= 0 && y < height && selected[y * width + x])
						score++;
				}

			return score;
		}

		static bool Clearance(bool[] allowed, int width, int height, RmgPoint point, int radius)
		{
			for (var dy = -radius; dy <= radius; dy++)
				for (var dx = -radius; dx <= radius; dx++)
				{
					var x = point.X + dx;
					var y = point.Y + dy;
					if (x < 0 || x >= width || y < 0 || y >= height || !allowed[y * width + x])
						return false;
				}

			return true;
		}

		static RmgPoint TransformLattice(RmgPoint point, RmgSymmetry symmetry, int width, int height) => symmetry switch
		{
			RmgSymmetry.MirrorHorizontal => new RmgPoint(point.X, height - 1 - point.Y),
			RmgSymmetry.MirrorVertical => new RmgPoint(width - 1 - point.X, point.Y),
			RmgSymmetry.Rotate180 => new RmgPoint(width - 1 - point.X, height - 1 - point.Y),
			_ => throw new ArgumentOutOfRangeException(nameof(symmetry))
		};

		static bool Canonical(RmgPoint point, RmgPoint partner, int width, bool strict)
		{
			var first = point.Y * width + point.X;
			var second = partner.Y * width + partner.X;
			return strict ? first < second : first <= second;
		}

		static int RoundedPercent(int count, int percent) => (count * percent + 50) / 100;

		static void Shuffle<T>(IList<T> values, DeterministicRandom random)
		{
			for (var i = values.Count - 1; i > 0; i--)
			{
				var j = random.NextInt(i + 1);
				(values[i], values[j]) = (values[j], values[i]);
			}
		}
	}
}
