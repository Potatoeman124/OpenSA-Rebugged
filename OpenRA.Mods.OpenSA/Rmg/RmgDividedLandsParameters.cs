#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Linq;
using OpenRA.Mods.OpenSA.Rmg.Reassessment;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public enum RmgLandCrossings { None, One, Two }

	public static class RmgDividedLandsParameters
	{
		public static string Name(RmgLandCrossings value) => value.ToString().ToLowerInvariant();
		public static string Display(RmgLandCrossings value) => value == RmgLandCrossings.None ? "None" : value == RmgLandCrossings.One ? "One per border" : "Two per border";
		public static RmgLandCrossings Parse(string value) => value switch
		{
			"none" => RmgLandCrossings.None, "one" => RmgLandCrossings.One, "two" => RmgLandCrossings.Two,
			_ => throw new ArgumentException("land_crossings must be none, one or two.")
		};

		public static void ValidateOptions(RmgPlayerSettings settings)
		{
			if (!Enum.IsDefined(settings.LandCrossings) || (!settings.IsDividedLands && settings.LandCrossings != RmgLandCrossings.One))
				throw new ArgumentException("Land Crossings requires Divided Lands and a valid choice.");
			if (settings.LayoutFamily == RmgPlayerLayoutFamily.DividedLands && (!settings.IsDividedLands || settings.PlayerCount is not (2 or 4 or 8)))
				throw new ArgumentException("Divided Lands requires schema 15 and 2, 4 or 8 players.");
		}

		public static void Validate(RmgGenerationSettings settings)
		{
			if (settings.MirroringAxes != RmgBattlefieldParameters.Axes(settings.PlayerCount) || !Enum.IsDefined(settings.LandCrossings) ||
				!Enum.IsDefined(settings.LaneWidth) || settings.BlockShape != RmgBattlefieldBlockShape.CutCorners ||
				settings.SideConnections != RmgCrossroadsConnections.Standard || settings.RingShape != RmgRingShape.Round)
				throw new ArgumentException("Divided Lands requires matching player symmetry and valid crossing options.");
		}
	}

	sealed class DividedLandsGeometry
	{
		readonly RmgGenerationSettings settings;
		public readonly double Center;
		public readonly double MaxChannelHalf;
		public readonly double[] Angles;
		public readonly double[] Gates;
		public readonly int Width;
		public double Phase => TerrainComparison.Mix(settings.Seed, 2110) % 1024 / 1024D * Math.PI;

		public DividedLandsGeometry(RmgGenerationSettings settings)
		{
			this.settings = settings;
			Center = (settings.MapSize - 1) / 2D;
			MaxChannelHalf = settings.MapSize * (settings.PlayerCount == 2 ? .24 : settings.PlayerCount == 4 ? .145 : .06);
			Angles = settings.PlayerCount == 2 ? new[] { (settings.Seed & 1) == 0 ? Math.PI / 2 : 0 } :
				Enumerable.Range(0, settings.PlayerCount).Select(i => i * 2 * Math.PI / settings.PlayerCount).ToArray();
			Gates = Centers(settings.LandCrossings);
			Width = CrossingWidth(settings.LaneWidth);
		}

		public double Variation(double x, double y, int depth) => .80 + (.02 + depth * .04) *
			Math.Cos(Along(x, y) / settings.MapSize * Math.PI * (3 + depth * 2) + Phase);

		public int[] PotentialWaterDistances()
		{
			var size = settings.MapSize; var width = size / 2; var water = new bool[size * size];
			for (var i = 0; i < width * width; i++)
			{
				var canonical = RmgMirroring.Canonical(i, width, settings.MirroringAxes, settings.Seed);
				var x = 2 * (canonical % width) + .5; var y = 2 * (canonical / width) + .5;
				var possible = Enumerable.Range(0, 5).Any(depth => Distance(x, y) <= MaxChannelHalf * Variation(x, y, depth));
				if (!possible) continue;
				for (var frame = 0; frame < 4; frame++) water[(2 * (i / width) + frame / 2) * size + 2 * (i % width) + frame % 2] = true;
			}

			// Union of every complexity envelope keeps colony sites fixed when terrain controls change.
			// Shoreline normalization only removes water from these potentially wet native cells.
			return WaterDistances(water, size);
		}

		public static int[] WaterDistances(bool[] water, int size)
		{
			var distance = water.Select(w => w ? 0 : size * 2).ToArray();
			for (var i = 0; i < distance.Length; i++)
			{
				var x = i % size; var y = i / size;
				if (x > 0) distance[i] = Math.Min(distance[i], distance[i - 1] + 1);
				if (y == 0) continue;
				for (var dx = -1; dx <= 1; dx++)
					if (x + dx >= 0 && x + dx < size) distance[i] = Math.Min(distance[i], distance[i - size + dx] + 1);
			}

			for (var i = distance.Length - 1; i >= 0; i--)
			{
				var x = i % size; var y = i / size;
				if (x < size - 1) distance[i] = Math.Min(distance[i], distance[i + 1] + 1);
				if (y == size - 1) continue;
				for (var dx = -1; dx <= 1; dx++)
					if (x + dx >= 0 && x + dx < size) distance[i] = Math.Min(distance[i], distance[i + size + dx] + 1);
			}

			return distance;
		}

		public int CrossingWidth(RmgBattlefieldLaneWidth value) => settings.MapSize == 64 ? 4 + 2 * (int)value :
			settings.MapSize == 128 && settings.PlayerCount == 8 ? 6 + 2 * (int)value : RmgBattlefieldParameters.Width(value);

		double[] Centers(RmgLandCrossings crossings) => crossings == RmgLandCrossings.None ? Array.Empty<double>() :
			settings.PlayerCount == 2 ? crossings == RmgLandCrossings.One ? new[] { 0D } : new[] { -.26 * settings.MapSize, .26 * settings.MapSize } :
			crossings == RmgLandCrossings.One ? new[] { .32 * settings.MapSize } : settings.MapSize == 64 ? new[] { .20 * settings.MapSize, .43 * settings.MapSize } : new[] { .23 * settings.MapSize, .40 * settings.MapSize };

		public double Along(double x, double y) => settings.PlayerCount == 2 ? (settings.Seed & 1) == 0 ? y - Center : x - Center :
			Math.Max(Math.Abs(x - Center), Math.Abs(y - Center));

		public double Distance(double x, double y)
		{
			x -= Center; y -= Center;
			if (settings.PlayerCount == 2) return (settings.Seed & 1) == 0 ? Math.Abs(x) : Math.Abs(y);
			var best = double.MaxValue;
			foreach (var a in Angles)
				if (x * Math.Cos(a) + y * Math.Sin(a) >= 0) best = Math.Min(best, Math.Abs(-x * Math.Sin(a) + y * Math.Cos(a)));
			return best;
		}

		public bool Gate(double x, double y, double[] gates, double halfWidth) =>
			Distance(x, y) <= MaxChannelHalf + 8 && gates.Any(g => Math.Abs(Along(x, y) - g) <= halfWidth);
	}
}
