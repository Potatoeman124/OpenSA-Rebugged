#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Linq;
using System.Reflection;
using System.Threading;
using Newtonsoft.Json.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Mods.OpenSA.Rmg;
using OpenRA.Mods.OpenSA.Traits.World;
using OpenRA.Mods.OpenSA.Widgets;
using OpenRA.Server;
using OpenRA.Network;
using OpenRA.Primitives;
using OpenRA.Support;
using OpenRA.Traits;
using OpenRA.Widgets;
using Colony = OpenRA.Mods.OpenSA.Traits.Colony.Colony;

namespace OpenRA.Mods.OpenSA.UtilityCommands
{
	// Opt-in integration check using an isolated server, worlds and real widgets. Never saves user settings.
	public sealed partial class ValidateRmgOwnershipRuntimeCommand : IUtilityCommand
	{
		string IUtilityCommand.Name => "--validate-sa-rmg-runtime";
		bool IUtilityCommand.ValidateArguments(string[] args) => args.Length >= 2 && args.Length <= 4 && args.Skip(2).All(a => a is "--wide" or "--512" or "--save" or "--pvp" or "--battlefield");

		[Desc("OUTPUT-DIRECTORY [--wide] [--512|--save|--pvp|--battlefield]", "Exercise colony ownership, live previews and skirmish startup; --512 checks large maps, --save checks saved copies, --pvp checks mirrored Regions.")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			var output = Path.GetFullPath(args[1]);
			if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
				throw new ArgumentException("Choose an empty output directory.");
			Directory.CreateDirectory(output);
			Game.ModData = utility.ModData;
			Log.AddChannel("graphics", null);
			Log.AddChannel("sound", null);
			Log.AddChannel("client", null);
			var assembly = new AssemblyLoader(Path.Combine(Platform.BinDir, "OpenRA.Platforms.Default.dll")).LoadDefaultAssembly();
			var platform = (IPlatform)Activator.CreateInstance(assembly.GetTypes().Single(t => typeof(IPlatform).IsAssignableFrom(t)));
			Game.Settings.Graphics.Mode = WindowMode.Windowed;
			Game.Settings.Graphics.WindowedSize = args.Contains("--wide") ? new int2(1280, 800) : new int2(1024, 600);
			Game.Settings.Graphics.DisableHardwareCursors = true;
			Game.Settings.Graphics.UIScale = 1;
			Game.Renderer = new Renderer(platform, Game.Settings.Graphics);
			Game.Sound = new Sound(platform, Game.Settings.Sound);
			Game.Sound.DisableAllSounds = true;
			utility.ModData.InitializeLoaders(utility.ModData.DefaultFileSystem);
			Game.Renderer.InitializeFonts(utility.ModData);
			utility.ModData.MapCache.LoadMaps();
			if (args.Contains("--save"))
			{
				CheckSavedMaps(utility, output);
				Game.Renderer.Dispose();
				return;
			}

			if (args.Contains("--pvp") || args.Contains("--battlefield")) CheckPvpWidgets(utility, output);
			else { CheckWidgets(output); CheckLobby(utility, output); }
			var results = new JArray();
			var battlefield = args.Contains("--battlefield");
			void Run(string id, int size, int[] shares, int[] spawns, int absent = -1, bool empty = false, bool bots = false, ulong seed = 397716241463670640, bool crowded = false, RmgColonyOwnershipMode mode = RmgColonyOwnershipMode.ClosestToSpawn, string tileset = "NORMAL", bool hostiles = false, int axes = 0, RmgPlayerSettings options = null)
			{
				var requested = options ?? new RmgPlayerSettings { SchemaVersion = battlefield ? 12 : axes == 0 ? 10 : 11, MirroringAxes = axes, MapSize = size, PlayerCount = shares.Length,
					Seed = seed, Tileset = tileset, NeutralColonyDensity = crowded ? RmgPlayerColonyDensity.Ultra : RmgPlayerColonyDensity.Standard, LayoutFamily = battlefield ? RmgPlayerLayoutFamily.ArtificialBattlefield : axes == 0 ? RmgPlayerLayoutFamily.NaturalLandscape : RmgPlayerLayoutFamily.NaturalLandscapePvp,
					StartingColonyShares = shares, StartingColonyMode = mode, NeutralColonyWeights = empty ? new(0, 0, 0, 0, 0) : new() };
				var settings = RmgPlayerSettingsContract.Resolve(requested).Normalized;
				var package = OpenRaRmgMapAdapter.GenerateAndSave(utility.ModData, RmgProfile.Load(utility.ModData, settings),
					settings, Path.Combine(output, id + ".oramap"), false, verifyRepeatability: true);
				File.WriteAllText(Path.Combine(output, id + "-report.json"), package.Report.ToString());
				using var directory = new OpenRA.FileSystem.Folder(output);
				utility.ModData.MapCache.LoadMap(id + ".oramap", directory, MapClassification.User, utility.ModData.Manifest.Get<MapGrid>(), null);
				var map = utility.ModData.MapCache[package.EngineUid];
				if (axes != 0 || battlefield) map = SavePvpRuntimeCopy(utility, map, directory, id);
				if (bots && crowded) CheckServer(utility, map);
				var first = CheckWorld(utility, map, shares, spawns, absent, bots, Path.Combine(output, id), hostiles);
				var second = CheckWorld(utility, map, shares, spawns, absent, bots, null, hostiles);
				Require(JToken.DeepEquals(first, second), id + " changed between identical world initializations.");
				first["id"] = id;
				results.Add(first);
				Console.WriteLine($"PASS: {id}, pool {first["pool"]}, assigned {first["counts"]}, repeat world identical.");
			}

			if (battlefield)
			{
				Run("battlefield-small", 64, new[] { 0, 10, 20, 30 }, new int[4]);
				Run("battlefield-desert", 128, new[] { 0, 10, 20, 30 }, new int[4], bots: true, crowded: true, tileset: "DESERT", hostiles: true);
				Run("battlefield-swamp", 256, new[] { 40, 80 }, new int[2], seed: 1, bots: true, mode: RmgColonyOwnershipMode.Random, tileset: "SWAMP");
				Run("battlefield-candy-eight", 512, Enumerable.Repeat(100, 8).ToArray(), new int[8], bots: true, mode: RmgColonyOwnershipMode.Random, tileset: "CANDY");
				Run("battlefield-review-eight", 256, new int[8], new int[8], bots: true, options: new RmgPlayerSettings
				{
					SchemaVersion = 12, LayoutFamily = RmgPlayerLayoutFamily.ArtificialBattlefield,
					Seed = 104842342679145068, MapSize = 256, PlayerCount = 8, StartingColonyShares = new int[8],
					BlockShape = RmgBattlefieldBlockShape.Diamonds, LaneWidth = RmgBattlefieldLaneWidth.Wide,
					TerrainComplexity = Rmg.Reassessment.TerrainComplexity.Ultra, WaterAmount = RmgPlayerParameterLevel.Extreme,
					TacticalTerrain = RmgPlayerParameterLevel.Extreme, NeutralColonyDensity = RmgPlayerColonyDensity.Sparse
				});
			}
			else if (args.Contains("--pvp"))
			{
				Run("pvp-small-two-axes", 64, new[] { 0, 10, 20, 30 }, new int[4], seed: 0, axes: 2);
				Run("pvp-desert-six", 128, new[] { 10, 20, 30, 40, 50, 60 }, new int[6], seed: 1, bots: true, mode: RmgColonyOwnershipMode.Random, tileset: "DESERT", axes: 1);
				Run("pvp-swamp-four", 256, new[] { 0, 10, 20, 30 }, new int[4], bots: true, crowded: true, tileset: "SWAMP", hostiles: true, axes: 2);
				Run("pvp-candy-eight", 512, Enumerable.Repeat(100, 8).ToArray(), new int[8], bots: true, mode: RmgColonyOwnershipMode.Random, tileset: "CANDY", axes: 4);
			}
			else if (args.Contains("--512"))
			{
				Run("512-normal-closest", 512, new[] { 0, 10, 20, 30 }, new int[4], bots: true, hostiles: true);
				Run("512-desert-random", 512, new[] { 0, 10, 20, 30 }, new int[4], bots: true, seed: 748797295927410807, crowded: true, mode: RmgColonyOwnershipMode.Random, tileset: "DESERT");
				Run("512-swamp-solo", 512, new[] { 50 }, new[] { 1 }, mode: RmgColonyOwnershipMode.Random, tileset: "SWAMP");
				Run("512-candy-eight", 512, Enumerable.Repeat(100, 8).ToArray(), new int[8], bots: true, mode: RmgColonyOwnershipMode.Random, tileset: "CANDY");
			}
			else
			{
				Run("reported-crash", 256, new[] { 0, 10, 20, 30 }, new int[4], bots: true, seed: 748797295927410807, crowded: true);
				Run("ai-other-seed", 128, new[] { 40, 40, 80 }, new int[3], bots: true, seed: 0);
				Run("default-zero", 256, new[] { 0, 0, 0 }, new[] { 1, 2, 3 });
				Run("percentages", 256, new[] { 0, 10, 50 }, new[] { 1, 2, 3 });
				Run("weighted-swapped", 256, new[] { 40, 40, 80 }, new[] { 3, 2, 1 });
				Run("closed-slot", 128, new[] { 40, 40, 80 }, new[] { 1, 2, 3 }, absent: 1);
				Run("solo-small", 64, new[] { 100 }, new[] { 1 });
				Run("eight-random", 256, Enumerable.Repeat(100, 8).ToArray(), new int[8]);
				Run("empty-pool", 64, new[] { 100 }, new[] { 1 }, empty: true);
				Run("random-reported", 256, new[] { 0, 10, 20, 30 }, new int[4], bots: true, seed: 748797295927410807, crowded: true, mode: RmgColonyOwnershipMode.Random);
				Run("random-weighted", 128, new[] { 40, 40, 80 }, new int[3], bots: true, seed: ulong.MaxValue, mode: RmgColonyOwnershipMode.Random);
				Run("random-closed", 128, new[] { 40, 40, 80 }, new[] { 3, 2, 1 }, absent: 1, mode: RmgColonyOwnershipMode.Random);
				Run("random-solo", 64, new[] { 50 }, new[] { 1 }, mode: RmgColonyOwnershipMode.Random);
				Run("random-eight", 256, Enumerable.Repeat(100, 8).ToArray(), new int[8], mode: RmgColonyOwnershipMode.Random);
				Run("random-empty", 64, new[] { 100 }, new[] { 1 }, empty: true, mode: RmgColonyOwnershipMode.Random);
				Run("random-zero", 128, new[] { 0, 0 }, new[] { 1, 2 }, mode: RmgColonyOwnershipMode.Random);
				Run("normal-hostiles", 128, new[] { 0, 10, 50 }, new int[3], bots: true, hostiles: true);
				foreach (var tileset in RmgBiome.Tilesets.Where(t => t != "NORMAL"))
				{
					Run(tileset + "-closest", 128, new[] { 0, 10, 50 }, new int[3], bots: true, tileset: tileset, hostiles: true);
					Run(tileset + "-random", 256, new[] { 0, 10, 20, 30 }, new int[4], bots: true, seed: 748797295927410807, crowded: true, mode: RmgColonyOwnershipMode.Random, tileset: tileset);
					Run(tileset + "-solo", 64, new[] { 50 }, new[] { 1 }, mode: RmgColonyOwnershipMode.Random, tileset: tileset);
					Run(tileset + "-eight", 256, Enumerable.Repeat(100, 8).ToArray(), new int[8], mode: RmgColonyOwnershipMode.Random, tileset: tileset);
				}
			}

			File.WriteAllText(Path.Combine(output, "verification.json"), new JObject { ["status"] = "PASS", ["cases"] = results }.ToString());
			Game.Renderer.Dispose();
		}

