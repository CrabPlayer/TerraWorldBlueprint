using System.Collections.Generic;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Tile_Entities;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace TerraWorldBlueprint.Blueprints;

public enum CellState { Done, Missing, Blocked, Unobtainable }

public enum StepKind { Wall, Tile, Decoration, Liquid }

/// <summary>One unit of work. A multi-tile object is a single Tile step at its top-left cell.</summary>
public readonly record struct BuildStep(StepKind Kind, BlueprintCell Cell);

/// <summary>Compares a blueprint projected at (OriginX, OriginY) against the live world and builds it.</summary>
public sealed class BuildPlan
{
	public readonly Blueprint Blueprint;
	public readonly int OriginX, OriginY;

	public BuildPlan(Blueprint blueprint, int originX, int originY)
	{
		Blueprint = blueprint;
		OriginX = originX;
		OriginY = originY;
	}

	static bool PlaceUnobtainable => ModContent.GetInstance<BlueprintConfig>().PlaceUnobtainableForFree;
	static bool PlaceLiquids => ModContent.GetInstance<BlueprintConfig>().PlaceLiquids;

	/// <summary>Walls first, then blocks and furniture bottom-up, then paint and wiring, then liquid.</summary>
	public List<BuildStep> Steps()
	{
		var steps = new List<BuildStep>();
		foreach (var cell in Blueprint.Cells) if (cell.HasWall) steps.Add(new BuildStep(StepKind.Wall, cell));
		for (int y = Blueprint.Height - 1; y >= 0; y--) {
			for (int x = 0; x < Blueprint.Width; x++) {
				var cell = Blueprint.At(x, y);
				if (cell is { HasTile: true } && MaterialCatalog.ObjectOffset(cell.Type, cell.U, cell.V, out _) == (0, 0))
					steps.Add(new BuildStep(StepKind.Tile, cell));
			}
		}
		foreach (var cell in Blueprint.Cells) if (HasDecoration(cell)) steps.Add(new BuildStep(StepKind.Decoration, cell));
		if (PlaceLiquids) foreach (var cell in Blueprint.Cells) if (cell.LiquidAmount > 0 && !cell.HasTile) steps.Add(new BuildStep(StepKind.Liquid, cell));
		return steps;
	}

	static bool HasDecoration(BlueprintCell cell) =>
		cell.Wires != 0 || cell.Actuator || cell.TileColor != 0 || cell.WallColor != 0
		|| cell.InvisibleBlock || cell.InvisibleWall || cell.FullBrightBlock || cell.FullBrightWall;

