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

	sealed class LabyrinthGeometry
	{
		public readonly List<RmgPoint> Nodes = new();
		public readonly List<RmgPoint> Starts = new();
		public readonly List<RmgPoint> Chambers = new();
		public readonly List<(int A, int B)> Edges = new();
		public readonly List<(RmgPoint A, RmgPoint B)> Segments = new();
		public readonly double[] PassageDistance;
		public readonly bool[] Clear, Land;
		public readonly double HalfWidth;
		public readonly int ExtraEdgeCount;

		public LabyrinthGeometry(RmgGenerationSettings settings, RmgProfile profile)
		{
			var size = settings.MapSize; var depth = (int)settings.TerrainComplexity;
			var count = size == 64 ? 3 : size == 128 ? 5 : size == 256 ? 11 : 22;
			var pitch = (size - 24D) / (count - 1);
			var random = new DeterministicRandom(TerrainComparison.Mix(settings.Seed, 2300));
			for (var y = 0; y < count; y++)
				for (var x = 0; x < count; x++)
					Nodes.Add(new RmgPoint((int)Math.Round(12 + x * pitch) + random.NextInt(3) - 1,
						(int)Math.Round(12 + y * pitch) + random.NextInt(3) - 1));
			var selected = new List<int>();
			for (var attempt = 0; attempt < Nodes.Count; attempt++)
			{
				selected.Clear();
				var order = Enumerable.Range(0, Nodes.Count).OrderBy(i => TerrainComparison.Mix(settings.Seed, (ulong)(2301 + i + attempt * Nodes.Count))).ToArray();
				foreach (var player in Enumerable.Range(0, settings.PlayerCount))
				{
					var candidate = order.Where(i => selected.All(j => profile.ColonyCombatRules.StartMarginAtNative(Nodes[i], Nodes[j]) >= 0))
						.OrderByDescending(i => selected.Count == 0 ? 0 : selected.Min(j => SquareDistance(Nodes[i], Nodes[j]))).Take(1).ToArray();
					if (candidate.Length == 0) break;
					selected.Add(candidate[0]);
				}

				if (selected.Count == settings.PlayerCount) break;
			}

			if (selected.Count != settings.PlayerCount) throw new RmgGenerationRejectedException("LABYRINTH_STARTS", "This size cannot fit safe labyrinth starts.");
			Starts.AddRange(selected.Select(i => new RmgPoint(Nodes[i].X - 2, Nodes[i].Y - 2)));
			var visited = new bool[Nodes.Count]; var stack = new Stack<int>(); stack.Push(random.NextInt(Nodes.Count)); visited[stack.Peek()] = true;
			IEnumerable<int> Neighbors(int i)
			{
				if (i % count > 0) yield return i - 1;
				if (i % count < count - 1) yield return i + 1;
				if (i >= count) yield return i - count;
				if (i < Nodes.Count - count) yield return i + count;
			}

			while (stack.Count > 0)
			{
				var i = stack.Peek(); var neighbors = Neighbors(i).Where(j => !visited[j]).ToArray();
				if (neighbors.Length == 0) { stack.Pop(); continue; }
				var next = neighbors[random.NextInt(neighbors.Length)]; Edges.Add((Math.Min(i, next), Math.Max(i, next))); visited[next] = true; stack.Push(next);
			}

			var used = Edges.ToHashSet();
			var closed = Enumerable.Range(0, Nodes.Count).SelectMany(i => Neighbors(i).Where(j => j > i && !used.Contains((i, j))).Select(j => (A: i, B: j)))
				.OrderBy(e => TerrainComparison.Mix(settings.Seed, (ulong)(2310 + e.A * Nodes.Count + e.B))).ToArray();
			ExtraEdgeCount = (int)Math.Round(closed.Length * (settings.ExtraRoutes == RmgLabyrinthRoutes.Few ? .03 : settings.ExtraRoutes == RmgLabyrinthRoutes.Many ? .40 : .15));
			Edges.AddRange(closed.Take(ExtraEdgeCount));

			// Candidate alcoves are part of terrain design, independent of colony counts/types/ownership.
			var chamberIds = Enumerable.Range(0, Nodes.Count).OrderBy(i => TerrainComparison.Mix(settings.Seed, (ulong)(2320 + i)))
				.Take(Math.Max(3, Nodes.Count / 3)).Concat(selected).Distinct().ToArray();
			Chambers.AddRange(chamberIds.Select(i => Nodes[i]));
			foreach (var (edgeA, edgeB) in Edges)
			{
				var a = Nodes[edgeA]; var b = Nodes[edgeB]; var dx = b.X - a.X; var dy = b.Y - a.Y; var length = Math.Sqrt(dx * dx + dy * dy);
				var salt = TerrainComparison.Mix(settings.Seed, (ulong)(2330 + edgeA * Nodes.Count + edgeB));
				var phase = salt % 1024 / 1024D * Math.PI * 2; var previous = a;
				for (var step = 1; step <= 6; step++)
				{
					var t = step / 6D;
					var offset = Math.Sin(Math.PI * t) * (Math.Sin(phase) * 2 + depth * 1.25 * Math.Sin(t * Math.PI * 2 + phase));
					var p = new RmgPoint((int)Math.Round(a.X + dx * t - dy / length * offset), (int)Math.Round(a.Y + dy * t + dx / length * offset));
					Segments.Add((previous, p)); previous = p;
				}
			}

			HalfWidth = (settings.LaneWidth == RmgBattlefieldLaneWidth.Narrow ? 2.5 : settings.LaneWidth == RmgBattlefieldLaneWidth.Wide ? 5 : 3.5) + (4 - depth) * .6;
			PassageDistance = Enumerable.Repeat(48D, size * size).ToArray();
			foreach (var (a, b) in Segments)
				for (var y = Math.Max(0, Math.Min(a.Y, b.Y) - 28); y <= Math.Min(size - 1, Math.Max(a.Y, b.Y) + 28); y++)
					for (var x = Math.Max(0, Math.Min(a.X, b.X) - 28); x <= Math.Min(size - 1, Math.Max(a.X, b.X) + 28); x++)
						PassageDistance[y * size + x] = Math.Min(PassageDistance[y * size + x], StrongholdsGeometry.SegmentDistance(x, y, a, b));
			Clear = new bool[size * size]; Land = PassageDistance.Select(d => d <= HalfWidth).ToArray();
			foreach (var node in Nodes)
			{
				var chamber = Chambers.Contains(node); var radius = chamber ? 9 : 6;
				for (var y = Math.Max(0, node.Y - radius); y <= Math.Min(size - 1, node.Y + radius); y++)
					for (var x = Math.Max(0, node.X - radius); x <= Math.Min(size - 1, node.X + radius); x++)
					{
						if (SquareDistance(node, new RmgPoint(x, y)) <= radius * radius) Land[y * size + x] = true;
						if (chamber && Math.Max(Math.Abs(x - node.X), Math.Abs(y - node.Y)) <= 6) Clear[y * size + x] = Land[y * size + x] = true;
					}
			}
		}

		static long SquareDistance(RmgPoint a, RmgPoint b) => (long)(a.X - b.X) * (a.X - b.X) + (long)(a.Y - b.Y) * (a.Y - b.Y);
	}

	public static partial class RmgGenerator
	{
		static RmgGenerationResult GenerateLabyrinth(RmgProfile profile, RmgGenerationSettings settings)
		{
			var size = settings.MapSize; var width = size / 2; var lattice = width + 1; var depth = (int)settings.TerrainComplexity;
			var geometry = new LabyrinthGeometry(settings, profile);
			var water = new double[width * width]; var allowed = new bool[water.Length];
			for (var i = 0; i < water.Length; i++)
			{
				var x = 2 * (i % width); var y = 2 * (i / width);
				water[i] = geometry.PassageDistance[y * size + x] + (1 + depth * .5) * Math.Sin(x / 9D + settings.Seed % 13) * Math.Cos(y / 11D + settings.Seed % 17);
				allowed[i] = !Enumerable.Range(0, 4).Any(f => geometry.Land[(y + f / 2) * size + x + f % 2]);
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

			var fields = new TerrainComparisonFields(water, geology, moisture, allowed, landAllowed);

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
				["actor_planning_ms"] = timer.Elapsed.TotalMilliseconds, ["maze_nodes"] = geometry.Nodes.Count, ["tree_edges"] = geometry.Nodes.Count - 1,
				["extra_edges"] = geometry.ExtraEdgeCount, ["extra_routes"] = RmgLabyrinthParameters.Name(settings.ExtraRoutes),
				["passage_width"] = RmgBattlefieldParameters.Name(settings.LaneWidth), ["passage_width_native"] = geometry.HalfWidth * 2,
				["water_space_fraction"] = waterFraction, ["water_available_percent_map"] = 100D * allowed.Count(v => v) / allowed.Length,
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
			result.Map.RegionsReport["characteristic_scale_native"] = size == 64 ? 20 : 24;
			result.Map.RegionsReport["preferred_start_reference"] = "fixed-labyrinth-junctions";
			result.Map.RegionsReport["strategic_routes_requirement"] = "CONNECTED_WINDING_PASSAGES";
			var metrics = result.Map.RegionsReport["metrics"];
			if ((double)metrics["gravel_percent_land"] + (double)metrics["moss_percent_land"] < terrainSettings.GravelPercent + terrainSettings.MossPercent - 3)
				result.Validation.Warnings.Add(new RmgValidationIssue("LABYRINTH_TERRAIN_CAPACITY", "Passages, alcoves and surface transitions limit modifier coverage."));
			return result;
		}
	}
}
