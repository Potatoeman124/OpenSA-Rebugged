#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Newtonsoft.Json.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.OpenSA.Rmg.Reassessment;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public enum RmgLabyrinthRoutes { Few, Standard, Many }

	public static class RmgLabyrinthParameters
	{
		public static string Name(RmgLabyrinthRoutes value) => value.ToString().ToLowerInvariant();
		public static RmgLabyrinthRoutes Parse(string value) => value switch
		{
			"few" => RmgLabyrinthRoutes.Few, "standard" => RmgLabyrinthRoutes.Standard, "many" => RmgLabyrinthRoutes.Many,
			_ => throw new ArgumentException("extra_routes must be few, standard or many.")
		};

		public static void ValidateOptions(RmgPlayerSettings settings)
		{
			if (!Enum.IsDefined(settings.ExtraRoutes) || (!settings.IsLabyrinth && settings.ExtraRoutes != RmgLabyrinthRoutes.Standard))
				throw new ArgumentException("Extra Routes applies only to Labyrinth.");
			if (settings.LayoutFamily == RmgPlayerLayoutFamily.Labyrinth && !settings.IsLabyrinth)
				throw new ArgumentException("Labyrinth requires schema 18.");
		}

		public static void Validate(RmgGenerationSettings settings)
		{
			if (settings.MirroringAxes != 0 || !Enum.IsDefined(settings.LaneWidth) || !Enum.IsDefined(settings.ExtraRoutes) ||
				settings.OwnStartingStronghold || !settings.GenerateCastles || settings.BlockShape != RmgBattlefieldBlockShape.CutCorners ||
				settings.SideConnections != RmgCrossroadsConnections.Standard || settings.LandCrossings != RmgLandCrossings.One || settings.RingShape != RmgRingShape.Round)
				throw new ArgumentException("Labyrinth requires asymmetric starts and valid passage options.");
		}
	}

	public static partial class RmgGenerator
	{
		static RmgGenerationResult GenerateLabyrinth(RmgProfile profile, RmgGenerationSettings settings)
		{
			var size = settings.MapSize; var width = size / 2; var lattice = width + 1; var depth = (int)settings.TerrainComplexity;
			var geometry = new LabyrinthGeometry(settings, profile);
			var water = new double[width * width]; var allowed = new bool[water.Length]; var required = new bool[water.Length];
			for (var i = 0; i < water.Length; i++)
			{
				var x = 2 * (i % width); var y = 2 * (i / width);
				water[i] = geometry.PassageDistance[y * size + x] + (1 + depth * .5) * Math.Sin(x / 9D + settings.Seed % 13) * Math.Cos(y / 11D + settings.Seed % 17);
				allowed[i] = !Enumerable.Range(0, 4).Any(f => geometry.Land[(y + f / 2) * size + x + f % 2]);
				required[i] = allowed[i] && Enumerable.Range(0, 4).Any(f => geometry.Walls[(y + f / 2) * size + x + f % 2]);
			}

			var geology = new double[lattice * lattice]; var moisture = new double[geology.Length]; var landAllowed = new bool[geology.Length];
			for (var i = 0; i < geology.Length; i++)
			{
				var x = 2 * (i % lattice); var y = 2 * (i / lattice);
				geology[i] = Math.Sin(x / 21D + settings.Seed % 23) + Math.Cos(y / 27D + settings.Seed % 19) + (.15 + .2 * depth) * Math.Sin(x / 6D) * Math.Cos(y / 8D);
				moisture[i] = 3 * Math.Cos((x + y) / 17D + settings.Seed % 29) + Math.Sin(y / 13D);
				landAllowed[i] = true;
				for (var py = Math.Max(0, y - 2); py <= Math.Min(size - 1, y + 2); py++)
					for (var px = Math.Max(0, x - 2); px <= Math.Min(size - 1, x + 2); px++) if (geometry.Clear[py * size + px]) landAllowed[i] = false;
			}

			var fields = new TerrainComparisonFields(water, geology, moisture, allowed, landAllowed) { RequiredWater = required };

			// Scale within the space left by the passage network. Absolute map targets would
			// make the upper water settings identical when narrow routes reach capacity.
			var waterFraction = settings.WaterAmount switch
			{
				RmgParameterLevel.Low => .60, RmgParameterLevel.Standard => .72, RmgParameterLevel.High => .82,
				RmgParameterLevel.Extreme => .91, _ => 1D
			};
			var terrainSettings = new TerrainComparisonSettings(settings.Seed, size, TerrainConstruction.Regions, settings.TerrainComplexity)
			{
				Continuity = true, ExtendedComplexity = true, OriginalSurfaceRelations = settings.OriginalSurfaceRelations,
				WaterPercent = (int)Math.Round(100D * allowed.Count(v => v) / allowed.Length * waterFraction),
				GravelPercent = Math.Min(40, profile.RockLandPercentFor(settings.TacticalTerrain) + 6 + depth * 3),
				MossPercent = Math.Min(30, profile.VegetationLandPercentFor(settings.TacticalTerrain) + 5 + depth * 2)
			};
			var terrain = TerrainComparison.Generate(Game.ModData, terrainSettings, fields: fields);
			for (var i = 0; i < geometry.Land.Length; i++)
				if (geometry.Land[i] && TerrainComparison.Native(terrain.Map, i % size, i / size) == RmgNativeTerrainIntent.Water)
					throw new InvalidOperationException("Labyrinth terrain blocked a protected passage.");
			var timer = Stopwatch.StartNew();
			var frozen = TerrainComparison.Hash(TerrainComparison.NativeBytes(terrain.Map));
			var shore = DividedLandsGeometry.WaterDistances(TerrainComparison.NativeBytes(terrain.Map).Select(b => b == 1).ToArray(), size);
			var footprints = profile.NeutralColonyActors.ToDictionary(t => t, t => Game.ModData.DefaultRules.Actors[t].TraitInfos<BuildingInfo>().SelectMany(b => b.OccupiedTiles(CPos.Zero)).ToArray());
			bool FitsShore(string type, RmgPoint p) => type == null || footprints[type].All(o => p.X + o.X >= 0 && p.Y + o.Y >= 0 && p.X + o.X < size && p.Y + o.Y < size && shore[(p.Y + o.Y) * size + p.X + o.X] >= 3);
			var sites = new RegionsSites(Game.ModData, terrain.Map, settings.OriginalSurfaceRelations, FitsShore);
			if (!sites.NativeOrbitFits(null, geometry.Starts)) throw new RmgGenerationRejectedException("LABYRINTH_START_SITES", "A labyrinth starting site does not fit.");
			sites.ReserveNativeOrbit(null, geometry.Starts);
			var blank = new RmgLogicalMap(width, width);

			// Spread the early choices across the seed's alcoves; density extends this fixed
			// order without turning sparse maps into a single cluster of colonies.
			var remaining = geometry.Chambers.ToList(); var spread = new List<RmgPoint>();
			while (remaining.Count > 0)
			{
				var next = remaining.OrderByDescending(p => spread.Count == 0 ? 0 : spread.Min(q => RegionDistanceSquared(p, q))).First();
				spread.Add(next); remaining.Remove(next);
			}

			var candidates = spread.Select(p => new[] { new RmgPoint(p.X - 2, p.Y - 2) }).ToArray();
			PlaceMirroredColonies(blank, profile, settings, sites, candidates, geometry.Starts, out var strict, out var evaluations, out var drawn);
			if (frozen != TerrainComparison.Hash(TerrainComparison.NativeBytes(terrain.Map))) throw new InvalidOperationException("Labyrinth colonies changed terrain.");
			var report = new JObject
			{
				["actor_planning_ms"] = timer.Elapsed.TotalMilliseconds, ["maze_nodes"] = geometry.MazeNodeCount, ["tree_edges"] = geometry.Nodes.Count - 1,
				["extra_edges"] = geometry.ExtraEdgeCount, ["route_length_native"] = geometry.RouteLength,
				["macro_nodes"] = new JArray(geometry.MacroNodes.Select(p => new JArray(p.X, p.Y))),
				["macro_tree"] = new JArray(geometry.MacroTree.Select(e => new JArray(e.A, e.B))),
				["subdivision"] = new JArray(geometry.SubdivisionX, geometry.SubdivisionY), ["extra_routes"] = RmgLabyrinthParameters.Name(settings.ExtraRoutes),
				["passage_width"] = RmgBattlefieldParameters.Name(settings.LaneWidth), ["passage_width_native"] = geometry.HalfWidth * 2,
				["required_wall_logical_cells"] = required.Count(v => v), ["water_space_fraction"] = waterFraction, ["water_available_percent_map"] = 100D * allowed.Count(v => v) / allowed.Length,
				["colony_capacity_sites"] = geometry.Chambers.Count, ["terrain_priority"] = "TERRAIN_BEFORE_COLONIES_NO_REPAINT",
				["nodes"] = new JArray(geometry.Nodes.Select(p => new JArray(p.X, p.Y))),
				["edges"] = new JArray(geometry.Edges.Select(e => new JArray(e.A, e.B))),
				["passages"] = new JArray(geometry.Segments.Select(s => new JArray(s.A.X, s.A.Y, s.B.X, s.B.Y))),
				["chambers"] = new JArray(geometry.Chambers.Select(p => new JArray(p.X, p.Y))), ["minimum_shore_clearance_native"] = 2
			};
			var plan = new BattlefieldPlan(geometry.Starts, blank.Actors.ToArray(), strict, evaluations, drawn, geometry.Clear, geometry.Land, fields, report);
			var result = CompleteRegionsWithPlan(profile, settings, terrain, null, plan);
			result.Map.RegionsReport["experiment_id"] = "labyrinth-v23";
			result.Map.RegionsReport["identity"] = settings.Canonical(profile);
			result.Map.RegionsReport["accessibility_requirement"] = "STARTS_AND_COLONIES_CONNECTED";
			result.Map.RegionsReport["characteristic_scale_native"] = (size - 4D) / Math.Sqrt(geometry.MazeNodeCount);
			result.Map.RegionsReport["preferred_start_reference"] = "fixed-labyrinth-junctions";
			result.Map.RegionsReport["strategic_routes_requirement"] = "CONNECTED_WINDING_PASSAGES";
			var metrics = result.Map.RegionsReport["metrics"];
			if ((double)metrics["water_percent_map"] > terrainSettings.WaterPercent + 2)
				result.Validation.Warnings.Add(new RmgValidationIssue("LABYRINTH_WATER_FLOOR", "Maze walls set minimum water coverage."));
			if ((double)metrics["gravel_percent_land"] + (double)metrics["moss_percent_land"] < terrainSettings.GravelPercent + terrainSettings.MossPercent - 3)
				result.Validation.Warnings.Add(new RmgValidationIssue("LABYRINTH_TERRAIN_CAPACITY", "Passages, alcoves and surface transitions limit modifier coverage."));
			return result;
		}
	}
}
