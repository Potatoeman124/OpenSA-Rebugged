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

		public static NaturalTerrainPrototypeCandidate Generate(NaturalTerrainPrototypeSettings settings)
		{
			if (settings == null)
				throw new ArgumentNullException(nameof(settings));
			if (settings.CandidateIndex < 0)
				throw new ArgumentException("CandidateIndex must be non-negative.", nameof(settings));

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
			var waterTarget = .16 + .08 * Unit(variation);
			var rockTarget = .11 + .06 * Unit(variation);
			var vegetationTarget = .075 + .04 * Unit(variation);
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
			for (var i = 0; i < CellCount; i++)
			{
				var score = roughness[i] + .18 * normalized[i];
				rockDecision[i] = (float)(score - (float)rockThreshold);
				if (semantic[i] == (byte)NaturalTerrainSemantic.Clear && score >= rockThreshold)
					semantic[i] = (byte)NaturalTerrainSemantic.Rock;
			}

			var vegetationScores = Enumerable.Range(0, CellCount)
				.Where(i => semantic[i] == (byte)NaturalTerrainSemantic.Clear)
				.Select(i => moisture[i] - .15 * roughness[i] + .10 * (1 - normalized[i])).ToArray();
			var vegetationThreshold = UpperQuantile(vegetationScores, vegetationTarget * CellCount / vegetationScores.Length);
			for (var i = 0; i < CellCount; i++)
			{
				var score = moisture[i] - .15 * roughness[i] + .10 * (1 - normalized[i]);
				vegetationDecision[i] = (float)(score - (float)vegetationThreshold);
				if (semantic[i] == (byte)NaturalTerrainSemantic.Clear && score >= vegetationThreshold)
					semantic[i] = (byte)NaturalTerrainSemantic.Vegetation;
			}

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
				VegetationTarget = vegetationTarget
			};
		}

		public static IReadOnlyList<string> RunSelfTests()
		{
			var failures = new List<string>();
			foreach (var variant in Enum.GetValues<NaturalTerrainPrototypeVariant>())
			{
				var settings = new NaturalTerrainPrototypeSettings { RootSeed = 92001, CandidateIndex = 0, Variant = variant };
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
			}

			return failures;
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
