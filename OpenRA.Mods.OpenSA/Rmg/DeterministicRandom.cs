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

using System;
using System.Security.Cryptography;
using System.Text;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public sealed class DeterministicRandom
	{
		ulong state;

		public DeterministicRandom(ulong seed)
		{
			state = seed;
		}

		public static DeterministicRandom ForStream(RmgGenerationSettings settings, RmgProfile profile, string streamName)
		{
			var canonical = profile.UsesClearLandDetails && streamName != "terrain-clear-land-details" ?
				InheritedShorelineCanonical(settings) :
				settings.Canonical(profile);
			var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical + "\nstream=" + streamName));
			ulong seed = 0;
			for (var i = 0; i < sizeof(ulong); i++)
				seed |= (ulong)bytes[i] << (8 * i);

			return new DeterministicRandom(seed);
		}

		static string InheritedShorelineCanonical(RmgGenerationSettings settings) => string.Join("\n", new[]
		{
			"profile=normal-water-shoreline-v3",
			"configuration=1",
			"generator=3",
			$"seed={settings.Seed}",
			$"players={settings.PlayerCount}",
			$"symmetry={settings.Symmetry}",
			$"archetype={settings.Archetype}",
			$"colonies={settings.NeutralColonyCount}",
			$"topology={RmgTopologyPreset.Shoreline}"
		});

		public ulong NextUInt64()
		{
			state += 0x9E3779B97F4A7C15UL;
			var value = state;
			value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
			value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
			return value ^ (value >> 31);
		}

		public int NextInt(int maximumExclusive)
		{
			if (maximumExclusive <= 0)
				throw new ArgumentOutOfRangeException(nameof(maximumExclusive));

			var bound = (ulong)maximumExclusive;
			var limit = ulong.MaxValue - ulong.MaxValue % bound;
			ulong value;
			do
				value = NextUInt64();
			while (value >= limit);

			return (int)(value % bound);
		}

		public int NextInt(int minimumInclusive, int maximumExclusive)
		{
			if (minimumInclusive >= maximumExclusive)
				throw new ArgumentOutOfRangeException(nameof(maximumExclusive));

			return minimumInclusive + NextInt(maximumExclusive - minimumInclusive);
		}
	}
}
