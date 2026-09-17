#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OpenRA.Mods.OpenSA.Rmg.Reassessment;

namespace OpenRA.Mods.OpenSA.Rmg
{
	// UI recipes expand into the existing settings contract. Historical serialized
	// preset identifiers and generator versions keep their original meaning.
	public sealed record RmgPresetDefinition(string Id, string Name, string Description, RmgPlayerLayoutFamily Family)
	{
		public RmgPlayerPreset LegacyPreset { get; init; } = RmgPlayerPreset.Balanced;
		public string Tileset { get; init; } = "NORMAL";
		public TerrainComplexity Complexity { get; init; } = TerrainComplexity.Standard;
		public RmgPlayerParameterLevel Water { get; init; } = RmgPlayerParameterLevel.Standard;
		public RmgPlayerParameterLevel Surfaces { get; init; } = RmgPlayerParameterLevel.Standard;
		public RmgPlayerColonyDensity Colonies { get; init; } = RmgPlayerColonyDensity.Standard;
		public bool OriginalSurfaces { get; init; } = true;
		public bool PreventOverlap { get; init; } = true;
		public bool SafeArea { get; init; } = true;
		public RmgColonyWeights Weights { get; init; } = new();
		public int StartingShare { get; init; }
		public RmgColonyOwnershipMode OwnershipMode { get; init; } = RmgColonyOwnershipMode.ClosestToSpawn;
		public int Axes { get; init; } = 1;
		public RmgBattlefieldBlockShape BlockShape { get; init; } = RmgBattlefieldBlockShape.CutCorners;
		public RmgBattlefieldLaneWidth LaneWidth { get; init; } = RmgBattlefieldLaneWidth.Standard;
		public RmgCrossroadsConnections SideConnections { get; init; } = RmgCrossroadsConnections.Standard;
		public RmgRingShape RingShape { get; init; } = RmgRingShape.Round;
		public RmgLandCrossings LandCrossings { get; init; } = RmgLandCrossings.One;
		public bool GenerateCastles { get; init; } = true;
		public bool OwnStronghold { get; init; }
		public RmgLabyrinthRoutes ExtraRoutes { get; init; } = RmgLabyrinthRoutes.Standard;
		public RmgIslandAmount IslandAmount { get; init; } = RmgIslandAmount.Standard;
		public RmgIslandSize IslandSize { get; init; } = RmgIslandSize.Standard;
		public RmgChaosScale ChaosScale { get; init; } = RmgChaosScale.Standard;
		public RmgChaosBiomes ChaosBiomes { get; init; } = RmgChaosBiomes.Patchwork;

		public RmgPlayerSettings CreateSettings(int size, int players, ulong seed)
		{
			players = Math.Clamp(players, 1, size == 64 ? 4 : 8);
			var axes = Family == RmgPlayerLayoutFamily.NaturalLandscapePvp ? Math.Min(Axes, size == 64 ? 2 : 4) : 0;
			if (axes != 0)
			{
				var group = RmgMirroring.GroupSize(axes);
				players = Math.Clamp((int)Math.Round((double)players / group, MidpointRounding.AwayFromZero) * group, group, size == 64 ? 4 : 8);
			}
			else if (Family is RmgPlayerLayoutFamily.ArtificialBattlefield or RmgPlayerLayoutFamily.Crossroads or RmgPlayerLayoutFamily.Ring or RmgPlayerLayoutFamily.DividedLands)
				players = players <= 2 ? 2 : players <= 4 ? 4 : 8;

			return new RmgPlayerSettings
			{
				SchemaVersion = 20, Preset = LegacyPreset, MapSize = size, PlayerCount = players, Seed = seed,
				LayoutFamily = Family, Tileset = Tileset, TerrainComplexity = Complexity,
				WaterAmount = Water, TacticalTerrain = Surfaces, NeutralColonyDensity = Colonies,
				OriginalSurfaceRelations = OriginalSurfaces, PreventColonyOverlapping = PreventOverlap,
				RespectStartingSafeArea = SafeArea, NeutralColonyWeights = Weights,
				StartingColonyShares = Enumerable.Repeat(StartingShare, players).ToArray(), StartingColonyMode = OwnershipMode,
				MirroringAxes = axes, BlockShape = BlockShape, LaneWidth = LaneWidth, SideConnections = SideConnections,
				RingShape = RingShape, LandCrossings = LandCrossings, GenerateCastles = GenerateCastles, OwnStartingStronghold = OwnStronghold,
				ExtraRoutes = ExtraRoutes, IslandAmount = IslandAmount, IslandSize = IslandSize, ChaosScale = ChaosScale, ChaosBiomes = ChaosBiomes
			};
		}