		static JObject CheckWorld(Utility utility, MapPreview map, int[] shares, int[] spawns, int absent, bool bots, string screenshot, bool hostiles)
		{
			var manager = new OrderManager(new EchoConnection());
			foreach (var definition in map.WorldActorInfo.TraitInfos<ILobbyOptions>().Concat(map.PlayerActorInfo.TraitInfos<ILobbyOptions>()).SelectMany(x => x.LobbyOptions(map)))
				manager.LobbyInfo.GlobalSettings.LobbyOptions[definition.Id] = new Session.LobbyOptionState { Value = definition.DefaultValue };
			void Set(string key, string value) => manager.LobbyInfo.GlobalSettings.LobbyOptions[key] = new Session.LobbyOptionState { Value = value };
			foreach (var key in new[] { "creeps", "plants", "flyers", "fog" }) Set(key, "False");
			if (hostiles) { Set("plants", "True"); Set("flyers", "True"); }
			Set("h-initial-count", "0");
			Set("explored", "True");
			for (var i = 0; i < shares.Length; i++)
			{
				var slot = "Multi" + i;
				manager.LobbyInfo.Slots.Add(slot, new Session.Slot { PlayerReference = slot, AllowBots = true, Closed = i == absent });
				if (i == absent) continue;
				manager.LobbyInfo.Clients.Add(new Session.Client { Index = manager.Connection.LocalClientId + i,
					Slot = slot, Faction = bots ? "Random" : new[] { "ants", "beetles", "wasps" }[i % 3], SpawnPoint = spawns[i], Name = slot,
					Team = bots ? 0 : 1, Bot = bots && i > 0 ? "easy" : null, BotControllerClientIndex = manager.Connection.LocalClientId, IsAdmin = i == 0, Color = bots ? new[] { Color.FromArgb(255, 210, 25, 25), Color.FromArgb(255, 225, 245, 0), Color.FromArgb(255, 0, 235, 65), Color.FromArgb(255, 245, 190, 20) }[i % 4] : Color.FromArgb(255, 30 + i * 25, 150, 220), State = Session.ClientState.Ready });
			}
			manager.LobbyInfo.GlobalSettings.RandomSeed = 12345;
			manager.LobbyInfo.GlobalSettings.Map = map.Uid;
			using var yaml = map.Package.GetStream("map.yaml");
			var sites = NeutralColonyPreview.ReadSites(MiniYaml.FromStream(yaml));
			var forecast = RmgOwnershipPreview.Resolve(map, manager.LobbyInfo, sites);
			if (screenshot != null && shares.Any(v => v > 0))
			{
				CheckPreview(map, manager, sites, screenshot);
				CheckAcknowledgement(utility, map, manager);
			}
			typeof(Game).GetField("OrderManager", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, manager);
			var world = (World)Activator.CreateInstance(typeof(World), BindingFlags.Instance | BindingFlags.NonPublic,
				null, new object[] { map.Uid, utility.ModData, manager, WorldType.Regular }, null);
			string firstPlant = null, firstFlier = null;
			world.ActorAdded += actor =>
			{
				if (HostileOptions.Plants.Contains(actor.Info.Name)) firstPlant ??= actor.Info.Name;
				if (HostileOptions.Fliers.Contains(actor.Info.Name)) firstFlier ??= actor.Info.Name;
			};
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
			var expected = RmgColonyOwnership.Assign(colonies.Select(a => new RmgPoint(a.Location.X, a.Location.Y)).ToArray(), starts, effective, info.ChoiceMode, info.RandomSeed);
			var terrain = world.Map.AllCells.Select(c => world.Map.Tiles[c]).ToArray();
			void Tick() { world.Tick(); manager.LocalFrameNumber++; }
			Tick();
			Require(controller.Applied, "Ownership setup did not run on the first tick.");
			if (forecast != null)
				foreach (var name in info.ColonyActorNames)
					Require(spawned[name].Owner.InternalName == (forecast.ColonyOwners.TryGetValue(name, out var slot) ? slot : "Creeps"), "Preview ownership disagrees with actual runtime owner.");
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
			var result = new JObject { ["tileset"] = world.Map.Tileset, ["ownership_mode"] = RmgColonyOwnership.ModeName(info.ChoiceMode), ["preview_matches_runtime"] = true, ["ai_opponents"] = bots, ["pool"] = colonies.Length, ["counts"] = new JArray(controller.AssignedCounts),
				["starts"] = new JArray(starts.Select(p => $"{p.X},{p.Y}")),
				["owners"] = new JArray(colonies.Select(a => a.Owner.InternalName)) };
			var captured = colonies.FirstOrDefault(a => !a.Owner.NonCombatant);
			if (captured != null)
			{
				captured.ChangeOwner(world.Players.First(p => p.InternalName == "Neutral"));
				Tick(); Tick();
				Require(captured.Owner.InternalName == "Neutral", "Setup ownership reapplied after capture.");
			}
			if (bots) for (var i = 0; i < 600; i++) Tick();
			if (hostiles)
			{
				for (var i = 0; i < 1600; i++) Tick();
				var theme = Array.IndexOf(RmgBiome.Tilesets, world.Map.Tileset);
				var expectedPlant = new[] { "popcorn", "thorn", "puff", "freckle" } [theme];
				var expectedFlier = new[] { "dragonfly", "fly", "moth", "flying_machine" } [theme];
				Require(firstPlant == expectedPlant && firstFlier == expectedFlier, $"{world.Map.Tileset} default hostiles mismatch: {firstPlant}/{firstFlier}.");
				var plantWeights = HostileOptions.Weights(manager.LobbyInfo.GlobalSettings, "plant", world.Map.Tileset);
				Require(plantWeights.Count(w => w > 0) == 1 && plantWeights[Array.IndexOf(HostileOptions.Plants, expectedPlant)] == 100, "Default biome plant weights changed.");
				Set("h-plant-0", "77");
				Set("h-flier-0", "33");
				Require(RmgBiome.Tilesets.All(t => HostileOptions.Weights(manager.LobbyInfo.GlobalSettings, "plant", t)[0] == 77 && HostileOptions.Weights(manager.LobbyInfo.GlobalSettings, "flier", t)[0] == 33), "Changing biome discarded explicit hostile weights.");
				result["default_plant"] = firstPlant; result["default_flier"] = firstFlier;
				Require(terrain.SequenceEqual(world.Map.AllCells.Select(c => world.Map.Tiles[c])), "Hostiles changed generated terrain.");
			}

			if (world.Map.Bounds.Width == 512 && screenshot != null)
				CheckLargeWorldRendering(renderer, screenshot);

			Ui.ResetAll();
			return result;
		}

