#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OpenRA.Mods.OpenSA.Rmg
{
	static class DividedLandsTopology
	{
		public static JObject Validate(byte[] cells, RmgGenerationSettings settings, IReadOnlyList<RmgPoint> starts) =>
			ValidateGround(cells.Select(c => c != 1).ToArray(), settings, starts);

		public static JObject ValidateGround(bool[] passable, RmgGenerationSettings settings, IReadOnlyList<RmgPoint> starts)
		{
			var size = settings.MapSize; var geometry = new DividedLandsGeometry(settings); var center = geometry.Center;
			var counts = new List<int>();
			foreach (var angle in geometry.Angles)
			{
				var dx = Math.Cos(angle); var dy = Math.Sin(angle); var limit = (int)(center / Math.Max(Math.Abs(dx), Math.Abs(dy)));
				var previous = false; var count = 0;
				for (var step = settings.PlayerCount == 2 ? -limit + 2 : 0; step < limit - 1; step++)
				{
					var x = (int)Math.Round(center + step * dx); var y = (int)Math.Round(center + step * dy);
					var land = passable[y * size + x];
					if (land && !previous)
					{
						if (!geometry.Gates.Any(g => Math.Abs(geometry.Along(x, y) - g) <= geometry.Width / 2D + 3))
							throw new RmgGenerationRejectedException("DIVIDED_LANDS_EXTRA_CROSSING", "An unplanned land crossing joins home territories.");
						count++;
					}

					previous = land;
				}

				if (count != (int)settings.LandCrossings)
					throw new RmgGenerationRejectedException("DIVIDED_LANDS_CROSSING_COUNT", $"Expected {(int)settings.LandCrossings} crossings per border, found {count}.");
				counts.Add(count);
			}

			// Close just the intended crossing gates. Any surviving connection is an accidental bypass.
			var closed = (bool[])passable.Clone();
			for (var i = 0; i < closed.Length; i++)
				if (geometry.Distance(i % size, i / size) <= 4 && geometry.Gate(i % size, i / size, geometry.Gates, geometry.Width / 2D + 3)) closed[i] = false;
			var labels = new int[closed.Length]; var group = 0;
			foreach (var start in starts)
			{
				var index = start.Y * size + start.X;
				if (!closed[index] || labels[index] != 0)
					throw new RmgGenerationRejectedException("DIVIDED_LANDS_BYPASS", "Home territories remain joined after closing the intended crossings.");
				var queue = new Queue<int>(); queue.Enqueue(index); labels[index] = ++group;
				while (queue.TryDequeue(out var current))
					for (var dy = -1; dy <= 1; dy++)
						for (var dx = -1; dx <= 1; dx++)
						{
							var x = current % size + dx; var y = current / size + dy;
							if (x < 0 || y < 0 || x >= size || y >= size) continue;
							var next = y * size + x;
							if (!closed[next] || labels[next] != 0) continue;
							labels[next] = group; queue.Enqueue(next);
						}
			}

			return new JObject { ["crossings_per_border"] = new JArray(counts), ["isolated_home_territories"] = group, ["unplanned_bypasses"] = 0 };
		}
	}
}
