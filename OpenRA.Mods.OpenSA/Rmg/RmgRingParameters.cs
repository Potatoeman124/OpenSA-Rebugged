#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public enum RmgRingShape { Round, Octagonal, Square }

	public static class RmgRingParameters
	{
		public static string Name(RmgRingShape shape) => shape.ToString().ToLowerInvariant();
		public static RmgRingShape Parse(string name) => name switch
		{
			"round" => RmgRingShape.Round, "octagonal" => RmgRingShape.Octagonal, "square" => RmgRingShape.Square,
			_ => throw new ArgumentException("ring_shape must be round, octagonal or square.")
		};

		public static double Radius(double x, double y, RmgRingShape shape)
		{
			x = Math.Abs(x); y = Math.Abs(y);
			return shape switch
			{
				RmgRingShape.Round => Math.Sqrt(x * x + y * y),
				RmgRingShape.Octagonal => Math.Max(Math.Max(x, y), (x + y) / Math.Sqrt(2)),
				_ => Math.Max(x, y)
			};
		}

		public static void ValidateOptions(RmgPlayerSettings settings)
		{
			if (!Enum.IsDefined(settings.RingShape) || (!settings.IsRing && settings.RingShape != RmgRingShape.Round))
				throw new ArgumentException("Ring Shape requires Ring and a valid choice.");
			if (settings.LayoutFamily == RmgPlayerLayoutFamily.Ring && (!settings.IsRing || settings.PlayerCount is not (2 or 4 or 8)))
				throw new ArgumentException("Ring requires schema 14 and 2, 4 or 8 players.");
		}

		public static void Validate(RmgGenerationSettings settings)
		{
			if (settings.MirroringAxes != RmgBattlefieldParameters.Axes(settings.PlayerCount) || !Enum.IsDefined(settings.RingShape) ||
				!Enum.IsDefined(settings.LaneWidth) || settings.BlockShape != RmgBattlefieldBlockShape.CutCorners || settings.SideConnections != RmgCrossroadsConnections.Standard)
				throw new ArgumentException("Ring requires matching player symmetry and valid ring geometry options.");
		}
	}
}