		static void CheckLargeWorldRendering(WorldRenderer renderer, string path)
		{
			foreach (var (name, x, y) in new[] { ("nw", 8, 8), ("ne", 503, 8), ("sw", 8, 503), ("se", 503, 503), ("center", 256, 256) })
			{
				renderer.Viewport.Center(renderer.World.Map.CenterOfCell(new CPos(x + 2, y + 2)));
				for (var frame = 0; frame < 3; frame++)
				{
					renderer.PrepareRenderables();
					Game.Renderer.BeginWorld(renderer.Viewport.Rectangle);
					renderer.Draw();
					Game.Renderer.BeginUI();
					Game.Renderer.EndFrame(new IgnoreInput());
				}

				var file = path + "-world-" + name + ".png";
				Game.Renderer.SaveScreenshot(file);
				for (var i = 0; i < 100 && !File.Exists(file); i++) Thread.Sleep(20);
				Require(File.Exists(file), "512 world screenshot was not saved.");
			}
		}

		static void CheckPreview(MapPreview map, OrderManager manager, ColonyPreviewSite[] sites, string path)
		{
			var original = manager.LobbyInfo.Serialize();
			var view = new ColonyMapPreviewWidget { Preview = () => map };
			view.Initialize(new WidgetArgs { { "orderManager", manager } });
			view.Bounds = new Rectangle(20, 20, 540, 540);
			Ui.Root.AddChild(view);
			var timer = Stopwatch.StartNew();
			while (map.GetMinimap() == null && timer.ElapsedMilliseconds < 5000) { Game.PerformDelayedActions(); Thread.Sleep(20); }
			Draw(Path.GetDirectoryName(path), Path.GetFileName(path) + "-ownership-preview");
			Require(view.Loaded && view.RenderBounds.Width == 540 && view.Ownership != null, "Ownership preview did not render at its intended size.");
			var first = view.Ownership;
			var owner = first.ColonyOwners.FirstOrDefault();
			if (owner.Key != null)
			{
				var client = manager.LobbyInfo.ClientInSlot(owner.Value);
				var oldColor = client.Color;
				client.Color = Color.White;
				Draw(Path.GetDirectoryName(path), Path.GetFileName(path) + "-recolored-preview");
				Require(view.Ownership.ColonyColors[owner.Key] == Color.White, "Preview failed to refresh changed player color.");
				client.Color = oldColor;
			}
			var active = manager.LobbyInfo.Clients.Where(c => c.Slot != null).ToArray();
			var originalSpawns = active.Select(c => c.SpawnPoint).ToArray();
			for (var i = 0; i < active.Length; i++) active[i].SpawnPoint = map.SpawnPoints.Length - i;
			Draw(Path.GetDirectoryName(path), Path.GetFileName(path) + "-changed-spawns-preview");
			Require(view.Ownership != null && active.All(c => view.Ownership.SpawnOccupants.TryGetValue(c.SpawnPoint, out var occupant) && occupant.PlayerName == c.Name),
				"Preview did not refresh changed spawn assignments.");
			if (map.WorldActorInfo.TraitInfo<RmgStartingColonyOwnershipInfo>().ChoiceMode == RmgColonyOwnershipMode.Random)
				Require(first.ColonyOwners.Count == view.Ownership.ColonyOwners.Count && first.ColonyOwners.All(pair => view.Ownership.ColonyOwners.TryGetValue(pair.Key, out var slot) && slot == pair.Value),
					"Random ownership changed when starting positions changed.");
			for (var i = 0; i < active.Length; i++) active[i].SpawnPoint = originalSpawns[i];
			Require(manager.LobbyInfo.Serialize() == original, "Preview mutated lobby settings or random state.");
			Ui.ResetAll();
		}

