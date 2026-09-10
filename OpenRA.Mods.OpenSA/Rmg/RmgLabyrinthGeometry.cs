#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.OpenSA.Rmg.Reassessment;

namespace OpenRA.Mods.OpenSA.Rmg
{
	sealed class LabyrinthGeometry
	{
		public readonly List<RmgPoint> Nodes = new();
		public readonly List<RmgPoint> Starts;
		public readonly List<RmgPoint> Chambers = new();
		public readonly List<RmgPoint> MacroNodes = new();
		public readonly List<(int A, int B)> MacroTree = new();
		public readonly List<(int A, int B)> Edges = new();
		public readonly List<(RmgPoint A, RmgPoint B)> Segments = new();
		public readonly double[] PassageDistance;
		public readonly bool[] Clear, Land, Walls;
		public readonly double HalfWidth;
		public readonly int ExtraEdgeCount, MazeNodeCount, SubdivisionX, SubdivisionY;
		public double RouteLength => Segments.Sum(s => Math.Sqrt(SquareDistance(s.A, s.B)));

		public LabyrinthGeometry(RmgGenerationSettings settings, RmgProfile profile)
		{
			var size = settings.MapSize; var depth = (int)settings.TerrainComplexity;
			Starts = SelectStarts(settings, profile);
			var blocks = size == 64 ? 1 : size == 128 ? 2 : size == 256 ? 3 : 6;
			(SubdivisionX, SubdivisionY) = depth switch { 0 => (1, 1), 1 => (2, 2), 2 => (3, 2), 3 => (3, 3), _ => (4, 4) };
			var fraction = settings.ExtraRoutes == RmgLabyrinthRoutes.Few ? .03 : settings.ExtraRoutes == RmgLabyrinthRoutes.Many ? .40 : .15;
			var extra = 0;
			var blockNodes = new List<int>[blocks * blocks];
			var allLocalExtras = new List<(int A, int B)>();
			var walls = new List<(RmgPoint A, RmgPoint B)>();
			for (var by = 0; by < blocks; by++)
				for (var bx = 0; bx < blocks; bx++)
				{
					var block = by * blocks + bx; var x0 = 2 + bx * (size - 4D) / blocks; var y0 = 2 + by * (size - 4D) / blocks;
					var span = (size - 4D) / blocks;
					MacroNodes.Add(new RmgPoint((int)Math.Round(x0 + span / 2), (int)Math.Round(y0 + span / 2)));
					var ids = blockNodes[block] = new List<int>();
					for (var y = 0; y < SubdivisionY; y++)
						for (var x = 0; x < SubdivisionX; x++)
						{
							var salt = TerrainComparison.Mix(settings.Seed, (ulong)(2340 + block * 100 + y * 10 + x));
							ids.Add(Nodes.Count);
							Nodes.Add(new RmgPoint((int)Math.Round(x0 + (x + .5) * span / SubdivisionX) + (int)(salt % 3) - 1,
								(int)Math.Round(y0 + (y + .5) * span / SubdivisionY) + (int)(salt / 3 % 3) - 1));
						}

					var tree = GridTree(SubdivisionX, SubdivisionY, TerrainComparison.Mix(settings.Seed, (ulong)(2350 + block)));
					Edges.AddRange(tree.Select(e => (ids[e.A], ids[e.B])));
					var additions = GridEdges(SubdivisionX, SubdivisionY).Except(tree).OrderBy(e => TerrainComparison.Mix(settings.Seed, (ulong)(2360 + block * 100 + e.A * 16 + e.B))).ToArray();
					allLocalExtras.AddRange(additions.Select(e => (ids[e.A], ids[e.B])));
					for (var x = 1; x < SubdivisionX; x++) walls.Add((new RmgPoint((int)Math.Round(x0 + x * span / SubdivisionX), (int)Math.Round(y0)), new RmgPoint((int)Math.Round(x0 + x * span / SubdivisionX), (int)Math.Round(y0 + span))));
					for (var y = 1; y < SubdivisionY; y++) walls.Add((new RmgPoint((int)Math.Round(x0), (int)Math.Round(y0 + y * span / SubdivisionY)), new RmgPoint((int)Math.Round(x0 + span), (int)Math.Round(y0 + y * span / SubdivisionY))));
				}

			MazeNodeCount = Nodes.Count;
			MacroTree.AddRange(GridTree(blocks, blocks, TerrainComparison.Mix(settings.Seed, 2370)));
			var macroExtras = GridEdges(blocks, blocks).Except(MacroTree).OrderBy(e => TerrainComparison.Mix(settings.Seed, (ulong)(2380 + e.A * blocks * blocks + e.B))).ToArray();
			var addedMacro = (int)Math.Round(macroExtras.Length * fraction); extra += addedMacro;
			foreach (var (a, b) in MacroTree.Concat(macroExtras.Take(addedMacro)))
			{
				var ca = MacroNodes[a]; var cb = MacroNodes[b]; var span = (size - 4D) / blocks;
				var offset = ((int)(TerrainComparison.Mix(settings.Seed, (ulong)(2390 + a * blocks * blocks + b)) % 101) - 50) * span / 500;
				var gate = new RmgPoint((int)Math.Round((ca.X + cb.X) / 2D + (ca.X == cb.X ? offset : 0)),
					(int)Math.Round((ca.Y + cb.Y) / 2D + (ca.Y == cb.Y ? offset : 0)));
				var id = Nodes.Count; Nodes.Add(gate);
				Edges.Add((Closest(blockNodes[a], gate), id)); Edges.Add((id, Closest(blockNodes[b], gate)));
			}

			var addedLocal = (int)Math.Round(allLocalExtras.Count * fraction); extra += addedLocal;
			Edges.AddRange(allLocalExtras.OrderBy(e => TerrainComparison.Mix(settings.Seed, (ulong)(2360 + e.A * MazeNodeCount + e.B))).Take(addedLocal)); ExtraEdgeCount = extra;
			for (var i = 1; i < blocks; i++)
			{
				var coordinate = (int)Math.Round(2 + i * (size - 4D) / blocks);
				walls.Add((new RmgPoint(coordinate, 2), new RmgPoint(coordinate, size - 2)));
				walls.Add((new RmgPoint(2, coordinate), new RmgPoint(size - 2, coordinate)));
			}

			foreach (var start in Starts)
			{
				var center = new RmgPoint(start.X + 2, start.Y + 2);
				var bx = Math.Clamp((center.X - 2) * blocks / (size - 4), 0, blocks - 1);
				var by = Math.Clamp((center.Y - 2) * blocks / (size - 4), 0, blocks - 1);
				var id = Nodes.Count; Nodes.Add(center); Edges.Add((id, Closest(blockNodes[by * blocks + bx], center)));
			}

			// Alcoves are chosen before colony requests are considered. Higher complexity
			// can supply more sites, but density and ownership cannot create terrain.
			Chambers.AddRange(Nodes.Take(MazeNodeCount).Where(p => p.X >= 11 && p.Y >= 11 && p.X < size - 11 && p.Y < size - 11)
				.OrderBy(p => TerrainComparison.Mix(settings.Seed, (ulong)(2400 + p.Y * size + p.X))).Take(Math.Max(3, size * size / 1700)));
			Chambers.AddRange(Starts.Select(p => new RmgPoint(p.X + 2, p.Y + 2)).Except(Chambers));
			HalfWidth = depth switch { 0 => 7D, 1 => 5.5, 2 => 4.25, 3 => 3.25, _ => 2.5 } + (settings.LaneWidth == RmgBattlefieldLaneWidth.Narrow ? 0 : settings.LaneWidth == RmgBattlefieldLaneWidth.Wide ? 2.5 : 1.25);
			foreach (var (a, b) in Edges) Segments.Add((Nodes[a], Nodes[b]));
			PassageDistance = Enumerable.Repeat((double)size, size * size).ToArray();
			foreach (var (a, b) in Segments)
				for (var y = Math.Max(0, Math.Min(a.Y, b.Y) - 48); y <= Math.Min(size - 1, Math.Max(a.Y, b.Y) + 48); y++)
					for (var x = Math.Max(0, Math.Min(a.X, b.X) - 48); x <= Math.Min(size - 1, Math.Max(a.X, b.X) + 48); x++)
						PassageDistance[y * size + x] = Math.Min(PassageDistance[y * size + x], StrongholdsGeometry.SegmentDistance(x, y, a, b));
			Clear = new bool[size * size]; Land = PassageDistance.Select(d => d <= HalfWidth).ToArray();
			foreach (var node in Nodes.Distinct())
			{
				var chamber = Chambers.Contains(node); var radius = chamber ? 9 : Math.Max(3, (int)Math.Ceiling(HalfWidth));
				for (var y = Math.Max(0, node.Y - radius); y <= Math.Min(size - 1, node.Y + radius); y++)
					for (var x = Math.Max(0, node.X - radius); x <= Math.Min(size - 1, node.X + radius); x++)
					{
						if (SquareDistance(node, new RmgPoint(x, y)) <= radius * radius) Land[y * size + x] = true;
						if (chamber && Math.Max(Math.Abs(x - node.X), Math.Abs(y - node.Y)) <= 6) Clear[y * size + x] = Land[y * size + x] = true;
					}
			}

			// Cell boundaries form real water separators. The protected passage mask cuts
			// only the planned openings; coverage ranking cannot erase the maze walls.
			Walls = new bool[size * size];
			foreach (var (a, b) in walls)
				for (var y = Math.Max(0, Math.Min(a.Y, b.Y) - 3); y <= Math.Min(size - 1, Math.Max(a.Y, b.Y) + 3); y++)
					for (var x = Math.Max(0, Math.Min(a.X, b.X) - 3); x <= Math.Min(size - 1, Math.Max(a.X, b.X) + 3); x++)
						if (StrongholdsGeometry.SegmentDistance(x, y, a, b) <= 2.5) Walls[y * size + x] = true;

			int Closest(IEnumerable<int> pool, RmgPoint p) => pool.OrderBy(i => SquareDistance(Nodes[i], p)).First();
		}

