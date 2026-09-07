#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Newtonsoft.Json.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Mods.OpenSA.Rmg;
using OpenRA.Mods.OpenSA.Traits.World;
using OpenRA.Network;
using OpenRA.Primitives;
using OpenRA.Support;
using OpenRA.Traits;
using OpenRA.Widgets;
using Colony = OpenRA.Mods.OpenSA.Traits.Colony.Colony;

namespace OpenRA.Mods.OpenSA.UtilityCommands
{
	// Opt-in integration check using isolated worlds and real widgets. Never saves user settings.
	public sealed class ValidateRmgOwnershipRuntimeCommand : IUtilityCommand
	{
		string IUtilityCommand.Name => "--validate-sa-rmg-runtime";
		bool IUtilityCommand.ValidateArguments(string[] args) => args.Length == 2 || (args.Length == 3 && args[2] == "--wide");

		[Desc("OUTPUT-DIRECTORY", "Exercise starting-colony ownership in live worlds and render both RMG slider dialogs.")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			var output = Path.GetFullPath(args[1]);
			if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
				throw new ArgumentException("Choose an empty output directory.");
			Directory.CreateDirectory(output);
			Game.ModData = utility.ModData;
			Log.AddChannel("graphics", null);
			Log.AddChannel("sound", null);
			var assembly = new AssemblyLoader(Path.Combine(Platform.BinDir, "OpenRA.Platforms.Default.dll")).LoadDefaultAssembly();
			var platform = (IPlatform)Activator.CreateInstance(assembly.GetTypes().Single(t => typeof(IPlatform).IsAssignableFrom(t)));
			Game.Settings.Graphics.Mode = WindowMode.Windowed;
			Game.Settings.Graphics.WindowedSize = args.Length == 3 ? new int2(1280, 800) : new int2(1024, 600);
			Game.Settings.Graphics.DisableHardwareCursors = true;
			Game.Settings.Graphics.UIScale = 1;
			Game.Renderer = new Renderer(platform, Game.Settings.Graphics);
			Game.Sound = new Sound(platform, Game.Settings.Sound);
			Game.Sound.DisableAllSounds = true;
			utility.ModData.InitializeLoaders(utility.ModData.DefaultFileSystem);
			Game.Renderer.InitializeFonts(utility.ModData);
			utility.ModData.MapCache.LoadMaps();
			CheckWidgets(output);
			CheckLobby(utility, output);
			var results = new JArray();
			void Run(string id, int size, int[] shares, int[] spawns, int absent = -1, bool empty = false)
			{
				var requested = new RmgPlayerSettings { SchemaVersion = 10, MapSize = size, PlayerCount = shares.Length,
					Seed = 397716241463670640, LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscape,
					StartingColonyShares = shares, NeutralColonyWeights = empty ? new(0, 0, 0, 0, 0) : new() };
				var settings = RmgPlayerSettingsContract.Resolve(requested).Normalized;
				var package = OpenRaRmgMapAdapter.GenerateAndSave(utility.ModData, RmgProfile.Load(utility.ModData, settings),
					settings, Path.Combine(output, id + ".oramap"), false, verifyRepeatability: true);
				File.WriteAllText(Path.Combine(output, id + "-report.json"), package.Report.ToString());
				using var directory = new OpenRA.FileSystem.Folder(output);
				utility.ModData.MapCache.LoadMap(id + ".oramap", directory, MapClassification.User, utility.ModData.Manifest.Get<MapGrid>(), null);
				var map = utility.ModData.MapCache[package.EngineUid];
				var first = CheckWorld(utility, map, shares, spawns, absent);
				var second = CheckWorld(utility, map, shares, spawns, absent);
				Require(JToken.DeepEquals(first, second), id + " changed between identical world initializations.");
				first["id"] = id;
				results.Add(first);
				Console.WriteLine($"PASS: {id}, pool {first["pool"]}, assigned {first["counts"]}, repeat world identical.");
			}
			Run("default-zero", 256, new[] { 0, 0, 0 }, new[] { 1, 2, 3 });
			Run("percentages", 256, new[] { 0, 10, 50 }, new[] { 1, 2, 3 });
			Run("weighted-swapped", 256, new[] { 40, 40, 80 }, new[] { 3, 2, 1 });
			Run("closed-slot", 128, new[] { 40, 40, 80 }, new[] { 1, 2, 3 }, absent: 1);
			Run("solo-small", 64, new[] { 100 }, new[] { 1 });
			Run("eight-random", 256, Enumerable.Repeat(100, 8).ToArray(), new int[8]);
			Run("empty-pool", 64, new[] { 100 }, new[] { 1 }, empty: true);
			File.WriteAllText(Path.Combine(output, "verification.json"), new JObject { ["status"] = "PASS", ["cases"] = results }.ToString());
			Game.Renderer.Dispose();
		}

