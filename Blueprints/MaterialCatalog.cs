using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace TerraWorldBlueprint.Blueprints;

/// <summary>Maps placed tiles, walls, paints and coatings back to the item that places them.</summary>
public static class MaterialCatalog
{
	static readonly Dictionary<(int Tile, int Style), int> tileItems = new();
	static readonly Dictionary<int, int> anyStyleTileItems = new();
	static readonly Dictionary<int, int> wallItems = new();
	static readonly Dictionary<int, int> paintItems = new();
	static readonly Dictionary<int, int> coatingItems = new();

	public static void Build()
	{
		Clear();
		// Lowest item id wins so vanilla items are preferred over mod duplicates.
		for (int type = 1; type < ItemLoader.ItemCount; type++) {
			if (!ContentSamples.ItemsByType.TryGetValue(type, out var item)) continue;
			if (item.createTile >= TileID.Dirt) {
				tileItems.TryAdd((item.createTile, item.placeStyle), type);
				anyStyleTileItems.TryAdd(item.createTile, type);
			}
			if (item.createWall > WallID.None) wallItems.TryAdd(item.createWall, type);
			if (item.paint > 0 && item.createTile < TileID.Dirt) paintItems.TryAdd(item.paint, type);
			if (item.paintCoating > 0) coatingItems.TryAdd(item.paintCoating, type);
		}
	}

	public static void Clear()
	{
		tileItems.Clear();
		anyStyleTileItems.Clear();
		wallItems.Clear();
		paintItems.Clear();
		coatingItems.Clear();
	}

	/// <returns>The item type, or 0 when no item places this tile (natural tiles such as grass or pots).</returns>
	public static int ItemForTile(int tileType, short frameX, short frameY)
	{
		int style = StyleOf(tileType, frameX, frameY);
		if (tileItems.TryGetValue((tileType, style), out int item)) return item;
		// Non-object tiles (blocks, platforms with framing) are placed by any item of that tile.
		if (!Main.tileFrameImportant[tileType] || TileObjectData.GetTileData(tileType, 0) == null)
			return anyStyleTileItems.GetValueOrDefault(tileType);
		return 0;
	}

	public static int ItemForWall(int wallType) => wallItems.GetValueOrDefault(wallType);
	public static int ItemForPaint(int paint) => paintItems.GetValueOrDefault(paint);
	public static int ItemForCoating(int coating) => coatingItems.GetValueOrDefault(coating);

	/// <summary>Same math as TileObjectData.GetTileStyle, which needs a live world tile.</summary>
	public static int StyleOf(int tileType, short frameX, short frameY)
	{
		if (!Main.tileFrameImportant[tileType] || frameX < 0 || frameY < 0) return 0;
		var data = TileObjectData.GetTileData(tileType, 0);
		if (data == null) return 0;
		int column = frameX / data.CoordinateFullWidth;
		int row = frameY / data.CoordinateFullHeight;
		int wrap = data.StyleWrapLimit == 0 ? 1 : data.StyleWrapLimit;
		int skip = data.StyleLineSkip == 0 ? 1 : data.StyleLineSkip;
		int style = data.StyleHorizontal ? row / skip * wrap + column : column / skip * wrap + row;
		return style / (data.StyleMultiplier == 0 ? 1 : data.StyleMultiplier);
	}

	/// <summary>
	/// Cell offset of a frame inside its multi-tile object, or (0, 0) for single tiles.
	/// The object is placed and paid for once, from its top-left cell.
	/// </summary>
	public static (int X, int Y) ObjectOffset(int tileType, short frameX, short frameY, out TileObjectData data)
	{
		data = Main.tileFrameImportant[tileType] && frameX >= 0 && frameY >= 0 ? TileObjectData.GetTileData(tileType, 0) : null;
		if (data == null || (data.Width <= 1 && data.Height <= 1)) return (0, 0);
		int x = frameX % data.CoordinateFullWidth / (data.CoordinateWidth + data.CoordinatePadding);
		int localY = frameY % data.CoordinateFullHeight, y = 0;
		while (y < data.Height - 1 && localY >= data.CoordinateHeights[y] + data.CoordinatePadding) {
			localY -= data.CoordinateHeights[y] + data.CoordinatePadding;
			y++;
		}
		return (x, y);
	}
}
