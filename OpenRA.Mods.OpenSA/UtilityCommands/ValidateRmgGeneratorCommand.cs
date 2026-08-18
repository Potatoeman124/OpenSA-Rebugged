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
using OpenRA.Mods.OpenSA.Rmg;

namespace OpenRA.Mods.OpenSA.UtilityCommands
{
	sealed class ValidateRmgGeneratorCommand : IUtilityCommand
	{
		string IUtilityCommand.Name => "--validate-sa-rmg";
		bool IUtilityCommand.ValidateArguments(string[] args) => args.Length == 1;

		[Desc("Run focused deterministic RMG self-tests for frozen V1-V4 and opt-in land-cover V5 profiles.")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			try
			{
				var failures = new List<string>();
				foreach (var topology in new[] { RmgTopologyPreset.Off, RmgTopologyPreset.Mixed, RmgTopologyPreset.Shoreline, RmgTopologyPreset.LandDetails, RmgTopologyPreset.LandCover })
				{
					var profile = RmgProfile.Load(utility.ModData, topology);
					var profileFailures = RmgGenerator.RunSelfTests(profile);
					failures.AddRange(profileFailures.Select(failure => $"{profile.ProfileId}: {failure}"));
					Console.WriteLine($"{profile.ProfileId}: {(profileFailures.Count == 0 ? "PASS" : "FAIL")}");
				}

				var shorelineProfile = RmgProfile.Load(utility.ModData, RmgTopologyPreset.Shoreline);
				var detailProfile = RmgProfile.Load(utility.ModData, RmgTopologyPreset.LandDetails);
				var shorelineSettings = BaselineSettings(shorelineProfile, RmgTopologyPreset.Shoreline);
				var detailSettings = BaselineSettings(detailProfile, RmgTopologyPreset.LandDetails);
				var shoreline = RmgGenerator.Generate(shorelineProfile, shorelineSettings);
				var details = RmgGenerator.Generate(detailProfile, detailSettings);
				var detailTemplates = detailProfile.ClearLandDetailTemplateIds.ToHashSet();
				var baseTerrainMatches = Enumerable.Range(0, shoreline.Map.TemplateIds.Length).All(i =>
					detailTemplates.Contains(details.Map.TemplateIds[i]) || shoreline.Map.TemplateIds[i] == details.Map.TemplateIds[i]);
				var inheritedBaselineMatches = shoreline.ActorHash == details.ActorHash &&
					shoreline.GraphHash == details.GraphHash &&
					shoreline.Map.Obstacles.SequenceEqual(details.Map.Obstacles) &&
					shoreline.Map.RouteMasks.SequenceEqual(details.Map.RouteMasks) &&
					shoreline.Map.StartReservations.SequenceEqual(details.Map.StartReservations) &&
					shoreline.Map.StructureReservations.SequenceEqual(details.Map.StructureReservations) &&
					shoreline.Map.StrategicRegions.SequenceEqual(details.Map.StrategicRegions) &&
					shoreline.Map.ShorelineRoles.SequenceEqual(details.Map.ShorelineRoles) &&
					baseTerrainMatches;
				if (!inheritedBaselineMatches)
					failures.Add("normal-land-details-v4: Version 4 does not inherit the Version 3 topology, actors, routes, shoreline, and non-detail templates.");
				Console.WriteLine($"v3-to-v4 inherited baseline: {(inheritedBaselineMatches ? "PASS" : "FAIL")}");

				var landProfile = RmgProfile.Load(utility.ModData, RmgTopologyPreset.LandCover);
				var landSettings = BaselineSettings(landProfile, RmgTopologyPreset.LandCover);
				var land = RmgGenerator.Generate(landProfile, landSettings);
				var version5BaselineMatches = details.ActorHash == land.ActorHash &&
					details.GraphHash == land.GraphHash &&
					details.Map.Obstacles.SequenceEqual(land.Map.Obstacles) &&
					details.Map.RouteMasks.SequenceEqual(land.Map.RouteMasks) &&
					details.Map.StartReservations.SequenceEqual(land.Map.StartReservations) &&
					details.Map.StructureReservations.SequenceEqual(land.Map.StructureReservations) &&
					details.Map.StrategicRegions.SequenceEqual(land.Map.StrategicRegions) &&
					details.Map.ShorelineRoles.SequenceEqual(land.Map.ShorelineRoles) &&
					Enumerable.Range(0, details.Map.TemplateIds.Length).Where(index => details.Map.Obstacles[index])
						.All(index => details.Map.TemplateIds[index] == land.Map.TemplateIds[index]);
				if (!version5BaselineMatches)
					failures.Add("normal-land-cover-v5: Version 5 does not inherit the Version 4 actors, topology, reservations, shoreline, and Water templates.");
				Console.WriteLine($"v4-to-v5 inherited baseline: {(version5BaselineMatches ? "PASS" : "FAIL")}");

				static RmgGenerationSettings BaselineSettings(RmgProfile profile, RmgTopologyPreset topology) => new()
				{
					Seed = 45006,
					PlayerCount = 2,
					NeutralColonyCount = 10,
					Symmetry = RmgSymmetry.Rotate180,
					Archetype = RmgArchetype.CentralContest,
					GeneratorVersion = profile.GeneratorVersion,
					TopologyPreset = topology
				};

				var nativeFailures = NativeMovementValidator.RunSelfTests();
				failures.AddRange(nativeFailures.Select(failure => $"native-validator: {failure}"));
				Console.WriteLine($"native-validator: {(nativeFailures.Count == 0 ? "PASS" : "FAIL")}");

				if (failures.Count > 0)
				{
					foreach (var failure in failures)
						Console.Error.WriteLine(failure);
					Environment.ExitCode = 4;
					return;
				}

				Console.WriteLine("Focused RMG self-tests passed.");
				Environment.ExitCode = 0;
			}
			catch (Exception e)
			{
				Console.Error.WriteLine(e);
				Environment.ExitCode = 6;
			}
		}
	}
}
