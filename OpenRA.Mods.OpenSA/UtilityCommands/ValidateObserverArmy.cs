#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.OpenSA.UtilityCommands
{
	public sealed partial class ValidateRmgOwnershipRuntimeCommand
	{
		static void CheckObserverArmy(WorldRenderer renderer, string output)
		{
			var world = renderer.World;
			var players = world.Players.Where(p => p.Playable && !p.NonCombatant).ToArray();
			var colony = world.Actors.First(a => a.Owner == players[0] && a.Info.Name == "ants_colony");
			var selector = Ui.Root.Get<DropDownButtonWidget>("STATS_DROPDOWN");
			void OpenArmy()
			{
				selector.OnMouseDown(default);
				Ui.Root.Get<ScrollPanelWidget>("LABEL_DROPDOWN_TEMPLATE").Children.OfType<ScrollItemWidget>()
					.Single(w => w.Get<LabelWidget>("LABEL").GetText() == "Army").OnClick();
			}

			OpenArmy();
			var panel = Ui.Root.Get<ScrollPanelWidget>("PLAYER_STATS_PANEL");
			ScrollItemWidget Row(Player player) => panel.Children.OfType<ScrollItemWidget>()
				.Single(r => r.GetOrNull<ObserverArmyIconsWidget>("ARMY_ICONS")?.GetPlayer() == player);
			void Verify()
			{
				foreach (var player in players)
				{
					var live = world.Actors.Where(a => a.Owner == player && a.IsInWorld && !a.IsDead && a.Info.HasTraitInfo<MobileInfo>()).ToArray();
					var stats = player.PlayerActor.Trait<PlayerStatistics>();
					Require(Row(player).Get<LabelWidget>("TOTAL_UNITS").GetText() == live.Length.ToString(), "Displayed army total differs from living units.");
					Require(stats.Units.Values.All(u => u.Count >= 0), "Negative army count.");
					Require(stats.Units.Values.Where(u => u.Count > 0).Sum(u => u.Count) == live.Length, "Army cache total differs from living units.");
					foreach (var group in live.GroupBy(a => a.Info.Name))
						Require(stats.Units[group.Key].Count == group.Count() && stats.Units[group.Key].Icon != null, "Army composition or icon is missing: " + group.Key);
				}
			}

			Verify();
			CaptureQolWorld(renderer, output, "army-empty", colony.CenterPosition);
			var units = new List<Actor>();
			foreach (var player in players.Take(7))
				foreach (var species in new[] { "ants", "beetles", "spiders", "scorpions", "wasps" })
					foreach (var tier in new[] { "light", "medium", "heavy" })
						for (var count = 0; count < (player == players[0] ? 3 : (player.ClientIndex % 3 + 1)); count++)
							units.Add(world.CreateActor(species + "_" + tier, new TypeDictionary
							{
								new OwnerInit(player), new LocationInit(new CPos(50 + units.Count % 20, 60 + units.Count / 20))
							}));
			Verify();
			var hash = world.SyncHash();
			CaptureQolWorld(renderer, output, "army-composition", colony.CenterPosition);
			Require(world.SyncHash() == hash, "Army UI changed synchronized gameplay state.");
			Require(panel.RenderBounds.Right <= Ui.Root.Get("RADAR_BG").RenderBounds.Left, "Army panel overlaps the observer radar.");

			// Rebuild the real UI for two teams and free-for-all, including the scroll threshold.
			foreach (var teamCount in new[] { 2, 0 })
			{
				for (var i = 0; i < world.LobbyInfo.Clients.Count; i++)
					world.LobbyInfo.Clients[i].Team = teamCount == 0 ? 0 : i % teamCount + 1;
				Ui.ResetAll();
				Game.LoadWidget(world, "INGAME_ROOT", Ui.Root, new WidgetArgs());
				selector = Ui.Root.Get<DropDownButtonWidget>("STATS_DROPDOWN");
				OpenArmy();
				panel = Ui.Root.Get<ScrollPanelWidget>("PLAYER_STATS_PANEL");
				Verify();
				Require(panel.ScrollBar == ScrollBar.Hidden == (teamCount == 0), "Army row height and scrollbar threshold disagree.");
				CaptureQolWorld(renderer, output, "army-teams-" + teamCount, colony.CenterPosition);
				Require(panel.RenderBounds.Right <= Ui.Root.Get("RADAR_BG").RenderBounds.Left, "Army layout overlaps the radar.");
				if (teamCount == 0)
					Require(panel.ContentHeight <= panel.Bounds.Height, "Free-for-all rows do not all fit.");
			}

			var icons = Row(players[0]).Get<ObserverArmyIconsWidget>("ARMY_ICONS");
			var rendered = ((IEnumerable)typeof(ObserverArmyIconsWidget).GetField("armyIcons", BindingFlags.Instance | BindingFlags.NonPublic)
				.GetValue(icons)).Cast<object>().ToArray();
			Require(rendered.Length == 15, "Not all fifteen regular unit types were rendered.");
			foreach (var entry in rendered)
			{
				var unit = (ArmyUnit)entry.GetType().GetProperty("Unit").GetValue(entry);
				var bounds = (Rectangle)entry.GetType().GetProperty("Bounds").GetValue(entry);
				Require(bounds.Right <= panel.RenderBounds.Right, "Army icon is clipped by the panel.");
				Require(unit.Count == 3, "Rendered per-type count is incorrect.");
			}

			var firstBounds = (Rectangle)rendered[0].GetType().GetProperty("Bounds").GetValue(rendered[0]);
			Ui.MouseOverWidget = icons;
			Viewport.LastMousePos = new int2(firstBounds.X + 4, firstBounds.Y + 4);
			icons.Tick();
			Require(icons.TooltipUnit != null, "Army icon hover did not select a tooltip.");
			Ui.Root.Get<TooltipContainerWidget>("TOOLTIP_CONTAINER").TooltipDelayMilliseconds = 0;
			CaptureQolWorld(renderer, output, "army-tooltip", colony.CenterPosition);
			Require(Ui.Root.GetOrNull("ARMY_TOOLTIP") != null, "Army tooltip did not render.");
			Ui.MouseOverWidget = null;
			icons.Tick();

			units[0].ChangeOwner(players[1]);
			world.Tick();
			Verify();
			units[1].Kill(units.Last());
			world.Tick();
			Verify();
			units[2].Dispose();
			world.Tick();
			Verify();
			Require(colony.Trait<Production>().Produce(colony, world.Map.Rules.Actors["ants_light"], "Unit.Ants", new TypeDictionary { new OwnerInit(players[0]) }, 0), "Colony could not produce a test unit.");
			world.Tick();
			Verify();
			foreach (var unit in world.Actors.Where(a => a.Owner == players[0] && a.Info.HasTraitInfo<MobileInfo>()).ToArray())
				unit.Dispose();
			world.Tick();
			Verify();
			Require(Row(players[0]).Get<LabelWidget>("TOTAL_UNITS").GetText() == "0", "Empty army does not display zero.");
			OpenArmy();
			Verify();
			CaptureQolWorld(renderer, output, "army-after-losses", colony.CenterPosition);
			File.WriteAllText(Path.Combine(output, "army-verification.json"), new JObject
			{
				["status"] = "PASS", ["regular_unit_types"] = 15, ["players"] = 8,
				["checks"] = "Live totals, per-type icon counts, eight/two/no teams, empty armies, creation, production, death, disposal, ownership changes, tooltips, panel reopening and unchanged UI sync hash"
			}.ToString());
			Console.WriteLine("PASS: live observer army totals, composition and unit lifecycle.");
		}
	}
}
