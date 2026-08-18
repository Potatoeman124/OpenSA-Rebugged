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
	public static class RmgClearLandDetailMaterializer
	{
		public static void Materialize(RmgLogicalMap map, RmgProfile profile, RmgGenerationSettings settings)
		{
			if (!profile.UsesClearLandDetails)
				return;

			var sideA = new List<int>();
			var sideB = new List<int>();
			var fixedPoints = new List<int>();
			var excludedProtected = 0;
			for (var y = 0; y < map.Height; y++)
				for (var x = 0; x < map.Width; x++)
				{
					var point = new RmgPoint(x, y);
					var index = map.Index(point);
					if (map.Obstacles[index])
						continue;

					if (Enumerable.Range(0, 4).Any(frame => map.NativeTerrainIntents[4 * index + frame] != RmgNativeTerrainIntent.Clear))
						continue;

					if (IsProtected(map, index))
					{
						excludedProtected++;
						continue;
					}

					if (!profile.ClearTemplateIds.Contains(map.TemplateIds[index]))
						throw new RmgGenerationRejectedException("CLEAR_DETAIL_BASE_TEMPLATE",
							$"Eligible Clear stamp {point} uses unexpected base template {map.TemplateIds[index]}.");

					var partner = RmgGenerator.Transform(point, settings.Symmetry, map.Width, map.Height);
					var partnerIndex = map.Index(partner);
					if (index < partnerIndex)
						sideA.Add(index);
					else if (index > partnerIndex)
						sideB.Add(index);
					else if (index == partnerIndex)
						fixedPoints.Add(index);
				}

			var eligibleCount = sideA.Count + sideB.Count + fixedPoints.Count;
			var targetCount = (eligibleCount * profile.ClearLandDetailPercent + 50) / 100;
			var random = DeterministicRandom.ForStream(settings, profile, "terrain-clear-land-details");
			Shuffle(sideA, random);
			Shuffle(sideB, random);
			Shuffle(fixedPoints, random);

			var fixedTarget = Math.Min(fixedPoints.Count, targetCount);
			var pairedTarget = targetCount - fixedTarget;
			var sideATarget = pairedTarget / 2;
			var sideBTarget = pairedTarget - sideATarget;
			if ((pairedTarget & 1) != 0 && random.NextInt(2) == 0)
				(sideATarget, sideBTarget) = (sideBTarget, sideATarget);

			if (sideATarget > sideA.Count || sideBTarget > sideB.Count)
				throw new RmgGenerationRejectedException("CLEAR_DETAIL_SELECTION_CAPACITY",
					$"Cannot select {targetCount} Clear details from {eligibleCount} symmetry-balanced candidates.");

			var selected = fixedPoints.Take(fixedTarget)
				.Concat(sideA.Take(sideATarget))
				.Concat(sideB.Take(sideBTarget))
				.OrderBy(index => index)
				.ToArray();
			foreach (var index in selected)
			{
				for (var frame = 0; frame < 4; frame++)
					if (map.NativeTerrainIntents[4 * index + frame] != RmgNativeTerrainIntent.Clear)
						throw new RmgGenerationRejectedException("CLEAR_DETAIL_NATIVE_TERRAIN",
							$"Clear detail candidate {index} frame {frame} is not Clear before decoration.");

				map.TemplateIds[index] = profile.ClearLandDetailTemplateIds[random.NextInt(profile.ClearLandDetailTemplateIds.Length)];
			}

			map.ClearLandDetailEligibleCount = eligibleCount;
			map.ClearLandDetailExcludedProtectedCount = excludedProtected;
			map.ClearLandDetailTargetCount = targetCount;
			map.ClearLandDetailSelectedCount = selected.Length;
			map.ClearLandDetailSymmetrySideACount = sideATarget;
			map.ClearLandDetailSymmetrySideBCount = sideBTarget;
		}

		public static bool IsProtected(RmgLogicalMap map, int index) =>
			map.StartReservations[index] || map.StructureReservations[index] || map.StrategicRegions[index] ||
			map.RouteMasks[index] != 0 || map.ChokepointIds[index] >= 0 || map.RepairChanges[index];

		public static string SelectionHash(RmgLogicalMap map, RmgProfile profile)
		{
			var details = profile.ClearLandDetailTemplateIds.ToHashSet();
			var text = string.Join("\n", map.TemplateIds.Select((template, index) => (template, index))
				.Where(entry => details.Contains(entry.template))
				.Select(entry => $"{entry.index}:{entry.template}"));
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
		}

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
