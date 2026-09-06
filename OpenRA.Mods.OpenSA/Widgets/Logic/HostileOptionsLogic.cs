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
using System.Globalization;
using System.Linq;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Mods.OpenSA.Traits.World;
using OpenRA.Network;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.OpenSA.Widgets.Logic
{
	public sealed class HostileOptionsLauncherLogic : ChromeLogic
	{
		[ObjectCreator.UseCtor]
		public HostileOptionsLauncherLogic(Widget widget, OrderManager orderManager, Func<MapPreview> getMap, Func<bool> configurationDisabled)
		{
			var presets = widget.Get<ButtonWidget>("GAME_PRESETS");
			presets.IsDisabled = () => getMap()?.WorldActorInfo == null || getMap()?.PlayerActorInfo == null;
			presets.OnClick = () => Ui.OpenWindow("LOBBY_PRESETS_PANEL", new WidgetArgs
			{
				{ "orderManager", orderManager }, { "getMap", getMap }, { "configurationDisabled", configurationDisabled },
				{ "presetStore", new LobbyOptionPresets() }
			});
			var button = widget.Get<ButtonWidget>("HOSTILE_OPTIONS");
			button.IsDisabled = () => getMap()?.WorldActorInfo?.TraitInfoOrDefault<LobbyHostilesInfo>() == null ||
				!orderManager.LobbyInfo.GlobalSettings.LobbyOptions.ContainsKey("h-initial-count");
			button.OnClick = () => Ui.OpenWindow("HOSTILE_OPTIONS_PANEL", new WidgetArgs
			{
				{ "orderManager", orderManager }, { "getMap", getMap }, { "configurationDisabled", configurationDisabled }
			});
		}
	}

	public sealed class HostileOptionsLogic : ChromeLogic
	{
		readonly OrderManager orderManager;
		readonly Func<MapPreview> getMap;
		readonly Func<bool> configurationDisabled;
		readonly MapPreview map;
		readonly ScrollPanelWidget panel;
		readonly Widget template;
		readonly Dictionary<string, string> draft = new();
		readonly List<Action> refresh = new();
		readonly List<Func<bool>> validators = new();
		readonly LabelWidget note;
		readonly CheckboxWidget enabled;
		readonly ButtonWidget apply;
		string section = "Initial pirates";
		bool closing;

		[ObjectCreator.UseCtor]
		public HostileOptionsLogic(Widget widget, OrderManager orderManager, Func<MapPreview> getMap, Func<bool> configurationDisabled)
		{
			this.orderManager = orderManager;
			this.getMap = getMap;
			this.configurationDisabled = configurationDisabled;
			map = getMap();
			var width = Math.Min(900, Game.Renderer.Resolution.Width - 40);
			var height = Math.Min(650, Game.Renderer.Resolution.Height - 40);
			widget.Bounds = new Rectangle((Game.Renderer.Resolution.Width - width) / 2, (Game.Renderer.Resolution.Height - height) / 2, width, height);
			foreach (var id in new[] { "TITLE", "HELP", "NOTE" })
				widget.Get(id).Bounds.Width = width - 40;
			widget.Get("SETTINGS").Bounds.Width = width - 40;
			widget.Get("SETTINGS").Bounds.Height = height - 240;
			widget.Get("NOTE").Bounds.Y = height - 89;
			foreach (var id in new[] { "RESET", "APPLY", "CANCEL" })
				widget.Get(id).Bounds.Y = height - 43;
			widget.Get("APPLY").Bounds.X = width - 270;
			widget.Get("CANCEL").Bounds.X = width - 140;
			for (var i = 0; i < 4; i++)
				widget.Get("TAB_" + i).Bounds = new Rectangle(20 + i * (width - 40) / 4, 74, (width - 40) / 4, 28);
			panel = widget.Get<ScrollPanelWidget>("SETTINGS");
			template = panel.Get("ROW");
			template.Bounds.Width = panel.Bounds.Width - 24;
			var shrink = (900 - width) / 2;
			template.Get("SLIDER").Bounds.Width -= shrink;
			foreach (var id in new[] { "VALUE", "DEFAULT", "CHANCE" })
				template.Get(id).Bounds.X -= shrink;
			template.Get("LABEL").Bounds.Width = template.Bounds.Width - 20;
			template.Get("CHANCE").Bounds.Width = template.Bounds.Width - template.Get("CHANCE").Bounds.X - 10;
			panel.RemoveChildren();
			note = widget.Get<LabelWidget>("NOTE");
			enabled = widget.Get<CheckboxWidget>("ENABLED");
			apply = widget.Get<ButtonWidget>("APPLY");

			foreach (var option in HostileOptions.All)
				draft[option.Id] = HostileOptions.Value(orderManager.LobbyInfo.GlobalSettings, option.Id, option.Default);
			foreach (var key in new[] { "creeps", "plants", "flyers" })
				draft[key] = HostileOptions.Value(orderManager.LobbyInfo.GlobalSettings, key, "True");

			var sections = new[] { "Initial pirates", "Pirate spawning", "Plant spawning", "Flier spawning" };
			for (var i = 0; i < sections.Length; i++)
			{
				var selected = sections[i];
				var tab = widget.Get<ButtonWidget>("TAB_" + i);
				tab.GetText = () => selected;
				tab.IsHighlighted = () => section == selected;
				tab.OnClick = () => { section = selected; Rebuild(); };
			}

			enabled.IsVisible = () => EnableKey() != null;
			enabled.IsChecked = () => EnableKey() is string key && draft[key] == "True";
			enabled.IsDisabled = () => configurationDisabled() || EnableKey() is not string key ||
				!orderManager.LobbyInfo.GlobalSettings.LobbyOptions.TryGetValue(key, out var state) || state.IsLocked;
			enabled.OnClick = () => { var key = EnableKey(); draft[key] = draft[key] == "True" ? "False" : "True"; };
			apply.IsDisabled = () => configurationDisabled() || validators.Any(valid => !valid());
			apply.OnClick = () =>
			{
				if (apply.IsDisabled() || getMap() != map)
					return;
				foreach (var pair in draft)
					if (orderManager.LobbyInfo.GlobalSettings.LobbyOptions.TryGetValue(pair.Key, out var current) &&
						!current.IsLocked && current.Value != pair.Value)
						orderManager.IssueOrder(Order.Command($"option {pair.Key} {pair.Value}"));
				closing = true;
				Ui.CloseWindow();
			};
			widget.Get<ButtonWidget>("CANCEL").OnClick = () => { closing = true; Ui.CloseWindow(); };
			widget.Get<ButtonWidget>("RESET").IsDisabled = configurationDisabled;
			widget.Get<ButtonWidget>("RESET").OnClick = () =>
			{
				foreach (var option in HostileOptions.All.Where(x => x.Section == section))
					draft[option.Id] = option.Default;
				Rebuild();
			};
			Rebuild();
		}

		string EnableKey() => section switch { "Pirate spawning" => "creeps", "Plant spawning" => "plants", "Flier spawning" => "flyers", _ => null };

		int Effective(HostileOption option) => int.TryParse(draft[option.Id], out var value) ? value :
			option.WeightGroup != null ? HostileOptions.ThemeWeight(option.WeightGroup, option.SpeciesIndex, map.TileSet) : option.Minimum;

		bool Locked(HostileOption option) => configurationDisabled() ||
			!orderManager.LobbyInfo.GlobalSettings.LobbyOptions.TryGetValue(option.Id, out var state) || state.IsLocked;

		void Rebuild()
		{
			refresh.Clear();
			validators.Clear();
			panel.RemoveChildren();
			foreach (var option in HostileOptions.All.Where(x => x.Section == section))
			{
				var row = template.Clone();
				row.IsVisible = () => true;
				panel.AddChild(row);
				row.Get<LabelWidget>("LABEL").GetText = () => option.Label;
				var slider = row.Get<SliderWidget>("SLIDER");
				var field = row.Get<TextFieldWidget>("VALUE");
				var reset = row.Get<ButtonWidget>("DEFAULT");
				var chance = row.Get<LabelWidget>("CHANCE");
				var dropdown = row.Get<DropDownButtonWidget>("CHOICE");
				if (option.Choices != null)
				{
					slider.IsVisible = field.IsVisible = reset.IsVisible = chance.IsVisible = () => false;
					dropdown.IsVisible = () => true;
					dropdown.IsDisabled = () => Locked(option);
					dropdown.GetText = () => option.Choices[draft[option.Id]];
					dropdown.OnMouseDown = _ =>
					{
						ScrollItemWidget Setup(KeyValuePair<string, string> value, ScrollItemWidget itemTemplate)
						{
							var item = ScrollItemWidget.Setup(itemTemplate, () => draft[option.Id] == value.Key, () => draft[option.Id] = value.Key);
							item.Get<LabelWidget>("LABEL").GetText = () => value.Value;
							return item;
						}
						dropdown.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", 100, option.Choices, Setup);
					};
					continue;
				}

				slider.MinimumValue = option.Minimum;
				slider.MaximumValue = option.Maximum;
				slider.GetValue = () => Effective(option);
				slider.IsDisabled = () => Locked(option);
				slider.OnChange += value =>
				{
					var rounded = Math.Clamp((int)Math.Round(value), option.Minimum, option.Maximum);
					if (rounded != Effective(option) && !Locked(option))
						draft[option.Id] = rounded.ToString(CultureInfo.InvariantCulture);
				};
				field.IsDisabled = () => Locked(option);
				string Display() => option.WeightGroup != null ? Effective(option).ToString(CultureInfo.InvariantCulture) : draft[option.Id];
				field.Text = Display();
				field.IsValid = () => field.Text == option.Default || int.TryParse(field.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) &&
					n >= option.Minimum && n <= option.Maximum;
				field.OnTextEdited = () =>
				{
					if (field.IsValid() && !Locked(option))
						draft[option.Id] = field.Text;
				};
				field.OnEnterKey = _ => { if (field.IsValid()) field.YieldKeyboardFocus(); return true; };
				field.OnLoseFocus = () => field.Text = Display();
				validators.Add(field.IsValid);
				refresh.Add(() => { if (!field.HasKeyboardFocus) field.Text = Display(); });
				reset.GetText = () => option.WeightGroup != null ? "Terrain default" : "Map default";
				reset.IsDisabled = () => Locked(option);
				reset.OnClick = () => draft[option.Id] = option.Default;
				chance.GetText = () =>
				{
					if (option.WeightGroup == null)
						return draft[option.Id] == "map" ? "Uses authored map value" : "Custom";
					var sum = HostileOptions.All.Where(x => x.WeightGroup == option.WeightGroup).Sum(Effective);
					return sum == 0 ? "0% - this group is disabled" :
						$"{100d * Effective(option) / sum:0.##}%  ({Effective(option)} / {sum})";
				};
			}
			panel.ScrollToTop();
			note.GetText = () => section switch
			{
				"Initial pirates" => "Map default keeps authored pirates. A number replaces them; zero removes them. Placement never changes terrain.",
				"Pirate spawning" => "Cap excludes initial pirates and includes pending emergence. Reversed group-size limits are sorted automatically.",
				"Plant spawning" => "Regular-spawn cap includes existing plants. Moth seeds/reproduction remain independent and can exceed this cap.",
				_ => "Fliers keep their native behavior: moths release seeds, and Flying Machines may leave a pirate pilot."
			};
		}

		public override void Tick()
		{
			if (closing)
				return;
			if (getMap() != map)
			{
				closing = true;
				Game.RunAfterTick(Ui.CloseWindow);
				return;
			}
			if (configurationDisabled())
				foreach (var key in draft.Keys.ToArray())
					if (orderManager.LobbyInfo.GlobalSettings.LobbyOptions.TryGetValue(key, out var state))
						draft[key] = state.Value;
			foreach (var action in refresh)
				action();
		}
	}
}
