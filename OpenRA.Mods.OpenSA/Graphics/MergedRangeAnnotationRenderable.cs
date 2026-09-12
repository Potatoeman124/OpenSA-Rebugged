#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Primitives;

namespace OpenRA.Mods.OpenSA.Graphics
{
	public sealed record SelectionRange(WPos Position, WDist Radius, Color Color);
	public sealed record RangeOutlineArc(int Circle, double Start, double End);

	public static class RangeOutline
	{
		public const double FullCircle = 2 * Math.PI;
		const double Epsilon = 1e-9;

		// Subtract the portions of each circumference covered by any other disc. This preserves
		// concave edges, separate groups and uncovered holes; a convex hull would invent extra reach.
		public static RangeOutlineArc[] Union(IReadOnlyList<SelectionRange> circles)
		{
			var result = new List<RangeOutlineArc>();
			var covered = new List<(double Start, double End)>();
			for (var i = 0; i < circles.Count; i++)
			{
				var a = circles[i];
				var radius = (double)a.Radius.Length;
				if (radius <= 0)
					continue;

				covered.Clear();
				var contained = false;
				for (var j = 0; j < circles.Count; j++)
				{
					if (i == j || circles[j].Radius.Length <= 0)
						continue;

					var b = circles[j];
					var otherRadius = (double)b.Radius.Length;

					// WorldRenderer projects Y-Z onto the screen. Compare in this plane so
					// flying and ground circles merge exactly where their displayed ranges overlap.
					var dx = (double)b.Position.X - a.Position.X;
					var dy = (double)b.Position.Y - b.Position.Z - a.Position.Y + a.Position.Z;
					if (Math.Abs(dx) >= radius + otherRadius || Math.Abs(dy) >= radius + otherRadius)
						continue;

					var distanceSquared = dx * dx + dy * dy;
					var distance = Math.Sqrt(distanceSquared);
					if (distance + radius <= otherRadius)
					{
						// Equal, coincident circles need exactly one surviving boundary.
						if (distance > 0 || radius < otherRadius || j < i)
						{
							contained = true;
							break;
						}

						continue;
					}

					if (distance >= radius + otherRadius || distance + otherRadius <= radius)
						continue;

					var direction = Math.Atan2(dy, dx);
					if (direction < 0)
						direction += FullCircle;
					var cosine = (distanceSquared + radius * radius - otherRadius * otherRadius) / (2 * distance * radius);
					var half = Math.Acos(Math.Clamp(cosine, -1, 1));
					var start = direction - half;
					var end = direction + half;
					if (start < 0)
					{
						covered.Add((start + FullCircle, FullCircle));
						start = 0;
					}

					if (end > FullCircle)
					{
						covered.Add((0, end - FullCircle));
						end = FullCircle;
					}

					covered.Add((start, end));
				}

				if (contained)
					continue;

				covered.Sort((a, b) => a.Start.CompareTo(b.Start));
				var cursor = 0.0;
				foreach (var (start, end) in covered)
				{
					if (start > cursor + Epsilon)
						result.Add(new RangeOutlineArc(i, cursor, start));
					cursor = Math.Max(cursor, end);
				}

				if (cursor < FullCircle - Epsilon)
					result.Add(new RangeOutlineArc(i, cursor, FullCircle));
			}

			return result.ToArray();
		}
	}

	public sealed class MergedRangeAnnotationRenderable : IRenderable, IFinalizedRenderable
	{
		public IReadOnlyList<SelectionRange> Ranges { get; }
		public IReadOnlyList<RangeOutlineArc> Arcs { get; }
		public WPos Pos => Ranges.Count == 0 ? WPos.Zero : Ranges[0].Position;
		public int ZOffset { get; }
		public bool IsDecoration => true;

		public MergedRangeAnnotationRenderable(SelectionRange[] ranges, int zOffset = 0)
		{
			Ranges = ranges;
			Arcs = RangeOutline.Union(ranges);
			ZOffset = zOffset;
		}

		public IRenderable WithZOffset(int zOffset) => new MergedRangeAnnotationRenderable(Ranges.ToArray(), zOffset);
		public IRenderable OffsetBy(in WVec vec)
		{
			var offset = vec;
			return new MergedRangeAnnotationRenderable(Ranges.Select(r => r with { Position = r.Position + offset }).ToArray(), ZOffset);
		}

		public IRenderable AsDecoration() => this;
		public IFinalizedRenderable PrepareRender(WorldRenderer wr) => this;
		public Rectangle ScreenBounds(WorldRenderer wr) => Rectangle.Empty;
		public void RenderDebugGeometry(WorldRenderer wr) { }

		public void Render(WorldRenderer wr)
		{
			const double DashAngle = RangeOutline.FullCircle / 32;
			var renderer = Game.Renderer.RgbaColorRenderer;
			foreach (var arc in Arcs)
			{
				var range = Ranges[arc.Circle];
				var center = wr.ScreenPosition(range.Position);
				var rx = (double)range.Radius.Length * wr.TileSize.Width / wr.TileScale;
				var ry = (double)range.Radius.Length * wr.TileSize.Height / wr.TileScale;
				var step = Math.Min(Math.PI / 64, 2 * Math.Acos(Math.Clamp(1 - 0.35 / Math.Max(1, Math.Max(rx, ry) * wr.Viewport.Zoom), -1, 1)));
				float2 Point(double angle) => wr.Viewport.WorldToViewPx(center + new float2((float)(rx * Math.Cos(angle)), (float)(ry * Math.Sin(angle))));
				for (var dash = (int)(arc.Start / DashAngle); dash * DashAngle < arc.End; dash++)
				{
					var start = Math.Max(arc.Start, dash * DashAngle);
					var end = Math.Min(arc.End, (dash + 0.75) * DashAngle);
					for (var angle = start; angle < end; angle += step)
					{
						var a = Point(angle);
						var b = Point(Math.Min(end, angle + step));
						renderer.DrawLine(a, b, 3.5f, Color.FromArgb(150, Color.Black));
						renderer.DrawLine(a, b, 1.5f, range.Color);
					}
				}
			}
		}
	}
}
