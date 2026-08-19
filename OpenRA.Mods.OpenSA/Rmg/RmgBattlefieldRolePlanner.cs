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
	public enum RmgBattlefieldRole
	{
		None,
		Blocked,
		ProtectedClear,
		Contest,
		PrimaryRoute,
		Flank,
		Quiet
	}

	public static class RmgBattlefieldRolePlanner
	{
		public static void Plan(RmgLogicalMap map, RmgProfile profile, RmgGenerationSettings settings)
		{
			if (!profile.UsesBattlefieldLayout)
				return;

			var tacticalSources = new List<RmgPoint>();

			// First classify local semantics, then close each role over the configured symmetry orbit.
			for (var y = 0; y < map.Height; y++)
				for (var x = 0; x < map.Width; x++)
				{
					var point = new RmgPoint(x, y);
					var index = map.Index(point);
					var role = map.Obstacles[index] ? RmgBattlefieldRole.Blocked :
						map.StartReservations[index] || map.StructureReservations[index] ? RmgBattlefieldRole.ProtectedClear :
						map.StrategicRegions[index] || map.ChokepointIds[index] >= 0 ? RmgBattlefieldRole.Contest :
						map.RouteMasks[index] != 0 ? RmgBattlefieldRole.PrimaryRoute : RmgBattlefieldRole.Quiet;
					map.BattlefieldRoles[index] = role;
				}

			for (var y = 0; y < map.Height; y++)
				for (var x = 0; x < map.Width; x++)
				{
					var point = new RmgPoint(x, y);
					var partner = RmgGenerator.Transform(point, settings.Symmetry, map.Width, map.Height);
					var pointIndex = map.Index(point);
					var partnerIndex = map.Index(partner);
					if (pointIndex > partnerIndex)
						continue;
					var merged = Merge(map.BattlefieldRoles[pointIndex], map.BattlefieldRoles[partnerIndex]);
					map.BattlefieldRoles[pointIndex] = merged;
					map.BattlefieldRoles[partnerIndex] = merged;
				}

			for (var y = 0; y < map.Height; y++)
				for (var x = 0; x < map.Width; x++)
				{
					var point = new RmgPoint(x, y);
					if (IsTactical(map.BattlefieldRoles[map.Index(point)]))
						tacticalSources.Add(point);
				}

			for (var y = 0; y < map.Height; y++)
				for (var x = 0; x < map.Width; x++)
				{
					var point = new RmgPoint(x, y);
					var index = map.Index(point);
					if (map.BattlefieldRoles[index] != RmgBattlefieldRole.Quiet)
						continue;
					if (tacticalSources.Any(source => source.ChebyshevDistance(point) <= profile.BattlefieldFlankRadiusLogical))
						map.BattlefieldRoles[index] = RmgBattlefieldRole.Flank;
				}

			for (var y = 0; y < map.Height; y++)
				for (var x = 0; x < map.Width; x++)
				{
					var point = new RmgPoint(x, y);
					var partner = RmgGenerator.Transform(point, settings.Symmetry, map.Width, map.Height);
					if (map.BattlefieldRoles[map.Index(point)] != map.BattlefieldRoles[map.Index(partner)])
						throw new RmgGenerationRejectedException("BATTLEFIELD_ROLE_SYMMETRY",
							$"Battlefield role at {point} differs from symmetry partner {partner}.");
				}

			static RmgBattlefieldRole Merge(RmgBattlefieldRole first, RmgBattlefieldRole second)
			{
				if (first == second)
					return first;
				if (first == RmgBattlefieldRole.Blocked || second == RmgBattlefieldRole.Blocked)
					return RmgBattlefieldRole.Blocked;
				if (first == RmgBattlefieldRole.ProtectedClear || second == RmgBattlefieldRole.ProtectedClear)
					return RmgBattlefieldRole.ProtectedClear;
				return Priority(first) >= Priority(second) ? first : second;
			}
		}

		public static bool IsTactical(RmgBattlefieldRole role) => role == RmgBattlefieldRole.Contest ||
			role == RmgBattlefieldRole.PrimaryRoute || role == RmgBattlefieldRole.Flank;

		public static bool MustRemainClear(RmgBattlefieldRole role) => role == RmgBattlefieldRole.ProtectedClear;

		public static int Priority(RmgBattlefieldRole role) => role switch
		{
			RmgBattlefieldRole.Contest => 4,
			RmgBattlefieldRole.PrimaryRoute => 3,
			RmgBattlefieldRole.Flank => 2,
			RmgBattlefieldRole.Quiet => 1,
			_ => 0
		};

		public static int Sector(RmgLogicalMap map, RmgPoint point, int gridSize = 4)
		{
			var x = Math.Min(gridSize - 1, point.X * gridSize / map.Width);
			var y = Math.Min(gridSize - 1, point.Y * gridSize / map.Height);
			return y * gridSize + x;
		}

		public static bool IsCentralHalf(RmgLogicalMap map, RmgPoint point) =>
			point.X >= map.Width / 4 && point.X < 3 * map.Width / 4 &&
			point.Y >= map.Height / 4 && point.Y < 3 * map.Height / 4;
	}
}