		static JObject CheckWorld(Utility utility, MapPreview map, int[] shares, int[] spawns, int absent)
		{
			var manager = new OrderManager(new EchoConnection());
			foreach (var definition in map.WorldActorInfo.TraitInfos<ILobbyOptions>().Concat(map.PlayerActorInfo.TraitInfos<ILobbyOptions>()).SelectMany(x => x.LobbyOptions(map)))
				manager.LobbyInfo.GlobalSettings.LobbyOptions[definition.Id] = new Session.LobbyOptionState { Value = definition.DefaultValue };
			void Set(string key, string value) => manager.LobbyInfo.GlobalSettings.LobbyOptions[key] = new Session.LobbyOptionState { Value = value };
			foreach (var key in new[] { "creeps", "plants", "flyers", "fog" }) Set(key, "False");
			Set("h-initial-count", "0");
			Set("explored", "True");
			for (var i = 0; i < shares.Length; i++)
			{
				var slot = "Multi" + i;
				manager.LobbyInfo.Slots.Add(slot, new Session.Slot { PlayerReference = slot, AllowBots = true, Closed = i == absent });
				if (i == absent) continue;
				manager.LobbyInfo.Clients.Add(new Session.Client { Index = manager.Connection.LocalClientId + i,
					Slot = slot, Faction = new[] { "ants", "beetles", "wasps" }[i % 3], SpawnPoint = spawns[i], Name = slot,
					Team = 1, IsAdmin = i == 0, Color = Color.FromArgb(255, 30 + i * 25, 150, 220), State = Session.ClientState.Ready });
			}
			manager.LobbyInfo.GlobalSettings.RandomSeed = 12345;
			typeof(Game).GetField("OrderManager", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, manager);
			var world = (World)Activator.CreateInstance(typeof(World), BindingFlags.Instance | BindingFlags.NonPublic,
				null, new object[] { map.Uid, utility.ModData, manager, WorldType.Regular }, null);
			manager.World = world;
			Game.Renderer.InitializeDepthBuffer(utility.ModData.Manifest.Get<MapGrid>());
			using var renderer = (WorldRenderer)Activator.CreateInstance(typeof(WorldRenderer), BindingFlags.Instance | BindingFlags.NonPublic,
				null, new object[] { utility.ModData, world }, null);
			typeof(Game).GetField("worldRenderer", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, renderer);
			Game.Cursor = new CursorManager(utility.ModData.CursorProvider);
			world.LoadComplete(renderer);
			var info = world.WorldActor.Info.TraitInfo<RmgStartingColonyOwnershipInfo>();
			var controller = world.WorldActor.Trait<RmgStartingColonyOwnership>();
			var spawned = world.WorldActor.Trait<SpawnMapActors>().Actors;
			var colonies = info.ColonyActorNames.Select(name => spawned[name]).ToArray();
			Require(colonies.All(a => a.Owner.InternalName == "Creeps"), "Saved colonies must begin neutral before setup assignment.");
			var players = Enumerable.Range(0, shares.Length).Select(i => world.Players.FirstOrDefault(p => p.InternalName == "Multi" + i)).ToArray();
			var startingActors = players.Select(p => p == null ? null : world.Actors.Single(a => a.Owner == p && a.TraitOrDefault<Colony>() != null)).ToArray();
			var starts = startingActors.Select(a => a == null ? new RmgPoint(0, 0) : new RmgPoint(a.Location.X, a.Location.Y)).ToArray();
			var effective = shares.Select((v, i) => players[i] == null ? 0 : v).ToArray();
			var expected = RmgColonyOwnership.Assign(colonies.Select(a => new RmgPoint(a.Location.X, a.Location.Y)).ToArray(), starts, effective);
			var terrain = world.Map.AllCells.Select(c => world.Map.Tiles[c]).ToArray();
			void Tick() { world.Tick(); manager.LocalFrameNumber++; }
			Tick();
			Require(controller.Applied, "Ownership setup did not run on the first tick.");
			Require(controller.AssignedCounts.SequenceEqual(RmgColonyOwnership.Allocate(colonies.Length, effective)), "Runtime quotas did not match allocation.");
			for (var i = 0; i < colonies.Length; i++)
				Require(colonies[i].Owner.InternalName == (expected[i] < 0 ? "Creeps" : players[expected[i]].InternalName), "Runtime colony assigned to wrong slot/start.");
			for (var i = 0; i < 20; i++) Tick();
			foreach (var colony in colonies.Where(a => !a.Owner.NonCombatant))
			{
				var queues = colony.TraitsImplementing<ProductionQueue>().ToArray();
				Require(queues.Length > 0 && queues.Any(q => q.BuildableItems().Any()), "Owned colony has no functional production queue: " + colony.Info.Name);
				Require(queues.All(q => q.Info.Sticky || q.Faction == colony.Owner.Faction.InternalName), "Queue faction was not refreshed after assignment.");
			}
			Require(startingActors.Select((a, i) => a == null || a.Owner == players[i]).All(v => v), "Starting colony changed owner.");
			Require(terrain.SequenceEqual(world.Map.AllCells.Select(c => world.Map.Tiles[c])), "Ownership changed terrain.");
			var result = new JObject { ["pool"] = colonies.Length, ["counts"] = new JArray(controller.AssignedCounts),
				["starts"] = new JArray(starts.Select(p => $"{p.X},{p.Y}")),
				["owners"] = new JArray(colonies.Select(a => a.Owner.InternalName)) };
			var captured = colonies.FirstOrDefault(a => !a.Owner.NonCombatant);
			if (captured != null)
			{
				captured.ChangeOwner(world.Players.First(p => p.InternalName == "Neutral"));
				Tick(); Tick();
				Require(captured.Owner.InternalName == "Neutral", "Setup ownership reapplied after capture.");
			}
			Ui.ResetAll();
			return result;
		}

