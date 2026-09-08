#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.OpenSA.Rmg;
using OpenRA.Traits;

namespace OpenRA.Mods.OpenSA.Traits.World
{
	[TraitLocation(SystemActors.World)]
	[Desc("Assign generated neutral colonies to participating player slots during match setup.")]
	public sealed class RmgStartingColonyOwnershipInfo : TraitInfo, Requires<SpawnMapActorsInfo>, Requires<SpawnStartingUnitsInfo>
	{
		public readonly int[] PlayerShares = Array.Empty<int>();
		public readonly string[] ColonyActorNames = Array.Empty<string>();
		public readonly RmgColonyOwnershipMode ChoiceMode = RmgColonyOwnershipMode.ClosestToSpawn;
		public readonly ulong RandomSeed = 0;
		public override object Create(ActorInitializer init) => new RmgStartingColonyOwnership(this);
	}

	public sealed class RmgStartingColonyOwnership : IWorldLoaded
	{
		readonly RmgStartingColonyOwnershipInfo info;
		public bool Applied { get; private set; }
		public int[] AssignedCounts { get; private set; } = Array.Empty<int>();

		public RmgStartingColonyOwnership(RmgStartingColonyOwnershipInfo info) { this.info = info; }

		void IWorldLoaded.WorldLoaded(OpenRA.World world, WorldRenderer renderer)
		{
			if (world.Type != WorldType.Regular) return;
			if (info.PlayerShares.Length > 8) throw new InvalidOperationException("Too many RMG ownership slots.");
			RmgColonyOwnership.ValidateShares(info.PlayerShares, info.PlayerShares.Length);
			var players = Enumerable.Range(0, info.PlayerShares.Length)
				.Select(i => world.Players.FirstOrDefault(p => p.Playable && p.InternalName == "Multi" + i)).ToArray();
			var shares = info.PlayerShares.Select((value, i) => players[i] == null ? 0 : value).ToArray();
			var spawned = world.WorldActor.Trait<SpawnMapActors>().Actors;
			var colonies = info.ColonyActorNames.Select(name => spawned.TryGetValue(name, out var actor) ? actor : null)
				.Where(actor => actor != null && actor.IsInWorld && actor.Owner.NonCombatant &&
					actor.TraitOrDefault<Colony.Colony>() != null).ToArray();
			var starts = players.Select(player =>
			{
				if (player == null) return new RmgPoint(0, 0);
				var startingColony = world.Actors.FirstOrDefault(actor => actor.Owner == player && actor.TraitOrDefault<Colony.Colony>() != null);
				var location = startingColony?.Location ?? player.HomeLocation;
				return new RmgPoint(location.X, location.Y);
			}).ToArray();
			var owners = RmgColonyOwnership.Assign(colonies.Select(actor => new RmgPoint(actor.Location.X, actor.Location.Y)).ToArray(), starts, shares, info.ChoiceMode, info.RandomSeed);
			AssignedCounts = new int[shares.Length];
			// Owner-change notifications refresh production, capture conditions and render traits.
			// Run once at the end of the setup frame, before players can issue normal orders.
			world.AddFrameEndTask(_ =>
			{
				for (var i = 0; i < colonies.Length; i++)
					if (owners[i] >= 0 && !colonies[i].IsDead && colonies[i].IsInWorld)
					{
						colonies[i].ChangeOwnerSync(players[owners[i]]);
						AssignedCounts[owners[i]]++;
					}
				Applied = true;
			});
		}
	}
}
