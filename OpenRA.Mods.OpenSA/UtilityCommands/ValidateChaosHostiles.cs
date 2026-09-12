#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.OpenSA.Rmg;
using OpenRA.Mods.OpenSA.Traits.Render;
using OpenRA.Mods.OpenSA.Traits.World;
using OpenRA.Network;
using OpenRA.Primitives;

namespace OpenRA.Mods.OpenSA.UtilityCommands
{
	public sealed partial class ValidateRmgOwnershipRuntimeCommand
	{
		static void CheckChaosHostiles(Utility utility, string output)
		{
			var results = new JArray();
			foreach (var theme in new[] { "CHAOS", "FRACTURED", "NORMAL", "DESERT", "SWAMP", "CANDY" })
			{
				var mixed = theme is "CHAOS" or "FRACTURED";
				var requested = new RmgPlayerSettings
				{
					SchemaVersion = mixed ? 20 : 10, MapSize = 128, PlayerCount = 2, Seed = 819341,
					LayoutFamily = mixed ? RmgPlayerLayoutFamily.Chaos : RmgPlayerLayoutFamily.NaturalLandscape,
					Tileset = mixed ? "NORMAL" : theme,
					ChaosBiomes = theme == "FRACTURED" ? RmgChaosBiomes.Fractured : RmgChaosBiomes.Patchwork
				};
				var settings = RmgPlayerSettingsContract.Resolve(requested).Normalized;
				var package = OpenRaRmgMapAdapter.GenerateAndSave(utility.ModData, RmgProfile.Load(utility.ModData, settings), settings,
					Path.Combine(output, theme + ".oramap"), false);
				using var directory = new OpenRA.FileSystem.Folder(output);
				utility.ModData.MapCache.LoadMap(theme + ".oramap", directory, MapClassification.User, utility.ModData.Manifest.Get<MapGrid>(), null);
				var map = utility.ModData.MapCache[package.EngineUid];
				CheckWorld(utility, map, new int[2], new int[2], -1, false, null, false, renderer =>
				{
					var world = renderer.World;
					var creep = world.Players.First(p => p.InternalName == "Creeps");
					var holes = new List<Actor>();
					var pirates = 0;
					var seen = new HashSet<string>();
					void Observe(Actor actor)
					{
						if (HostileOptions.Pirates.Contains(actor.Info.Name)) pirates++;
						if (actor.Info.Name != "anthole") return;
						holes.Add(actor);
						var animation = (Animation)typeof(WithAntHoleBody).GetField("defaultAnimation", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(actor.Trait<WithAntHoleBody>());
						var source = mixed ? RmgBiome.Tilesets[world.Map.Tiles[actor.Location].Type / 256] : theme;
						Require(animation.Name == "anthole_" + source.ToLowerInvariant(), "Ant hole artwork does not match its local biome.");
						foreach (var sequence in new[] { "idle", "open", "close" }) world.Map.Sequences.GetSequence(animation.Name, sequence).GetSprite(0);
						seen.Add(source);
					}

					world.ActorAdded += Observe;
					foreach (var actor in world.Actors.ToArray()) Observe(actor);
					foreach (var biome in mixed ? RmgBiome.Tilesets : new[] { theme })
					{
						var cell = world.Map.AllCells.Where(c => world.Map.Contains(c) && world.Map.GetTerrainInfo(c).Type != "Water" &&
							!world.ActorMap.GetActorsAt(c).Any() && (!mixed || world.Map.Tiles[c].Type / 256 == Array.IndexOf(RmgBiome.Tilesets, biome)))
							.OrderBy(c => Math.Abs(c.X - world.Map.Bounds.Width / 2) + Math.Abs(c.Y - world.Map.Bounds.Height / 2)).First();
						var hole = world.CreateActor("anthole", new TypeDictionary { new OwnerInit(creep), new LocationInit(cell) });
						for (var tick = 0; tick < 28; tick++) world.Tick();
						CaptureQolWorld(renderer, output, theme + "-" + biome + "-hole", hole.CenterPosition);
					}

					var initialHoles = holes.Count;
					for (var tick = 0; tick < 500; tick++) world.Tick();
					Require(holes.Count > initialHoles && pirates > 0, "Recurring hostile spawner did not produce holes and pirates.");

					// Stop new holes, then drain every pending spawn and closing animation.
					world.LobbyInfo.GlobalSettings.LobbyOptions["h-pirate-cap"] = new Session.LobbyOptionState { Value = "0" };
					for (var tick = 0; tick < 250; tick++) world.Tick();
					Require(holes.All(h => h.Disposed), "Ant hole did not complete opening, spawning and closing.");
					Require(seen.SetEquals(mixed ? RmgBiome.Tilesets : new[] { theme }), "Not all local ant-hole variants were tested.");
					Require(world.WorldActor.Trait<LobbyHostiles>().ReservedPirates is >= 0 and <= 40, "Pirate population reservations escaped their cap.");
					world.ActorAdded -= Observe;
					results.Add(new JObject { ["theme"] = theme, ["holes"] = holes.Count, ["pirates"] = pirates, ["variants"] = new JArray(seen) });
					Console.WriteLine($"PASS: {theme}, {holes.Count} ant holes, {pirates} pirates, complete animation cycles and local biome variants.");
				}, manager =>
				{
					void Set(string key, string value) => manager.LobbyInfo.GlobalSettings.LobbyOptions[key] = new Session.LobbyOptionState { Value = value };
					Set("creeps", "True"); Set("h-pirate-delay", "0"); Set("h-pirate-interval", "1");
					Set("h-pirate-min", "1"); Set("h-pirate-max", "2"); Set("h-pirate-cap", "40");
				});
			}

			File.WriteAllText(Path.Combine(output, "verification.json"), new JObject { ["status"] = "PASS", ["cases"] = results }.ToString());
		}
	}
}
