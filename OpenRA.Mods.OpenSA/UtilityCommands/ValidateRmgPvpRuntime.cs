#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OpenRA.FileSystem;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Mods.OpenSA.Rmg;
using OpenRA.Mods.OpenSA.Rmg.Reassessment;
using OpenRA.Mods.OpenSA.Widgets.Logic;
using OpenRA.Network;
using OpenRA.Widgets;

namespace OpenRA.Mods.OpenSA.UtilityCommands
{
	public sealed partial class ValidateRmgOwnershipRuntimeCommand
	{
		static void CheckPvpWidgets(Utility utility, string output)
		{
			// The accepted complexity scaling must survive mirroring unchanged. Compare
			// the actual priority fields to the ordinary Natural Landscape construction.
			foreach (var level in Enum.GetValues<TerrainComplexity>())
			{
				var baseline = new TerrainComparisonSettings(0, 128, TerrainConstruction.Regions, level)
				{ Continuity = true, ExtendedComplexity = true };
				var natural = TerrainComparison.Generate(utility.ModData, baseline);
				foreach (var axes in new[] { 1, 2, 4 })
				{
					var mirrored = TerrainComparison.Generate(utility.ModData, baseline with { MirroringAxes = axes });
					foreach (var pair in new[] { (natural.WaterPriority, mirrored.WaterPriority, 64), (natural.GeologyPriority, mirrored.GeologyPriority, 65) })
						for (var i = 0; i < pair.Item1.Length; i++)
							Require(pair.Item2[i] == pair.Item1[RmgMirroring.Canonical(i, pair.Item3, axes, 0)], "PvP changed the accepted Natural Landscape priority field.");
				}
			}

			Console.WriteLine("PASS: all five complexity levels use the unchanged Natural Landscape terrain priority fields under all three mirror settings.");
			var valid = 0;
			foreach (var size in new[] { 64, 128, 256, 512 })
				foreach (var axes in new[] { 1, 2, 4 })
					for (var players = 1; players <= 8; players++)
					{
						var expected = players <= (size == 64 ? 4 : 8) && players % RmgMirroring.GroupSize(axes) == 0;
						var requested = new RmgPlayerSettings
						{
							SchemaVersion = 11, MapSize = size, PlayerCount = players, MirroringAxes = axes,
							LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscapePvp, Tileset = "CANDY"
						};
						var accepted = false;
						try
						{
							var settings = RmgPlayerSettingsContract.Resolve(RmgPlayerSettingsContract.Parse(requested.ToJson())).Normalized;
							accepted = settings.GeneratorVersion == 17 && settings.MirroringAxes == axes && settings.StartingColonyShares.Length == players;
						}
						catch (ArgumentException) { }
						Require(accepted == expected, $"PvP settings round trip {size}/{players}/{axes} disagrees with allowed player groups.");
						if (accepted) valid++;
					}

			foreach (var axes in new[] { -1, 0, 3, 5, 8 })
			{
				var rejected = false;
				try { RmgMirroring.Validate(axes, 256, 8); } catch (ArgumentException) { rejected = true; }
				Require(rejected, "Invalid mirroring axis count accepted.");
			}

			foreach (var invalid in new[]
			{
				new RmgPlayerSettings { SchemaVersion = 10, LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscapePvp, MirroringAxes = 1 },
				new RmgPlayerSettings { SchemaVersion = 11, LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscape, MirroringAxes = 1 },
				new RmgPlayerSettings { SchemaVersion = 11, LayoutFamily = RmgPlayerLayoutFamily.NaturalLandscapePvp }
			})
			{
				var rejected = false;
				try { RmgPlayerSettingsContract.Resolve(invalid); } catch (ArgumentException) { rejected = true; }
				Require(rejected, "Invalid PvP schema/family/axes combination accepted.");
			}

			foreach (var invalid in new[] { ("schema_version", "11"), ("layout_family", "natural-landscape"), ("block_shape", "circles"), ("lane_width", "ultra"), ("mirroring_axes", "1") })
			{
				var requested = new RmgPlayerSettings { SchemaVersion = 12, LayoutFamily = RmgPlayerLayoutFamily.ArtificialBattlefield }.ToJson();
				requested[invalid.Item1] = invalid.Item1 is "schema_version" or "mirroring_axes" ? new Newtonsoft.Json.Linq.JValue(int.Parse(invalid.Item2)) : new Newtonsoft.Json.Linq.JValue(invalid.Item2);
				var rejected = false;
				try { RmgPlayerSettingsContract.Resolve(RmgPlayerSettingsContract.Parse(requested)); } catch (ArgumentException) { rejected = true; }
				Require(rejected, "Invalid Battlefield field/schema accepted: " + invalid.Item1);
			}

			var manager = new OrderManager(new EchoConnection());
			manager.LobbyInfo.Clients.Add(new Session.Client { Index = manager.Connection.LocalClientId, IsAdmin = true, State = Session.ClientState.NotReady });
			typeof(Game).GetField("OrderManager", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, manager);
			using var source = utility.ModData.DefaultFileSystem.Open("sa|chrome/lobby.yaml");
			var node = MiniYaml.FromStream(source).Single(n => n.Key == "Background@SERVER_LOBBY").Clone();
			node.Value.Nodes.RemoveAll(n => n.Key == "Logic"); node.Value.Nodes.Add(new MiniYamlNode("Logic", "RmgLobbyLogic"));
			var lobby = utility.ModData.WidgetLoader.LoadWidget(new WidgetArgs { { "orderManager", manager }, { "skirmishMode", true } }, Ui.Root, node);
			var logic = lobby.LogicObjects.OfType<RmgLobbyLogic>().Single();
			RmgGenerationSettings Settings() => RmgPlayerSettingsContract.Resolve((RmgPlayerSettings)typeof(RmgLobbyLogic)
				.GetMethod("CreatePlayerSettings", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(logic, new object[] { 0UL })).Normalized;
			lobby.Get<ButtonWidget>("RMG_TOGGLE_BUTTON").OnClick();
			Require(Settings().GeneratorVersion == 16 && !lobby.Get("RMG_MIRRORING_AXES").IsVisible(), "Natural baseline/default changed.");
			void Choose(string id, string label)
			{
				var button = lobby.Get<DropDownButtonWidget>(id); button.OnMouseDown(default);
				var rows = Ui.Root.Children.OfType<ScrollPanelWidget>().Last().Children.OfType<ScrollItemWidget>().ToArray();
				if (id == "RMG_LAYOUT_FAMILY") Require(rows.All(row => row.Get<LabelWidget>("LABEL").GetText() != "Structured Competitive"), "Retired family still appears in the menu.");
				rows.Single(row => row.Get<LabelWidget>("LABEL").GetText() == label).OnClick(); button.RemovePanel();
			}

			Choose("RMG_LAYOUT_FAMILY", "Natural Landscape PVP");
			Choose("RMG_PRESET", "Open Conflict");
			Require(Settings().GeneratorVersion == 17 && Settings().MirroringAxes == 1, "Changing quantities through a preset exited PVP.");
			Choose("RMG_PRESET", "Balanced");
			var slider = lobby.Get<SliderWidget>("RMG_PLAYERS");
			Require(Settings().GeneratorVersion == 17 && Settings().MirroringAxes == 1 && lobby.Get("RMG_MIRRORING_AXES").IsVisible(), "PvP family did not activate.");
			slider.UpdateValue(6); Require(Settings().PlayerCount == 6, "One-axis six-player setup unavailable.");
			Choose("RMG_TERRAIN", "Desert"); Choose("RMG_WATER_AMOUNT", "Ultra"); Choose("RMG_GRAVEL_MOSS_AMOUNT", "Extreme");
			Require(Settings().Tileset == "DESERT" && Settings().WaterAmount == RmgParameterLevel.Ultra && Settings().TacticalTerrain == RmgParameterLevel.Extreme, "PvP dropped modern controls.");
			Choose("RMG_MIRRORING_AXES", "2 axes (4 quarters)");
			Require(slider.GetValue() == 8, "Two-axis player snapping failed.");
			slider.UpdateValue(4);
			Choose("RMG_MIRRORING_AXES", "4 axes (8 sectors)");
			Require(Settings().PlayerCount == 8 && slider.MinimumValue == 8, "Four axes did not require eight players.");
			Choose("RMG_SIZE", "512 x 512");
			Require(Settings().MapSize == 512 && !lobby.Get<ButtonWidget>("RMG_GENERATE_BUTTON").IsDisabled(), "512 PvP generation disabled.");
			Draw(output, "pvp-512-four-axes");
			lobby.Get<ButtonWidget>("RMG_COLONY_OWNERSHIP").OnClick();
			Require(Ui.CurrentWindow().Get<ScrollPanelWidget>("SETTINGS").Children.Count == 8, "Ownership sliders disagree with mirrored player count.");
			Ui.CurrentWindow().Get<ButtonWidget>("CANCEL").OnClick();
			lobby.Get<ButtonWidget>("RMG_COLONY_WEIGHTS").OnClick(); Ui.CurrentWindow().Get<ButtonWidget>("CANCEL").OnClick();
			Choose("RMG_SIZE", "64 x 64");
			Require(Settings().PlayerCount == 4 && Settings().MirroringAxes == 2 && slider.MaximumValue == 4, "Small-map symmetry cap failed.");
			Draw(output, "pvp-64-two-axes");
			Choose("RMG_LAYOUT_FAMILY", "Natural Landscape (Regions)");
			slider.UpdateValue(1);
			Require(Settings().GeneratorVersion == 16 && Settings().MirroringAxes == 0 && Settings().PlayerCount == 1 && !lobby.Get("RMG_MIRRORING_AXES").IsVisible(), "Returning to Natural changed the baseline contract.");
			Choose("RMG_LAYOUT_FAMILY", "Artificial Battlefield");
			Require(Settings().GeneratorVersion == 18 && Settings().PlayerCount == 2, "Artificial Battlefield did not select the new generator.");
			Choose("RMG_SIZE", "512 x 512");
			slider.UpdateValue(1); Require(Settings().PlayerCount == 4, "Battlefield slider middle position is not four players.");
			slider.UpdateValue(2); Require(Settings().PlayerCount == 8, "Battlefield slider maximum is not eight players.");
			Choose("RMG_BLOCK_SHAPE", "Diamonds"); Choose("RMG_LANE_WIDTH", "Wide");
			Choose("RMG_TERRAIN", "Candy"); Choose("RMG_TERRAIN_COMPLEXITY", "Ultra");
			Require(Settings().BlockShape == RmgBattlefieldBlockShape.Diamonds && Settings().LaneWidth == RmgBattlefieldLaneWidth.Wide && Settings().Tileset == "CANDY", "Battlefield controls do not reach generation settings.");
			Require(!lobby.Get("RMG_MIRRORING_AXES").IsVisible() && lobby.Get("RMG_BLOCK_SHAPE").IsVisible(), "Wrong family controls visible.");
			Draw(output, "battlefield-512-options");
			Choose("RMG_PRESET", "Balanced"); Require(Settings().GeneratorVersion == 18, "Preset selection exited Battlefield.");
			Choose("RMG_SIZE", "64 x 64"); Require(Settings().PlayerCount == 4 && slider.MaximumValue == 1, "Battlefield small-map cap failed.");
			lobby.Get<ButtonWidget>("RMG_COLONY_OWNERSHIP").OnClick();
			Require(Ui.CurrentWindow().Get<ScrollPanelWidget>("SETTINGS").Children.Count == 4, "Battlefield ownership rows disagree with the player count.");
			Ui.CurrentWindow().Get<ButtonWidget>("CANCEL").OnClick();
			Draw(output, "battlefield-64-options");
			Choose("RMG_LAYOUT_FAMILY", "Natural Landscape (Regions)");
			Require(Settings().GeneratorVersion == 16 && !lobby.Get("RMG_BLOCK_SHAPE").IsVisible(), "Leaving Battlefield changed the Natural baseline.");
			Choose("RMG_LAYOUT_FAMILY", "Crossroads");
			Require(Settings().GeneratorVersion == 19 && lobby.Get("RMG_APPROACH_WIDTH").IsVisible() && !lobby.Get("RMG_BLOCK_SHAPE").IsVisible(), "Crossroads family controls did not activate.");
			Choose("RMG_APPROACH_WIDTH", "Narrow"); Choose("RMG_SIDE_CONNECTIONS", "Two tiers");
			Choose("RMG_SIZE", "512 x 512"); slider.UpdateValue(2);
			Require(Settings().PlayerCount == 8 && Settings().LaneWidth == RmgBattlefieldLaneWidth.Narrow && Settings().SideConnections == RmgCrossroadsConnections.Many, "Crossroads UI choices did not reach generation settings.");
			Draw(output, "crossroads-512-options");
			Choose("RMG_PRESET", "Balanced"); Require(Settings().GeneratorVersion == 19, "Preset selection exited Crossroads.");
			lobby.Get<ButtonWidget>("RMG_COLONY_OWNERSHIP").OnClick();
			Require(Ui.CurrentWindow().Get<ScrollPanelWidget>("SETTINGS").Children.Count == 8, "Crossroads ownership rows disagree with players.");
			Ui.CurrentWindow().Get<ButtonWidget>("CANCEL").OnClick();
			Choose("RMG_SIZE", "64 x 64"); Require(Settings().PlayerCount == 4 && slider.MaximumValue == 1, "Crossroads small-map cap failed.");
			Draw(output, "crossroads-64-options");
			Choose("RMG_LAYOUT_FAMILY", "Artificial Battlefield");
			Require(Settings().GeneratorVersion == 18 && Settings().BlockShape == RmgBattlefieldBlockShape.Diamonds && Settings().LaneWidth == RmgBattlefieldLaneWidth.Wide, "Crossroads changed stored Battlefield geometry choices.");
			Choose("RMG_LAYOUT_FAMILY", "Natural Landscape (Regions)");
			Require(Settings().GeneratorVersion == 16 && !lobby.Get("RMG_APPROACH_WIDTH").IsVisible(), "Leaving Crossroads changed the Natural baseline.");
			CheckCrossroadsContract();
			Choose("RMG_LAYOUT_FAMILY", "Ring");
			Require(Settings().GeneratorVersion == 20 && lobby.Get("RMG_RING_SHAPE").IsVisible() && !lobby.Get("RMG_SIDE_CONNECTIONS").IsVisible(), "Ring controls did not activate.");
			Choose("RMG_RING_SHAPE", "Square"); Choose("RMG_RING_WIDTH", "Wide");
			Choose("RMG_SIZE", "512 x 512"); slider.UpdateValue(2);
			Require(Settings().PlayerCount == 8 && Settings().RingShape == RmgRingShape.Square && Settings().LaneWidth == RmgBattlefieldLaneWidth.Wide, "Ring UI choices did not reach settings.");
			Draw(output, "ring-512-options");
			Choose("RMG_PRESET", "Balanced"); Require(Settings().GeneratorVersion == 20, "Preset selection exited Ring.");
			lobby.Get<ButtonWidget>("RMG_COLONY_OWNERSHIP").OnClick();
			Require(Ui.CurrentWindow().Get<ScrollPanelWidget>("SETTINGS").Children.Count == 8, "Ring ownership rows disagree with players.");
			Ui.CurrentWindow().Get<ButtonWidget>("CANCEL").OnClick();
			Choose("RMG_SIZE", "64 x 64"); Require(Settings().PlayerCount == 4 && slider.MaximumValue == 1, "Ring small-map cap failed.");
			Draw(output, "ring-64-options");
			Choose("RMG_LAYOUT_FAMILY", "Crossroads");
			Require(Settings().SideConnections == RmgCrossroadsConnections.Many && Settings().LaneWidth == RmgBattlefieldLaneWidth.Narrow, "Ring changed stored Crossroads settings.");
			Choose("RMG_LAYOUT_FAMILY", "Ring");
			Require(Settings().RingShape == RmgRingShape.Square && Settings().LaneWidth == RmgBattlefieldLaneWidth.Wide, "Ring choices were lost after switching families.");
			Choose("RMG_LAYOUT_FAMILY", "Natural Landscape (Regions)");
			Require(Settings().GeneratorVersion == 16 && !lobby.Get("RMG_RING_SHAPE").IsVisible(), "Leaving Ring changed the Natural baseline.");
			CheckRingContract();

			Choose("RMG_LAYOUT_FAMILY", "Divided Lands");
			Require(Settings().GeneratorVersion == 21 && lobby.Get("RMG_LAND_CROSSINGS").IsVisible() && !lobby.Get("RMG_SIDE_CONNECTIONS").IsVisible(), "Divided Lands controls did not activate.");
			Choose("RMG_LAND_CROSSINGS", "Two per border"); Choose("RMG_CROSSING_WIDTH", "Wide");
			Choose("RMG_SIZE", "512 x 512"); slider.UpdateValue(2);
			Require(Settings().PlayerCount == 8 && Settings().LandCrossings == RmgLandCrossings.Two && Settings().LaneWidth == RmgBattlefieldLaneWidth.Wide, "Divided Lands UI choices did not reach settings.");
			Draw(output, "divided-512-options");
			Choose("RMG_PRESET", "Balanced"); Require(Settings().GeneratorVersion == 21, "Preset selection exited Divided Lands.");
			lobby.Get<ButtonWidget>("RMG_COLONY_OWNERSHIP").OnClick();
			Require(Ui.CurrentWindow().Get<ScrollPanelWidget>("SETTINGS").Children.Count == 8, "Divided Lands ownership rows disagree with players.");
			Ui.CurrentWindow().Get<ButtonWidget>("CANCEL").OnClick();
			Choose("RMG_SIZE", "64 x 64"); Require(Settings().PlayerCount == 4 && slider.MaximumValue == 1, "Divided Lands small-map cap failed.");
			Draw(output, "divided-64-options");
			Choose("RMG_LAYOUT_FAMILY", "Crossroads");
			Require(Settings().SideConnections == RmgCrossroadsConnections.Many && Settings().LaneWidth == RmgBattlefieldLaneWidth.Narrow, "Divided Lands changed stored Crossroads settings.");
			Choose("RMG_LAYOUT_FAMILY", "Divided Lands");
			Require(Settings().LandCrossings == RmgLandCrossings.Two && Settings().LaneWidth == RmgBattlefieldLaneWidth.Wide, "Divided Lands choices were lost after switching families.");
			Choose("RMG_LAYOUT_FAMILY", "Natural Landscape (Regions)");
			Require(Settings().GeneratorVersion == 16 && !lobby.Get("RMG_LAND_CROSSINGS").IsVisible(), "Leaving Divided Lands changed the Natural baseline.");
			CheckDividedLandsContract();
			Choose("RMG_LAYOUT_FAMILY", "Strongholds");
			Require(Settings().GeneratorVersion == 22 && Settings().MirroringAxes == 0 && Settings().GenerateCastles && lobby.Get("RMG_GENERATE_CASTLES").IsVisible(), "Strongholds did not activate asymmetric generation and default castles.");
			Require(!lobby.Get("RMG_MIRRORING_AXES").IsVisible() && !lobby.Get("RMG_LAND_CROSSINGS").IsVisible(), "Symmetric controls leaked into Strongholds.");
			lobby.Get<CheckboxWidget>("RMG_GENERATE_CASTLES").OnClick();
			Choose("RMG_SIZE", "512 x 512");
			foreach (var count in new[] { 1, 3, 5, 7, 8 }) { slider.UpdateValue(count); Require(Settings().PlayerCount == count, "Strongholds lost an asymmetric player count."); }
			Choose("RMG_PRESET", "Balanced");
			Require(Settings().GeneratorVersion == 22 && !Settings().GenerateCastles, "Preset changed Strongholds or castles.");
			Draw(output, "strongholds-512-options");
			lobby.Get<ButtonWidget>("RMG_COLONY_OWNERSHIP").OnClick();
			Require(Ui.CurrentWindow().Get<ScrollPanelWidget>("SETTINGS").Children.Count == 8, "Strongholds ownership rows differ from players.");
			Ui.CurrentWindow().Get<ButtonWidget>("CANCEL").OnClick();
			Choose("RMG_SIZE", "64 x 64");
			Require(Settings().PlayerCount == 4 && slider.MaximumValue == 4, "Strongholds small-map cap failed.");
			Choose("RMG_LAYOUT_FAMILY", "Natural Landscape (Regions)");
			Require(Settings().GeneratorVersion == 16 && !lobby.Get("RMG_GENERATE_CASTLES").IsVisible(), "Strongholds changed Natural baseline.");
			Choose("RMG_LAYOUT_FAMILY", "Strongholds");
			Require(!Settings().GenerateCastles, "Stored castle selection lost.");
			lobby.Get<CheckboxWidget>("RMG_GENERATE_CASTLES").OnClick();
			Draw(output, "strongholds-64-options");
			CheckStrongholdsContract();
			var battlefieldValid = 0;
			foreach (var size in new[] { 64, 128, 256, 512 })
				for (var players = 1; players <= 8; players++)
					foreach (var shape in Enum.GetValues<RmgBattlefieldBlockShape>())
						foreach (var lane in Enum.GetValues<RmgBattlefieldLaneWidth>())
						{
							var expected = players is 2 or 4 or 8 && players <= (size == 64 ? 4 : 8);
							var accepted = false;
							try
							{
								var requested = new RmgPlayerSettings
								{
									SchemaVersion = 12, LayoutFamily = RmgPlayerLayoutFamily.ArtificialBattlefield, MapSize = size,
									PlayerCount = players, BlockShape = shape, LaneWidth = lane, Tileset = "SWAMP"
								};
								var parsed = RmgPlayerSettingsContract.Resolve(RmgPlayerSettingsContract.Parse(requested.ToJson())).Normalized;
								accepted = parsed.GeneratorVersion == 18 && parsed.BlockShape == shape && parsed.LaneWidth == lane && parsed.MirroringAxes == RmgBattlefieldParameters.Axes(players);
							}
							catch (ArgumentException) { }
							Require(accepted == expected, $"Battlefield contract disagrees at {size}/{players}/{shape}/{lane}.");
							if (accepted) battlefieldValid++;
						}

			Console.WriteLine($"PASS: 288 Battlefield size/player/shape/lane settings ({battlefieldValid} valid), actual Battlefield UI, all modern controls and baseline switching.");
			Ui.ResetAll();
			Console.WriteLine($"PASS: 96 size/player/axes combinations ({valid} allowed), invalid axes, actual PvP UI settings, size limits, ownership and baseline switching.");
		}

		static void CheckStrongholdsContract()
		{
			var valid = 0;
			foreach (var size in new[] { 64, 128, 256, 512 })
				for (var players = 1; players <= 8; players++)
					foreach (var castles in new[] { false, true })
					{
						var accepted = false;
						try
						{
							var request = new RmgPlayerSettings { SchemaVersion = 16, LayoutFamily = RmgPlayerLayoutFamily.Strongholds, MapSize = size, PlayerCount = players, GenerateCastles = castles, Tileset = "CANDY" };
							var normalized = RmgPlayerSettingsContract.Resolve(RmgPlayerSettingsContract.Parse(request.ToJson())).Normalized;
							accepted = normalized.GeneratorVersion == 22 && normalized.MirroringAxes == 0 && normalized.GenerateCastles == castles;
						}
						catch (ArgumentException) { }
						Require(accepted == (size != 64 || players <= 4), $"Strongholds settings disagree at {size}/{players}/{castles}.");
						if (accepted) valid++;
					}

			foreach (var invalid in new[] { ("schema_version", "15"), ("layout_family", "natural-landscape"), ("generate_castles", "yes"), ("mirroring_axes", "1"), ("land_crossings", "one") })
			{
				var request = new RmgPlayerSettings { SchemaVersion = 16, LayoutFamily = RmgPlayerLayoutFamily.Strongholds }.ToJson();
				request[invalid.Item1] = invalid.Item1 is "schema_version" or "mirroring_axes" ? new Newtonsoft.Json.Linq.JValue(int.Parse(invalid.Item2)) : new Newtonsoft.Json.Linq.JValue(invalid.Item2);
				var rejected = false;
				try { RmgPlayerSettingsContract.Resolve(RmgPlayerSettingsContract.Parse(request)); } catch (ArgumentException) { rejected = true; }
				Require(rejected, "Invalid Strongholds field accepted: " + invalid.Item1);
			}

			Console.WriteLine($"PASS: 64 Strongholds size/player/castle combinations ({valid} valid), invalid fields, and actual Strongholds UI.");
		}

		static void CheckCrossroadsContract()
		{
			var valid = 0;
			foreach (var size in new[] { 64, 128, 256, 512 })
				for (var players = 1; players <= 8; players++)
					foreach (var width in Enum.GetValues<RmgBattlefieldLaneWidth>())
						foreach (var connections in Enum.GetValues<RmgCrossroadsConnections>())
						{
							var expected = players is 2 or 4 or 8 && players <= (size == 64 ? 4 : 8);
							var accepted = false;
							try
							{
								var requested = new RmgPlayerSettings
								{
									SchemaVersion = 13, LayoutFamily = RmgPlayerLayoutFamily.Crossroads,
									MapSize = size, PlayerCount = players, LaneWidth = width, SideConnections = connections, Tileset = "DESERT"
								};
								var parsed = RmgPlayerSettingsContract.Resolve(RmgPlayerSettingsContract.Parse(requested.ToJson())).Normalized;
								accepted = parsed.GeneratorVersion == 19 && parsed.LaneWidth == width && parsed.SideConnections == connections;
							}
							catch (ArgumentException) { }
							Require(accepted == expected, $"Crossroads settings disagree at {size}/{players}/{width}/{connections}.");
							if (accepted) valid++;
						}

			foreach (var invalid in new[]
			{
				("schema_version", "12"), ("layout_family", "artificial-battlefield"), ("block_shape", "diamonds"),
				("lane_width", "standard"), ("approach_width", "ultra"), ("side_connections", "three"), ("mirroring_axes", "1")
			})
			{
				var requested = new RmgPlayerSettings { SchemaVersion = 13, LayoutFamily = RmgPlayerLayoutFamily.Crossroads }.ToJson();
				requested[invalid.Item1] = invalid.Item1 is "schema_version" or "mirroring_axes" ? new Newtonsoft.Json.Linq.JValue(int.Parse(invalid.Item2)) : new Newtonsoft.Json.Linq.JValue(invalid.Item2);
				var rejected = false;
				try { RmgPlayerSettingsContract.Resolve(RmgPlayerSettingsContract.Parse(requested)); } catch (ArgumentException) { rejected = true; }
				Require(rejected, "Invalid Crossroads field/schema accepted: " + invalid.Item1);
			}

			Console.WriteLine($"PASS: 288 Crossroads settings ({valid} valid), invalid field/schema checks and actual Crossroads UI.");
		}

		static void CheckRingContract()
		{
			var annulus = Enumerable.Range(0, 64 * 64).Select(i => Math.Abs(Math.Sqrt((i % 64 - 31.5) * (i % 64 - 31.5) + (i / 64 - 31.5) * (i / 64 - 31.5)) - 20) <= 5).ToArray();
			Require(RingTopology.HasGroundLoop(annulus, 64, 64, 32 * 64 + 12), "Ring topology rejected a complete loop.");
			for (var y = 0; y < 32; y++) for (var x = 30; x <= 33; x++) annulus[y * 64 + x] = false;
			Require(!RingTopology.HasGroundLoop(annulus, 64, 64, 32 * 64 + 12), "Ring topology accepted a broken loop.");

			var valid = 0;
			foreach (var size in new[] { 64, 128, 256, 512 })
				for (var players = 1; players <= 8; players++)
					foreach (var width in Enum.GetValues<RmgBattlefieldLaneWidth>())
						foreach (var shape in Enum.GetValues<RmgRingShape>())
						{
							var expected = players is 2 or 4 or 8 && players <= (size == 64 ? 4 : 8);
							var accepted = false;
							try
							{
								var requested = new RmgPlayerSettings
								{
									SchemaVersion = 14, LayoutFamily = RmgPlayerLayoutFamily.Ring,
									MapSize = size, PlayerCount = players, LaneWidth = width, RingShape = shape, Tileset = "DESERT"
								};
								var parsed = RmgPlayerSettingsContract.Resolve(RmgPlayerSettingsContract.Parse(requested.ToJson())).Normalized;
								accepted = parsed.GeneratorVersion == 20 && parsed.LaneWidth == width && parsed.RingShape == shape;
							}
							catch (ArgumentException) { }
							Require(accepted == expected, $"Ring settings disagree at {size}/{players}/{width}/{shape}.");
							if (accepted) valid++;
						}

			foreach (var invalid in new[]
			{
				("schema_version", "13"), ("layout_family", "artificial-battlefield"), ("block_shape", "diamonds"),
				("lane_width", "standard"), ("ring_width", "ultra"), ("ring_shape", "triangle"), ("mirroring_axes", "1")
			})
			{
				var requested = new RmgPlayerSettings { SchemaVersion = 14, LayoutFamily = RmgPlayerLayoutFamily.Ring }.ToJson();
				requested[invalid.Item1] = invalid.Item1 is "schema_version" or "mirroring_axes" ? new Newtonsoft.Json.Linq.JValue(int.Parse(invalid.Item2)) : new Newtonsoft.Json.Linq.JValue(invalid.Item2);
				var rejected = false;
				try { RmgPlayerSettingsContract.Resolve(RmgPlayerSettingsContract.Parse(requested)); } catch (ArgumentException) { rejected = true; }
				Require(rejected, "Invalid Ring field/schema accepted: " + invalid.Item1);
			}

			Console.WriteLine($"PASS: 288 Ring settings ({valid} valid), invalid field/schema checks and actual Ring UI.");
		}

		static void CheckDividedLandsContract()
		{
			var valid = 0;
			foreach (var size in new[] { 64, 128, 256, 512 })
				for (var players = 1; players <= 8; players++)
					foreach (var width in Enum.GetValues<RmgBattlefieldLaneWidth>())
						foreach (var crossings in Enum.GetValues<RmgLandCrossings>())
						{
							var expected = players is 2 or 4 or 8 && players <= (size == 64 ? 4 : 8);
							var accepted = false;
							try
							{
								var requested = new RmgPlayerSettings
								{
									SchemaVersion = 15, LayoutFamily = RmgPlayerLayoutFamily.DividedLands,
									MapSize = size, PlayerCount = players, LaneWidth = width, LandCrossings = crossings, Tileset = "DESERT"
								};
								var parsed = RmgPlayerSettingsContract.Resolve(RmgPlayerSettingsContract.Parse(requested.ToJson())).Normalized;
								accepted = parsed.GeneratorVersion == 21 && parsed.LaneWidth == width && parsed.LandCrossings == crossings;
							}
							catch (ArgumentException) { }
							Require(accepted == expected, $"Divided Lands settings disagree at {size}/{players}/{width}/{crossings}.");
							if (accepted) valid++;
						}

			foreach (var invalid in new[]
			{
				("schema_version", "14"), ("layout_family", "artificial-battlefield"), ("block_shape", "diamonds"),
				("lane_width", "standard"), ("crossing_width", "ultra"), ("land_crossings", "three"), ("mirroring_axes", "1")
			})
			{
				var requested = new RmgPlayerSettings { SchemaVersion = 15, LayoutFamily = RmgPlayerLayoutFamily.DividedLands }.ToJson();
				requested[invalid.Item1] = invalid.Item1 is "schema_version" or "mirroring_axes" ? new Newtonsoft.Json.Linq.JValue(int.Parse(invalid.Item2)) : new Newtonsoft.Json.Linq.JValue(invalid.Item2);
				var rejected = false;
				try { RmgPlayerSettingsContract.Resolve(RmgPlayerSettingsContract.Parse(requested)); } catch (ArgumentException) { rejected = true; }
				Require(rejected, "Invalid Divided Lands field/schema accepted: " + invalid.Item1);
			}

			var shoreFixture = new bool[16]; shoreFixture[0] = true;
			var shoreDistances = DividedLandsGeometry.WaterDistances(shoreFixture, 4);
			Require(shoreDistances[15] == 3 && shoreDistances[10] == 2 && shoreDistances[3] == 3,
				"Shore clearance must distinguish two-cell passages from one-cell passages, including diagonal banks.");

			var topologySettings = new RmgGenerationSettings { GeneratorVersion = 21, MapSize = 64, PlayerCount = 2, MirroringAxes = 1, LandCrossings = RmgLandCrossings.None };
			var starts = new[] { new RmgPoint(10, 31), new RmgPoint(53, 31) };
			var separated = Enumerable.Range(0, 64 * 64).Select(i => Math.Abs(i % 64 - 31.5) > 5).ToArray();
			DividedLandsTopology.ValidateGround(separated, topologySettings, starts);
			var bridge = (bool[])separated.Clone();
			for (var y = 29; y <= 34; y++) for (var x = 26; x <= 37; x++) bridge[y * 64 + x] = true;
			var extraRejected = false;
			try { DividedLandsTopology.ValidateGround(bridge, topologySettings, starts); } catch (RmgGenerationRejectedException) { extraRejected = true; }
			Require(extraRejected, "Divided Lands accepted an unexpected crossing.");
			topologySettings.LandCrossings = RmgLandCrossings.One;
			DividedLandsTopology.ValidateGround(bridge, topologySettings, starts);
			var missingRejected = false;
			try { DividedLandsTopology.ValidateGround(separated, topologySettings, starts); } catch (RmgGenerationRejectedException) { missingRejected = true; }
			Require(missingRejected, "Divided Lands accepted a missing crossing.");
			var bypass = (bool[])bridge.Clone();
			for (var x = 0; x < 64; x++) bypass[x] = true;
			var bypassRejected = false;
			try { DividedLandsTopology.ValidateGround(bypass, topologySettings, starts); } catch (RmgGenerationRejectedException) { bypassRejected = true; }
			Require(bypassRejected, "Divided Lands accepted an unplanned map-edge bypass.");

			Console.WriteLine($"PASS: 288 Divided Lands settings ({valid} valid), invalid fields, missing/extra crossing and edge-bypass negative controls, and actual Divided Lands UI.");
		}

		static MapPreview SavePvpRuntimeCopy(Utility utility, MapPreview source, Folder directory, string id)
		{
			var locations = (Dictionary<IReadOnlyPackage, MapClassification>)utility.ModData.MapCache.MapLocations;
			var original = locations.Where(p => p.Value == MapClassification.User).ToArray();
			foreach (var pair in original) locations.Remove(pair.Key);
			locations.Add(directory, MapClassification.User);
			try
			{
				var saved = RmgMapSaver.Save(utility.ModData, source, "Saved " + id);
				var preview = utility.ModData.MapCache[saved.Uid];
				CompareSavedPackage(source, preview);
				return preview;
			}
			finally
			{
				locations.Remove(directory);
				foreach (var pair in original) locations.Add(pair.Key, pair.Value);
			}
		}
	}
}
