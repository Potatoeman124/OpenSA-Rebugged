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
using System.IO;
using System.Linq;
using OpenRA.Mods.OpenSA.Traits.World;
using OpenRA.Mods.OpenSA.Widgets.Logic;
using OpenRA.Network;

namespace OpenRA.Mods.OpenSA.UtilityCommands
{
	public sealed class ValidateLobbyPresetsCommand : IUtilityCommand
	{
		string IUtilityCommand.Name => "--validate-sa-presets";
		bool IUtilityCommand.ValidateArguments(string[] args) => args.Length == 2;

		[Desc("OUTPUT-DIRECTORY", "Check game option preset files, round trips, overwrite protection and cross-map compatibility.")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			Game.ModData = utility.ModData;
			TranslationProvider.Initialize(utility.ModData, utility.ModData.DefaultFileSystem);
			var store = new LobbyOptionPresets(Path.Combine(Path.GetFullPath(args[1]), "presets-" + Guid.NewGuid().ToString("N")));
			var checks = 0;
			void Check(bool condition, string description)
			{
				checks++;
				if (!condition)
					throw new InvalidOperationException(description);
			}

			void Reject(Action action, string description)
			{
				var rejected = false;
				try { action(); }
				catch (Exception ex) when (ex is IOException or InvalidDataException) { rejected = true; }
				Check(rejected, description);
			}

			utility.ModData.MapCache.LoadMaps();
			var maps = utility.ModData.MapCache.Where(m => m.Status == MapStatus.Available && m.Visibility == MapVisibility.Lobby).ToArray();
			var map = maps.First(m => m.TileSet == "NORMAL");
			var definitions = LobbyOptionPresets.Definitions(map);
			var settings = new Session.Global();
			foreach (var definition in definitions.Values)
				settings.LobbyOptions[definition.Id] = new Session.LobbyOptionState { Value = definition.DefaultValue };
			settings.LobbyOptions["h-initial-count"].Value = "17";
			settings.LobbyOptions["h-plant-0"].Value = "75";
			settings.LobbyOptions["h-plant-1"].Value = "50";
			settings.LobbyOptions["h-plant-2"].Value = "40";
			settings.LobbyOptions["h-plant-3"].Value = "10";
			foreach (var option in HostileOptions.All.Where(o => o.WeightGroup == "plant" && o.SpeciesIndex >= 4))
				settings.LobbyOptions[option.Id].Value = "0";
			settings.LobbyOptions["creeps"].Value = "False";
			var snapshot = LobbyOptionPresets.Capture(settings);
			Check(snapshot.Options.Count == settings.LobbyOptions.Count, "Must save every synchronized option, including hidden hostile values.");
			Check(store.Names().Length == 0, "Missing directory must be an empty list.");
			store.Save("Test options", snapshot, false);
			var loaded = store.Load("Test options");
			Check(snapshot.Options.All(p => loaded.Options[p.Key] == p.Value), "All values must round trip exactly.");
			Check(loaded.Options["h-flier-0"] == "theme" && loaded.Options["h-pirate-delay"] == "map", "Default markers must survive.");
			Check(store.Names().SequenceEqual(new[] { "Test options" }), "Saved preset must appear in list.");
			Check(LobbyOptionPresets.Plan(loaded, settings, definitions).Changes.Count == 0, "Loading identical settings must issue no orders.");
			var json = File.ReadAllText(store.FilePath("Test options"));
			Check(!json.Contains("Clients") && !json.Contains("MapUID") && !json.Contains("ServerName"), "No players, map selection or server details.");
			settings.LobbyOptions["h-initial-count"].Value = "0";
			Check(loaded.Options["h-initial-count"] == "17", "Snapshot must not reference live lobby state.");
			Reject(() => store.Save("Test options", LobbyOptionPresets.Capture(settings), false), "Overwrite must require explicit permission.");
			Check(store.Load("Test options").Options["h-initial-count"] == "17", "Refused overwrite must preserve existing file.");
			store.Save("Test options", LobbyOptionPresets.Capture(settings), true);
			Check(store.Load("Test options").Options["h-initial-count"] == "0", "Explicit replacement must work.");
			Check(!Directory.EnumerateFiles(store.DirectoryPath, "*.tmp").Any(), "Temporary files must be cleaned up.");

			foreach (var name in new[] { "", "../escape", "a/b", "a\\b", "name.json", "CON", "LPT1", "name ", new string('x', 81) })
				Reject(() => store.Save(name, snapshot, false), "Unsafe preset filename must be rejected: " + name);
			Check(LobbyOptionPresets.ValidName("Weekend 2 - custom_weights"), "Readable preset names must be allowed.");

			foreach (var terrain in new[] { "NORMAL", "DESERT", "SWAMP", "CANDY" })
			{
				var target = maps.First(m => m.TileSet == terrain);
				var targetDefinitions = LobbyOptionPresets.Definitions(target);
				var targetSettings = new Session.Global();
				foreach (var definition in targetDefinitions.Values)
					targetSettings.LobbyOptions[definition.Id] = new Session.LobbyOptionState { Value = definition.DefaultValue };
				var plan = LobbyOptionPresets.Plan(snapshot, targetSettings, targetDefinitions);
				foreach (var pair in plan.Changes)
					targetSettings.LobbyOptions[pair.Key].Value = pair.Value;
				Check(HostileOptions.Weights(targetSettings, "plant", terrain).SequenceEqual(new[] { 75, 50, 40, 10, 0, 0, 0, 0, 0, 0, 0 }),
					"Cross-theme load must keep all explicit plant weights.");
				Check(HostileOptions.Weights(targetSettings, "flier", terrain).Count(x => x > 0) == 1,
					"Terrain-default flier weights must continue to follow the map.");
				Check(targetSettings.LobbyOptions["h-initial-count"].Value == "17" && targetSettings.LobbyOptions["creeps"].Value == "False",
					"Both hostile settings and original lobby switches must be restored.");
			}

			var incompatible = new LobbyOptionPreset();
			incompatible.Options["h-initial-count"] = "12";
			incompatible.Options["h-plant-0"] = "1001";
			incompatible.Options["future-option"] = "anything";
			settings.LobbyOptions["h-initial-count"].IsLocked = true;
			var blocked = LobbyOptionPresets.Plan(incompatible, settings, definitions);
			Check(blocked.Changes.Count == 0 && blocked.Skipped.Count == 3, "Locked, invalid and unknown values must not issue commands.");
			incompatible.Options["h-plant-0"] = "75\noption creeps True";
			Reject(() => LobbyOptionPresets.Plan(incompatible, settings, definitions), "Injected command text must be rejected.");
			File.WriteAllText(store.FilePath("Broken"), "{not json");
			Reject(() => store.Load("Broken"), "Malformed JSON must be rejected.");
			File.WriteAllText(store.FilePath("Future"), "{\"FormatVersion\":2,\"Mod\":\"sa\",\"Options\":{\"creeps\":\"True\"}}");
			Reject(() => store.Load("Future"), "Unknown preset versions must be rejected.");
			File.WriteAllText(store.FilePath("Other mod"), "{\"FormatVersion\":1,\"Mod\":\"other\",\"Options\":{\"creeps\":\"True\"}}");
			Reject(() => store.Load("Other mod"), "Other mod presets must be rejected.");
			File.WriteAllText(store.FilePath("Empty"), "{}");
			Reject(() => store.Load("Empty"), "Empty objects must be rejected.");
			File.WriteAllText(store.FilePath("Oversized"), new string(' ', 1024 * 1024 + 1));
			Reject(() => store.Load("Oversized"), "Oversized files must be rejected.");
			Console.WriteLine($"PASS: {checks} game-preset checks: all option values, 4 terrains, defaults, compatibility, file safety and overwrite protection.");
			Console.WriteLine("Test files (isolated from user presets): " + store.DirectoryPath);
		}
	}
}
