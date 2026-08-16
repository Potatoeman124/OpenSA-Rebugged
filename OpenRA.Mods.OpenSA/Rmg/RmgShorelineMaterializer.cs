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
	public static class RmgShorelineMaterializer
	{
		static readonly (int X, int Y, RmgShorelineRole Role)[] Diagonals =
		{
			(-1, -1, RmgShorelineRole.InnerCornerNorthWest),
			(1, -1, RmgShorelineRole.InnerCornerNorthEast),
			(1, 1, RmgShorelineRole.InnerCornerSouthEast),
			(-1, 1, RmgShorelineRole.InnerCornerSouthWest)
		};

		public static void Materialize(RmgLogicalMap map, RmgProfile profile, RmgGenerationSettings settings)
		{
			var unsupported = FindUnsupported(map).ToArray();
			map.ShorelineUnsupportedNeighborhoodCount = unsupported.Length;
			if (unsupported.Length > 0)
			{
				var samples = string.Join("; ", unsupported.Take(8).Select(sample =>
					$"{sample.Point}:cardinal=0x{sample.CardinalMask:X},diagonal-clear={sample.DiagonalClearCount}"));
				throw new RmgGenerationRejectedException("TERRAIN_MATERIALIZATION_UNSUPPORTED_NEIGHBORHOOD",
					$"NORMAL shoreline materialization found {unsupported.Length} unsupported Water neighborhoods. {samples}");
			}

			var clearRandom = DeterministicRandom.ForStream(settings, profile, "terrain-clear-variants");
			var interiorRandom = DeterministicRandom.ForStream(settings, profile, "terrain-water-interiors");
			var shorelineRandom = DeterministicRandom.ForStream(settings, profile, "terrain-shoreline-variants");
			for (var y = 0; y < map.Height; y++)
				for (var x = 0; x < map.Width; x++)
				{
					var point = new RmgPoint(x, y);
					var partner = RmgGenerator.Transform(point, settings.Symmetry, map.Width, map.Height);
					if (!IsCanonical(point, partner, map.Width))
						continue;

					var index = map.Index(point);
					var partnerIndex = map.Index(partner);
					if (!map.Obstacles[index])
					{
						var template = profile.ClearTemplateIds[clearRandom.NextInt(profile.ClearTemplateIds.Length)];
						Assign(map, index, template, RmgShorelineRole.None, Clear());
						Assign(map, partnerIndex, template, RmgShorelineRole.None, Clear());
						continue;
					}

					var role = Classify(map, point, out _, out _);
					var partnerRole = Classify(map, partner, out _, out _);
					if (role == RmgShorelineRole.Interior)
					{
						var template = SelectInteriorTemplate(profile, interiorRandom);
						var partnerTemplate = SelectInteriorTemplate(profile, interiorRandom);
						Assign(map, index, template, role, Water());
						Assign(map, partnerIndex, partnerTemplate, role, Water());
						continue;
					}

					var transformedRole = NormalWaterTransitionCatalogue.TransformRole(role, settings.Symmetry);
					if (partnerRole != transformedRole)
						throw new RmgGenerationRejectedException("TERRAIN_MATERIALIZATION_SYMMETRY",
							$"Shoreline role {role} at {point} transforms to {transformedRole}, but {partner} classified as {partnerRole}.");

					var transition = SelectShorelineTransition(role, profile, shorelineRandom);
					var partnerTransition = SelectShorelineTransition(partnerRole, profile, shorelineRandom);

					Assign(map, index, transition.TemplateId, role, transition.NativeTerrain);
					Assign(map, partnerIndex, partnerTransition.TemplateId, partnerRole, partnerTransition.NativeTerrain);
				}

			ValidateEdges(map);
		}

		public static IReadOnlyList<string> RunSelfTests()
		{
			var failures = new List<string>();
			foreach (var (mask, role) in new[]
			{
				(1, RmgShorelineRole.EdgeNorth), (2, RmgShorelineRole.EdgeEast),
				(4, RmgShorelineRole.EdgeSouth), (8, RmgShorelineRole.EdgeWest),
				(9, RmgShorelineRole.OuterCornerNorthWest), (3, RmgShorelineRole.OuterCornerNorthEast),
				(6, RmgShorelineRole.OuterCornerSouthEast), (12, RmgShorelineRole.OuterCornerSouthWest)
			})
			{
				var map = Fixture(mask, -1);
				if (Classify(map, new RmgPoint(1, 1), out _, out _) != role)
					failures.Add($"Cardinal shoreline fixture 0x{mask:X} did not classify as {role}.");
			}

			for (var diagonal = 0; diagonal < Diagonals.Length; diagonal++)
			{
				var map = Fixture(0, diagonal);
				if (Classify(map, new RmgPoint(1, 1), out _, out _) != Diagonals[diagonal].Role)
					failures.Add($"Diagonal shoreline fixture {diagonal} did not classify as {Diagonals[diagonal].Role}.");
			}

			if (Classify(Fixture(5, -1), new RmgPoint(1, 1), out _, out _) != RmgShorelineRole.Unsupported)
				failures.Add("Opposite-edge shoreline fixture was not rejected.");
			if (Classify(Fixture(0, -2), new RmgPoint(1, 1), out _, out _) != RmgShorelineRole.Unsupported)
				failures.Add("Multiple-inner-corner shoreline fixture was not rejected.");
			var twoByTwo = new RmgLogicalMap(4, 4);
			for (var y = 1; y <= 2; y++)
				for (var x = 1; x <= 2; x++)
					twoByTwo.Obstacles[twoByTwo.Index(new RmgPoint(x, y))] = true;
			if (Enumerable.Range(0, twoByTwo.Obstacles.Length).Where(i => twoByTwo.Obstacles[i])
				.Any(i => Classify(twoByTwo, new RmgPoint(i % twoByTwo.Width, i / twoByTwo.Width), out _, out _) == RmgShorelineRole.Unsupported))
				failures.Add("A two-logical-cell-thick Water region was incorrectly rejected by the shoreline classifier.");

			return failures;
		}

		public static RmgShorelineRole Classify(RmgLogicalMap map, RmgPoint point, out int cardinalMask,
			out int diagonalClearCount)
		{
			if (!map.Contains(point) || !map.Obstacles[map.Index(point)])
			{
				cardinalMask = 0;
				diagonalClearCount = 0;
				return RmgShorelineRole.None;
			}

			cardinalMask = 0;
			if (Clear(map, point.X, point.Y - 1))
				cardinalMask |= 1;
			if (Clear(map, point.X + 1, point.Y))
				cardinalMask |= 2;
			if (Clear(map, point.X, point.Y + 1))
				cardinalMask |= 4;
			if (Clear(map, point.X - 1, point.Y))
				cardinalMask |= 8;

			var clearDiagonals = Diagonals.Where(diagonal => Clear(map, point.X + diagonal.X, point.Y + diagonal.Y)).ToArray();
			diagonalClearCount = clearDiagonals.Length;
			return cardinalMask switch
			{
				0 when diagonalClearCount == 0 => RmgShorelineRole.Interior,
				0 when diagonalClearCount == 1 => clearDiagonals[0].Role,
				1 => RmgShorelineRole.EdgeNorth,
				2 => RmgShorelineRole.EdgeEast,
				4 => RmgShorelineRole.EdgeSouth,
				8 => RmgShorelineRole.EdgeWest,
				9 => RmgShorelineRole.OuterCornerNorthWest,
				3 => RmgShorelineRole.OuterCornerNorthEast,
				6 => RmgShorelineRole.OuterCornerSouthEast,
				12 => RmgShorelineRole.OuterCornerSouthWest,
				_ => RmgShorelineRole.Unsupported
			};
		}

		public static RmgTerrainEdgeSignature Edge(RmgLogicalMap map, int index, RmgCardinalDirection direction)
		{
			if (!map.Obstacles[index])
				return RmgTerrainEdgeSignature.Land;
			if (map.ShorelineRoles[index] == RmgShorelineRole.Interior)
				return RmgTerrainEdgeSignature.Water;
			if (!NormalWaterTransitionCatalogue.TryGet(map.TemplateIds[index], out var transition))
				throw new RmgGenerationRejectedException("TERRAIN_MATERIALIZATION_TEMPLATE",
					$"Water cell {index} uses template {map.TemplateIds[index]}, which is not a permitted fixed transition or interior.");

			return transition.Edge(direction);
		}

		static IEnumerable<(RmgPoint Point, int CardinalMask, int DiagonalClearCount)> FindUnsupported(RmgLogicalMap map)
		{
			for (var y = 0; y < map.Height; y++)
				for (var x = 0; x < map.Width; x++)
				{
					var point = new RmgPoint(x, y);
					if (Classify(map, point, out var cardinal, out var diagonal) == RmgShorelineRole.Unsupported)
						yield return (point, cardinal, diagonal);
				}
		}

		static void ValidateEdges(RmgLogicalMap map)
		{
			for (var y = 0; y < map.Height; y++)
				for (var x = 0; x < map.Width; x++)
				{
					var index = map.Index(new RmgPoint(x, y));
					if (x + 1 < map.Width)
						Compatible(index, map.Index(new RmgPoint(x + 1, y)), RmgCardinalDirection.East, RmgCardinalDirection.West);
					if (y + 1 < map.Height)
						Compatible(index, map.Index(new RmgPoint(x, y + 1)), RmgCardinalDirection.South, RmgCardinalDirection.North);
				}

			void Compatible(int first, int second, RmgCardinalDirection firstDirection, RmgCardinalDirection secondDirection)
			{
				var firstEdge = Edge(map, first, firstDirection);
				var secondEdge = Edge(map, second, secondDirection);
				if (firstEdge != secondEdge)
					throw new RmgGenerationRejectedException("TERRAIN_MATERIALIZATION_EDGE_MISMATCH",
						$"Logical cells {first} and {second} expose incompatible {firstEdge}/{secondEdge} edges.");
			}
		}

		static ushort SelectInteriorTemplate(RmgProfile profile, DeterministicRandom random)
		{
			var useDetail = profile.OpenWaterDetailTemplateIds.Length > 0 &&
				random.NextInt(100) < profile.OpenWaterDetailPercent;
			var candidates = useDetail ? profile.OpenWaterDetailTemplateIds : profile.BlockedTemplateIds;
			return candidates[random.NextInt(candidates.Length)];
		}

		static NormalWaterTransition SelectShorelineTransition(RmgShorelineRole role, RmgProfile profile,
			DeterministicRandom random)
		{
			var candidates = NormalWaterTransitionCatalogue.Entries
				.Where(transition => transition.Permitted && transition.Role == role).ToArray();
			var decorated = candidates.Where(transition => transition.ShoreDecoration).ToArray();
			var plain = candidates.Where(transition => !transition.ShoreDecoration).ToArray();
			var useDecoration = decorated.Length > 0 && random.NextInt(100) < profile.ShorelineDecorationPercent;
			var eligible = useDecoration ? decorated : plain;
			if (eligible.Length == 0)
				throw new RmgGenerationRejectedException("TERRAIN_MATERIALIZATION_VISUAL_VARIANT",
					$"Shoreline role {role} has no {(useDecoration ? "decorated" : "plain")} visual variant.");

			return eligible[random.NextInt(eligible.Length)];
		}

		static void Assign(RmgLogicalMap map, int index, ushort templateId, RmgShorelineRole role,
			IReadOnlyList<RmgNativeTerrainIntent> nativeTerrain)
		{
			map.TemplateIds[index] = templateId;
			map.ShorelineRoles[index] = role;
			for (var frame = 0; frame < 4; frame++)
				map.NativeTerrainIntents[4 * index + frame] = nativeTerrain[frame];
		}

		static bool Clear(RmgLogicalMap map, int x, int y) =>
			x < 0 || x >= map.Width || y < 0 || y >= map.Height || !map.Obstacles[map.Index(new RmgPoint(x, y))];

		static bool IsCanonical(RmgPoint point, RmgPoint partner, int width) =>
			point.Y * width + point.X <= partner.Y * width + partner.X;

		static RmgNativeTerrainIntent[] Clear() => Enumerable.Repeat(RmgNativeTerrainIntent.Clear, 4).ToArray();
		static RmgNativeTerrainIntent[] Water() => Enumerable.Repeat(RmgNativeTerrainIntent.Water, 4).ToArray();

		static RmgLogicalMap Fixture(int cardinalClearMask, int diagonalClear)
		{
			var map = new RmgLogicalMap(3, 3);
			Array.Fill(map.Obstacles, true);
			var center = new RmgPoint(1, 1);
			var cardinals = new[] { (0, -1), (1, 0), (0, 1), (-1, 0) };
			for (var i = 0; i < cardinals.Length; i++)
				if ((cardinalClearMask & (1 << i)) != 0)
					map.Obstacles[map.Index(new RmgPoint(center.X + cardinals[i].Item1, center.Y + cardinals[i].Item2))] = false;
			if (diagonalClear >= 0)
			{
				var (diagonalX, diagonalY, _) = Diagonals[diagonalClear];
				map.Obstacles[map.Index(new RmgPoint(center.X + diagonalX, center.Y + diagonalY))] = false;
			}
			else if (diagonalClear == -2)
			{
				foreach (var (diagonalX, diagonalY, _) in Diagonals.Take(2))
					map.Obstacles[map.Index(new RmgPoint(center.X + diagonalX, center.Y + diagonalY))] = false;
			}

			return map;
		}
	}
}
