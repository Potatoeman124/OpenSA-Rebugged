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

using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.OpenSA.Traits.World;
using OpenRA.Traits;

namespace OpenRA.Mods.OpenSA.Traits
{
	class PirateAntInfo : TraitInfo, Requires<MobileInfo>
	{
		public override object Create(ActorInitializer init) { return new PirateAnt(init.Self); }
	}

	class PirateAnt : INotifyAddedToWorld, INotifyActorDisposing, INotifyKilled, INotifyRemovedFromWorld
	{
		readonly PirateSpawner spawner;
		readonly Mobile mobile;
		bool disposed;

		public int AntHoleAmount;
		public LobbyHostiles SpawnBudget;

		public PirateAnt(Actor self)
		{
			mobile = self.Trait<Mobile>();
			spawner = self.World.WorldActor.TraitOrDefault<PirateSpawner>();
		}

		void INotifyAddedToWorld.AddedToWorld(Actor self)
		{
			// pre-placed actors
			if (self.World.WorldTick == 0)
				return;

			for (var i = 0; i < 3; i++)
				mobile.Nudge(self);
		}

		void INotifyKilled.Killed(Actor self, AttackInfo e) => Release();
		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self) => Release();
		void INotifyActorDisposing.Disposing(Actor self) => Release();

		void Release()
		{
			if (disposed)
				return;

			if (SpawnBudget != null)
				SpawnBudget.ReleasePirates(1);
			else if (AntHoleAmount > 0)
				spawner?.DecreaseActorCount(1f / AntHoleAmount);
			disposed = true;
		}
	}
}
