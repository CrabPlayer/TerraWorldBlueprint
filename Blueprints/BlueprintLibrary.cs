using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria;

namespace TerraWorldBlueprint.Blueprints;

/// <summary>.twpack files found in Documents/My Games/Terraria/tModLoader/TerraWorldBlueprints.</summary>
public static class BlueprintLibrary
{
	public sealed record Entry(string FileName, Blueprint Blueprint, string Error);

	public static string Folder => Path.Combine(Main.SavePath, "TerraWorldBlueprints");

	public static List<Entry> Entries { get; private set; } = new();

	public static void Refresh()
	{
		var entries = new List<Entry>();
		try {
			Directory.CreateDirectory(Folder);
			foreach (string path in Directory.EnumerateFiles(Folder, "*.twpack").OrderBy(path => path, StringComparer.OrdinalIgnoreCase)) {
				string file = Path.GetFileName(path);
				try {
					entries.Add(new Entry(file, Blueprint.Load(File.ReadAllBytes(path)), null));
				}
				catch (Exception error) {
					TerraWorldBlueprint.Instance.Logger.Warn($"Could not read blueprint {file}", error);
					entries.Add(new Entry(file, null, error.Message));
				}
			}
		}
		catch (Exception error) {
			TerraWorldBlueprint.Instance.Logger.Warn("Could not scan the blueprint folder", error);
		}
		Entries = entries;
	}

	public static void OpenFolder()
	{
		Directory.CreateDirectory(Folder);
		Utils.OpenFolder(Folder);
	}
}