		static void CheckLobby(Utility utility, string output)
		{
			var manager = new OrderManager(new EchoConnection());
			manager.LobbyInfo.Clients.Add(new Session.Client { Index = manager.Connection.LocalClientId, IsAdmin = true, State = Session.ClientState.NotReady });
			typeof(Game).GetField("OrderManager", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, manager);
			using var source = utility.ModData.DefaultFileSystem.Open("sa|chrome/lobby.yaml");
			var node = MiniYaml.FromStream(source).Single(n => n.Key == "Background@SERVER_LOBBY").Clone();
			// Exercise the real RMG controller and layout without joining or creating a server lobby.
			node.Value.Nodes.RemoveAll(n => n.Key == "Logic");
			node.Value.Nodes.Add(new MiniYamlNode("Logic", "RmgLobbyLogic"));
			var lobby = utility.ModData.WidgetLoader.LoadWidget(new WidgetArgs { { "orderManager", manager }, { "skirmishMode", true } }, Ui.Root, node);
			lobby.Get<ButtonWidget>("RMG_TOGGLE_BUTTON").OnClick();
			var slider = lobby.Get<SliderWidget>("RMG_PLAYERS");
			Require(slider.MaximumValue == 8 && slider.GetValue() == 4, "Default RMG player range changed.");
			slider.UpdateValue(8);
			void Size(string prefix)
			{
				var button = lobby.Get<DropDownButtonWidget>("RMG_SIZE");
				button.OnMouseDown(default);
				var panel = Ui.Root.Children.OfType<ScrollPanelWidget>().Last();
				panel.Children.OfType<ScrollItemWidget>().Single(r => r.Get<LabelWidget>("LABEL").GetText().StartsWith(prefix, StringComparison.Ordinal)).OnClick();
				button.RemovePanel();
			}
			Size("64");
			Require(slider.MaximumValue == 4 && slider.GetValue() == 4 && lobby.Get<LabelWidget>("RMG_PLAYERS_MAX").GetText() == "4", "Small-map cap did not update actual widgets.");
			slider.UpdateValue(8);
			Require(slider.GetValue() == 4, "Slider callback bypassed small-map cap.");
			if (Game.Renderer.Resolution.Width >= 1182) Draw(output, "rmg-small-four-players");
			lobby.Get<ButtonWidget>("RMG_COLONY_OWNERSHIP").OnClick();
			var dialog = Ui.CurrentWindow();
			Require(dialog.Get<ScrollPanelWidget>("SETTINGS").Children.Count == 4, "Small map opened wrong number of ownership sliders.");
			var field = dialog.Get<ScrollPanelWidget>("SETTINGS").Children.First().Get<TextFieldWidget>("VALUE");
			field.Text = "35"; field.OnTextEdited();
			dialog.Get<ButtonWidget>("APPLY").OnClick();
			lobby.Get<ButtonWidget>("RMG_COLONY_OWNERSHIP").OnClick();
			dialog = Ui.CurrentWindow();
			Require(dialog.Get<ScrollPanelWidget>("SETTINGS").Children.First().Get<TextFieldWidget>("VALUE").Text == "35", "Lobby ownership Apply was not retained.");
			dialog.Get<ButtonWidget>("CANCEL").OnClick();
			Size("256"); slider.UpdateValue(8);
			Require(slider.MaximumValue == 8 && slider.GetValue() == 8, "Large-map range did not restore.");
			lobby.Get<ButtonWidget>("RMG_COLONY_OWNERSHIP").OnClick();
			dialog = Ui.CurrentWindow();
			Require(dialog.Get<ScrollPanelWidget>("SETTINGS").Children.Count == 8, "Large map opened wrong number of ownership sliders.");
			dialog.Get<ButtonWidget>("CANCEL").OnClick();
			lobby.Get<ButtonWidget>("RMG_COLONY_WEIGHTS").OnClick();
			Require(Ui.CurrentWindow().Id == "RMG_COLONY_WEIGHTS_PANEL", "Species launcher opened wrong window.");
			Ui.CurrentWindow().Get<ButtonWidget>("CANCEL").OnClick();
			Ui.ResetAll();
			Console.WriteLine("PASS: actual RMG launchers, default range, 8 -> 4 small-map cap, 4 -> 8 larger range, ownership row counts and Apply persistence.");
		}

