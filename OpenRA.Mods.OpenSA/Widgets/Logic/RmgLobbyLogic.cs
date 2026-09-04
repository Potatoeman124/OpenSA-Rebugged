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
		const int NormalLobbyWidth = 900;
		const int NormalLobbyHeight = 600;
		const int RmgLobbyWidth = 1182;
		const int RmgLobbyHeight = 412;

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
		readonly DropDownButtonWidget layoutFamilyButton;
		readonly DropDownButtonWidget colonyButton;
		readonly DropDownButtonWidget waterButton;
		readonly DropDownButtonWidget tacticalTerrainButton;
		readonly CheckboxWidget originalSurfaceRelationsCheckbox;
		readonly ButtonWidget rmgToggleButton;

		RmgPlayerPreset preset = RmgPlayerPreset.Balanced;
		TerrainChoice terrain = TerrainChoice.Normal;
		SizeChoice size = SizeChoice.Standard;
		LayoutChoice layout = LayoutChoice.ContestedCenter;
		RmgPlayerLayoutFamily layoutFamily = RmgPlayerLayoutFamily.StructuredCompetitive;
		RmgPlayerColonyDensity colonyDensity = RmgPlayerColonyDensity.Standard;
		RmgPlayerParameterLevel waterAmount = RmgPlayerParameterLevel.Standard;
		RmgPlayerParameterLevel tacticalTerrain = RmgPlayerParameterLevel.Standard;
		int playerCount = 4;
		bool presetCustomized;
		bool originalSurfaceRelations = true;
		bool stale;
		bool rmgView;
		bool rmgMode;
		bool generating;
		bool generationQueued;
		long generationQueuedAt;
		bool visibilityComposed;
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
			var toggleButton = lobby.Get<ButtonWidget>("RMG_TOGGLE_BUTTON");
			rmgPanel.IsVisible = () => skirmishMode && rmgView;
			toggleButton.IsVisible = () => skirmishMode;
			if (!skirmishMode)
				return;

			rmgToggleButton = toggleButton;
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
			layoutFamilyButton = lobby.Get<DropDownButtonWidget>("RMG_LAYOUT_FAMILY");
			colonyButton = lobby.Get<DropDownButtonWidget>("RMG_COLONY_DENSITY");
			waterButton = lobby.Get<DropDownButtonWidget>("RMG_WATER_AMOUNT");
			tacticalTerrainButton = lobby.Get<DropDownButtonWidget>("RMG_TERRAIN_COMPLEXITY");
			originalSurfaceRelationsCheckbox = lobby.Get<CheckboxWidget>("RMG_ORIGINAL_SURFACE_RELATIONS");

			BindControls();
			rmgToggleButton.GetText = () => rmgView ? "Return to Skirmish" : "Random Map Generator";
			rmgToggleButton.IsDisabled = () => generating;
			rmgToggleButton.OnClick = ToggleRmgView;
			ApplyNormalLayout();
			lastObservedMapUid = CurrentMapUid();
		}

		void ApplyNormalLayout()
		{
			SetLobbyBounds(NormalLobbyWidth, NormalLobbyHeight);
			SetBounds("SERVER_NAME", 0, 16, 900, 25);
			SetBounds("RMG_TOGGLE_BUTTON", 20, 16, 200, 25);
			SetBounds("MAP_PREVIEW_ROOT", 706, 67, 174, 250);
			SetBounds("SLOTS_DROPDOWNBUTTON", 20, 291, 185, 25);
			SetBounds("CHANGEMAP_BUTTON", 706, 291, 174, 25);

			var tabs = lobby.Get("SKIRMISH_TABS");
			tabs.Bounds = new Rectangle(209, 0, 486, 600);
			tabs.Get<ButtonWidget>("PLAYERS_TAB").Bounds = new Rectangle(0, 285, 162, 31);
			tabs.Get<ButtonWidget>("OPTIONS_TAB").Bounds = new Rectangle(162, 285, 162, 31);
			tabs.Get<ButtonWidget>("MUSIC_TAB").Bounds = new Rectangle(324, 285, 162, 31);

			SetBounds("TOP_PANELS_ROOT", 20, 67, 675, 219);
			var chat = lobby.Get("LOBBYCHAT");
			chat.Bounds = new Rectangle(20, 321, 860, 259);
			chat.Get<ScrollPanelWidget>("CHAT_DISPLAY").Bounds = new Rectangle(0, 0, 860, 229);
			chat.Get<ButtonWidget>("CHAT_MODE").Bounds = new Rectangle(0, 234, 50, 25);
			chat.Get<TextFieldWidget>("CHAT_TEXTFIELD").Bounds = new Rectangle(55, 234, 545, 25);
			SetBounds("START_GAME_BUTTON", 630, 555, 120, 25);
			SetBounds("DISCONNECT_BUTTON", 760, 555, 120, 25);
		}

		void ApplyRmgLayout()
		{
			SetLobbyBounds(RmgLobbyWidth, RmgLobbyHeight);
			SetBounds("SERVER_NAME", 0, 8, 1182, 25);
			SetBounds("RMG_TOGGLE_BUTTON", 20, 8, 200, 25);
			SetBounds("RMG_PANEL", 20, 42, 1142, 350);
			SetBounds("MAP_PREVIEW_ROOT", 875, 55, 270, 250);
		}

		void SetLobbyBounds(int width, int height)
		{
			var resolution = Game.Renderer.Resolution;
			lobby.Bounds = new Rectangle(
				Math.Max(0, (resolution.Width - width) / 2),
				Math.Max(0, (resolution.Height - height) / 2),
				width,
				height);
		}

		void ToggleRmgView()
		{
			if (generating)
				return;

			rmgView = !rmgView;
			if (rmgView)
				ApplyRmgLayout();
			else
				ApplyNormalLayout();
		}

		void ComposeViewVisibility()
		{
			foreach (var id in new[]
			{
				"SLOTS_DROPDOWNBUTTON",
				"SKIRMISH_TABS",
				"TOP_PANELS_ROOT",
				"CHANGEMAP_BUTTON",
				"LOBBYCHAT",
				"START_GAME_BUTTON",
				"DISCONNECT_BUTTON",
				"FACTION_DROPDOWN_PANEL_ROOT"
			})
			{
				var widget = lobby.Get(id);
				var originalVisibility = widget.IsVisible;
				widget.IsVisible = () => !rmgView && originalVisibility();
			}
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

			layoutFamilyButton.GetText = () => LayoutFamilyDisplayName(layoutFamily);
			BindDropDown(layoutFamilyButton,
				new[]
				{
					new Choice<RmgPlayerLayoutFamily>(RmgPlayerLayoutFamily.NaturalLandscape, "Natural Landscape (experimental)"),
					new Choice<RmgPlayerLayoutFamily>(RmgPlayerLayoutFamily.StructuredCompetitive, "Structured Competitive"),
					new Choice<RmgPlayerLayoutFamily>(RmgPlayerLayoutFamily.ArtificialBattlefield, "Artificial Battlefield")
				}, () => layoutFamily, value =>
				{
					layoutFamily = value;
					presetCustomized = true;
					MarkStale();
				});

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

			waterButton.GetText = () => ParameterLevelDisplayName(waterAmount);
			BindDropDown(waterButton,
				new[]
				{
					new Choice<RmgPlayerParameterLevel>(RmgPlayerParameterLevel.Low, "Low"),
					new Choice<RmgPlayerParameterLevel>(RmgPlayerParameterLevel.Standard, "Standard"),
					new Choice<RmgPlayerParameterLevel>(RmgPlayerParameterLevel.High, "High")
				}, () => waterAmount, value =>
				{
					waterAmount = value;
					presetCustomized = true;
					MarkStale();
				});

			tacticalTerrainButton.GetText = () => ParameterLevelDisplayName(tacticalTerrain);
			BindDropDown(tacticalTerrainButton,
				new[]
				{
					new Choice<RmgPlayerParameterLevel>(RmgPlayerParameterLevel.Low, "Low"),
					new Choice<RmgPlayerParameterLevel>(RmgPlayerParameterLevel.Standard, "Standard"),
					new Choice<RmgPlayerParameterLevel>(RmgPlayerParameterLevel.High, "High")
				}, () => tacticalTerrain, value =>
				{
					tacticalTerrain = value;
					presetCustomized = true;
					MarkStale();
				});

			originalSurfaceRelationsCheckbox.IsChecked = () => originalSurfaceRelations;
			originalSurfaceRelationsCheckbox.IsDisabled = () =>
				!CanConfigure() || layoutFamily != RmgPlayerLayoutFamily.NaturalLandscape;
			originalSurfaceRelationsCheckbox.OnClick = () =>
			{
				if (originalSurfaceRelationsCheckbox.IsDisabled())
					return;

				originalSurfaceRelations ^= true;
				presetCustomized = true;
				MarkStale();
			};

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
				seedField.Text = CreateDisplaySeed().ToString(CultureInfo.InvariantCulture);
				MarkStale();
			};

			generateButton.GetText = () => generating ? "Generating..." : "Generate Preview";
			generateButton.IsDisabled = () => !CanConfigure() || UnsupportedReason() != null || !TryGetSeed();
			generateButton.OnClick = QueueGeneration;

			foreach (var id in new[] { "RMG_CHOKEPOINTS", "RMG_HOSTILES" })
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
					layoutFamily = RmgPlayerLayoutFamily.ArtificialBattlefield;
					layout = LayoutChoice.OpenFields;
					colonyDensity = RmgPlayerColonyDensity.Sparse;
					waterAmount = RmgPlayerParameterLevel.Low;
					tacticalTerrain = RmgPlayerParameterLevel.Low;
					break;
				case RmgPlayerPreset.TacticalCrossroads:
					layoutFamily = RmgPlayerLayoutFamily.ArtificialBattlefield;
					layout = LayoutChoice.ContestedCenter;
					colonyDensity = RmgPlayerColonyDensity.Dense;
					waterAmount = RmgPlayerParameterLevel.Standard;
					tacticalTerrain = RmgPlayerParameterLevel.High;
					break;
				default:
					layoutFamily = RmgPlayerLayoutFamily.StructuredCompetitive;
					layout = LayoutChoice.ContestedCenter;
					colonyDensity = RmgPlayerColonyDensity.Standard;
					waterAmount = RmgPlayerParameterLevel.Standard;
					tacticalTerrain = RmgPlayerParameterLevel.Standard;
					break;
			}

			MarkStale();
		}

		bool CanConfigure() => !generating && Game.IsHost && orderManager.LocalClient != null && !orderManager.LocalClient.IsReady;

		static ulong CreateDisplaySeed()
		{
			const ulong ExclusiveUpperBound = 1_000_000_000_000_000_000UL;
			var rejectionLimit = ulong.MaxValue - ulong.MaxValue % ExclusiveUpperBound;
			var bytes = new byte[sizeof(ulong)];
			ulong value;
			do
			{
				RandomNumberGenerator.Fill(bytes);
				value = BitConverter.ToUInt64(bytes, 0);
			}
			while (value >= rejectionLimit);

			return value % ExclusiveUpperBound;
		}

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
			rmgMode = generatedUid != null && CurrentMapUid() == generatedUid;
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
			rmgMode = generatedUid != null && CurrentMapUid() == generatedUid;
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
					LayoutFamily = layoutFamily,
					NeutralColonyDensity = colonyDensity,
					WaterAmount = waterAmount,
					TacticalTerrain = tacticalTerrain,
					OriginalSurfaceRelations = originalSurfaceRelations
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
				var placedColonies = result.Generation.Map.Actors.Count(actor => actor.Owner == result.Generation.Profile.ColonyOwner);
				var obstaclePercent = result.Generation.Validation.Metrics["obstacle_density_percent"];
				var interiorWaterPercent = result.Generation.Validation.Metrics["water_interior_density_percent"];
				var waterBodyCount = result.Generation.Validation.Metrics["water_body_count"];
				var largestWaterBodyShare = result.Generation.Validation.Metrics["water_largest_body_share_percent"];
				var rockPercent = result.Generation.Validation.Metrics["rock_land_achieved_percent"];
				var vegetationPercent = result.Generation.Validation.Metrics["vegetation_land_achieved_percent"];
				var colonySummary = placedColonies < settingsResolution.Normalized.NeutralColonyCount ?
					$"; colonies {placedColonies}/{settingsResolution.Normalized.NeutralColonyCount}" : string.Empty;
				var adjusted = result.Generation.Validation.Warnings.Count > 0 ? "; adjusted safely" : string.Empty;
				SetStatus($"Ready: {candidate.Title} ({result.Performance.TotalMilliseconds / 1000d:0.0}s; " +
					$"Water {obstaclePercent:0.0}% total/{interiorWaterPercent:0.0}% interior, " +
					$"{waterBodyCount:0} bodies/{largestWaterBodyShare:0}% largest; " +
					$"Rock/Vegetation {rockPercent:0.0}/{vegetationPercent:0.0}%{colonySummary}{adjusted})",
					result.Generation.Validation.Warnings.Count > 0 ? StatusKind.Warning : StatusKind.Success);
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
			const int MaximumLength = 120;
			return line.Length <= MaximumLength ? line : line[..(MaximumLength - 3)] + "...";
		}

		string CurrentMapUid() => orderManager.LobbyInfo?.GlobalSettings?.Map;

		public override void Tick()
		{
			if (startGameButton == null)
				return;

			if (!visibilityComposed)
			{
				visibilityComposed = true;
				ComposeViewVisibility();
			}

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

		static string LayoutFamilyDisplayName(RmgPlayerLayoutFamily value) => value switch
		{
			RmgPlayerLayoutFamily.NaturalLandscape => "Natural Landscape (experimental)",
			RmgPlayerLayoutFamily.StructuredCompetitive => "Structured Competitive",
			RmgPlayerLayoutFamily.ArtificialBattlefield => "Artificial Battlefield",
			_ => "Preset"
		};

		static string LayoutDisplayName(LayoutChoice value) => value switch
		{
			LayoutChoice.OpenFields => "Open Fields",
			LayoutChoice.ContestedCenter => "Contested Center",
			LayoutChoice.MixedFronts => "Mixed Fronts (planned)",
			LayoutChoice.NarrowPassages => "Narrow Passages (planned)",
			_ => "Chaos (planned)"
		};

		static string ParameterLevelDisplayName(RmgPlayerParameterLevel value) => value switch
		{
			RmgPlayerParameterLevel.Low => "Low",
			RmgPlayerParameterLevel.High => "High",
			_ => "Standard"
		};

		static string ColonyDisplayName(RmgPlayerColonyDensity value) => value switch
		{
			RmgPlayerColonyDensity.Sparse => "Sparse",
			RmgPlayerColonyDensity.Dense => "Dense",
			_ => "Standard"
		};
	}
}
