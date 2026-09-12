#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using Newtonsoft.Json.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Mods.OpenSA.Rmg;
using OpenRA.Mods.OpenSA.Traits.World;
using OpenRA.Mods.OpenSA.Widgets;
using OpenRA.Network;
using OpenRA.Primitives;
using OpenRA.Server;
using OpenRA.Widgets;

namespace OpenRA.Mods.OpenSA.UtilityCommands
{
	public sealed partial class ValidateRmgOwnershipRuntimeCommand
	{
		static void CheckQol(Utility utility, string output)
		{
			var settings = RmgPlayerSettingsContract.Resolve(new RmgPlayerSettings { SchemaVersion = 10, LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscape, MapSize = 128, PlayerCount = 8, Seed = 74231 }).Normalized;
			var package = OpenRaRmgMapAdapter.GenerateAndSave(utility.ModData, RmgProfile.Load(utility.ModData, settings), settings,
				Path.Combine(output, "qol.oramap"), false);
			using var directory = new OpenRA.FileSystem.Folder(output);
			utility.ModData.MapCache.LoadMap("qol.oramap", directory, MapClassification.User, utility.ModData.Manifest.Get<MapGrid>(), null);
			var map = utility.ModData.MapCache[package.EngineUid];
			CheckWorld(utility, map, new int[8], new int[8], -1, false, null, false, renderer =>
			{
				CheckQolRanges(renderer, output);
				CheckQolLobby(utility, map, renderer, output);
			});
			File.WriteAllText(Path.Combine(output, "verification.json"), new JObject
			{
				["status"] = "PASS", ["scope"] = "LIVE_QOL_WIDGETS_AND_SERVER",
				["lobby"] = "All map bot difficulties, closed slots, existing bots, humans, no-bot slots, ready/non-host guards, RMG and tab visibility, successful server start",
				["ranges"] = "Real unit and colony weapons, selection, movement, disabled armament, local sync hash, button, Alt tap/repeat/chords/mouse/keyboard focus"
			}.ToString());
		}

