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
using System.Linq;
using System.Reflection;
using OpenRA.Graphics;
using OpenRA.Mods.OpenSA.Traits;
using OpenRA.Mods.OpenSA.Traits.World;
using OpenRA.Network;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.OpenSA.UtilityCommands
{
	static class HostileRuntimeSmoke
	{
		public static void Run(Utility utility)
		{
			for (var i = 0; i < 15; i++)
				RunCase(utility, i);
		}

		static void RunCase(Utility utility, int index)
		{
			var map = utility.ModData.MapCache.First(x => x.Status == MapStatus.Available && x.Title == "Two Armies");
			var manager = new OrderManager(new EchoConnection());
			foreach (var definition in map.WorldActorInfo.TraitInfos<ILobbyOptions>().Concat(map.PlayerActorInfo.TraitInfos<ILobbyOptions>()).SelectMany(x => x.LobbyOptions(map)))
				manager.LobbyInfo.GlobalSettings.LobbyOptions[definition.Id] = new Session.LobbyOptionState { Value = definition.DefaultValue };
			void Set(string key, string value) => manager.LobbyInfo.GlobalSettings.LobbyOptions[key] = new Session.LobbyOptionState { Value = value };
			Set("h-initial-count", "12");
			Set("h-initial-distribution", "groups");
			Set("h-pirate-interval", "1");
			Set("h-pirate-delay", "0");
			Set("h-pirate-min", "3");
			Set("h-pirate-max", "3");
			Set("h-pirate-cap", "5");
			Set("h-plant-interval", "1");
			Set("h-plant-delay", "0");
			Set("h-plant-cap", "2");
			Set("h-flier-interval", "1");
			Set("h-flier-delay", "9");
			Set("h-flier-0", "0");
			Set("h-flier-2", "100");
			Set("creeps", "True");
			Set("plants", "True");
			Set("flyers", "True");

			var mode = index % 5;
			var initialExpected = mode == 2 || mode == 3 ? 0 : 12;
			var spawnedExpected = mode == 0 || mode == 2 ? 5 : 0;
			Set("h-initial-count", mode == 2 ? "0" : "12");
			Set("h-initial-distribution", new[] { "scattered", "groups", "mixed" }[index / 5]);
			if (mode == 1)
			{
				Set("creeps", "False");
				Set("plants", "False");
				Set("flyers", "False");
			}
			if (mode == 3)
				foreach (var option in HostileOptions.All.Where(x => x.WeightGroup != null))
					Set(option.Id, "0");
			if (mode == 4)
			{
				Set("h-pirate-cap", "0");
				Set("h-plant-cap", "0");
				Set("flyers", "False");
			}
			manager.LobbyInfo.GlobalSettings.RandomSeed = 12345 + index;
			typeof(Game).GetField("OrderManager", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, manager);
			var world = (OpenRA.World)Activator.CreateInstance(typeof(OpenRA.World), BindingFlags.Instance | BindingFlags.NonPublic,
				null, new object[] { map.Uid, utility.ModData, manager, WorldType.Regular }, null);
			manager.World = world;
			Game.Renderer.InitializeDepthBuffer(utility.ModData.Manifest.Get<MapGrid>());
			var renderer = (WorldRenderer)Activator.CreateInstance(typeof(WorldRenderer), BindingFlags.Instance | BindingFlags.NonPublic,
				null, new object[] { utility.ModData, world }, null);
			typeof(Game).GetField("worldRenderer", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, renderer);
			Game.Cursor = new CursorManager(utility.ModData.CursorProvider);
			world.LoadComplete(renderer);
			var terrain = world.Map.AllCells.Select(cell => world.Map.Tiles[cell]).ToArray();
			void Advance(int count)
			{
				for (var i = 0; i < count; i++)
				{
					world.Tick();
					manager.LocalFrameNumber++;
				}
			}
			Advance(200);
			var controller = world.WorldActor.Trait<LobbyHostiles>();
			var initial = world.ActorsWithTrait<PirateAnt>().Count(x => !x.Actor.IsDead && x.Trait.SpawnBudget == null);
			var spawned = world.ActorsWithTrait<PirateAnt>().Where(x => !x.Actor.IsDead && x.Trait.SpawnBudget != null).ToArray();
			if (initial != initialExpected || spawned.Length != spawnedExpected || controller.ReservedPirates != spawnedExpected)
				throw new InvalidOperationException($"Runtime pirate placement/cap failed: initial={initial}, spawned={spawned.Length}, budget={controller.ReservedPirates}.");
			if (world.ActorsWithTrait<Plant>().Count(x => !x.Actor.IsDead) > 2)
				throw new InvalidOperationException("Regular plant spawning exceeded its cap before moth arrival.");
			if (spawnedExpected > 0)
			{
				spawned[0].Actor.Kill(spawned[0].Actor);
				if (controller.ReservedPirates != 4)
					throw new InvalidOperationException("Pirate death did not release capacity immediately.");
			}
			Advance(150);
			if (controller.ReservedPirates != spawnedExpected)
				throw new InvalidOperationException("Pirate spawning did not respect/refill its capacity.");
			if (world.Actors.Any(x => x.Info.Name == "moth") != (mode == 0 || mode == 2))
				throw new InvalidOperationException("Custom moth enable/disable behavior failed on NORMAL terrain.");
			if ((mode == 1 || mode == 3 || mode == 4) && world.ActorsWithTrait<Plant>().Any())
				throw new InvalidOperationException("Disabled/zero-weight/zero-cap plants spawned.");
			if (world.Actors.Any(x => x.Info.Name == "dragonfly"))
				throw new InvalidOperationException("Zero-weight dragonfly spawned.");
			if (!terrain.SequenceEqual(world.Map.AllCells.Select(cell => world.Map.Tiles[cell])))
				throw new InvalidOperationException("Hostile placement modified terrain.");
			Console.WriteLine($"PASS: live world {index + 1}/15, seed {12345 + index}, mode {mode}, distribution {new[] { "scattered", "groups", "mixed" }[index / 5]}: initial {initialExpected}, cap {spawnedExpected}, switches/weights, cross-theme fliers, unchanged terrain.");
			Ui.ResetAll();
			renderer.Dispose();
		}
	}
}
