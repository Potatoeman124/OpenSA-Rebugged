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
		sealed record CrossroadsSegment(RmgPoint A, RmgPoint B);

		static RmgGenerationResult GenerateCrossroads(RmgProfile profile, RmgGenerationSettings settings)
		{
			var plan = PlanCrossroads(profile, settings);
			var terrainSettings = new TerrainComparisonSettings(settings.Seed, settings.MapSize, TerrainConstruction.Regions, settings.TerrainComplexity)
			{
				Continuity = true, ExtendedComplexity = true, MirroringAxes = settings.MirroringAxes,
				WaterPercent = RegionsWaterPercent(settings.WaterAmount), GravelPercent = profile.RockLandPercentFor(settings.TacticalTerrain),
				MossPercent = profile.VegetationLandPercentFor(settings.TacticalTerrain), OriginalSurfaceRelations = settings.OriginalSurfaceRelations
			};
			var terrain = TerrainComparison.Generate(Game.ModData, terrainSettings, fields: plan.Fields);
			for (var i = 0; i < plan.Clear.Length; i++)
			{
				var cell = TerrainComparison.Native(terrain.Map, i % settings.MapSize, i / settings.MapSize);
				if ((plan.Clear[i] && cell != RmgNativeTerrainIntent.Clear) || (plan.Land[i] && cell == RmgNativeTerrainIntent.Water))
					throw new InvalidOperationException("Crossroads terrain changed a protected approach, junction or colony site.");
			}

			plan.Report["topology"] = CrossroadsTopology.Validate(terrain.Map, settings, plan.Starts, plan.Report);
			var result = CompleteRegionsWithPlan(profile, settings, terrain, null, plan);
			var report = result.Map.RegionsReport;
			report["experiment_id"] = "crossroads-v19"; report["identity"] = settings.Canonical(profile);
			report["accessibility_requirement"] = "STARTS_AND_COLONIES_CONNECTED";
			report["characteristic_scale_native"] = settings.MapSize / (1D + BattlefieldDepth(settings.TerrainComplexity));
			var metrics = terrain.Report["metrics"];
			var shortfalls = new JObject
			{
				["water_percentage_points"] = Math.Max(0, terrainSettings.WaterPercent - (double)metrics["water_percent_map"]),
				["gravel_percentage_points_land"] = Math.Max(0, terrainSettings.GravelPercent - (double)metrics["gravel_percent_land"]),
				["moss_percentage_points_land"] = Math.Max(0, terrainSettings.MossPercent - (double)metrics["moss_percent_land"])
			};
			plan.Report["terrain_shortfalls"] = shortfalls;
			if ((double)metrics["water_percent_map"] > terrainSettings.WaterPercent + 2)
				result.Validation.Warnings.Add(new RmgValidationIssue("CROSSROADS_WATER_FLOOR", "Continuous dividers require more water than the selected target at this map size."));
			if (shortfalls.Properties().Any(p => (double)p.Value > 2))
				result.Validation.Warnings.Add(new RmgValidationIssue("CROSSROADS_TERRAIN_CAPACITY", "Approaches, colony plazas and surface transitions limit the requested terrain coverage."));
			return result;
		}

		static RmgPoint ClosestCrossroadsPoint(RmgPoint p, CrossroadsSegment segment)
		{
			var dx = segment.B.X - segment.A.X; var dy = segment.B.Y - segment.A.Y;
			var t = Math.Clamp(((p.X - segment.A.X) * dx + (p.Y - segment.A.Y) * dy) / (double)Math.Max(1, dx * dx + dy * dy), 0, 1);
			return new RmgPoint((int)Math.Round(segment.A.X + t * dx), (int)Math.Round(segment.A.Y + t * dy));
		}

		static int CrossroadsWidth(RmgGenerationSettings settings) => settings.MapSize == 64 ?
			4 + 2 * (int)settings.LaneWidth : settings.MapSize == 128 && settings.PlayerCount == 8 ?
			6 + 3 * (int)settings.LaneWidth : RmgBattlefieldParameters.Width(settings.LaneWidth);

		static double[] CrossroadsDividers(List<RmgPoint> starts, double center)
		{
			var angles = starts.Select(p => Math.Atan2(p.Y - center, p.X - center)).OrderBy(a => a).ToArray();
			return angles.Select((a, i) => (a + (i + 1 == angles.Length ? angles[0] + 2 * Math.PI : angles[i + 1])) / 2).ToArray();
		}

		static BattlefieldPlan PlanCrossroads(RmgProfile profile, RmgGenerationSettings settings)
		{
			var timer = Stopwatch.StartNew();
			var size = settings.MapSize; var center = (size - 1) / 2D;
			var hub = new RmgPoint(size / 2 - 1, size / 2 - 1);
			var margin = size == 64 ? 10 : 16;
			var anchor = settings.PlayerCount switch
			{
				2 => (settings.Seed & 1) == 0 ? new RmgPoint(margin, hub.Y) : new RmgPoint(hub.X, margin),
				4 => new RmgPoint(margin, margin),
				_ => new RmgPoint(margin, (int)Math.Round(center - (center - margin) * (Math.Sqrt(2) - 1)))
			};
			var starts = RmgMirroring.Points(anchor, size, settings.MirroringAxes, settings.Seed).ToList();
			var approaches = starts.Select(p => new CrossroadsSegment(p, hub)).ToArray();
			var dividers = CrossroadsDividers(starts, center);
			double Radial(RmgPoint p) => Math.Sqrt((p.X - center) * (p.X - center) + (p.Y - center) * (p.Y - center));
			double ApproachDistance(RmgPoint p) => approaches.Min(s => Math.Sqrt(RegionDistanceSquared(p, ClosestCrossroadsPoint(p, s))));
			double DividerDistance(RmgPoint p) => dividers.Min(a => (p.X - center) * Math.Cos(a) + (p.Y - center) * Math.Sin(a) < 0 ?
				Radial(p) : Math.Abs(-(p.X - center) * Math.Sin(a) + (p.Y - center) * Math.Cos(a)));
			var width = CrossroadsWidth(settings);
			var junction = Math.Clamp((int)Math.Round(size * .06), 6, 30);
			var tiers = (int)settings.SideConnections;
			var tierRadii = (size == 128 && settings.PlayerCount == 8 ? new[] { size * .28, size * .425 } :
				new[] { size * .25, size * .40 }).Take(tiers).ToArray();
			var bridgeHalf = size == 64 ? 1.5 : 3D;
			var clear = new bool[size * size]; var land = new bool[clear.Length];
			var placement = new bool[clear.Length];
			var siteBand = Math.Clamp(size * .10, 10, 46);
			for (var i = 0; i < clear.Length; i++)
			{
				var p = new RmgPoint(i % size, i / size);
				var distance = ApproachDistance(p);
				var squareRadius = Math.Max(Math.Abs(p.X - center), Math.Abs(p.Y - center));
				clear[i] = distance <= width / 2D || Radial(p) <= junction || starts.Any(q => RegionDistanceSquared(p, q) <= 81);

				// These fixed land strips exist at every density. Colonies cannot reserve cells in a divider.
				placement[i] = distance <= siteBand && DividerDistance(p) >= (size == 128 && settings.PlayerCount == 8 ? 4 : size <= 128 ? 6 : 9);
				land[i] = clear[i] || placement[i] || tierRadii.Any(r => Math.Abs(squareRadius - r) <= bridgeHalf);
			}

			// The planning terrain contains only the designated colony strips. Test real actor footprints/exits.
			var blank = new RmgLogicalMap(size / 2, size / 2);
			Array.Fill(blank.NativeTerrainIntents, RmgNativeTerrainIntent.Clear);
			var sites = new RegionsSites(Game.ModData, blank, true);
			if (!sites.NativeOrbitFits(null, starts) || starts.SelectMany((p, i) => starts.Skip(i + 1).Select(q => profile.ColonyCombatRules.StartMarginAtNative(p, q))).Any(m => m < 0))
				throw new RmgGenerationRejectedException("CROSSROADS_STARTS", "This size cannot safely fit the Crossroads starting plazas.");
			sites.ReserveNativeOrbit(null, starts);
			var startingCells = sites.ReservedCells.ToArray();
			for (var y = 0; y < size; y++)
				for (var x = 0; x < size; x++)
					blank.NativeTerrainIntents[4 * (y / 2 * (size / 2) + x / 2) + 2 * (y % 2) + x % 2] =
						placement[y * size + x] && (size < 256 || ApproachDistance(new RmgPoint(x, y)) > 3) ?
						RmgNativeTerrainIntent.Clear : RmgNativeTerrainIntent.Water;
			var radius = Math.Sqrt(RegionDistanceSquared(anchor, hub));
			var contestRadius = Math.Max(12, radius * .28);
			var candidates = Enumerable.Range(0, size * size)
				.Where(i => RmgMirroring.Canonical(i, size, settings.MirroringAxes, settings.Seed) == i)
				.Select(i => new RmgPoint(i % size, i / size))
				.Where(p => p.X >= 8 && p.Y >= 8 && p.X < size - 8 && p.Y < size - 8 &&
					(size / 2 - 1 - Math.Min(p.X, size - 1 - p.X)) % 4 == 0 && (size / 2 - 1 - Math.Min(p.Y, size - 1 - p.Y)) % 4 == 0)
				.Select(p => RmgMirroring.Points(p, size, settings.MirroringAxes, settings.Seed)).Where(o => o.Length == settings.PlayerCount)
				.OrderBy(o => Math.Abs(Radial(o[0]) - contestRadius) < 8 ? 0 : Math.Abs(Radial(o[0]) - radius * .76) < 12 ? 1 : 2)
				.ThenBy(o => Math.Abs(ApproachDistance(o[0]) - 16))
				.ThenBy(o => TerrainComparison.Mix(settings.Seed, (ulong)(1901 + o[0].Y * size + o[0].X))).ToArray();
			PlaceMirroredColonies(blank, profile, settings, sites, candidates, starts, out var strict, out var evaluations, out var types);
			var colonies = blank.Actors.ToArray();

			// Only small local clear pads depend on colony density; water reservations never do.
			foreach (var p in settings.OriginalSurfaceRelations ? sites.ReservedCells : startingCells) clear[p.Y * size + p.X] = true;
			foreach (var mask in new[] { clear, land })
			{
				var original = (bool[])mask.Clone();
				for (var i = 0; i < original.Length; i++)
					if (original[i]) foreach (var member in RmgMirroring.Orbit(i, size, settings.MirroringAxes, settings.Seed)) mask[member] = true;
			}

			var fields = CrossroadsFields(settings, dividers, clear, land);
			var report = new JObject
			{
				["actor_planning_ms"] = timer.Elapsed.TotalMilliseconds, ["main_approaches"] = starts.Count, ["junction_radius_native"] = junction,
				["approach_width_native"] = width, ["side_connections"] = RmgCrossroadsParameters.Name(settings.SideConnections),
				["side_connection_tiers"] = tiers, ["side_connection_radii_native"] = new JArray(tierRadii), ["bridge_half_width_native"] = bridgeHalf,
				["divider_detail_level"] = BattlefieldDepth(settings.TerrainComplexity), ["divider_angles"] = new JArray(dividers),
				["planned_starts"] = new JArray(starts.Select(p => p.ToString())), ["planned_colony_sites"] = colonies.Length,
				["central_colonies"] = colonies.Count(c => Radial(RmgMirroring.Native(c)) <= radius * .55),
				["central_colony_radius_native"] = radius * .55,
				["protected_clear_cells"] = clear.Count(c => c), ["protected_land_cells"] = land.Count(c => c),
				["placement_order"] = "FIXED_APPROACH_SHOULDERS", ["density_changes_water"] = false,
				["fairness_scope"] = "EQUAL_TERRAIN_TRAVEL_COSTS_TO_TYPED_COLONY_POOLS_BEFORE_OWNERSHIP"
			};
			return new BattlefieldPlan(starts, colonies, strict, evaluations, types, clear, land, fields, report);
		}

		static TerrainComparisonFields CrossroadsFields(RmgGenerationSettings settings, double[] dividers, bool[] clear, bool[] land)
		{
			var size = settings.MapSize; var center = (size - 1) / 2D;
			var depth = BattlefieldDepth(settings.TerrainComplexity);
			double Priority(double x, double y, bool geology)
			{
				var best = double.NegativeInfinity;
				for (var i = 0; i < dividers.Length; i++)
				{
					var angle = dividers[i]; var cos = Math.Cos(angle); var sin = Math.Sin(angle);
					var u = (x - center) * cos + (y - center) * sin;
					if (u < 0) continue;
					var v = Math.Abs(-(x - center) * sin + (y - center) * cos);
					var salt = TerrainComparison.Mix(settings.Seed, (ulong)(1910 + i));
					var phase = salt % 1024 / 1024D * Math.PI;

					// Vary the width of a continuous divider; complexity must never open a crossing.
					var variation = 1 + (.08 + depth * .16) * Math.Cos(u / size * Math.PI * (2 + depth * 2) + phase);
					best = Math.Max(best, -v / variation + (geology ? Math.Sin(u / size * 12 + phase) * 3 : 0));
				}

				return best;
			}

			bool Protected(bool[] mask, int x, int y)
			{
				for (var py = Math.Max(0, y - 2); py <= Math.Min(size - 1, y + 2); py++)
					for (var px = Math.Max(0, x - 2); px <= Math.Min(size - 1, x + 2); px++) if (mask[py * size + px]) return true;
				return false;
			}

			var width = size / 2; var lattice = width + 1;
			var water = new double[width * width]; var geology = new double[lattice * lattice]; var moisture = new double[geology.Length];
			var waterAllowed = new bool[water.Length]; var required = new bool[water.Length]; var landAllowed = new bool[geology.Length];
			for (var i = 0; i < water.Length; i++)
			{
				var canonical = RmgMirroring.Canonical(i, width, settings.MirroringAxes, settings.Seed);
				var x = 2 * (canonical % width) + .5; var y = 2 * (canonical / width) + .5;
				water[i] = Priority(x, y, false);
				waterAllowed[i] = !Enumerable.Range(0, 4).Any(f => land[(2 * (i / width) + f / 2) * size + 2 * (i % width) + f % 2]);
				required[i] = waterAllowed[i] && dividers.Any(a => (x - center) * Math.Cos(a) + (y - center) * Math.Sin(a) >= 0 &&
					Math.Abs(-(x - center) * Math.Sin(a) + (y - center) * Math.Cos(a)) <= (size == 64 || (size == 128 && settings.PlayerCount == 8) ? 3 : 5));
				if (required[i]) water[i] += 10000;
			}

			for (var i = 0; i < required.Length; i++)
				if (!RmgMirroring.Orbit(i, width, settings.MirroringAxes, settings.Seed).All(m => waterAllowed[m])) required[i] = false;

			for (var i = 0; i < geology.Length; i++)
			{
				var canonical = RmgMirroring.Canonical(i, lattice, settings.MirroringAxes, settings.Seed);
				var x = 2 * (canonical % lattice) - .5; var y = 2 * (canonical / lattice) - .5;
				geology[i] = Priority(x, y, true); moisture[i] = Priority(x, y, false);
				landAllowed[i] = !Protected(clear, 2 * (i % lattice), 2 * (i / lattice));
			}

			return new TerrainComparisonFields(water, geology, moisture, waterAllowed, landAllowed) { RequiredWater = required };
		}
	}
}