		static void CheckQolRanges(WorldRenderer renderer, string output)
		{
			var world = renderer.World;
			var overlay = world.WorldActor.Trait<SelectedUnitRangeOverlay>();
			var button = Ui.Root.Get<ButtonWidget>("SELECTED_UNIT_RANGE");
			var listener = Ui.Root.Get<SelectedUnitRangeHotkeyWidget>("SELECTED_UNIT_RANGE_HOTKEY");
			var colony = world.Actors.First(a => a.Owner == world.LocalPlayer && a.Info.Name.EndsWith("_colony", StringComparison.Ordinal));
			var location = colony.Location + new CVec(7, 7);
			var units = new[] { "scorpions_medium", "ants_light", "wasps_heavy" }.Select((name, i) => world.CreateActor(name,
				new TypeDictionary { new OwnerInit(world.LocalPlayer), new LocationInit(location + new CVec(i * 3, 0)) })).ToArray();
			for (var i = 0; i < 3; i++) world.Tick();
			world.Selection.Combine(world, units.Append(colony), false, false);
			var hash = world.SyncHash();
			Require(!overlay.Enabled && !overlay.RenderAnnotations(world.WorldActor, renderer).Any(), "Range overlay must start off.");
			button.OnClick();
			Require(button.IsHighlighted() && overlay.Enabled && world.SyncHash() == hash, "Button changed synchronized gameplay state or failed to highlight.");
			var circles = overlay.RenderAnnotations(world.WorldActor, renderer).ToArray();
			Require(circles.Length == 4, "Expected a range for each armed unit and colony.");
			foreach (var actor in units.Append(colony))
			{
				var circle = circles.Single(c => c.Pos == actor.CenterPosition);
				var radius = (WDist)typeof(RangeCircleAnnotationRenderable).GetField("radius", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(circle);
				Require(radius == actor.TraitsImplementing<AttackBase>().Max(a => a.GetMaximumRange()), "Displayed range disagrees with live weapon range.");
			}

			var before = units[0].CenterPosition;
			units[0].Trait<Mobile>().SetPosition(units[0], units[0].Location + new CVec(2, 0));
			Require(overlay.RenderAnnotations(world.WorldActor, renderer).Any(c => c.Pos == units[0].CenterPosition && c.Pos != before), "Range did not follow movement.");
			var token = units[0].GrantCondition("paralyzed");
			Require(!overlay.RenderAnnotations(world.WorldActor, renderer).Any(c => c.Pos == units[0].CenterPosition), "Paused weapon kept a usable range.");
			units[0].RevokeCondition(token);
			world.Selection.Clear();
			Require(!overlay.RenderAnnotations(world.WorldActor, renderer).Any() && overlay.Enabled, "Empty selection should retain toggle state without circles.");
			world.Selection.Combine(world, units.Append(colony), false, false);
			Require(overlay.RenderAnnotations(world.WorldActor, renderer).Count() == 4, "New selection did not inherit toggle.");
			Require(button.RenderBounds.Bottom <= Game.Renderer.Resolution.Height && button.RenderBounds.Left > Ui.Root.Get("STANCE_HOLDFIRE").RenderBounds.Right,
				"Range control is clipped or overlaps stance controls.");

			void Key(Keycode key, KeyInputEvent ev, Modifiers mods = Modifiers.None, bool repeat = false) =>
				Ui.HandleKeyPress(new KeyInput { Key = key, Event = ev, Modifiers = mods, IsRepeat = repeat });
			void Down() => Key(Keycode.LALT, KeyInputEvent.Down, Modifiers.Alt);
			void Up() => Key(Keycode.LALT, KeyInputEvent.Up);

			// Native window input is pumped before synthesizing events through the real widget tree.
			CaptureQolWorld(renderer, output, "ranges-on", colony.CenterPosition);

			// The tool window need not take focus from the user. Inject SDL's focus flag for input tests only.
			var window = typeof(Renderer).GetProperty("Window", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Game.Renderer);
			var focus = window.GetType().GetProperty("HasInputFocus");
			focus.SetValue(window, true);
			Down(); Key(Keycode.LALT, KeyInputEvent.Down, Modifiers.Alt, true);
			Require(overlay.Enabled, "Alt toggled before release or on repeat."); Up();
			Require(!overlay.Enabled, "Bare Alt tap did not toggle off.");
			Key(Keycode.RALT, KeyInputEvent.Down, Modifiers.Alt); Key(Keycode.RALT, KeyInputEvent.Up);
			Require(overlay.Enabled, "Right Alt tap did not toggle on.");
			Down(); Key(Keycode.A, KeyInputEvent.Down, Modifiers.Alt); Up();
			Require(overlay.Enabled, "Alt shortcut also toggled ranges.");
			Down(); listener.HandleMouseInput(new MouseInput(MouseInputEvent.Down, MouseButton.Right, new int2(300, 300), int2.Zero, Modifiers.Alt, 1)); Up();
			Require(overlay.Enabled, "Alt mouse command also toggled ranges.");
			Down(); Key(Keycode.LCTRL, KeyInputEvent.Down, Modifiers.Ctrl | Modifiers.Alt); Up();
			Require(overlay.Enabled, "AltGr/Ctrl+Alt toggled ranges.");
			Down(); Ui.KeyboardFocusWidget = new TextFieldWidget(); listener.Tick(); Ui.KeyboardFocusWidget = null; Up();
			Require(overlay.Enabled, "Keyboard focus interruption left a pending Alt tap.");
			Down(); focus.SetValue(window, false); listener.Tick(); focus.SetValue(window, true); Up();
			Require(overlay.Enabled, "Focus loss left a pending Alt tap.");
			Down(); listener.Hidden(); Up(); Require(overlay.Enabled, "Hidden control left a pending Alt tap.");
			Down(); Key(Keycode.TAB, KeyInputEvent.Down, Modifiers.Alt); Up(); Require(overlay.Enabled, "Alt+Tab toggled ranges.");
			button.OnClick(); CaptureQolWorld(renderer, output, "ranges-off", colony.CenterPosition);
			Console.WriteLine("PASS: live range overlay, weapon values, moving selection, pause state, no sync changes, button and Alt input combinations.");
		}

		static void CaptureQolWorld(WorldRenderer renderer, string output, string name, WPos center)
		{
			renderer.Viewport.Center(center);
			for (var frame = 0; frame < 3; frame++)
			{
				Ui.Tick(); renderer.World.TickRender(renderer); renderer.PrepareRenderables(); Ui.PrepareRenderables();
				Game.Renderer.BeginWorld(renderer.Viewport.Rectangle); renderer.Draw();
				Game.Renderer.BeginUI(); renderer.DrawAnnotations(); Ui.Draw(); Game.Renderer.EndFrame(new IgnoreInput());
			}

			var file = Path.Combine(output, name + ".png"); Game.Renderer.SaveScreenshot(file);
			for (var i = 0; i < 100 && !File.Exists(file); i++) Thread.Sleep(20);
			Require(File.Exists(file), "World screenshot missing.");
		}

		static void CheckQolLobby(Utility utility, MapPreview map, WorldRenderer renderer, string output)
		{
			Ui.ResetAll();
			var server = new OpenRA.Server.Server(new List<IPEndPoint> { new(IPAddress.Loopback, 0) },
				new ServerSettings { Name = "Skirmish Game", Map = map.Uid, AdvertiseOnline = false, RecordReplays = false, QueryMapRepository = false },
				utility.ModData, ServerType.Local);
			try
			{
				using IConnection connection = new NetworkConnection(server.GetEndpointForLocalConnection());
				void Wait(Func<bool> condition, string message)
				{
					var timer = Stopwatch.StartNew();
					while (timer.ElapsedMilliseconds < 10000)
					{
						lock (server.LobbyInfo) if (condition()) return;
						Game.PerformDelayedActions(); Thread.Sleep(20);
					}

					throw new InvalidOperationException(message);
				}

				Wait(() => server.Conns.Count == 1, "Test connection failed.");
				var handshake = new HandshakeResponse
				{
					Mod = utility.ModData.Manifest.Id,
					Version = utility.ModData.Manifest.Metadata.Version,
					OrdersProtocol = ProtocolVersion.Orders,
					Client = new Session.Client { Name = "QoL review", Faction = "Random", Color = Color.Red, PreferredColor = Color.Red }
				};
				connection.SendImmediate(new[] { new Order("HandshakeResponse", null, false) { Type = OrderType.Handshake, IsImmediate = true, TargetString = handshake.Serialize() } });
				Wait(() => server.Conns[0].Validated, "Test handshake failed.");
				connection.SendImmediate(new[] { Order.Command("state NotReady") });
				Wait(() => server.LobbyInfo.Clients[0].State == Session.ClientState.NotReady, "Map acknowledgement failed.");
				var manager = new OrderManager(connection);
				void Refresh() { lock (server.LobbyInfo) manager.LobbyInfo = Session.Deserialize(server.LobbyInfo.Serialize()); }
				Refresh();
				typeof(Game).GetField("OrderManager", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, manager);
				var lobby = Ui.LoadWidget("SERVER_LOBBY", Ui.Root, new WidgetArgs
				{
					{ "orderManager", manager }, { "worldRenderer", renderer }, { "skirmishMode", true },
					{ "onExit", () => { } }, { "onStart", () => { } }
				});
				var lobbyLogic = lobby.LogicObjects.Single(l => l.GetType().Name == "LobbyLogic");
				void UpdatePlayers() => lobbyLogic.GetType().GetMethod("UpdatePlayerList", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(lobbyLogic, null);
				UpdatePlayers();
				var queued = (List<Order>)typeof(OrderManager).GetField("localImmediateOrders", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
				queued.Clear();
				var fill = lobby.Get<DropDownButtonWidget>("FILL_OPPONENTS");
				Require(fill.IsVisible() && !fill.IsDisabled(), "Fill button unavailable in playable skirmish.");
				Wait(() => map.GetMinimap() != null, "Map preview failed to load.");
				fill.OnMouseDown(default); Draw(output, "fill-opponents-menu"); fill.RemovePanel();
				var types = map.PlayerActorInfo.TraitInfos<OpenRA.Traits.IBotInfo>().ToArray();
				Require(types.Length >= 3, "Missing AI difficulties.");
				foreach (var bot in types)
				{
					fill.OnMouseDown(default);
					var menu = Ui.Root.Children.OfType<ScrollPanelWidget>().Last();
					Require(menu.Children.OfType<ScrollItemWidget>().Count() == types.Length, "Dropdown omitted a difficulty.");
					menu.Children.OfType<ScrollItemWidget>().Single(r => r.Get<LabelWidget>("LABEL").GetText() == bot.Name).OnClick();
					Require(queued.Count == 7 && queued.All(o => o.TargetString.EndsWith(" " + bot.Type, StringComparison.Ordinal)), "Fill did not queue seven matching opponents.");
					connection.SendImmediate(queued.ToArray()); queued.Clear();
					Wait(() => server.LobbyInfo.Clients.Count == 8 && server.LobbyInfo.Clients.Where(c => c.Bot != null).All(c => c.Bot == bot.Type), "Server did not fill/update the selected difficulty.");
					Refresh();
				}

				// Use the real server's lobby state to exercise exceptional slots too.
				lock (server.LobbyInfo)
				{
					server.LobbyInfo.Clients.RemoveAll(c => c.Slot is "Multi1" or "Multi7");
					server.LobbyInfo.Slots["Multi1"].Closed = true;
					server.LobbyInfo.Slots["Multi7"].AllowBots = false;
					server.LobbyInfo.ClientInSlot("Multi2").Bot = null;
					server.LobbyInfo.ClientInSlot("Multi3").Team = 3;
					server.LobbyInfo.ClientInSlot("Multi3").Handicap = 20;
				}

				Refresh(); fill.OnMouseDown(default);
				Ui.Root.Children.OfType<ScrollPanelWidget>().Last().Children.OfType<ScrollItemWidget>().First().OnClick();
				Require(queued.Count == 5 && !queued.Any(o => o.TargetString.StartsWith("slot_bot Multi2 ", StringComparison.Ordinal) || o.TargetString.StartsWith("slot_bot Multi7 ", StringComparison.Ordinal)), "Fill overwrote a human or no-bot slot.");
				connection.SendImmediate(queued.ToArray()); queued.Clear();
				Wait(() => server.LobbyInfo.ClientInSlot("Multi1")?.Bot == types[0].Type, "Closed slot was not filled.");
				Require(!server.LobbyInfo.Slots["Multi1"].Closed && server.LobbyInfo.ClientInSlot("Multi3").Team == 3 && server.LobbyInfo.ClientInSlot("Multi3").Handicap == 20, "Fill discarded existing bot configuration.");
				Refresh();
				manager.LocalClient.State = Session.ClientState.Ready; Require(fill.IsDisabled(), "Ready host can fill opponents."); manager.LocalClient.State = Session.ClientState.NotReady;
				manager.LocalClient.IsAdmin = false; Require(fill.IsDisabled(), "Non-host can fill opponents."); manager.LocalClient.IsAdmin = true;
				lobby.Get("SKIRMISH_TABS").Get<ButtonWidget>("OPTIONS_TAB").OnClick(); Require(!fill.IsVisible(), "Fill visible on Options.");
				lobby.Get("SKIRMISH_TABS").Get<ButtonWidget>("PLAYERS_TAB").OnClick(); Require(fill.IsVisible(), "Fill missing after returning to Players.");
				lobby.Get<ButtonWidget>("RMG_TOGGLE_BUTTON").OnClick(); Require(!fill.IsVisible(), "Fill overlaps RMG Save Map.");
				lobby.Get<ButtonWidget>("RMG_TOGGLE_BUTTON").OnClick(); Require(fill.IsVisible(), "Fill missing after returning from RMG.");
				UpdatePlayers(); Draw(output, "fill-opponents-lobby");
				Ui.ResetAll();
				lock (server.LobbyInfo)
				{
					server.LobbyInfo.ClientInSlot("Multi2").Bot = types[0].Type;
					server.LobbyInfo.Slots["Multi7"].Closed = true;
				}

				connection.SendImmediate(new[] { Order.Command("startgame") });
				Wait(() => server.State == ServerState.GameStarted, "Filled lobby could not start a skirmish.");
				Console.WriteLine("PASS: real lobby dropdown, all AI difficulties, protected humans, closed/no-bot slots, guards, tab/RMG visibility and actual server start.");
			}
			finally { server.Shutdown(); }
		}
	}
}
