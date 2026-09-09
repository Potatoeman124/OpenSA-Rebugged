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
	public static partial class RmgGenerator
	{
		static int BattlefieldDepth(TerrainComplexity complexity) => complexity switch
		{
			TerrainComplexity.Low => 0, TerrainComplexity.Standard => 1, TerrainComplexity.High => 2,
			TerrainComplexity.Extreme => 3, _ => 4
		};

		static RmgPoint[][] OrderBattlefieldSites(RmgGenerationSettings settings, List<RmgPoint> starts)
		{
			var size = settings.MapSize;
			var candidates = Enumerable.Range(0, size * size)
				.Where(i => RmgMirroring.Canonical(i, size, settings.MirroringAxes, settings.Seed) == i)
				.Select(i => new RmgPoint(i % size, i / size))
				.Where(p => p.X >= 8 && p.Y >= 8 && p.X < size - 8 && p.Y < size - 8 &&
					(size / 2 - 1 - Math.Min(p.X, size - 1 - p.X)) % 8 == 0 && (size / 2 - 1 - Math.Min(p.Y, size - 1 - p.Y)) % 8 == 0)
				.Select(p => RmgMirroring.Points(p, size, settings.MirroringAxes, settings.Seed)).Where(o => o.Length == settings.PlayerCount)
				.OrderBy(o => TerrainComparison.Mix(settings.Seed, (ulong)(1802 + o[0].Y * size + o[0].X))).ToArray();
			var separation = candidates.Select(o => starts.Concat(o.Skip(1)).Min(p => RegionDistanceSquared(o[0], p))).ToArray();
			var ordered = new List<RmgPoint[]>();

			// Space opportunities across the whole arena, including its contested interior.
			// The order is fixed before choosing species or applying terrain parameters.
			for (var count = 0; count < candidates.Length; count++)
			{
				var chosen = -1;
				for (var i = 0; i < candidates.Length; i++)
					if (separation[i] >= 0 && (chosen == -1 || separation[i] > separation[chosen])) chosen = i;
				var points = candidates[chosen];
				ordered.Add(points); separation[chosen] = -1;
				for (var i = 0; i < candidates.Length; i++)
					if (separation[i] >= 0)
						foreach (var p in points) separation[i] = Math.Min(separation[i], RegionDistanceSquared(candidates[i][0], p));
			}

			return ordered.ToArray();
		}

		static (bool[] Clear, bool[] Land, JObject Report) BattlefieldLanes(RmgGenerationSettings settings, List<RmgPoint> starts,
			RmgActorPlan[] colonies, RegionsSites sites)
		{
			var size = settings.MapSize;
			var clear = new bool[size * size];
			var land = new bool[size * size];
			var width = RmgBattlefieldParameters.Width(settings.LaneWidth);
			var depth = BattlefieldDepth(settings.TerrainComplexity);
			void Box(int left, int top, int right, int bottom)
			{
				for (var y = Math.Max(0, top); y <= Math.Min(size - 1, bottom); y++)
					for (var x = Math.Max(0, left); x <= Math.Min(size - 1, right); x++) clear[y * size + x] = true;
			}

			var segments = new List<(RmgPoint From, RmgPoint To)>();
			void Segment(RmgPoint a, RmgPoint b)
			{
				if (a == b) return;
				segments.Add((a, b));
				var dx = b.X - a.X; var dy = b.Y - a.Y;
				var lengthSquared = dx * dx + dy * dy;
				var half = width / 2D;
				for (var y = Math.Max(0, Math.Min(a.Y, b.Y) - width); y <= Math.Min(size - 1, Math.Max(a.Y, b.Y) + width); y++)
					for (var x = Math.Max(0, Math.Min(a.X, b.X) - width); x <= Math.Min(size - 1, Math.Max(a.X, b.X) + width); x++)
					{
						var t = Math.Clamp(((x - a.X) * dx + (y - a.Y) * dy) / (double)lengthSquared, 0, 1);
						var ex = x - a.X - t * dx; var ey = y - a.Y - t * dy;
						if (ex * ex + ey * ey <= half * half) land[y * size + x] = true;
					}
			}

			void Route(RmgPoint a, RmgPoint b)
			{
				var dx = b.X - a.X; var dy = b.Y - a.Y;
				var salt = TerrainComparison.Mix(settings.Seed, (ulong)(a.Y * size + a.X + 1805));
				var horizontal = Math.Abs(dx) >= Math.Abs(dy);
				var offset = (int)Math.Round(Math.Min(Math.Sqrt(dx * dx + dy * dy) / 4, depth * 4)) * ((salt & 1) == 0 ? 1 : -1);
				var middle = horizontal ?
					new RmgPoint((a.X + b.X) / 2, Math.Clamp((a.Y + b.Y) / 2 + offset, 8, size - 9)) :
					new RmgPoint(Math.Clamp((a.X + b.X) / 2 + offset, 8, size - 9), (a.Y + b.Y) / 2);
				var waypoints = depth == 0 ? new[] { a, b } : new[] { a, middle, b };
				for (var i = 1; i < waypoints.Length; i++)
				{
					var from = waypoints[i - 1]; var to = waypoints[i];
					var corner = horizontal ? new RmgPoint(to.X, from.Y) : new RmgPoint(from.X, to.Y);
					var bevel = Math.Min(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y));
					if (settings.BlockShape == RmgBattlefieldBlockShape.Rectangles) bevel = 0;
					else if (settings.BlockShape == RmgBattlefieldBlockShape.CutCorners) bevel /= 2;
					var before = horizontal ? new RmgPoint(corner.X - Math.Sign(to.X - from.X) * bevel, corner.Y) :
						new RmgPoint(corner.X, corner.Y - Math.Sign(to.Y - from.Y) * bevel);
					var after = horizontal ? new RmgPoint(corner.X, corner.Y + Math.Sign(to.Y - from.Y) * bevel) :
						new RmgPoint(corner.X + Math.Sign(to.X - from.X) * bevel, corner.Y);
					Segment(from, before); Segment(before, after); Segment(after, to);
				}
			}

			var edges = new HashSet<(int, int)>();
			void MirroredRoute(RmgPoint a, RmgPoint b)
			{
				var pairs = new List<(int A, int B)>();
				for (var transform = 0; transform < 8; transform++)
				{
					if (settings.MirroringAxes == 1 && transform != 0 && transform != ((settings.Seed & 1) == 0 ? 1 : 2)) continue;
					if (settings.MirroringAxes == 2 && transform >= 4) continue;
					int Index(RmgPoint p)
					{
						var x = (transform & 1) == 0 ? p.X : size - 1 - p.X;
						var y = (transform & 2) == 0 ? p.Y : size - 1 - p.Y;
						return transform < 4 ? y * size + x : x * size + y;
					}

					var ia = Index(a); var ib = Index(b);
					pairs.Add((Math.Min(ia, ib), Math.Max(ia, ib)));
				}

				var pair = pairs.OrderBy(p => p.A).ThenBy(p => p.B).First();
				if (edges.Add(pair)) Route(new RmgPoint(pair.A % size, pair.A / size), new RmgPoint(pair.B % size, pair.B / size));
			}

			var objectives = starts.Concat(colonies.Select(RmgMirroring.Native)).ToArray();
			var nodes = new[] { new RmgPoint(size / 2 - 1, size / 2 - 1) }.Concat(objectives).ToArray();
			var connected = new bool[nodes.Length]; connected[0] = true;
			var distances = nodes.Select(p => RegionDistanceSquared(p, nodes[0])).ToArray();
			var parents = new int[nodes.Length];

			// A minimum spanning network avoids reserving an entire street across the map
			// for every objective. Reflection adds matching routes and useful local loops.
			for (var count = 1; count < nodes.Length; count++)
			{
				var chosen = -1;
				for (var i = 1; i < nodes.Length; i++)
					if (!connected[i] && (chosen == -1 || distances[i] < distances[chosen])) chosen = i;
				MirroredRoute(nodes[parents[chosen]], nodes[chosen]); connected[chosen] = true;
				for (var i = 1; i < nodes.Length; i++)
				{
					var distance = RegionDistanceSquared(nodes[i], nodes[chosen]);
					if (!connected[i] && distance < distances[i]) { distances[i] = distance; parents[i] = chosen; }
				}
			}

			foreach (var point in objectives)
			{
				var radius = starts.Contains(point) ? 8 : 5;
				Box(point.X - radius, point.Y - radius, point.X + radius, point.Y + radius);
			}

			// Include actual species footprints/exits; artwork itself is not rotated.
			foreach (var cell in sites.ReservedCells) Box(cell.X - 2, cell.Y - 2, cell.X + 2, cell.Y + 2);
			for (var i = 0; i < clear.Length; i++) land[i] |= clear[i];
			foreach (var mask in new[] { clear, land })
			{
				var original = (bool[])mask.Clone();
				for (var i = 0; i < mask.Length; i++)
					if (original[i]) foreach (var member in RmgMirroring.Orbit(i, size, settings.MirroringAxes, settings.Seed)) mask[member] = true;
			}

			return (clear, land, new JObject
			{
				["network_edges"] = nodes.Length - 1, ["unique_edge_orbits"] = edges.Count, ["segments_before_reflection"] = segments.Count,
				["centerline_length_before_reflection"] = segments.Sum(s => Math.Sqrt(RegionDistanceSquared(s.From, s.To))),
				["objective_points"] = new JArray(objectives.Select(p => p.ToString()))
			});
		}
	}
}
