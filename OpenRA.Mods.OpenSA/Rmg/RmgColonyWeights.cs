#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OpenRA.Mods.OpenSA.Rmg
{
	// Immutable draft/settings value; order also defines deterministic ticket selection.
	public sealed record RmgColonyWeights(int Ants = 100, int Beetles = 100, int Scorpions = 100, int Spiders = 100, int Wasps = 100)
	{
		public static readonly string[] Keys = { "ants", "beetles", "scorpions", "spiders", "wasps" };
		public int[] Values => new[] { Ants, Beetles, Scorpions, Spiders, Wasps };
		public int Total => Ants + Beetles + Scorpions + Spiders + Wasps;
		public JObject ToJson() => new(Keys.Select((key, i) => new JProperty(key, Values[i])));

		public void Validate(int maximum = 1000)
		{
			if (Values.Any(value => value < 0 || value > maximum))
				throw new ArgumentException($"Neutral colony weights must be whole numbers from 0 through {maximum}.");
		}

		public static RmgColonyWeights Parse(JToken token, int maximum = 1000)
		{
			if (token is not JObject json || json.Properties().Any(p => !Keys.Contains(p.Name)) ||
				Keys.Any(key => json[key]?.Type != JTokenType.Integer || (decimal)json[key] < 0 || (decimal)json[key] > maximum))
				throw new ArgumentException($"neutral_colony_weights must contain ants, beetles, scorpions, spiders, and wasps as integers from 0 through {maximum}.");
			return new((int)json["ants"], (int)json["beetles"], (int)json["scorpions"], (int)json["spiders"], (int)json["wasps"]);
		}

		public string ActorForTicket(int ticket)
		{
			Validate();
			if (ticket < 0 || ticket >= Total)
				throw new ArgumentOutOfRangeException(nameof(ticket));
			var values = Values;
			for (var i = 0; i < values.Length; i++)
			{
				if (ticket < values[i]) return Keys[i] + "_colony";
				ticket -= values[i];
			}

			throw new InvalidOperationException("Colony ticket selection failed.");
		}
	}
}