		public bool Matches(RmgPlayerSettings current) => JToken.DeepEquals(
			RmgPlayerSettingsContract.Resolve(current).ToJson()["normalized"],
			RmgPlayerSettingsContract.Resolve(CreateSettings(current.MapSize, current.PlayerCount, current.Seed)).ToJson()["normalized"]);
	}

	public static class RmgPresetCatalog
	{
		public static IReadOnlyList<RmgPresetDefinition> All { get; } = Array.AsReadOnly(new[]
		{
			new RmgPresetDefinition("balanced", "Balanced", "A varied natural landscape with moderate terrain and colony density. A general-purpose starting point.", RmgPlayerLayoutFamily.NaturalLandscape),
			new RmgPresetDefinition("open-conflict", "Open Conflict", "Open natural terrain, light surface effects and scattered colonies. Room for movement and larger field battles.", RmgPlayerLayoutFamily.NaturalLandscape)
			{
				LegacyPreset = RmgPlayerPreset.OpenConflict, Complexity = TerrainComplexity.Low,
				Water = RmgPlayerParameterLevel.Low, Surfaces = RmgPlayerParameterLevel.Low, Colonies = RmgPlayerColonyDensity.Sparse
			},
			new RmgPresetDefinition("wild-frontier", "Wild Frontier", "A tangled swamp with plentiful objectives and heavy slowing surfaces. Colonies may occupy surface modifiers.", RmgPlayerLayoutFamily.NaturalLandscape)
			{
				Tileset = "SWAMP", Complexity = TerrainComplexity.Extreme, Water = RmgPlayerParameterLevel.High,
				Surfaces = RmgPlayerParameterLevel.Extreme, Colonies = RmgPlayerColonyDensity.Dense, OriginalSurfaces = false
			},
			new RmgPresetDefinition("mirror-match", "Mirror Match", "Mirrored natural terrain, starts and colony opportunities. One reflection axis keeps the geography comparable for opposing sides.", RmgPlayerLayoutFamily.NaturalLandscapePvp)
			{
				Complexity = TerrainComplexity.High, Surfaces = RmgPlayerParameterLevel.High, Colonies = RmgPlayerColonyDensity.Dense
			},
			new RmgPresetDefinition("grand-arena", "Grand Arena", "A geometric desert battlefield with diamond blocks, broad combat lanes and planned colony sites.", RmgPlayerLayoutFamily.ArtificialBattlefield)
			{
				Tileset = "DESERT", Complexity = TerrainComplexity.High, Surfaces = RmgPlayerParameterLevel.High,
				Colonies = RmgPlayerColonyDensity.Dense, BlockShape = RmgBattlefieldBlockShape.Diamonds, LaneWidth = RmgBattlefieldLaneWidth.Wide
			},
			new RmgPresetDefinition("tactical-crossroads", "Tactical Crossroads", "Narrow approaches lead into a contested center. No side connections: control of the central routes matters.", RmgPlayerLayoutFamily.Crossroads)
			{
				LegacyPreset = RmgPlayerPreset.TacticalCrossroads, Complexity = TerrainComplexity.High, Colonies = RmgPlayerColonyDensity.Dense,
				LaneWidth = RmgBattlefieldLaneWidth.Narrow, SideConnections = RmgCrossroadsConnections.None
			},
			new RmgPresetDefinition("long-way-round", "Long Way Round", "A narrow square circuit around a candy lake. Fewer Wasps colonies put more emphasis on fighting along the ring.", RmgPlayerLayoutFamily.Ring)
			{
				Tileset = "CANDY", Complexity = TerrainComplexity.High, Water = RmgPlayerParameterLevel.High,
				Surfaces = RmgPlayerParameterLevel.High, Colonies = RmgPlayerColonyDensity.Dense,
				RingShape = RmgRingShape.Square, LaneWidth = RmgBattlefieldLaneWidth.Narrow, Weights = new(100, 100, 100, 100, 20)
			},
			new RmgPresetDefinition("border-wars", "Border Wars", "Separate territories joined by one narrow crossing per border. Defend the crossings or develop flying forces to go around them.", RmgPlayerLayoutFamily.DividedLands)
			{
				Complexity = TerrainComplexity.High, Water = RmgPlayerParameterLevel.High,
				Colonies = RmgPlayerColonyDensity.Dense, LaneWidth = RmgBattlefieldLaneWidth.Narrow
			},
			new RmgPresetDefinition("fortress-realms", "Fortress Realms", "Begin owning your entire stronghold. Packed desert fortresses and neutral castles encourage immediate territorial fighting.", RmgPlayerLayoutFamily.Strongholds)
			{
				Tileset = "DESERT", Complexity = TerrainComplexity.High, Water = RmgPlayerParameterLevel.High,
				Surfaces = RmgPlayerParameterLevel.Extreme, Colonies = RmgPlayerColonyDensity.Dense,
				SafeArea = false, PreventOverlap = false, OwnStronghold = true
			},
			new RmgPresetDefinition("castle-hunt", "Castle Hunt", "Start with one colony and capture fortified neutral clusters. Swamp surfaces and smaller castles reward careful expansion.", RmgPlayerLayoutFamily.Strongholds)
			{
				Tileset = "SWAMP", Complexity = TerrainComplexity.High, Surfaces = RmgPlayerParameterLevel.High, Colonies = RmgPlayerColonyDensity.Dense
			},
			new RmgPresetDefinition("twisting-paths", "Twisting Paths", "A dense swamp maze with few colonies, narrow passages and few shortcuts. Wasps colonies are rare; ground skirmishes take priority.", RmgPlayerLayoutFamily.Labyrinth)
			{
				Tileset = "SWAMP", Complexity = TerrainComplexity.Extreme, Water = RmgPlayerParameterLevel.High,
				Surfaces = RmgPlayerParameterLevel.Extreme, Colonies = RmgPlayerColonyDensity.Sparse, OriginalSurfaces = false,
				LaneWidth = RmgBattlefieldLaneWidth.Narrow, ExtraRoutes = RmgLabyrinthRoutes.Few, Weights = new(100, 80, 50, 50, 10)
			},
			new RmgPresetDefinition("great-continents", "Great Continents", "A few broad landmasses with room for inland campaigns. Every island retains a neutral Wasps nest for travel across the sea.", RmgPlayerLayoutFamily.Archipelago)
			{
				Complexity = TerrainComplexity.High, Water = RmgPlayerParameterLevel.Low, Colonies = RmgPlayerColonyDensity.Dense,
				IslandAmount = RmgIslandAmount.Few, IslandSize = RmgIslandSize.Ultra
			},
			new RmgPresetDefinition("island-hopping", "Island Hopping", "Many irregular candy islands and scattered objectives. Each island has a neutral Wasps nest; flying units connect the campaign.", RmgPlayerLayoutFamily.Archipelago)
			{
				Tileset = "CANDY", Complexity = TerrainComplexity.Extreme, Surfaces = RmgPlayerParameterLevel.High,
				IslandAmount = RmgIslandAmount.Many, IslandSize = RmgIslandSize.Large
			},
			new RmgPresetDefinition("scrambled-empires", "Scrambled Empires", "Mixed-biome Chaos with all optional colonies shared equally and owned at random. Mandatory Wasps nests remain neutral. Expect scattered holdings.", RmgPlayerLayoutFamily.Chaos)
			{
				Complexity = TerrainComplexity.High, Water = RmgPlayerParameterLevel.High, Surfaces = RmgPlayerParameterLevel.High,
				Colonies = RmgPlayerColonyDensity.Dense, OriginalSurfaces = false, PreventOverlap = false, SafeArea = false,
				StartingShare = 100, OwnershipMode = RmgColonyOwnershipMode.Random, ChaosScale = RmgChaosScale.Large
			},
			new RmgPresetDefinition("total-mayhem", "Total Mayhem", "Ultra terrain and colonies with Standard water and fractured biomes. Crowded neutral colonies, relaxed spacing and no starting safe areas.", RmgPlayerLayoutFamily.Chaos)
			{
				Complexity = TerrainComplexity.Ultra, Water = RmgPlayerParameterLevel.Standard, Surfaces = RmgPlayerParameterLevel.Ultra,
				Colonies = RmgPlayerColonyDensity.Ultra, OriginalSurfaces = false, PreventOverlap = false, SafeArea = false,
				ChaosScale = RmgChaosScale.Small, ChaosBiomes = RmgChaosBiomes.Fractured
			}
		});
	}
}
