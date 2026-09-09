#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public enum RmgBattlefieldBlockShape { Rectangles, CutCorners, Diamonds }
	public enum RmgBattlefieldLaneWidth { Narrow, Standard, Wide }

	public static class RmgBattlefieldParameters
	{
		public static int Axes(int players) => players switch { 2 => 1, 4 => 2, 8 => 4, _ => throw new ArgumentException("Artificial Battlefield supports 2, 4 or 8 players.") };
		public static string Name(RmgBattlefieldBlockShape shape) => shape switch
		{
			RmgBattlefieldBlockShape.Rectangles => "rectangles", RmgBattlefieldBlockShape.CutCorners => "cut-corners",
			RmgBattlefieldBlockShape.Diamonds => "diamonds", _ => throw new ArgumentOutOfRangeException(nameof(shape))
		};
		public static string Name(RmgBattlefieldLaneWidth width) => width.ToString().ToLowerInvariant();
		public static RmgBattlefieldBlockShape ParseShape(string name) => name switch
		{
			"rectangles" => RmgBattlefieldBlockShape.Rectangles, "cut-corners" => RmgBattlefieldBlockShape.CutCorners,
			"diamonds" => RmgBattlefieldBlockShape.Diamonds, _ => throw new ArgumentException("block_shape must be rectangles, cut-corners or diamonds.")
		};
		public static RmgBattlefieldLaneWidth ParseLane(string name) => name switch
		{
			"narrow" => RmgBattlefieldLaneWidth.Narrow, "standard" => RmgBattlefieldLaneWidth.Standard,
			"wide" => RmgBattlefieldLaneWidth.Wide, _ => throw new ArgumentException("lane_width must be narrow, standard or wide.")
		};
		public static int Width(RmgBattlefieldLaneWidth width) => width switch
		{
			RmgBattlefieldLaneWidth.Narrow => 6, RmgBattlefieldLaneWidth.Standard => 10,
			RmgBattlefieldLaneWidth.Wide => 16, _ => throw new ArgumentOutOfRangeException(nameof(width))
		};
		public static void ValidateOptions(RmgPlayerSettings settings)
		{
			if (!Enum.IsDefined(settings.BlockShape) || !Enum.IsDefined(settings.LaneWidth)) throw new ArgumentException("Invalid Battlefield geometry options.");
			if (!settings.IsPlannedBattlefield && (settings.BlockShape != RmgBattlefieldBlockShape.CutCorners || settings.LaneWidth != RmgBattlefieldLaneWidth.Standard))
				throw new ArgumentException("Block Shape and Lane Width require schema 12 and Artificial Battlefield.");
		}

		public static void Validate(RmgGenerationSettings settings)
		{
			if (settings.MirroringAxes != Axes(settings.PlayerCount) || !Enum.IsDefined(settings.BlockShape) || !Enum.IsDefined(settings.LaneWidth))
				throw new ArgumentException("Artificial Battlefield requires matching player symmetry and valid geometry options.");
		}
	}
}