		static IEnumerable<(int A, int B)> GridEdges(int width, int height)
		{
			for (var i = 0; i < width * height; i++)
			{
				if (i % width < width - 1) yield return (i, i + 1);
				if (i < width * (height - 1)) yield return (i, i + width);
			}
		}

		static List<(int A, int B)> GridTree(int width, int height, ulong seed)
		{
			var random = new DeterministicRandom(seed); var result = new List<(int A, int B)>();
			var neighbors = Enumerable.Range(0, width * height).Select(i => GridEdges(width, height).Where(e => e.A == i || e.B == i).Select(e => e.A == i ? e.B : e.A).ToArray()).ToArray();
			var visited = new bool[width * height]; var stack = new Stack<int>(); stack.Push(random.NextInt(visited.Length)); visited[stack.Peek()] = true;
			while (stack.Count > 0)
			{
				var current = stack.Peek(); var next = neighbors[current].Where(n => !visited[n]).ToArray();
				if (next.Length == 0) { stack.Pop(); continue; }
				var chosen = next[random.NextInt(next.Length)]; visited[chosen] = true; stack.Push(chosen);
				result.Add((Math.Min(current, chosen), Math.Max(current, chosen)));
			}

			return result;
		}

		static List<RmgPoint> SelectStarts(RmgGenerationSettings settings, RmgProfile profile)
		{
			var size = settings.MapSize; var reference = new List<RmgPoint>();
			var count = size == 64 ? 3 : size == 128 ? 5 : size == 256 ? 11 : 22;
			var pitch = (size - 24D) / (count - 1);
			var random = new DeterministicRandom(TerrainComparison.Mix(settings.Seed, 2300));
			for (var y = 0; y < count; y++)
				for (var x = 0; x < count; x++)
					reference.Add(new RmgPoint((int)Math.Round(12 + x * pitch) + random.NextInt(3) - 1,
						(int)Math.Round(12 + y * pitch) + random.NextInt(3) - 1));
			var selected = new List<int>();
			for (var attempt = 0; attempt < reference.Count; attempt++)
			{
				selected.Clear();
				var order = Enumerable.Range(0, reference.Count).OrderBy(i => TerrainComparison.Mix(settings.Seed, (ulong)(2301 + i + attempt * reference.Count))).ToArray();
				foreach (var player in Enumerable.Range(0, settings.PlayerCount))
				{
					var candidate = order.Where(i => selected.All(j => profile.ColonyCombatRules.StartMarginAtNative(reference[i], reference[j]) >= 0))
						.OrderByDescending(i => selected.Count == 0 ? 0 : selected.Min(j => SquareDistance(reference[i], reference[j]))).Take(1).ToArray();
					if (candidate.Length == 0) break;
					selected.Add(candidate[0]);
				}

				if (selected.Count == settings.PlayerCount) break;
			}

			if (selected.Count != settings.PlayerCount) throw new RmgGenerationRejectedException("LABYRINTH_STARTS", "This size cannot fit safe labyrinth starts.");
			return selected.Select(i => new RmgPoint(reference[i].X - 2, reference[i].Y - 2)).ToList();
		}

		static long SquareDistance(RmgPoint a, RmgPoint b) => (long)(a.X - b.X) * (a.X - b.X) + (long)(a.Y - b.Y) * (a.Y - b.Y);
	}
}
