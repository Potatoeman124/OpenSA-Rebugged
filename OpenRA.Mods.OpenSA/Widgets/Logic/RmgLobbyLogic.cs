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
using OpenRA.Mods.OpenSA.Rmg.Reassessment;
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
		const int RmgLobbyHeight = 452;

		enum TerrainChoice { Normal, Desert, Swamp, Candy }
		enum SizeChoice { Small, Standard, Large, Huge }
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
		readonly LabelWithTooltipWidget statusLabel;
		readonly DropDownButtonWidget presetButton;
		readonly DropDownButtonWidget terrainButton;
		readonly DropDownButtonWidget sizeButton;
		readonly DropDownButtonWidget layoutButton;
		readonly DropDownButtonWidget layoutFamilyButton;
		readonly DropDownButtonWidget colonyButton;
		readonly DropDownButtonWidget waterButton;
		readonly DropDownButtonWidget tacticalTerrainButton;
		readonly DropDownButtonWidget complexityButton;
		readonly CheckboxWidget originalSurfaceRelationsCheckbox;
		readonly CheckboxWidget preventColonyOverlappingCheckbox;
		readonly ButtonWidget rmgToggleButton;

		RmgColonyWeights colonyWeights = new();
		readonly int[] ownershipShares = new int[8];
		RmgColonyOwnershipMode ownershipMode = RmgColonyOwnershipMode.ClosestToSpawn;
		RmgPlayerPreset preset = RmgPlayerPreset.Balanced;
		TerrainChoice terrain = TerrainChoice.Normal;
		SizeChoice size = SizeChoice.Large;
		LayoutChoice layout = LayoutChoice.ContestedCenter;
		RmgPlayerLayoutFamily layoutFamily = RmgPlayerLayoutFamily.NaturalLandscape;
		int mirroringAxes = 1;
		bool IsPvp => layoutFamily == RmgPlayerLayoutFamily.NaturalLandscapePvp;
		bool FixedPlayerCount => IsPvp && RmgMirroring.GroupSize(mirroringAxes) == MaximumPlayers;
		bool IsBattlefield => layoutFamily == RmgPlayerLayoutFamily.ArtificialBattlefield;
		bool generateCastles = true;
		bool respectStartingSafeArea = true;
		bool ownStartingStronghold;
		bool IsLabyrinth => layoutFamily == RmgPlayerLayoutFamily.Labyrinth;
		RmgBattlefieldLaneWidth passageWidth = RmgBattlefieldLaneWidth.Standard;
		RmgLabyrinthRoutes extraRoutes = RmgLabyrinthRoutes.Standard;
		bool IsStrongholds => layoutFamily == RmgPlayerLayoutFamily.Strongholds;
		bool IsDividedLands => layoutFamily == RmgPlayerLayoutFamily.DividedLands;
		bool IsRing => layoutFamily == RmgPlayerLayoutFamily.Ring;
		bool IsCrossroads => layoutFamily == RmgPlayerLayoutFamily.Crossroads;
		bool HasFixedPlayerCounts => IsBattlefield || IsCrossroads || IsRing || IsDividedLands;
		bool HasLayoutOptions => IsPvp || HasFixedPlayerCounts || IsStrongholds || IsLabyrinth;
		bool UsesModernTerrain => layoutFamily is RmgPlayerLayoutFamily.NaturalLandscape or RmgPlayerLayoutFamily.NaturalLandscapePvp or RmgPlayerLayoutFamily.ArtificialBattlefield or RmgPlayerLayoutFamily.Crossroads or RmgPlayerLayoutFamily.Ring or RmgPlayerLayoutFamily.DividedLands or RmgPlayerLayoutFamily.Strongholds or RmgPlayerLayoutFamily.Labyrinth;
		static readonly int[] BattlefieldPlayerCounts = { 2, 4, 8 };
		RmgBattlefieldBlockShape blockShape = RmgBattlefieldBlockShape.CutCorners;
		RmgBattlefieldLaneWidth laneWidth = RmgBattlefieldLaneWidth.Standard;
		RmgBattlefieldLaneWidth approachWidth = RmgBattlefieldLaneWidth.Standard;
		RmgLandCrossings landCrossings = RmgLandCrossings.One;
		RmgBattlefieldLaneWidth crossingWidth = RmgBattlefieldLaneWidth.Standard;
		RmgRingShape ringShape = RmgRingShape.Round;
		RmgBattlefieldLaneWidth ringWidth = RmgBattlefieldLaneWidth.Standard;
		RmgCrossroadsConnections sideConnections = RmgCrossroadsConnections.Standard;
		TerrainComplexity complexity = TerrainComplexity.Standard;
		RmgPlayerColonyDensity colonyDensity = RmgPlayerColonyDensity.Standard;
		RmgPlayerParameterLevel waterAmount = RmgPlayerParameterLevel.Standard;
		RmgPlayerParameterLevel tacticalTerrain = RmgPlayerParameterLevel.Standard;
		int playerCount = 4;
		bool presetCustomized;
		bool originalSurfaceRelations = true;
		bool preventColonyOverlapping = true;
		bool stale;
		bool rmgView;
		bool rmgMode;
		bool generating;
		bool generationQueued;
		long generationQueuedAt;
		bool visibilityComposed;
		bool rmgVisibilityDefaultsApplied;
		bool startGuardComposed;
		string generatedUid;
		string lastObservedMapUid;
		string pendingMapAcknowledgement;
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

			var saveMapButton = lobby.Get<ButtonWidget>("RMG_SAVE_MAP_BUTTON");
			saveMapButton.IsVisible = () => rmgView;
			saveMapButton.IsDisabled = () => !CanSaveGeneratedMap();
			saveMapButton.OnClick = OpenSaveMap;

			rmgToggleButton = toggleButton;
			startGameButton = lobby.Get<ButtonWidget>("START_GAME_BUTTON");
			generateButton = lobby.Get<ButtonWidget>("RMG_GENERATE_BUTTON");
			seedField = lobby.Get<TextFieldWidget>("RMG_SEED");
			playersSlider = lobby.Get<SliderWidget>("RMG_PLAYERS");
			playersValueLabel = lobby.Get<LabelWidget>("RMG_PLAYERS_VALUE");
			statusLabel = lobby.Get<LabelWithTooltipWidget>("RMG_STATUS");
			presetButton = lobby.Get<DropDownButtonWidget>("RMG_PRESET");
			terrainButton = lobby.Get<DropDownButtonWidget>("RMG_TERRAIN");
			sizeButton = lobby.Get<DropDownButtonWidget>("RMG_SIZE");
			layoutButton = lobby.Get<DropDownButtonWidget>("RMG_LAYOUT");
			layoutFamilyButton = lobby.Get<DropDownButtonWidget>("RMG_LAYOUT_FAMILY");
			colonyButton = lobby.Get<DropDownButtonWidget>("RMG_COLONY_DENSITY");
			waterButton = lobby.Get<DropDownButtonWidget>("RMG_WATER_AMOUNT");
			tacticalTerrainButton = lobby.Get<DropDownButtonWidget>("RMG_GRAVEL_MOSS_AMOUNT");
			complexityButton = lobby.Get<DropDownButtonWidget>("RMG_TERRAIN_COMPLEXITY");
			originalSurfaceRelationsCheckbox = lobby.Get<CheckboxWidget>("RMG_ORIGINAL_SURFACE_RELATIONS");
			preventColonyOverlappingCheckbox = lobby.Get<CheckboxWidget>("RMG_PREVENT_COLONY_OVERLAPPING");

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
			SetLobbyBounds(RmgLobbyWidth, RmgLobbyHeight + (HasLayoutOptions ? 40 : 0));
			SetBounds("SERVER_NAME", 0, 8, 1182, 25);
			SetBounds("RMG_TOGGLE_BUTTON", 20, 8, 200, 25);
			SetBounds("RMG_PANEL", 20, 42, 1142, 390 + (HasLayoutOptions ? 40 : 0));
			lobby.Get("RMG_SETTINGS_BACKGROUND").Bounds = new Rectangle(0, 0, 820, 390 + (HasLayoutOptions ? 40 : 0));
			lobby.Get("RMG_PREVIEW_BACKGROUND").Bounds = new Rectangle(835, 0, 307, 390 + (HasLayoutOptions ? 40 : 0));
			lobby.Get("RMG_STATUS").Bounds = new Rectangle(20, HasLayoutOptions ? 388 : 348, 770, 38);
			lobby.Get("RMG_PREVIEW_CAPTION").Bounds = new Rectangle(845, HasLayoutOptions ? 398 : 358, 287, 25);
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
			waterButton.GetTooltipText = () => IsLabyrinth ? "Water fills the spaces between passages. Low through Ultra use 60%, 72%, 82%, 91% and 100% of that space; maze walls set a minimum, and tile transitions affect final coverage." : waterButton.TooltipText;
			tacticalTerrainButton.GetTooltipText = () => IsLabyrinth ? "Slowing surfaces shape alternative routes. Complexity increases their target coverage; narrow passages, alcoves and original surface relations can limit it." : tacticalTerrainButton.TooltipText;
			colonyButton.GetTooltipText = () => IsLabyrinth ? "Labyrinth uses one quarter of the usual colony target, rounded up. Colonies occupy existing alcoves and never widen passages. Crowded maps can stop below the target." : colonyButton.TooltipText;
			complexityButton.GetTooltipText = () => IsLabyrinth ? "Higher complexity subdivides the maze into more, longer and narrower routes. The seed retains its starting positions and large-scale connections." : complexityButton.TooltipText;
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
					new Choice<TerrainChoice>(TerrainChoice.Desert, "Desert"),
					new Choice<TerrainChoice>(TerrainChoice.Swamp, "Swamp"),
					new Choice<TerrainChoice>(TerrainChoice.Candy, "Candy")
				}, () => terrain, value => { terrain = value; MarkStale(); },
				value => value == TerrainChoice.Normal || UsesModernTerrain);

			sizeButton.GetText = () => SizeDisplayName(size);
			BindDropDown(sizeButton,
				new[]
				{
					new Choice<SizeChoice>(SizeChoice.Small, "64 x 64"),
					new Choice<SizeChoice>(SizeChoice.Standard, "128 x 128"),
					new Choice<SizeChoice>(SizeChoice.Large, "256 x 256"),
					new Choice<SizeChoice>(SizeChoice.Huge, "512 x 512")
				}, () => size, value => { size = value; MarkStale(); },
				value => value == SizeChoice.Standard || UsesModernTerrain);

			layoutFamilyButton.GetText = () => LayoutFamilyDisplayName(layoutFamily);
			BindDropDown(layoutFamilyButton,
				new[]
				{
					new Choice<RmgPlayerLayoutFamily>(RmgPlayerLayoutFamily.NaturalLandscape, "Natural Landscape (Regions)"),
					new Choice<RmgPlayerLayoutFamily>(RmgPlayerLayoutFamily.NaturalLandscapePvp, "Natural Landscape PVP"),
					new Choice<RmgPlayerLayoutFamily>(RmgPlayerLayoutFamily.ArtificialBattlefield, "Artificial Battlefield"),
					new Choice<RmgPlayerLayoutFamily>(RmgPlayerLayoutFamily.Crossroads, "Crossroads"),
					new Choice<RmgPlayerLayoutFamily>(RmgPlayerLayoutFamily.Ring, "Ring"),
					new Choice<RmgPlayerLayoutFamily>(RmgPlayerLayoutFamily.DividedLands, "Divided Lands"),
					new Choice<RmgPlayerLayoutFamily>(RmgPlayerLayoutFamily.Strongholds, "Strongholds"),
					new Choice<RmgPlayerLayoutFamily>(RmgPlayerLayoutFamily.Labyrinth, "Labyrinth")
				}, () => layoutFamily, value =>
				{
					layoutFamily = value;

					presetCustomized = true;
					MarkStale();
				});

			var axesButton = lobby.Get<DropDownButtonWidget>("RMG_MIRRORING_AXES");
			axesButton.IsVisible = () => IsPvp;
			lobby.Get("RMG_MIRRORING_AXES_LABEL").IsVisible = () => IsPvp;
			axesButton.GetText = () => mirroringAxes == 1 ? "1 axis (2 halves)" : mirroringAxes == 2 ? "2 axes (4 quarters)" : "4 axes (8 sectors)";
			BindDropDown(axesButton, new[] { new Choice<int>(1, "1 axis (2 halves)"), new Choice<int>(2, "2 axes (4 quarters)"), new Choice<int>(4, "4 axes (8 sectors)") },
				() => mirroringAxes, value => { mirroringAxes = value; MarkStale(); }, value => value != 4 || size != SizeChoice.Small);

			var shapeButton = lobby.Get<DropDownButtonWidget>("RMG_BLOCK_SHAPE");
			shapeButton.GetText = () => blockShape switch { RmgBattlefieldBlockShape.Rectangles => "Rectangles", RmgBattlefieldBlockShape.Diamonds => "Diamonds", _ => "Cut Corners" };
			BindDropDown(shapeButton, new[]
			{
				new Choice<RmgBattlefieldBlockShape>(RmgBattlefieldBlockShape.Rectangles, "Rectangles"),
				new Choice<RmgBattlefieldBlockShape>(RmgBattlefieldBlockShape.CutCorners, "Cut Corners"),
				new Choice<RmgBattlefieldBlockShape>(RmgBattlefieldBlockShape.Diamonds, "Diamonds")
			},
				() => blockShape, value => { blockShape = value; MarkStale(); });
			var laneButton = lobby.Get<DropDownButtonWidget>("RMG_LANE_WIDTH");
			laneButton.GetText = () => laneWidth.ToString();
			BindDropDown(laneButton, new[]
			{
				new Choice<RmgBattlefieldLaneWidth>(RmgBattlefieldLaneWidth.Narrow, "Narrow"),
				new Choice<RmgBattlefieldLaneWidth>(RmgBattlefieldLaneWidth.Standard, "Standard"),
				new Choice<RmgBattlefieldLaneWidth>(RmgBattlefieldLaneWidth.Wide, "Wide")
			},
				() => laneWidth, value => { laneWidth = value; MarkStale(); });
			foreach (var id in new[] { "RMG_BLOCK_SHAPE", "RMG_BLOCK_SHAPE_LABEL", "RMG_LANE_WIDTH", "RMG_LANE_WIDTH_LABEL" })
				lobby.Get(id).IsVisible = () => IsBattlefield;

			var approachButton = lobby.Get<DropDownButtonWidget>("RMG_APPROACH_WIDTH");
			approachButton.GetText = () => approachWidth.ToString();
			BindDropDown(approachButton, new[]
			{
				new Choice<RmgBattlefieldLaneWidth>(RmgBattlefieldLaneWidth.Narrow, "Narrow"),
				new Choice<RmgBattlefieldLaneWidth>(RmgBattlefieldLaneWidth.Standard, "Standard"),
				new Choice<RmgBattlefieldLaneWidth>(RmgBattlefieldLaneWidth.Wide, "Wide")
			}, () => approachWidth, value => { approachWidth = value; MarkStale(); });
			var connectionsButton = lobby.Get<DropDownButtonWidget>("RMG_SIDE_CONNECTIONS");
			connectionsButton.GetText = () => sideConnections switch { RmgCrossroadsConnections.None => "None", RmgCrossroadsConnections.Many => "Two tiers", _ => "One tier" };
			BindDropDown(connectionsButton, new[]
			{
				new Choice<RmgCrossroadsConnections>(RmgCrossroadsConnections.None, "None"),
				new Choice<RmgCrossroadsConnections>(RmgCrossroadsConnections.Standard, "One tier"),
				new Choice<RmgCrossroadsConnections>(RmgCrossroadsConnections.Many, "Two tiers")
			}, () => sideConnections, value => { sideConnections = value; MarkStale(); });
			foreach (var id in new[] { "RMG_APPROACH_WIDTH", "RMG_APPROACH_WIDTH_LABEL", "RMG_SIDE_CONNECTIONS", "RMG_SIDE_CONNECTIONS_LABEL" })
				lobby.Get(id).IsVisible = () => IsCrossroads;

			var passageButton = lobby.Get<DropDownButtonWidget>("RMG_PASSAGE_WIDTH");
			passageButton.GetText = () => passageWidth.ToString();
			BindDropDown(passageButton, Enum.GetValues<RmgBattlefieldLaneWidth>().Select(v => new Choice<RmgBattlefieldLaneWidth>(v, v.ToString())).ToArray(),
				() => passageWidth, value => { passageWidth = value; MarkStale(); });
			var routesButton = lobby.Get<DropDownButtonWidget>("RMG_EXTRA_ROUTES");
			routesButton.GetText = () => extraRoutes.ToString();
			BindDropDown(routesButton, Enum.GetValues<RmgLabyrinthRoutes>().Select(v => new Choice<RmgLabyrinthRoutes>(v, v.ToString())).ToArray(),
				() => extraRoutes, value => { extraRoutes = value; MarkStale(); });
			foreach (var id in new[] { "RMG_PASSAGE_WIDTH", "RMG_PASSAGE_WIDTH_LABEL", "RMG_EXTRA_ROUTES", "RMG_EXTRA_ROUTES_LABEL" })
				lobby.Get(id).IsVisible = () => IsLabyrinth;
			var ringShapeButton = lobby.Get<DropDownButtonWidget>("RMG_RING_SHAPE");
			ringShapeButton.GetText = () => ringShape.ToString();
			BindDropDown(ringShapeButton, Enum.GetValues<RmgRingShape>().Select(v => new Choice<RmgRingShape>(v, v.ToString())).ToArray(),
				() => ringShape, value => { ringShape = value; MarkStale(); });
			var ringWidthButton = lobby.Get<DropDownButtonWidget>("RMG_RING_WIDTH");
			ringWidthButton.GetText = () => ringWidth.ToString();
			BindDropDown(ringWidthButton, Enum.GetValues<RmgBattlefieldLaneWidth>().Select(v => new Choice<RmgBattlefieldLaneWidth>(v, v.ToString())).ToArray(),
				() => ringWidth, value => { ringWidth = value; MarkStale(); });
			foreach (var id in new[] { "RMG_RING_SHAPE", "RMG_RING_SHAPE_LABEL", "RMG_RING_WIDTH", "RMG_RING_WIDTH_LABEL" })
				lobby.Get(id).IsVisible = () => IsRing;

			var safeArea = lobby.Get<CheckboxWidget>("RMG_RESPECT_STARTING_SAFE_AREA");
			safeArea.IsChecked = () => respectStartingSafeArea;
			safeArea.IsDisabled = () => !CanConfigure();
			safeArea.OnClick = () => { respectStartingSafeArea = !respectStartingSafeArea; MarkStale(); };
			var strongholdOwnership = lobby.Get<CheckboxWidget>("RMG_OWN_STARTING_STRONGHOLD");
			strongholdOwnership.IsVisible = () => IsStrongholds;
			strongholdOwnership.IsChecked = () => ownStartingStronghold;
			strongholdOwnership.IsDisabled = () => !CanConfigure();
			strongholdOwnership.OnClick = () => { ownStartingStronghold = !ownStartingStronghold; MarkStale(); };

			var castles = lobby.Get<CheckboxWidget>("RMG_GENERATE_CASTLES");
			castles.IsVisible = () => IsStrongholds;
			castles.IsChecked = () => generateCastles;
			castles.OnClick = () => { generateCastles = !generateCastles; MarkStale(); };

			var landCrossingsButton = lobby.Get<DropDownButtonWidget>("RMG_LAND_CROSSINGS");
			landCrossingsButton.GetText = () => RmgDividedLandsParameters.Display(landCrossings);
			BindDropDown(landCrossingsButton, Enum.GetValues<RmgLandCrossings>().Select(v => new Choice<RmgLandCrossings>(v, RmgDividedLandsParameters.Display(v))).ToArray(),
				() => landCrossings, value => { landCrossings = value; MarkStale(); });
			var crossingWidthButton = lobby.Get<DropDownButtonWidget>("RMG_CROSSING_WIDTH");
			crossingWidthButton.GetText = () => crossingWidth.ToString();
			BindDropDown(crossingWidthButton, Enum.GetValues<RmgBattlefieldLaneWidth>().Select(v => new Choice<RmgBattlefieldLaneWidth>(v, v.ToString())).ToArray(),
				() => crossingWidth, value => { crossingWidth = value; MarkStale(); });
			foreach (var id in new[] { "RMG_LAND_CROSSINGS", "RMG_LAND_CROSSINGS_LABEL", "RMG_CROSSING_WIDTH", "RMG_CROSSING_WIDTH_LABEL" })
				lobby.Get(id).IsVisible = () => IsDividedLands;

			layoutButton.GetText = () => LayoutDisplayName(layout);
			BindDropDown(layoutButton,
				new[]
				{
					new Choice<LayoutChoice>(LayoutChoice.OpenFields, "Open Fields"),
					new Choice<LayoutChoice>(LayoutChoice.ContestedCenter, "Contested Center")
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
					new Choice<RmgPlayerColonyDensity>(RmgPlayerColonyDensity.Dense, "Dense"),
					new Choice<RmgPlayerColonyDensity>(RmgPlayerColonyDensity.Extreme, "Extreme"),
					new Choice<RmgPlayerColonyDensity>(RmgPlayerColonyDensity.Ultra, "Ultra")
				}, () => colonyDensity, value =>
				{
					colonyDensity = value;
					presetCustomized = true;
					MarkStale();
				}, value => value <= RmgPlayerColonyDensity.Dense || UsesModernTerrain);

			waterButton.GetText = () => ParameterLevelDisplayName(waterAmount);
			BindDropDown(waterButton,
				new[]
				{
					new Choice<RmgPlayerParameterLevel>(RmgPlayerParameterLevel.Low, "Low"),
					new Choice<RmgPlayerParameterLevel>(RmgPlayerParameterLevel.Standard, "Standard"),
					new Choice<RmgPlayerParameterLevel>(RmgPlayerParameterLevel.High, "High"),
					new Choice<RmgPlayerParameterLevel>(RmgPlayerParameterLevel.Extreme, "Extreme"),
					new Choice<RmgPlayerParameterLevel>(RmgPlayerParameterLevel.Ultra, "Ultra")
				}, () => waterAmount, value =>
				{
					waterAmount = value;
					presetCustomized = true;
					MarkStale();
				}, value => value <= RmgPlayerParameterLevel.High || UsesModernTerrain);

			tacticalTerrainButton.GetText = () => ParameterLevelDisplayName(tacticalTerrain);
			BindDropDown(tacticalTerrainButton,
				new[]
				{
					new Choice<RmgPlayerParameterLevel>(RmgPlayerParameterLevel.Low, "Low"),
					new Choice<RmgPlayerParameterLevel>(RmgPlayerParameterLevel.Standard, "Standard"),
					new Choice<RmgPlayerParameterLevel>(RmgPlayerParameterLevel.High, "High"),
					new Choice<RmgPlayerParameterLevel>(RmgPlayerParameterLevel.Extreme, "Extreme"),
					new Choice<RmgPlayerParameterLevel>(RmgPlayerParameterLevel.Ultra, "Ultra")
				}, () => tacticalTerrain, value =>
				{
					tacticalTerrain = value;
					presetCustomized = true;
					MarkStale();
				}, value => value <= RmgPlayerParameterLevel.High || UsesModernTerrain);

			complexityButton.GetText = () => RmgPlayerSettingsContract.ComplexityDisplayName(complexity, true);
			BindDropDown(complexityButton, new[]
			{
				new Choice<TerrainComplexity>(TerrainComplexity.Low, "Small"),
				new Choice<TerrainComplexity>(TerrainComplexity.Standard, "Medium"),
				new Choice<TerrainComplexity>(TerrainComplexity.High, "High"),
				new Choice<TerrainComplexity>(TerrainComplexity.Extreme, "Extreme"),
				new Choice<TerrainComplexity>(TerrainComplexity.Ultra, "Ultra")
			}, () => complexity, value => { complexity = value; presetCustomized = true; MarkStale(); });
			foreach (var id in new[] { "RMG_LAYOUT", "RMG_LAYOUT_LABEL" })
				lobby.Get(id).IsVisible = () => !UsesModernTerrain;
			foreach (var id in new[] { "RMG_TERRAIN_COMPLEXITY", "RMG_TERRAIN_COMPLEXITY_LABEL", "RMG_PREVENT_COLONY_OVERLAPPING" })
				lobby.Get(id).IsVisible = () => UsesModernTerrain;

			originalSurfaceRelationsCheckbox.IsChecked = () => originalSurfaceRelations;
			originalSurfaceRelationsCheckbox.IsDisabled = () =>
				!CanConfigure() || !UsesModernTerrain;
			originalSurfaceRelationsCheckbox.OnClick = () =>
			{
				if (originalSurfaceRelationsCheckbox.IsDisabled())
					return;

				originalSurfaceRelations ^= true;
				presetCustomized = true;
				MarkStale();
			};

			preventColonyOverlappingCheckbox.IsChecked = () => preventColonyOverlapping;
			preventColonyOverlappingCheckbox.IsDisabled = () =>
				!CanConfigure() || !UsesModernTerrain;
			preventColonyOverlappingCheckbox.OnClick = () =>
			{
				if (preventColonyOverlappingCheckbox.IsDisabled())
					return;
				preventColonyOverlapping ^= true;
				presetCustomized = true;
				MarkStale();
			};

			UpdatePlayerRange();
			lobby.Get<LabelWidget>("RMG_PLAYERS_MIN").GetText = () => HasFixedPlayerCounts ? "2" : IsPvp ? RmgMirroring.GroupSize(mirroringAxes).ToString(CultureInfo.InvariantCulture) : UsesModernTerrain ? "1" : "2";
			lobby.Get<LabelWidget>("RMG_PLAYERS_MAX").GetText = () => MaximumPlayers.ToString(CultureInfo.InvariantCulture);
			var weightsButton = lobby.Get<ButtonWidget>("RMG_COLONY_WEIGHTS");
			weightsButton.IsDisabled = () => !CanConfigure() || !UsesModernTerrain;
			weightsButton.OnClick = () => Ui.OpenWindow("RMG_COLONY_WEIGHTS_PANEL", new WidgetArgs
			{
				{ "initialWeights", colonyWeights },
				{ "configurationDisabled", (Func<bool>)(() => !CanConfigure()) },
				{ "onApply", (Action<RmgColonyWeights>)(weights =>
				{
					if (!CanConfigure() || weights == colonyWeights) return;
					colonyWeights = weights;
					presetCustomized = true;
					MarkStale();
				}) }
			});
			var ownershipButton = lobby.Get<ButtonWidget>("RMG_COLONY_OWNERSHIP");
			ownershipButton.IsDisabled = () => weightsButton.IsDisabled() || (IsStrongholds && ownStartingStronghold);
			ownershipButton.GetText = () => IsStrongholds && ownStartingStronghold ? "Whole Stronghold Owned" : "Starting Ownership...";
			ownershipButton.OnClick = () => Ui.OpenWindow("RMG_COLONY_OWNERSHIP_PANEL", new WidgetArgs
			{
				{ "initialShares", ownershipShares.Take(playerCount).ToArray() },
				{ "initialMode", ownershipMode },
				{ "configurationDisabled", (Func<bool>)(() => !CanConfigure()) },
				{ "onApply", (Action<int[], RmgColonyOwnershipMode>)((shares, mode) =>
				{
					if (!CanConfigure() || (mode == ownershipMode && shares.SequenceEqual(ownershipShares.Take(playerCount)))) return;
					ownershipMode = mode;
					Array.Copy(shares, ownershipShares, shares.Length);
					presetCustomized = true;
					MarkStale();
				}) }
			});
			playersSlider.Value = playerCount;
			playersSlider.GetValue = () => HasFixedPlayerCounts ? Array.IndexOf(BattlefieldPlayerCounts, playerCount) : playerCount;
			playersSlider.OnChange += value =>
			{
				if (!CanConfigure()) return;
				var selected = HasFixedPlayerCounts ? BattlefieldPlayerCounts[Math.Clamp((int)Math.Round(value), 0, MaximumPlayers == 4 ? 1 : 2)] : IsPvp ? Math.Clamp((int)Math.Round(value / RmgMirroring.GroupSize(mirroringAxes), MidpointRounding.AwayFromZero) * RmgMirroring.GroupSize(mirroringAxes), RmgMirroring.GroupSize(mirroringAxes), MaximumPlayers) : UsesModernTerrain ? Math.Clamp((int)Math.Round(value), 1, MaximumPlayers) : value < 3 ? 2 : 4;
				if (selected == playerCount)
					return;

				playerCount = selected;
				playersSlider.Value = HasFixedPlayerCounts ? Array.IndexOf(BattlefieldPlayerCounts, selected) : selected;
				presetCustomized = true;
				MarkStale();
			};
			playersValueLabel.GetText = () => playerCount.ToString(CultureInfo.InvariantCulture);
			playersSlider.IsDisabled = () => !CanConfigure() || FixedPlayerCount;
			playersSlider.IsVisible = () => !FixedPlayerCount;
			lobby.Get("RMG_PLAYERS_FIXED").IsVisible = () => FixedPlayerCount;
			lobby.Get("RMG_PLAYERS_MIN").IsVisible = () => !FixedPlayerCount;
			lobby.Get("RMG_PLAYERS_MAX").IsVisible = () => !FixedPlayerCount;

			seedField.Text = CreateDisplaySeed().ToString(CultureInfo.InvariantCulture);
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


			var statusLayout = new CachedTransform<(string Text, int Width, int Height), string>(key =>
				RmgStatusText.Fit(key.Text, key.Width, key.Height,
					text => Game.Renderer.Fonts[statusLabel.Font].Measure(text)));
			statusLabel.GetText = () => statusLayout.Update((statusText, statusLabel.Bounds.Width, statusLabel.Bounds.Height));
			statusLabel.GetTooltipText = () => statusText;
			statusLabel.GetColor = () => statusKind switch
			{
				StatusKind.Success => ChromeMetrics.Get<Color>("NoticeSuccessColor"),
				StatusKind.Warning => Color.Orange,
				StatusKind.Error => ChromeMetrics.Get<Color>("NoticeErrorColor"),
				_ => ChromeMetrics.Get<Color>("NoticeInfoColor")
			};
		}

		void BindDropDown<T>(DropDownButtonWidget button, IReadOnlyList<Choice<T>> choices,
			Func<T> selected, Action<T> onSelected, Func<T, bool> available = null)
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

				button.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", Math.Min(175, choices.Count * 25), choices.Where(choice => available == null || available(choice.Value)), SetupItem);
			};
		}

		void ApplyPreset(RmgPlayerPreset selected)
		{
			preset = selected;
			presetCustomized = false;
			if (!IsPvp && !HasFixedPlayerCounts && !IsStrongholds && !IsLabyrinth) layoutFamily = RmgPlayerLayoutFamily.NaturalLandscape;
			layout = LayoutChoice.OpenFields;
			originalSurfaceRelations = true;
			preventColonyOverlapping = true;
			colonyWeights = new();
			Array.Clear(ownershipShares);
			ownershipMode = RmgColonyOwnershipMode.ClosestToSpawn;
			complexity = selected switch
			{
				RmgPlayerPreset.OpenConflict => TerrainComplexity.Low,
				RmgPlayerPreset.TacticalCrossroads => TerrainComplexity.High,
				_ => TerrainComplexity.Standard
			};
			colonyDensity = selected switch
			{
				RmgPlayerPreset.OpenConflict => RmgPlayerColonyDensity.Sparse,
				RmgPlayerPreset.TacticalCrossroads => RmgPlayerColonyDensity.Dense,
				_ => RmgPlayerColonyDensity.Standard
			};
			waterAmount = selected == RmgPlayerPreset.OpenConflict ? RmgPlayerParameterLevel.Low : RmgPlayerParameterLevel.Standard;
			tacticalTerrain = selected switch
			{
				RmgPlayerPreset.OpenConflict => RmgPlayerParameterLevel.Low,
				RmgPlayerPreset.TacticalCrossroads => RmgPlayerParameterLevel.High,
				_ => RmgPlayerParameterLevel.Standard
			};
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
			if (terrain != TerrainChoice.Normal && !UsesModernTerrain)
				return "Desert, Swamp and Candy require Natural Landscape.";
			if (size != SizeChoice.Standard && !UsesModernTerrain)
				return "64 x 64, 256 x 256 and 512 x 512 require Natural Landscape.";
			if (UsesModernTerrain ? playerCount < 1 || playerCount > MaximumPlayers : playerCount != 2 && playerCount != 4)
				return "64 x 64 supports 1 through 4 players; larger Natural Landscape maps support 1 through 8. Historical layouts support 2 or 4.";
			if (layout is LayoutChoice.MixedFronts or LayoutChoice.NarrowPassages or LayoutChoice.Chaos)
				return $"{LayoutDisplayName(layout)} is a planned layout placeholder.";
			return null;
		}

		int MaximumPlayers => UsesModernTerrain && size != SizeChoice.Small ? 8 : 4;

		void UpdatePlayerRange()
		{
			if (HasFixedPlayerCounts)
			{
				playerCount = Math.Min(MaximumPlayers, playerCount <= 2 ? 2 : playerCount <= 4 ? 4 : 8);
				playersSlider.MinimumValue = 0;
				playersSlider.MaximumValue = MaximumPlayers == 4 ? 1 : 2;
				playersSlider.Ticks = MaximumPlayers == 4 ? 2 : 3;
				playersSlider.Value = Array.IndexOf(BattlefieldPlayerCounts, playerCount);
				return;
			}

			if (IsPvp && size == SizeChoice.Small && mirroringAxes == 4) mirroringAxes = 2;
			var natural = UsesModernTerrain;
			var minimum = IsPvp ? RmgMirroring.GroupSize(mirroringAxes) : natural ? 1 : 2;
			playersSlider.MinimumValue = minimum;
			playersSlider.MaximumValue = MaximumPlayers;
			playersSlider.Ticks = FixedPlayerCount ? 0 : IsPvp ? MaximumPlayers / minimum : natural ? MaximumPlayers : 2;
			if (IsPvp)
			{
				playerCount = Math.Clamp((int)Math.Round((double)playerCount / minimum, MidpointRounding.AwayFromZero) * minimum, minimum, MaximumPlayers);
				playersSlider.Value = playerCount;
			}

			if (playerCount > MaximumPlayers) playerCount = MaximumPlayers;

			// Fixed-count families store a slider index; free-count families store the count.
			playersSlider.Value = playerCount;
		}

		void MarkStale()
		{
			UpdatePlayerRange();
			if (rmgView) ApplyRmgLayout();
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

		RmgPlayerSettings CreatePlayerSettings(ulong seed) => new RmgPlayerSettings
		{
			GenerateCastles = !IsStrongholds || generateCastles,
			RespectStartingSafeArea = respectStartingSafeArea,
			OwnStartingStronghold = IsStrongholds && ownStartingStronghold,
			SchemaVersion = IsLabyrinth ? 18 : !respectStartingSafeArea || (IsStrongholds && ownStartingStronghold) ? 17 : IsStrongholds ? 16 : IsDividedLands ? 15 : IsRing ? 14 : IsCrossroads ? 13 : IsBattlefield ? 12 : IsPvp ? 11 : UsesModernTerrain ? 10 : 3,
			MirroringAxes = IsPvp ? mirroringAxes : 0,
			BlockShape = IsBattlefield ? blockShape : RmgBattlefieldBlockShape.CutCorners,
			RingShape = IsRing ? ringShape : RmgRingShape.Round,
			LandCrossings = IsDividedLands ? landCrossings : RmgLandCrossings.One,
			ExtraRoutes = IsLabyrinth ? extraRoutes : RmgLabyrinthRoutes.Standard,
			LaneWidth = IsLabyrinth ? passageWidth : IsDividedLands ? crossingWidth : IsRing ? ringWidth : IsCrossroads ? approachWidth : IsBattlefield ? laneWidth : RmgBattlefieldLaneWidth.Standard,
			SideConnections = IsCrossroads ? sideConnections : RmgCrossroadsConnections.Standard,
			MapSize = size switch { SizeChoice.Small => 64, SizeChoice.Standard => 128, SizeChoice.Large => 256, _ => 512 },
			Preset = preset,
			Seed = seed,
			Tileset = terrain.ToString().ToUpperInvariant(),
			PlayerCount = playerCount,
			Symmetry = RmgPlayerSymmetry.Automatic,
			Layout = UsesModernTerrain ? RmgPlayerLayout.Preset :
				layout == LayoutChoice.OpenFields ? RmgPlayerLayout.OpenFields : RmgPlayerLayout.ContestedCenter,
			LayoutFamily = layoutFamily,
			NeutralColonyDensity = colonyDensity,
			WaterAmount = waterAmount,
			TacticalTerrain = tacticalTerrain,
			TerrainComplexity = UsesModernTerrain ? complexity : TerrainComplexity.Standard,
			OriginalSurfaceRelations = originalSurfaceRelations,
			PreventColonyOverlapping = !UsesModernTerrain || preventColonyOverlapping,
			NeutralColonyWeights = UsesModernTerrain ? colonyWeights : new(),
			StartingColonyMode = UsesModernTerrain ? ownershipMode : RmgColonyOwnershipMode.ClosestToSpawn,
			StartingColonyShares = UsesModernTerrain ? ownershipShares.Take(playerCount).ToArray() : Array.Empty<int>()
		};

		void GeneratePreview()
		{
			try
			{
				if (!ulong.TryParse(seedField.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var seed))
					throw new ArgumentException("Seed must be an unsigned whole number.");

				var playerSettings = CreatePlayerSettings(seed);
				var settingsResolution = RmgPlayerSettingsContract.Resolve(playerSettings);
				var profile = RmgProfile.Load(modData, settingsResolution.Normalized);
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
				if (result.Generation.Profile.UsesRegionsTerrain)
				{
					var cells = result.Generation.Map.NativeTerrainIntents;
					var water = cells.Count(cell => cell == RmgNativeTerrainIntent.Water);
					var land = cells.Length - water;
					var gravel = cells.Count(cell => cell == RmgNativeTerrainIntent.Rock);
					var moss = cells.Count(cell => cell == RmgNativeTerrainIntent.Vegetation);
					var closerColonies = (int?)result.Generation.Map.RegionsReport["neutral_colonies_fallback"] ?? 0;
					var spacingSummary = closerColonies > 0 ? $" {closerColonies} placed with closer spacing." : string.Empty;
					if ((HasFixedPlayerCounts || IsStrongholds || IsLabyrinth) && result.Generation.Validation.Warnings.Any(w => w.Code is "BATTLEFIELD_TERRAIN_CAPACITY" or "CROSSROADS_TERRAIN_CAPACITY" or "RING_TERRAIN_CAPACITY" or "DIVIDED_LANDS_TERRAIN_CAPACITY" or "STRONGHOLDS_TERRAIN_CAPACITY" or "LABYRINTH_TERRAIN_CAPACITY"))
						spacingSummary += IsLabyrinth ? " Passages/alcoves/transitions limit modifiers." : " Routes/plazas/transitions limit coverage.";
					if (IsLabyrinth && result.Generation.Validation.Warnings.Any(w => w.Code == "LABYRINTH_WATER_FLOOR"))
						spacingSummary += " Maze walls set minimum water coverage.";
					if (IsCrossroads && result.Generation.Validation.Warnings.Any(w => w.Code == "CROSSROADS_WATER_FLOOR"))
						spacingSummary += " Dividers set minimum water coverage.";
					if (IsRing && result.Generation.Validation.Warnings.Any(w => w.Code == "RING_WATER_FLOOR"))
						spacingSummary += " Central lake sets minimum water coverage.";
					if (IsDividedLands && landCrossings == RmgLandCrossings.None) spacingSummary += " No land crossings.";
					if (IsDividedLands && result.Generation.Validation.Warnings.Any(w => w.Code == "DIVIDED_LANDS_WATER_FLOOR"))
						spacingSummary += " Channels set minimum water coverage.";
					SetStatus($"Ready: {(IsLabyrinth ? "Labyrinth" : IsStrongholds ? "Strongholds" : IsDividedLands ? "Divided Lands" : IsRing ? "Ring" : IsCrossroads ? "Crossroads" : IsBattlefield ? "Battlefield" : "Regions")} / {RmgPlayerSettingsContract.ComplexityDisplayName(complexity, true)} ({result.Performance.TotalMilliseconds / 1000d:0.0}s). " +
						$"Water {100D * water / cells.Length:0.0}%; {(terrain == TerrainChoice.Normal ? "gravel/moss" : "surface modifiers")} {100D * gravel / land:0.0}/{100D * moss / land:0.0}% of land; " +
						$"colonies {placedColonies}/{settingsResolution.Normalized.EffectiveNeutralColonyCount}.{spacingSummary}",
						result.Generation.Validation.Warnings.Count > 0 ? StatusKind.Warning : StatusKind.Success);
				}
				else
				{
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
				}
				// An identical generation is already selected; re-selecting it would reset lobby readiness.
				if (CurrentMapUid() != generatedUid)
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

		bool CanSaveGeneratedMap() => !generating && !stale && generatedUid != null && CurrentMapUid() == generatedUid &&
			modData.MapCache[generatedUid].Status == MapStatus.Available;

		void OpenSaveMap()
		{
			if (!CanSaveGeneratedMap()) return;
			var uid = generatedUid;
			void Save(string name)
			{
				if (!CanSaveGeneratedMap() || generatedUid != uid)
					throw new InvalidOperationException("The generated preview changed before saving.");
				var saved = RmgMapSaver.Save(modData, modData.MapCache[uid], name);
				SetStatus("Saved to custom maps: " + Path.GetFileName(saved.Path), StatusKind.Success);
			}

			Ui.OpenWindow("RMG_SAVE_MAP_PANEL", new WidgetArgs
			{
				{ "saveDisabled", (Func<bool>)(() => !CanSaveGeneratedMap() || generatedUid != uid) },
				{ "onSave", (Action<string>)Save }
			});
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
				startGameButton.IsDisabled = () => originalStartDisabled() || orderManager.LocalClient == null || orderManager.LocalClient.IsInvalid ||
					(rmgMode && (generating || stale || generatedUid == null || CurrentMapUid() != generatedUid));
			}

			var currentMap = CurrentMapUid();
			// Recover an interrupted/redundant map handshake only after the local cache confirms the package.
			if (orderManager.LocalClient?.IsInvalid == true && !string.IsNullOrEmpty(currentMap))
			{
				var preview = modData.MapCache[currentMap];
				if (preview.Status == MapStatus.Available && Server.RmgLobbyCommands.IsGeneratedMap(preview) && pendingMapAcknowledgement != currentMap)
				{
					pendingMapAcknowledgement = currentMap;
					orderManager.IssueOrder(Order.Command("state NotReady"));
				}
			}
			else pendingMapAcknowledgement = null;
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

			// Initialize the RMG visibility preference once, after the server has
			// selected its first generated map. Later regenerations keep user choices.
			if (!rmgVisibilityDefaultsApplied && generatedUid != null && currentMap == generatedUid)
			{
				var options = orderManager.LobbyInfo.GlobalSettings.LobbyOptions;
				if (options.TryGetValue("explored", out var explored) && options.TryGetValue("fog", out var fog))
				{
					rmgVisibilityDefaultsApplied = true;
					if (!explored.IsLocked && !explored.IsEnabled)
						orderManager.IssueOrder(Order.Command("option explored True"));
					if (!fog.IsLocked && fog.IsEnabled)
						orderManager.IssueOrder(Order.Command("option fog False"));
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
			TerrainChoice.Desert => "Desert",
			TerrainChoice.Swamp => "Swamp",
			_ => "Candy"
		};

		static string SizeDisplayName(SizeChoice value) => value switch
		{
			SizeChoice.Small => "64 x 64",
			SizeChoice.Standard => "128 x 128",
			SizeChoice.Large => "256 x 256",
			_ => "512 x 512"
		};

		static string LayoutFamilyDisplayName(RmgPlayerLayoutFamily value) => value switch
		{
			RmgPlayerLayoutFamily.NaturalLandscape => "Natural Landscape (Regions)",
			RmgPlayerLayoutFamily.NaturalLandscapePvp => "Natural Landscape PVP",
			RmgPlayerLayoutFamily.StructuredCompetitive => "Structured Competitive",
			RmgPlayerLayoutFamily.ArtificialBattlefield => "Artificial Battlefield",
			RmgPlayerLayoutFamily.Crossroads => "Crossroads",
			RmgPlayerLayoutFamily.Ring => "Ring",
			RmgPlayerLayoutFamily.DividedLands => "Divided Lands",
			RmgPlayerLayoutFamily.Strongholds => "Strongholds",
			RmgPlayerLayoutFamily.Labyrinth => "Labyrinth",
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
			RmgPlayerParameterLevel.Extreme => "Extreme",
			RmgPlayerParameterLevel.Ultra => "Ultra",
			_ => "Standard"
		};

		static string ColonyDisplayName(RmgPlayerColonyDensity value) => value switch
		{
			RmgPlayerColonyDensity.Sparse => "Sparse",
			RmgPlayerColonyDensity.Dense => "Dense",
			RmgPlayerColonyDensity.Extreme => "Extreme",
			RmgPlayerColonyDensity.Ultra => "Ultra",
			_ => "Standard"
		};
	}
}
