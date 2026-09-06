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
using System.IO;
using System.Linq;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Network;
using OpenRA.Widgets;

namespace OpenRA.Mods.OpenSA.Widgets.Logic
{
	public sealed class LobbyOptionPresetsLogic : ChromeLogic
	{
		readonly OrderManager manager;
		readonly Func<MapPreview> getMap;
		readonly MapPreview map;
		readonly LobbyOptionPresets store;
		readonly System.Collections.Generic.Dictionary<string, OpenRA.Traits.LobbyOption> definitions;
		readonly ScrollPanelWidget details;
		readonly LabelWidget status;
		string[] names = Array.Empty<string>();
		string selected;
		bool readable;
		bool closing;

		[ObjectCreator.UseCtor]
		public LobbyOptionPresetsLogic(Widget widget, OrderManager orderManager, Func<MapPreview> getMap,
			Func<bool> configurationDisabled, LobbyOptionPresets presetStore)
		{
			manager = orderManager;
			this.getMap = getMap;
			store = presetStore;
			map = getMap();
			definitions = LobbyOptionPresets.Definitions(map);
			details = widget.Get<ScrollPanelWidget>("DETAILS");
			status = widget.Get<LabelWidget>("STATUS");
			var name = widget.Get<TextFieldWidget>("NAME");
			var replace = widget.Get<CheckboxWidget>("REPLACE");
			var replaceExisting = false;
			replace.IsChecked = () => replaceExisting;
			replace.OnClick = () => replaceExisting = !replaceExisting;
			name.OnTextEdited = () => replaceExisting = false;
			name.IsValid = () => LobbyOptionPresets.ValidName(name.Text);

			var dropdown = widget.Get<DropDownButtonWidget>("PRESETS");
			dropdown.GetText = () => WidgetUtils.TruncateText(selected ?? "Select a saved preset", dropdown.Bounds.Width - 40, Game.Renderer.Fonts[dropdown.Font]);
			dropdown.IsDisabled = () => names.Length == 0;
			dropdown.OnMouseDown = _ =>
			{
				ScrollItemWidget Setup(string value, ScrollItemWidget template)
				{
					var item = ScrollItemWidget.Setup(template, () => selected == value, () =>
					{
						selected = value;
						name.Text = value;
						replaceExisting = false;
						Preview();
					});
					var label = item.Get<LabelWidget>("LABEL");
					label.GetText = () => WidgetUtils.TruncateText(value, label.Bounds.Width, Game.Renderer.Fonts[label.Font]);
					return item;
				}

				dropdown.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", Math.Min(300, names.Length * 30), names, Setup);
			};

			widget.Get<ButtonWidget>("REFRESH").OnClick = Refresh;
			var save = widget.Get<ButtonWidget>("SAVE");
			save.IsDisabled = () => !name.IsValid() || getMap() != map;
			save.OnClick = () =>
			{
				if (save.IsDisabled())
					return;
				try
				{
					var preset = LobbyOptionPresets.Capture(manager.LobbyInfo.GlobalSettings);
					store.Save(name.Text, preset, replaceExisting);
					selected = name.Text;
					replaceExisting = false;
					Refresh();
					Message($"Saved {preset.Options.Count} game/hostile options to:\n{store.FilePath(selected)}");
				}
				catch (Exception ex) when (ex is IOException or InvalidDataException)
				{
					Message("Could not save. If the file already exists, tick Replace existing file or choose another name.\n" + ex.Message);
				}
				catch (UnauthorizedAccessException ex) { Message("Could not write preset: " + ex.Message); }
			};

			var load = widget.Get<ButtonWidget>("LOAD");
			load.IsDisabled = () => configurationDisabled() || !readable || getMap() != map;
			load.OnClick = () =>
			{
				if (load.IsDisabled())
					return;
				try
				{
					// Re-read and revalidate against current lock/value state immediately before issuing orders.
					var plan = LobbyOptionPresets.Plan(store.Load(selected), manager.LobbyInfo.GlobalSettings, definitions);
					foreach (var pair in plan.Changes)
						manager.IssueOrder(Order.Command($"option {pair.Key} {pair.Value}"));
					Message($"Requested {plan.Changes.Count} option updates; {plan.Unchanged} already match; {plan.Skipped.Count} skipped." +
						(plan.Skipped.Count == 0 ? "" : "\n" + string.Join("\n", plan.Skipped)));
				}
				catch (Exception ex) when (ex is IOException or InvalidDataException) { readable = false; Message("Could not load preset: " + ex.Message); }
				catch (UnauthorizedAccessException ex) { readable = false; Message("Could not read preset: " + ex.Message); }
			};
			widget.Get<ButtonWidget>("COPY_FOLDER").OnClick = () =>
			{
				Game.SetClipboardText(store.DirectoryPath);
				Message("Preset folder copied. Paste it into your file manager to copy or share preset files.\n" + store.DirectoryPath);
			};
			widget.Get<ButtonWidget>("CLOSE").OnClick = () => { closing = true; Ui.CloseWindow(); };
			Refresh();
		}

		void Refresh()
		{
			try
			{
				names = store.Names();
				if (!names.Contains(selected))
					selected = names.FirstOrDefault();
				Preview();
			}
			catch (Exception ex) when (ex is IOException or InvalidDataException) { readable = false; Message("Could not list presets: " + ex.Message); }
			catch (UnauthorizedAccessException ex) { readable = false; Message("Could not read preset folder: " + ex.Message); }
		}

		void Preview()
		{
			readable = false;
			if (selected == null)
			{
				Message("No saved presets yet. Enter a name and save the current lobby options.\nFolder: " + store.DirectoryPath);
				return;
			}

			try
			{
				var preset = store.Load(selected);
				var plan = LobbyOptionPresets.Plan(preset, manager.LobbyInfo.GlobalSettings, definitions);
				readable = true;
				Message($"{preset.Options.Count} saved values: {plan.Changes.Count} changes, {plan.Unchanged} already match, {plan.Skipped.Count} skipped." +
					(plan.Skipped.Count == 0 ? "\nReady to load. Only the lobby host can change options." : "\n" + string.Join("\n", plan.Skipped)));
			}
			catch (Exception ex) when (ex is IOException or InvalidDataException) { Message("Could not read preset: " + ex.Message); }
			catch (UnauthorizedAccessException ex) { Message("Could not read preset: " + ex.Message); }
		}

		void Message(string text)
		{
			var font = Game.Renderer.Fonts[status.Font];
			status.Text = RmgStatusText.Fit(text, status.Bounds.Width, int.MaxValue, s => font.Measure(s));
			status.Bounds.Height = font.Measure(status.Text).Y + 12;
			details.ContentHeight = status.Bounds.Height + 10;
			details.ScrollToTop();
		}

		public override void Tick()
		{
			if (!closing && getMap() != map)
			{
				closing = true;
				Game.RunAfterTick(Ui.CloseWindow);
			}
		}
	}
}
