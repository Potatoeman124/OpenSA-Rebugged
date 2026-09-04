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

namespace OpenRA.Mods.OpenSA.Rmg.NaturalPrototype
{
	public static class NaturalTerrainPrototypeGenerator
	{
		const int Width = NaturalTerrainPrototypeSettings.Width;
		const int Height = NaturalTerrainPrototypeSettings.Height;
		const int CellCount = Width * Height;
		static readonly (int X, int Y)[] ForwardMooreNeighbors =
		{
			(1, 0), (0, 1), (1, 1), (-1, 1)
		};

		public static NaturalTerrainPrototypeCandidate Generate(NaturalTerrainPrototypeSettings settings)
		{
			if (settings == null)
				throw new ArgumentNullException(nameof(settings));
			if (settings.CandidateIndex < 0)
				throw new ArgumentException("CandidateIndex must be non-negative.", nameof(settings));
			ValidateTargetOverride(settings.WaterTargetOverride, "WaterTargetOverride");
			ValidateTargetOverride(settings.RockTargetOverride, "RockTargetOverride");
			ValidateTargetOverride(settings.VegetationTargetOverride, "VegetationTargetOverride");

			var streams = settings.StreamSeeds();
			var basins = CreateBasins(settings, streams["basin-potential"]);
			var raw = new float[CellCount];
			var normalized = new float[CellCount];
			var warpX = new float[CellCount];
			var warpY = new float[CellCount];
			var basinPotential = new float[CellCount];
			var moisture = new float[CellCount];
			var roughness = new float[CellCount];
			var waterDecision = new float[CellCount];
			var rockDecision = new float[CellCount];
			var vegetationDecision = new float[CellCount];

			for (var y = 0; y < Height; y++)
				for (var x = 0; x < Width; x++)
				{
					var i = y * Width + x;
					var wx = 5.5 * FractalNoise(streams["domain-warp-x"], x, y, 58, 2, .5);
					var wy = 5.5 * FractalNoise(streams["domain-warp-y"], x, y, 58, 2, .5);
					warpX[i] = (float)wx;
					warpY[i] = (float)wy;

					var land = .78 * FractalNoise(streams["landform-elevation"], x + wx, y + wy, 44, 4, .52) +
						.22 * FractalNoise(Mix(streams["landform-elevation"], 0xE1E7A710UL), x + wx * .35, y + wy * .35, 86, 2, .55);
					var edgeDistance = Math.Min(Math.Min(x, Width - 1 - x), Math.Min(y, Height - 1 - y));
					var edgeUplift = .34 * SmoothStep(18, 1, edgeDistance);
					var basin = BasinValue(basins, streams["basin-potential"], x + wx * .2, y + wy * .2);
					basinPotential[i] = (float)basin;
					raw[i] = (float)(land + edgeUplift + basin);
				}

			Normalize(raw, normalized);
			for (var y = 0; y < Height; y++)
				for (var x = 0; x < Width; x++)
				{
					var i = y * Width + x;
					var m = .52 * FractalNoise(streams["moisture"], x + warpX[i] * .35, y + warpY[i] * .35, 38, 3, .55) +
						.34 * (1 - normalized[i]) - .14 * basinPotential[i];
					var r = .62 * FractalNoise(streams["roughness"], x + warpX[i] * .2, y + warpY[i] * .2, 24, 3, .57) +
						.22 * FractalNoise(Mix(streams["roughness"], 0xB0AD5CA1UL), x, y, 68, 2, .5) +
						.16 * Math.Abs(normalized[i] - .52);
					moisture[i] = (float)m;
					roughness[i] = (float)r;
				}

			var variation = new DeterministicRandom(streams["classification-variation"]);
			var waterTarget = settings.WaterTargetOverride ?? .16 + .08 * Unit(variation);
			var rockTarget = settings.RockTargetOverride ?? .11 + .06 * Unit(variation);
			var vegetationTarget = settings.VegetationTargetOverride ?? .075 + .04 * Unit(variation);
			var waterThreshold = LowerQuantile(normalized, waterTarget);
			for (var i = 0; i < CellCount; i++)
				waterDecision[i] = (float)(waterThreshold - normalized[i]);

			var semantic = new byte[CellCount];
			for (var i = 0; i < CellCount; i++)
				if (normalized[i] <= waterThreshold)
					semantic[i] = (byte)NaturalTerrainSemantic.Water;

			var rockScores = Enumerable.Range(0, CellCount)
				.Where(i => semantic[i] == (byte)NaturalTerrainSemantic.Clear)
				.Select(i => roughness[i] + .18 * normalized[i]).ToArray();
			var rockThreshold = UpperQuantile(rockScores, rockTarget * CellCount / rockScores.Length);
			var unrestrictedRock = new bool[CellCount];
			for (var i = 0; i < CellCount; i++)
			{
				var score = roughness[i] + .18 * normalized[i];
				rockDecision[i] = (float)(score - (float)rockThreshold);
				unrestrictedRock[i] = semantic[i] == (byte)NaturalTerrainSemantic.Clear && score >= rockThreshold;
				if (unrestrictedRock[i] &&
					(!settings.OriginalSurfaceRelations || !HasSurfaceWithin(semantic, i, NaturalTerrainSemantic.Water, 1)))
					semantic[i] = (byte)NaturalTerrainSemantic.Rock;
			}

			var vegetationScores = Enumerable.Range(0, CellCount)
				.Where(i => semantic[i] != (byte)NaturalTerrainSemantic.Water && !unrestrictedRock[i])
				.Select(i => moisture[i] - .15 * roughness[i] + .10 * (1 - normalized[i])).ToArray();
			var vegetationThreshold = UpperQuantile(vegetationScores, vegetationTarget * CellCount / vegetationScores.Length);
			for (var i = 0; i < CellCount; i++)
			{
				var score = moisture[i] - .15 * roughness[i] + .10 * (1 - normalized[i]);
				vegetationDecision[i] = (float)(score - (float)vegetationThreshold);
				if (semantic[i] == (byte)NaturalTerrainSemantic.Clear && !unrestrictedRock[i] &&
					score >= vegetationThreshold &&
					(!settings.OriginalSurfaceRelations || !HasSurfaceWithin(semantic, i, NaturalTerrainSemantic.Water, 2)))
					semantic[i] = (byte)NaturalTerrainSemantic.Vegetation;
			}

			if (settings.OriginalSurfaceRelations)
				AddGravelTransitionAroundMoss(semantic);
			var adjacencyCounts = CountSurfaceAdjacencies(semantic);
			var forbiddenAdjacencyCount = ForbiddenSurfaceAdjacencyCount(adjacencyCounts);
			if (settings.OriginalSurfaceRelations && forbiddenAdjacencyCount != 0)
				throw new InvalidOperationException(
					$"Original surface relations produced {forbiddenAdjacencyCount} forbidden edge/corner contacts.");

			return new NaturalTerrainPrototypeCandidate
			{
				Settings = settings,
				StreamSeeds = streams,
				Basins = basins,
				Fields = new Dictionary<string, float[]>(StringComparer.Ordinal)
				{
					["landform-raw"] = raw,
					["landform-normalized"] = normalized,
					["domain-warp-x"] = warpX,
					["domain-warp-y"] = warpY,
					["basin-potential"] = basinPotential,
					["moisture"] = moisture,
					["roughness"] = roughness,
					["water-decision"] = waterDecision,
					["rock-decision"] = rockDecision,
					["vegetation-decision"] = vegetationDecision
				},
				Semantic = semantic,
				WaterThreshold = waterThreshold,
				RockThreshold = rockThreshold,
				VegetationThreshold = vegetationThreshold,
				WaterTarget = waterTarget,
				RockTarget = rockTarget,
				VegetationTarget = vegetationTarget,
				SurfaceAdjacencyCounts = adjacencyCounts,
				ForbiddenSurfaceAdjacencyCount = forbiddenAdjacencyCount
			};
		}

