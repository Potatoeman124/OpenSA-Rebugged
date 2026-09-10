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
using System.Linq;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public sealed record RmgProductionPath(
		int StartXWorld,
		int StartYWorld,
		int EndXWorld,
		int EndYWorld);

	public sealed record RmgColonyCombatProfile(
		string ActorType,
		int MaximumAttackRangeWorld,
		int CenterXWorld,
		int CenterYWorld,
		RmgProductionPath[] ProductionPaths,
		int MaximumProductionPathOffsetWorld);

	public sealed class RmgColonyCombatRules
	{
		const int WorldUnitsPerNativeCell = 1024;
		const int WorldUnitsPerLogicalCell = 2 * WorldUnitsPerNativeCell;

		readonly IReadOnlyDictionary<string, RmgColonyCombatProfile> profiles;

		public string[] StartingColonyActors { get; }
		public int SafetyBufferNative { get; }
		public int MaximumAttackRangeNative { get; }
		public int MaximumRequiredSeparationLogical { get; }

		RmgColonyCombatRules(IReadOnlyDictionary<string, RmgColonyCombatProfile> profiles,
			string[] startingColonyActors, int safetyBufferNative)
		{
			this.profiles = profiles;
			StartingColonyActors = startingColonyActors;
			SafetyBufferNative = safetyBufferNative;
			MaximumAttackRangeNative = profiles.Values.Max(p => DivideRoundUp(p.MaximumAttackRangeWorld, WorldUnitsPerNativeCell));
			MaximumRequiredSeparationLogical = profiles.Values.SelectMany(first => profiles.Values.Select(second =>
				Math.Max(DirectedConservativeSeparationLogical(first, second), DirectedConservativeSeparationLogical(second, first)))).Max();
		}

		public static RmgColonyCombatRules Load(ModData modData, IEnumerable<string> neutralColonyActors, int safetyBufferNative)
		{
			if (safetyBufferNative < 1)
				throw new InvalidOperationException("The colony combat-space buffer must be at least one native cell.");

			// Trait parsing may instantiate shapes through Game.CreateObject. Utility commands own a
			// ModData instance but do not establish the static game context automatically.
			Game.ModData = modData;

			var worldInfo = modData.DefaultRules.Actors[SystemActors.World];
			var startingColonyActors = worldInfo.TraitInfos<StartingUnitsInfo>()
				.Where(info => !string.IsNullOrEmpty(info.BaseActor))
				.Select(info => info.BaseActor)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(actor => actor, StringComparer.OrdinalIgnoreCase)
				.ToArray();
			if (startingColonyActors.Length == 0)
				throw new InvalidOperationException("No runtime starting-colony actors are available for combat-space validation.");

			var actorTypes = neutralColonyActors.Concat(startingColonyActors)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(actor => actor, StringComparer.OrdinalIgnoreCase)
				.ToArray();
			var profiles = actorTypes.ToDictionary(actor => actor, actor => LoadActorProfile(modData, actor),
				StringComparer.OrdinalIgnoreCase);
			return new RmgColonyCombatRules(profiles, startingColonyActors, safetyBufferNative);
		}

		public bool CombatSpaceIsSafe(string firstActor, RmgPoint firstLocation, string secondActor, RmgPoint secondLocation) =>
			ColonyCenterMarginWorld(firstActor, firstLocation, secondActor, secondLocation) >= 0;

		public bool CombatSpaceIsSafeFromAnyStartingActor(string neutralActor, RmgPoint neutralLocation, RmgPoint startLocation) =>
			StartingColonyActors.All(startingActor => StartingAndNeutralMarginWorld(
				neutralActor, neutralLocation, startingActor, startLocation) >= 0);

		public int CombatSpaceMarginNative(string firstActor, RmgPoint firstLocation, string secondActor, RmgPoint secondLocation) =>
			ToNativeMargin(ColonyCenterMarginWorld(firstActor, firstLocation, secondActor, secondLocation));

		public int CombatSpaceMarginFromAnyStartingActorNative(string neutralActor, RmgPoint neutralLocation, RmgPoint startLocation) =>
			StartingColonyActors.Min(startingActor => ToNativeMargin(StartingAndNeutralMarginWorld(
				neutralActor, neutralLocation, startingActor, startLocation)));

		public int StartingCombatSpaceMarginNative(RmgPoint firstLocation, RmgPoint secondLocation) =>
			StartingColonyActors.SelectMany(first => StartingColonyActors.Select(second =>
				ToNativeMargin(StartingPairMarginWorld(first, firstLocation, second, secondLocation)))).Min();

		public int AttackRangeNative(string actorType) => DivideRoundUp(Profile(actorType).MaximumAttackRangeWorld, WorldUnitsPerNativeCell);

		public int ColonyMarginAtNative(string firstActor, RmgPoint first, string secondActor, RmgPoint second) =>
			ToNativeMargin(ColonyCenterMarginWorld(firstActor, first, secondActor, second, WorldUnitsPerNativeCell));

		public int ColonyStartMarginAtNative(string actor, RmgPoint colony, RmgPoint start) =>
			StartingColonyActors.Min(type => ToNativeMargin(StartingAndNeutralMarginWorld(actor, colony, type, start, WorldUnitsPerNativeCell)));

		public int StartMarginAtNative(RmgPoint first, RmgPoint second) =>
			StartingColonyActors.SelectMany(a => StartingColonyActors.Select(b =>
				ToNativeMargin(StartingPairMarginWorld(a, first, b, second, WorldUnitsPerNativeCell)))).Min();

		double ColonyCenterMarginWorld(string firstActor, RmgPoint firstLocation, string secondActor, RmgPoint secondLocation, int scale = WorldUnitsPerLogicalCell)
		{
			var first = Profile(firstActor);
			var second = Profile(secondActor);
			return Math.Min(
				DirectedCombatSpaceMarginWorld(first, firstLocation, second, secondLocation, false, scale),
				DirectedCombatSpaceMarginWorld(second, secondLocation, first, firstLocation, false, scale));
		}

		double StartingAndNeutralMarginWorld(string neutralActor, RmgPoint neutralLocation,
			string startingActor, RmgPoint startLocation, int scale = WorldUnitsPerLogicalCell)
		{
			var neutral = Profile(neutralActor);
			var start = Profile(startingActor);
			return Math.Min(
				DirectedCombatSpaceMarginWorld(neutral, neutralLocation, start, startLocation, true, scale),
				DirectedCombatSpaceMarginWorld(start, startLocation, neutral, neutralLocation, false, scale));
		}

		double StartingPairMarginWorld(string firstActor, RmgPoint firstLocation, string secondActor, RmgPoint secondLocation, int scale = WorldUnitsPerLogicalCell)
		{
			var first = Profile(firstActor);
			var second = Profile(secondActor);
			return Math.Min(
				DirectedCombatSpaceMarginWorld(first, firstLocation, second, secondLocation, true, scale),
				DirectedCombatSpaceMarginWorld(second, secondLocation, first, firstLocation, true, scale));
		}

		double DirectedCombatSpaceMarginWorld(RmgColonyCombatProfile attacker, RmgPoint attackerLocation,
			RmgColonyCombatProfile target, RmgPoint targetLocation, bool protectProductionPath, int scale)
		{
			var attackerCenter = Center(attacker, attackerLocation, scale);
			var targetCenter = Center(target, targetLocation, scale);
			var minimumDistanceSquared = DistanceSquared(attackerCenter, targetCenter);
			if (protectProductionPath)
				foreach (var path in target.ProductionPaths)
			{
				var start = new WorldPoint(targetCenter.X + path.StartXWorld, targetCenter.Y + path.StartYWorld);
				var end = new WorldPoint(targetCenter.X + path.EndXWorld, targetCenter.Y + path.EndYWorld);
				minimumDistanceSquared = Math.Min(minimumDistanceSquared, DistanceSquared(attackerCenter, start, end));
			}

			var requiredDistance = attacker.MaximumAttackRangeWorld + SafetyBufferNative * WorldUnitsPerNativeCell;
			return Math.Sqrt(minimumDistanceSquared) - requiredDistance;
		}

		RmgColonyCombatProfile Profile(string actorType)
		{
			if (!profiles.TryGetValue(actorType, out var profile))
				throw new InvalidOperationException($"No colony combat profile is available for '{actorType}'.");
			return profile;
		}

		static int ToNativeMargin(double marginWorld) => (int)Math.Floor(marginWorld / WorldUnitsPerNativeCell);

		int DirectedConservativeSeparationLogical(RmgColonyCombatProfile attacker, RmgColonyCombatProfile target)
		{
			var requiredWorld = attacker.MaximumAttackRangeWorld + target.MaximumProductionPathOffsetWorld +
				SafetyBufferNative * WorldUnitsPerNativeCell;
			return DivideRoundUp(requiredWorld, WorldUnitsPerLogicalCell);
		}

		static RmgColonyCombatProfile LoadActorProfile(ModData modData, string actorType)
		{
			if (!modData.DefaultRules.Actors.TryGetValue(actorType, out var actorInfo))
				throw new InvalidOperationException($"Colony combat actor '{actorType}' is not defined by the loaded rules.");

			var buildings = actorInfo.TraitInfos<BuildingInfo>().ToArray();
			if (buildings.Length != 1)
				throw new InvalidOperationException($"Colony combat actor '{actorType}' must define exactly one BuildingInfo.");

			var maximumAttackRange = actorInfo.TraitInfos<ArmamentInfo>()
				.Select(armament => armament.WeaponInfo?.Range.Length ?? 0)
				.DefaultIfEmpty(0)
				.Max();
			if (maximumAttackRange <= 0)
				throw new InvalidOperationException($"Colony combat actor '{actorType}' has no positive armament range.");

			var building = buildings[0];
			var centerX = (building.Dimensions.X - 1) * WorldUnitsPerNativeCell / 2 + building.LocalCenterOffset.X;
			var centerY = (building.Dimensions.Y - 1) * WorldUnitsPerNativeCell / 2 + building.LocalCenterOffset.Y;
			var paths = actorInfo.TraitInfos<ExitInfo>()
				.Select(exit => new RmgProductionPath(
					exit.SpawnOffset.X,
					exit.SpawnOffset.Y,
					exit.ExitCell.X * WorldUnitsPerNativeCell - centerX,
					exit.ExitCell.Y * WorldUnitsPerNativeCell - centerY))
				.ToArray();
			if (paths.Length == 0)
				throw new InvalidOperationException($"Colony combat actor '{actorType}' has no production exit path.");

			var maximumPathOffset = paths.SelectMany(path => new[]
			{
				DistanceFromOrigin(path.StartXWorld, path.StartYWorld),
				DistanceFromOrigin(path.EndXWorld, path.EndYWorld)
			}).Max();
			return new RmgColonyCombatProfile(actorType, maximumAttackRange, centerX, centerY, paths, maximumPathOffset);
		}

		static WorldPoint Center(RmgColonyCombatProfile profile, RmgPoint location, int scale) =>
			new(location.X * scale + profile.CenterXWorld,
				location.Y * scale + profile.CenterYWorld);

		static double DistanceSquared(WorldPoint first, WorldPoint second)
		{
			var dx = (double)first.X - second.X;
			var dy = (double)first.Y - second.Y;
			return dx * dx + dy * dy;
		}

		static double DistanceSquared(WorldPoint point, WorldPoint segmentStart, WorldPoint segmentEnd)
		{
			var dx = (double)segmentEnd.X - segmentStart.X;
			var dy = (double)segmentEnd.Y - segmentStart.Y;
			var lengthSquared = dx * dx + dy * dy;
			if (lengthSquared == 0)
				return DistanceSquared(point, segmentStart);

			var projection = ((point.X - segmentStart.X) * dx + (point.Y - segmentStart.Y) * dy) / lengthSquared;
			projection = Math.Clamp(projection, 0, 1);
			var nearestX = segmentStart.X + projection * dx;
			var nearestY = segmentStart.Y + projection * dy;
			var pointDx = point.X - nearestX;
			var pointDy = point.Y - nearestY;
			return pointDx * pointDx + pointDy * pointDy;
		}

		static int DistanceFromOrigin(int x, int y) => (int)Math.Ceiling(Math.Sqrt((double)x * x + (double)y * y));
		static int DivideRoundUp(int value, int divisor) => checked((value + divisor - 1) / divisor);

		readonly struct WorldPoint
		{
			public readonly long X;
			public readonly long Y;

			public WorldPoint(long x, long y)
			{
				X = x;
				Y = y;
			}
		}
	}

	public static partial class RmgGenerator
	{
		static bool ColonyCombatSpaceIsValid(RmgLogicalMap map, RmgProfile profile, string actorType, RmgPoint point)
		{
			if (profile.GeneratorVersion < 2)
				return true;

			var rules = profile.ColonyCombatRules;
			if (map.Starts.Any(start => !rules.CombatSpaceIsSafeFromAnyStartingActor(actorType, point, start)))
				return false;

			return !map.Actors.Where(actor => actor.Owner == profile.ColonyOwner).Any(actor =>
				!rules.CombatSpaceIsSafe(actorType, point, actor.Type, actor.LogicalLocation));
		}

		static void ValidateColonyCombatSpace(RmgLogicalMap map, RmgProfile profile, RmgValidationReport report, bool allowNeutralOverlap = false, bool nativeCoordinates = false)
		{
			if (profile.GeneratorVersion < 2)
				return;

			var rules = profile.ColonyCombatRules;
			var colonies = map.Actors.Where(actor => actor.Owner == profile.ColonyOwner).ToArray();
			var starts = nativeCoordinates ? map.Actors.Where(a => a.Role == "start").Select(RmgMirroring.Native).ToArray() : map.Starts.ToArray();
			var minimumMargin = int.MaxValue;
			var overlappingPairs = 0;
			var maximumOverlap = 0;

			foreach (var colony in colonies)
				foreach (var start in starts)
				{
					var margin = nativeCoordinates ? rules.ColonyStartMarginAtNative(colony.Type, RmgMirroring.Native(colony), start) : rules.CombatSpaceMarginFromAnyStartingActorNative(colony.Type, colony.LogicalLocation, start);
					minimumMargin = Math.Min(minimumMargin, margin);
					if (margin < 0)
						report.HardFailures.Add(new RmgValidationIssue("COLONY_COMBAT_SPACE",
							$"{colony.Type} at {colony.LogicalLocation} violates a possible starting colony's bidirectional turret/production envelope at {start} by {-margin} native cells."));
				}

			for (var i = 0; i < colonies.Length; i++)
				for (var j = i + 1; j < colonies.Length; j++)
				{
					var margin = nativeCoordinates ? rules.ColonyMarginAtNative(colonies[i].Type, RmgMirroring.Native(colonies[i]), colonies[j].Type, RmgMirroring.Native(colonies[j])) : rules.CombatSpaceMarginNative(colonies[i].Type, colonies[i].LogicalLocation,
						colonies[j].Type, colonies[j].LogicalLocation);
					minimumMargin = Math.Min(minimumMargin, margin);
					if (margin < 0)
					{
						overlappingPairs++;
						maximumOverlap = Math.Max(maximumOverlap, -margin);
					}
					if (margin < 0 && !allowNeutralOverlap)
						report.HardFailures.Add(new RmgValidationIssue("COLONY_COMBAT_SPACE",
							$"{colonies[i].Type} at {colonies[i].LogicalLocation} and {colonies[j].Type} at {colonies[j].LogicalLocation} violate their bidirectional turret envelopes by {-margin} native cells."));
				}

			for (var i = 0; i < starts.Length; i++)
				for (var j = i + 1; j < starts.Length; j++)
				{
					var margin = nativeCoordinates ? rules.StartMarginAtNative(starts[i], starts[j]) : rules.StartingCombatSpaceMarginNative(starts[i], starts[j]);
					minimumMargin = Math.Min(minimumMargin, margin);
					if (margin < 0)
						report.HardFailures.Add(new RmgValidationIssue("START_COMBAT_SPACE",
							$"Starts {map.Starts[i]} and {map.Starts[j]} violate a possible pair of starting-colony turret/production envelopes by {-margin} native cells."));
				}

			if (profile.GeneratorVersion is 14 or 15 or 16 or 17 or 18 or 19 or 20 or 21 or 22)
			{
				report.Metrics["neutral_overlapping_pairs"] = overlappingPairs;
				report.Metrics["maximum_neutral_overlap_native"] = maximumOverlap;
			}

			report.Metrics["maximum_colony_attack_range_native"] = rules.MaximumAttackRangeNative;
			report.Metrics["colony_combat_safety_buffer_native"] = rules.SafetyBufferNative;
			report.Metrics["maximum_required_colony_separation_logical"] = rules.MaximumRequiredSeparationLogical;
			report.Metrics["minimum_colony_combat_margin_native"] = minimumMargin == int.MaxValue ? 0 : minimumMargin;
		}
	}
}
