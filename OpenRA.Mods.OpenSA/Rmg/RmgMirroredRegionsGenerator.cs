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
		static RmgGenerationResult CompleteRegionsWithPlan(RmgProfile profile, RmgGenerationSettings settings,
			TerrainComparisonResult terrain, RmgLogicalMap reference, BattlefieldPlan planned = null)
		{
			var map = terrain.Map;
			var frozen = TerrainComparison.Hash(TerrainComparison.NativeBytes(map));
			var timer = Stopwatch.StartNew();
			var groupSize = settings.MirroringAxes == 0 ? 1 : RmgMirroring.GroupSize(settings.MirroringAxes);
			var sites = new RegionsSites(Game.ModData, map, settings.OriginalSurfaceRelations);
			var random = new DeterministicRandom(TerrainComparison.Mix(settings.Seed, 1701));
			var candidates = Enumerable.Range(0, settings.MapSize * settings.MapSize)
				.Where(i => RmgMirroring.Canonical(i, settings.MapSize, settings.MirroringAxes, settings.Seed) == i)
				.Select(i => RmgMirroring.Points(new RmgPoint(i % settings.MapSize, i / settings.MapSize), settings.MapSize, settings.MirroringAxes, settings.Seed))
				.Where(points => points.Length == groupSize).ToArray();
			for (var i = candidates.Length - 1; i > 0; i--)
			{
				var j = random.NextInt(i + 1);
				(candidates[i], candidates[j]) = (candidates[j], candidates[i]);
			}

			var preferred = planned?.Starts ?? SelectMirroredStarts(profile, settings, reference, candidates, null, false);
			var starts = planned?.Starts ?? SelectMirroredStarts(profile, settings, map, candidates, preferred, true);
			foreach (var point in starts)
			{
				map.Starts.Add(new RmgPoint(point.X / 2, point.Y / 2));
				map.Actors.Add(RmgMirroring.Actor(profile.SpawnActor, profile.SpawnOwner, "start", point, map.Starts.Count - 1));
			}

			sites.ReserveNativeOrbit(null, starts);
			int colonyCount, strictCount;
			long evaluations;
			string[] drawnTypes;
			if (planned == null)
				colonyCount = PlaceMirroredColonies(map, profile, settings, sites, candidates, starts, out strictCount, out evaluations, out drawnTypes);
			else
			{
				map.Actors.AddRange(planned.Colonies);
				foreach (var colony in planned.Colonies) sites.ReserveNativeOrbit(colony.Type, new[] { RmgMirroring.Native(colony) });
				colonyCount = planned.Colonies.Length; strictCount = planned.StrictCount;
				evaluations = planned.Evaluations; drawnTypes = planned.DrawnTypes;
			}

			var placementMs = timer.Elapsed.TotalMilliseconds + (planned == null ? 0 : (double)planned.Report["actor_planning_ms"]);
			timer.Restart();
			var target = (settings.MapSize * settings.MapSize * profile.LandDecorationPerThousand + 500) / 1000;
			var decorations = new List<RmgPoint>();
			foreach (var orbit in candidates)
			{
				if (decorations.Count + groupSize > target) break;
				if (orbit.Any(p => sites.NearReserved(p) || decorations.Any(d => d.ChebyshevDistance(p) < 4)) ||
					orbit.Any(p => orbit.Any(q => p != q && p.ChebyshevDistance(q) < 4))) continue;
				var bank = TerrainComparison.Native(map, orbit[0].X, orbit[0].Y) switch
				{
					RmgNativeTerrainIntent.Clear => profile.SoilDecorationActors,
					RmgNativeTerrainIntent.Rock => profile.RockDecorationActors,
					RmgNativeTerrainIntent.Vegetation => profile.VegetationDecorationActors,
					_ => Array.Empty<string>()
				};
				if (bank.Length == 0) continue;
				var type = bank[random.NextInt(bank.Length)];
				foreach (var point in orbit) map.Actors.Add(RmgMirroring.Actor(type, profile.SpawnOwner, "decoration-passable", point, -1));
				decorations.AddRange(orbit);
			}

			if (frozen != TerrainComparison.Hash(TerrainComparison.NativeBytes(map))) throw new InvalidOperationException("Mirrored placement changed terrain.");
			map.NaturalSurfacesFrozen = true;
			var validation = new RmgValidationReport();
			ValidateColonyCombatSpace(map, profile, validation, !settings.PreventColonyOverlapping, nativeCoordinates: true);
			if (colonyCount < settings.EffectiveNeutralColonyCount)
				validation.Warnings.Add(new RmgValidationIssue("NEUTRAL_CAPACITY", settings.GeneratorVersion == 22 ? $"Placed {colonyCount}/{settings.EffectiveNeutralColonyCount} colonies in available fortified sites." : $"Placed {colonyCount}/{settings.EffectiveNeutralColonyCount} colonies in complete mirrored groups of {groupSize}."));
			map.RegionsReport = terrain.Report;
			var report = map.RegionsReport;
			report["status"] = settings.GeneratorVersion == 22 ? "PLAYABLE_STRONGHOLDS_V22" : settings.GeneratorVersion == 21 ? "PLAYABLE_DIVIDED_LANDS_V21" : settings.GeneratorVersion == 20 ? "PLAYABLE_RING_V20" : settings.GeneratorVersion == 19 ? "PLAYABLE_CROSSROADS_V19" : planned == null ? "PLAYABLE_REGIONS_V17" : "PLAYABLE_ARTIFICIAL_BATTLEFIELD_V18";
			report["placement_status"] = settings.GeneratorVersion == 22 ? "FORTIFIED_LOCAL_SITES_VALID" : "MIRRORED_LOCAL_SITES_VALID";
			report["terrain_repainted_for_placement"] = false;
			report["placement_ms"] = placementMs;
			report["doodads_ms"] = timer.Elapsed.TotalMilliseconds;
			report["mirroring_axes"] = settings.MirroringAxes;
			report["mirror_orientation"] = settings.MirroringAxes != 1 ? "horizontal-vertical" + (settings.MirroringAxes == 4 ? "-diagonals" : "") : (settings.Seed & 1) == 0 ? "vertical" : "horizontal";
			report["symmetry_requirement"] = settings.GeneratorVersion == 22 ? "NOT_REQUIRED" : "NATIVE_TERRAIN_STARTS_AND_TYPED_COLONIES";
			report["strategic_routes_requirement"] = settings.GeneratorVersion == 21 ? (settings.LandCrossings == RmgLandCrossings.None ? "DISCONNECTED_TERRITORIES" : "EXACT_BORDER_CROSSINGS") : planned == null ? "NOT_REQUIRED" : "CONNECTED_GROUND_LANE_NETWORK";
			report["neutral_colonies_requested"] = settings.EffectiveNeutralColonyCount;
			report["neutral_colonies_density_target"] = settings.NeutralColonyCount;
			report["neutral_colonies_group_target"] = settings.EffectiveNeutralColonyCount / groupSize * groupSize;
			report["neutral_colonies_placed"] = colonyCount;
			report["prevent_colony_overlapping"] = settings.PreventColonyOverlapping;
			report["neutral_colonies_strict"] = strictCount;
			report["neutral_colonies_fallback"] = colonyCount - strictCount;
			report["fallback_pair_evaluations"] = evaluations;
			report["neutral_overlapping_pairs"] = validation.Metrics.GetValueOrDefault("neutral_overlapping_pairs");
			report["maximum_neutral_overlap_native"] = validation.Metrics.GetValueOrDefault("maximum_neutral_overlap_native");
			report["neutral_colony_weights"] = settings.NeutralColonyWeights.ToJson();
			report["neutral_colonies_disabled"] = settings.NeutralColonyWeights.Total == 0;
			report["neutral_colonies_drawn_by_type"] = new JObject(RmgColonyWeights.Keys.Select(key => new JProperty(key, drawnTypes.Count(t => t == key + "_colony") * groupSize)));
			report["neutral_colonies_placed_by_type"] = new JObject(RmgColonyWeights.Keys.Select(key => new JProperty(key, map.Actors.Count(a => a.Role == "neutral-colony" && a.Type == key + "_colony"))));
			report["starting_colony_shares"] = new JArray(settings.StartingColonyShares);
			var allocation = RmgColonyOwnership.Allocate(colonyCount, settings.StartingColonyShares);
			report["starting_colonies_allocated_if_all_slots_occupied"] = new JArray(allocation);
			report["unowned_colonies_if_all_slots_occupied"] = colonyCount - allocation.Sum();
			report["ownership_assignment"] = settings.StartingColonyMode == RmgColonyOwnershipMode.Random ? "runtime-player-slot-and-seeded-random" : "runtime-player-slot-and-actual-start";
			report["starting_colony_mode"] = RmgColonyOwnership.ModeName(settings.StartingColonyMode);
			report["doodads_requested"] = target;
			report["doodads_placed"] = decorations.Count;
			report["placement_candidates"] = candidates.Length;
			report["geography_contract"] = settings.GeneratorVersion == 22 ? "fixed-asymmetric-fortresses-v22" : settings.GeneratorVersion == 21 ? "separate-home-territories-v21" : settings.GeneratorVersion == 20 ? "continuous-ring-central-lake-v20" : settings.GeneratorVersion == 19 ? "central-junction-approaches-v19" : planned == null ? "mirrored-fixed-regions-extended-detail-v17" : "planned-geometric-battlefield-v18";
			report["preferred_start_reference"] = planned == null ? "same-axes-v12-low-complexity" : "fixed-planned-player-plazas";
			if (planned != null) report[settings.GeneratorVersion == 22 ? "strongholds_plan" : settings.GeneratorVersion == 21 ? "divided_lands_plan" : settings.GeneratorVersion == 20 ? "ring_plan" : settings.GeneratorVersion == 19 ? "crossroads_plan" : "battlefield_plan"] = planned.Report;
			if (settings.GeneratorVersion == 22) report.Remove("mirror_orientation");
			report["start_displacement_native"] = new JArray(starts.Select((p, i) => i < preferred.Count ? Math.Sqrt(RegionDistanceSquared(p, preferred[i])) : (double?)null));
			return new RmgGenerationResult
			{
				Settings = settings, Profile = profile, Map = map, Validation = validation,
				LogicalHash = HashLogicalMap(map), ActorHash = HashActors(map), GraphHash = HashGraph(map)
			};
		}

		static List<RmgPoint> SelectMirroredStarts(RmgProfile profile, RmgGenerationSettings settings, RmgLogicalMap terrain,
			RmgPoint[][] candidates, List<RmgPoint> preferred, bool requireAll)
		{
			var sites = new RegionsSites(Game.ModData, terrain, settings.OriginalSurfaceRelations);
			var starts = new List<RmgPoint>();
			var rules = profile.ColonyCombatRules;
			var valid = candidates.Where(points => sites.NativeOrbitFits(null, points)).ToArray();
			while (starts.Count < settings.PlayerCount)
			{
				var anchor = preferred != null && preferred.Count > starts.Count ? preferred[starts.Count] : (RmgPoint?)null;
				long Spread(RmgPoint[] orbit) => orbit.SelectMany((p, i) => orbit.Skip(i + 1).Concat(starts).Select(q => RegionDistanceSquared(p, q))).DefaultIfEmpty(0).Min();
				var ordered = anchor.HasValue ? valid.OrderBy(points => RegionDistanceSquared(points[0], anchor.Value)) : valid.OrderByDescending(Spread);
				var chosen = ordered.FirstOrDefault(points => sites.NativeOrbitFits(null, points) && points.SelectMany((p, i) => points.Skip(i + 1).Concat(starts)
					.Select(q => rules.StartMarginAtNative(p, q))).All(margin => margin >= 0));
				if (chosen == null)
				{
					if (requireAll) throw new RmgGenerationRejectedException("PVP_START_CAPACITY", $"Terrain supports {starts.Count}/{settings.PlayerCount} starts in safe mirrored groups. Try another seed or a larger map.");
					break;
				}

				starts.AddRange(chosen); sites.ReserveNativeOrbit(null, chosen);
			}

			return starts;
		}

		static int PlaceMirroredColonies(RmgLogicalMap map, RmgProfile profile, RmgGenerationSettings settings,
			RegionsSites sites, RmgPoint[][] candidates, List<RmgPoint> starts, out int strictCount, out long evaluations, out string[] drawnTypes)
		{
			var groupSize = RmgMirroring.GroupSize(settings.MirroringAxes);
			var random = new DeterministicRandom(TerrainComparison.Mix(settings.Seed, 1702));
			drawnTypes = Enumerable.Range(0, settings.EffectiveNeutralColonyCount / groupSize)
				.Select(_ => settings.NeutralColonyWeights.ActorForTicket(random.NextInt(settings.NeutralColonyWeights.Total))).ToArray();
			var colonies = new List<(string Type, RmgPoint Point)>();
			var missing = new List<string>();
			var rules = profile.ColonyCombatRules;
			var cursors = drawnTypes.Distinct().ToDictionary(type => type, _ => 0);
			bool Fits(string type, RmgPoint[] points) => sites.NativeOrbitFits(type, points) &&
				points.All(p => starts.All(q => rules.ColonyStartMarginAtNative(type, p, q) >= 0));
			void Place(string type, RmgPoint[] points)
			{
				foreach (var p in points)
				{
					map.Actors.Add(RmgMirroring.Actor(type, profile.ColonyOwner, "neutral-colony", p, settings.PlayerCount + colonies.Count));
					colonies.Add((type, p));
				}

				sites.ReserveNativeOrbit(type, points);
			}

			foreach (var type in drawnTypes)
			{
				var placed = false;
				while (cursors[type] < candidates.Length)
				{
					var points = candidates[cursors[type]++];
					if (!Fits(type, points) || points.Any(p => colonies.Any(c => rules.ColonyMarginAtNative(type, p, c.Type, c.Point) < 0)) ||
						points.SelectMany((p, i) => points.Skip(i + 1).Select(q => rules.ColonyMarginAtNative(type, p, type, q))).Any(m => m < 0)) continue;
					Place(type, points); placed = true; break;
				}

				if (!placed) missing.Add(type);
			}

			strictCount = colonies.Count; evaluations = 0;
			if (settings.PreventColonyOverlapping || missing.Count == 0) return colonies.Count;
			var queues = new Dictionary<string, PriorityQueue<MirroredColonyCandidate, (int, long, int, int)>>();
			foreach (var type in missing.Distinct())
			{
				var queue = new PriorityQueue<MirroredColonyCandidate, (int, long, int, int)>();
				queues.Add(type, queue);
				for (var i = 0; i < candidates.Length; i++)
				{
					if (!Fits(type, candidates[i])) continue;
					var candidate = new MirroredColonyCandidate(candidates[i], i);
					for (var a = 0; a < candidate.Points.Length; a++)
						for (var b = a + 1; b < candidate.Points.Length; b++)
						{
							candidate.Score(rules.ColonyMarginAtNative(type, candidate.Points[a], type, candidate.Points[b])); evaluations++;
						}

					queue.Enqueue(candidate, candidate.Priority);
				}
			}

			foreach (var type in missing)
				while (queues[type].TryDequeue(out var candidate, out var previous))
				{
					if (!sites.NativeOrbitFits(type, candidate.Points)) continue;
					for (; candidate.Scored < colonies.Count; candidate.Scored++)
						foreach (var p in candidate.Points)
						{
							var other = colonies[candidate.Scored];
							candidate.Score(rules.ColonyMarginAtNative(type, p, other.Type, other.Point)); evaluations++;
						}

					if (candidate.Priority != previous) { queues[type].Enqueue(candidate, candidate.Priority); continue; }
					Place(type, candidate.Points); break;
				}

			return colonies.Count;
		}

		sealed class MirroredColonyCandidate
		{
			public readonly RmgPoint[] Points;
			readonly int index;
			int depth, pairs;
			long squaredDepth;
			public int Scored;
			public (int Depth, long SquaredDepth, int Pairs, int Index) Priority => (depth, squaredDepth, pairs, index);
			public MirroredColonyCandidate(RmgPoint[] points, int index) { Points = points; this.index = index; }
			public void Score(int margin)
			{
				var overlap = Math.Max(0, -margin);
				depth = Math.Max(depth, overlap); squaredDepth += (long)overlap * overlap;
				if (overlap > 0) pairs++;
			}
		}
	}
}
