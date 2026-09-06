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
using System.Text.Json;
using OpenRA.Network;
using OpenRA.Traits;

namespace OpenRA.Mods.OpenSA.Widgets.Logic
{
	public sealed class LobbyOptionPreset
	{
		public int FormatVersion { get; set; } = 1;
		public string Mod { get; set; } = "sa";
		public Dictionary<string, string> Options { get; set; } = new();
	}

	public sealed class LobbyPresetPlan
	{
		public readonly Dictionary<string, string> Changes = new();
		public readonly List<string> Skipped = new();
		public int Unchanged;
	}

	// Presets contain option values, never player identities, server details, maps, or executable commands.
	public sealed class LobbyOptionPresets
	{
		static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, MaxDepth = 8 };
		public static string DefaultDirectory => Path.Combine(Platform.SupportDir, "Presets", "sa", "GameOptions");
		public string DirectoryPath { get; }

		public LobbyOptionPresets(string directory = null)
		{
			DirectoryPath = Path.GetFullPath(directory ?? DefaultDirectory);
		}

		public static bool ValidName(string name)
		{
			if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name != name.Trim() ||
				!name.All(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_'))
				return false;
			var reserved = new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }
				.Concat(Enumerable.Range(1, 9).SelectMany(i => new[] { "COM" + i, "LPT" + i }));
			return !reserved.Contains(name, StringComparer.OrdinalIgnoreCase);
		}

		public string FilePath(string name)
		{
			if (!ValidName(name))
				throw new InvalidDataException("Use 1-80 letters, digits, spaces, hyphens or underscores; no reserved device names.");
			return Path.Combine(DirectoryPath, name + ".json");
		}

		public string[] Names() => !Directory.Exists(DirectoryPath) ? Array.Empty<string>() :
			Directory.EnumerateFiles(DirectoryPath, "*.json").Select(Path.GetFileNameWithoutExtension)
				.Where(ValidName).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();

		public void Save(string name, LobbyOptionPreset preset, bool replace)
		{
			Validate(preset);
			var path = FilePath(name);
			Directory.CreateDirectory(DirectoryPath);
			var temporary = Path.Combine(DirectoryPath, Guid.NewGuid().ToString("N") + ".tmp");
			try
			{
				using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
					JsonSerializer.Serialize(stream, preset, JsonOptions);

				// Same-directory rename: errors must not truncate a previously saved preset.
				File.Move(temporary, path, replace);
			}
			finally
			{
				if (File.Exists(temporary))
					File.Delete(temporary);
			}
		}

		public LobbyOptionPreset Load(string name)
		{
			var path = FilePath(name);
			using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
			if (stream.Length > 1024 * 1024)
				throw new InvalidDataException("Preset is too large (maximum 1 MB).");
			try
			{
				var preset = JsonSerializer.Deserialize<LobbyOptionPreset>(stream, JsonOptions);
				Validate(preset);
				return preset;
			}
			catch (JsonException)
			{
				throw new InvalidDataException("This file is not a valid game-options preset.");
			}
		}

		static void Validate(LobbyOptionPreset preset)
		{
			if (preset == null || preset.FormatVersion != 1 || preset.Mod != "sa" || preset.Options == null ||
				preset.Options.Count == 0 || preset.Options.Count > 512 ||
				preset.Options.Any(x => string.IsNullOrEmpty(x.Key) || x.Key.Length > 128 || x.Key.Any(char.IsWhiteSpace) ||
					x.Value == null || x.Value.Length > 4096 || x.Value.Any(char.IsControl)))
				throw new InvalidDataException("Unsupported or invalid OpenSA game-options preset.");
		}

		public static LobbyOptionPreset Capture(Session.Global settings) => new()
		{
			Options = settings.LobbyOptions.OrderBy(x => x.Key, StringComparer.Ordinal)
				.ToDictionary(x => x.Key, x => x.Value.Value)
		};

		public static Dictionary<string, LobbyOption> Definitions(MapPreview map) =>
			map.PlayerActorInfo.TraitInfos<ILobbyOptions>().Concat(map.WorldActorInfo.TraitInfos<ILobbyOptions>())
				.SelectMany(t => t.LobbyOptions(map)).ToDictionary(x => x.Id);

		public static LobbyPresetPlan Plan(LobbyOptionPreset preset, Session.Global settings, IReadOnlyDictionary<string, LobbyOption> definitions)
		{
			Validate(preset);
			var plan = new LobbyPresetPlan();
			foreach (var pair in preset.Options.OrderBy(x => x.Key, StringComparer.Ordinal))
			{
				if (!definitions.TryGetValue(pair.Key, out var definition) || !settings.LobbyOptions.TryGetValue(pair.Key, out var state))
					plan.Skipped.Add(pair.Key + ": unavailable on this map");
				else if (!definition.Values.ContainsKey(pair.Value))
					plan.Skipped.Add(definition.Name + ": value not supported by this map");
				else if (state.Value == pair.Value)
					plan.Unchanged++;
				else if (state.IsLocked || definition.IsLocked)
					plan.Skipped.Add(definition.Name + ": locked by this map/server");
				else
					plan.Changes.Add(pair.Key, pair.Value);
			}

			return plan;
		}
	}
}
