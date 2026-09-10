#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public enum RmgCrossroadsConnections { None, Standard, Many }

	public static class RmgCrossroadsParameters
	{
		public static string Name(RmgCrossroadsConnections value) => value.ToString().ToLowerInvariant();
		public static RmgCrossroadsConnections Parse(string value) => value switch
		{
			"none" => RmgCrossroadsConnections.None, "standard" => RmgCrossroadsConnections.Standard,
			"many" => RmgCrossroadsConnections.Many, _ => throw new ArgumentException("side_connections must be none, standard or many.")
		};

		public static void ValidateOptions(RmgPlayerSettings settings)
		{
			if (!Enum.IsDefined(settings.SideConnections) || (!settings.IsCrossroads && settings.SideConnections != RmgCrossroadsConnections.Standard))
				throw new ArgumentException("Side Connections requires Crossroads and a valid choice.");
			if (settings.LayoutFamily == RmgPlayerLayoutFamily.Crossroads && (!settings.IsCrossroads || settings.PlayerCount is not (2 or 4 or 8)))
				throw new ArgumentException("Crossroads requires schema 13 and 2, 4 or 8 players.");
		}

		public static void Validate(RmgGenerationSettings settings)
		{
			if (settings.MirroringAxes != RmgBattlefieldParameters.Axes(settings.PlayerCount) || !Enum.IsDefined(settings.LaneWidth) ||
				!Enum.IsDefined(settings.SideConnections) || settings.BlockShape != RmgBattlefieldBlockShape.CutCorners)
				throw new ArgumentException("Crossroads requires matching player symmetry, valid approach width and side connections.");
		}
	}
}
