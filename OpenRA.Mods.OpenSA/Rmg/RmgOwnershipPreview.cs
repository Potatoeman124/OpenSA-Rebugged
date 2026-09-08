#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion
using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Mods.Common.Widgets.Logic;
using OpenRA.Mods.OpenSA.Traits.World;
using OpenRA.Mods.OpenSA.Widgets;
using OpenRA.Network;
using OpenRA.Primitives;
using OpenRA.Support;
using OpenRA.Traits;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public sealed class RmgOwnershipPreview
	{
		public Dictionary<string, Color> ColonyColors { get; } = new();
		public Dictionary<string, string> ColonyOwners { get; } = new();
		public Dictionary<int, SpawnOccupant> SpawnOccupants { get; } = new();

		// Only values used by spawn resolution, ownership and displayed player colors invalidate the forecast.
		public static string CacheKey(Session lobby) => string.Join("|", lobby.GlobalSettings.Map, lobby.GlobalSettings.RandomSeed,
			string.Join(";", lobby.Slots.Select(s => $"{s.Key},{s.Value.PlayerReference},{s.Value.LockFaction}")),
			string.Join(";", lobby.Clients.Select(c => $"{c.Index},{c.Slot},{c.SpawnPoint},{c.Faction},{c.Team},{c.Bot},{c.Name},{c.Color}")),
			string.Join(",", lobby.DisabledSpawnPoints.OrderBy(p => p)),
			lobby.GlobalSettings.OptionOrDefault("separateteamspawns", true), lobby.GlobalSettings.OptionOrDefault("startingunits", "none"));

		public static RmgOwnershipPreview Resolve(MapPreview map, Session lobby, IReadOnlyList<ColonyPreviewSite> sites)
		{
			var info = map.WorldActorInfo.TraitInfoOrDefault<RmgStartingColonyOwnershipInfo>();
			if (info == null || !info.PlayerShares.Any(s => s > 0) || lobby.GlobalSettings.Map != map.Uid) return null;
			RmgColonyOwnership.ValidateShares(info.PlayerShares, map.PlayerCount);
			// Lobby data arrives in separate slot/client/settings messages. Wait for a consistent, startable snapshot.
			if (lobby.Clients.Any(c => c.Slot != null && (!lobby.Slots.ContainsKey(c.Slot) || !map.Players.Players.ContainsKey(c.Slot))) ||
				lobby.Clients.Where(c => c.Slot != null && c.SpawnPoint > 0).GroupBy(c => c.SpawnPoint).Any(g => g.Count() > 1) ||
				lobby.Clients.Any(c => c.SpawnPoint < 0 || c.SpawnPoint > map.SpawnPoints.Length) ||
				LobbyUtils.InsufficientEnabledSpawnPoints(map, lobby)) return null;
			var players = new List<GameInformation.Player>();
			var random = new MersenneTwister(lobby.GlobalSettings.RandomSeed);
			foreach (var creator in map.WorldActorInfo.TraitInfos<ICreatePlayersInfo>()) creator.CreateServerPlayers(map, lobby, players, random);
			var result = new RmgOwnershipPreview();
			var starts = new RmgPoint[info.PlayerShares.Length];
			var shares = new int[starts.Length];
			var clients = new Session.Client[starts.Length];
			for (var i = 0; i < starts.Length; i++)
			{
				var client = lobby.ClientInSlot("Multi" + i);
				if (client == null) continue;
				var player = players.FirstOrDefault(p => p != null && p.ClientIndex == client.Index);
				if (player == null || player.SpawnPoint <= 0 || player.SpawnPoint > map.SpawnPoints.Length) return null;
				var spawnClass = map.Players.Players[client.Slot].StartingUnitsClass ?? lobby.GlobalSettings.OptionOrDefault("startingunits", "none");
				var offsets = map.WorldActorInfo.TraitInfos<StartingUnitsInfo>().Where(g => g.Class == spawnClass && g.Factions != null && g.Factions.Contains(player.FactionId))
					.Select(g => g.BaseActorOffset).Distinct().ToArray();
				// Current OpenSA has one starting group per faction. Do not claim an exact forecast for ambiguous future offsets.
				if (offsets.Length != 1) return null;
				var location = map.SpawnPoints[player.SpawnPoint - 1] + offsets[0];
				starts[i] = new RmgPoint(location.X, location.Y);
				shares[i] = info.PlayerShares[i];
				clients[i] = client;
				// Keep random faction hidden in the tooltip while showing its resolved starting position.
				result.SpawnOccupants[player.SpawnPoint] = new SpawnOccupant(new Session.Client { Name = client.Name, Color = client.Color,
					Team = client.Team, Faction = client.Faction, SpawnPoint = player.SpawnPoint });
			}
			var byName = sites.ToDictionary(s => s.Name);
			var eligible = info.ColonyActorNames.Where(byName.ContainsKey).Select(name => byName[name]).ToArray();
			var owners = RmgColonyOwnership.Assign(eligible.Select(s => new RmgPoint(s.Location.X, s.Location.Y)).ToArray(), starts, shares, info.ChoiceMode, info.RandomSeed);
			for (var i = 0; i < owners.Length; i++)
				if (owners[i] >= 0)
				{
					result.ColonyColors[eligible[i].Name] = clients[owners[i]].Color;
					result.ColonyOwners[eligible[i].Name] = clients[owners[i]].Slot;
				}
			return result;
		}
	}
}
