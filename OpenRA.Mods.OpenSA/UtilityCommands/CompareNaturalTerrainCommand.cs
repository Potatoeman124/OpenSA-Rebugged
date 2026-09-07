#region Copyright & License Information
/*
 * Copyright The OpenSA Developers (see CREDITS)
 * This file is part of OpenSA, under the GNU General Public License, version 3 or later.
 */
#endregion

using System;
using System.IO;
using OpenRA.Mods.OpenSA.Rmg;
using OpenRA.Mods.OpenSA.Rmg.Reassessment;

namespace OpenRA.Mods.OpenSA.UtilityCommands
{
	sealed class CompareNaturalTerrainCommand : IUtilityCommand
	{
		string IUtilityCommand.Name => "--compare-natural-terrain";
		bool IUtilityCommand.ValidateArguments(string[] args)
		{
			if (args.Length is 2 or 3 && args[1] == "--self-test")
				return true;
			if (args.Length == 3 && args[1] == "--verify-manifest")
				return true;
			if (args.Length == 4 && args[1] is "--reference" or "--player-evidence")
				return true;
			if (args.Length != 6)
				return false;
			return ulong.TryParse(args[2], out _) &&
				int.TryParse(args[3], out var size) && size is 128 or 256 &&
				Enum.TryParse<TerrainConstruction>(args[4], true, out var method) && Enum.IsDefined(method) &&
				Enum.TryParse<TerrainComplexity>(args[5], true, out var complexity) && complexity is TerrainComplexity.Low or TerrainComplexity.Standard or TerrainComplexity.High;
		}

		[Desc("OUTPUT_DIR SEED SIZE Fields|Regions Low|Standard|High (or --self-test [FIXTURE_DIR])",
			"Export one isolated terrain comparison. These packages have no playable starts.")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			Game.ModData = utility.ModData;
			if (args[1] == "--self-test")
			{
				TerrainComparisonExport.SelfTest(utility.ModData, args.Length == 3 ? Path.GetFullPath(args[2]) : null);
				return;
			}

			if (args[1] == "--verify-manifest")
			{
				TerrainComparisonExport.VerifyManifest(utility.ModData, Path.GetFullPath(args[2]));
				return;
			}

			if (args[1] == "--player-evidence")
			{
				var requested = RmgPlayerSettingsContract.Load(Path.GetFullPath(args[2])).Normalized;
				if (requested.GeneratorVersion is not (11 or 12 or 13))
					throw new ArgumentException("Player terrain evidence requires Regions V11/V12/V13 settings.");
				var profile = RmgProfile.Load(utility.ModData, requested);
				var evidenceSettings = new TerrainComparisonSettings(requested.Seed, requested.MapSize,
					TerrainConstruction.Regions, requested.TerrainComplexity)
				{
					WaterPercent = requested.WaterAmount switch { RmgParameterLevel.Low => 16, RmgParameterLevel.High => 24, _ => 20 },
					GravelPercent = profile.RockLandPercentFor(requested.TacticalTerrain),
					MossPercent = profile.VegetationLandPercentFor(requested.TacticalTerrain),
					OriginalSurfaceRelations = requested.OriginalSurfaceRelations,
					Continuity = requested.GeneratorVersion is 12 or 13,
					ExtendedComplexity = requested.GeneratorVersion == 13
				};
				TerrainComparisonExport.Write(utility.ModData, TerrainComparison.Generate(utility.ModData, evidenceSettings), Path.GetFullPath(args[3]));
				return;
			}

			if (args[1] == "--reference")
			{
				TerrainComparisonExport.WriteReference(utility.ModData, Path.GetFullPath(args[2]), Path.GetFullPath(args[3]));
				return;
			}

			var settings = new TerrainComparisonSettings(ulong.Parse(args[2]), int.Parse(args[3]),
				Enum.Parse<TerrainConstruction>(args[4], true), Enum.Parse<TerrainComplexity>(args[5], true));
			Console.WriteLine(settings.Identity);
			var result = TerrainComparison.Generate(utility.ModData, settings);
			TerrainComparisonExport.Write(utility.ModData, result, Path.GetFullPath(args[1]));
			Console.WriteLine(result.Report);
		}
	}
}
