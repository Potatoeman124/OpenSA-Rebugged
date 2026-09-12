#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Mods.OpenSA.Rmg;
using OpenRA.Widgets;

namespace OpenRA.Mods.OpenSA.UtilityCommands
{
	public sealed partial class ValidateRmgOwnershipRuntimeCommand
	{
		static void CheckObserverUi(Utility utility, string output)
		{
			var settings = RmgPlayerSettingsContract.Resolve(new RmgPlayerSettings
			{
				SchemaVersion = 10, LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscape,
				MapSize = 128, PlayerCount = 8, Seed = 74231
			}).Normalized;
			var package = OpenRaRmgMapAdapter.GenerateAndSave(utility.ModData, RmgProfile.Load(utility.ModData, settings), settings,
				Path.Combine(output, "observer.oramap"), false);
			using var directory = new OpenRA.FileSystem.Folder(output);
			utility.ModData.MapCache.LoadMap("observer.oramap", directory, MapClassification.User, utility.ModData.Manifest.Get<MapGrid>(), null);
			CheckWorld(utility, utility.ModData.MapCache[package.EngineUid], new int[8], new int[8], -1, false, null, false, renderer =>
			{
				var world = renderer.World;
				Ui.ResetAll();

				// Use the actual observer UI in an isolated world without changing the user's session.
				typeof(World).GetProperty(nameof(World.LocalPlayer)).SetValue(world, null);
				for (var i = 0; i < world.LobbyInfo.Clients.Count; i++)
					world.LobbyInfo.Clients[i].Team = i + 1;
				Game.LoadWidget(world, "INGAME_ROOT", Ui.Root, new WidgetArgs());
				var selector = Ui.Root.Get<DropDownButtonWidget>("SHROUD_SELECTOR");
				var center = world.Actors.First(a => a.Info.Name.EndsWith("_colony", StringComparison.Ordinal)).CenterPosition;
				selector.OnMouseDown(default);
				var panel = Ui.Root.Get<ScrollPanelWidget>("SPECTATOR_DROPDOWN_TEMPLATE");
				Require(panel.Children.OfType<ScrollItemWidget>().Count(i => i.Id == "HEADER") == 9, "Missing observer team headers.");
				Require(panel.RenderBounds.Top >= 0 && panel.RenderBounds.Bottom <= Game.Renderer.Resolution.Height, "Observer menu extends off screen.");
				CaptureQolWorld(renderer, output, "observer-dropdown", center);
				CheckObserverItemStates(panel, output, "observer");
				panel.Bounds.Height = 200;
				Require(panel.ContentHeight > panel.Bounds.Height, "Observer scroll test did not overflow.");
				panel.ScrollToBottom();
				CaptureQolWorld(renderer, output, "observer-scrolled", center);
				selector.RemovePanel();

				// Every view must remain selectable after drawing and closing the dropdown.
				var players = world.Players.Where(p => p.Playable && !p.NonCombatant).ToArray();
				for (var i = 0; i < players.Length + 2; i++)
				{
					selector.OnMouseDown(default);
					panel = Ui.Root.Get<ScrollPanelWidget>("SPECTATOR_DROPDOWN_TEMPLATE");
					panel.Children.OfType<ScrollItemWidget>().Where(w => w.Id == "TEMPLATE").ElementAt(i).OnClick();
					var expected = i == 0 ? world.Players.Single(p => p.InternalName == "Everyone") : i == 1 ? null : players[i - 2];
					Require(world.RenderPlayer == expected, "Observer view selection did not change the render player.");
				}

				var stats = Ui.Root.Get<DropDownButtonWidget>("STATS_DROPDOWN");
				stats.OnMouseDown(default);
				CaptureQolWorld(renderer, output, "observer-statistics", center);
				stats.RemovePanel();

				// Also exercise the other shared spectator template, including its optional header.
				var groups = new Dictionary<string, IEnumerable<int>>
				{
					["Statistics"] = Enumerable.Range(0, 12)
				};
				stats.ShowDropDown("SPECTATOR_LABEL_DROPDOWN_TEMPLATE", 160, groups, (value, template) =>
				{
					var item = ScrollItemWidget.Setup(template, () => value == 0, () => { });
					item.Get<LabelWidget>("LABEL").GetText = () => "Entry " + (value + 1);
					return item;
				});
				panel = Ui.Root.Get<ScrollPanelWidget>("SPECTATOR_LABEL_DROPDOWN_TEMPLATE");
				CheckObserverItemStates(panel, output, "spectator-label");
				stats.RemovePanel();
			});
			File.WriteAllText(Path.Combine(output, "verification.json"), new JObject
			{
				["status"] = "PASS", ["scope"] = "LIVE_OBSERVER_DROPDOWNS",
				["checks"] = "Eight team headers, on-screen bounds, scrolling, all ten camera views, statistics menu, both shared spectator templates and row interaction states"
			}.ToString());
			Console.WriteLine("PASS: observer dropdown rendering, scrolling, states and camera selection.");
		}

		static void CheckObserverItemStates(ScrollPanelWidget panel, string output, string prefix)
		{
			var header = panel.Children.OfType<ScrollItemWidget>().First(i => i.Id == "HEADER");
			var row = panel.Children.OfType<ScrollItemWidget>().First(i => i.Id == "TEMPLATE");
			foreach (var item in new[] { header, row })
			{
				var selected = item.IsSelected;
				for (var state = 0; state < 5; state++)
				{
					item.IsSelected = () => state == 3;
					item.IsDisabled = () => state == 4;
					item.Depressed = state == 2;
					Ui.MouseOverWidget = state == 1 ? item : null;
					Draw(output, $"{prefix}-{item.Id}-{state}");
				}

				item.IsSelected = selected;
				item.IsDisabled = () => false;
				item.Depressed = false;
			}

			Ui.MouseOverWidget = null;
			foreach (var suffix in new[] { "", "-hover", "-pressed", "-disabled" })
				Require(ChromeProvider.GetPanelImages(panel.Button + suffix).Any(s => s != null), "Observer scrollbar artwork is missing.");
			Require(ChromeProvider.GetPanelImages(panel.Background).Any(s => s != null), "Observer panel background is missing.");
		}
	}
}
