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
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Mods.OpenSA.Rmg;
using OpenRA.Network;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.OpenSA.Widgets.Logic
{
	public sealed class RmgLobbyLogic : ChromeLogic
	{
		const int RmgLobbyWidth = 1182;
		const int RmgLobbyHeight = 763;

		enum TerrainChoice { Normal, Desert, Swamp, Candy }
		enum SizeChoice { Small, Standard, Large }
		enum LayoutChoice { OpenFields, ContestedCenter, MixedFronts, NarrowPassages, Chaos }
		enum StatusKind { Info, Success, Warning, Error }

		sealed record Choice<T>(T Value, string Label);

		readonly Widget lobby;
		readonly ModData modData;
		readonly OrderManager orderManager;
		readonly ButtonWidget startGameButton;
		readonly ButtonWidget generateButton;
		readonly TextFieldWidget seedField;
		readonly SliderWidget playersSlider;
		readonly LabelWidget playersValueLabel;
		readonly LabelWidget statusLabel;
		readonly DropDownButtonWidget presetButton;
		readonly DropDownButtonWidget terrainButton;
		readonly DropDownButtonWidget sizeButton;
		readonly DropDownButtonWidget layoutButton;
		readonly DropDownButtonWidget colonyButton;

		RmgPlayerPreset preset = RmgPlayerPreset.Balanced;
		TerrainChoice terrain = TerrainChoice.Normal;
		SizeChoice size = SizeChoice.Standard;
		LayoutChoice layout = LayoutChoice.ContestedCenter;
		RmgPlayerColonyDensity colonyDensity = RmgPlayerColonyDensity.Standard;
		int playerCount = 4;
		bool presetCustomized;
		bool stale;
		bool rmgMode;
		bool generating;
		bool generationQueued;
		long generationQueuedAt;
		bool startGuardComposed;
		string generatedUid;
		string lastObservedMapUid;
		string statusText = "Ready. Generate Preview creates and selects a playable map.";
		StatusKind statusKind = StatusKind.Info;
		Func<bool> originalStartDisabled;

		[ObjectCreator.UseCtor]
		public RmgLobbyLogic(Widget widget, ModData modData, OrderManager orderManager, bool skirmishMode)
		{
			lobby = widget;
			this.modData = modData;
			this.orderManager = orderManager;

			var rmgPanel = lobby.Get("RMG_PANEL");
			rmgPanel.IsVisible = () => skirmishMode;
			if (!skirmishMode)
				return;

			ApplySkirmishLayout();
			startGameButton = lobby.Get<ButtonWidget>("START_GAME_BUTTON");
			generateButton = lobby.Get<ButtonWidget>("RMG_GENERATE_BUTTON");
			seedField = lobby.Get<TextFieldWidget>("RMG_SEED");
			playersSlider = lobby.Get<SliderWidget>("RMG_PLAYERS");
			playersValueLabel = lobby.Get<LabelWidget>("RMG_PLAYERS_VALUE");
			statusLabel = lobby.Get<LabelWidget>("RMG_STATUS");
			presetButton = lobby.Get<DropDownButtonWidget>("RMG_PRESET");
			terrainButton = lobby.Get<DropDownButtonWidget>("RMG_TERRAIN");
			sizeButton = lobby.Get<DropDownButtonWidget>("RMG_SIZE");
			layoutButton = lobby.Get<DropDownButtonWidget>("RMG_LAYOUT");
			colonyButton = lobby.Get<DropDownButtonWidget>("RMG_COLONY_DENSITY");

			BindControls();
			lastObservedMapUid = CurrentMapUid();
		}

		void ApplySkirmishLayout()
		{
			var resolution = Game.Renderer.Resolution;
			lobby.Bounds = new Rectangle(
				Math.Max(0, (resolution.Width - RmgLobbyWidth) / 2),
				Math.Max(0, (resolution.Height - RmgLobbyHeight) / 2),
				RmgLobbyWidth,
				RmgLobbyHeight);

			SetBounds("SERVER_NAME", 20, 8, 1142, 25);
			SetBounds("RMG_PANEL", 20, 42, 1142, 310);
			SetBounds("MAP_PREVIEW_ROOT", 875, 55, 270, 250);
			SetBounds("SLOTS_DROPDOWNBUTTON", 20, 365, 205, 28);
			SetBounds("CHANGEMAP_BUTTON", 987, 365, 175, 28);

			var tabs = lobby.Get("SKIRMISH_TABS");
			tabs.Bounds = new Rectangle(235, 365, 742, 31);
			tabs.Get<ButtonWidget>("PLAYERS_TAB").Bounds = new Rectangle(0, 0, 247, 31);
			tabs.Get<ButtonWidget>("OPTIONS_TAB").Bounds = new Rectangle(247, 0, 248, 31);
			tabs.Get<ButtonWidget>("MUSIC_TAB").Bounds = new Rectangle(495, 0, 247, 31);

			SetBounds("TOP_PANELS_ROOT", 20, 420, 1142, 145);
			var chat = lobby.Get("LOBBYCHAT");
			chat.Bounds = new Rectangle(20, 577, 1142, 166);
			chat.Get<ScrollPanelWidget>("CHAT_DISPLAY").Bounds = new Rectangle(0, 0, 1142, 136);
			chat.Get<ButtonWidget>("CHAT_MODE").Bounds = new Rectangle(0, 141, 50, 25);
			chat.Get<TextFieldWidget>("CHAT_TEXTFIELD").Bounds = new Rectangle(55, 141, 795, 25);
			SetBounds("START_GAME_BUTTON", 870, 718, 132, 25);
			SetBounds("DISCONNECT_BUTTON", 1012, 718, 150, 25);
		}

		void SetBounds(string id, int x, int y, int width, int height) =>
			lobby.Get(id).Bounds = new Rectangle(x, y, width, height);

		void BindControls()
		{
			presetButton.GetText = () => presetCustomized ?
				$"Custom ({RmgPlayerSettingsContract.PresetDisplayName(preset)})" :
				RmgPlayerSettingsContract.PresetDisplayName(preset);
			BindDropDown(presetButton,
				new[]
				{
					new Choice<RmgPlayerPreset>(RmgPlayerPreset.Balanced, "Balanced"),
					new Choice<RmgPlayerPreset>(RmgPlayerPreset.OpenConflict, "Open Conflict"),
					new Choice<RmgPlayerPreset>(RmgPlayerPreset.TacticalCrossroads, "Tactical Crossroads")
				}, () => preset, ApplyPreset);

			terrainButton.GetText = () => TerrainDisplayName(terrain);
			BindDropDown(terrainButton,
				new[]
				{
					new Choice<TerrainChoice>(TerrainChoice.Normal, "Normal"),
					new Choice<TerrainChoice>(TerrainChoice.Desert, "Desert (planned)"),
					new Choice<TerrainChoice>(TerrainChoice.Swamp, "Swamp (planned)"),
					new Choice<TerrainChoice>(TerrainChoice.Candy, "Candy (planned)")
				}, () => terrain, value => { terrain = value; MarkStale(); });

			sizeButton.GetText = () => SizeDisplayName(size);
			BindDropDown(sizeButton,
				new[]
				{
					new Choice<SizeChoice>(SizeChoice.Small, "64 x 64 (planned)"),
					new Choice<SizeChoice>(SizeChoice.Standard, "128 x 128"),
					new Choice<SizeChoice>(SizeChoice.Large, "256 x 256 (planned)")
				}, () => size, value => { size = value; MarkStale(); });

			layoutButton.GetText = () => LayoutDisplayName(layout);
			BindDropDown(layoutButton,
				new[]
				{
					new Choice<LayoutChoice>(LayoutChoice.OpenFields, "Open Fields"),
					new Choice<LayoutChoice>(LayoutChoice.ContestedCenter, "Contested Center"),
					new Choice<LayoutChoice>(LayoutChoice.MixedFronts, "Mixed Fronts (planned)"),
					new Choice<LayoutChoice>(LayoutChoice.NarrowPassages, "Narrow Passages (planned)"),
					new Choice<LayoutChoice>(LayoutChoice.Chaos, "Chaos (planned)")
				}, () => layout, value =>
				{
					layout = value;
					presetCustomized = true;
					MarkStale();
				});

			colonyButton.GetText = () => ColonyDisplayName(colonyDensity);
			BindDropDown(colonyButton,
				new[]
				{
					new Choice<RmgPlayerColonyDensity>(RmgPlayerColonyDensity.Sparse, "Sparse"),
					new Choice<RmgPlayerColonyDensity>(RmgPlayerColonyDensity.Standard, "Standard"),
					new Choice<RmgPlayerColonyDensity>(RmgPlayerColonyDensity.Dense, "Dense")
				}, () => colonyDensity, value =>
				{
					colonyDensity = value;
					presetCustomized = true;
					MarkStale();
				});

			playersSlider.Value = playerCount;
			playersSlider.GetValue = () => playerCount;
			playersSlider.OnChange += value =>
			{
				var selected = Math.Clamp((int)Math.Round(value), 1, 8);
				if (selected == playerCount)
					return;

				playerCount = selected;
				playersSlider.Value = selected;
				presetCustomized = true;
				MarkStale();
			};
			playersValueLabel.GetText = () => playerCount.ToString(CultureInfo.InvariantCulture);
			playersSlider.IsDisabled = () => !CanConfigure();

			seedField.Text = "3100025";
			seedField.IsValid = TryGetSeed;
			seedField.OnTextEdited = MarkStale;

			var randomizeButton = lobby.Get<ButtonWidget>("RMG_RANDOMIZE_BUTTON");
			randomizeButton.IsDisabled = () => !CanConfigure();
			randomizeButton.OnClick = () =>
			{
				var bytes = new byte[sizeof(ulong)];
				RandomNumberGenerator.Fill(bytes);
				seedField.Text = BitConverter.ToUInt64(bytes, 0).ToString(CultureInfo.InvariantCulture);
				MarkStale();
			};

			generateButton.GetText = () => generating ? "Generating..." : "Generate Preview";
			generateButton.IsDisabled = () => !CanConfigure() || UnsupportedReason() != null || !TryGetSeed();
			generateButton.OnClick = QueueGeneration;

			foreach (var id in new[] { "RMG_TERRAIN_COMPLEXITY", "RMG_WATER_AMOUNT", "RMG_CHOKEPOINTS", "RMG_HOSTILES" })
				lobby.Get<DropDownButtonWidget>(id).IsDisabled = () => true;

			statusLabel.GetText = () => statusText;
			statusLabel.GetColor = () => statusKind switch
			{
				StatusKind.Success => ChromeMetrics.Get<Color>("NoticeSuccessColor"),
				StatusKind.Warning => Color.Orange,
				StatusKind.Error => ChromeMetrics.Get<Color>("NoticeErrorColor"),
				_ => ChromeMetrics.Get<Color>("NoticeInfoColor")
			};
		}

		void BindDropDown<T>(DropDownButtonWidget button, IReadOnlyList<Choice<T>> choices,
			Func<T> selected, Action<T> onSelected)
		{
			button.IsDisabled = () => !CanConfigure();
			button.OnMouseDown = _ =>
			{
				ScrollItemWidget SetupItem(Choice<T> choice, ScrollItemWidget template)
				{
					var item = ScrollItemWidget.Setup(template,
						() => EqualityComparer<T>.Default.Equals(selected(), choice.Value),
						() => onSelected(choice.Value));
					item.Get<LabelWidget>("LABEL").GetText = () => choice.Label;
					return item;
				}

				button.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", Math.Min(175, choices.Count * 25), choices, SetupItem);
			};
		}

		void ApplyPreset(RmgPlayerPreset selected)
		{
			preset = selected;
			presetCustomized = false;
			switch (selected)
			{
				case RmgPlayerPreset.OpenConflict:
					layout = LayoutChoice.OpenFields;
					colonyDensity = RmgPlayerColonyDensity.Sparse;
					break;
				case RmgPlayerPreset.TacticalCrossroads:
					layout = LayoutChoice.ContestedCenter;
					colonyDensity = RmgPlayerColonyDensity.Dense;
					break;
				default:
					layout = LayoutChoice.ContestedCenter;
					colonyDensity = RmgPlayerColonyDensity.Standard;
					break;
			}

			MarkStale();
		}

		bool CanConfigure() => !generating && Game.IsHost && orderManager.LocalClient != null && !orderManager.LocalClient.IsReady;
		bool TryGetSeed() => ulong.TryParse(seedField?.Text, NumberStyles.None, CultureInfo.InvariantCulture, out _);

		string UnsupportedReason()
		{
			if (terrain != TerrainChoice.Normal)
				return $"{TerrainDisplayName(terrain)} terrain is a planned placeholder; NORMAL is currently supported.";
			if (size != SizeChoice.Standard)
				return $"{SizeDisplayName(size)} maps are a planned placeholder; 128 x 128 is currently supported.";
			if (playerCount != 2 && playerCount != 4)
				return $"{playerCount}-player generation is planned; the current generator supports 2 or 4 players.";
			if (layout is LayoutChoice.MixedFronts or LayoutChoice.NarrowPassages or LayoutChoice.Chaos)
				return $"{LayoutDisplayName(layout)} is a planned layout placeholder.";
			return null;
		}

		void MarkStale()
		{
			rmgMode = true;
			stale = true;
			var unsupported = UnsupportedReason();
			if (unsupported != null)
				SetStatus(unsupported, StatusKind.Warning);
			else if (!TryGetSeed())
				SetStatus("Seed must be an unsigned whole number.", StatusKind.Error);
			else
				SetStatus("Settings changed. Generate a new preview before starting this RMG map.", StatusKind.Warning);
		}

		void QueueGeneration()
		{
			if (generateButton.IsDisabled())
				return;

			generationQueued = true;
			generationQueuedAt = Game.RunTime;
			generating = true;
			rmgMode = true;
			stale = true;
			SetStatus("Generating and validating the map...", StatusKind.Info);
		}

		void SetStatus(string text, StatusKind kind)
		{
			statusText = text;
			statusKind = kind;
		}

		void GeneratePreview()
		{
			try
			{
				if (!ulong.TryParse(seedField.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var seed))
					throw new ArgumentException("Seed must be an unsigned whole number.");

				var playerSettings = new RmgPlayerSettings
				{
					Preset = preset,
					Seed = seed,
					PlayerCount = playerCount,
					Symmetry = RmgPlayerSymmetry.Automatic,
					Layout = layout == LayoutChoice.OpenFields ? RmgPlayerLayout.OpenFields : RmgPlayerLayout.ContestedCenter,
					NeutralColonyDensity = colonyDensity
				};
				var settingsResolution = RmgPlayerSettingsContract.Resolve(playerSettings);
				var profile = RmgProfile.Load(modData, settingsResolution.Normalized.TopologyPreset);
				var userLocation = modData.MapCache.MapLocations.FirstOrDefault(location => location.Value == MapClassification.User);
				if (userLocation.Key == null)
					throw new InvalidOperationException("The OpenSA user map directory is unavailable.");

				var userDirectory = Path.GetFullPath(Platform.ResolvePath(userLocation.Key.Name));
				Directory.CreateDirectory(userDirectory);
				var outputPath = NextCandidatePath(userDirectory);
				var oldPreview = modData.MapCache.FirstOrDefault(map => map.Package != null && PathsEqual(map.Package.Name, outputPath));
				var result = OpenRaRmgMapAdapter.GenerateAndSave(modData, profile, settingsResolution.Normalized, outputPath, true,
					RmgMovementValidationMode.Both);

				modData.MapCache.LoadMap(Path.GetFileName(outputPath), userLocation.Key, MapClassification.User,
					modData.Manifest.Get<MapGrid>(), oldPreview?.Uid);
				var candidate = modData.MapCache[result.EngineUid];
				if (candidate.Status != MapStatus.Available)
					throw new InvalidOperationException("The generated package passed validation but was not accepted by the live map cache.");

				generatedUid = result.EngineUid;
				stale = false;
				rmgMode = true;
				SetStatus($"Ready: {candidate.Title} ({result.Performance.TotalMilliseconds / 1000d:0.0}s)", StatusKind.Success);
				orderManager.IssueOrder(Order.Command("map " + generatedUid));
				Game.Settings.Server.Map = generatedUid;
				Game.Settings.Save();
			}
			catch (Exception e)
			{
				stale = true;
				SetStatus("Generation failed: " + FirstLine(e.Message), StatusKind.Error);
				Log.Write("debug", "RMG lobby generation failed:");
				Log.Write("debug", e);
			}
			finally
			{
				generating = false;
			}
		}

		string NextCandidatePath(string userDirectory)
		{
			var candidateA = Path.Combine(userDirectory, "OpenSA-RMG-preview-a.oramap");
			var candidateB = Path.Combine(userDirectory, "OpenSA-RMG-preview-b.oramap");
			var currentUid = CurrentMapUid();
			var currentPackage = string.IsNullOrEmpty(currentUid) ? null : modData.MapCache[currentUid].Package?.Name;
			if (PathsEqual(currentPackage, candidateA))
				return candidateB;
			if (PathsEqual(currentPackage, candidateB))
				return candidateA;
			if (!File.Exists(candidateA))
				return candidateA;
			if (!File.Exists(candidateB))
				return candidateB;
			return File.GetLastWriteTimeUtc(candidateA) <= File.GetLastWriteTimeUtc(candidateB) ? candidateA : candidateB;
		}

		static bool PathsEqual(string first, string second) => !string.IsNullOrEmpty(first) && !string.IsNullOrEmpty(second) &&
			string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);

		static string FirstLine(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return "Unknown error.";

			var lineBreak = text.IndexOfAny(new[] { '\r', '\n' });
			var line = lineBreak < 0 ? text : text[..lineBreak];
			const int maximumLength = 120;
			return line.Length <= maximumLength ? line : line[..(maximumLength - 3)] + "...";
		}

		string CurrentMapUid() => orderManager.LobbyInfo?.GlobalSettings?.Map;

		public override void Tick()
		{
			if (startGameButton == null)
				return;

			if (!startGuardComposed)
			{
				startGuardComposed = true;
				originalStartDisabled = startGameButton.IsDisabled;
				startGameButton.IsDisabled = () => originalStartDisabled() ||
					(rmgMode && (generating || stale || generatedUid == null || CurrentMapUid() != generatedUid));
			}

			var currentMap = CurrentMapUid();
			if (currentMap != lastObservedMapUid)
			{
				lastObservedMapUid = currentMap;
				if (currentMap == generatedUid)
					rmgMode = true;
				else if (!generating && (generatedUid != null || rmgMode))
				{
					rmgMode = false;
					stale = false;
					SetStatus("Existing map selected. Adjust settings or Generate Preview to return to RMG.", StatusKind.Info);
				}
			}

			// Leave one render opportunity after the click so the user sees the Generating state.
			if (generationQueued && Game.RunTime != generationQueuedAt)
			{
				generationQueued = false;
				GeneratePreview();
			}
		}

		static string TerrainDisplayName(TerrainChoice value) => value switch
		{
			TerrainChoice.Normal => "Normal",
			TerrainChoice.Desert => "Desert (planned)",
			TerrainChoice.Swamp => "Swamp (planned)",
			_ => "Candy (planned)"
		};

		static string SizeDisplayName(SizeChoice value) => value switch
		{
			SizeChoice.Small => "64 x 64 (planned)",
			SizeChoice.Standard => "128 x 128",
			_ => "256 x 256 (planned)"
		};

		static string LayoutDisplayName(LayoutChoice value) => value switch
		{
			LayoutChoice.OpenFields => "Open Fields",
			LayoutChoice.ContestedCenter => "Contested Center",
			LayoutChoice.MixedFronts => "Mixed Fronts (planned)",
			LayoutChoice.NarrowPassages => "Narrow Passages (planned)",
			_ => "Chaos (planned)"
		};

		static string ColonyDisplayName(RmgPlayerColonyDensity value) => value switch
		{
			RmgPlayerColonyDensity.Sparse => "Sparse",
			RmgPlayerColonyDensity.Dense => "Dense",
			_ => "Standard"
		};
	}
}
