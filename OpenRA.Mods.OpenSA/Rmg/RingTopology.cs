#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OpenRA.Mods.OpenSA.Rmg.Reassessment;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public static class RingTopology
	{
		// A nonzero winding cycle proves that occupied ground still offers a complete trip around the lake.
		// Cardinal edges deliberately reject loops that depend on slipping through touching actor corners.
		public static bool HasGroundLoop(bool[] passable, int width, int height, int origin)
		{
			if (origin < 0 || !passable[origin]) return false;
			var visited = new bool[passable.Length]; var turns = new int[passable.Length]; var queue = new Queue<int>();
			var centerX = (width - 1) / 2D; var centerY = (height - 1) / 2D;
			double Angle(int i) => Math.Atan2(i / width - centerY, i % width - centerX);
			queue.Enqueue(origin); visited[origin] = true;
			while (queue.Count > 0)
			{
				var current = queue.Dequeue(); var angle = Angle(current);
				foreach (var offset in new[] { -1, 1, -width, width })
				{
					var next = current + offset;
					if (next < 0 || next >= passable.Length || Math.Abs(next % width - current % width) + Math.Abs(next / width - current / width) != 1 || !passable[next]) continue;
					var delta = Angle(next) - angle;
					var winding = turns[current] + (delta > Math.PI ? -1 : delta < -Math.PI ? 1 : 0);
					if (visited[next]) { if (turns[next] != winding) return true; }
					else { visited[next] = true; turns[next] = winding; queue.Enqueue(next); }
				}
			}

			return false;
		}

		public static JObject Validate(RmgLogicalMap map, RmgGenerationSettings settings, JObject plan)
		{
			var size = settings.MapSize; var center = (size - 1) / 2D;
			var native = TerrainComparison.NativeBytes(map);
			var radius = (double)plan["ring_radius_native"];
			var centerCell = size / 2 * size + size / 2;
			if (native[centerCell] != 1) throw new InvalidOperationException("Ring central lake is missing.");
			var seen = new bool[native.Length]; var queue = new Queue<int>(); queue.Enqueue(centerCell); seen[centerCell] = true;
			var lakeCells = 0;
			while (queue.Count > 0)
			{
				var i = queue.Dequeue(); var x = i % size; var y = i / size; lakeCells++;
				if (x == 0 || y == 0 || x == size - 1 || y == size - 1) throw new InvalidOperationException("Ring lake reaches the edge: land loop is broken.");
				for (var dy = -1; dy <= 1; dy++)
					for (var dx = -1; dx <= 1; dx++)
					{
						var next = (y + dy) * size + x + dx;
						if (!seen[next] && native[next] == 1) { seen[next] = true; queue.Enqueue(next); }
					}
			}

			var routeCells = 0;
			for (var i = 0; i < native.Length; i++)
				if (Math.Abs(RmgRingParameters.Radius(i % size - center, i / size - center, settings.RingShape) - radius) <= 1)
				{
					if (native[i] != 0) throw new InvalidOperationException("Ring centerline is not continuous clear terrain.");
					routeCells++;
				}

			return new JObject { ["enclosed_central_lake"] = true, ["central_lake_cells"] = lakeCells, ["clear_loop_cells"] = routeCells };
		}
	}
}
