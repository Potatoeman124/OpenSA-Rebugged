#region Copyright & License Information
/*
 * Copyright The OpenSA Developers (see CREDITS)
 * This file is part of OpenSA, under the GNU General Public License, version 3 or later.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.OpenSA.Rmg.Reassessment;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public static partial class RmgGenerator
	{
		public static IReadOnlyList<string> RunRegionsExtendedSelfTests(ModData modData)
		{
			Game.ModData = modData;
			var failures = new List<string>();
			void Check(bool condition, string message)
			{
				if (!condition) failures.Add(message);
			}

			foreach (var size in new[] { 128, 256 })
			{
				var identities = new HashSet<string>();
				foreach (var complexity in Enum.GetValues<TerrainComplexity>())
				{
					var requested = new RmgPlayerSettings
					{
						SchemaVersion = 7, MapSize = size, TerrainComplexity = complexity,
						LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscape
					};
					var settings = RmgPlayerSettingsContract.Resolve(requested).Normalized;
					var repeat = RmgPlayerSettingsContract.Resolve(RmgPlayerSettingsContract.Parse(requested.ToJson())).Normalized;
					var profile = RmgProfile.Load(modData, settings);
					Check(settings.GeneratorVersion == 13 && profile.GeneratorVersion == 13 &&
						settings.TerrainComplexity == complexity && settings.Canonical(profile) == repeat.Canonical(profile),
						$"V13 schema round trip failed: {size}/{complexity}.");
					Check(identities.Add(settings.Canonical(profile)), "Two V13 levels share a canonical identity.");
				}
			}

			foreach (var schema in new[] { 5, 6 })
				foreach (var value in new[] { "small", "medium", "extreme", "ultra" })
				{
					var json = new RmgPlayerSettings { SchemaVersion = schema,
						LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscape }.ToJson();
					json["terrain_complexity"] = value;
					try { RmgPlayerSettingsContract.Parse(json); failures.Add($"Historical schema {schema} accepted {value}."); }
					catch (ArgumentException) { }
				}

			try
			{
				RmgPlayerSettingsContract.Resolve(new RmgPlayerSettings { SchemaVersion = 6,
					LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscape, TerrainComplexity = TerrainComplexity.Ultra });
				failures.Add("Direct V12 settings accepted an extended level.");
			}
			catch (ArgumentException) { }

			foreach (var seed in new[] { 397716241463670640UL, 0UL, ulong.MaxValue })
				foreach (var size in new[] { 128, 256 })
				{
					var baseline = new TerrainComparisonSettings(seed, size, TerrainConstruction.Regions, TerrainComplexity.Standard)
					{
						Continuity = true
					};
					var oldStandard = TerrainComparison.Generate(modData, baseline);
					var newSmall = TerrainComparison.Generate(modData, baseline with
					{
						ExtendedComplexity = true, Complexity = TerrainComplexity.Low
					});
					Check(oldStandard.Map.NativeTerrainIntents.SequenceEqual(newSmall.Map.NativeTerrainIntents) &&
						oldStandard.Map.TemplateIds.SequenceEqual(newSmall.Map.TemplateIds),
						$"V13 Small no longer reproduces V12 Standard terrain: {seed}/{size}.");
					byte[] first = null;
					byte[] previous = null;
					foreach (var complexity in Enum.GetValues<TerrainComplexity>())
					{
						var input = baseline with { ExtendedComplexity = true, Complexity = complexity };
						var terrain = TerrainComparison.Generate(modData, input);
						var cells = TerrainComparison.NativeBytes(terrain.Map);
						first ??= cells;
						Check(previous == null || !previous.SequenceEqual(cells), $"V13 adjacent levels are identical: {seed}/{size}/{complexity}.");
						previous = cells;
						if (complexity != TerrainComplexity.Ultra) continue;
						Check(cells.SequenceEqual(TerrainComparison.NativeBytes(TerrainComparison.Generate(modData, input).Map)),
							$"V13 Ultra replay failed: {seed}/{size}.");
						// Fixed-seed regression tripwire; the retained matrix/visual review
						// assesses continuity more broadly. Never reject runtime maps here.
						Check(RegionCoarseCorrelation(first, cells, size, t => t == 1) >= .65,
							$"V13 Ultra abandoned the seed's broad water geography: {seed}/{size}.");
						var changedLand = TerrainComparison.Generate(modData, input with { GravelPercent = 18, MossPercent = 11 });
						Check(terrain.Map.Obstacles.SequenceEqual(changedLand.Map.Obstacles), "V13 gravel/moss quantity moved water.");
					}
				}

			return failures;
		}
	}
}