	/// <summary>Items still needed for a step against the current world, with the step's state.</summary>
	/// <param name="planning">
	/// Count paint, coatings and actuators for tiles and walls that are not built yet, so the material
	/// list is complete up front. While building they are only charged once their tile or wall exists.
	/// </param>
	public CellState Evaluate(BuildStep step, List<(int Item, int Count)> needs, bool planning = false)
	{
		needs?.Clear();
		var cell = step.Cell;
		int x = OriginX + cell.X, y = OriginY + cell.Y;
		if (!WorldGen.InWorld(x, y, 2)) return CellState.Blocked;
		var tile = Main.tile[x, y];
		switch (step.Kind) {
			case StepKind.Wall: {
				if (tile.WallType == cell.Wall) return CellState.Done;
				if (tile.WallType != WallID.None) return CellState.Blocked;
				int item = MaterialCatalog.ItemForWall(cell.Wall);
				if (item == 0) return PlaceUnobtainable ? CellState.Missing : CellState.Unobtainable;
				needs?.Add((item, 1));
				return CellState.Missing;
			}
			case StepKind.Tile: {
				if (ObjectMatches(cell)) return CellState.Done;
				if (!ObjectAreaFree(cell)) return CellState.Blocked;
				int item = MaterialCatalog.ItemForTile(cell.Type, cell.U, cell.V);
				if (item == 0) return PlaceUnobtainable ? CellState.Missing : CellState.Unobtainable;
				needs?.Add((item, 1));
				return CellState.Missing;
			}
			case StepKind.Decoration: {
				bool missing = false;
				void Need(bool wanted, bool present, int item)
				{
					if (!wanted || present) return;
					missing = true;
					if (item != 0) needs?.Add((item, 1));
				}
				bool tileHere = tile.HasTile && cell.HasTile && tile.TileType == cell.Type;
				bool wallHere = cell.HasWall && tile.WallType == cell.Wall;
				bool tileWanted = planning ? cell.HasTile : tileHere, wallWanted = planning ? cell.HasWall : wallHere;
				Need((cell.Wires & 1) != 0, tile.RedWire, ItemID.Wire);
				Need((cell.Wires & 2) != 0, tile.BlueWire, ItemID.Wire);
				Need((cell.Wires & 4) != 0, tile.GreenWire, ItemID.Wire);
				Need((cell.Wires & 8) != 0, tile.YellowWire, ItemID.Wire);
				Need(cell.Actuator && tileWanted, tileHere && tile.HasActuator, ItemID.Actuator);
				Need(cell.TileColor != 0 && tileWanted, tileHere && tile.TileColor == cell.TileColor, MaterialCatalog.ItemForPaint(cell.TileColor));
				Need(cell.WallColor != 0 && wallWanted, wallHere && tile.WallColor == cell.WallColor, MaterialCatalog.ItemForPaint(cell.WallColor));
				Need(cell.InvisibleBlock && tileWanted, tileHere && tile.IsTileInvisible, MaterialCatalog.ItemForCoating(PaintCoatingID.Echo));
				Need(cell.InvisibleWall && wallWanted, wallHere && tile.IsWallInvisible, MaterialCatalog.ItemForCoating(PaintCoatingID.Echo));
				Need(cell.FullBrightBlock && tileWanted, tileHere && tile.IsTileFullbright, MaterialCatalog.ItemForCoating(PaintCoatingID.Glow));
				Need(cell.FullBrightWall && wallWanted, wallHere && tile.IsWallFullbright, MaterialCatalog.ItemForCoating(PaintCoatingID.Glow));
				return missing ? CellState.Missing : CellState.Done;
			}
			default: {
				if (tile.LiquidAmount > 0 && tile.LiquidType == cell.LiquidType) return CellState.Done;
				if (tile.HasTile && Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType]) return CellState.Blocked;
				int bucket = (int)cell.LiquidType switch {
					LiquidID.Water => ItemID.WaterBucket,
					LiquidID.Lava => ItemID.LavaBucket,
					LiquidID.Honey => ItemID.HoneyBucket,
					_ => 0,
				};
				if (bucket == 0) return PlaceUnobtainable ? CellState.Missing : CellState.Unobtainable;
				needs?.Add((bucket, 1));
				return CellState.Missing;
			}
		}
	}

	/// <summary>Ghost overlay state of a cell's block or furniture.</summary>
	public CellState TileOverlayState(BlueprintCell cell)
	{
		if (!cell.HasTile) return CellState.Done;
		int x = OriginX + cell.X, y = OriginY + cell.Y;
		if (!WorldGen.InWorld(x, y, 2)) return CellState.Blocked;
		var tile = Main.tile[x, y];
		if (tile.HasTile && tile.TileType == cell.Type) return CellState.Done;
		if (tile.HasTile && !Main.tileCut[tile.TileType]) return CellState.Blocked;
		return CellState.Missing;
	}

	/// <summary>Ghost overlay state of a cell's wall.</summary>
	public CellState WallOverlayState(BlueprintCell cell)
	{
		if (!cell.HasWall) return CellState.Done;
		int x = OriginX + cell.X, y = OriginY + cell.Y;
		if (!WorldGen.InWorld(x, y, 2)) return CellState.Blocked;
		var tile = Main.tile[x, y];
		if (tile.WallType == cell.Wall) return CellState.Done;
		return tile.WallType != WallID.None ? CellState.Blocked : CellState.Missing;
	}

	/// <summary>Ghost overlay state of a cell's liquid. Small shortfalls from settling count as done.</summary>
	public CellState LiquidOverlayState(BlueprintCell cell)
	{
		if (cell.LiquidAmount == 0) return CellState.Done;
		int x = OriginX + cell.X, y = OriginY + cell.Y;
		if (!WorldGen.InWorld(x, y, 2)) return CellState.Blocked;
		var tile = Main.tile[x, y];
		if (tile.LiquidAmount > 0 && tile.LiquidType == cell.LiquidType && tile.LiquidAmount + 32 >= cell.LiquidAmount) return CellState.Done;
		if (tile.HasTile && Main.tileSolid[tile.TileType] && !tile.IsActuated && !Main.tileSolidTop[tile.TileType]) return CellState.Blocked;
		return CellState.Missing;
	}

	bool ObjectMatches(BlueprintCell origin)
	{
		var tile = Main.tile[OriginX + origin.X, OriginY + origin.Y];
		if (!tile.HasTile || tile.TileType != origin.Type) return false;
		return !Main.tileFrameImportant[origin.Type] || (tile.TileFrameX == origin.U && tile.TileFrameY == origin.V);
	}

	IEnumerable<BlueprintCell> ObjectCells(BlueprintCell origin)
	{
		MaterialCatalog.ObjectOffset(origin.Type, origin.U, origin.V, out var data);
		int width = data?.Width ?? 1, height = data?.Height ?? 1;
		for (int dy = 0; dy < height; dy++) {
			for (int dx = 0; dx < width; dx++) {
				var part = Blueprint.At(origin.X + dx, origin.Y + dy);
				if (part is { HasTile: true } && part.Type == origin.Type) yield return part;
			}
		}
	}

	bool ObjectAreaFree(BlueprintCell origin)
	{
		foreach (var part in ObjectCells(origin)) {
			int x = OriginX + part.X, y = OriginY + part.Y;
			if (!WorldGen.InWorld(x, y, 2)) return false;
			var tile = Main.tile[x, y];
			if (tile.HasTile && !Main.tileCut[tile.TileType] && !(tile.TileType == part.Type && !Main.tileFrameImportant[part.Type])) return false;
		}
		return true;
	}

	/// <summary>Writes one step into the world. The caller has already paid for it.</summary>
	public void Apply(BuildStep step)
	{
		var cell = step.Cell;
		int x = OriginX + cell.X, y = OriginY + cell.Y;
		var tile = Main.tile[x, y];
		switch (step.Kind) {
			case StepKind.Wall:
				tile.WallType = (ushort)cell.Wall;
				WorldGen.SquareWallFrame(x, y);
				Sync(x, y, 1, 1);
				break;
			case StepKind.Tile:
				PlaceObject(cell);
				break;
			case StepKind.Decoration:
				if ((cell.Wires & 1) != 0) tile.RedWire = true;
				if ((cell.Wires & 2) != 0) tile.BlueWire = true;
				if ((cell.Wires & 4) != 0) tile.GreenWire = true;
				if ((cell.Wires & 8) != 0) tile.YellowWire = true;
				bool tileHere = tile.HasTile && cell.HasTile && tile.TileType == cell.Type;
				bool wallHere = cell.HasWall && tile.WallType == cell.Wall;
				if (tileHere) {
					if (cell.Actuator) tile.HasActuator = true;
					if (cell.Actuator && cell.InActive) tile.IsActuated = true;
					if (cell.TileColor != 0) tile.TileColor = cell.TileColor;
					if (cell.InvisibleBlock) tile.IsTileInvisible = true;
					if (cell.FullBrightBlock) tile.IsTileFullbright = true;
				}
				if (wallHere) {
					if (cell.WallColor != 0) tile.WallColor = cell.WallColor;
					if (cell.InvisibleWall) tile.IsWallInvisible = true;
					if (cell.FullBrightWall) tile.IsWallFullbright = true;
				}
				Sync(x, y, 1, 1);
				break;
			case StepKind.Liquid:
				tile.LiquidType = cell.LiquidType;
				tile.LiquidAmount = cell.LiquidAmount;
				Liquid.AddWater(x, y);
				Sync(x, y, 1, 1);
				break;
		}
	}

	void PlaceObject(BlueprintCell origin)
	{
		int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
		bool frameImportant = Main.tileFrameImportant[origin.Type];
		foreach (var part in ObjectCells(origin)) {
			int x = OriginX + part.X, y = OriginY + part.Y;
			var tile = Main.tile[x, y];
			if (tile.HasTile && Main.tileCut[tile.TileType]) WorldGen.KillTile(x, y, noItem: false);
			tile.HasTile = true;
			tile.TileType = (ushort)part.Type;
			if (frameImportant) {
				tile.TileFrameX = part.U;
				tile.TileFrameY = part.V;
			}
			else tile.BlockType = (BlockType)part.BrickStyle;
			minX = System.Math.Min(minX, x); minY = System.Math.Min(minY, y);
			maxX = System.Math.Max(maxX, x); maxY = System.Math.Max(maxY, y);
		}
		int originX = OriginX + origin.X, originY = OriginY + origin.Y;
		if (frameImportant) CreateObjectData(origin, originX, originY);
		else WorldGen.SquareTileFrame(originX, originY, true);
		Sync(minX - 1, minY - 1, maxX - minX + 3, maxY - minY + 3);
	}

	/// <summary>Chests, signs and tile entities exist beside the tiles and are created from the object's top-left.</summary>
	void CreateObjectData(BlueprintCell origin, int x, int y)
	{
		int type = origin.Type;
		if (TileID.Sets.BasicChest[type] || TileID.Sets.BasicDresser[type]) {
			int chest = Chest.FindChest(x, y);
			if (chest < 0) chest = Chest.CreateChest(x, y);
			if (chest >= 0 && Blueprint.ChestNames.TryGetValue((origin.X, origin.Y), out string name)) Main.chest[chest].name = name;
		}
		if (Main.tileSign[type]) {
			int sign = Sign.ReadSign(x, y, true);
			if (sign >= 0 && Blueprint.SignTexts.TryGetValue((origin.X, origin.Y), out string text)) Sign.TextSign(sign, text);
		}
		switch (type) {
			case TileID.ItemFrame: TEItemFrame.Place(x, y); break;
			case TileID.WeaponsRack2: TEWeaponsRack.Place(x, y); break;
			case TileID.DisplayDoll: TEDisplayDoll.Place(x, y); break;
			case TileID.HatRack: TEHatRack.Place(x, y); break;
			case TileID.FoodPlatter: TEFoodPlatter.Place(x, y); break;
			case TileID.LogicSensor: TELogicSensor.Place(x, y); break;
			case TileID.TargetDummy: TETrainingDummy.Place(x, y); break;
			case TileID.TeleportationPylon: TETeleportationPylon.Place(x, y); break;
		}
	}

	static void Sync(int x, int y, int width, int height)
	{
		if (Main.netMode == NetmodeID.Server) NetMessage.SendTileSquare(-1, x, y, width, height);
	}
}
