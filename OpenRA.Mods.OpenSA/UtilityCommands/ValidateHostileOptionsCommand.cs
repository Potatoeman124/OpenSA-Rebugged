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
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.OpenSA.Traits;
using OpenRA.Mods.OpenSA.Traits.World;
using OpenRA.Network;
using OpenRA.Support;
using OpenRA.Traits;

namespace OpenRA.Mods.OpenSA.UtilityCommands
{
	public sealed class ValidateHostileOptionsCommand : IUtilityCommand
	{
		string IUtilityCommand.Name => "--validate-sa-hostiles";
		bool IUtilityCommand.ValidateArguments(string[] args) => args.Length == 1;

		[Desc("Validate relative weights, caps, lobby values, cross-theme actor availability, and authored-map option registration.")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			Game.ModData = utility.ModData;
			TranslationProvider.Initialize(utility.ModData, utility.ModData.DefaultFileSystem);
			var checks = 0;
			void Check(bool value, string message)
			{
				checks++;
				if (!value)
					throw new InvalidOperationException(message);
			}

			var weights = new[] { 75, 50, 40, 10 };
			var counts = new int[weights.Length];
			for (var ticket = 0; ticket < weights.Sum(); ticket++)
				counts[HostileOptions.SelectTicket(weights, ticket)]++;
			Check(counts.SequenceEqual(weights), "175-ticket normalization must yield exactly 75/50/40/10 tickets.");
			Check(HostileOptions.SelectTicket(new[] { 0, 0, 1, 0 }, 0) == 2, "Zero weights must never win.");
			Check(HostileOptions.SelectTicket(new[] { 0, 0 }, 0) == -1, "All-zero weights must be disabled.");
			Check(HostileOptions.SelectTicket(weights, 175) == -1, "Upper boundary must be exclusive.");
			Check(HostileOptions.Choose(new[] { "a", "b" }, new[] { 0, 0 }, new MersenneTwister(5)) == null, "Empty pool must not throw.");
			var first = new MersenneTwister(123);
			var second = new MersenneTwister(123);
			for (var i = 0; i < 10000; i++)
				Check(HostileOptions.Choose(new[] { "a", "b", "c", "d" }, weights, first) ==
					HostileOptions.Choose(new[] { "a", "b", "c", "d" }, weights, second), "Weighted draws must be deterministic.");
			Console.WriteLine("PASS: exact 175-ticket weights, zero exclusions, empty pool and 10,000 deterministic draws.");

			foreach (var tileset in new[] { "NORMAL", "DESERT", "SWAMP", "CANDY" })
			{
				var session = new Session.Global();
				foreach (var group in new[] { "plant", "flier" })
				{
					var defaults = HostileOptions.Weights(session, group, tileset);
					Check(defaults.Count(x => x > 0) == 1 && defaults.Sum() == 100, "Exactly one terrain-default species is required.");
				}
				session.LobbyOptions["h-plant-0"] = new Session.LobbyOptionState { Value = "75" };
				session.LobbyOptions["h-plant-1"] = new Session.LobbyOptionState { Value = "50" };
				session.LobbyOptions["h-plant-2"] = new Session.LobbyOptionState { Value = "40" };
				session.LobbyOptions["h-plant-3"] = new Session.LobbyOptionState { Value = "10" };
				foreach (var option in HostileOptions.All.Where(x => x.WeightGroup == "plant" && x.SpeciesIndex >= 4))
					session.LobbyOptions[option.Id] = new Session.LobbyOptionState { Value = "0" };
				Check(HostileOptions.Weights(session, "plant", tileset).Sum() == 175, "Custom mix must override theme defaults.");
			}
			Console.WriteLine("PASS: single-species defaults and custom cross-theme weights for all four terrains.");

			var budget = new HostilePopulationBudget();
			Check(budget.Reserve(5, 7) == 5, "Initial reservation.");
			Check(budget.Reserve(5, 7) == 2 && budget.Count == 7, "Pending groups must respect remaining capacity.");
			Check(budget.Reserve(1, 7) == 0, "Full cap must stop spawning.");
			budget.Release(3);
			Check(budget.Reserve(5, 7) == 3 && budget.Count == 7, "Deaths must reopen capacity.");
			budget.Release(7);
			Check(budget.Reserve(5, 0) == 0 && budget.Count == 0, "Zero cap must disable spawning.");
			for (var i = 0; i < 1000; i++)
			{
				var reserved = budget.Reserve(5, 100);
				budget.Release(reserved);
			}
			Check(budget.Count == 0, "Repeated groups must not leak capacity.");
			Console.WriteLine("PASS: partial groups, zero cap, death recovery, and 1,000 leak-free reservation cycles.");

			Check(HostileOptions.All.Select(x => x.Id).Distinct().Count() == HostileOptions.All.Length, "Option IDs must be unique.");
			foreach (var option in HostileOptions.All)
			{
				Check(option.Values().ContainsKey(option.Default), "Default must be accepted by server validation.");
				if (option.Choices != null)
					continue;
				Check(option.Values().ContainsKey(option.Minimum.ToString()) && option.Values().ContainsKey(option.Maximum.ToString()), "Numeric bounds must be accepted.");
				Check(!option.Values().ContainsKey((option.Maximum + 1).ToString()), "Out-of-range values must be rejected.");
				Check(!option.Values().ContainsKey("-2"), "Negative values must be rejected.");
			}
			Console.WriteLine($"PASS: {HostileOptions.All.Length} synchronized option definitions and server-side bounds.");

			utility.ModData.MapCache.LoadMaps();
			var lobbyMaps = utility.ModData.MapCache.Where(x => x.Status == MapStatus.Available && x.Visibility.HasFlag(MapVisibility.Lobby)).ToArray();
			Check(lobbyMaps.Length > 0, "Authored lobby maps must be available.");
			foreach (var tileset in new[] { "NORMAL", "DESERT", "SWAMP", "CANDY" })
			{
				var preview = lobbyMaps.First(x => x.TileSet == tileset);
				var options = ((ILobbyOptions)preview.WorldActorInfo.TraitInfo<LobbyHostilesInfo>()).LobbyOptions(preview).ToArray();
				Check(options.Length == HostileOptions.All.Length, "All controls must be registered on authored maps.");
				var rules = preview.LoadRuleset();
				foreach (var species in HostileOptions.Plants)
					Check(rules.Actors[species].HasTraitInfo<PlantInfo>(), "Plant unavailable on " + tileset + ": " + species);
				foreach (var species in HostileOptions.Fliers)
					Check(rules.Actors[species].HasTraitInfo<AircraftInfo>(), "Flier unavailable on " + tileset + ": " + species);
			}
			var mission = utility.ModData.MapCache.First(x => x.Status == MapStatus.Available && x.Visibility == MapVisibility.MissionSelector);
			Check(!((ILobbyOptions)mission.WorldActorInfo.TraitInfo<LobbyHostilesInfo>()).LobbyOptions(mission).Any(), "Campaigns must retain authored behavior.");
			Console.WriteLine("PASS: authored maps in all four themes expose every species; campaign options remain untouched.");
			Console.WriteLine($"PASS: {checks} hostile-settings assertions. This utility does not substitute for live world/UI validation.");
		}
	}
}