		static void ValidateTargetOverride(double? value, string name)
		{
			if (value.HasValue && (double.IsNaN(value.Value) || double.IsInfinity(value.Value) ||
				value.Value <= 0 || value.Value >= 1))
				throw new ArgumentException($"{name} must be strictly between zero and one.");
		}

		public static IReadOnlyList<string> RunSelfTests()
		{
			var failures = new List<string>();
			foreach (var variant in Enum.GetValues<NaturalTerrainPrototypeVariant>())
			{
				var settings = new NaturalTerrainPrototypeSettings
				{
					RootSeed = 92001,
					CandidateIndex = 0,
					Variant = variant,
					OriginalSurfaceRelations = true
				};
				var first = Generate(settings);
				var second = Generate(settings);
				if (!first.Semantic.SequenceEqual(second.Semantic))
					failures.Add($"Semantic replay failed for {settings.VariantId}.");
				foreach (var field in first.Fields.Keys)
					if (!first.Fields[field].SequenceEqual(second.Fields[field]))
						failures.Add($"Field replay failed for {settings.VariantId}/{field}.");
				var differentRoot = Generate(new NaturalTerrainPrototypeSettings { RootSeed = 92002, CandidateIndex = 0, Variant = variant });
				if (first.Semantic.SequenceEqual(differentRoot.Semantic))
					failures.Add($"Root-seed diversity collapsed for {settings.VariantId}.");
				var waterFraction = first.Semantic.Count(v => v == (byte)NaturalTerrainSemantic.Water) / (double)CellCount;
				if (waterFraction < .12 || waterFraction > .28)
					failures.Add($"Water soft band failed for {settings.VariantId}: {waterFraction:P2}.");
				if (variant == NaturalTerrainPrototypeVariant.CorrelatedFieldBaseline && first.Basins.Count != 0)
					failures.Add("Variant A must not contain basin objects.");
				if (variant == NaturalTerrainPrototypeVariant.CorrelatedFieldWithBasinPotential && (first.Basins.Count < 1 || first.Basins.Count > 3))
					failures.Add("Variant B must contain one to three basin potentials.");
				if (first.ForbiddenSurfaceAdjacencyCount != 0)
					failures.Add($"Original surface relations failed for {settings.VariantId}: {first.ForbiddenSurfaceAdjacencyCount} forbidden contacts.");

				var unrestricted = Generate(new NaturalTerrainPrototypeSettings
				{
					RootSeed = settings.RootSeed,
					CandidateIndex = settings.CandidateIndex,
					Variant = settings.Variant,
					OriginalSurfaceRelations = false
				});
				foreach (var field in first.Fields.Keys)
					if (!first.Fields[field].SequenceEqual(unrestricted.Fields[field]))
						failures.Add($"Surface-relations option changed the accepted Step 2 field stream for {settings.VariantId}/{field}.");
				if (first.Semantic.SequenceEqual(unrestricted.Semantic))
					failures.Add($"Surface-relations option did not affect semantic classification for {settings.VariantId}.");
			}

			return failures;
		}