		static void CheckWidgets(string output)
		{
			foreach (var count in new[] { 1, 3, 8 })
			{
				int[] applied = null;
				var disabled = false;
				var original = new int[count];
				Widget Open() => Ui.OpenWindow("RMG_COLONY_OWNERSHIP_PANEL", new WidgetArgs {
					{ "initialShares", original }, { "configurationDisabled", (Func<bool>)(() => disabled) },
					{ "onApply", (Action<int[]>)(v => applied = v) } });
				var widget = Open();
				var panel = widget.Get<ScrollPanelWidget>("SETTINGS");
				Require(panel.Children.Count == count, "Wrong ownership row count.");
				Require(panel.Children.All(r => r.Get<SliderWidget>("SLIDER").MaximumValue == 100), "Wrong slider maximum.");
				var field = panel.Children.First().Get<TextFieldWidget>("VALUE");
				field.Text = "101"; field.OnTextEdited();
				Require(widget.Get<ButtonWidget>("APPLY").IsDisabled(), "Invalid ownership value accepted.");
				field.Text = "40"; field.OnTextEdited();
				Draw(output, "ownership-" + count);
				if (count == 8) { panel.ScrollToBottom(); Draw(output, "ownership-8-bottom"); }
				disabled = true;
				widget.Get<ButtonWidget>("APPLY").OnClick();
				Require(applied == null, "Non-host applied ownership.");
				disabled = false;
				widget.Get<ButtonWidget>("CANCEL").OnClick();
				Require(applied == null && original.All(v => v == 0), "Cancel mutated ownership.");
				widget = Open();
				field = widget.Get<ScrollPanelWidget>("SETTINGS").Children.First().Get<TextFieldWidget>("VALUE");
				field.Text = "100"; field.OnTextEdited();
				widget.Get<ButtonWidget>("RESET").OnClick();
				Require(field.Text == "0", "Reset did not clear shares.");
				field.Text = "25"; field.OnTextEdited();
				widget.Get<ButtonWidget>("APPLY").OnClick();
				Require(applied[0] == 25 && original[0] == 0, "Apply failed or mutated original.");
			}
			RmgColonyWeights weights = null;
			var species = Ui.OpenWindow("RMG_COLONY_WEIGHTS_PANEL", new WidgetArgs {
				{ "initialWeights", new RmgColonyWeights() }, { "configurationDisabled", (Func<bool>)(() => false) },
				{ "onApply", (Action<RmgColonyWeights>)(v => weights = v) } });
			Require(species.Get<ScrollPanelWidget>("SETTINGS").Children.All(r => r.Get<SliderWidget>("SLIDER").MaximumValue == 100), "Species slider maximum.");
			Draw(output, "species-weights");
			species.Get<ButtonWidget>("APPLY").OnClick();
			Require(weights.Values.All(v => v == 100), "Species defaults changed.");
			Ui.ResetAll();
			Console.WriteLine("PASS: real ownership/species widgets, 1/3/8 rows, validation, reset, Cancel/Apply isolation, host guard, scrolling.");
		}

		static void Draw(string output, string name)
		{
			for (var i = 0; i < 3; i++) { Ui.Tick(); Game.Renderer.BeginUI(); Ui.Draw(); Game.Renderer.EndFrame(new IgnoreInput()); }
			var path = Path.Combine(output, name + ".png");
			Game.Renderer.SaveScreenshot(path);
			for (var i = 0; i < 100 && !File.Exists(path); i++) Thread.Sleep(20);
			Require(File.Exists(path), "Screenshot was not saved.");
		}

		static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
		sealed class IgnoreInput : IInputHandler
		{
			public void ModifierKeys(Modifiers mods) { }
			public void OnKeyInput(KeyInput input) { }
			public void OnMouseInput(MouseInput input) { }
			public void OnTextInput(string text) { }
		}
	}
}
