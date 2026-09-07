#region Copyright & License Information
/*
 * Copyright The OpenSA Developers (see CREDITS)
 * This file is part of OpenSA, under the GNU General Public License, version 3 or later.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;

namespace OpenRA.Mods.OpenSA.Rmg.Reassessment
{
	public enum TerrainConstruction { Fields, Regions }
	public enum TerrainComplexity { Low, Standard, High, Extreme, Ultra }

	public sealed record TerrainComparisonSettings(ulong Seed, int Size, TerrainConstruction Method, TerrainComplexity Complexity)
	{
		public int WaterPercent { get; init; } = 20;
		public int GravelPercent { get; init; } = 14;
		public int MossPercent { get; init; } = 8;
		public bool OriginalSurfaceRelations { get; init; } = true;
		public bool Continuity { get; init; }
		public bool ExtendedComplexity { get; init; }
		public TerrainComparisonSettings ContinuityReference => this with { Complexity = TerrainComplexity.Low, ExtendedComplexity = false };
		public string ConstructionId => ExtendedComplexity ? "natural-regions-extended-v13-r2" : Continuity ? "natural-regions-continuity-v12" : ExperimentId;
		public const string ExperimentId = "natural-reassessment-comparison-v1";
		public string Identity => $"{ConstructionId}/seed={Seed}/size={Size}/method={Method}/complexity={RmgPlayerSettingsContract.ComplexityDisplayName(Complexity, ExtendedComplexity)}/water={WaterPercent}/rock={GravelPercent}/moss={MossPercent}/original={OriginalSurfaceRelations.ToString().ToLowerInvariant()}";

		// Native cells: fixed gameplay scale, independent of map dimensions.
		public double Scale => Continuity ? 64D : Complexity switch
		{
			TerrainComplexity.Low => 112D,
			TerrainComplexity.Standard => 64D,
			TerrainComplexity.High => 36D,
			_ => throw new ArgumentOutOfRangeException(nameof(Complexity))
		};
	}

	public sealed class TerrainComparisonResult
	{
		public TerrainComparisonSettings Settings { get; init; }
		public RmgLogicalMap Map { get; init; }
		public byte[] Intent { get; init; }
		public double[] WaterPriority { get; init; }
		public double[] GeologyPriority { get; init; }
		public JObject Report { get; init; }
	}

	public static partial class TerrainComparison
	{
		public static TerrainComparisonResult Generate(ModData modData, TerrainComparisonSettings settings, RmgLogicalMap reference = null)
		{
			if (settings.Size is not (128 or 256))
				throw new ArgumentException("Terrain comparison supports 128 or 256 native cells.");
			if (settings.ExtendedComplexity && !settings.Continuity)
				throw new ArgumentException("Extended complexity requires continuous Regions.");
			if (!Enum.IsDefined(settings.Complexity) || (!settings.ExtendedComplexity && settings.Complexity > TerrainComplexity.High))
				throw new ArgumentException("This construction does not support the requested complexity.");
			if (settings.Continuity && settings.Method != TerrainConstruction.Regions)
				throw new ArgumentException("Continuity is supported only by Regions.");
			if (settings.Continuity && (settings.ExtendedComplexity || settings.Complexity != TerrainComplexity.Low) && reference == null)
				reference = Generate(modData, settings.ContinuityReference).Map;
			var timer = Stopwatch.StartNew();
			var width = settings.Size / 2;
			var map = new RmgLogicalMap(width, width);
			var waterFraction = settings.WaterPercent / 100D;
			var geologyFraction = (settings.GravelPercent + settings.MossPercent) / 100D;
			var mossFraction = settings.MossPercent / 100D;
			var water = BuildPriorities(settings, width, width, 2, 11, waterFraction);
			var geology = BuildPriorities(settings, width + 1, width + 1, 2, 29, geologyFraction);
			var moistureSettings = settings.Continuity ? settings.ContinuityReference : settings;
			var moisture = BuildPriorities(moistureSettings, width + 1, width + 1, 2, 53, mossFraction);
			var waterMask = Top(water, Enumerable.Repeat(true, water.Length).ToArray(), (int)Math.Round(water.Length * waterFraction));
			Array.Copy(waterMask, map.Obstacles, waterMask.Length);
			var rawWater = (bool[])waterMask.Clone();
			var constructionMs = timer.Elapsed.TotalMilliseconds;
			timer.Restart();

			NormalizeWater(map, water);

			// Reuse only the audited fixed-tile shoreline emitter and variant banks.
			// No legacy generator, water replenishment, route, or connectivity stage runs.
			var profile = RmgProfile.Load(modData, settings.Size == 256 ?
				"sa|rmg/normal-natural-landscape-v10-256.yaml" : "sa|rmg/normal-natural-landscape-v10.yaml");
			var legacyTileSettings = new RmgGenerationSettings
			{
				Seed = Mix(settings.Seed, 101), GeneratorVersion = 10, MapSize = settings.Size,
				TopologyPreset = RmgTopologyPreset.NaturalTerrainV10, OriginalSurfaceRelations = true
			};
			RmgShorelineMaterializer.Materialize(map, profile, legacyTileSettings);
			var land = map.NativeTerrainIntents.Count(t => t != RmgNativeTerrainIntent.Water);
			var lattice = width + 1;
			var allowed = new bool[lattice * lattice];
			for (var y = 0; y < lattice; y++)
				for (var x = 0; x < lattice; x++)
				{
					var valid = true;
					for (var sy = Math.Max(0, y - 1); sy <= Math.Min(width - 1, y); sy++)
						for (var sx = Math.Max(0, x - 1); sx <= Math.Min(width - 1, x); sx++)
							for (var frame = 0; frame < 4; frame++)
							{
								var nx = 2 * sx + frame % 2;
								var ny = 2 * sy + frame / 2;
								for (var dy = settings.OriginalSurfaceRelations ? -1 : 0; dy <= (settings.OriginalSurfaceRelations ? 1 : 0); dy++)
									for (var dx = settings.OriginalSurfaceRelations ? -1 : 0; dx <= (settings.OriginalSurfaceRelations ? 1 : 0); dx++)
										if (nx + dx >= 0 && ny + dy >= 0 && nx + dx < settings.Size && ny + dy < settings.Size &&
											Native(map, nx + dx, ny + dy) == RmgNativeTerrainIntent.Water)
											valid = false;
							}

					allowed[y * lattice + x] = valid;
				}

			var envelope = SelectWeighted(geology, allowed, lattice, (int)Math.Round(land * geologyFraction));
			var rawEnvelope = (bool[])envelope.Clone();
			NormalizeLand(envelope, geology, lattice);
			var coreAllowed = new bool[envelope.Length];
			for (var y = 0; y < lattice; y++)
				for (var x = 0; x < lattice; x++)
				{
					var valid = true;
					for (var dy = -1; dy <= 1; dy++)
						for (var dx = -1; dx <= 1; dx++)
						{
							var px = Math.Clamp(x + dx, 0, lattice - 1);
							var py = Math.Clamp(y + dy, 0, lattice - 1);
							if (!envelope[py * lattice + px]) valid = false;
						}

					coreAllowed[y * lattice + x] = valid;
				}

			// Moisture varies inside the completed geological envelope. No extra region is
			// grown to compensate for a moss shortfall.
			// V12 keeps moisture centers tied to the seed's broad geology. Using the
			// perturbed envelope priority here can relocate whole moss regions.
			var mossGeology = settings.Continuity && (settings.ExtendedComplexity || settings.Complexity != TerrainComplexity.Low) ?
				BuildPriorities(moistureSettings, width + 1, width + 1, 2, 29, geologyFraction) : geology;
			var mossPriority = mossGeology.Select((value, i) => value + .35 * moisture[i]).ToArray();
			if (settings.Continuity && reference != null)
				AnchorMossPriorities(mossPriority, reference.VegetationLattice, lattice);
			var moss = SelectWeighted(mossPriority, coreAllowed, lattice, (int)Math.Round(land * mossFraction));
			var rawMoss = (bool[])moss.Clone();
			NormalizeLand(moss, mossPriority, lattice);
			var intent = new byte[settings.Size * settings.Size];
			for (var y = 0; y < settings.Size; y++)
				for (var x = 0; x < settings.Size; x++)
				{
					var sx = x / 2;
					var sy = y / 2;
					var vertex = (sy + y % 2) * lattice + sx + x % 2;
					intent[y * settings.Size + x] = (byte)(rawWater[sy * width + sx] ? RmgNativeTerrainIntent.Water :
						rawMoss[vertex] ? RmgNativeTerrainIntent.Vegetation :
						rawEnvelope[vertex] ? RmgNativeTerrainIntent.Rock : RmgNativeTerrainIntent.Clear);
				}

			var variants = new DeterministicRandom(Mix(settings.Seed, 97));
			for (var y = 0; y < width; y++)
				for (var x = 0; x < width; x++)
				{
					var rockMask = Mask(envelope, lattice, x, y);
					var mossMask = Mask(moss, lattice, x, y);
					if (mossMask != 0 && rockMask != 15)
						throw new InvalidOperationException("Moss escaped its existing gravel envelope.");
					if (rockMask == 0)
						continue;
					var choices = NormalLandTransitionCatalogue.ForMask(mossMask == 0 ?
						RmgLandTemplateBank.ClearRock : RmgLandTemplateBank.RockVegetation, mossMask == 0 ? rockMask : mossMask);
					var template = choices[variants.NextInt(choices.Count)];
					var index = y * width + x;
					map.TemplateIds[index] = template.TemplateId;
					for (var f = 0; f < 4; f++)
						map.NativeTerrainIntents[4 * index + f] = template.NativeTerrain[f];
				}

			Array.Copy(envelope, map.RockEnvelopeLattice, envelope.Length);
			Array.Copy(moss, map.VegetationLattice, moss.Length);
			var native = NativeBytes(map);
			var forbidden = CountForbiddenContacts(native, settings.Size);
			if (settings.OriginalSurfaceRelations && forbidden != 0)
				throw new InvalidOperationException($"Materialization produced {forbidden} forbidden native surface contacts.");
			var report = new JObject
			{
				["experiment_id"] = settings.ConstructionId,
				["identity"] = settings.Identity,
				["seed"] = settings.Seed.ToString(),
				["size"] = settings.Size,
				["method"] = settings.Method.ToString(),
				["complexity"] = RmgPlayerSettingsContract.ComplexityDisplayName(settings.Complexity, settings.ExtendedComplexity),
				["characteristic_scale_native"] = settings.Scale,
				["status"] = "TERRAIN_COMPARISON_ONLY",
				["accessibility_requirement"] = "NOT_REQUIRED",
				["placement_status"] = "NOT_APPLICABLE_TERRAIN_ONLY",
				["original_surface_relations"] = settings.OriginalSurfaceRelations,
				["forbidden_surface_contacts"] = forbidden,
				["terrain_attempts"] = 1,
				["water_requested_percent_map"] = settings.WaterPercent,
				["gravel_requested_percent_land"] = settings.GravelPercent,
				["moss_requested_percent_land"] = settings.MossPercent,
				["construction_ms"] = constructionMs,
				["materialization_ms"] = timer.Elapsed.TotalMilliseconds,
				["intent_changed_native_cells"] = native.Where((value, i) => value != intent[i]).Count(),
				["shoreline_removed_logical_cells"] = rawWater.Where((value, i) => value && !map.Obstacles[i]).Count(),
				["geology_removed_lattice_points"] = rawEnvelope.Where((value, i) => value && !envelope[i]).Count(),
				["moss_removed_lattice_points"] = rawMoss.Where((value, i) => value && !moss[i]).Count(),
				["semantic_sha256"] = Hash(native),
				["intent_sha256"] = Hash(intent),
				["metrics"] = Measure(native, settings.Size)
			};
			return new TerrainComparisonResult
			{
				Settings = settings, Map = map, Intent = intent, WaterPriority = water,
				GeologyPriority = geology, Report = report
			};
		}

		static double[] BuildPriorities(TerrainComparisonSettings settings, int width, int height, int nativeStep, ulong stream, double fraction)
		{
			if (settings.Continuity)
				return BuildContinuousPriorities(settings, width, height, nativeStep, stream);

			var result = new double[width * height];
			var seed = Mix(settings.Seed, stream);
			var scale = settings.Scale;
			var random = new DeterministicRandom(seed);
			var centers = new List<(double X, double Y, double R, double Aspect, double Angle)>();
			if (settings.Method == TerrainConstruction.Regions)
			{
				var radius = scale * .42;
				var count = Math.Max(1, (int)Math.Ceiling(settings.Size * settings.Size * fraction / (Math.PI * radius * radius * .55)));
				for (var i = 0; i < count; i++)
					centers.Add((Unit(random) * (settings.Size + scale * .4) - scale * .2,
						Unit(random) * (settings.Size + scale * .4) - scale * .2,
						radius * (.65 + .75 * Unit(random)), .55 + .65 * Unit(random), Unit(random) * Math.PI));
			}

			for (var y = 0; y < height; y++)
				for (var x = 0; x < width; x++)
				{
					var nx = x * nativeStep;
					var ny = y * nativeStep;
					var px = nx + scale * .16 * Noise(Mix(seed, 3), nx / (scale * .7), ny / (scale * .7));
					var py = ny + scale * .16 * Noise(Mix(seed, 5), nx / (scale * .7), ny / (scale * .7));
					double value;
					if (settings.Method == TerrainConstruction.Fields)
						value = Noise(seed, px / scale, py / scale) +
							.28 * Noise(Mix(seed, 7), px / (scale * .47), py / (scale * .47)) +
							.10 * Noise(Mix(seed, 9), px / (scale * .23), py / (scale * .23));
					else
					{
						value = double.NegativeInfinity;
						foreach (var (cx, cy, radius, aspect, angle) in centers)
						{
							var dx = px - cx;
							var dy = py - cy;
							var rx = (Math.Cos(angle) * dx + Math.Sin(angle) * dy) / radius;
							var ry = (-Math.Sin(angle) * dx + Math.Cos(angle) * dy) / (radius * aspect);
							value = Math.Max(value, 1 - Math.Sqrt(rx * rx + ry * ry));
						}

						value += .18 * Noise(Mix(seed, 7), nx / (scale * .25), ny / (scale * .25));
					}

					result[y * width + x] = value;
				}

			return result;
		}

		static bool[] Top(double[] values, bool[] allowed, int count)
		{
			var result = new bool[values.Length];
			foreach (var i in Enumerable.Range(0, values.Length).Where(i => allowed[i])
				.OrderByDescending(i => values[i]).ThenBy(i => i).Take(count))
				result[i] = true;
			return result;
		}

		static bool[] SelectWeighted(double[] values, bool[] allowed, int width, int count)
		{
			var result = new bool[values.Length];
			var used = 0;
			foreach (var i in Enumerable.Range(0, values.Length).Where(i => allowed[i])
				.OrderByDescending(i => values[i]).ThenBy(i => i))
			{
				if (used >= count) break;
				var x = i % width;
				var y = i / width;
				used += (x == 0 || x == width - 1 ? 1 : 2) * (y == 0 || y == width - 1 ? 1 : 2);
				result[i] = true;
			}

			return result;
		}

		public static void NormalizeWater(RmgLogicalMap map, double[] priority)
		{
			var pending = new PriorityQueue<int, (double, int)>();
			var queued = new HashSet<int>();
			void Queue(int i)
			{
				if (map.Obstacles[i] && queued.Add(i))
					pending.Enqueue(i, (priority[i], i));
			}

			for (var i = 0; i < map.Obstacles.Length; i++) Queue(i);
			while (pending.TryDequeue(out var index, out _))
			{
				queued.Remove(index);
				var point = new RmgPoint(index % map.Width, index / map.Width);
				if (RmgShorelineMaterializer.Classify(map, point, out _, out _) != RmgShorelineRole.Unsupported)
					continue;
				map.Obstacles[index] = false;
				for (var dy = -1; dy <= 1; dy++)
					for (var dx = -1; dx <= 1; dx++)
					{
						var next = new RmgPoint(point.X + dx, point.Y + dy);
						if (map.Contains(next)) Queue(map.Index(next));
					}
			}
		}

		static void NormalizeLand(bool[] mask, double[] priority, int width)
		{
			var pending = new Queue<int>(Enumerable.Range(0, (width - 1) * (width - 1)));
			while (pending.TryDequeue(out var stamp))
			{
				var x = stamp % (width - 1);
				var y = stamp / (width - 1);
				var bits = Mask(mask, width, x, y);
				if (bits is not (6 or 9)) continue;
				var corners = bits == 6 ? new[] { y * width + x + 1, (y + 1) * width + x } :
					new[] { y * width + x, (y + 1) * width + x + 1 };
				var removed = corners.OrderBy(i => priority[i]).ThenBy(i => i).First();
				mask[removed] = false;
				var vx = removed % width;
				var vy = removed / width;
				for (var sy = Math.Max(0, vy - 1); sy <= Math.Min(width - 2, vy); sy++)
					for (var sx = Math.Max(0, vx - 1); sx <= Math.Min(width - 2, vx); sx++)
						pending.Enqueue(sy * (width - 1) + sx);
			}
		}

		static int Mask(bool[] mask, int width, int x, int y) =>
			(mask[y * width + x] ? 1 : 0) | (mask[y * width + x + 1] ? 2 : 0) |
			(mask[(y + 1) * width + x] ? 4 : 0) | (mask[(y + 1) * width + x + 1] ? 8 : 0);

		public static RmgNativeTerrainIntent Native(RmgLogicalMap map, int x, int y) =>
			map.NativeTerrainIntents[4 * (y / 2 * map.Width + x / 2) + y % 2 * 2 + x % 2];

		public static byte[] NativeBytes(RmgLogicalMap map)
		{
			var result = new byte[map.Width * map.Height * 4];
			for (var y = 0; y < map.Height * 2; y++)
				for (var x = 0; x < map.Width * 2; x++)
					result[y * map.Width * 2 + x] = (byte)Native(map, x, y);
			return result;
		}

		public static int CountForbiddenContacts(byte[] cells, int width)
		{
			var count = 0;
			for (var y = 0; y < width; y++)
				for (var x = 0; x < width; x++)
					foreach (var (dx, dy) in new[] { (1, 0), (-1, 1), (0, 1), (1, 1) })
					{
						var nx = x + dx;
						var ny = y + dy;
						if (nx < 0 || ny >= width || nx >= width) continue;
						var a = (RmgNativeTerrainIntent)cells[y * width + x];
						var b = (RmgNativeTerrainIntent)cells[ny * width + nx];
						if ((a == RmgNativeTerrainIntent.Water && b is RmgNativeTerrainIntent.Rock or RmgNativeTerrainIntent.Vegetation) ||
							(b == RmgNativeTerrainIntent.Water && a is RmgNativeTerrainIntent.Rock or RmgNativeTerrainIntent.Vegetation) ||
							(a == RmgNativeTerrainIntent.Clear && b == RmgNativeTerrainIntent.Vegetation) ||
							(b == RmgNativeTerrainIntent.Clear && a == RmgNativeTerrainIntent.Vegetation))
							count++;
					}

			return count;
		}

		public static JObject Measure(byte[] cells, int width)
		{
			var counts = Enumerable.Range(0, 4).Select(t => cells.Count(v => v == t)).ToArray();
			var summed = new int[width + 1, width + 1];
			var squares = new int[width + 1, width + 1];
			var largest = 0;
			var changes = 0;
			for (var y = 0; y < width; y++)
				for (var x = 0; x < width; x++)
				{
					var dirt = cells[y * width + x] == 0;
					summed[y + 1, x + 1] = (dirt ? 0 : 1) + summed[y, x + 1] + summed[y + 1, x] - summed[y, x];
					if (dirt)
					{
						squares[y + 1, x + 1] = 1 + Math.Min(squares[y, x], Math.Min(squares[y + 1, x], squares[y, x + 1]));
						largest = Math.Max(largest, squares[y + 1, x + 1]);
					}

					if (x > 0 && cells[y * width + x] != cells[y * width + x - 1]) changes++;
					if (y > 0 && cells[y * width + x] != cells[(y - 1) * width + x]) changes++;
				}

			var windows = new JArray();
			foreach (var side in new[] { 16, 32, 64 })
			{
				var empty = 0;
				for (var y = 0; y <= width - side; y++)
					for (var x = 0; x <= width - side; x++)
						if (summed[y + side, x + side] - summed[y, x + side] - summed[y + side, x] + summed[y, x] == 0) empty++;
				windows.Add(new JObject { ["side_native"] = side, ["all_dirt_percent"] = 100D * empty / Math.Pow(width - side + 1, 2) });
			}

			var land = cells.Length - counts[1];
			return new JObject
			{
				["water_percent_map"] = 100D * counts[1] / cells.Length,
				["gravel_percent_land"] = 100D * counts[2] / Math.Max(1, land),
				["moss_percent_land"] = 100D * counts[3] / Math.Max(1, land),
				["largest_all_dirt_square_native"] = largest,
				["surface_edges_per_100_native_edges"] = 100D * changes / (2 * width * (width - 1)),
				["dry_components"] = Components(cells, width, false),
				["water_components"] = Components(cells, width, true),
				["windows"] = windows
			};
		}

		static int Components(byte[] cells, int width, bool water)
		{
			var seen = new bool[cells.Length];
			var queue = new Queue<int>();
			var count = 0;
			for (var i = 0; i < cells.Length; i++)
			{
				if (seen[i] || cells[i] == 1 != water) continue;
				count++;
				Visit(i);
				while (queue.TryDequeue(out var cell))
				{
					var x = cell % width;
					var y = cell / width;
					if (x > 0) Visit(cell - 1);
					if (x + 1 < width) Visit(cell + 1);
					if (y > 0) Visit(cell - width);
					if (y + 1 < width) Visit(cell + width);
				}
			}

			return count;
			void Visit(int cell)
			{
				if (seen[cell] || cells[cell] == 1 != water) return;
				seen[cell] = true;
				queue.Enqueue(cell);
			}
		}

		public static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
		static double Unit(DeterministicRandom random) => (random.NextUInt64() >> 11) * (1D / (1UL << 53));
		static ulong Mix(ulong seed, ulong value)
		{
			var z = unchecked(seed + value + 0x9E3779B97F4A7C15UL);
			z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
			z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
			return z ^ (z >> 31);
		}

		static double Noise(ulong seed, double x, double y)
		{
			var ix = (int)Math.Floor(x);
			var iy = (int)Math.Floor(y);
			var fx = x - ix;
			var fy = y - iy;
			fx = fx * fx * (3 - 2 * fx);
			fy = fy * fy * (3 - 2 * fy);
			double At(int px, int py) => (Mix(seed, unchecked((ulong)((long)px * 73856093 ^ (long)py * 19349663))) >> 11) * (2D / (1UL << 53)) - 1D;
			var a = At(ix, iy) * (1 - fx) + At(ix + 1, iy) * fx;
			var b = At(ix, iy + 1) * (1 - fx) + At(ix + 1, iy + 1) * fx;
			return a * (1 - fy) + b * fy;
		}
	}
}
