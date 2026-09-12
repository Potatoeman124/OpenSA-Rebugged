#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.OpenSA.Widgets
{
	public sealed class SelectedUnitRangeIconWidget : Widget
	{
		public Func<bool> IsActive = () => false;
		static readonly float2[] Ring = CreateRing();

		static float2[] CreateRing()
		{
			var points = new float2[33];
			for (var i = 0; i < points.Length; i++)
			{
				var angle = i * Math.PI / 16;
				points[i] = new float2(12 + 9 * (float)Math.Cos(angle), 12 + 9 * (float)Math.Sin(angle));
			}

			return points;
		}

		public override void Draw()
		{
			var color = IsActive() ? Color.FromArgb(255, 113, 212, 94) : Color.FromArgb(255, 64, 137, 61);
			var origin = RenderOrigin;
			var renderer = Game.Renderer.RgbaColorRenderer;
			void Line(float2 a, float2 b)
			{
				var start = new float3(origin.X + a.X, origin.Y + a.Y, 0);
				var end = new float3(origin.X + b.X, origin.Y + b.Y, 0);
				renderer.DrawLine(start, end, 3, Color.FromArgb(200, 23, 40, 21));
				renderer.DrawLine(start, end, 1.5f, color);
			}

			for (var i = 1; i < Ring.Length; i++)
				Line(Ring[i - 1], Ring[i]);
			Line(new float2(12, 12), new float2(21, 12));
			Line(new float2(10, 12), new float2(14, 12));
			Line(new float2(12, 10), new float2(12, 14));
		}
	}
}