		static void CheckAcknowledgement(Utility utility, MapPreview map, OrderManager manager)
		{
			typeof(Game).GetField("OrderManager", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, manager);
			var previousState = manager.LocalClient.State;
			manager.LocalClient.State = Session.ClientState.Invalid;
			using var source = utility.ModData.DefaultFileSystem.Open("sa|chrome/lobby.yaml");
			var node = MiniYaml.FromStream(source).Single(n => n.Key == "Background@SERVER_LOBBY").Clone();
			node.Value.Nodes.RemoveAll(n => n.Key == "Logic");
			node.Value.Nodes.Add(new MiniYamlNode("Logic", "RmgLobbyLogic"));
			var lobby = utility.ModData.WidgetLoader.LoadWidget(new WidgetArgs { { "orderManager", manager }, { "skirmishMode", true } }, Ui.Root, node);
			var orders = (List<Order>)typeof(OrderManager).GetField("localImmediateOrders", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
			var before = orders.Count;
			Ui.Tick(); Ui.Tick();
			Require(lobby.Get<ButtonWidget>("START_GAME_BUTTON").IsDisabled(), "Invalid host can press Start Game.");
			Require(orders.Skip(before).Count(o => o.TargetString == "state NotReady") == 1, "Missing or repeated map acknowledgement.");
			manager.LocalClient.State = Session.ClientState.NotReady;
			Ui.Tick();
			Require(!lobby.Get<ButtonWidget>("START_GAME_BUTTON").IsDisabled(), "Confirmed host remains blocked.");
			manager.LocalClient.State = previousState;
			orders.Clear();
			Ui.ResetAll();
		}

		static void CheckServer(Utility utility, MapPreview map)
		{
			var server = new OpenRA.Server.Server(new List<IPEndPoint> { new(IPAddress.Loopback, 0) },
				new ServerSettings { Name = "RMG isolated regression", Map = map.Uid, AdvertiseOnline = false, RecordReplays = false, QueryMapRepository = false },
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
				Wait(() => server.Conns.Count == 1, "Local test connection failed.");
				var handshake = new HandshakeResponse { Mod = utility.ModData.Manifest.Id, Version = utility.ModData.Manifest.Metadata.Version,
					OrdersProtocol = ProtocolVersion.Orders, Client = new Session.Client { Name = "RMG regression", Faction = "Random",
						Color = Color.Red, PreferredColor = Color.Red } };
				connection.SendImmediate(new[] { new Order("HandshakeResponse", null, false) { Type = OrderType.Handshake,
					IsImmediate = true, TargetString = handshake.Serialize() } });
				Wait(() => server.Conns[0].Validated, "Local test handshake failed.");
				void Send(params string[] commands) => connection.SendImmediate(commands.Select(Order.Command));
				Send("state NotReady");
				Wait(() => server.LobbyInfo.Clients[0].State == Session.ClientState.NotReady, "Map acknowledgement failed.");
				for (var i = 1; i < map.PlayerCount; i++) Send($"slot_bot Multi{i} 0 easy");
				Wait(() => server.LobbyInfo.Clients.Count == map.PlayerCount, "AI lobby setup failed.");
				// Reproduce the old bug at its actual source, without invoking its fatal StartGame call.
				lock (server.LobbyInfo)
				{
					new OpenRA.Mods.Common.Server.LobbyCommands().InterpretCommand(server, server.Conns[0], server.LobbyInfo.Clients[0], "map " + map.Uid);
					Require(server.LobbyInfo.Clients[0].IsInvalid, "Old repeated-map invalidation was not reproduced.");
				}
				Send("startgame");
				// Barrier confirms the rejected start was processed before checking server state.
				Send("state NotReady");
				Wait(() => server.LobbyInfo.Clients[0].State == Session.ClientState.NotReady, "Start guard disconnected the host.");
				Require(server.State == ServerState.WaitingPlayers && server.Conns.Count == 1, "Invalid-host start was not blocked safely.");
				for (var i = 0; i < 3; i++) Send("map " + map.Uid);
				Send("state Ready");
				// Ready may auto-start with all bots ready; explicit start covers the manual path otherwise.
				Send("startgame");
				Wait(() => server.State == ServerState.GameStarted, "Repeated map selection still prevents real server startup.");
				Require(server.Conns.Count == 1 && server.Conns[0].Validated, "Server startup dropped the human host.");
				Console.WriteLine("PASS: actual loopback server handshake, three AI opponents, reproduced old same-UID invalidation, rejected invalid-host start, repeated selection and successful Server.StartGame.");
			}
			finally { server.Shutdown(); }
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
			var terrain = lobby.Get<DropDownButtonWidget>("RMG_TERRAIN");
			foreach (var label in new[] { "Desert", "Swamp", "Candy", "Normal" })
			{
				terrain.OnMouseDown(default);
				var options = Ui.Root.Children.OfType<ScrollPanelWidget>().Last();
				Require(options.Children.OfType<ScrollItemWidget>().Count() == 4, "Missing playable RMG tileset choices.");
				options.Children.OfType<ScrollItemWidget>().Single(r => r.Get<LabelWidget>("LABEL").GetText() == label).OnClick();
				terrain.RemovePanel();
				Require(terrain.GetText() == label && !lobby.Get<ButtonWidget>("RMG_GENERATE_BUTTON").IsDisabled(), "Terrain choice remains a disabled placeholder.");
				if (Game.Renderer.Resolution.Width >= 1182 && label != "Normal") Draw(output, "rmg-" + label);
			}

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
			Size("512");
			Require(lobby.Get<DropDownButtonWidget>("RMG_SIZE").GetText() == "512 x 512" && slider.MaximumValue == 8 && slider.GetValue() == 8 &&
				!lobby.Get<ButtonWidget>("RMG_GENERATE_BUTTON").IsDisabled(), "512 size did not activate with eight players.");
			if (Game.Renderer.Resolution.Width >= 1182) Draw(output, "rmg-512-eight-players");
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
			// Change only the mode: it must commit even with unchanged shares.
			ChooseMode(dialog, "Random");
			dialog.Get<ButtonWidget>("APPLY").OnClick();
			lobby.Get<ButtonWidget>("RMG_COLONY_OWNERSHIP").OnClick();
			dialog = Ui.CurrentWindow();
			Require(dialog.Get<DropDownButtonWidget>("OWNERSHIP_MODE").GetText() == "Random", "Lobby mode-only Apply was not retained.");
			ChooseMode(dialog, "Closest to Spawn");
			dialog.Get<ButtonWidget>("CANCEL").OnClick();
			Size("256"); slider.UpdateValue(8);
			Require(slider.MaximumValue == 8 && slider.GetValue() == 8, "Large-map range did not restore.");
			lobby.Get<ButtonWidget>("RMG_COLONY_OWNERSHIP").OnClick();
			dialog = Ui.CurrentWindow();
			Require(dialog.Get<ScrollPanelWidget>("SETTINGS").Children.Count == 8, "Large map opened wrong number of ownership sliders.");
			Require(dialog.Get<DropDownButtonWidget>("OWNERSHIP_MODE").GetText() == "Random", "Cancel or player-count change lost applied mode.");
			dialog.Get<ButtonWidget>("CANCEL").OnClick();
			lobby.Get<ButtonWidget>("RMG_COLONY_WEIGHTS").OnClick();
			Require(Ui.CurrentWindow().Id == "RMG_COLONY_WEIGHTS_PANEL", "Species launcher opened wrong window.");
			Ui.CurrentWindow().Get<ButtonWidget>("CANCEL").OnClick();
			Ui.ResetAll();
			Console.WriteLine("PASS: actual RMG launchers, default range, 8 -> 4 small-map cap, 4 -> 8 larger range, ownership row counts and Apply persistence.");
		}

		static void ChooseMode(Widget widget, string label)
		{
			var button = widget.Get<DropDownButtonWidget>("OWNERSHIP_MODE");
			button.OnMouseDown(default);
			var panel = Ui.Root.Children.OfType<ScrollPanelWidget>().Last();
			Require(panel.Children.OfType<ScrollItemWidget>().Count() == 2, "Wrong ownership mode choices.");
			panel.Children.OfType<ScrollItemWidget>().Single(r => r.Get<LabelWidget>("LABEL").GetText() == label).OnClick();
			button.RemovePanel();
			Require(button.GetText() == label, "Ownership mode did not change.");
		}

		static void CheckWidgets(string output)
		{
			foreach (var count in new[] { 1, 3, 8 })
			{
				int[] applied = null;
				RmgColonyOwnershipMode? appliedMode = null;
				var disabled = false;
				var original = new int[count];
				Widget Open() => Ui.OpenWindow("RMG_COLONY_OWNERSHIP_PANEL", new WidgetArgs {
					{ "initialShares", original }, { "initialMode", RmgColonyOwnershipMode.ClosestToSpawn }, { "configurationDisabled", (Func<bool>)(() => disabled) },
					{ "onApply", (Action<int[], RmgColonyOwnershipMode>)((v, mode) => { applied = v; appliedMode = mode; }) } });
				var widget = Open();
				Require(widget.Get<DropDownButtonWidget>("OWNERSHIP_MODE").GetText() == "Closest to Spawn", "Ownership mode default changed.");
				ChooseMode(widget, "Random");
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
				Require(widget.Get<DropDownButtonWidget>("OWNERSHIP_MODE").IsDisabled(), "Non-host can change ownership mode.");
				widget.Get<ButtonWidget>("APPLY").OnClick();
				Require(applied == null, "Non-host applied ownership.");
				disabled = false;
				widget.Get<ButtonWidget>("CANCEL").OnClick();
				Require(applied == null && original.All(v => v == 0), "Cancel mutated ownership.");
				widget = Open();
				Require(widget.Get<DropDownButtonWidget>("OWNERSHIP_MODE").GetText() == "Closest to Spawn" && appliedMode == null, "Cancel retained draft ownership mode.");
				ChooseMode(widget, "Random");
				field = widget.Get<ScrollPanelWidget>("SETTINGS").Children.First().Get<TextFieldWidget>("VALUE");
				field.Text = "100"; field.OnTextEdited();
				widget.Get<ButtonWidget>("RESET").OnClick();
				Require(field.Text == "0", "Reset did not clear shares.");
				field.Text = "25"; field.OnTextEdited();
				widget.Get<ButtonWidget>("APPLY").OnClick();
				Require(applied[0] == 25 && original[0] == 0 && appliedMode == RmgColonyOwnershipMode.Random, "Apply failed or mutated original.");
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
