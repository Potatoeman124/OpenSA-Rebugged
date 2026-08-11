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
using Newtonsoft.Json.Linq;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public enum RmgSymmetry
	{
		MirrorHorizontal,
		MirrorVertical,
		Rotate180
	}

	public enum RmgArchetype
	{
		Open,
		CentralContest
	}

	public readonly struct RmgPoint : IEquatable<RmgPoint>
	{
		public int X { get; }
		public int Y { get; }

		public RmgPoint(int x, int y)
		{
			X = x;
			Y = y;
		}

		public int ManhattanDistance(RmgPoint other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);
		public int ChebyshevDistance(RmgPoint other) => Math.Max(Math.Abs(X - other.X), Math.Abs(Y - other.Y));
		public bool Equals(RmgPoint other) => X == other.X && Y == other.Y;
		public override bool Equals(object obj) => obj is RmgPoint other && Equals(other);
		public override int GetHashCode() => HashCode.Combine(X, Y);
		public override string ToString() => $"{X},{Y}";
		public static bool operator ==(RmgPoint left, RmgPoint right) => left.Equals(right);
		public static bool operator !=(RmgPoint left, RmgPoint right) => !left.Equals(right);
	}

	public sealed class RmgGenerationSettings
	{
		public ulong Seed { get; set; }
		public int PlayerCount { get; set; } = 2;
		public RmgSymmetry Symmetry { get; set; } = RmgSymmetry.MirrorHorizontal;
		public RmgArchetype Archetype { get; set; } = RmgArchetype.Open;
		public int NeutralColonyCount { get; set; } = 10;
		public int GeneratorVersion { get; set; } = 1;

		public string Canonical(RmgProfile profile)
		{
			return string.Join("\n", new[]
			{
				$"profile={profile.ProfileId}",
				$"configuration={profile.ConfigurationVersion}",
				$"generator={GeneratorVersion}",
				$"seed={Seed}",
				$"players={PlayerCount}",
				$"symmetry={Symmetry}",
				$"archetype={Archetype}",
				$"colonies={NeutralColonyCount}"
			});
		}
	}

	public sealed class RmgProfile
	{
		public string ProfileId { get; private set; }
		public int ConfigurationVersion { get; private set; }
		public int GeneratorVersion { get; private set; }
		public string Tileset { get; private set; }
		public int PlayableWidth { get; private set; }
		public int PlayableHeight { get; private set; }
		public int CordonWidth { get; private set; }
		public int LogicalWidth { get; private set; }
		public int LogicalHeight { get; private set; }
		public int MinimumRouteWidthNative { get; private set; }
		public int StartRegionRadiusNative { get; private set; }
		public ushort[] ClearTemplateIds { get; private set; }
		public string[] NeutralColonyActors { get; private set; }
		public string SpawnActor { get; private set; }
		public string SpawnOwner { get; private set; }
		public string ColonyOwner { get; private set; }
		public int ObstacleDensity { get; private set; }
		public int VegetationDensity { get; private set; }

		public static RmgProfile Load(ModData modData, string path = "sa|rmg/normal-clear-v1.yaml")
		{
			using var stream = modData.DefaultFileSystem.Open(path);
			var nodes = MiniYaml.FromStream(stream, path).ToDictionary(n => n.Key, n => n.Value, StringComparer.Ordinal);

			T Get<T>(string key)
			{
				if (!nodes.TryGetValue(key, out var value))
					throw new InvalidOperationException($"RMG profile is missing required field '{key}'.");

				return FieldLoader.GetValue<T>(key, value.Value);
			}

			var profile = new RmgProfile
			{
				ProfileId = Get<string>(nameof(ProfileId)),
				ConfigurationVersion = Get<int>(nameof(ConfigurationVersion)),
				GeneratorVersion = Get<int>(nameof(GeneratorVersion)),
				Tileset = Get<string>(nameof(Tileset)),
				PlayableWidth = Get<int>(nameof(PlayableWidth)),
				PlayableHeight = Get<int>(nameof(PlayableHeight)),
				CordonWidth = Get<int>(nameof(CordonWidth)),
				LogicalWidth = Get<int>(nameof(LogicalWidth)),
				LogicalHeight = Get<int>(nameof(LogicalHeight)),
				MinimumRouteWidthNative = Get<int>(nameof(MinimumRouteWidthNative)),
				StartRegionRadiusNative = Get<int>(nameof(StartRegionRadiusNative)),
				ClearTemplateIds = Get<int[]>(nameof(ClearTemplateIds)).Select(i => checked((ushort)i)).ToArray(),
				NeutralColonyActors = Get<string[]>(nameof(NeutralColonyActors)),
				SpawnActor = Get<string>(nameof(SpawnActor)),
				SpawnOwner = Get<string>(nameof(SpawnOwner)),
				ColonyOwner = Get<string>(nameof(ColonyOwner)),
				ObstacleDensity = Get<int>(nameof(ObstacleDensity)),
				VegetationDensity = Get<int>(nameof(VegetationDensity))
			};

			profile.Validate();
			return profile;
		}

		void Validate()
		{
			if (ProfileId != "normal-clear-v1" || ConfigurationVersion != 1 || GeneratorVersion != 1)
				throw new InvalidOperationException("Only the frozen normal-clear-v1 Generator Version 1 profile is supported.");
			if (Tileset != "NORMAL" || PlayableWidth != 128 || PlayableHeight != 128 || CordonWidth != 2 || LogicalWidth != 64 || LogicalHeight != 64)
				throw new InvalidOperationException("The Version 1 geometry or tileset was changed without a contract revision.");
			if (ObstacleDensity != 0 || VegetationDensity != 0)
				throw new InvalidOperationException("Version 1 is Clear-only: obstacle and vegetation density must be zero.");
			if (ClearTemplateIds.Length == 0 || NeutralColonyActors.Length == 0)
				throw new InvalidOperationException("The RMG profile must declare clear templates and neutral colony actors.");
		}
	}

	public sealed record RmgGraphNode(string Id, string Role, RmgPoint Location);
	public sealed record RmgGraphEdge(string Id, string From, string To, int RouteId);
	public sealed record RmgActorPlan(string Type, string Owner, string Role, RmgPoint LogicalLocation, int EquivalenceGroup);

	public sealed class RmgLogicalMap
	{
		public int Width { get; }
		public int Height { get; }
		public ushort[] TemplateIds { get; }
		public int[] RegionIds { get; }
		public int[] RouteIds { get; }
		public bool[] StartReservations { get; }
		public bool[] StructureReservations { get; }
		public bool[] Obstacles { get; }
		public List<RmgPoint> Starts { get; } = new();
		public List<RmgGraphNode> GraphNodes { get; } = new();
		public List<RmgGraphEdge> GraphEdges { get; } = new();
		public List<RmgActorPlan> Actors { get; } = new();
		public int RepairCount { get; set; }
		public int RetryCount { get; set; }

		public RmgLogicalMap(int width, int height)
		{
			Width = width;
			Height = height;
			TemplateIds = new ushort[width * height];
			RegionIds = Enumerable.Repeat(-1, width * height).ToArray();
			RouteIds = Enumerable.Repeat(-1, width * height).ToArray();
			StartReservations = new bool[width * height];
			StructureReservations = new bool[width * height];
			Obstacles = new bool[width * height];
		}

		public int Index(RmgPoint p) => p.Y * Width + p.X;
		public bool Contains(RmgPoint p) => p.X >= 0 && p.X < Width && p.Y >= 0 && p.Y < Height;
	}

	public sealed record RmgValidationIssue(string Code, string Message)
	{
		public JObject ToJson() => new() { ["code"] = Code, ["message"] = Message };
	}

	public sealed class RmgValidationReport
	{
		public List<RmgValidationIssue> HardFailures { get; } = new();
		public List<RmgValidationIssue> Warnings { get; } = new();
		public Dictionary<string, double> Metrics { get; } = new(StringComparer.Ordinal);
		public bool Accepted => HardFailures.Count == 0;

		public JObject ToJson()
		{
			return new JObject
			{
				["accepted"] = Accepted,
				["hard_failures"] = new JArray(HardFailures.Select(i => i.ToJson())),
				["warnings"] = new JArray(Warnings.Select(i => i.ToJson())),
				["metrics"] = new JObject(Metrics.Select(kv => new JProperty(kv.Key, kv.Value)))
			};
		}
	}

	public sealed class RmgGenerationResult
	{
		public RmgGenerationSettings Settings { get; init; }
		public RmgProfile Profile { get; init; }
		public RmgLogicalMap Map { get; init; }
		public RmgValidationReport Validation { get; init; }
		public string LogicalHash { get; init; }
		public string ActorHash { get; init; }
		public string GraphHash { get; init; }
	}
}
