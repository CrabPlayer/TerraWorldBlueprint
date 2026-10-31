using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerraWorldBlueprint.Blueprints;

/// <summary>One cell of a TerraWorld structure. Mirrors a record of payload/tiles.bin.</summary>
public sealed class BlueprintCell
{
	public int X, Y;
	/// <summary>Resolved runtime tile type, or -1 when the tile's mod is not loaded.</summary>
	public int Type;
	/// <summary>Resolved runtime wall type, or -1 when the wall's mod is not loaded.</summary>
	public int Wall;
	public short U, V;
	public byte LiquidAmount, LiquidType, Wires, TileColor, WallColor, BrickStyle;
	public bool Active, Actuator, InActive, InvisibleBlock, InvisibleWall, FullBrightBlock, FullBrightWall;

	public bool HasTile => Active && Type >= 0;
	public bool HasWall => Wall > 0;
}

/// <summary>A structure package exported by TerraWorld Structure Studio (.twpack).</summary>
public sealed class Blueprint
{
	const string Format = "terraworld-data-package";
	const int MaxCells = 10_000_000;
	const int RecordSize = 28;

	public string Name { get; private set; }
	public string Description { get; private set; }
	public int Width { get; private set; }
	public int Height { get; private set; }
	/// <summary>Dense Width*Height grid; null where the package has no cell.</summary>
	public BlueprintCell[] Grid { get; private set; }
	public List<BlueprintCell> Cells { get; } = new();
	public Dictionary<(int X, int Y), string> SignTexts { get; } = new();
	public Dictionary<(int X, int Y), string> ChestNames { get; } = new();
	/// <summary>Mod content referenced by the package that is not loaded right now.</summary>
	public SortedSet<string> MissingContent { get; } = new();
	public byte[] Source { get; private set; }
	public string Hash { get; private set; }

	public BlueprintCell At(int x, int y) => x < 0 || y < 0 || x >= Width || y >= Height ? null : Grid[x + y * Width];

	public static Blueprint Load(byte[] data)
	{
		using var zip = new ZipArchive(new MemoryStream(data, false), ZipArchiveMode.Read);
		using var manifest = ReadJson(zip, "manifest.json");
		var root = manifest.RootElement;
		if (root.GetProperty("format").GetString() != Format || root.GetProperty("kind").GetString() != "structure")
			throw new InvalidDataException("Not a TerraWorld structure package.");
		if (root.GetProperty("schemaVersion").GetInt32() != 1)
			throw new InvalidDataException("Unsupported .twpack schema version; update the mod.");
		var files = root.GetProperty("files");

		var blueprint = new Blueprint { Source = data, Hash = Convert.ToHexString(SHA1.HashData(data)) };
		using (var structure = ReadJson(zip, files.GetProperty("structure").GetString())) {
			var meta = structure.RootElement;
			blueprint.Name = meta.TryGetProperty("name", out var name) ? name.GetString() : "Structure";
			blueprint.Description = meta.TryGetProperty("description", out var description) ? description.GetString() : "";
			blueprint.Width = meta.GetProperty("width").GetInt32();
			blueprint.Height = meta.GetProperty("height").GetInt32();
		}
		if (blueprint.Width < 1 || blueprint.Height < 1 || (long)blueprint.Width * blueprint.Height > MaxCells)
			throw new InvalidDataException("Invalid structure dimensions.");
		blueprint.Grid = new BlueprintCell[blueprint.Width * blueprint.Height];

		string tilesPath = files.GetProperty("tiles").GetString();
		if (!tilesPath.EndsWith(".bin"))
			throw new InvalidDataException("Re-export this structure with a current version of Structure Studio.");
		blueprint.ReadTiles(ReadBytes(zip, tilesPath));

		using (var entities = ReadJson(zip, files.GetProperty("entities").GetString()))
			blueprint.ReadEntities(entities.RootElement);
		return blueprint;
	}

