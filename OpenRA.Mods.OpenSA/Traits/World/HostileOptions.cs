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
using System.Globalization;
using System.Linq;
using OpenRA.Network;
using OpenRA.Support;
using OpenRA.Traits;

namespace OpenRA.Mods.OpenSA.Traits.World
{
	public sealed class HostileOption
	{
		public string Id;
		public string Section;
		public string Label;
		public int Minimum;
		public int Maximum;
		public string Default = "map";
		public string WeightGroup;
		public int SpeciesIndex;
		public Dictionary<string, string> Choices;

		public Dictionary<string, string> Values()
		{
			if (Choices != null)
				return Choices;

			var values = new Dictionary<string, string> { { Default, Default == "theme" ? "Terrain default" : "Map default" } };
			for (var n = Minimum; n <= Maximum; n++)
				values[n.ToString(CultureInfo.InvariantCulture)] = n.ToString(CultureInfo.InvariantCulture);
			return values;
		}
	}

	public static class HostileOptions
	{
		public static readonly string[] Pirates = { "ant_bull_pirate", "ants_grenadier_pirate", "ants_bazooka_pirate" };
		public static readonly string[] Plants = { "popcorn", "venus", "thorn", "gumnut", "puff", "mushroom", "freckle", "lolly_blue", "lolly_orange", "lolly_white", "lolly_red" };
		public static readonly string[] Fliers = { "dragonfly", "fly", "moth", "flying_machine" };
		public static readonly HostileOption[] All = Build();

		static HostileOption[] Build()
		{
			var result = new List<HostileOption>();
			void Number(string id, string section, string label, int min, int max) =>
				result.Add(new HostileOption { Id = "h-" + id, Section = section, Label = label, Minimum = min, Maximum = max });
			void Weights(string group, string section, string[] labels)
			{
				for (var i = 0; i < labels.Length; i++)
					result.Add(new HostileOption { Id = "h-" + group + "-" + i, Section = section, Label = labels[i],
						Minimum = 0, Maximum = 1000, Default = "theme", WeightGroup = group, SpeciesIndex = i });
			}

			Number("initial-count", "Initial pirates", "Amount (replaces map pirates)", 0, 500);
			result.Add(new HostileOption { Id = "h-initial-distribution", Section = "Initial pirates", Label = "Distribution",
				Default = "scattered", Choices = new Dictionary<string, string> { { "scattered", "Scattered" }, { "groups", "Groups" }, { "mixed", "Mixed" } } });
			var pirateLabels = new[] { "Bull Ant weight", "Grenadier weight", "Bazooka weight" };
			Weights("initial", "Initial pirates", pirateLabels);
			Number("pirate-interval", "Pirate spawning", "Event interval (seconds)", 1, 3600);
			Number("pirate-delay", "Pirate spawning", "Initial delay (seconds)", 0, 3600);
			Number("pirate-min", "Pirate spawning", "Pirates per anthole: minimum", 1, 50);
			Number("pirate-max", "Pirate spawning", "Pirates per anthole: maximum", 1, 50);
			Number("pirate-cap", "Pirate spawning", "Maximum living spawned pirates", 0, 1000);
			Weights("pirate", "Pirate spawning", pirateLabels);
			Number("plant-interval", "Plant spawning", "Event interval (seconds)", 1, 3600);
			Number("plant-delay", "Plant spawning", "Initial delay (seconds)", 0, 3600);
			Number("plant-cap", "Plant spawning", "Maximum plants (regular spawning)", 0, 1000);
			Weights("plant", "Plant spawning", new[] { "Popcorn weight", "Venus Flytrap weight", "Thorn Bush weight", "Gumnut weight",
				"Seed Ball weight", "Poison Mushroom weight", "Choc Freckle weight", "Blue Lolly weight", "Orange Lolly weight", "White Lolly weight", "Red Lolly weight" });
			Number("flier-interval", "Flier spawning", "Event interval (seconds)", 1, 3600);
			Number("flier-delay", "Flier spawning", "Initial delay (seconds)", 0, 3600);
			Weights("flier", "Flier spawning", new[] { "Dragonfly weight", "Desert Fly weight", "Spawn Moth weight", "Flying Machine weight" });
			return result.ToArray();
		}

		public static int ThemeWeight(string group, int index, string tileset)
		{
			if (group == "initial" || group == "pirate")
				return new[] { 45, 45, 10 }[index];
			var theme = tileset switch { "DESERT" => 1, "SWAMP" => 2, "CANDY" => 3, _ => 0 };
			return index == (group == "plant" ? new[] { 0, 2, 4, 6 }[theme] : theme) ? 100 : 0;
		}

		public static string Value(Session.Global settings, string id, string fallback = "map") =>
			settings.LobbyOptions.TryGetValue(id, out var option) ? option.Value : fallback;

		public static int Number(Session.Global settings, string id, int fallback)
		{
			var definition = All.First(x => x.Id == "h-" + id);
			return int.TryParse(Value(settings, definition.Id), NumberStyles.None, CultureInfo.InvariantCulture, out var n) &&
				n >= definition.Minimum && n <= definition.Maximum ? n : fallback;
		}

		public static int[] Weights(Session.Global settings, string group, string tileset) =>
			All.Where(x => x.WeightGroup == group).Select(x =>
				Number(settings, x.Id.Substring(2), ThemeWeight(group, x.SpeciesIndex, tileset))).ToArray();

		// Integer tickets avoid rounding, floating point drift, and the old zero-weight/off-by-one bug.
		public static int SelectTicket(int[] weights, int ticket)
		{
			if (ticket < 0)
				return -1;
			for (var i = 0; i < weights.Length; i++)
			{
				if (weights[i] < 0)
					throw new ArgumentOutOfRangeException(nameof(weights));
				if (ticket < weights[i])
					return i;
				ticket -= weights[i];
			}

			return -1;
		}

		public static string Choose(string[] species, int[] weights, MersenneTwister random)
		{
			if (species.Length != weights.Length || weights.Any(x => x < 0))
				throw new ArgumentException("Species and non-negative weights must have matching lengths.");
			var total = weights.Sum();
			return total == 0 ? null : species[SelectTicket(weights, random.Next(total))];
		}
	}

	[TraitLocation(SystemActors.World)]
	public sealed class LobbyHostilesInfo : TraitInfo, ILobbyOptions
	{
		public override object Create(ActorInitializer init) => new LobbyHostiles(init.Self);

		IEnumerable<LobbyOption> ILobbyOptions.LobbyOptions(MapPreview map)
		{
			// Campaigns and shellmaps retain their authored rules.
			if (!map.Visibility.HasFlag(MapVisibility.Lobby))
				yield break;
			foreach (var option in HostileOptions.All)
				yield return new LobbyOption(map, option.Id, option.Section + ": " + option.Label,
					"Relative weights need not total 100. Zero excludes a species.", false, 100, option.Values(), option.Default, false);
		}
	}
}
