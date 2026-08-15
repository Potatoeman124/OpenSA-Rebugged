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

		[Desc("Run focused deterministic RMG self-tests for frozen V1/V2 and opt-in shoreline V3 profiles.")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			try
			{
				var failures = new List<string>();
				foreach (var topology in new[] { RmgTopologyPreset.Off, RmgTopologyPreset.Mixed, RmgTopologyPreset.Shoreline })
				{
					var profile = RmgProfile.Load(utility.ModData, topology);
					var profileFailures = RmgGenerator.RunSelfTests(profile);
					failures.AddRange(profileFailures.Select(failure => $"{profile.ProfileId}: {failure}"));
					Console.WriteLine($"{profile.ProfileId}: {(profileFailures.Count == 0 ? "PASS" : "FAIL")}");
				}

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
