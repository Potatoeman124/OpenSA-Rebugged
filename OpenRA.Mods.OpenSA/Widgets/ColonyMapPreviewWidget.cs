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
using OpenRA.Mods.OpenSA.Rmg;
using OpenRA.Network;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.OpenSA.Widgets
{
	public sealed class ColonyMapPreviewWidget : MapPreviewWidget
	{
		IReadOnlyPackage cachedPackage;
		string cachedUid;
		ColonyPreviewSite[] colonies = Array.Empty<ColonyPreviewSite>();
		string cachedLobby;
		RmgOwnershipPreview ownership;
		Func<Dictionary<int, SpawnOccupant>> fallbackOccupants;
		Func<Dictionary<int, SpawnOccupant>> combinedOccupants;
		public Func<Session> LobbySession = () => null;
		public RmgOwnershipPreview Ownership => ownership;

		public ColonyMapPreviewWidget() { }

		ColonyMapPreviewWidget(ColonyMapPreviewWidget other)
			: base(other)
		{
			DisabledSpawnPoints = other.DisabledSpawnPoints;
			ShowUnoccupiedSpawnpoints = other.ShowUnoccupiedSpawnpoints;
			OnMouseDown = other.OnMouseDown;
			LobbySession = other.LobbySession;
			SpawnOccupants = other.fallbackOccupants ?? other.SpawnOccupants;
		}

		public override Widget Clone() => new ColonyMapPreviewWidget(this);

		public override void Initialize(WidgetArgs args)
		{
			base.Initialize(args);
			if (args.TryGetValue("orderManager", out var value) && value is OrderManager manager)
				LobbySession = () => manager.LobbyInfo;
		}

		public override void Draw()
		{
			var preview = Preview();
			if (preview == null || preview.Package == null)
			{
				ownership = null;
				cachedLobby = null;
				base.Draw();
				return;
			}

			if (cachedUid != preview.Uid || !ReferenceEquals(cachedPackage, preview.Package))
			{
				cachedUid = preview.Uid;
				cachedPackage = preview.Package;
				colonies = Array.Empty<ColonyPreviewSite>();
				cachedLobby = null;
				ownership = null;
				try
				{
					using var stream = preview.Package.GetStream("map.yaml");
					if (stream != null)
						colonies = NeutralColonyPreview.ReadSites(MiniYaml.FromStream(stream, "map.yaml"));
				}
				catch (Exception e) when (e is IOException || e is InvalidDataException || e is FormatException)
				{
					Log.Write("debug", $"Unable to read colony preview for {preview.Uid}: {e.Message}");
				}
			}

			var session = LobbySession();
			var key = session == null ? null : RmgOwnershipPreview.CacheKey(session);
			if (key != cachedLobby)
			{
				cachedLobby = key;
				ownership = null;
				try { if (session != null) ownership = RmgOwnershipPreview.Resolve(preview, session, colonies); }
				catch (Exception e) when (e is InvalidOperationException || e is ArgumentException || e is KeyNotFoundException)
				{
					Log.Write("debug", $"Ownership preview is waiting for valid lobby settings: {e.Message}");
				}
			}
			combinedOccupants ??= () => ownership?.SpawnOccupants ?? fallbackOccupants();
			if (SpawnOccupants != combinedOccupants) fallbackOccupants = SpawnOccupants;
			SpawnOccupants = combinedOccupants;
			base.Draw();
			if (!Loaded) return;

			foreach (var site in colonies)
			{
				var colony = site.Location;
				if (!preview.Bounds.Contains(colony.X, colony.Y))
					continue;
				var position = ConvertToPreview(colony, preview.GridType);

				// Smaller than lettered player starts, with a dark border on every surface.
				WidgetUtils.FillRectWithColor(new Rectangle(position.X - 3, position.Y - 3, 7, 7), Color.Black);
				var color = ownership != null && ownership.ColonyColors.TryGetValue(site.Name, out var ownerColor) ? ownerColor : Color.FromArgb(255, 160, 160, 160);
				WidgetUtils.FillRectWithColor(new Rectangle(position.X - 2, position.Y - 2, 5, 5), color);
			}
		}
	}

	public sealed record ColonyPreviewSite(string Name, CPos Location);

	public static class NeutralColonyPreview
	{
		static readonly HashSet<string> ColonyTypes = new(StringComparer.OrdinalIgnoreCase)
		{
			"ants_colony", "beetles_colony", "scorpions_colony", "spiders_colony", "wasps_colony"
		};

		public static CPos[] Read(IEnumerable<MiniYamlNode> mapYaml) => ReadSites(mapYaml).Select(s => s.Location).ToArray();

		public static ColonyPreviewSite[] ReadSites(IEnumerable<MiniYamlNode> mapYaml)
		{
			var actors = mapYaml.FirstOrDefault(node => node.Key == "Actors");
			if (actors == null)
				return Array.Empty<ColonyPreviewSite>();
			var result = new List<ColonyPreviewSite>();
			foreach (var actor in actors.Value.Nodes)
			{
				if (!ColonyTypes.Contains(actor.Value.Value))
					continue;
				var values = actor.Value.ToDictionary();
				if (!values.TryGetValue("Owner", out var owner) ||
					!(owner.Value == "Creeps" || owner.Value == "Neutral") ||
					!values.TryGetValue("Location", out var location))
					continue;
				result.Add(new ColonyPreviewSite(actor.Key, FieldLoader.GetValue<CPos>("Location", location.Value)));
			}

			return result.ToArray();
		}
	}
}
