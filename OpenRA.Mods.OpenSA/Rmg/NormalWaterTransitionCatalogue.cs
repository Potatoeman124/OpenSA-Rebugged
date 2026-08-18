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
	public enum RmgNativeTerrainIntent
	{
		Clear,
		Water,
		Rock,
		Vegetation
	}

	public enum RmgShorelineRole
	{
		None,
		Interior,
		EdgeNorth,
		EdgeEast,
		EdgeSouth,
		EdgeWest,
		OuterCornerNorthWest,
		OuterCornerNorthEast,
		OuterCornerSouthEast,
		OuterCornerSouthWest,
		InnerCornerNorthWest,
		InnerCornerNorthEast,
		InnerCornerSouthEast,
		InnerCornerSouthWest,
		FixedDetail,
		Unsupported
	}

	public enum RmgTerrainEdgeSignature
	{
		Land,
		Water
	}

	public enum RmgCardinalDirection
	{
		North,
		East,
		South,
		West
	}

	public sealed class NormalWaterTransition
	{
		readonly RmgNativeTerrainIntent[] nativeTerrain;

		public ushort TemplateId { get; }
		public RmgShorelineRole Role { get; }
		public int Variant { get; }
		public bool ShoreDecoration { get; }
		public bool Permitted { get; }
		public RmgTerrainEdgeSignature North { get; }
		public RmgTerrainEdgeSignature East { get; }
		public RmgTerrainEdgeSignature South { get; }
		public RmgTerrainEdgeSignature West { get; }
		public ushort MirrorHorizontal { get; }
		public ushort MirrorVertical { get; }
		public ushort Rotate180 { get; }
		public IReadOnlyList<RmgNativeTerrainIntent> NativeTerrain => nativeTerrain;

		public NormalWaterTransition(ushort templateId, RmgShorelineRole role, int variant, bool permitted, bool shoreDecoration,
			RmgNativeTerrainIntent[] nativeTerrain, RmgTerrainEdgeSignature north, RmgTerrainEdgeSignature east,
			RmgTerrainEdgeSignature south, RmgTerrainEdgeSignature west, ushort mirrorHorizontal,
			ushort mirrorVertical, ushort rotate180)
		{
			if (nativeTerrain == null || nativeTerrain.Length != 4)
				throw new ArgumentException("A NORMAL transition must declare exactly four native terrain frames.", nameof(nativeTerrain));

			TemplateId = templateId;
			Role = role;
			Variant = variant;
			ShoreDecoration = shoreDecoration;
			Permitted = permitted;
			this.nativeTerrain = nativeTerrain;
			North = north;
			East = east;
			South = south;
			West = west;
			MirrorHorizontal = mirrorHorizontal;
			MirrorVertical = mirrorVertical;
			Rotate180 = rotate180;
		}

		public RmgTerrainEdgeSignature Edge(RmgCardinalDirection direction) => direction switch
		{
			RmgCardinalDirection.North => North,
			RmgCardinalDirection.East => East,
			RmgCardinalDirection.South => South,
			RmgCardinalDirection.West => West,
			_ => throw new ArgumentOutOfRangeException(nameof(direction))
		};
	}

	public static class NormalWaterTransitionCatalogue
	{
		const RmgNativeTerrainIntent C = RmgNativeTerrainIntent.Clear;
		const RmgNativeTerrainIntent W = RmgNativeTerrainIntent.Water;
		const RmgTerrainEdgeSignature L = RmgTerrainEdgeSignature.Land;
		const RmgTerrainEdgeSignature A = RmgTerrainEdgeSignature.Water;

		static readonly NormalWaterTransition[] AllTransitions =
		{
			T(0, RmgShorelineRole.OuterCornerNorthWest, 0, new[] { C, W, W, W }, L, A, A, L, 16, 2, 18),
			T(3, RmgShorelineRole.OuterCornerNorthWest, 1, new[] { C, W, W, W }, L, A, A, L, 19, 5, 21),
			T(2, RmgShorelineRole.OuterCornerNorthEast, 0, new[] { W, C, W, W }, L, L, A, A, 18, 0, 16),
			T(5, RmgShorelineRole.OuterCornerNorthEast, 1, new[] { W, C, W, W }, L, L, A, A, 21, 3, 19),
			T(18, RmgShorelineRole.OuterCornerSouthEast, 0, new[] { W, W, W, C }, A, L, L, A, 2, 16, 0),
			T(21, RmgShorelineRole.OuterCornerSouthEast, 1, new[] { W, W, W, C }, A, L, L, A, 5, 19, 3),
			T(16, RmgShorelineRole.OuterCornerSouthWest, 0, new[] { W, W, C, W }, A, A, L, L, 0, 18, 2),
			T(19, RmgShorelineRole.OuterCornerSouthWest, 1, new[] { W, W, C, W }, A, A, L, L, 3, 21, 5),

			T(1, RmgShorelineRole.EdgeNorth, 0, Water(), L, A, A, A, 17, 1, 17),
			T(4, RmgShorelineRole.EdgeNorth, 1, Water(), L, A, A, A, 20, 4, 20, shoreDecoration: true),
			T(10, RmgShorelineRole.EdgeEast, 0, Water(), A, L, A, A, 10, 8, 8, shoreDecoration: true),
			T(13, RmgShorelineRole.EdgeEast, 1, Water(), A, L, A, A, 13, 11, 11),
			T(17, RmgShorelineRole.EdgeSouth, 0, Water(), A, A, L, A, 1, 17, 1, shoreDecoration: true),
			T(20, RmgShorelineRole.EdgeSouth, 1, Water(), A, A, L, A, 4, 20, 4),
			T(8, RmgShorelineRole.EdgeWest, 0, Water(), A, A, A, L, 8, 10, 10, shoreDecoration: true),
			T(11, RmgShorelineRole.EdgeWest, 1, Water(), A, A, A, L, 11, 13, 13),

			T(15, RmgShorelineRole.InnerCornerNorthWest, 0, Water(), A, A, A, A, 7, 14, 6),
			T(31, RmgShorelineRole.InnerCornerNorthWest, 1, Water(), A, A, A, A, 23, 30, 22),
			T(14, RmgShorelineRole.InnerCornerNorthEast, 0, Water(), A, A, A, A, 6, 15, 7),
			T(30, RmgShorelineRole.InnerCornerNorthEast, 1, Water(), A, A, A, A, 22, 31, 23, shoreDecoration: true),
			T(6, RmgShorelineRole.InnerCornerSouthEast, 0, Water(), A, A, A, A, 14, 7, 15),
			T(22, RmgShorelineRole.InnerCornerSouthEast, 1, Water(), A, A, A, A, 30, 23, 31),
			T(7, RmgShorelineRole.InnerCornerSouthWest, 0, Water(), A, A, A, A, 15, 6, 14),
			T(23, RmgShorelineRole.InnerCornerSouthWest, 1, Water(), A, A, A, A, 31, 22, 30),

			T(24, RmgShorelineRole.FixedDetail, 0, Water(), A, A, A, A, 24, 24, 24, false),
			T(25, RmgShorelineRole.FixedDetail, 1, Water(), A, A, A, A, 25, 25, 25, false),
			T(27, RmgShorelineRole.FixedDetail, 2, Water(), A, A, A, A, 27, 27, 27, false)
		};

		static readonly IReadOnlyDictionary<ushort, NormalWaterTransition> ById =
			AllTransitions.ToDictionary(transition => transition.TemplateId);
		static readonly IReadOnlyDictionary<RmgShorelineRole, NormalWaterTransition[]> ByRole = AllTransitions
			.Where(transition => transition.Permitted)
			.GroupBy(transition => transition.Role)
			.ToDictionary(group => group.Key, group => group.OrderBy(transition => transition.Variant).ToArray());

		public static IReadOnlyList<NormalWaterTransition> Entries => AllTransitions;
		public static IReadOnlyCollection<ushort> PermittedTemplateIds => ById.Values
			.Where(transition => transition.Permitted).Select(transition => transition.TemplateId).ToArray();
		public static IReadOnlyCollection<ushort> OpenWaterDetailTemplateIds => ById.Values
			.Where(transition => transition.Role == RmgShorelineRole.FixedDetail).Select(transition => transition.TemplateId).OrderBy(id => id).ToArray();

		public static bool TryGet(ushort templateId, out NormalWaterTransition transition) => ById.TryGetValue(templateId, out transition);

		public static NormalWaterTransition ForRole(RmgShorelineRole role, int variant)
		{
			if (!ByRole.TryGetValue(role, out var transitions) || variant < 0 || variant >= transitions.Length)
				throw new RmgGenerationRejectedException("TERRAIN_MATERIALIZATION_ROLE",
					$"NORMAL shoreline role {role} does not provide variant {variant}.");

			return transitions[variant];
		}

		public static int VariantCount(RmgShorelineRole role) =>
			ByRole.TryGetValue(role, out var transitions) ? transitions.Length : 0;

		public static ushort Transform(ushort templateId, RmgSymmetry symmetry)
		{
			if (!ById.TryGetValue(templateId, out var transition))
				throw new ArgumentException($"Template {templateId} is not a fixed NORMAL Water transition.", nameof(templateId));

			return symmetry switch
			{
				RmgSymmetry.MirrorHorizontal => transition.MirrorHorizontal,
				RmgSymmetry.MirrorVertical => transition.MirrorVertical,
				RmgSymmetry.Rotate180 => transition.Rotate180,
				_ => throw new ArgumentOutOfRangeException(nameof(symmetry))
			};
		}

		public static RmgShorelineRole TransformRole(RmgShorelineRole role, RmgSymmetry symmetry)
		{
			var transition = ForRole(role, 0);
			var transformedId = Transform(transition.TemplateId, symmetry);
			return ById[transformedId].Role;
		}

		public static RmgCardinalDirection TransformDirection(RmgCardinalDirection direction, RmgSymmetry symmetry) =>
			(direction, symmetry) switch
			{
				(RmgCardinalDirection.North, RmgSymmetry.MirrorHorizontal) => RmgCardinalDirection.South,
				(RmgCardinalDirection.South, RmgSymmetry.MirrorHorizontal) => RmgCardinalDirection.North,
				(RmgCardinalDirection.East, RmgSymmetry.MirrorVertical) => RmgCardinalDirection.West,
				(RmgCardinalDirection.West, RmgSymmetry.MirrorVertical) => RmgCardinalDirection.East,
				(RmgCardinalDirection.North, RmgSymmetry.Rotate180) => RmgCardinalDirection.South,
				(RmgCardinalDirection.East, RmgSymmetry.Rotate180) => RmgCardinalDirection.West,
				(RmgCardinalDirection.South, RmgSymmetry.Rotate180) => RmgCardinalDirection.North,
				(RmgCardinalDirection.West, RmgSymmetry.Rotate180) => RmgCardinalDirection.East,
				_ => direction
			};

		public static int TransformFrame(int frame, RmgSymmetry symmetry) => (frame, symmetry) switch
		{
			(0, RmgSymmetry.MirrorHorizontal) => 2,
			(1, RmgSymmetry.MirrorHorizontal) => 3,
			(2, RmgSymmetry.MirrorHorizontal) => 0,
			(3, RmgSymmetry.MirrorHorizontal) => 1,
			(0, RmgSymmetry.MirrorVertical) => 1,
			(1, RmgSymmetry.MirrorVertical) => 0,
			(2, RmgSymmetry.MirrorVertical) => 3,
			(3, RmgSymmetry.MirrorVertical) => 2,
			(0, RmgSymmetry.Rotate180) => 3,
			(1, RmgSymmetry.Rotate180) => 2,
			(2, RmgSymmetry.Rotate180) => 1,
			(3, RmgSymmetry.Rotate180) => 0,
			_ => throw new ArgumentOutOfRangeException(nameof(frame))
		};

		public static IReadOnlyList<string> RunSelfTests()
		{
			var failures = new List<string>();
			var expectedFixedIds = Enumerable.Range(0, 32).Select(id => (ushort)id)
				.Except(new ushort[] { 9, 12, 26, 28, 29 }).OrderBy(id => id).ToArray();
			if (!expectedFixedIds.SequenceEqual(AllTransitions.Select(transition => transition.TemplateId).OrderBy(id => id)))
				failures.Add("The NORMAL fixed-Water catalogue does not cover exactly the 27 audited templates.");
			if (AllTransitions.Count(transition => transition.Permitted) != 24)
				failures.Add("The NORMAL fixed-Water catalogue must permit exactly 24 shoreline transitions.");
			var expectedDecoratedIds = new ushort[] { 4, 8, 10, 17, 30 };
			if (!expectedDecoratedIds.SequenceEqual(AllTransitions.Where(transition => transition.ShoreDecoration)
				.Select(transition => transition.TemplateId).OrderBy(id => id)))
				failures.Add("The NORMAL shoreline-decoration allow-list changed without a visual contract revision.");
			var expectedDetailIds = new ushort[] { 24, 25, 27 };
			if (!expectedDetailIds.SequenceEqual(OpenWaterDetailTemplateIds))
				failures.Add("The NORMAL open-Water detail allow-list changed without a visual contract revision.");
			foreach (var role in Enum.GetValues<RmgShorelineRole>().Where(role => role >= RmgShorelineRole.EdgeNorth && role <= RmgShorelineRole.InnerCornerSouthWest))
				if (VariantCount(role) != 2)
					failures.Add($"Shoreline role {role} does not expose exactly two deterministic variants.");

			foreach (var transition in AllTransitions)
				foreach (var symmetry in Enum.GetValues<RmgSymmetry>())
				{
					var transformedId = Transform(transition.TemplateId, symmetry);
					if (!ById.TryGetValue(transformedId, out var transformed))
					{
						failures.Add($"Template {transition.TemplateId} transforms to missing template {transformedId} under {symmetry}.");
						continue;
					}

					if (Transform(transformedId, symmetry) != transition.TemplateId)
						failures.Add($"Template transform {transition.TemplateId}/{symmetry} is not an involution.");
					for (var frame = 0; frame < 4; frame++)
						if (transition.NativeTerrain[frame] != transformed.NativeTerrain[TransformFrame(frame, symmetry)])
							failures.Add($"Template {transition.TemplateId}/{symmetry} changes native terrain semantics at frame {frame}.");
					foreach (var direction in Enum.GetValues<RmgCardinalDirection>())
						if (transition.Edge(direction) != transformed.Edge(TransformDirection(direction, symmetry)))
							failures.Add($"Template {transition.TemplateId}/{symmetry} changes the {direction} visual edge signature.");
				}

			return failures;
		}

		static RmgNativeTerrainIntent[] Water() => new[] { W, W, W, W };

		static NormalWaterTransition T(ushort id, RmgShorelineRole role, int variant, RmgNativeTerrainIntent[] native,
			RmgTerrainEdgeSignature north, RmgTerrainEdgeSignature east, RmgTerrainEdgeSignature south,
			RmgTerrainEdgeSignature west, ushort mirrorHorizontal, ushort mirrorVertical, ushort rotate180,
			bool permitted = true, bool shoreDecoration = false) => new(id, role, variant, permitted, shoreDecoration, native,
			north, east, south, west, mirrorHorizontal, mirrorVertical, rotate180);
	}
}
