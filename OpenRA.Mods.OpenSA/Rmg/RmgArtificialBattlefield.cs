#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Newtonsoft.Json.Linq;
using OpenRA.Mods.OpenSA.Rmg.Reassessment;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public static partial class RmgGenerator
	{
		sealed record BattlefieldPlan(List<RmgPoint> Starts, RmgActorPlan[] Colonies, int StrictCount,
			long Evaluations, string[] DrawnTypes, bool[] Clear, bool[] Land, TerrainComparisonFields Fields, JObject Report);

		static RmgGenerationResult GenerateArtificialBattlefield(RmgProfile profile, RmgGenerationSettings settings)
		{
			var plan = PlanBattlefield(profile, settings);
			var terrainSettings = new TerrainComparisonSettings(settings.Seed, settings.MapSize, TerrainConstruction.Regions, settings.TerrainComplexity)
			{
				Continuity = true, ExtendedComplexity = true, MirroringAxes = settings.MirroringAxes,
				WaterPercent = RegionsWaterPercent(settings.WaterAmount), GravelPercent = profile.RockLandPercentFor(settings.TacticalTerrain),
				MossPercent = profile.VegetationLandPercentFor(settings.TacticalTerrain), OriginalSurfaceRelations = settings.OriginalSurfaceRelations
			};
			var terrain = TerrainComparison.Generate(Game.ModData, terrainSettings, fields: plan.Fields);
			for (var i = 0; i < plan.Clear.Length; i++)
				if (plan.Clear[i] && TerrainComparison.Native(terrain.Map, i % settings.MapSize, i / settings.MapSize) != RmgNativeTerrainIntent.Clear)
					throw new InvalidOperationException("Battlefield materialization changed a planned colony plaza.");
			for (var i = 0; i < plan.Land.Length; i++)
				if (plan.Land[i] && TerrainComparison.Native(terrain.Map, i % settings.MapSize, i / settings.MapSize) == RmgNativeTerrainIntent.Water)
					throw new InvalidOperationException("Battlefield materialization blocked a planned ground route.");
			var result = CompleteRegionsWithPlan(profile, settings, terrain, null, plan);
			result.Map.RegionsReport["characteristic_scale_native"] = plan.Report["district_pitch_native"];
			result.Map.RegionsReport["experiment_id"] = "artificial-battlefield-v18";
			result.Map.RegionsReport["identity"] = settings.Canonical(profile);
			result.Map.RegionsReport["accessibility_requirement"] = "STARTS_AND_COLONIES_CONNECTED";
			var metrics = terrain.Report["metrics"];
			var shortfalls = new JObject
			{
				["water_percentage_points"] = Math.Max(0, terrainSettings.WaterPercent - (double)metrics["water_percent_map"]),
				["gravel_percentage_points_land"] = Math.Max(0, terrainSettings.GravelPercent - (double)metrics["gravel_percent_land"]),
				["moss_percentage_points_land"] = Math.Max(0, terrainSettings.MossPercent - (double)metrics["moss_percent_land"])
			};
			plan.Report["terrain_shortfalls"] = shortfalls;
			if (shortfalls.Properties().Any(p => (double)p.Value > 2))
				result.Validation.Warnings.Add(new RmgValidationIssue("BATTLEFIELD_TERRAIN_CAPACITY", "Ground routes, colony plazas and surface transitions limit the requested terrain coverage."));
			return result;
		}

		static BattlefieldPlan PlanBattlefield(RmgProfile profile, RmgGenerationSettings settings)
		{
			var timer = Stopwatch.StartNew();
			var size = settings.MapSize;
			var center = (size - 1) / 2D;
			var margin = size == 64 ? 10 : 16;
			var anchor = settings.PlayerCount switch
			{
				2 => (settings.Seed & 1) == 0 ? new RmgPoint(margin, size / 2 - 1) : new RmgPoint(size / 2 - 1, margin),
				4 => new RmgPoint(margin, margin),
				_ => new RmgPoint(margin, (int)Math.Round(center - (center - margin) * (Math.Sqrt(2) - 1)))
			};
			var starts = RmgMirroring.Points(anchor, size, settings.MirroringAxes, settings.Seed).ToList();
			var blank = new RmgLogicalMap(size / 2, size / 2);
			Array.Fill(blank.NativeTerrainIntents, RmgNativeTerrainIntent.Clear);
			var sites = new RegionsSites(Game.ModData, blank, true);
			if (!sites.NativeOrbitFits(null, starts) || starts.SelectMany((p, i) => starts.Skip(i + 1).Select(q => profile.ColonyCombatRules.StartMarginAtNative(p, q))).Any(m => m < 0))
				throw new RmgGenerationRejectedException("BATTLEFIELD_STARTS", "This map size cannot safely fit the planned player plazas.");
			sites.ReserveNativeOrbit(null, starts);
			var pitch = size <= 128 ? 48 : 88 + 8 * (int)(TerrainComparison.Mix(settings.Seed, 1801) % 3);
			var candidates = OrderBattlefieldSites(settings, starts);
			PlaceMirroredColonies(blank, profile, settings, sites, candidates, starts, out var strict, out var evaluations, out var types);
			var colonies = blank.Actors.ToArray();
			var planningMs = timer.Elapsed.TotalMilliseconds;
			var (clear, land, routes) = BattlefieldLanes(settings, starts, colonies, sites);
			var fields = BattlefieldFields(settings, clear, land, pitch);
			var report = new JObject
			{
				["actor_planning_ms"] = planningMs,
				["block_shape"] = RmgBattlefieldParameters.Name(settings.BlockShape), ["lane_width"] = RmgBattlefieldParameters.Name(settings.LaneWidth),
				["lane_width_native"] = RmgBattlefieldParameters.Width(settings.LaneWidth), ["district_pitch_native"] = pitch,
				["subdivision_depth"] = BattlefieldDepth(settings.TerrainComplexity),
				["blocks_per_district"] = 1 << BattlefieldDepth(settings.TerrainComplexity),
				["planned_starts"] = new JArray(starts.Select(p => p.ToString())), ["planned_colony_sites"] = colonies.Length,
				["planned_site_candidates"] = candidates.Length, ["protected_clear_cells"] = clear.Count(c => c),
				["protected_land_cells"] = land.Count(c => c),
				["placement_order"] = "DISTRIBUTED_OBJECTIVES_CONNECTED_ROUTES_SUBDIVIDED_BLOCKS",
				["routes"] = routes, ["construction_revision"] = 2,
				["fairness_scope"] = "EQUAL_TERRAIN_TRAVEL_COSTS_TO_TYPED_COLONY_POOLS_BEFORE_OWNERSHIP"
			};
			return new BattlefieldPlan(starts, colonies, strict, evaluations, types, clear, land, fields, report);
		}

		static TerrainComparisonFields BattlefieldFields(RmgGenerationSettings settings, bool[] clear, bool[] land, int pitch)
		{
			var size = settings.MapSize;
			var width = size / 2;
			var center = (size - 1) / 2D;
			var depth = BattlefieldDepth(settings.TerrainComplexity);
			double Priority(double x, double y, bool geology)
			{
				var bx = (int)Math.Floor((x - center) / pitch); var by = (int)Math.Floor((y - center) / pitch);
				var salt = TerrainComparison.Mix(settings.Seed, (ulong)(1803 + (bx + 32) * 128 + by + 32));
				var left = center + bx * pitch; var top = center + by * pitch;
				double w = pitch, h = pitch;

				// All levels refine the same seed-fixed districts. Cuts alternate direction;
				// their positions are independent of the selected final subdivision depth.
				for (var d = 0; d < depth; d++)
				{
					var fraction = .44 + (salt >> (d * 3) & 3) * .04;
					if ((d + (int)(salt & 1)) % 2 == 0)
					{
						var cut = w * fraction;
						if (x < left + cut) w = cut;
						else { left += cut; w -= cut; }
					}
					else
					{
						var cut = h * fraction;
						if (y < top + cut) h = cut;
						else { top += cut; h -= cut; }
					}
				}

				var dx = (x - left - w / 2) / Math.Max(3, w / 2 - 2);
				var dy = (y - top - h / 2) / Math.Max(3, h / 2 - 2);
				var norm = settings.BlockShape switch
				{
					RmgBattlefieldBlockShape.Rectangles => Math.Max(Math.Abs(dx), Math.Abs(dy)),
					RmgBattlefieldBlockShape.Diamonds => (Math.Abs(dx) + Math.Abs(dy)) / Math.Sqrt(2),
					_ => Math.Max(Math.Max(Math.Abs(dx), Math.Abs(dy)), .70 * (Math.Abs(dx) + Math.Abs(dy)))
				};
				var district = salt % 3 == 0;
				return -norm + (geology ? (district ? -.3 : .3) : (district ? .3 : 0)) + salt % 7 * .025;
			}

			bool Protected(bool[] mask, int x, int y, int radius)
			{
				for (var py = Math.Max(0, y - radius); py <= Math.Min(size - 1, y + radius); py++)
					for (var px = Math.Max(0, x - radius); px <= Math.Min(size - 1, x + radius); px++)
						if (mask[py * size + px]) return true;
				return false;
			}

			var water = new double[width * width]; var waterAllowed = new bool[water.Length];
			var lattice = width + 1; var geology = new double[lattice * lattice]; var moisture = new double[geology.Length]; var landAllowed = new bool[geology.Length];
			for (var i = 0; i < water.Length; i++)
			{
				var canonical = RmgMirroring.Canonical(i, width, settings.MirroringAxes, settings.Seed);
				water[i] = Priority(2 * (canonical % width) + .5, 2 * (canonical / width) + .5, false);
				waterAllowed[i] = !Protected(land, 2 * (i % width), 2 * (i / width), 2);
			}

			for (var i = 0; i < geology.Length; i++)
			{
				var canonical = RmgMirroring.Canonical(i, lattice, settings.MirroringAxes, settings.Seed);
				var x = 2 * (canonical % lattice) - .5; var y = 2 * (canonical / lattice) - .5;
				geology[i] = Priority(x, y, true); moisture[i] = Priority(x + pitch / 3D, y + pitch / 3D, true);
				landAllowed[i] = !Protected(clear, 2 * (i % lattice), 2 * (i / lattice), 2);
			}

			return new TerrainComparisonFields(water, geology, moisture, waterAllowed, landAllowed);
		}
	}
}
