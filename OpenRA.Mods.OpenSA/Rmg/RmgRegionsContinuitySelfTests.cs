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
		public static IReadOnlyList<string> RunRegionsContinuitySelfTests(ModData modData)
		{
			Game.ModData = modData;
			var failures = new List<string>();
			void Check(bool condition, string message)
			{
				if (!condition) failures.Add(message);
			}

			foreach (var schema in new[] { 5, 6 })
				foreach (var size in new[] { 128, 256 })
					foreach (var complexity in new[] { TerrainComplexity.Low, TerrainComplexity.Standard, TerrainComplexity.High })
					{
						var requested = new RmgPlayerSettings
						{
							SchemaVersion = schema, Seed = 397716241463670640, MapSize = size,
							LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscape, TerrainComplexity = complexity
						};
						var settings = RmgPlayerSettingsContract.Resolve(requested).Normalized;
						var repeat = RmgPlayerSettingsContract.Resolve(RmgPlayerSettingsContract.Parse(requested.ToJson())).Normalized;
						var profile = RmgProfile.Load(modData, settings);
						Check(settings.GeneratorVersion == schema + 6 && settings.Canonical(profile) == repeat.Canonical(profile),
							$"Regions version/schema identity failed: schema {schema}/{size}/{complexity}.");
					}

			var invalid = new RmgPlayerSettings { SchemaVersion = 6, LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscape }.ToJson();
			invalid["terrain_complexity"] = "ultra";
			try
			{
				RmgPlayerSettingsContract.Parse(invalid);
				failures.Add("Deferred complexity levels must not be silently accepted in V12.");
			}
			catch (ArgumentException) { }

			foreach (var seed in new[] { 397716241463670640UL, 0UL, ulong.MaxValue })
				foreach (var size in new[] { 128, 256 })
				{
					var baseSettings = new TerrainComparisonSettings(seed, size, TerrainConstruction.Regions, TerrainComplexity.Low)
					{
						Continuity = true
					};
					var low = TerrainComparison.Generate(modData, baseSettings);
					var high = TerrainComparison.Generate(modData, baseSettings with { Complexity = TerrainComplexity.High });
					var repeat = TerrainComparison.Generate(modData, baseSettings with { Complexity = TerrainComplexity.High });
					var left = TerrainComparison.NativeBytes(low.Map);
					var right = TerrainComparison.NativeBytes(high.Map);
					Check(right.SequenceEqual(TerrainComparison.NativeBytes(repeat.Map)), $"V12 terrain replay failed: {seed}/{size}.");
					Check(!left.SequenceEqual(right), $"Complexity made no native terrain change: {seed}/{size}.");
					var water = left.Count(t => t == (byte)RmgNativeTerrainIntent.Water);
					var kept = left.Where((t, i) => t == (byte)RmgNativeTerrainIntent.Water && right[i] == t).Count();
					// A broad continuity regression tripwire, not a runtime map rejection
					// or a substitute for the multi-seed spatial/visual acceptance review.
					Check(kept >= .60 * water, $"V12 lost most of the reference water geography: {seed}/{size}.");
					Check(RegionCoarseCorrelation(left, right, size, t => t >= 2) >= .70,
						$"V12 moved the broad gravel/moss geography: {seed}/{size}.");
					// Moss cannot survive where water or the changed gravel envelope
					// removes its legal interior. Test retention of the sites still valid.
					var eligibleMoss = 0;
					var retainedMoss = 0;
					var lattice = size / 2 + 1;
					for (var y = 0; y < lattice; y++)
						for (var x = 0; x < lattice; x++)
						{
							var index = y * lattice + x;
							if (!low.Map.VegetationLattice[index]) continue;
							var eligible = true;
							for (var dy = -1; dy <= 1; dy++)
								for (var dx = -1; dx <= 1; dx++)
									eligible &= high.Map.RockEnvelopeLattice[Math.Clamp(y + dy, 0, lattice - 1) * lattice +
										Math.Clamp(x + dx, 0, lattice - 1)];
							if (!eligible) continue;
							eligibleMoss++;
							if (high.Map.VegetationLattice[index]) retainedMoss++;
						}

					Check(retainedMoss >= .95 * eligibleMoss, $"V12 abandoned still-valid moss sites: {seed}/{size}.");
					var differentLand = TerrainComparison.Generate(modData, baseSettings with { GravelPercent = 18, MossPercent = 11 });
					Check(low.Map.Obstacles.SequenceEqual(differentLand.Map.Obstacles), "Gravel/moss amount changed water geography.");
					var moreWater = TerrainComparison.Generate(modData, baseSettings with { WaterPercent = 24 });
					Check(low.WaterPriority.SequenceEqual(moreWater.WaterPriority), "Water quantity reshuffled the V12 geographic priorities.");
					Check((double)moreWater.Report["metrics"]["water_percent_map"] > (double)low.Report["metrics"]["water_percent_map"],
						"Water amount no longer changes native coverage.");
				}

			return failures;
		}
		static double RegionCoarseCorrelation(byte[] left, byte[] right, int size, Func<byte, bool> surface)
		{
			var a = new List<double>();
			var b = new List<double>();
			for (var by = 0; by < size; by += 32)
				for (var bx = 0; bx < size; bx += 32)
				{
					var first = 0;
					var second = 0;
					for (var y = by; y < by + 32; y++)
						for (var x = bx; x < bx + 32; x++)
						{
							if (surface(left[y * size + x])) first++;
							if (surface(right[y * size + x])) second++;
						}

					a.Add(first);
					b.Add(second);
				}

			var meanA = a.Average();
			var meanB = b.Average();
			var denominator = Math.Sqrt(a.Sum(v => (v - meanA) * (v - meanA)) * b.Sum(v => (v - meanB) * (v - meanB)));
			return denominator == 0 ? 0 : a.Select((v, i) => (v - meanA) * (b[i] - meanB)).Sum() / denominator;
		}

	}
}
