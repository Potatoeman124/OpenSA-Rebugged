#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Diagnostics;
using System.Linq;
using OpenRA.Mods.OpenSA.Graphics;
using OpenRA.Primitives;

namespace OpenRA.Mods.OpenSA.UtilityCommands
{
	public sealed partial class ValidateRmgOwnershipRuntimeCommand
	{
		static void CheckRangeUnion()
		{
			SelectionRange Circle(int x, int y, int radius, int z = 0) => new(new WPos(x, y, z), new WDist(radius), Color.LimeGreen);
			double Length(params SelectionRange[] circles) => RangeOutline.Union(circles).Sum(a => a.End - a.Start);
			var tau = RangeOutline.FullCircle;
			Require(Math.Abs(Length(Circle(0, 0, 1000)) - tau) < 1e-8, "Single circle lost range.");
			Require(Math.Abs(Length(Circle(0, 0, 1000), Circle(1000, 0, 1000)) - 8 * Math.PI / 3) < 1e-8, "Overlapping circles did not form the exact union.");
			Require(Math.Abs(Length(Circle(0, 0, 1000), Circle(3000, 0, 1000)) - 2 * tau) < 1e-8, "Separate groups were connected.");
			Require(Math.Abs(Length(Circle(0, 0, 1000), Circle(2000, 0, 1000)) - 2 * tau) < 1e-8, "Tangent circles lost their boundaries.");
			Require(Math.Abs(Length(Circle(0, 0, 1000), Circle(0, 0, 1000), Circle(100, 0, 200)) - tau) < 1e-8, "Coincident/contained circles leave internal lines.");
			Require(Math.Abs(Length(Circle(0, 0, 1000), Circle(1000, 700, 1000, 700)) - 8 * Math.PI / 3) < 1e-8, "Flying range projection is inconsistent.");
			Require(Length(Circle(0, 0, 0)) == 0, "Zero range produces an outline.");
			var random = new Random(5193);
			var samples = 0;
			for (var scenario = 0; scenario < 120; scenario++)
			{
				var circles = Enumerable.Range(0, 1 + random.Next(30)).Select(_ => Circle(random.Next(-5000, 5001), random.Next(-5000, 5001), random.Next(50, 2001))).ToArray();
				var arcs = RangeOutline.Union(circles);
				for (var i = 0; i < circles.Length; i++)
					for (var sample = 0; sample < 180; sample++)
					{
						var angle = tau * (sample + 0.371) / 180;
						var x = circles[i].Position.X + circles[i].Radius.Length * Math.Cos(angle);
						var y = circles[i].Position.Y + circles[i].Radius.Length * Math.Sin(angle);
						var covered = circles.Where((_, j) => j != i).Any(c =>
							(x - c.Position.X) * (x - c.Position.X) + (y - c.Position.Y) * (y - c.Position.Y) < (double)c.Radius.Length * c.Radius.Length);
						var visible = arcs.Any(a => a.Circle == i && angle >= a.Start && angle <= a.End);
						Require(visible != covered, "Union omitted exposed range or retained an internal overlap.");
						samples++;
					}
			}

			var crowded = Enumerable.Range(0, 1000).Select(_ => Circle(random.Next(20000), random.Next(20000), random.Next(100, 3000))).ToArray();
			var timer = Stopwatch.StartNew();
			var boundary = RangeOutline.Union(crowded);
			Require(boundary.Length > 0, "Large selection lost its outline.");
			Console.WriteLine($"PASS: exact circle union edge cases and {samples} independent boundary samples; 1000 ranges -> {boundary.Length} arcs in {timer.ElapsedMilliseconds} ms.");
		}
	}
}
