#region Copyright & License Information
/*
 * Copyright The OpenSA Developers (see CREDITS)
 * This file is part of OpenSA, under the GNU General Public License, version 3 or later.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenRA.FileSystem;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.OpenSA.Widgets
{
	public sealed class ColonyMapPreviewWidget : MapPreviewWidget
	{
		IReadOnlyPackage cachedPackage;
		string cachedUid;
		CPos[] colonies = Array.Empty<CPos>();

		public ColonyMapPreviewWidget() { }

		ColonyMapPreviewWidget(ColonyMapPreviewWidget other)
			: base(other)
		{
			DisabledSpawnPoints = other.DisabledSpawnPoints;
			ShowUnoccupiedSpawnpoints = other.ShowUnoccupiedSpawnpoints;
			OnMouseDown = other.OnMouseDown;
		}

		public override Widget Clone() => new ColonyMapPreviewWidget(this);

		public override void Draw()
		{
			base.Draw();
			var preview = Preview();
			if (preview == null || !Loaded || preview.Package == null)
				return;

			if (cachedUid != preview.Uid || !ReferenceEquals(cachedPackage, preview.Package))
			{
				cachedUid = preview.Uid;
				cachedPackage = preview.Package;
				colonies = Array.Empty<CPos>();
				try
				{
					using var stream = preview.Package.GetStream("map.yaml");
					if (stream != null)
						colonies = NeutralColonyPreview.Read(MiniYaml.FromStream(stream, "map.yaml"));
				}
				catch (Exception e) when (e is IOException || e is InvalidDataException || e is FormatException)
				{
					Log.Write("debug", $"Unable to read colony preview for {preview.Uid}: {e.Message}");
				}
			}

			foreach (var colony in colonies)
			{
				if (!preview.Bounds.Contains(colony.X, colony.Y))
					continue;
				var position = ConvertToPreview(colony, preview.GridType);

				// Smaller than lettered player starts, with a dark border on every surface.
				WidgetUtils.FillRectWithColor(new Rectangle(position.X - 3, position.Y - 3, 7, 7), Color.Black);
				WidgetUtils.FillRectWithColor(new Rectangle(position.X - 2, position.Y - 2, 5, 5), Color.FromArgb(255, 160, 160, 160));
			}
		}
	}

	public static class NeutralColonyPreview
	{
		static readonly HashSet<string> ColonyTypes = new(StringComparer.OrdinalIgnoreCase)
		{
			"ants_colony", "beetles_colony", "scorpions_colony", "spiders_colony", "wasps_colony"
		};

		public static CPos[] Read(IEnumerable<MiniYamlNode> mapYaml)
		{
			var actors = mapYaml.FirstOrDefault(node => node.Key == "Actors");
			if (actors == null)
				return Array.Empty<CPos>();
			var result = new List<CPos>();
			foreach (var actor in actors.Value.Nodes)
			{
				if (!ColonyTypes.Contains(actor.Value.Value))
					continue;
				var values = actor.Value.ToDictionary();
				if (!values.TryGetValue("Owner", out var owner) ||
					!(owner.Value == "Creeps" || owner.Value == "Neutral") ||
					!values.TryGetValue("Location", out var location))
					continue;
				result.Add(FieldLoader.GetValue<CPos>("Location", location.Value));
			}

			return result.ToArray();
		}
	}
}
