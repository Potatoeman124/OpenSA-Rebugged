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
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OpenRA.Mods.OpenSA.Rmg.NaturalPrototype
{
	public static class NaturalTerrainPrototypeExporter
	{
		public static JObject Export(NaturalTerrainPrototypeCandidate candidate, string outputDirectory, bool overwrite)
		{
			var root = Path.GetFullPath(outputDirectory);
			if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any() && !overwrite)
				throw new IOException($"Prototype candidate directory is not empty: {root}. Pass --overwrite to replace its files.");
			Directory.CreateDirectory(root);
			var fieldsDirectory = Path.Combine(root, "fields");
			var semanticDirectory = Path.Combine(root, "semantic");
			Directory.CreateDirectory(fieldsDirectory);
			Directory.CreateDirectory(semanticDirectory);

			var fieldHashes = new JObject();
			foreach (var field in candidate.Fields.OrderBy(kv => kv.Key, StringComparer.Ordinal))
			{
				var path = Path.Combine(fieldsDirectory, field.Key + ".f32");
				WriteFloat32(path, field.Value);
				fieldHashes[field.Key] = Sha256(path);
			}

			var semanticPath = Path.Combine(semanticDirectory, "semantic.u8");
			File.WriteAllBytes(semanticPath, candidate.Semantic);
			var coverage = new JObject();
			foreach (var value in Enum.GetValues<NaturalTerrainSemantic>())
				coverage[value.ToString().ToLowerInvariant()] = candidate.Semantic.Count(v => v == (byte)value) / (double)candidate.Semantic.Length;

			var streamSeeds = new JObject();
			foreach (var stream in candidate.StreamSeeds.OrderBy(kv => kv.Key, StringComparer.Ordinal))
				streamSeeds[stream.Key] = new JObject
				{
					["subseed_u64"] = stream.Value.ToString(),
					["identity"] = candidate.Settings.CanonicalIdentity + "\nstream=" + stream.Key
				};

			var combined = string.Join("\n", fieldHashes.Properties().Select(p => p.Name + "=" + p.Value));
			var combinedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(combined))).ToLowerInvariant();
			var warnings = new JArray();
			var water = (double)coverage["water"];
			if (water < .12 || water > .28)
				warnings.Add($"Water coverage {water:P2} is outside the 12-28% diagnostic band.");

			var manifest = new JObject
			{
				["schema_version"] = NaturalTerrainPrototypeSettings.SchemaVersion,
				["prototype_id"] = NaturalTerrainPrototypeSettings.PrototypeId,
				["morphology_id"] = NaturalTerrainPrototypeSettings.MorphologyId,
				["variant"] = candidate.Settings.VariantId,
				["root_seed"] = candidate.Settings.RootSeed.ToString(),
				["candidate_index"] = candidate.Settings.CandidateIndex,
				["canonical_identity"] = candidate.Settings.CanonicalIdentity,
				["width"] = NaturalTerrainPrototypeSettings.Width,
				["height"] = NaturalTerrainPrototypeSettings.Height,
				["tileset"] = "NORMAL",
				["streams"] = streamSeeds,
				["fixed_parameters"] = new JObject
				{
					["landform_octaves"] = 4,
					["landform_base_scale_cells"] = 44,
					["domain_warp_octaves"] = 2,
					["domain_warp_scale_cells"] = 58,
					["domain_warp_amplitude_cells"] = 5.5,
					["edge_uplift_width_cells"] = 18,
					["classification_order"] = new JArray("Water", "Rock", "Vegetation", "Clear"),
					["post_hoc_cleanup"] = false,
					["cleanup_changed_cells"] = 0
				},
				["basins"] = JArray.FromObject(candidate.Basins),
				["thresholds"] = new JObject
				{
					["water"] = candidate.WaterThreshold,
					["rock"] = candidate.RockThreshold,
					["vegetation"] = candidate.VegetationThreshold
				},
				["soft_targets"] = new JObject
				{
					["water"] = candidate.WaterTarget,
					["rock"] = candidate.RockTarget,
					["vegetation"] = candidate.VegetationTarget
				},
				["realized_coverage"] = coverage,
				["semantic_sha256"] = Sha256(semanticPath),
				["field_sha256"] = fieldHashes,
				["field_data_sha256"] = combinedHash,
				["numeric_format"] = new JObject
				{
					["fields"] = "IEEE-754 float32 little-endian, row-major, 128x128",
					["semantic"] = "uint8 row-major, 0=Clear, 1=Water, 2=Rock, 3=Vegetation"
				},
				["warnings"] = warnings,
				["decoration_metrics"] = "NOT_APPLICABLE",
				["gameplay_route_fairness_metrics"] = "NOT_APPLICABLE"
			};
			File.WriteAllText(Path.Combine(root, "candidate-manifest.json"), manifest.ToString(Formatting.Indented) + Environment.NewLine);
			return manifest;
		}

		static void WriteFloat32(string path, IEnumerable<float> values)
		{
			using var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
			using var writer = new BinaryWriter(stream);
			foreach (var value in values)
				writer.Write(value);
		}

		static string Sha256(string path)
		{
			using var stream = File.OpenRead(path);
			using var sha = SHA256.Create();
			return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
		}
	}
}
