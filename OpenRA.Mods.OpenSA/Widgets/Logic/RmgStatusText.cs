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

namespace OpenRA.Mods.OpenSA.Widgets.Logic
{
	// Keep status layout testable without opening a renderer or changing engine LabelWidget.
	public static class RmgStatusText
	{
		public static string Fit(string text, int width, int height, Func<string, int2> measure)
		{
			if (string.IsNullOrEmpty(text) || width <= 0 || height <= 0)
				return string.Empty;
			var lines = new List<string>();
			foreach (var paragraph in text.Replace("\r", "").Split('\n'))
			{
				var remaining = paragraph.Trim();
				if (remaining.Length == 0)
					lines.Add(string.Empty);
				while (remaining.Length > 0)
				{
					var length = 0;
					while (length < remaining.Length && measure(remaining[..(length + 1)]).X <= width)
						length++;
					if (length == 0)
						return string.Empty;
					var split = length;
					if (length < remaining.Length)
					{
						var space = remaining.LastIndexOf(' ', length - 1, length);
						if (space > 0)
							split = space;
					}
					lines.Add(remaining[..split].TrimEnd());
					remaining = remaining[split..].TrimStart();
				}
			}

			var truncated = false;
			while (lines.Count > 0 && measure(string.Join("\n", lines)).Y > height)
			{
				lines.RemoveAt(lines.Count - 1);
				truncated = true;
			}
			if (truncated && lines.Count > 0)
			{
				var last = lines[^1];
				while (last.Length > 0 && measure(last + "...").X > width)
					last = last[..^1];
				lines[^1] = measure(last + "...").X <= width ? last + "..." : string.Empty;
			}
			return string.Join("\n", lines);
		}

		public static IReadOnlyList<string> RunSelfTests()
		{
			var failures = new List<string>();
			int2 Measure(string text) => new(text.Split('\n').Max(line => line.Length) * 7,
				text.Split('\n').Length * 14);
			var messages = new[]
			{
				"Ready.",
				"Ready: OpenSA RMG Balanced Natural Landscape W-Standard T-Standard 656060532586988781 " +
					"(3.0s; Water 20.1% total/20.8% interior; Rock/Vegetation 14.0/8.0%; colonies 8/16; adjusted safely)",
				"Generation failed: " + new string('W', 200),
				"First line\nSecond line\nThird line\nFourth line"
			};
			foreach (var text in messages)
				foreach (var width in new[] { 70, 210, 770 })
				{
					var fitted = Fit(text, width, 38, Measure);
					if (Measure(fitted).X > width || Measure(fitted).Y > 38)
						failures.Add("RMG status text exceeded its bounded display area.");
				}
			if (Fit("Ready.", 770, 38, Measure) != "Ready." ||
				!Fit(messages[3], 770, 38, Measure).EndsWith("...", StringComparison.Ordinal))
				failures.Add("RMG status text did not preserve short messages or signal truncation.");
			return failures;
		}
	}
}
