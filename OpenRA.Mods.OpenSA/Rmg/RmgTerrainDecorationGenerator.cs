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
	public static class RmgTerrainDecorationGenerator
	{
		const int SectorGridSize = 4;
		const int MinimumSpacingNative = 4;
		const string PassableRole = "decoration-passable";
		const string BlockingRole = "decoration-blocking";

		sealed record NativeAnchor(RmgPoint Logical, int Frame, RmgPoint Native);
		sealed record CandidateOrbit(RmgNativeTerrainIntent Terrain, NativeAnchor[] Anchors, int[] Sectors);

		public static void Materialize(RmgLogicalMap map, RmgProfile profile, RmgGenerationSettings settings)
		{
			if (!profile.UsesTerrainDecorations)
				return;

			var occupied = map.Actors.Select(NativePoint).ToHashSet();
			var candidates = new Dictionary<RmgNativeTerrainIntent, List<CandidateOrbit>>
			{
				[RmgNativeTerrainIntent.Clear] = new(),
				[RmgNativeTerrainIntent.Rock] = new(),
				[RmgNativeTerrainIntent.Vegetation] = new()
			};

			for (var y = 0; y < profile.PlayableHeight; y++)
				for (var x = 0; x < profile.PlayableWidth; x++)
				{
					var native = new RmgPoint(x, y);
					var partner = profile.UsesNaturalTerrainMorphology ? native :
						RmgGenerator.Transform(native, settings.Symmetry, profile.PlayableWidth, profile.PlayableHeight);
					if (NativeIndex(native, profile.PlayableWidth) > NativeIndex(partner, profile.PlayableWidth))
						continue;
					var orbit = new[] { native, partner }.Distinct().Select(ToAnchor).ToArray();
					if (orbit.Any(anchor => !Eligible(map, profile, anchor, occupied)) ||
						orbit.Select(anchor => TerrainAt(map, anchor)).Distinct().Count() != 1 ||
						orbit.SelectMany((first, index) => orbit.Skip(index + 1)
							.Select(second => first.Native.ChebyshevDistance(second.Native))).Any(distance => distance < MinimumSpacingNative))
						continue;

					var terrain = TerrainAt(map, orbit[0]);
					if (!candidates.ContainsKey(terrain))
						continue;
					candidates[terrain].Add(new CandidateOrbit(terrain, orbit,
						orbit.Select(anchor => Sector(anchor.Native, profile)).Distinct().ToArray()));
				}

			var requested = (profile.PlayableWidth * profile.PlayableHeight * profile.LandDecorationPerThousand + 500) / 1000;
			var target = profile.UsesNaturalTerrainMorphology ? requested : requested & ~1;
			var landCells = map.NativeTerrainIntents.Count(intent => intent == RmgNativeTerrainIntent.Clear ||
				intent == RmgNativeTerrainIntent.Rock || intent == RmgNativeTerrainIntent.Vegetation);
			var rockTarget = SurfaceTarget(target, map.NativeTerrainIntents.Count(intent => intent == RmgNativeTerrainIntent.Rock), landCells);
			var vegetationTarget = SurfaceTarget(target, map.NativeTerrainIntents.Count(intent => intent == RmgNativeTerrainIntent.Vegetation), landCells);
			var clearTarget = target - rockTarget - vegetationTarget;
			if (clearTarget < 2)
				throw new RmgGenerationRejectedException("LAND_DECORATION_TARGETS",
					$"Terrain-weighted decoration targets {clearTarget}/{rockTarget}/{vegetationTarget} cannot fit total {target}.");

			var targets = new Dictionary<RmgNativeTerrainIntent, int>
			{
				[RmgNativeTerrainIntent.Clear] = clearTarget,
				[RmgNativeTerrainIntent.Rock] = rockTarget,
				[RmgNativeTerrainIntent.Vegetation] = vegetationTarget
			};
			var random = DeterministicRandom.ForStream(settings, profile, "actors-terrain-decoration");
			foreach (var values in candidates.Values)
				Shuffle(values, random);
			var selected = new List<CandidateOrbit>();
			var actorOffsets = targets.Keys.ToDictionary(terrain => terrain,
				terrain => random.NextInt(ActorsForTerrain(profile, terrain).Count));
			var selectedOrbitCounts = targets.Keys.ToDictionary(terrain => terrain, _ => 0);
			var selectedPoints = new HashSet<RmgPoint>();
			var spacedPoints = targets.Keys.ToDictionary(terrain => terrain,
				_ => new HashSet<RmgPoint>());
			var covered = new HashSet<int>();

			foreach (var terrain in new[] { RmgNativeTerrainIntent.Vegetation, RmgNativeTerrainIntent.Rock, RmgNativeTerrainIntent.Clear })
			{
				var selectedForTerrain = 0;
				while (selectedForTerrain < targets[terrain])
				{
					var actors = ActorsForTerrain(profile, terrain);
					var actor = actors[(actorOffsets[terrain] + selectedOrbitCounts[terrain]) % actors.Count];
					var blocking = profile.BlockingDecorationActors.Contains(actor);
					var candidate = candidates[terrain]
						.Where(orbit => !selected.Contains(orbit) && CanSelect(orbit, spacedPoints[terrain]) &&
							(!blocking || BlockingSafe(map, orbit)))
						.OrderByDescending(orbit => orbit.Sectors.Count(sector => !covered.Contains(sector)))
						.FirstOrDefault();
					if (candidate == null || selectedForTerrain + candidate.Anchors.Length > targets[terrain])
						throw new RmgGenerationRejectedException("LAND_DECORATION_CAPACITY",
							$"Selected {selectedForTerrain}/{targets[terrain]} {terrain} decorations from {candidates[terrain].Count} eligible candidate orbits.");
					Select(candidate);
					selectedForTerrain += candidate.Anchors.Length;
					selectedOrbitCounts[terrain]++;
				}
			}

			if (selectedPoints.Count != target)
				throw new RmgGenerationRejectedException("LAND_DECORATION_CAPACITY",
					$"Selected {selectedPoints.Count} land decorations; target is {target}.");
			if (covered.Count < profile.MinimumLandDecorationSectors)
				throw new RmgGenerationRejectedException("LAND_DECORATION_COVERAGE",
					$"Land decorations cover {covered.Count}/16 sectors; required minimum is {profile.MinimumLandDecorationSectors}.");

			var actorOrdinals = targets.Keys.ToDictionary(terrain => terrain, _ => 0);
			var equivalenceGroup = 100000;
			foreach (var orbit in selected)
			{
				var actors = ActorsForTerrain(profile, orbit.Terrain);
				var actor = actors[(actorOffsets[orbit.Terrain] + actorOrdinals[orbit.Terrain]++) % actors.Count];
				var role = profile.BlockingDecorationActors.Contains(actor) ? BlockingRole : PassableRole;
				foreach (var anchor in orbit.Anchors)
					map.Actors.Add(new RmgActorPlan(actor, profile.SpawnOwner, role, anchor.Logical, equivalenceGroup, anchor.Frame));
				equivalenceGroup++;
			}

			map.LandDecorationRequestedCount = requested;
			map.LandDecorationTargetCount = target;
			map.LandDecorationSelectedCount = selectedPoints.Count;
			map.LandDecorationSectorCount = covered.Count;
			map.LandDecorationClearTargetCount = clearTarget;
			map.LandDecorationRockTargetCount = rockTarget;
			map.LandDecorationVegetationTargetCount = vegetationTarget;

			void Select(CandidateOrbit orbit)
			{
				selected.Add(orbit);
				foreach (var anchor in orbit.Anchors)
				{
					selectedPoints.Add(anchor.Native);
					spacedPoints[orbit.Terrain].Add(anchor.Native);
				}

				foreach (var sector in orbit.Sectors)
					covered.Add(sector);
			}
		}

		public static bool IsDecoration(RmgActorPlan actor) =>
			actor.Role == PassableRole || actor.Role == BlockingRole;

		public static bool IsBlocking(RmgActorPlan actor) => actor.Role == BlockingRole;

		public static RmgPoint NativePoint(RmgActorPlan actor) => new(
			2 * actor.LogicalLocation.X + actor.NativeFrame % 2,
			2 * actor.LogicalLocation.Y + actor.NativeFrame / 2);

		public static RmgNativeTerrainIntent TerrainAt(RmgLogicalMap map, RmgActorPlan actor) =>
			map.NativeTerrainIntents[4 * map.Index(actor.LogicalLocation) + actor.NativeFrame];

		public static IReadOnlyList<string> ActorsForTerrain(RmgProfile profile, RmgNativeTerrainIntent terrain) => terrain switch
		{
			RmgNativeTerrainIntent.Clear => profile.SoilDecorationActors,
			RmgNativeTerrainIntent.Rock => profile.RockDecorationActors,
			RmgNativeTerrainIntent.Vegetation => profile.VegetationDecorationActors,
			_ => Array.Empty<string>()
		};

		public static string SelectionHash(RmgLogicalMap map)
		{
			var text = string.Join("\n", map.Actors.Where(IsDecoration)
				.OrderBy(actor => NativePoint(actor).Y).ThenBy(actor => NativePoint(actor).X)
				.Select(actor => $"{actor.Type}:{actor.Role}:{NativePoint(actor)}"));
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
		}

		static NativeAnchor ToAnchor(RmgPoint native) => new(
			new RmgPoint(native.X / 2, native.Y / 2), native.X % 2 + 2 * (native.Y % 2), native);

		static bool Eligible(RmgLogicalMap map, RmgProfile profile, NativeAnchor anchor, IReadOnlySet<RmgPoint> occupied)
		{
			var index = map.Index(anchor.Logical);
			var protectedClear = profile.UsesNaturalTerrainMorphology ?
				RmgClearLandDetailMaterializer.IsProtected(map, index) :
				RmgBattlefieldRolePlanner.MustRemainClear(map.BattlefieldRoles[index]);
			return !map.Obstacles[index] && !protectedClear && !occupied.Contains(anchor.Native) &&
				TerrainAt(map, anchor) != RmgNativeTerrainIntent.Water;
		}

		static RmgNativeTerrainIntent TerrainAt(RmgLogicalMap map, NativeAnchor anchor) =>
			map.NativeTerrainIntents[4 * map.Index(anchor.Logical) + anchor.Frame];

		static bool CanSelect(CandidateOrbit candidate, IReadOnlyCollection<RmgPoint> selected) =>
			candidate.Anchors.All(anchor => selected.All(other => anchor.Native.ChebyshevDistance(other) >= MinimumSpacingNative));

		// Blocking decoration footprints must not consume the reserved cells used to measure named route width.
		// Strategic endpoint regions remain eligible and are checked by the authoritative native movement validator.
		static bool BlockingSafe(RmgLogicalMap map, CandidateOrbit candidate) =>
			candidate.Anchors.All(anchor => map.RouteMasks[map.Index(anchor.Logical)] == 0);

		static int SurfaceTarget(int totalTarget, int surfaceCells, int landCells)
		{
			if (surfaceCells == 0)
				return 0;
			var rounded = 2 * (int)Math.Round(totalTarget * surfaceCells / (double)landCells / 2,
				MidpointRounding.AwayFromZero);
			return Math.Max(2, rounded);
		}

		static int Sector(RmgPoint native, RmgProfile profile)
		{
			var x = Math.Min(SectorGridSize - 1, native.X * SectorGridSize / profile.PlayableWidth);
			var y = Math.Min(SectorGridSize - 1, native.Y * SectorGridSize / profile.PlayableHeight);
			return y * SectorGridSize + x;
		}

		static int NativeIndex(RmgPoint point, int width) => point.Y * width + point.X;

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
