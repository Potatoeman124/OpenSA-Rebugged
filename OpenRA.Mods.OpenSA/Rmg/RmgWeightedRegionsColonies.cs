#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public static partial class RmgGenerator
	{
		static int PlaceWeightedRegionsColonies(RmgLogicalMap map, RmgProfile profile, RmgGenerationSettings settings,
			RegionsSites sites, List<RmgPoint> candidates, out int strictCount, out long pairEvaluations, out string[] requestedTypes)
		{
			var random = DeterministicRandom.ForStream(settings, profile, "regions-colony-composition");

			// Draw each requested colony once. Site capacity must not silently replace a

			// selected species with one that happens to have a smaller combat envelope.
			requestedTypes = Enumerable.Range(0, settings.EffectiveNeutralColonyCount)
				.Select(_ => settings.NeutralColonyWeights.ActorForTicket(random.NextInt(settings.NeutralColonyWeights.Total))).ToArray();
			var colonies = new List<RmgActorPlan>();
			var missing = new List<string>();
			var cursors = requestedTypes.Distinct().ToDictionary(type => type, _ => 0);
			void Place(string type, RmgPoint point)
			{
				var actor = new RmgActorPlan(type, profile.ColonyOwner, "neutral-colony", point, settings.PlayerCount + colonies.Count);
				map.Actors.Add(actor);
				colonies.Add(actor);
				sites.ReserveColony(type, point);
			}

			foreach (var type in requestedTypes)
			{
				var placed = false;

				// Occupancy and combat restrictions only grow, so rejected sites never

				// become valid later in this pass. Keep an independent cursor per species.
				while (cursors[type] < candidates.Count)
				{
					var point = candidates[cursors[type]++];
					if (!sites.ColonyFits(type, point) || !ColonyCombatSpaceIsValid(map, profile, type, point)) continue;
					Place(type, point);
					placed = true;
					break;
				}

				if (!placed) missing.Add(type);
			}

			strictCount = colonies.Count;
			pairEvaluations = 0;
			if (settings.PreventColonyOverlapping || missing.Count == 0) return colonies.Count;
			var rules = profile.ColonyCombatRules;
			var queues = new Dictionary<string, PriorityQueue<RegionsColonyCandidate, (int, long, int, int)>>();
			foreach (var type in missing.Distinct())
			{
				var queue = new PriorityQueue<RegionsColonyCandidate, (int, long, int, int)>();
				queues.Add(type, queue);
				for (var i = 0; i < candidates.Count; i++)
				{
					var point = candidates[i];
					if (!sites.ColonyFits(type, point) || map.Starts.Any(start => !rules.CombatSpaceIsSafeFromAnyStartingActor(type, point, start))) continue;
					var candidate = new RegionsColonyCandidate(type, point, i);
					queue.Enqueue(candidate, candidate.Priority);
				}
			}

			foreach (var type in missing)
			{
				var queue = queues[type];
				while (queue.TryDequeue(out var candidate, out var previous))
				{
					if (!sites.ColonyFits(type, candidate.Point)) continue;
					for (; candidate.ScoredColonies < colonies.Count; candidate.ScoredColonies++)
					{
						var other = colonies[candidate.ScoredColonies];
						var depth = Math.Max(0, -rules.CombatSpaceMarginNative(type, candidate.Point, other.Type, other.LogicalLocation));
						pairEvaluations++;
						candidate.Depth = Math.Max(candidate.Depth, depth);
						candidate.SquaredDepth += (long)depth * depth;
						if (depth > 0) candidate.Pairs++;
					}

					if (candidate.Priority != previous)
					{
						queue.Enqueue(candidate, candidate.Priority);
						continue;
					}

					Place(type, candidate.Point);
					break;
				}
			}

			return colonies.Count;
		}
	}
}
