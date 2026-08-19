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
	public static class RmgPassableDecorationGenerator
	{
		const int SectorGridSize = 4;
		const int MinimumSpacingLogical = 2;

		sealed record CandidateOrbit(RmgPoint[] Points, int[] Sectors);

		public static void Materialize(RmgLogicalMap map, RmgProfile profile, RmgGenerationSettings settings)
		{
			if (!profile.UsesBattlefieldLayout)
				return;

			var occupied = map.Actors.Select(actor => actor.LogicalLocation).ToHashSet();
			var candidates = new List<CandidateOrbit>();
			for (var y = 0; y < map.Height; y++)
				for (var x = 0; x < map.Width; x++)
				{
					var point = new RmgPoint(x, y);
					var partner = RmgGenerator.Transform(point, settings.Symmetry, map.Width, map.Height);
					var index = map.Index(point);
					var partnerIndex = map.Index(partner);
					if (index > partnerIndex)
						continue;
					var orbit = new[] { point, partner }.Distinct().ToArray();
					if (orbit.Any(candidate => !Eligible(map, candidate, occupied)))
						continue;
					candidates.Add(new CandidateOrbit(orbit,
						orbit.Select(candidate => RmgBattlefieldRolePlanner.Sector(map, candidate, SectorGridSize)).Distinct().ToArray()));
				}

			var requested = (profile.PlayableWidth * profile.PlayableHeight * profile.PassableDecorationPerThousand + 500) / 1000;
			var target = requested & ~1;
			var random = DeterministicRandom.ForStream(settings, profile, "actors-passable-decoration");
			Shuffle(candidates, random);
			var selected = new List<CandidateOrbit>();
			var selectedPoints = new HashSet<RmgPoint>();
			var covered = new HashSet<int>();

			while (covered.Count < SectorGridSize * SectorGridSize)
			{
				var candidate = candidates
					.Where(orbit => selectedPoints.Count + orbit.Points.Length <= target && CanSelect(orbit, selectedPoints))
					.OrderByDescending(orbit => orbit.Sectors.Count(sector => !covered.Contains(sector)))
					.FirstOrDefault();
				if (candidate == null || candidate.Sectors.All(covered.Contains))
					break;
				Select(candidate);
			}

			foreach (var candidate in candidates)
			{
				if (selectedPoints.Count >= target)
					break;
				if (selectedPoints.Count + candidate.Points.Length > target || !CanSelect(candidate, selectedPoints))
					continue;
				Select(candidate);
			}

			if (selectedPoints.Count != target)
				throw new RmgGenerationRejectedException("PASSABLE_DECORATION_CAPACITY",
					$"Selected {selectedPoints.Count} passable decorations; target is {target} from {candidates.Count} candidate orbits.");
			if (covered.Count < profile.MinimumPassableDecorationSectors)
				throw new RmgGenerationRejectedException("PASSABLE_DECORATION_COVERAGE",
					$"Passable decorations cover {covered.Count}/16 sectors; required minimum is {profile.MinimumPassableDecorationSectors}.");

			var equivalenceGroup = 100000;
			foreach (var orbit in selected)
			{
				var actor = profile.PassableDecorationActors[random.NextInt(profile.PassableDecorationActors.Length)];
				foreach (var point in orbit.Points)
					map.Actors.Add(new RmgActorPlan(actor, profile.SpawnOwner, "cosmetic-passable", point, equivalenceGroup));
				equivalenceGroup++;
			}

			map.PassableDecorationRequestedCount = requested;
			map.PassableDecorationTargetCount = target;
			map.PassableDecorationSelectedCount = selectedPoints.Count;
			map.PassableDecorationSectorCount = covered.Count;

			void Select(CandidateOrbit orbit)
			{
				selected.Add(orbit);
				foreach (var point in orbit.Points)
					selectedPoints.Add(point);
				foreach (var sector in orbit.Sectors)
					covered.Add(sector);
			}
		}

		public static string SelectionHash(RmgLogicalMap map)
		{
			var text = string.Join("\n", map.Actors.Where(actor => actor.Role == "cosmetic-passable")
				.OrderBy(actor => actor.LogicalLocation.Y).ThenBy(actor => actor.LogicalLocation.X)
				.Select(actor => $"{actor.Type}:{actor.LogicalLocation}"));
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
		}

		static bool Eligible(RmgLogicalMap map, RmgPoint point, IReadOnlySet<RmgPoint> occupied)
		{
			var index = map.Index(point);
			return !map.Obstacles[index] && !RmgBattlefieldRolePlanner.MustRemainClear(map.BattlefieldRoles[index]) &&
				!occupied.Contains(point) && Enumerable.Range(0, 4).All(frame =>
					map.NativeTerrainIntents[4 * index + frame] != RmgNativeTerrainIntent.Water);
		}

		static bool CanSelect(CandidateOrbit candidate, IReadOnlyCollection<RmgPoint> selected) =>
			candidate.Points.All(point => selected.All(other => point.ChebyshevDistance(other) >= MinimumSpacingLogical));

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
