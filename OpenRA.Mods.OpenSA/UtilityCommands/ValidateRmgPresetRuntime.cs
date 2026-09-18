#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Newtonsoft.Json.Linq;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Mods.OpenSA.Rmg;
using OpenRA.Mods.OpenSA.Widgets.Logic;
using OpenRA.Network;
using OpenRA.Widgets;

namespace OpenRA.Mods.OpenSA.UtilityCommands
{
	public sealed partial class ValidateRmgOwnershipRuntimeCommand
	{
		static void CheckRmgPresets(Utility utility, string output, bool matrix)
		{
			var catalog = RmgPresetCatalog.All;
			Require(catalog.Select(p => p.Id).Distinct().Count() == catalog.Count, "Duplicate preset ids.");
			var contracts = 0;
			foreach (var preset in catalog)
				foreach (var size in new[] { 64, 128, 256, 512 })
					for (var players = 1; players <= (size == 64 ? 4 : 8); players++)
					{
						var requested = preset.CreateSettings(size, players, ulong.MaxValue);
						var roundTrip = RmgPlayerSettingsContract.Parse(requested.ToJson());
						Require(preset.Matches(roundTrip), $"Preset round trip failed: {preset.Id}/{size}/{players}.");
						Require(requested.StartingColonyShares.Length == requested.PlayerCount, "Ownership rows do not match players.");
						contracts++;
					}

			var manager = new OrderManager(new EchoConnection());
			manager.LobbyInfo.Clients.Add(new Session.Client { Index = manager.Connection.LocalClientId, IsAdmin = true, State = Session.ClientState.NotReady });
			typeof(Game).GetField("OrderManager", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, manager);
			using var source = utility.ModData.DefaultFileSystem.Open("sa|chrome/lobby.yaml");
			var node = MiniYaml.FromStream(source).Single(n => n.Key == "Background@SERVER_LOBBY").Clone();
			node.Value.Nodes.RemoveAll(n => n.Key == "Logic");
			node.Value.Nodes.Add(new MiniYamlNode("Logic", "RmgLobbyLogic"));
			var lobby = utility.ModData.WidgetLoader.LoadWidget(new WidgetArgs { { "orderManager", manager }, { "skirmishMode", true } }, Ui.Root, node);
			var logic = lobby.LogicObjects.OfType<RmgLobbyLogic>().Single();
			RmgPlayerSettings Settings() => (RmgPlayerSettings)typeof(RmgLobbyLogic)
				.GetMethod("CreatePlayerSettings", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(logic, new object[] { 74231UL });
			var map = MapCache.UnknownMap;
			Ui.LoadWidget("MAP_PREVIEW", lobby.Get("MAP_PREVIEW_ROOT"), new WidgetArgs
			{
				{ "orderManager", manager }, { "getMap", (Func<(MapPreview, Session.MapStatus)>)(() => (map, Session.MapStatus.Playable)) },
				{ "onMouseDown", (Action<MapPreviewWidget, MapPreview, MouseInput>)((_, _, _) => { }) },
				{ "getSpawnOccupants", (Func<Dictionary<int, SpawnOccupant>>)(() => new()) },
				{ "getDisabledSpawnPoints", (Func<HashSet<int>>)(() => new()) }, { "showUnoccupiedSpawnpoints", true }
			});
			lobby.Get<ButtonWidget>("RMG_TOGGLE_BUTTON").OnClick();
			lobby.Get<TextFieldWidget>("RMG_SEED").Text = "74231";
			void Choose(string id, string label)
			{
				var button = lobby.Get<DropDownButtonWidget>(id);
				button.OnMouseDown(default);
				Ui.Root.Children.OfType<ScrollPanelWidget>().Last().Children.OfType<ScrollItemWidget>()
					.Single(r => r.Get<LabelWidget>("LABEL").GetText() == label).OnClick();
				button.RemovePanel();
			}

			using var directory = new OpenRA.FileSystem.Folder(output);
			var results = new JArray();
			foreach (var preset in catalog)
			{
				Choose("RMG_PRESET", preset.Name);
				Require(preset.Matches(Settings()), "Live preset omitted settings: " + preset.Id);
				Require(Settings().MapSize == 256 && Settings().PlayerCount == 4 && lobby.Get<TextFieldWidget>("RMG_SEED").Text == "74231", "Preset overwrote seed, size or a supported player count.");
				Require(lobby.Get<DropDownButtonWidget>("RMG_PRESET").GetText() == preset.Name, "Fresh preset marked custom.");
				var safety = lobby.Get<CheckboxWidget>("RMG_RESPECT_STARTING_SAFE_AREA");
				safety.OnClick();
				Require(lobby.Get<DropDownButtonWidget>("RMG_PRESET").GetText().StartsWith("Custom (", StringComparison.Ordinal), "Edited preset not marked custom.");
				Choose("RMG_PRESET", preset.Name);
				Require(preset.Matches(Settings()), "Reselection failed to reset edited settings.");
				lobby.Get<ButtonWidget>("RMG_COLONY_OWNERSHIP").OnClick();
				Require(Ui.CurrentWindow().Get<ScrollPanelWidget>("SETTINGS").Children.Count == 4, "Wrong ownership slider count.");
				Require(Ui.CurrentWindow().Get<ScrollPanelWidget>("SETTINGS").Children.All(r => r.Get<SliderWidget>("SLIDER").GetValue() == preset.StartingShare), "Preset ownership values did not reach the dialog.");
				if (preset.Id == "total-mayhem") Draw(output, "total-mayhem-ownership");
				Ui.CurrentWindow().Get<ButtonWidget>("CANCEL").OnClick();
				lobby.Get<ButtonWidget>("RMG_COLONY_WEIGHTS").OnClick();
				Ui.CurrentWindow().Get<ButtonWidget>("CANCEL").OnClick();

				var scenarios = matrix ? new[] { (256, 4, 74231UL), (256, 8, 397716241463670640UL), (256, 1, 5UL), (64, 1, 0UL), (64, 4, 1UL), (128, 3, 19UL), (512, 8, 92UL) } : new[] { (256, 4, 74231UL) };
				foreach (var (size, players, seed) in scenarios)
				{
					var id = $"{preset.Id}-{size}-{players}";
					var requested = preset.CreateSettings(size, players, seed);
					var settings = RmgPlayerSettingsContract.Resolve(requested).Normalized;
					RmgPackageResult package;
					try
					{
						package = OpenRaRmgMapAdapter.GenerateAndSave(utility.ModData, RmgProfile.Load(utility.ModData, settings), settings,
							Path.Combine(output, id + ".oramap"), false, RmgMovementValidationMode.Both, verifyRepeatability: true);
					}
					catch (RmgGenerationRejectedException ex) when (size == 64)
					{
						results.Add(new JObject { ["preset"] = preset.Id, ["size"] = size, ["requested_players"] = players, ["status"] = "REJECTED", ["reason"] = ex.Message });
						Console.WriteLine($"REJECTED: {id}: {ex.Message}");
						continue;
					}

					File.WriteAllText(Path.Combine(output, id + "-settings.json"), requested.ToJson().ToString());
					File.WriteAllText(Path.Combine(output, id + "-report.json"), package.Report.ToString());
					results.Add(new JObject { ["preset"] = preset.Id, ["size"] = size, ["requested_players"] = players, ["players"] = settings.PlayerCount, ["seed"] = seed.ToString(), ["uid"] = package.EngineUid });
					if (size == 256 && players == 4)
					{
						utility.ModData.MapCache.LoadMap(id + ".oramap", directory, MapClassification.User, utility.ModData.Manifest.Get<MapGrid>(), null);
						map = utility.ModData.MapCache[package.EngineUid];
						for (var tick = 0; tick < 200 && map.GetMinimap() == null; tick++) { Game.PerformDelayedActions(); Thread.Sleep(10); }
						Require(map.GetMinimap() != null, "Preview did not load.");
						Draw(output, "ui-" + preset.Id);
					}

					Console.WriteLine("PASS: " + id);
				}
			}

			// Exercise the opposite selection order after the strongest ownership/weight changes.
			foreach (var preset in catalog.Reverse())
			{
				Choose("RMG_PRESET", preset.Name);
				Require(preset.Matches(Settings()), "State leaked between presets: " + preset.Id);
			}

			Choose("RMG_SIZE", "64 x 64");
			Choose("RMG_PRESET", "Open Conflict");
			lobby.Get<SliderWidget>("RMG_PLAYERS").UpdateValue(1);
			Choose("RMG_PRESET", "Mirror Match");
			Require(Settings().MapSize == 64 && Settings().PlayerCount == 2, "Preset did not normalize incompatible players.");
			Choose("RMG_PRESET", "Balanced");
			Require(!Settings().OwnStartingStronghold && Settings().StartingColonyShares.All(v => v == 0) && Settings().NeutralColonyWeights == new RmgColonyWeights(), "Balanced did not clear ownership and species overrides.");
			var button = lobby.Get<DropDownButtonWidget>("RMG_PRESET");
			button.OnMouseDown(default);
			var menu = Ui.Root.Children.OfType<ScrollPanelWidget>().Last();
			Require(menu.Children.OfType<ScrollItemWidget>().Count() == catalog.Count, "Preset menu is incomplete.");
			Require(menu.Children.OfType<ScrollItemWidget>().All(i => !string.IsNullOrEmpty(i.GetTooltipText())), "Menu descriptions missing.");
			Require(menu.RenderBounds.Bottom <= Game.Renderer.Resolution.Height, "Preset menu exceeds screen.");
			Draw(output, "preset-menu");
			button.RemovePanel();
			Ui.ResetAll();
			var worlds = new JArray();
			if (!matrix)
				foreach (var preset in catalog)
				{
					var uid = (string)results.Single(r => (string)r["preset"] == preset.Id)["uid"];
					var result = CheckWorld(utility, utility.ModData.MapCache[uid], Enumerable.Repeat(preset.StartingShare, 4).ToArray(), new int[4], -1, true, null, false);
					result["preset"] = preset.Id;
					worlds.Add(result);
					Console.WriteLine("PASS: live skirmish startup " + preset.Id);
				}

			File.WriteAllText(Path.Combine(output, "verification.json"), new JObject { ["status"] = results.Any(r => (string)r["status"] == "REJECTED") ? "PASS_WITH_SMALL_MAP_REJECTIONS" : "PASS", ["settings_round_trips"] = contracts, ["maps"] = results, ["worlds"] = worlds }.ToString());
			Console.WriteLine($"PASS: {catalog.Count} live presets, {contracts} settings round trips, {results.Count(r => r["uid"] != null)} native maps, {results.Count(r => (string)r["status"] == "REJECTED")} small-map rejections.");
		}
	}
}
