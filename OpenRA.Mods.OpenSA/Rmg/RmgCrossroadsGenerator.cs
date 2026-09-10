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
		sealed record CrossroadsSegment(RmgPoint A, RmgPoint B, bool Primary);

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

			var result = CompleteMirroredRegions(profile, settings, terrain, null, plan);
			var report = result.Map.RegionsReport;
			report["experiment_id"] = "crossroads-v19"; report["identity"] = settings.Canonical(profile);
			report["accessibility_requirement"] = "STARTS_AND_COLONIES_CONNECTED";
			report["characteristic_scale_native"] = settings.MapSize * .36 / Math.Pow(2, BattlefieldDepth(settings.TerrainComplexity) / 2D);
			var metrics = terrain.Report["metrics"];
			var shortfalls = new JObject
			{
				["water_percentage_points"] = Math.Max(0, terrainSettings.WaterPercent - (double)metrics["water_percent_map"]),
				["gravel_percentage_points_land"] = Math.Max(0, terrainSettings.GravelPercent - (double)metrics["gravel_percent_land"]),
				["moss_percentage_points_land"] = Math.Max(0, terrainSettings.MossPercent - (double)metrics["moss_percent_land"])
			};
			plan.Report["terrain_shortfalls"] = shortfalls;
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
			var blank = new RmgLogicalMap(size / 2, size / 2);
			Array.Fill(blank.NativeTerrainIntents, RmgNativeTerrainIntent.Clear);
			var sites = new RegionsSites(Game.ModData, blank, true);
			if (!sites.NativeOrbitFits(null, starts) || starts.SelectMany((p, i) => starts.Skip(i + 1).Select(q => profile.ColonyCombatRules.StartMarginAtNative(p, q))).Any(m => m < 0))
				throw new RmgGenerationRejectedException("CROSSROADS_STARTS", "This size cannot safely fit the Crossroads starting plazas.");
			sites.ReserveNativeOrbit(null, starts);
			var approaches = starts.Select(p => new CrossroadsSegment(p, hub, true)).ToArray();
			var radius = Math.Sqrt(RegionDistanceSquared(anchor, hub));
			var contestRadius = Math.Max(12, radius * .28);
			double Radial(RmgPoint p) => Math.Sqrt((p.X - center) * (p.X - center) + (p.Y - center) * (p.Y - center));
			double ApproachDistance(RmgPoint p) => approaches.Min(s => Math.Sqrt(RegionDistanceSquared(p, ClosestCrossroadsPoint(p, s))));
			var candidates = Enumerable.Range(0, size * size)
				.Where(i => RmgMirroring.Canonical(i, size, settings.MirroringAxes, settings.Seed) == i)
				.Select(i => new RmgPoint(i % size, i / size))
				.Where(p => p.X >= 8 && p.Y >= 8 && p.X < size - 8 && p.Y < size - 8 &&
					(size / 2 - 1 - Math.Min(p.X, size - 1 - p.X)) % 4 == 0 && (size / 2 - 1 - Math.Min(p.Y, size - 1 - p.Y)) % 4 == 0)
				.Select(p => RmgMirroring.Points(p, size, settings.MirroringAxes, settings.Seed)).Where(o => o.Length == settings.PlayerCount)
				.OrderBy(o => Math.Abs(Radial(o[0]) - contestRadius) < 8 ? 0 : Math.Abs(Radial(o[0]) - radius * .76) < 12 ? 1 : 2)
				.ThenBy(o => Math.Abs(ApproachDistance(o[0]) - 14))
				.ThenBy(o => TerrainComparison.Mix(settings.Seed, (ulong)(1901 + o[0].Y * size + o[0].X))).ToArray();
			PlaceMirroredColonies(blank, profile, settings, sites, candidates, starts, out var strict, out var evaluations, out var types);
			var colonies = blank.Actors.ToArray();
			var planningMs = timer.Elapsed.TotalMilliseconds;
			var lanes = approaches.ToList();
			var tiers = (int)settings.SideConnections;
			for (var tier = 0; tier < tiers; tier++)
			{
				var fraction = tier == 0 ? .48 : .74;
				var points = starts.Select(p => new RmgPoint((int)Math.Round(center + (p.X - center) * fraction),
					(int)Math.Round(center + (p.Y - center) * fraction))).ToList();
				if (points.Count == 2)
				{
					var r = radius * fraction;
					points = new[]
					{
						new RmgPoint((int)Math.Round(center - r), hub.Y), new RmgPoint(hub.X, (int)Math.Round(center - r)),
						new RmgPoint((int)Math.Round(center + r), hub.Y), new RmgPoint(hub.X, (int)Math.Round(center + r))
					}.ToList();
				}

				points = points.OrderBy(p => Math.Atan2(p.Y - center, p.X - center)).ToList();
				for (var i = 0; i < points.Count; i++) lanes.Add(new CrossroadsSegment(points[i], points[(i + 1) % points.Count], false));
			}

			foreach (var colony in colonies)
			{
				var point = RmgMirroring.Native(colony);
				var closest = approaches.Select(s => ClosestCrossroadsPoint(point, s)).OrderBy(p => RegionDistanceSquared(point, p)).First();
				lanes.Add(new CrossroadsSegment(point, closest, false));
			}

			var clear = new bool[size * size]; var land = new bool[clear.Length];
			var width = RmgBattlefieldParameters.Width(settings.LaneWidth);
			var junction = Math.Clamp((int)Math.Round(size * .06), 6, 30);
			void Circle(bool[] mask, RmgPoint point, int r)
			{
				for (var y = Math.Max(0, point.Y - r); y <= Math.Min(size - 1, point.Y + r); y++)
					for (var x = Math.Max(0, point.X - r); x <= Math.Min(size - 1, point.X + r); x++)
						if ((x - point.X) * (x - point.X) + (y - point.Y) * (y - point.Y) <= r * r) mask[y * size + x] = true;
			}

			foreach (var segment in lanes)
			{
				var half = segment.Primary ? width / 2 : Math.Max(3, width / 3);
				for (var y = Math.Max(0, Math.Min(segment.A.Y, segment.B.Y) - half); y <= Math.Min(size - 1, Math.Max(segment.A.Y, segment.B.Y) + half); y++)
					for (var x = Math.Max(0, Math.Min(segment.A.X, segment.B.X) - half); x <= Math.Min(size - 1, Math.Max(segment.A.X, segment.B.X) + half); x++)
					{
						var p = new RmgPoint(x, y);
						if (RegionDistanceSquared(p, ClosestCrossroadsPoint(p, segment)) > half * half) continue;
						land[y * size + x] = true;
						if (segment.Primary) clear[y * size + x] = true;
					}
			}

			Circle(clear, hub, junction);
			foreach (var point in starts) Circle(clear, point, 9);
			foreach (var actor in colonies) Circle(clear, RmgMirroring.Native(actor), 5);
			foreach (var p in sites.ReservedCells)
				for (var dy = -2; dy <= 2; dy++)
					for (var dx = -2; dx <= 2; dx++)
						if (p.X + dx >= 0 && p.Y + dy >= 0 && p.X + dx < size && p.Y + dy < size) clear[(p.Y + dy) * size + p.X + dx] = true;
			for (var i = 0; i < land.Length; i++) land[i] |= clear[i];
			foreach (var mask in new[] { clear, land })
			{
				var original = (bool[])mask.Clone();
				for (var i = 0; i < original.Length; i++)
					if (original[i]) foreach (var member in RmgMirroring.Orbit(i, size, settings.MirroringAxes, settings.Seed)) mask[member] = true;
			}

			var fields = CrossroadsFields(settings, starts, clear, land);
			var report = new JObject
			{
				["actor_planning_ms"] = planningMs, ["main_approaches"] = starts.Count, ["junction_radius_native"] = junction,
				["approach_width_native"] = width, ["side_connections"] = RmgCrossroadsParameters.Name(settings.SideConnections),
				["side_connection_tiers"] = tiers, ["subdivision_depth"] = BattlefieldDepth(settings.TerrainComplexity),
				["planned_starts"] = new JArray(starts.Select(p => p.ToString())), ["planned_colony_sites"] = colonies.Length,
				["central_colonies"] = colonies.Count(c => Radial(RmgMirroring.Native(c)) <= contestRadius + 10),
				["central_colony_radius_native"] = contestRadius + 10,
				["protected_clear_cells"] = clear.Count(c => c), ["protected_land_cells"] = land.Count(c => c),
				["placement_order"] = "CENTRAL_CONTEST_START_EXPANSIONS_APPROACH_SHOULDERS",
				["fairness_scope"] = "EQUAL_TERRAIN_TRAVEL_COSTS_TO_TYPED_COLONY_POOLS_BEFORE_OWNERSHIP"
			};
			return new BattlefieldPlan(starts, colonies, strict, evaluations, types, clear, land, fields, report);
		}

		static TerrainComparisonFields CrossroadsFields(RmgGenerationSettings settings, List<RmgPoint> starts, bool[] clear, bool[] land)
		{
			var size = settings.MapSize; var center = (size - 1) / 2D;
			var angles = starts.Select(p => Math.Atan2(p.Y - center, p.X - center)).OrderBy(a => a).ToArray();
			var basins = angles.Select((a, i) => (a + (i + 1 == angles.Length ? angles[0] + 2 * Math.PI : angles[i + 1])) / 2).ToArray();
			var depth = BattlefieldDepth(settings.TerrainComplexity);
			double Priority(double x, double y, bool geology)
			{
				var best = double.NegativeInfinity;
				for (var i = 0; i < basins.Length; i++)
				{
					var angle = basins[i]; var cos = Math.Cos(angle); var sin = Math.Sin(angle);
					var radial = size * (geology ? .26 : .29);
					var u = (x - center) * cos + (y - center) * sin - radial;
					var v = -(x - center) * sin + (y - center) * cos;
					var salt = TerrainComparison.Mix(settings.Seed, (ulong)(1910 + i));
					var halfU = size * .32; var halfV = size * Math.Min(.16, .44 / starts.Count);
					var left = -halfU; var top = -halfV; var w = 2 * halfU; var h = 2 * halfV;
					var leaf = salt;
					for (var d = 0; d < depth; d++)
					{
						var fraction = .46 + (salt >> (d * 2) & 3) * .025;
						if (w >= h)
						{
							var cut = w * fraction;
							if (u < left + cut) { w = cut; leaf = TerrainComparison.Mix(leaf, 1); }
							else { left += cut; w -= cut; leaf = TerrainComparison.Mix(leaf, 2); }
						}
						else
						{
							var cut = h * fraction;
							if (v < top + cut) { h = cut; leaf = TerrainComparison.Mix(leaf, 3); }
							else { top += cut; h -= cut; leaf = TerrainComparison.Mix(leaf, 4); }
						}
					}

					var dx = (u - left - w / 2) / Math.Max(3, w / 2 - 2); var dy = (v - top - h / 2) / Math.Max(3, h / 2 - 2);
					best = Math.Max(best, -Math.Sqrt(dx * dx + dy * dy) + salt % 7 * .025 + (depth == 0 ? 0 : .9 * (leaf % 1024) / 1023D));
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
			var waterAllowed = new bool[water.Length]; var landAllowed = new bool[geology.Length];
			for (var i = 0; i < water.Length; i++)
			{
				var canonical = RmgMirroring.Canonical(i, width, settings.MirroringAxes, settings.Seed);
				water[i] = Priority(2 * (canonical % width) + .5, 2 * (canonical / width) + .5, false);
				waterAllowed[i] = !Protected(land, 2 * (i % width), 2 * (i / width));
			}

			for (var i = 0; i < geology.Length; i++)
			{
				var canonical = RmgMirroring.Canonical(i, lattice, settings.MirroringAxes, settings.Seed);
				var x = 2 * (canonical % lattice) - .5; var y = 2 * (canonical / lattice) - .5;
				geology[i] = Priority(x, y, true); moisture[i] = Priority(x, y, false);
				landAllowed[i] = !Protected(clear, 2 * (i % lattice), 2 * (i / lattice));
			}

			return new TerrainComparisonFields(water, geology, moisture, waterAllowed, landAllowed);
		}
	}
}
