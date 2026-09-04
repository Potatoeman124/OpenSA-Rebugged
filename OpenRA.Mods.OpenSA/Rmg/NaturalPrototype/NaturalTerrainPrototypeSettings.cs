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
using System.Security.Cryptography;
using System.Text;

namespace OpenRA.Mods.OpenSA.Rmg.NaturalPrototype
{
	public enum NaturalTerrainPrototypeVariant
	{
		CorrelatedFieldBaseline,
		CorrelatedFieldWithBasinPotential
	}

	public sealed class NaturalTerrainPrototypeSettings
	{
		public const string PrototypeId = "natural-v9-terrain-prototype-step2";
		public const string MorphologyId = "NATURAL_INLAND_LAKES_V1";
		public const int SchemaVersion = 2;
		public const int Width = 128;
		public const int Height = 128;

		public ulong RootSeed { get; init; }
		public int CandidateIndex { get; init; }
		public NaturalTerrainPrototypeVariant Variant { get; init; }
		public bool OriginalSurfaceRelations { get; init; } = true;
		public double? WaterTargetOverride { get; init; }
		public double? RockTargetOverride { get; init; }
		public double? VegetationTargetOverride { get; init; }

		public string VariantId => Variant switch
		{
			NaturalTerrainPrototypeVariant.CorrelatedFieldBaseline => "correlated-field-baseline",
			NaturalTerrainPrototypeVariant.CorrelatedFieldWithBasinPotential => "correlated-field-with-basin-potential",
			_ => throw new ArgumentOutOfRangeException()
		};

		public string SurfaceRelationsId => OriginalSurfaceRelations ? "original" : "unrestricted";

		// Preserve the accepted Step 2 field streams. The option changes semantic classification only.
		public string FieldIdentity => string.Join("\n", new[]
		{
			$"prototype={PrototypeId}",
			"schema=1",
			$"morphology={MorphologyId}",
			$"variant={VariantId}",
			$"root_seed={RootSeed}",
			$"candidate_index={CandidateIndex}",
			$"size={Width}x{Height}",
			"tileset=NORMAL"
		});

		public string CanonicalIdentity => FieldIdentity +
			$"\nschema_extension={SchemaVersion}\noriginal_surface_relations={OriginalSurfaceRelations.ToString().ToLowerInvariant()}" +
			$"\nwater_target_override={TargetIdentity(WaterTargetOverride)}" +
			$"\nrock_target_override={TargetIdentity(RockTargetOverride)}" +
			$"\nvegetation_target_override={TargetIdentity(VegetationTargetOverride)}";

		static string TargetIdentity(double? value) =>
			value?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? "automatic";

		public static readonly string[] StreamNames =
		{
			"candidate-root",
			"landform-elevation",
			"domain-warp-x",
			"domain-warp-y",
			"basin-potential",
			"moisture",
			"roughness",
			"classification-variation"
		};

		public IReadOnlyDictionary<string, ulong> StreamSeeds()
		{
			var result = new Dictionary<string, ulong>(StringComparer.Ordinal);
			foreach (var stream in StreamNames)
				result.Add(stream, DeriveSeed(FieldIdentity, stream));
			return result;
		}

		public static ulong DeriveSeed(string canonicalIdentity, string streamName)
		{
			var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalIdentity + "\nstream=" + streamName));
			ulong seed = 0;
			for (var i = 0; i < sizeof(ulong); i++)
				seed |= (ulong)bytes[i] << (8 * i);
			return seed;
		}
	}
}
