#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Newtonsoft.Json.Linq;
using OpenRA.FileSystem;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Mods.OpenSA.Rmg;
using OpenRA.Mods.OpenSA.Widgets.Logic;
using OpenRA.Network;
using OpenRA.Widgets;

namespace OpenRA.Mods.OpenSA.UtilityCommands
{
	public sealed partial class ValidateRmgOwnershipRuntimeCommand
	{
		static void CheckSavedMaps(Utility utility, string output)
		{
			var mod = utility.ModData;
			var grid = mod.Manifest.Get<MapGrid>();

			// Redirect only this test's save destination; never write the player's maps or settings.
			var locations = (Dictionary<IReadOnlyPackage, MapClassification>)mod.MapCache.MapLocations;
			var original = locations.Where(p => p.Value == MapClassification.User).ToArray();
			using var directory = new Folder(Path.Combine(output, "custom-maps"));
			using var tracker = new MapDirectoryTracker(grid, directory, MapClassification.User);
			foreach (var pair in original) locations.Remove(pair.Key);
			locations.Add(directory, MapClassification.User);
			var savedFiles = new Dictionary<string, byte[]>();
			var results = new JArray();
			try
			{
				var cases = new[]
				{
					(Tileset: "NORMAL", Size: 64, Shares: new[] { 50 }, Mode: RmgColonyOwnershipMode.ClosestToSpawn, Title: "Little Garden"),
					(Tileset: "DESERT", Size: 128, Shares: new[] { 0, 10, 50 }, Mode: RmgColonyOwnershipMode.Random, Title: "Dunes #1: Desert"),
					(Tileset: "SWAMP", Size: 256, Shares: new[] { 0, 10, 20, 30 }, Mode: RmgColonyOwnershipMode.ClosestToSpawn, Title: "Swamp / Channels"),
					(Tileset: "CANDY", Size: 512, Shares: Enumerable.Repeat(100, 8).ToArray(), Mode: RmgColonyOwnershipMode.Random, Title: "Candy 512")
				};
				for (var index = 0; index < cases.Length; index++)
				{
					var (tileset, size, shares, mode, title) = cases[index];
					var settings = RmgPlayerSettingsContract.Resolve(new RmgPlayerSettings
					{
						SchemaVersion = 10, MapSize = size, PlayerCount = shares.Length, Seed = 397716241463670640,
						Tileset = tileset, LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscape,
						StartingColonyShares = shares, StartingColonyMode = mode
					}).Normalized;

					// Reuse the two preview filenames to prove that named copies survive later generations.
					var previewName = index % 2 == 0 ? "OpenSA-RMG-preview-a.oramap" : "OpenSA-RMG-preview-b.oramap";
					var previewPath = Path.Combine(directory.Name, previewName);
					var generated = OpenRaRmgMapAdapter.GenerateAndSave(mod, RmgProfile.Load(mod, settings), settings, previewPath, overwrite: true);
					mod.MapCache.LoadMap(previewName, directory, MapClassification.User, grid, null);
					var preview = mod.MapCache[generated.EngineUid];
					var before = File.ReadAllBytes(previewPath);
					var manager = new OrderManager(new EchoConnection());
					manager.LobbyInfo.GlobalSettings.Map = preview.Uid;
					manager.LobbyInfo.Clients.Add(new Session.Client { Index = manager.Connection.LocalClientId, IsAdmin = true, State = Session.ClientState.NotReady });
					typeof(Game).GetField("OrderManager", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, manager);
					using var lobbySource = mod.DefaultFileSystem.Open("sa|chrome/lobby.yaml");
					var node = MiniYaml.FromStream(lobbySource).Single(n => n.Key == "Background@SERVER_LOBBY").Clone();
					node.Value.Nodes.RemoveAll(n => n.Key == "Logic");
					node.Value.Nodes.Add(new MiniYamlNode("Logic", "RmgLobbyLogic"));
					var lobby = mod.WidgetLoader.LoadWidget(new WidgetArgs { { "orderManager", manager }, { "skirmishMode", true } }, Ui.Root, node);
					var logic = lobby.LogicObjects.OfType<RmgLobbyLogic>().Single();
					void State(string field, object value) => typeof(RmgLobbyLogic).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(logic, value);
					var launch = lobby.Get<ButtonWidget>("RMG_SAVE_MAP_BUTTON");
					var toggle = lobby.Get<ButtonWidget>("RMG_TOGGLE_BUTTON");
					Require(!launch.IsVisible() && launch.IsDisabled(), "Save must be hidden outside RMG and disabled without a generated preview.");
					toggle.OnClick();
					Require(launch.IsVisible() && launch.Bounds.Left > toggle.Bounds.Right && launch.Bounds.Top == toggle.Bounds.Top, "Save button is not beside Return to Skirmish.");
					State("generatedUid", preview.Uid); State("stale", false);
					Require(!launch.IsDisabled(), "Fresh generated preview cannot be saved.");
					static void Name(Widget dialog, string value) { var field = dialog.Get<TextFieldWidget>("MAP_NAME"); field.Text = value; field.OnTextEdited(); }
					Widget Open() { launch.OnClick(); return Ui.CurrentWindow(); }
					if (index == 0)
					{
						var dialog = Open();
						Require(dialog.Id == "RMG_SAVE_MAP_PANEL" && dialog.Get<ButtonWidget>("SAVE_BUTTON").IsDisabled(), "Blank name was accepted.");
						Name(dialog, "   "); Require(dialog.Get<ButtonWidget>("SAVE_BUTTON").IsDisabled(), "Whitespace name was accepted.");
						Name(dialog, "Canceled"); dialog.Get<ButtonWidget>("CANCEL_BUTTON").OnClick();
						dialog = Open(); Name(dialog, "Escaped"); dialog.Get<TextFieldWidget>("MAP_NAME").OnEscKey(default);
						Require(Directory.GetFiles(directory.Name).Length == 1, "Cancel wrote a map.");
						foreach (var flag in new[] { "stale", "generating" })
						{
							State(flag, true); Require(launch.IsDisabled(), "Save ignored " + flag); State(flag, false);
						}

						manager.LobbyInfo.GlobalSettings.Map = "other";
						Require(launch.IsDisabled(), "Save allowed a different selected map."); manager.LobbyInfo.GlobalSettings.Map = preview.Uid;
						dialog = Open(); Name(dialog, "Changed"); State("stale", true);
						Require(dialog.Get<ButtonWidget>("SAVE_BUTTON").IsDisabled(), "An open dialog accepted a changed preview.");
						dialog.Get<ButtonWidget>("CANCEL_BUTTON").OnClick(); State("stale", false);
						manager.LobbyInfo.Clients[0].State = Session.ClientState.Ready;
						Require(!launch.IsDisabled(), "Local saving should work when the player is ready.");
						Draw(output, "save-button");
					}

					var saveDialog = Open(); Name(saveDialog, "  " + title + "  ");
					if (index == 0)
					{
						Draw(output, "save-dialog");
						locations.Remove(directory);
						saveDialog.Get<ButtonWidget>("SAVE_BUTTON").OnClick();
						Require(Ui.CurrentWindow() == saveDialog && saveDialog.Get<LabelWidget>("ERROR").GetText().Contains("directory"), "Save failure did not keep the dialog open with an error.");
						Draw(output, "save-error");
						locations.Add(directory, MapClassification.User);
					}

					var readyState = manager.LobbyInfo.Clients[0].State;
					saveDialog.Get<TextFieldWidget>("MAP_NAME").OnEnterKey(default);
					Require(Ui.CurrentWindow() != saveDialog, "Successful save did not close the dialog.");
					var saved = mod.MapCache[mod.MapCache.LastModifiedMap];
					Require(saved.Title == title && saved.Class == MapClassification.User && saved.Status == MapStatus.Available, "Named map did not appear in the Custom Maps cache.");
					Require(manager.LobbyInfo.GlobalSettings.Map == preview.Uid && manager.LobbyInfo.Clients[0].State == readyState, "Save changed lobby selection or readiness.");
					Require(before.SequenceEqual(File.ReadAllBytes(previewPath)), "Save modified the source preview.");
					CompareSavedPackage(preview, saved);
					var savedPath = saved.Package.Name;
					savedFiles.Add(savedPath, File.ReadAllBytes(savedPath));
					if (index == 0)
					{
						var duplicate = RmgMapSaver.Save(mod, preview, title);
						Require(duplicate.Path.EndsWith(" (2).oramap", StringComparison.Ordinal) && savedFiles[savedPath].SequenceEqual(File.ReadAllBytes(savedPath)), "Duplicate save replaced an existing map.");
						savedFiles.Add(duplicate.Path, File.ReadAllBytes(duplicate.Path));
						foreach (var bad in new[] { null, "", " ", "line\nbreak", new string('x', 97) })
						{
							var rejected = false;
							try { RmgMapSaver.Save(mod, preview, bad); } catch (ArgumentException) { rejected = true; }
							Require(rejected, "Invalid map name accepted.");
						}

						var special = RmgMapSaver.Save(mod, preview, "../CON: Zażółć #1?");
						Require(Path.GetDirectoryName(special.Path) == directory.Name && Path.GetFileName(special.Path).StartsWith("RMG-", StringComparison.Ordinal), "Map name escaped the custom maps directory.");
						CompareSavedPackage(preview, mod.MapCache[special.Uid]);
						savedFiles.Add(special.Path, File.ReadAllBytes(special.Path));
						var sameTitle = RmgMapSaver.Save(mod, preview, preview.Title);
						Require(sameTitle.Uid == preview.Uid && sameTitle.Path != previewPath && mod.MapCache[preview.Uid].Status == MapStatus.Available, "Saving with the original title invalidated the selected map.");
						savedFiles.Add(sameTitle.Path, File.ReadAllBytes(sameTitle.Path));
					}

					// Drain the same filesystem watcher used by a running game, including temporary-file moves.
					Thread.Sleep(100); tracker.UpdateMaps(mod.MapCache);
					Require(mod.MapCache[saved.Uid].Status == MapStatus.Available && mod.MapCache[preview.Uid].Status == MapStatus.Available, "Directory watcher invalidated saved or selected map.");
					Require(!Directory.EnumerateFiles(directory.Name, ".rmg-save-*").Any(), "Save left temporary files behind.");
					Ui.ResetAll();
					string selected = null;
					var chooser = Ui.OpenWindow("MAPCHOOSER_PANEL", new WidgetArgs
					{
						{ "initialMap", saved.Uid }, { "initialTab", MapClassification.User },
						{ "onExit", () => { } }, { "onSelect", (Action<string>)(uid => selected = uid) }, { "filter", MapVisibility.Lobby }
					});
					var row = chooser.Get("USER_MAPS_TAB").Get<ScrollPanelWidget>("MAP_LIST").Children.OfType<ScrollItemWidget>().Single(r => r.ItemKey == saved.Uid);
					Require(row.Get<LabelWithTooltipWidget>("TITLE").GetText() == title, "Custom Maps displayed the wrong title.");
					row.OnClick();
					if (index == cases.Length - 1)
					{
						var timer = Stopwatch.StartNew();
						while (saved.GetMinimap() == null && timer.ElapsedMilliseconds < 5000) { Game.PerformDelayedActions(); Thread.Sleep(20); }
						Require(saved.GetMinimap() != null, "Saved map preview did not load in the chooser.");
						Draw(output, "custom-maps");
					}

					chooser.Get<ButtonWidget>("BUTTON_OK").OnClick();
					Require(selected == saved.Uid, "Saved map could not be selected in the real chooser.");
					Ui.ResetAll();
					if (tileset == "SWAMP") CheckServer(utility, saved);
					var world = CheckWorld(utility, saved, shares, new int[shares.Length], -1, true, Path.Combine(output, tileset), false);
					world["title"] = title; world["file"] = Path.GetFileName(savedPath); results.Add(world);
					Console.WriteLine($"PASS: {tileset} {size}, named copy, custom-map selection and live-world ownership.");
				}

				using var reloaded = new MapCache(mod) { LoadPreviewImages = false };
				foreach (var file in savedFiles)
				{
					Require(file.Value.SequenceEqual(File.ReadAllBytes(file.Key)), "A later preview generation changed a saved copy.");
					reloaded.LoadMap(Path.GetFileName(file.Key), directory, MapClassification.User, grid, null);
					Require(reloaded[reloaded.LastModifiedMap].Status == MapStatus.Available, "Saved copy could not be loaded into a fresh map cache.");
				}

				File.WriteAllText(Path.Combine(output, "verification.json"), new JObject { ["status"] = "PASS", ["savedFiles"] = savedFiles.Count, ["cases"] = results }.ToString());
				Console.WriteLine("PASS: cancel, Enter/Escape, stale/ready guards, errors/retry, safe filenames, duplicate protection, unchanged source and persistent copies.");
			}
			finally
			{
				Ui.ResetAll();
				locations.Remove(directory);
				foreach (var pair in original) locations.Add(pair.Key, pair.Value);
			}
		}

		static void CompareSavedPackage(MapPreview source, MapPreview saved)
		{
			Require(source.Author == saved.Author && source.Package.Contents.OrderBy(n => n).SequenceEqual(saved.Package.Contents.OrderBy(n => n)), "Saved package lost its author or members.");
			foreach (var entry in source.Package.Contents)
			{
				using var before = source.Package.GetStream(entry);
				using var after = saved.Package.GetStream(entry);
				if (entry == "map.yaml")
				{
					var expected = MiniYaml.FromStream(before, discardCommentsAndWhitespace: false);
					expected.Single(n => n.Key == "Title").Value.Value = saved.Title;
					var actual = MiniYaml.FromStream(after, discardCommentsAndWhitespace: false);
					Require(expected.WriteToString() == actual.WriteToString(), "Saved map metadata changed beyond its title.");
				}
				else
				{
					using var expected = new MemoryStream(); using var actual = new MemoryStream();
					before.CopyTo(expected); after.CopyTo(actual);
					Require(expected.ToArray().SequenceEqual(actual.ToArray()), "Saved copy changed " + entry);
				}
			}
		}
	}
}
