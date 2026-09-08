#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Linq;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public static class RmgMirroring
	{
		public static int GroupSize(int axes) => axes switch { 1 => 2, 2 => 4, 4 => 8, _ => throw new ArgumentException("Mirroring axes must be 1, 2 or 4.") };
		public static void Validate(int axes, int size, int players)
		{
			var group = GroupSize(axes);
			if (players < group || players > (size == 64 ? 4 : 8) || players % group != 0)
				throw new ArgumentException($"{axes} mirroring axes require complete groups of {group} players, up to {(size == 64 ? 4 : 8)} on this size.");
		}

		// Reflection is around the centers of the square, on either cell or vertex lattices.
		// A seed-fixed orientation makes the one-axis option horizontal or vertical.
		public static int Canonical(int index, int width, int axes, ulong seed)
		{
			if (axes == 0) return index;
			var x = index % width; var y = index / width;
			if (axes == 1)
				return (seed & 1) == 0 ? y * width + Math.Min(x, width - 1 - x) : Math.Min(y, width - 1 - y) * width + x;
			x = Math.Min(x, width - 1 - x); y = Math.Min(y, width - 1 - y);
			return axes == 4 ? Math.Min(x, y) * width + Math.Max(x, y) : y * width + x;
		}

		public static int[] Orbit(int index, int width, int axes, ulong seed)
		{
			if (axes == 0) return new[] { index };
			var x = index % width; var y = index / width; var nx = width - 1 - x; var ny = width - 1 - y;
			if (axes == 1) return new[] { index, (seed & 1) == 0 ? y * width + nx : ny * width + x }.Distinct().ToArray();
			var members = new[] { index, y * width + nx, ny * width + x, ny * width + nx };
			return axes == 4 ? members.Concat(members.Select(i => i % width * width + i / width)).Distinct().ToArray() : members.Distinct().ToArray();
		}

		public static RmgPoint[] Points(RmgPoint point, int size, int axes, ulong seed) =>
			Orbit(point.Y * size + point.X, size, axes, seed).Select(i => new RmgPoint(i % size, i / size)).ToArray();

		public static RmgPoint Native(RmgActorPlan actor) => new(2 * actor.LogicalLocation.X + actor.NativeFrame % 2,
			2 * actor.LogicalLocation.Y + actor.NativeFrame / 2);

		public static RmgActorPlan Actor(string type, string owner, string role, RmgPoint native, int index) =>
			new(type, owner, role, new RmgPoint(native.X / 2, native.Y / 2), index, native.X % 2 + 2 * (native.Y % 2));

		public static bool[] Select(double[] values, bool[] allowed, int width, int target, bool vertices, int axes, ulong seed)
		{
			var result = new bool[values.Length];
			var used = 0;
			foreach (var i in Enumerable.Range(0, values.Length).Where(i => Canonical(i, width, axes, seed) == i)
				.OrderByDescending(i => values[i]).ThenBy(i => i))
			{
				if (used >= target) break;
				var orbit = Orbit(i, width, axes, seed);
				if (orbit.Any(p => !allowed[p])) continue;
				foreach (var p in orbit)
				{
					result[p] = true;
					used += vertices ? (p % width == 0 || p % width == width - 1 ? 1 : 2) * (p / width == 0 || p / width == width - 1 ? 1 : 2) : 1;
				}
			}

			return result;
		}

		public static int TerrainMismatches(byte[] native, int size, int axes, ulong seed) =>
			Enumerable.Range(0, native.Length).Count(i => native[i] != native[Canonical(i, size, axes, seed)]);
	}
}