		public static IReadOnlyDictionary<string, int> CountSurfaceAdjacencies(byte[] semantic)
		{
			if (semantic == null || semantic.Length != CellCount)
				throw new ArgumentException($"Semantic field must contain exactly {CellCount} cells.", nameof(semantic));

			var counts = new Dictionary<string, int>(StringComparer.Ordinal)
			{
				["water-dirt"] = 0,
				["water-gravel"] = 0,
				["water-moss"] = 0,
				["dirt-gravel"] = 0,
				["dirt-moss"] = 0,
				["gravel-moss"] = 0
			};
			for (var y = 0; y < Height; y++)
				for (var x = 0; x < Width; x++)
				{
					var value = (NaturalTerrainSemantic)semantic[y * Width + x];
					foreach (var (offsetX, offsetY) in ForwardMooreNeighbors)
					{
						var nx = x + offsetX;
						var ny = y + offsetY;
						if (nx < 0 || nx >= Width || ny < 0 || ny >= Height)
							continue;
						var neighbor = (NaturalTerrainSemantic)semantic[ny * Width + nx];
						if (value == neighbor)
							continue;
						var key = SurfacePairKey(value, neighbor);
						counts[key]++;
					}
				}

			return counts;
		}

		public static int CountForbiddenSurfaceAdjacencies(byte[] semantic) =>
			ForbiddenSurfaceAdjacencyCount(CountSurfaceAdjacencies(semantic));

