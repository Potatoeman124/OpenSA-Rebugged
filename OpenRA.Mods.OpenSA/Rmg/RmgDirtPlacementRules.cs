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
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public sealed class RmgDirtPlacementRules
	{
		readonly Dictionary<string, RmgPoint[]> colonies;
		readonly RmgPoint[] startingOffsets;

		RmgDirtPlacementRules(Dictionary<string, RmgPoint[]> colonies, RmgPoint[] startingOffsets)
		{
			this.colonies = colonies;
			this.startingOffsets = startingOffsets;
		}

		public static RmgDirtPlacementRules Load(ModData modData, IEnumerable<string> colonyActors)
		{
			RmgPoint[] Coverage(string actor) => modData.DefaultRules.Actors[actor]
				.TraitInfos<BuildingInfo>().SelectMany(info => info.Tiles(new CPos(0, 0)))
				.Select(cell => new RmgPoint(cell.X, cell.Y)).Append(new RmgPoint(0, 0)).Distinct().ToArray();

			var colonies = colonyActors.ToDictionary(actor => actor, Coverage, StringComparer.OrdinalIgnoreCase);
			var starts = modData.DefaultRules.Actors[SystemActors.World].TraitInfos<StartingUnitsInfo>()
				.Where(info => !string.IsNullOrEmpty(info.BaseActor))
				.SelectMany(info => Coverage(info.BaseActor).Select(offset =>
					new RmgPoint(offset.X + info.BaseActorOffset.X, offset.Y + info.BaseActorOffset.Y)))
				.Append(new RmgPoint(0, 0)).Distinct().ToArray();
			if (starts.Length <= 1 || colonies.Values.Any(coverage => coverage.Length <= 1))
				throw new InvalidOperationException("Dirt placement requires runtime starting-base and colony footprints.");
			return new RmgDirtPlacementRules(colonies, starts);
		}

		// Queries only: terrain generation is finished before these predicates are used.
		public bool StartFits(RmgLogicalMap map, RmgPoint anchor, int[] waterDistance) =>
			Fits(map, anchor, startingOffsets) && startingOffsets.All(offset =>
			{
				var x = 2 * anchor.X + offset.X;
				var y = 2 * anchor.Y + offset.Y;
				return waterDistance[y * map.Width * 2 + x] >= 7;
			});

		public bool ColonyFits(RmgLogicalMap map, string actor, RmgPoint anchor) =>
			Fits(map, anchor, colonies[actor]);

		static bool Fits(RmgLogicalMap map, RmgPoint anchor, IEnumerable<RmgPoint> offsets) =>
			offsets.All(offset =>
			{
				var x = 2 * anchor.X + offset.X;
				var y = 2 * anchor.Y + offset.Y;
				return x >= 0 && y >= 0 && x < 2 * map.Width && y < 2 * map.Height &&
					map.NativeTerrainIntents[4 * ((y / 2) * map.Width + x / 2) + (y % 2) * 2 + x % 2] ==
						RmgNativeTerrainIntent.Clear;
			});

		public IReadOnlyList<string> RunSelfTests(RmgProfile profile)
		{
			var failures = new List<string>();
			var map = new RmgLogicalMap(64, 64);
			var anchor = new RmgPoint(20, 20);
			var waterDistance = Enumerable.Repeat(100, 128 * 128).ToArray();
			if (!StartFits(map, anchor, waterDistance) ||
				colonies.Keys.Any(actor => !ColonyFits(map, actor, anchor)))
				failures.Add("Dirt placement rejected a fully Clear footprint.");
			foreach (var terrain in new[] { RmgNativeTerrainIntent.Rock, RmgNativeTerrainIntent.Vegetation, RmgNativeTerrainIntent.Water })
			{
				foreach (var offset in startingOffsets)
				{
					var x = 2 * anchor.X + offset.X;
					var y = 2 * anchor.Y + offset.Y;
					var index = 4 * ((y / 2) * map.Width + x / 2) + (y % 2) * 2 + x % 2;
					map.NativeTerrainIntents[index] = terrain;
					if (StartFits(map, anchor, waterDistance) || map.NativeTerrainIntents[index] != terrain)
						failures.Add("Start placement accepted or modified a non-dirt footprint cell.");
					map.NativeTerrainIntents[index] = RmgNativeTerrainIntent.Clear;
				}
				foreach (var actor in colonies.Keys)
					foreach (var offset in colonies[actor])
					{
						var x = 2 * anchor.X + offset.X;
						var y = 2 * anchor.Y + offset.Y;
						var index = 4 * ((y / 2) * map.Width + x / 2) + (y % 2) * 2 + x % 2;
						map.NativeTerrainIntents[index] = terrain;
						if (ColonyFits(map, actor, anchor) || map.NativeTerrainIntents[index] != terrain ||
							!ColonyFits(map, actor, new RmgPoint(40, 40)))
							failures.Add("Colony placement did not reject an occupied surface and permit an alternative dirt site.");
						map.NativeTerrainIntents[index] = RmgNativeTerrainIntent.Clear;
					}
			}
			Array.Fill(waterDistance, 6);
			if (StartFits(map, anchor, waterDistance))
				failures.Add("Dirt-only start placement ignored native Water clearance.");
			if (colonies.Keys.Any(actor => ColonyFits(map, actor, new RmgPoint(63, 63))))
				failures.Add("Dirt-only colony placement accepted an out-of-bounds footprint.");
			return failures;
		}
	}
}
