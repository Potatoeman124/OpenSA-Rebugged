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
	// Descriptors for ranking candidates, never an automatic declaration of visual acceptance.
	// Frame edges are excluded: cropping a coast must not count as a straight shoreline.
	public sealed class RmgNaturalVisualMetrics
	{
		public double LongestRunNative { get; private set; }
		public double LongRunShare { get; private set; }
		public double RepeatedRunShare { get; private set; }
		public double LargestRectangularity { get; private set; }
		public double SmallBodyShare { get; private set; }
		public double LargestBodyShare { get; private set; }
		public int BodyCount { get; private set; }
		public double Risk => Math.Max(0D, LongestRunNative - 18D) * 1.2D +
			100D * LongRunShare + 10D * RepeatedRunShare +
			30D * Math.Max(0D, LargestRectangularity - .72D) + 30D * SmallBodyShare;

		public void Report(IDictionary<string, double> metrics, string surface)
		{
			var prefix = "natural_" + surface + "_";
			metrics[prefix + "longest_run_native"] = LongestRunNative;
			metrics[prefix + "long_run_share_percent"] = LongRunShare * 100D;
			metrics[prefix + "repeated_run_share_percent"] = RepeatedRunShare * 100D;
			metrics[prefix + "largest_rectangularity"] = LargestRectangularity;
			metrics[prefix + "shape_risk"] = Risk;
		}

		public static RmgNaturalVisualMetrics Measure(bool[] mask, int width, int height)
		{
			if (width <= 0 || height <= 0 || mask.Length != width * height)
				throw new ArgumentException("Shape mask dimensions do not match.");
			var result = new RmgNaturalVisualMetrics();
			var runs = new List<int>();
			for (var y = 1; y < height; y++)
			{
				var run = 0;
				for (var x = 0; x < width; x++)
					Accumulate(mask[y * width + x] != mask[(y - 1) * width + x], ref run);
				Accumulate(false, ref run);
			}
			for (var x = 1; x < width; x++)
			{
				var run = 0;
				for (var y = 0; y < height; y++)
					Accumulate(mask[y * width + x] != mask[y * width + x - 1], ref run);
				Accumulate(false, ref run);
			}
			var scale = 128D / width;
			var perimeter = runs.Sum();
			result.LongestRunNative = runs.Count == 0 ? 0 : runs.Max() * scale;
			result.LongRunShare = perimeter == 0 ? 0 : runs.Where(r => r * scale >= 12D).Sum() / (double)perimeter;
			// High frequency of exactly repeated coarse lengths is a warning about lattice rhythm.
			var coarse = runs.Where(r => r * scale >= 4D && r * scale <= 10D).GroupBy(r => r);
			result.RepeatedRunShare = perimeter == 0 ? 0 :
				coarse.Select(g => g.Key * g.Count()).DefaultIfEmpty(0).Max() / (double)perimeter;

			var seen = new bool[mask.Length];
			var queue = new Queue<int>();
			var total = mask.Count(value => value);
			var largest = 0;
			var small = 0;
			for (var i = 0; i < mask.Length; i++)
			{
				if (!mask[i] || seen[i])
					continue;
				result.BodyCount++;
				seen[i] = true;
				queue.Enqueue(i);
				var area = 0;
				var minX = width;
				var maxX = 0;
				var minY = height;
				var maxY = 0;
				while (queue.Count > 0)
				{
					var cell = queue.Dequeue();
					var x = cell % width;
					var y = cell / width;
					area++;
					minX = Math.Min(minX, x);
					maxX = Math.Max(maxX, x);
					minY = Math.Min(minY, y);
					maxY = Math.Max(maxY, y);
					if (x > 0) Visit(cell - 1);
					if (x + 1 < width) Visit(cell + 1);
					if (y > 0) Visit(cell - width);
					if (y + 1 < height) Visit(cell + width);
				}
				if (area < mask.Length * .005D)
					small += area;
				if (area > largest)
				{
					largest = area;
					result.LargestRectangularity = area / (double)((maxX - minX + 1) * (maxY - minY + 1));
				}
			}
			result.SmallBodyShare = total == 0 ? 0 : small / (double)total;
			result.LargestBodyShare = total == 0 ? 0 : largest / (double)total;
			return result;

			void Accumulate(bool edge, ref int run)
			{
				if (edge)
					run++;
				else if (run > 0)
				{
					runs.Add(run);
					run = 0;
				}
			}
			void Visit(int index)
			{
				if (!mask[index] || seen[index])
					return;
				seen[index] = true;
				queue.Enqueue(index);
			}
		}
	}
}