		static void AddGravelTransitionAroundMoss(byte[] semantic)
		{
			var moss = new List<int>();
			for (var i = 0; i < semantic.Length; i++)
				if (semantic[i] == (byte)NaturalTerrainSemantic.Vegetation)
					moss.Add(i);

			foreach (var index in moss)
			{
				var x = index % Width;
				var y = index / Width;
				for (var dy = -1; dy <= 1; dy++)
					for (var dx = -1; dx <= 1; dx++)
					{
						if (dx == 0 && dy == 0)
							continue;
						var nx = x + dx;
						var ny = y + dy;
						if (nx < 0 || nx >= Width || ny < 0 || ny >= Height)
							continue;
						var neighbor = ny * Width + nx;
						if (semantic[neighbor] == (byte)NaturalTerrainSemantic.Clear)
							semantic[neighbor] = (byte)NaturalTerrainSemantic.Rock;
					}
			}
		}

		static bool HasSurfaceWithin(byte[] semantic, int index, NaturalTerrainSemantic surface, int radius)
		{
			var x = index % Width;
			var y = index / Width;
			for (var dy = -radius; dy <= radius; dy++)
				for (var dx = -radius; dx <= radius; dx++)
				{
					if (dx == 0 && dy == 0)
						continue;
					var nx = x + dx;
					var ny = y + dy;
					if (nx >= 0 && nx < Width && ny >= 0 && ny < Height &&
						semantic[ny * Width + nx] == (byte)surface)
						return true;
				}

			return false;
		}

		static int ForbiddenSurfaceAdjacencyCount(IReadOnlyDictionary<string, int> counts) =>
			counts["water-gravel"] + counts["water-moss"] + counts["dirt-moss"];

		static string SurfacePairKey(NaturalTerrainSemantic first, NaturalTerrainSemantic second)
		{
			var low = (NaturalTerrainSemantic)Math.Min((byte)first, (byte)second);
			var high = (NaturalTerrainSemantic)Math.Max((byte)first, (byte)second);
			return (low, high) switch
			{
				(NaturalTerrainSemantic.Clear, NaturalTerrainSemantic.Water) => "water-dirt",
				(NaturalTerrainSemantic.Water, NaturalTerrainSemantic.Rock) => "water-gravel",
				(NaturalTerrainSemantic.Water, NaturalTerrainSemantic.Vegetation) => "water-moss",
				(NaturalTerrainSemantic.Clear, NaturalTerrainSemantic.Rock) => "dirt-gravel",
				(NaturalTerrainSemantic.Clear, NaturalTerrainSemantic.Vegetation) => "dirt-moss",
				(NaturalTerrainSemantic.Rock, NaturalTerrainSemantic.Vegetation) => "gravel-moss",
				_ => throw new ArgumentException($"Unsupported surface adjacency {first}-{second}.")
			};
		}

		static List<NaturalTerrainPrototypeBasin> CreateBasins(NaturalTerrainPrototypeSettings settings, ulong seed)
		{
			var result = new List<NaturalTerrainPrototypeBasin>();
			if (settings.Variant == NaturalTerrainPrototypeVariant.CorrelatedFieldBaseline)
				return result;
			var random = new DeterministicRandom(seed);
			var count = 1 + random.NextInt(3);
			for (var i = 0; i < count; i++)
			{
				var major = 24 + 21 * Unit(random);
				var aspect = 1.15 + 1.15 * Unit(random);
				result.Add(new NaturalTerrainPrototypeBasin
				{
					CenterX = 25 + 77 * Unit(random),
					CenterY = 25 + 77 * Unit(random),
					RadiusMajor = major,
					RadiusMinor = major / aspect,
					AngleRadians = Math.PI * Unit(random),
					Strength = .48 + .42 * Unit(random),
					Modulation = .10 + .12 * Unit(random)
				});
			}

			return result;
		}

