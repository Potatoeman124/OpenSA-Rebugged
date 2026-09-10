#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OpenRA.Mods.OpenSA.Rmg.Reassessment;

namespace OpenRA.Mods.OpenSA.Rmg
{
	static class CrossroadsTopology
	{
		public static JObject Validate(RmgLogicalMap map, RmgGenerationSettings settings, List<RmgPoint> starts, JObject plan)
		{
			var size = settings.MapSize; var center = (size - 1) / 2D;
			var angles = ((JArray)plan["divider_angles"]).Values<double>().ToArray();
			var tiers = ((JArray)plan["side_connection_radii_native"]).Values<double>().ToArray();
			var bridge = (double)plan["bridge_half_width_native"];
			var junction = Math.Max((int)plan["junction_radius_native"] + 3,
				((int)plan["approach_width_native"] / 2D + 2) / Math.Sin(Math.PI / settings.PlayerCount) + 8);
			var cells = TerrainComparison.NativeBytes(map);
			var counts = new List<int>();
			foreach (var angle in angles)
			{
				var dx = Math.Cos(angle); var dy = Math.Sin(angle); var scale = Math.Max(Math.Abs(dx), Math.Abs(dy));
				var limit = (int)(center / scale); var count = 0; var previous = true; var firstWater = false;
				for (var step = 0; step <= limit; step++)
				{
					var x = (int)Math.Round(center + step * dx); var y = (int)Math.Round(center + step * dy);
					var land = cells[y * size + x] != (byte)RmgNativeTerrainIntent.Water;
					if (!land) firstWater = true;
					if (land && !previous && step < limit - 1)
					{
						var r = step * scale;
						if (!tiers.Any(t => Math.Abs(r - t) <= bridge + 3))
							throw new RmgGenerationRejectedException("CROSSROADS_EXTRA_CROSSING", "A terrain divider contains an unplanned crossing.");
						count++;
					}

					previous = land;
				}

				if (!firstWater || count != tiers.Length)
					throw new RmgGenerationRejectedException("CROSSROADS_TIER_COUNT", $"Expected {tiers.Length} separate side crossings, found {count}.");
				counts.Add(count);
			}

			// Close only the junction and the gates through the dividers. No other terrain is removed.
			// Any remaining path between starts is an unintended bypass, including a route along the map edge.
			var closed = new bool[cells.Length];
			for (var i = 0; i < cells.Length; i++)
			{
				var x = i % size - center; var y = i / size - center;
				var r = Math.Max(Math.Abs(x), Math.Abs(y));
				closed[i] = cells[i] == (byte)RmgNativeTerrainIntent.Water || x * x + y * y <= junction * junction ||
					(tiers.Any(t => Math.Abs(r - t) <= bridge + 4) && angles.Any(a => x * Math.Cos(a) + y * Math.Sin(a) >= 0 && Math.Abs(-x * Math.Sin(a) + y * Math.Cos(a)) <= 3));
			}

			var labels = new int[cells.Length]; var group = 0;
			foreach (var start in starts)
			{
				var index = start.Y * size + start.X;
				if (closed[index] || labels[index] != 0)
					throw new RmgGenerationRejectedException("CROSSROADS_UNPLANNED_BYPASS", "Starts remain connected after closing the intended junction and side crossings.");
				group++; var queue = new Queue<int>(); queue.Enqueue(index); labels[index] = group;
				while (queue.TryDequeue(out var current))
					for (var dy = -1; dy <= 1; dy++)
						for (var dx = -1; dx <= 1; dx++)
						{
							var x = current % size + dx; var y = current / size + dy;
							if (x < 0 || y < 0 || x >= size || y >= size) continue;
							var next = y * size + x;
							if (closed[next] || labels[next] != 0) continue;
							labels[next] = group; queue.Enqueue(next);
						}
			}

			return new JObject
			{
				["crossings_per_divider"] = new JArray(counts), ["isolated_start_sectors"] = group,
				["unplanned_bypasses"] = 0, ["junction_closure_radius_native"] = junction
			};
		}
	}
}
