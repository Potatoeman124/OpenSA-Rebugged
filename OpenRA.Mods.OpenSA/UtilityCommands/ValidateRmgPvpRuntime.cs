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
			Choose("RMG_SIZE", "512 x 512 (Natural Landscape)");
			Require(Settings().MapSize == 512 && !lobby.Get<ButtonWidget>("RMG_GENERATE_BUTTON").IsDisabled(), "512 PvP generation disabled.");
			Draw(output, "pvp-512-four-axes");
			lobby.Get<ButtonWidget>("RMG_COLONY_OWNERSHIP").OnClick();
			Require(Ui.CurrentWindow().Get<ScrollPanelWidget>("SETTINGS").Children.Count == 8, "Ownership sliders disagree with mirrored player count.");
			Ui.CurrentWindow().Get<ButtonWidget>("CANCEL").OnClick();
			lobby.Get<ButtonWidget>("RMG_COLONY_WEIGHTS").OnClick(); Ui.CurrentWindow().Get<ButtonWidget>("CANCEL").OnClick();
			Choose("RMG_SIZE", "64 x 64 (Natural Landscape)");
			Require(Settings().PlayerCount == 4 && Settings().MirroringAxes == 2 && slider.MaximumValue == 4, "Small-map symmetry cap failed.");
			Draw(output, "pvp-64-two-axes");
			Choose("RMG_LAYOUT_FAMILY", "Natural Landscape (Regions)");
			slider.UpdateValue(1);
			Require(Settings().GeneratorVersion == 16 && Settings().MirroringAxes == 0 && Settings().PlayerCount == 1 && !lobby.Get("RMG_MIRRORING_AXES").IsVisible(), "Returning to Natural changed the baseline contract.");
			Ui.ResetAll();
			Console.WriteLine($"PASS: 96 size/player/axes combinations ({valid} allowed), invalid axes, actual PvP UI settings, size limits, ownership and baseline switching.");
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
