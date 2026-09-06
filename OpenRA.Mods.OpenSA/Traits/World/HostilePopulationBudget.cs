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

namespace OpenRA.Mods.OpenSA.Traits.World
{
	// Reservations cover both living units and units waiting for their emergence animation.
	public sealed class HostilePopulationBudget
	{
		public int Count { get; private set; }

		public int Reserve(int requested, int maximum)
		{
			if (requested < 0 || maximum < 0)
				throw new ArgumentOutOfRangeException(nameof(requested));
			var accepted = Math.Min(requested, Math.Max(0, maximum - Count));
			Count += accepted;
			return accepted;
		}

		public void Release(int amount)
		{
			if (amount < 0 || amount > Count)
				throw new ArgumentOutOfRangeException(nameof(amount), "Population reservations must be released exactly once.");
			Count -= amount;
		}
	}
}