		static double BasinValue(IReadOnlyList<NaturalTerrainPrototypeBasin> basins, ulong seed, double x, double y)
		{
			var value = 0D;
			for (var i = 0; i < basins.Count; i++)
			{
				var basin = basins[i];
				var dx = x - basin.CenterX;
				var dy = y - basin.CenterY;
				var cos = Math.Cos(basin.AngleRadians);
				var sin = Math.Sin(basin.AngleRadians);
				var u = (cos * dx + sin * dy) / basin.RadiusMajor;
				var v = (-sin * dx + cos * dy) / basin.RadiusMinor;
				var radial = u * u + v * v;
				var modulation = 1 + basin.Modulation * FractalNoise(Mix(seed, (ulong)i + 1), x, y, 31, 2, .5);
				value -= basin.Strength * Math.Exp(-1.7 * radial) * modulation;
			}

			return value;
		}

		static double Unit(DeterministicRandom random) => (random.NextUInt64() >> 11) * (1.0 / (1UL << 53));

		static void Normalize(float[] source, float[] destination)
		{
			var min = source.Min();
			var max = source.Max();
			var range = max - min;
			for (var i = 0; i < source.Length; i++)
				destination[i] = range > 0 ? (source[i] - min) / range : 0;
		}

		static double LowerQuantile(IEnumerable<float> values, double fraction)
		{
			var sorted = values.Select(v => (double)v).OrderBy(v => v).ToArray();
			return sorted[Math.Clamp((int)Math.Round(fraction * (sorted.Length - 1)), 0, sorted.Length - 1)];
		}

		static double UpperQuantile(IEnumerable<double> values, double fraction)
		{
			var sorted = values.OrderByDescending(v => v).ToArray();
			return sorted[Math.Clamp((int)Math.Round(fraction * (sorted.Length - 1)), 0, sorted.Length - 1)];
		}

		static double FractalNoise(ulong seed, double x, double y, double scale, int octaves, double persistence)
		{
			var total = 0D;
			var amplitude = 1D;
			var normalizer = 0D;
			for (var octave = 0; octave < octaves; octave++)
			{
				total += amplitude * ValueNoise(Mix(seed, (ulong)octave + 1), x / scale, y / scale);
				normalizer += amplitude;
				amplitude *= persistence;
				scale *= .5;
			}

			return total / normalizer;
		}

		static double ValueNoise(ulong seed, double x, double y)
		{
			var x0 = (int)Math.Floor(x);
			var y0 = (int)Math.Floor(y);
			var tx = Fade(x - x0);
			var ty = Fade(y - y0);
			var a = Sample(seed, x0, y0);
			var b = Sample(seed, x0 + 1, y0);
			var c = Sample(seed, x0, y0 + 1);
			var d = Sample(seed, x0 + 1, y0 + 1);
			return Lerp(Lerp(a, b, tx), Lerp(c, d, tx), ty);
		}

		static double Sample(ulong seed, int x, int y)
		{
			var value = Mix(seed, unchecked((ulong)(long)x) * 0x9E3779B97F4A7C15UL ^ unchecked((ulong)(long)y) * 0xC2B2AE3D27D4EB4FUL);
			return 2 * ((value >> 11) * (1.0 / (1UL << 53))) - 1;
		}

		static ulong Mix(ulong seed, ulong value)
		{
			var z = seed + value + 0x9E3779B97F4A7C15UL;
			z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
			z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
			return z ^ (z >> 31);
		}

		static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
		static double Lerp(double a, double b, double t) => a + (b - a) * t;
		static double SmoothStep(double edge0, double edge1, double value)
		{
			var t = Math.Clamp((value - edge0) / (edge1 - edge0), 0, 1);
			return t * t * (3 - 2 * t);
		}
	}
}
