#region Copyright & License Information
/*
 * Copyright The OpenSA Developers (see CREDITS)
 * This file is part of OpenSA, under the GNU General Public License, version 3 or later.
 */
#endregion

using System;
using System.Collections.Generic;

namespace OpenRA.Mods.OpenSA.Rmg.Reassessment
{
	public static partial class TerrainComparison
	{
		// All frequencies and region identities are fixed across Complexity. Only the
		// bounded contribution of secondary terrain varies. Quantities select coverage.
		static double[] BuildContinuousPriorities(TerrainComparisonSettings settings, int width, int height, int step, ulong stream)
		{
			const double scale = 64;
			var seed = Mix(settings.Seed, stream);
			var random = new DeterministicRandom(seed);
			var fraction = stream switch { 11 => .20, 29 => .24, 53 => .08, _ => throw new ArgumentOutOfRangeException(nameof(stream)) };
			var radius = scale * .42;
			var count = Math.Max(1, (int)Math.Ceiling(settings.Size * settings.Size * fraction / (Math.PI * radius * radius * .55)));
			var regions = new List<(double X, double Y, double R, double Aspect, double Cos, double Sin)>();
			for (var i = 0; i < count; i++)
			{
				var x = Unit(random) * (settings.Size + scale * .4) - scale * .2;
				var y = Unit(random) * (settings.Size + scale * .4) - scale * .2;
				var r = radius * (.65 + .75 * Unit(random));
				var aspect = .55 + .65 * Unit(random);
				var angle = Unit(random) * Math.PI;
				regions.Add((x, y, r, aspect, Math.Cos(angle), Math.Sin(angle)));
			}

			var strength = settings.ExtendedComplexity ? settings.Complexity switch
			{
				TerrainComplexity.Low => .65,
				TerrainComplexity.Standard => 1.15,
				TerrainComplexity.High => 1.4,
				TerrainComplexity.Extreme => 1.65,
				TerrainComplexity.Ultra => 1.9,
				_ => throw new ArgumentOutOfRangeException(nameof(settings))
			} : settings.Complexity switch
			{
				TerrainComplexity.Low => 0D,
				TerrainComplexity.Standard => .65,
				TerrainComplexity.High => 1.15,
				_ => throw new ArgumentOutOfRangeException(nameof(settings))
			};
			// Above Medium, add a fixed finer band rather than allowing the coarse
			// displacement alone to dominate. Its positions/frequencies are seed-stable.
			var fineStrength = settings.ExtendedComplexity ? settings.Complexity switch
			{
				TerrainComplexity.High => .4,
				TerrainComplexity.Extreme => .85,
				TerrainComplexity.Ultra => 1.3,
				_ => 0D
			} : 0D;
			// Geological envelopes need broader interiors for the nested moss bank.
			// Their detail contribution must not displace those interiors wholesale.
			if (stream != 11)
			{
				strength *= .6;
				fineStrength *= .6;
			}
			var result = new double[width * height];
			for (var y = 0; y < height; y++)
				for (var x = 0; x < width; x++)
				{
					var nx = x * step;
					var ny = y * step;
					var px = nx + scale * .16 * Noise(Mix(seed, 3), nx / (scale * .7), ny / (scale * .7));
					var py = ny + scale * .16 * Noise(Mix(seed, 5), nx / (scale * .7), ny / (scale * .7));
					var value = double.NegativeInfinity;
					foreach (var region in regions)
					{
						var dx = px - region.X;
						var dy = py - region.Y;
						var rx = (region.Cos * dx + region.Sin * dy) / region.R;
						var ry = (-region.Sin * dx + region.Cos * dy) / (region.R * region.Aspect);
						value = Math.Max(value, 1 - Math.Sqrt(rx * rx + ry * ry));
					}

					var baseDetail = .12 * Noise(Mix(seed, 7), nx / 16D, ny / 16D);
					// Signed, spatially coherent detail can form inlets, dry interruptions
					// and nearby satellite patches without replacing the broad regions.
					var detail = .75 * Noise(Mix(seed, 13), nx / 24D, ny / 24D) +
						.25 * Noise(Mix(seed, 17), nx / 12D, ny / 12D);
					// Preserve the central identity of a region while letting its margins
					// and shallower interiors split, branch, or acquire satellite patches.
					var detailWeight = .25 + .75 * (1 - Math.Clamp(value, 0, 1));
					result[y * width + x] = value + baseDetail + strength * detailWeight * detail;
					if (fineStrength > 0)
						result[y * width + x] += fineStrength * detailWeight *
							(.75 * Noise(Mix(seed, 19), nx / 12D, ny / 12D) +
							.25 * Noise(Mix(seed, 23), nx / 6D, ny / 6D));
				}

			return result;
		}
		// Prefer the seed's existing moss locations where the completed geological
		// envelope still supports them; replace lost coverage nearby. Never enlarge
		// an envelope or repaint water to force an old moss cell to survive.
		static void AnchorMossPriorities(double[] priority, bool[] referenceMoss, int width)
		{
			var distance = new int[priority.Length];
			Array.Fill(distance, int.MaxValue);
			var queue = new Queue<int>();
			for (var i = 0; i < distance.Length; i++)
				if (referenceMoss[i])
				{
					distance[i] = 0;
					queue.Enqueue(i);
				}

			if (queue.Count == 0)
				return;
			while (queue.TryDequeue(out var i))
			{
				var x = i % width;
				var y = i / width;
				for (var dy = -1; dy <= 1; dy++)
					for (var dx = -1; dx <= 1; dx++)
					{
						var nx = x + dx;
						var ny = y + dy;
						if (nx < 0 || ny < 0 || nx >= width || ny >= width)
							continue;
						var next = ny * width + nx;
						if (distance[next] <= distance[i] + 1)
							continue;
						distance[next] = distance[i] + 1;
						queue.Enqueue(next);
					}
			}

			for (var i = 0; i < priority.Length; i++)
				priority[i] = -distance[i] + .125 * priority[i];
		}

	}
}
