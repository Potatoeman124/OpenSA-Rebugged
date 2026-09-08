#region Copyright & License Information
/* Copyright The OpenSA Developers. GPL version 3 or later. */
#endregion

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using OpenRA.FileSystem;

namespace OpenRA.Mods.OpenSA.Rmg
{
	public sealed record RmgSavedMap(string Uid, string Title, string Path);

	public static class RmgMapSaver
	{
		public const int MaximumNameLength = 96;

		public static string NameError(string name)
		{
			var title = name?.Trim();
			if (string.IsNullOrEmpty(title)) return "Enter a name for the map.";
			if (title.Length > MaximumNameLength) return $"Use at most {MaximumNameLength} characters.";
			if (title.Any(char.IsControl)) return "The map name must be a single line.";
			return null;
		}

		public static RmgSavedMap Save(ModData modData, MapPreview source, string name)
		{
			var error = NameError(name);
			if (error != null) throw new ArgumentException(error);
			if (source?.Status != MapStatus.Available || source.Package == null)
				throw new InvalidOperationException("The generated preview is no longer available.");
			var directory = modData.MapCache.MapLocations
				.Where(location => location.Value == MapClassification.User).Select(location => location.Key).OfType<Folder>().FirstOrDefault();
			if (directory == null) throw new InvalidOperationException("The custom maps directory is unavailable.");
			var title = name.Trim();
			var invalid = Path.GetInvalidFileNameChars().Concat("<>:\"/\\|?*").ToHashSet();
			var filename = "RMG-" + new string(title.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).TrimEnd(' ', '.');
			var root = Path.GetFullPath(Platform.ResolvePath(directory.Name));
			Directory.CreateDirectory(root);
			var temporary = Path.Combine(root, ".rmg-save-" + Guid.NewGuid().ToString("N") + ".tmp");
			try
			{
				// Copy the validated package without regenerating terrain, actors, rules or its preview.
				// MiniYaml handles escaping names containing # and preserves comments/blank lines.
				using (var buffer = new MemoryStream())
				{
					using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, true))
						foreach (var entryName in source.Package.Contents)
						{
							using var input = source.Package.GetStream(entryName);
							using var output = archive.CreateEntry(entryName, CompressionLevel.Optimal).Open();
							if (entryName == "map.yaml")
							{
								var yaml = MiniYaml.FromStream(input, discardCommentsAndWhitespace: false);
								yaml.Single(node => node.Key == "Title").Value.Value = title;
								output.Write(Encoding.UTF8.GetBytes(yaml.WriteToString()));
							}
							else
								input.CopyTo(output);
						}

					File.WriteAllBytes(temporary, buffer.ToArray());
				}

				string uid;
				using (var package = directory.OpenPackage(Path.GetFileName(temporary), modData.ModFiles))
				using (var map = new Map(modData, package))
				{
					if (map.Title != title) throw new InvalidDataException("The map name could not be saved correctly.");
					uid = map.Uid;
				}

				string destination;
				for (var copy = 1; ; copy++)
				{
					var suffix = copy == 1 ? string.Empty : $" ({copy})";
					destination = Path.Combine(root, filename + suffix + ".oramap");
					try { File.Move(temporary, destination); break; }
					catch (IOException) when (File.Exists(destination) || Directory.Exists(destination)) { }
				}

				// This is a new copy, not a replacement of the selected preview UID.
				modData.MapCache.LoadMap(Path.GetFileName(destination), directory, MapClassification.User, modData.Manifest.Get<MapGrid>(), null);
				if (modData.MapCache[uid].Status != MapStatus.Available)
					throw new IOException($"Map saved as {Path.GetFileName(destination)}, but the custom map list could not refresh.");
				return new RmgSavedMap(uid, title, destination);
			}
			finally
			{
				if (File.Exists(temporary)) File.Delete(temporary);
			}
		}
	}
}