	void ReadTiles(byte[] input)
	{
		if (input.Length < 12 || input[0] != 'T' || input[1] != 'W' || input[2] != 'S' || input[3] != 'T')
			throw new InvalidDataException("Invalid tile payload.");
		int version = BitConverter.ToUInt16(input, 4);
		int recordSize = BitConverter.ToUInt16(input, 6);
		long count = BitConverter.ToUInt32(input, 8);
		if (version != 1 || recordSize != RecordSize || count > MaxCells || 12 + count * RecordSize != input.Length)
			throw new InvalidDataException("Unsupported tile payload.");

		for (int index = 0, offset = 12; index < count; index++, offset += RecordSize) {
			int flags = BitConverter.ToUInt16(input, offset + 22);
			var cell = new BlueprintCell {
				X = (int)BitConverter.ToUInt32(input, offset),
				Y = (int)BitConverter.ToUInt32(input, offset + 4),
				Type = BitConverter.ToUInt16(input, offset + 8),
				Wall = BitConverter.ToUInt16(input, offset + 10),
				U = BitConverter.ToInt16(input, offset + 12),
				V = BitConverter.ToInt16(input, offset + 14),
				LiquidAmount = input[offset + 16],
				LiquidType = input[offset + 17],
				Wires = input[offset + 18],
				TileColor = input[offset + 19],
				WallColor = input[offset + 20],
				BrickStyle = input[offset + 21],
				Active = (flags & 1) != 0,
				Actuator = (flags & (1 << 1)) != 0,
				InActive = (flags & (1 << 2)) != 0,
				InvisibleBlock = (flags & (1 << 3)) != 0,
				InvisibleWall = (flags & (1 << 4)) != 0,
				FullBrightBlock = (flags & (1 << 5)) != 0,
				FullBrightWall = (flags & (1 << 6)) != 0,
			};
			if (cell.X < 0 || cell.Y < 0 || cell.X >= Width || cell.Y >= Height)
				throw new InvalidDataException("Tile cell outside the structure bounds.");
			// Numeric ids above the vanilla range are world-local mod ids; entities.json names them.
			if (cell.Active && cell.Type >= TileID.Count) cell.Type = -1;
			if (cell.Wall >= WallID.Count) cell.Wall = -1;
			Grid[cell.X + cell.Y * Width] = cell;
			Cells.Add(cell);
		}
	}

	void ReadEntities(JsonElement root)
	{
		if (root.TryGetProperty("modTiles", out var modTiles)) {
			foreach (var entry in modTiles.EnumerateArray()) {
				var cell = At(entry.GetProperty("relX").GetInt32(), entry.GetProperty("relY").GetInt32());
				var content = entry.GetProperty("modContent");
				string mod = content.GetProperty("modName").GetString(), item = content.GetProperty("contentName").GetString();
				if (cell == null) continue;
				if (ModContent.TryFind<ModTile>(mod, item, out var tile)) cell.Type = tile.Type;
				else { cell.Type = -1; MissingContent.Add($"{mod}/{item}"); }
			}
		}
		if (root.TryGetProperty("modWalls", out var modWalls)) {
			foreach (var entry in modWalls.EnumerateArray()) {
				var cell = At(entry.GetProperty("relX").GetInt32(), entry.GetProperty("relY").GetInt32());
				var content = entry.GetProperty("modContent");
				string mod = content.GetProperty("modName").GetString(), item = content.GetProperty("contentName").GetString();
				if (cell == null) continue;
				if (ModContent.TryFind<ModWall>(mod, item, out var wall)) cell.Wall = wall.Type;
				else { cell.Wall = -1; MissingContent.Add($"{mod}/{item}"); }
			}
		}
		if (root.TryGetProperty("signs", out var signs)) {
			foreach (var entry in signs.EnumerateArray()) {
				if (entry.TryGetProperty("sign", out var sign) && sign.TryGetProperty("text", out var text))
					SignTexts[(entry.GetProperty("relX").GetInt32(), entry.GetProperty("relY").GetInt32())] = text.GetString() ?? "";
			}
		}
		// Only chest names are copied. Contents would let a blueprint duplicate items.
		if (root.TryGetProperty("chests", out var chests)) {
			foreach (var entry in chests.EnumerateArray()) {
				if (entry.TryGetProperty("chest", out var chest) && chest.TryGetProperty("name", out var name) && !string.IsNullOrEmpty(name.GetString()))
					ChestNames[(entry.GetProperty("relX").GetInt32(), entry.GetProperty("relY").GetInt32())] = name.GetString();
			}
		}
	}

	static JsonDocument ReadJson(ZipArchive zip, string path) => JsonDocument.Parse(ReadBytes(zip, path));

	static byte[] ReadBytes(ZipArchive zip, string path)
	{
		var entry = zip.GetEntry(path) ?? throw new InvalidDataException($"Missing package file: {path}");
		if (entry.Length > 256L * 1024 * 1024) throw new InvalidDataException("Package is too large.");
		using var stream = entry.Open();
		using var buffer = new MemoryStream((int)entry.Length);
		stream.CopyTo(buffer);
		return buffer.ToArray();
	}
}
