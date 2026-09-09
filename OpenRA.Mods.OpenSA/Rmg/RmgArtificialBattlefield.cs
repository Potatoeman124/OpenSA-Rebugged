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
			long Evaluations, string[] DrawnTypes, bool[] Clear, TerrainComparisonFields Fields, JObject Report);

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
					throw new InvalidOperationException("Battlefield materialization changed a planned clear lane or colony plaza.");
			var result = CompleteMirroredRegions(profile, settings, terrain, null, plan);
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
				result.Validation.Warnings.Add(new RmgValidationIssue("BATTLEFIELD_TERRAIN_CAPACITY", "Planned clear lanes and colony plazas limit the requested water or surface coverage."));
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
			var pitch = size <= 128 ? 48 : 80 + 8 * (int)(TerrainComparison.Mix(settings.Seed, 1801) % 3);
			var streets = Enumerable.Range(-size / pitch, 2 * (size / pitch) + 1).Select(k => center + k * pitch)
				.Where(c => c >= margin && c <= size - 1 - margin).Concat(new[] { margin, size - 1D - margin }).Distinct().ToArray();
			double StreetDistance(double value) => streets.Min(c => Math.Abs(c - value));
			var candidates = Enumerable.Range(0, size * size)
				.Where(i => RmgMirroring.Canonical(i, size, settings.MirroringAxes, settings.Seed) == i)
				.Select(i => new RmgPoint(i % size, i / size))
				.Where(p => p.X >= 8 && p.Y >= 8 && p.X < size - 8 && p.Y < size - 8 &&
					(size / 2 - 1 - Math.Min(p.X, size - 1 - p.X)) % 8 == 0 && (size / 2 - 1 - Math.Min(p.Y, size - 1 - p.Y)) % 8 == 0)
				.Select(p => RmgMirroring.Points(p, size, settings.MirroringAxes, settings.Seed)).Where(o => o.Length == settings.PlayerCount)
				.OrderBy(o => Math.Abs(Math.Sqrt(starts.Min(s => RegionDistanceSquared(s, o[0]))) - 36) < 12 ? 0 : 1)
				.ThenBy(o => StreetDistance(o[0].X) + StreetDistance(o[0].Y))
				.ThenBy(o => TerrainComparison.Mix(settings.Seed, (ulong)(1802 + o[0].Y * size + o[0].X))).ToArray();
			PlaceMirroredColonies(blank, profile, settings, sites, candidates, starts, out var strict, out var evaluations, out var types);
			var colonies = blank.Actors.ToArray();
			var planningMs = timer.Elapsed.TotalMilliseconds;
			var clear = new bool[size * size];
			var half = RmgBattlefieldParameters.Width(settings.LaneWidth) / 2;
			void Box(int left, int top, int right, int bottom)
			{
				for (var y = Math.Max(0, top); y <= Math.Min(size - 1, bottom); y++)
					for (var x = Math.Max(0, left); x <= Math.Min(size - 1, right); x++) clear[y * size + x] = true;
			}

			foreach (var street in streets)
			{
				Box((int)Math.Ceiling(street - half), 0, (int)Math.Floor(street + half), size - 1);
				Box(0, (int)Math.Ceiling(street - half), size - 1, (int)Math.Floor(street + half));
			}

			foreach (var point in starts.Concat(colonies.Select(RmgMirroring.Native)))
			{
				var radius = half + (starts.Contains(point) ? 8 : 3);
				Box(point.X - radius, point.Y - radius, point.X + radius, point.Y + radius);
				var x = (int)Math.Round(streets.OrderBy(c => Math.Abs(c - point.X)).First());
				var y = (int)Math.Round(streets.OrderBy(c => Math.Abs(c - point.Y)).First());
				Box(Math.Min(x, point.X), point.Y - half, Math.Max(x, point.X), point.Y + half);
				Box(point.X - half, Math.Min(y, point.Y), point.X + half, Math.Max(y, point.Y));
			}

			// Include actual species footprints/exits and mirror the union, because actor
			// artwork/footprint orientation itself is not rotated by OpenRA.
			foreach (var cell in sites.ReservedCells) Box(cell.X - 2, cell.Y - 2, cell.X + 2, cell.Y + 2);
			var original = (bool[])clear.Clone();
			for (var i = 0; i < clear.Length; i++)
				if (original[i]) foreach (var member in RmgMirroring.Orbit(i, size, settings.MirroringAxes, settings.Seed)) clear[member] = true;
			var fields = BattlefieldFields(settings, clear, pitch);
			var report = new JObject
			{
				["actor_planning_ms"] = planningMs,
				["block_shape"] = RmgBattlefieldParameters.Name(settings.BlockShape), ["lane_width"] = RmgBattlefieldParameters.Name(settings.LaneWidth),
				["lane_width_native"] = 2 * half, ["district_pitch_native"] = pitch,
				["planned_starts"] = new JArray(starts.Select(p => p.ToString())), ["planned_colony_sites"] = colonies.Length,
				["planned_site_candidates"] = candidates.Length, ["protected_clear_cells"] = clear.Count(c => c),
				["placement_order"] = "PLAYER_PLAZAS_COLONIES_LANES_THEN_TERRAIN",
				["fairness_scope"] = "EQUAL_TERRAIN_TRAVEL_COSTS_TO_TYPED_COLONY_POOLS_BEFORE_OWNERSHIP"
			};
			return new BattlefieldPlan(starts, colonies, strict, evaluations, types, clear, fields, report);
		}

		static TerrainComparisonFields BattlefieldFields(RmgGenerationSettings settings, bool[] clear, int pitch)
		{
			var size = settings.MapSize;
			var width = size / 2;
			var center = (size - 1) / 2D;
			var detail = settings.TerrainComplexity switch
			{
				TerrainComplexity.Low => 0D, TerrainComplexity.Standard => .35, TerrainComplexity.High => .7,
				TerrainComplexity.Extreme => 1.2, _ => 2D
			};
			double Norm(double x, double y) => settings.BlockShape switch
			{
				RmgBattlefieldBlockShape.Rectangles => Math.Max(Math.Abs(x), Math.Abs(y)),
				RmgBattlefieldBlockShape.Diamonds => Math.Abs(x) + Math.Abs(y),
				_ => Math.Max(Math.Max(Math.Abs(x), Math.Abs(y)), .72 * (Math.Abs(x) + Math.Abs(y)))
			};
			double Priority(double x, double y, bool geology)
			{
				var bx = (int)Math.Floor((x - center) / pitch); var by = (int)Math.Floor((y - center) / pitch);
				var dx = (x - center) / pitch - bx - .5; var dy = (y - center) / pitch - by - .5;
				var salt = TerrainComparison.Mix(settings.Seed, (ulong)(1803 + (bx + 32) * 128 + by + 32));
				var radius = .29 + salt % 5 * .025;
				var broad = Norm(dx / radius, dy / radius);
				var fine = Norm((Math.Abs(dx) - .20) / .13, (Math.Abs(dy) - .20) / .13);
				var district = salt % 3 == 0;
				return -broad + (geology ? (district ? -1.0 : .5) : (district ? 1.2 : 0)) + detail * Math.Max(-1.5, 1 - fine) + salt % 7 * .045;
			}

			bool Protected(int x, int y, int radius)
			{
				for (var py = Math.Max(0, y - radius); py <= Math.Min(size - 1, y + radius); py++)
					for (var px = Math.Max(0, x - radius); px <= Math.Min(size - 1, x + radius); px++)
						if (clear[py * size + px]) return true;
				return false;
			}

			var water = new double[width * width]; var waterAllowed = new bool[water.Length];
			var lattice = width + 1; var geology = new double[lattice * lattice]; var moisture = new double[geology.Length]; var landAllowed = new bool[geology.Length];
			for (var i = 0; i < water.Length; i++)
			{
				var canonical = RmgMirroring.Canonical(i, width, settings.MirroringAxes, settings.Seed);
				water[i] = Priority(2 * (canonical % width) + .5, 2 * (canonical / width) + .5, false);
				waterAllowed[i] = !Protected(2 * (i % width), 2 * (i / width), 2);
			}

			for (var i = 0; i < geology.Length; i++)
			{
				var canonical = RmgMirroring.Canonical(i, lattice, settings.MirroringAxes, settings.Seed);
				var x = 2 * (canonical % lattice) - .5; var y = 2 * (canonical / lattice) - .5;
				geology[i] = Priority(x, y, true); moisture[i] = Priority(x + pitch / 3D, y + pitch / 3D, true);
				landAllowed[i] = !Protected(2 * (i % lattice), 2 * (i / lattice), 2);
			}

			return new TerrainComparisonFields(water, geology, moisture, waterAllowed, landAllowed);
		}
	}
}
