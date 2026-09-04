#region Copyright & License Information
/*
 * Copyright The OpenSA Developers (see CREDITS)
 * This file is part of OpenSA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Collections.Generic;

namespace OpenRA.Mods.OpenSA.Rmg.NaturalPrototype
{
	public enum NaturalTerrainSemantic : byte
	{
		Clear = 0,
		Water = 1,
		Rock = 2,
		Vegetation = 3
	}

	public sealed class NaturalTerrainPrototypeBasin
	{
		public double CenterX { get; init; }
		public double CenterY { get; init; }
		public double RadiusMajor { get; init; }
		public double RadiusMinor { get; init; }
		public double AngleRadians { get; init; }
		public double Strength { get; init; }
		public double Modulation { get; init; }
	}

	public sealed class NaturalTerrainPrototypeCandidate
	{
		public NaturalTerrainPrototypeSettings Settings { get; init; }
		public IReadOnlyDictionary<string, ulong> StreamSeeds { get; init; }
		public IReadOnlyList<NaturalTerrainPrototypeBasin> Basins { get; init; }
		public IReadOnlyDictionary<string, float[]> Fields { get; init; }
		public byte[] Semantic { get; init; }
		public double WaterThreshold { get; init; }
		public double RockThreshold { get; init; }
		public double VegetationThreshold { get; init; }
		public double WaterTarget { get; init; }
		public double RockTarget { get; init; }
		public double VegetationTarget { get; init; }
		public IReadOnlyDictionary<string, int> SurfaceAdjacencyCounts { get; init; }
		public int ForbiddenSurfaceAdjacencyCount { get; init; }
	}
}
