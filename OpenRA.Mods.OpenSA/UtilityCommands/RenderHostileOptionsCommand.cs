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
using System.Reflection;
using System.Threading;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Mods.OpenSA.Traits.World;
using OpenRA.Network;
using OpenRA.Primitives;
using OpenRA.Support;
using OpenRA.Widgets;

namespace OpenRA.Mods.OpenSA.UtilityCommands
{
	// Opt-in real-renderer test: no desktop clicks, no user lobby, and no persisted settings.
	public sealed class RenderHostileOptionsCommand : IUtilityCommand
	{
		string IUtilityCommand.Name => "--render-sa-hostiles";
		bool IUtilityCommand.ValidateArguments(string[] args) => args.Length == 2 || (args.Length == 3 && args[2] == "--small");

		[Desc("OUTPUT-DIRECTORY", "Render and exercise the actual hostile-settings widgets in an isolated engine window.")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			var output = Path.GetFullPath(args[1]);
			Directory.CreateDirectory(output);
			Game.ModData = utility.ModData;
			Log.AddChannel("graphics", null);
			Log.AddChannel("sound", null);
			var platformAssembly = new AssemblyLoader(Path.Combine(Platform.BinDir, "OpenRA.Platforms.Default.dll")).LoadDefaultAssembly();
			var platform = (IPlatform)Activator.CreateInstance(platformAssembly.GetTypes().Single(t => typeof(IPlatform).IsAssignableFrom(t)));
			Game.Settings.Graphics.Mode = WindowMode.Windowed;
			Game.Settings.Graphics.WindowedSize = args.Length == 3 ? new int2(1024, 600) : new int2(1280, 800);
			Game.Settings.Graphics.DisableHardwareCursors = true;
			Game.Settings.Graphics.UIScale = 1;
			Game.Renderer = new Renderer(platform, Game.Settings.Graphics);
			Game.Sound = new Sound(platform, Game.Settings.Sound);
			Game.Sound.DisableAllSounds = true;
			utility.ModData.InitializeLoaders(utility.ModData.DefaultFileSystem);
			Game.Renderer.InitializeFonts(utility.ModData);
			utility.ModData.MapCache.LoadMaps();
			var map = utility.ModData.MapCache.First(x => x.Status == MapStatus.Available && x.Visibility == MapVisibility.Lobby && x.TileSet == "NORMAL");
			var manager = new OrderManager(new EchoConnection());
			foreach (var definition in map.WorldActorInfo.TraitInfos<OpenRA.Traits.ILobbyOptions>().Concat(map.PlayerActorInfo.TraitInfos<OpenRA.Traits.ILobbyOptions>()).SelectMany(x => x.LobbyOptions(map)))
				manager.LobbyInfo.GlobalSettings.LobbyOptions[definition.Id] = new Session.LobbyOptionState { Value = definition.DefaultValue };
			foreach (var option in HostileOptions.All)
				manager.LobbyInfo.GlobalSettings.LobbyOptions[option.Id] = new Session.LobbyOptionState { Value = option.Default };
			foreach (var key in new[] { "creeps", "plants", "flyers" })
				manager.LobbyInfo.GlobalSettings.LobbyOptions[key] = new Session.LobbyOptionState { Value = "True" };
			var disabled = false;
			var widget = Ui.OpenWindow("HOSTILE_OPTIONS_PANEL", new WidgetArgs
			{
				{ "orderManager", manager }, { "getMap", (Func<MapPreview>)(() => map) }, { "configurationDisabled", (Func<bool>)(() => disabled) }
			});

			void Draw(string name)
			{
				for (var i = 0; i < 3; i++)
				{
					Ui.Tick();
					Game.Renderer.BeginUI();
					Ui.Draw();
					Game.Renderer.EndFrame(new IgnoreInput());
				}
				var path = Path.Combine(output, name + ".png");
				Game.Renderer.SaveScreenshot(path);
				for (var wait = 0; wait < 100 && !File.Exists(path); wait++)
					Thread.Sleep(20);
				Console.WriteLine(path);
			}

			for (var tab = 0; tab < 4; tab++)
			{
				widget.Get<ButtonWidget>("TAB_" + tab).OnClick();
				Draw("tab-" + tab);
				var panel = widget.Get<ScrollPanelWidget>("SETTINGS");
				if (tab == 2)
				{
					foreach (var row in panel.Children.Skip(3))
					{
						var field = row.Get<TextFieldWidget>("VALUE");
						field.Text = "0";
						field.OnTextEdited();
					}
					var values = new[] { 75, 50, 40, 10 };
					for (var i = 0; i < values.Length; i++)
					{
						var field = panel.Children.Skip(3 + i).First().Get<TextFieldWidget>("VALUE");
						field.Text = values[i].ToString();
						field.OnTextEdited();
					}
					Ui.Tick();
					var chance = panel.Children.Skip(3).First().Get<LabelWidget>("CHANCE").GetText();
					if (!chance.Contains("75 / 175"))
						throw new InvalidOperationException("Rendered weight editor did not normalize 75 / 175: " + chance);
					Draw("plants-custom-weights");
					panel.ScrollToBottom();
					Draw("plants-scrolled-bottom");
				}
			}
			disabled = true;
			if (!widget.Get<ButtonWidget>("APPLY").IsDisabled())
				throw new InvalidOperationException("Read-only clients must not apply changes.");
			Draw("read-only");
			widget.Get<ButtonWidget>("CANCEL").OnClick();
			Console.WriteLine("PASS: actual widget construction, all tabs, scrolling, 75/175 normalization, and read-only Apply guard.");
			disabled = false;
			var parent = new ContainerWidget { Bounds = new Rectangle(30, 50, 675, 219) };
			Ui.Root.AddChild(parent);
			var launcher = Ui.LoadWidget("LOBBY_OPTIONS_BIN", parent, new WidgetArgs
			{
				{ "orderManager", manager }, { "getMap", (Func<MapPreview>)(() => map) }, { "configurationDisabled", (Func<bool>)(() => disabled) }
			});
			Draw("lobby-options-entry");
			if (launcher.Get<ButtonWidget>("HOSTILE_OPTIONS").IsDisabled())
				throw new InvalidOperationException("Lobby entry point must be available.");
			launcher.Get<ButtonWidget>("HOSTILE_OPTIONS").OnClick();
			var appliedWindow = Ui.CurrentWindow();
			var amount = appliedWindow.Get<ScrollPanelWidget>("SETTINGS").Children.First().Get<TextFieldWidget>("VALUE");
			amount.Text = "17";
			amount.OnTextEdited();
			appliedWindow.Get<ButtonWidget>("APPLY").OnClick();
			var orders = (System.Collections.Generic.List<Order>)typeof(OrderManager).GetField("localImmediateOrders", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
			if (!orders.Any(o => o.TargetString == "option h-initial-count 17"))
				throw new InvalidOperationException("Apply did not issue the synchronized lobby option command.");
			Console.WriteLine("PASS: original lobby Options entry point and Apply command dispatch.");
			// Exercise files using an isolated directory, never the user's actual presets.
			launcher.Get<ButtonWidget>("GAME_PRESETS").OnClick();
			if (Ui.CurrentWindow().Id != "LOBBY_PRESETS_PANEL")
				throw new InvalidOperationException("Preset launcher opened the wrong window.");
			Ui.CurrentWindow().Get<ButtonWidget>("CLOSE").OnClick();
			var presetStore = new OpenRA.Mods.OpenSA.Widgets.Logic.LobbyOptionPresets(
				Path.Combine(output, "preset-files-" + Guid.NewGuid().ToString("N")));
			Widget OpenPresets() => Ui.OpenWindow("LOBBY_PRESETS_PANEL", new WidgetArgs
			{
				{ "orderManager", manager }, { "getMap", (Func<MapPreview>)(() => map) },
				{ "configurationDisabled", (Func<bool>)(() => disabled) }, { "presetStore", presetStore }
			});
			manager.LobbyInfo.GlobalSettings.LobbyOptions["h-initial-count"].Value = "17";
			manager.LobbyInfo.GlobalSettings.LobbyOptions["creeps"].Value = "False";
			var presetWindow = OpenPresets();
			var presetName = presetWindow.Get<TextFieldWidget>("NAME");
			presetName.Text = "Weekend custom options";
			presetName.OnTextEdited();
			presetWindow.Get<ButtonWidget>("SAVE").OnClick();
			var savedPreset = presetStore.Load(presetName.Text);
			if (savedPreset.Options.Count != manager.LobbyInfo.GlobalSettings.LobbyOptions.Count ||
				savedPreset.Options["h-initial-count"] != "17" || savedPreset.Options["creeps"] != "False")
				throw new InvalidOperationException("Save widget did not persist all game/hostile options.");
			Draw("preset-saved");
			// Existing files are not overwritten unless the user explicitly opts in.
			manager.LobbyInfo.GlobalSettings.LobbyOptions["h-initial-count"].Value = "19";
			presetWindow.Get<ButtonWidget>("SAVE").OnClick();
			if (presetStore.Load(presetName.Text).Options["h-initial-count"] != "17")
				throw new InvalidOperationException("Save overwrote a preset without permission.");
			presetWindow.Get<CheckboxWidget>("REPLACE").OnClick();
			presetWindow.Get<ButtonWidget>("SAVE").OnClick();
			if (presetStore.Load(presetName.Text).Options["h-initial-count"] != "19")
				throw new InvalidOperationException("Explicit preset replacement did not work.");
			presetWindow.Get<ButtonWidget>("CLOSE").OnClick();

			// A reopened window/new lobby state must discover the file and restore its exact values.
			manager.LobbyInfo.GlobalSettings.LobbyOptions["h-initial-count"].Value = "0";
			manager.LobbyInfo.GlobalSettings.LobbyOptions["creeps"].Value = "True";
			presetWindow = OpenPresets();
			var priorOrders = orders.Count;
			disabled = true;
			presetWindow.Get<ButtonWidget>("LOAD").OnClick();
			if (!presetWindow.Get<ButtonWidget>("LOAD").IsDisabled() || orders.Count != priorOrders)
				throw new InvalidOperationException("Non-host preset load issued orders.");
			disabled = false;
			presetWindow.Get<ButtonWidget>("LOAD").OnClick();
			var loadedOrders = orders.Skip(priorOrders).ToArray();
			if (loadedOrders.Length != 2 || !loadedOrders.Any(o => o.TargetString == "option h-initial-count 19") ||
				!loadedOrders.Any(o => o.TargetString == "option creeps False"))
				throw new InvalidOperationException("Preset load did not issue exact validated option updates.");
			Draw("preset-loaded");
			presetWindow.Get<ButtonWidget>("CLOSE").OnClick();

			// Invalid files must remain a recoverable UI message, with Load disabled.
			File.WriteAllText(presetStore.FilePath("AAA Broken"), "{broken json");
			presetWindow = OpenPresets();
			if (!presetWindow.Get<ButtonWidget>("LOAD").IsDisabled())
				throw new InvalidOperationException("Malformed preset must disable loading.");
			Draw("preset-invalid-file");
			presetWindow.Get<ButtonWidget>("CLOSE").OnClick();
			Console.WriteLine("PASS: preset launcher, save/reopen/load commands, explicit replacement, non-host guard and malformed-file UI.");
			Ui.ResetAll();
			HostileRuntimeSmoke.Run(utility);
			Thread.Sleep(250);
			Game.Renderer.Dispose();
		}

		sealed class IgnoreInput : IInputHandler
		{
			public void ModifierKeys(Modifiers mods) { }
			public void OnKeyInput(KeyInput input) { }
			public void OnMouseInput(MouseInput input) { }
			public void OnTextInput(string text) { }
		}
	}
}
