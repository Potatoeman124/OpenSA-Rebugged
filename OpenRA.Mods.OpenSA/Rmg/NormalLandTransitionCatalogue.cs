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
	public enum RmgLandTemplateBank
	{
		ClearRock,
		RockVegetation
	}

	public enum RmgLandTemplateUse
	{
		Transition,
		Interior,
		FixedDetail,
		OtherSurfaceDetail
	}

	public enum RmgLandTransitionRole
	{
		None,
		CornerNorthWest,
		CornerNorthEast,
		CornerSouthWest,
		CornerSouthEast,
		EdgeNorth,
		EdgeWest,
		EdgeEast,
		EdgeSouth,
		InnerCornerNorthWest,
		InnerCornerNorthEast,
		InnerCornerSouthWest,
		InnerCornerSouthEast,
		Unsupported
	}

	public sealed class NormalLandTemplate
	{
		readonly RmgNativeTerrainIntent[] nativeTerrain;

		public ushort TemplateId { get; }
		public int BankLocalIndex { get; }
		public RmgLandTemplateBank Bank { get; }
		public RmgLandTemplateUse Use { get; }
		public bool PickAny { get; }
		public bool Permitted { get; }
		public IReadOnlyList<RmgNativeTerrainIntent> NativeTerrain => nativeTerrain;
		public RmgNativeTerrainIntent HighTerrain => Bank == RmgLandTemplateBank.ClearRock ?
			RmgNativeTerrainIntent.Rock : RmgNativeTerrainIntent.Vegetation;
		public int SemanticMask => NormalLandTransitionCatalogue.SemanticMask(nativeTerrain, HighTerrain);
		public RmgLandTransitionRole Role => NormalLandTransitionCatalogue.RoleForMask(SemanticMask);

		public NormalLandTemplate(ushort templateId, int bankLocalIndex, RmgLandTemplateBank bank,
			RmgLandTemplateUse use, bool pickAny, bool permitted, params RmgNativeTerrainIntent[] nativeTerrain)
		{
			if (nativeTerrain == null || nativeTerrain.Length != 4)
				throw new ArgumentException("A NORMAL land template must declare exactly four native terrain frames.", nameof(nativeTerrain));

			TemplateId = templateId;
			BankLocalIndex = bankLocalIndex;
			Bank = bank;
			Use = use;
			PickAny = pickAny;
			Permitted = permitted;
			this.nativeTerrain = nativeTerrain;
		}
	}

	public static class NormalLandTransitionCatalogue
	{
		const RmgNativeTerrainIntent C = RmgNativeTerrainIntent.Clear;
		const RmgNativeTerrainIntent R = RmgNativeTerrainIntent.Rock;
		const RmgNativeTerrainIntent V = RmgNativeTerrainIntent.Vegetation;

		static readonly NormalLandTemplate[] AllTemplates =
		{
			T(37, 0, RmgLandTemplateBank.ClearRock, C, C, C, R),
			T(38, 1, RmgLandTemplateBank.ClearRock, C, C, R, R),
			T(39, 2, RmgLandTemplateBank.ClearRock, C, C, R, C),
			T(40, 3, RmgLandTemplateBank.ClearRock, C, C, C, R),
			T(41, 4, RmgLandTemplateBank.ClearRock, C, C, R, R),
			T(42, 5, RmgLandTemplateBank.ClearRock, C, C, R, C),
			T(43, 6, RmgLandTemplateBank.ClearRock, R, R, R, C),
			T(44, 7, RmgLandTemplateBank.ClearRock, R, R, C, R),
			T(45, 8, RmgLandTemplateBank.ClearRock, C, R, C, R),
			I(46, 9, RmgLandTemplateBank.ClearRock, R),
			T(47, 10, RmgLandTemplateBank.ClearRock, R, C, R, C),
			T(48, 11, RmgLandTemplateBank.ClearRock, C, R, C, R),
			I(49, 12, RmgLandTemplateBank.ClearRock, R),
			T(50, 13, RmgLandTemplateBank.ClearRock, R, C, R, C),
			T(51, 14, RmgLandTemplateBank.ClearRock, R, C, R, R),
			T(52, 15, RmgLandTemplateBank.ClearRock, C, R, R, R),
			T(53, 16, RmgLandTemplateBank.ClearRock, C, R, C, C),
			T(54, 17, RmgLandTemplateBank.ClearRock, R, R, C, C),
			T(55, 18, RmgLandTemplateBank.ClearRock, R, C, C, C),
			T(56, 19, RmgLandTemplateBank.ClearRock, C, R, C, C),
			T(57, 20, RmgLandTemplateBank.ClearRock, R, R, C, C),
			T(58, 21, RmgLandTemplateBank.ClearRock, R, C, C, C),
			T(59, 22, RmgLandTemplateBank.ClearRock, R, R, R, C),
			T(60, 23, RmgLandTemplateBank.ClearRock, R, R, C, R),
			D(61, 24, RmgLandTemplateBank.ClearRock, RmgLandTemplateUse.OtherSurfaceDetail, C),
			D(62, 25, RmgLandTemplateBank.ClearRock, RmgLandTemplateUse.OtherSurfaceDetail, C),
			I(63, 26, RmgLandTemplateBank.ClearRock, R),
			I(64, 27, RmgLandTemplateBank.ClearRock, R),
			I(65, 28, RmgLandTemplateBank.ClearRock, R),
			I(66, 29, RmgLandTemplateBank.ClearRock, R),
			T(67, 30, RmgLandTemplateBank.ClearRock, R, C, R, R),
			T(68, 31, RmgLandTemplateBank.ClearRock, C, R, R, R),

			T(69, 0, RmgLandTemplateBank.RockVegetation, V, V, R, V),
			T(70, 1, RmgLandTemplateBank.RockVegetation, R, R, V, V),
			T(71, 2, RmgLandTemplateBank.RockVegetation, R, R, V, R),
			T(72, 3, RmgLandTemplateBank.RockVegetation, R, R, R, V),
			T(73, 4, RmgLandTemplateBank.RockVegetation, R, R, V, V),
			T(74, 5, RmgLandTemplateBank.RockVegetation, R, R, V, R),
			T(75, 6, RmgLandTemplateBank.RockVegetation, V, V, V, R),
			T(76, 7, RmgLandTemplateBank.RockVegetation, V, V, R, V),
			T(77, 8, RmgLandTemplateBank.RockVegetation, R, V, R, V),
			I(78, 9, RmgLandTemplateBank.RockVegetation, V),
			T(79, 10, RmgLandTemplateBank.RockVegetation, V, R, V, R),
			T(80, 11, RmgLandTemplateBank.RockVegetation, R, V, R, V),
			I(81, 12, RmgLandTemplateBank.RockVegetation, V),
			T(82, 13, RmgLandTemplateBank.RockVegetation, V, R, V, R),
			T(83, 14, RmgLandTemplateBank.RockVegetation, V, R, V, V),
			T(84, 15, RmgLandTemplateBank.RockVegetation, R, V, V, V),
			T(85, 16, RmgLandTemplateBank.RockVegetation, R, V, R, R),
			T(86, 17, RmgLandTemplateBank.RockVegetation, V, V, R, R),
			T(87, 18, RmgLandTemplateBank.RockVegetation, V, R, R, R),
			T(88, 19, RmgLandTemplateBank.RockVegetation, R, V, R, R),
			T(89, 20, RmgLandTemplateBank.RockVegetation, V, V, R, R),
			T(90, 21, RmgLandTemplateBank.RockVegetation, V, R, R, R),
			T(91, 22, RmgLandTemplateBank.RockVegetation, V, V, V, R),
			T(92, 23, RmgLandTemplateBank.RockVegetation, V, V, R, V),
			D(93, 24, RmgLandTemplateBank.RockVegetation, RmgLandTemplateUse.FixedDetail, V),
			D(94, 25, RmgLandTemplateBank.RockVegetation, RmgLandTemplateUse.OtherSurfaceDetail, R),
			I(95, 26, RmgLandTemplateBank.RockVegetation, V),
			I(96, 27, RmgLandTemplateBank.RockVegetation, V),
			I(97, 28, RmgLandTemplateBank.RockVegetation, V),
			I(98, 29, RmgLandTemplateBank.RockVegetation, V),
			T(99, 30, RmgLandTemplateBank.RockVegetation, V, R, V, V),
			T(100, 31, RmgLandTemplateBank.RockVegetation, R, V, V, V)
		};

		static readonly IReadOnlyDictionary<ushort, NormalLandTemplate> ById =
			AllTemplates.ToDictionary(template => template.TemplateId);

		public static IReadOnlyList<NormalLandTemplate> Entries => AllTemplates;
		public static IReadOnlyCollection<ushort> LandMaterializationTemplateIds => AllTemplates
			.Where(template => template.Permitted).Select(template => template.TemplateId).OrderBy(id => id).ToArray();
		public static IReadOnlyCollection<ushort> RockInteriorTemplateIds => AllTemplates
			.Where(template => template.Bank == RmgLandTemplateBank.ClearRock && template.Use == RmgLandTemplateUse.Interior)
			.Select(template => template.TemplateId).OrderBy(id => id).ToArray();
		public static IReadOnlyCollection<ushort> VegetationInteriorTemplateIds => AllTemplates
			.Where(template => template.Bank == RmgLandTemplateBank.RockVegetation && template.Use == RmgLandTemplateUse.Interior)
			.Select(template => template.TemplateId).OrderBy(id => id).ToArray();

		public static bool TryGet(ushort templateId, out NormalLandTemplate template) => ById.TryGetValue(templateId, out template);

		public static IReadOnlyList<NormalLandTemplate> ForMask(RmgLandTemplateBank bank, int semanticMask)
		{
			var use = semanticMask == 15 ? RmgLandTemplateUse.Interior : RmgLandTemplateUse.Transition;
			var matches = AllTemplates.Where(template => template.Permitted && template.Bank == bank &&
				template.Use == use && template.SemanticMask == semanticMask).ToArray();
			if (matches.Length == 0)
				throw new RmgGenerationRejectedException("LAND_COVER_UNSUPPORTED_MASK",
					$"NORMAL {bank} catalogue does not support semantic mask 0x{semanticMask:X}.");

			return matches;
		}

		public static IReadOnlyList<NormalLandTemplate> TransformCandidates(NormalLandTemplate template, RmgSymmetry symmetry)
		{
			var transformed = new RmgNativeTerrainIntent[4];
			for (var frame = 0; frame < 4; frame++)
				transformed[NormalWaterTransitionCatalogue.TransformFrame(frame, symmetry)] = template.NativeTerrain[frame];
			return AllTemplates.Where(candidate => candidate.Bank == template.Bank && candidate.Use == template.Use &&
				candidate.NativeTerrain.SequenceEqual(transformed)).ToArray();
		}

		public static int SemanticMask(IEnumerable<RmgNativeTerrainIntent> nativeTerrain, RmgNativeTerrainIntent highTerrain)
		{
			var mask = 0;
			var frame = 0;
			foreach (var terrain in nativeTerrain)
			{
				if (terrain == highTerrain)
					mask |= 1 << frame;
				frame++;
			}

			return mask;
		}

		public static RmgLandTransitionRole RoleForMask(int mask) => mask switch
		{
			0 or 15 => RmgLandTransitionRole.None,
			1 => RmgLandTransitionRole.CornerNorthWest,
			2 => RmgLandTransitionRole.CornerNorthEast,
			4 => RmgLandTransitionRole.CornerSouthWest,
			8 => RmgLandTransitionRole.CornerSouthEast,
			3 => RmgLandTransitionRole.EdgeNorth,
			5 => RmgLandTransitionRole.EdgeWest,
			10 => RmgLandTransitionRole.EdgeEast,
			12 => RmgLandTransitionRole.EdgeSouth,
			14 => RmgLandTransitionRole.InnerCornerNorthWest,
			13 => RmgLandTransitionRole.InnerCornerNorthEast,
			11 => RmgLandTransitionRole.InnerCornerSouthWest,
			7 => RmgLandTransitionRole.InnerCornerSouthEast,
			_ => RmgLandTransitionRole.Unsupported
		};

		public static IReadOnlyList<string> RunSelfTests()
		{
			var failures = new List<string>();
			if (!Enumerable.Range(37, 64).Select(id => (ushort)id).SequenceEqual(AllTemplates.Select(template => template.TemplateId)))
				failures.Add("The NORMAL land catalogue does not cover exactly templates 37 through 100 in bank order.");
			if (AllTemplates.Count(template => template.Use == RmgLandTemplateUse.Transition) != 48)
				failures.Add("The NORMAL land catalogue must expose exactly 48 audited transition templates.");

			var masks = new[] { 1, 2, 4, 8, 3, 5, 10, 12, 7, 11, 13, 14 };
			foreach (var bank in Enum.GetValues<RmgLandTemplateBank>())
				foreach (var mask in masks)
				{
					var count = AllTemplates.Count(template => template.Bank == bank &&
						template.Use == RmgLandTemplateUse.Transition && template.SemanticMask == mask);
					var expected = bank == RmgLandTemplateBank.RockVegetation && mask == 11 ? 3 :
						bank == RmgLandTemplateBank.RockVegetation && mask == 8 ? 1 : 2;
					if (count != expected)
						failures.Add($"{bank} mask 0x{mask:X} exposes {count} variants; expected {expected}.");
				}

			foreach (var template in AllTemplates.Where(template => template.Permitted))
				foreach (var symmetry in Enum.GetValues<RmgSymmetry>())
				{
					var candidates = TransformCandidates(template, symmetry);
					if (candidates.Count == 0)
						failures.Add($"Template {template.TemplateId} has no {symmetry} transform candidate.");
					foreach (var candidate in candidates)
						for (var frame = 0; frame < 4; frame++)
							if (template.NativeTerrain[frame] != candidate.NativeTerrain[NormalWaterTransitionCatalogue.TransformFrame(frame, symmetry)])
								failures.Add($"Template {template.TemplateId}/{symmetry} candidate {candidate.TemplateId} changes frame {frame} semantics.");
				}

			return failures;
		}

		static NormalLandTemplate T(ushort id, int local, RmgLandTemplateBank bank,
			params RmgNativeTerrainIntent[] native) => new(id, local, bank, RmgLandTemplateUse.Transition, false, true, native);
		static NormalLandTemplate I(ushort id, int local, RmgLandTemplateBank bank,
			RmgNativeTerrainIntent native) => new(id, local, bank, RmgLandTemplateUse.Interior, true, true, native, native, native, native);
		static NormalLandTemplate D(ushort id, int local, RmgLandTemplateBank bank, RmgLandTemplateUse use,
			RmgNativeTerrainIntent native) => new(id, local, bank, use, false,
			id == 93 || id == 94, native, native, native, native);
	}
}
