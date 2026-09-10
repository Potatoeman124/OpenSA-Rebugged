#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Linq;

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
