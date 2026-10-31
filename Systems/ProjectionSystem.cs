using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TerraWorldBlueprint.Blueprints;
using TerraWorldBlueprint.Content.Items;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.UI;

namespace TerraWorldBlueprint.Systems;

/// <summary>A blueprint pinned to a world position. The package bytes are saved with the world.</summary>
public sealed class Projection
{
	public int Id;
	public BuildPlan Plan;
	public Blueprint Blueprint => Plan.Blueprint;
	public Rectangle Bounds => new(Plan.OriginX, Plan.OriginY, Blueprint.Width, Blueprint.Height);
}

public sealed class ProjectionSystem : ModSystem
{
	public static readonly List<Projection> Projections = new();
	/// <summary>Blueprint chosen in the tablet; follows the mouse while the tablet is held, until it is projected.</summary>
	public static Blueprint Selected;
	public static bool ShowGhosts = true;
	public static bool ShowTiles = true, ShowWalls = true, ShowLiquids = true;
	/// <summary>Layer mode shows one band of rows at a time, counted from the bottom of each projection.</summary>
	public static bool LayerMode;
	public static int LayerRows = 3;
	public static int Layer;
	public static readonly int[] RowChoices = { 1, 2, 3, 5, 8, 12 };
	static int nextId = 1;

	static readonly Color MissingColor = new(150, 210, 255);
	static readonly Color BlockedColor = new(255, 80, 80);

	public static Projection Find(int id) => Projections.FirstOrDefault(projection => projection.Id == id);

	public static Projection Add(Blueprint blueprint, int x, int y)
	{
		var projection = new Projection { Id = nextId++, Plan = new BuildPlan(blueprint, x, y) };
		Projections.Add(projection);
		return projection;
	}

	public static void Remove(Projection projection) => Projections.Remove(projection);

	/// <summary>Top-left tile for the held blueprint: the mouse marks the bottom centre.</summary>
	public static Point PreviewOrigin(Blueprint blueprint)
	{
		var mouse = Main.MouseWorld.ToTileCoordinates();
		return new Point(mouse.X - blueprint.Width / 2, mouse.Y - blueprint.Height + 1);
	}

	public static bool HoldingTablet => Main.LocalPlayer.HeldItem.type == ModContent.ItemType<BlueprintTablet>();
	public static bool GhostsVisible => ShowGhosts || HoldingTablet;

	// ---- Layers ---------------------------------------------------------------

	public static int LayerCount(Blueprint blueprint) => (blueprint.Height + LayerRows - 1) / LayerRows;

	public static int MaxLayerCount => Projections.Count == 0 ? 1 : Projections.Max(projection => LayerCount(projection.Blueprint));

	/// <summary>Band index of a blueprint row; band 0 is the bottom rows.</summary>
	static int BandOf(Blueprint blueprint, int relY) => (blueprint.Height - 1 - relY) / LayerRows;

	public static bool RowVisible(Blueprint blueprint, int relY) =>
		!LayerMode || BandOf(blueprint, relY) == Math.Min(Layer, LayerCount(blueprint) - 1);

	/// <summary>The projection the player is standing in or closest to.</summary>
	public static Projection Nearest()
	{
		var here = Main.LocalPlayer.Center / 16;
		return Projections.OrderBy(projection => {
			var bounds = projection.Bounds;
			float dx = Math.Max(0, Math.Max(bounds.Left - here.X, here.X - bounds.Right));
			float dy = Math.Max(0, Math.Max(bounds.Top - here.Y, here.Y - bounds.Bottom));
			return dx * dx + dy * dy;
		}).FirstOrDefault();
	}

	/// <summary>Lowest band of the nearest projection that still has visible work.</summary>
	public static int LowestUnfinishedLayer()
	{
		var projection = Nearest();
		if (projection == null) return 0;
		var blueprint = projection.Blueprint;
		int count = LayerCount(blueprint);
		for (int band = 0; band < count; band++) {
			int bottom = blueprint.Height - 1 - band * LayerRows;
			for (int y = bottom; y > bottom - LayerRows && y >= 0; y--) {
				for (int x = 0; x < blueprint.Width; x++) {
					var cell = blueprint.At(x, y);
					if (cell != null && Parts(projection.Plan, cell).Any()) return band;
				}
			}
		}
		return count - 1;
	}

	public static void SetLayer(int layer)
	{
		LayerMode = true;
		Layer = Math.Clamp(layer, 0, MaxLayerCount - 1);
	}

	public static void CycleRows()
	{
		// Keep roughly the same height in view when the band size changes.
		int bottomRow = Layer * LayerRows;
		LayerRows = RowChoices[(Array.IndexOf(RowChoices, LayerRows) + 1) % RowChoices.Length];
		Layer = Math.Clamp(bottomRow / LayerRows, 0, MaxLayerCount - 1);
	}

	public static void AnnounceLayer()
	{
		string text = LayerMode
			? Language.GetTextValue("Mods.TerraWorldBlueprint.UI.LayerStatus", Layer + 1, MaxLayerCount, LayerRows)
			: Language.GetTextValue("Mods.TerraWorldBlueprint.UI.LayersOff");
		Main.NewText(text, MissingColor);
	}

	// ---- Ghost parts ------------------------------------------------------------

	public enum Part { Wall, Tile, Liquid }

	/// <summary>Unfinished parts of a cell that the current filters show.</summary>
	public static IEnumerable<(Part Part, CellState State)> Parts(BuildPlan plan, BlueprintCell cell)
	{
		if (ShowWalls && cell.HasWall) {
			var state = plan.WallOverlayState(cell);
			if (state != CellState.Done) yield return (Part.Wall, state);
		}
		if (ShowTiles && cell.HasTile) {
			var state = plan.TileOverlayState(cell);
			if (state != CellState.Done) yield return (Part.Tile, state);
		}
		if (ShowLiquids && cell.LiquidAmount > 0) {
			var state = plan.LiquidOverlayState(cell);
			if (state != CellState.Done) yield return (Part.Liquid, state);
		}
	}

	static Color LiquidColor(int type) => type switch {
		LiquidID.Lava => new Color(255, 110, 30),
		LiquidID.Honey => new Color(255, 190, 40),
		LiquidID.Shimmer => new Color(220, 150, 255),
		_ => new Color(50, 120, 255),
	};

	// ---- World save -------------------------------------------------------------

	public override void ClearWorld()
	{
		Projections.Clear();
		nextId = 1;
		Layer = 0;
	}

	public override void SaveWorldData(TagCompound tag)
	{
		tag["projections"] = Projections.Select(projection => new TagCompound {
			["id"] = projection.Id,
			["x"] = projection.Plan.OriginX,
			["y"] = projection.Plan.OriginY,
			["package"] = projection.Blueprint.Source,
		}).ToList();
	}

	public override void LoadWorldData(TagCompound tag)
	{
		Projections.Clear();
		foreach (var entry in tag.GetList<TagCompound>("projections")) {
			try {
				var blueprint = Blueprint.Load(entry.GetByteArray("package"));
				Projections.Add(new Projection { Id = entry.GetInt("id"), Plan = new BuildPlan(blueprint, entry.GetInt("x"), entry.GetInt("y")) });
			}
			catch (Exception error) {
				Mod.Logger.Warn("Dropped a saved projection that could not be read", error);
			}
		}
		nextId = Projections.Count == 0 ? 1 : Projections.Max(projection => projection.Id) + 1;
	}

	// ---- Drawing ----------------------------------------------------------------

	public override void PostDrawTiles()
	{
		bool holdingTablet = HoldingTablet;
		if (!GhostsVisible) return;

		float opacity = ModContent.GetInstance<BlueprintConfig>().GhostOpacity / 100f;
		var screen = new Rectangle((int)(Main.screenPosition.X / 16) - 1, (int)(Main.screenPosition.Y / 16) - 1, Main.screenWidth / 16 + 3, Main.screenHeight / 16 + 3);
		Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);
		foreach (var projection in Projections) DrawPlan(projection.Plan, screen, opacity);
		if (holdingTablet && Selected != null && !Main.LocalPlayer.mouseInterface) {
			var origin = PreviewOrigin(Selected);
			DrawPreview(new BuildPlan(Selected, origin.X, origin.Y), screen, Math.Max(opacity, 0.35f));
		}
		Main.spriteBatch.End();
	}

	static void DrawPlan(BuildPlan plan, Rectangle screen, float opacity)
	{
		var blueprint = plan.Blueprint;
		var bounds = new Rectangle(plan.OriginX, plan.OriginY, blueprint.Width, blueprint.Height);
		DrawOutline(bounds, MissingColor * 0.6f);
		if (LayerMode) {
			int band = Math.Min(Layer, LayerCount(blueprint) - 1);
			int bottom = bounds.Bottom - band * LayerRows;
			int top = Math.Max(bounds.Top, bottom - LayerRows);
			DrawOutline(new Rectangle(bounds.X, top, bounds.Width, bottom - top), new Color(255, 220, 90));
		}

		var visible = Rectangle.Intersect(bounds, screen);
		if (visible.IsEmpty) return;
		for (int y = visible.Top; y < visible.Bottom; y++) {
			if (!RowVisible(blueprint, y - plan.OriginY)) continue;
			for (int x = visible.Left; x < visible.Right; x++) {
				var cell = blueprint.At(x - plan.OriginX, y - plan.OriginY);
				if (cell == null) continue;
				var position = new Vector2(x * 16, y * 16) - Main.screenPosition;
				foreach (var (part, state) in Parts(plan, cell)) {
					var tint = state == CellState.Blocked ? BlockedColor : MissingColor;
					switch (part) {
						case Part.Wall: DrawWall(cell.Wall, position, tint * opacity); break;
						case Part.Tile: DrawTile(cell, position, tint * (opacity * 1.6f)); break;
						case Part.Liquid: DrawLiquid(cell, position, (state == CellState.Blocked ? BlockedColor : LiquidColor(cell.LiquidType)) * (opacity * 1.4f)); break;
					}
				}
			}
		}
	}

	/// <summary>The held blueprint before it is projected: everything, in one colour.</summary>
	static void DrawPreview(BuildPlan plan, Rectangle screen, float opacity)
	{
		var blueprint = plan.Blueprint;
		var bounds = new Rectangle(plan.OriginX, plan.OriginY, blueprint.Width, blueprint.Height);
		var tint = new Color(150, 210, 255);
		var visible = Rectangle.Intersect(bounds, screen);
		for (int y = visible.Top; y < visible.Bottom; y++) {
			for (int x = visible.Left; x < visible.Right; x++) {
				var cell = blueprint.At(x - plan.OriginX, y - plan.OriginY);
				if (cell == null) continue;
				var position = new Vector2(x * 16, y * 16) - Main.screenPosition;
				if (cell.HasWall) DrawWall(cell.Wall, position, tint * opacity);
				if (cell.HasTile) DrawTile(cell, position, tint * (opacity * 1.6f));
			}
		}
		DrawOutline(bounds, new Color(120, 255, 160));
	}

	static void DrawTile(BlueprintCell cell, Vector2 position, Color color)
	{
		Main.instance.LoadTiles(cell.Type);
		var texture = TextureAssets.Tile[cell.Type].Value;
		// Blocks are re-framed in the world; their surrounded frame reads well as a ghost.
		var source = cell.U >= 0 && cell.V >= 0 ? new Rectangle(cell.U, cell.V, 16, 16) : new Rectangle(18, 18, 16, 16);
		Main.spriteBatch.Draw(texture, position, source, color);
	}

	static void DrawWall(int wall, Vector2 position, Color color)
	{
		Main.instance.LoadWall(wall);
		Main.spriteBatch.Draw(TextureAssets.Wall[wall].Value, position, new Rectangle(44, 44, 16, 16), color);
	}

	static void DrawLiquid(BlueprintCell cell, Vector2 position, Color color)
	{
		int height = Math.Max(2, cell.LiquidAmount * 16 / 255);
		Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle((int)position.X, (int)position.Y + 16 - height, 16, height), color);
	}

	static void DrawOutline(Rectangle bounds, Color color)
	{
		var pixel = TextureAssets.MagicPixel.Value;
		var area = new Rectangle((int)(bounds.X * 16 - Main.screenPosition.X), (int)(bounds.Y * 16 - Main.screenPosition.Y), bounds.Width * 16, bounds.Height * 16);
		color *= 0.8f;
		Main.spriteBatch.Draw(pixel, new Rectangle(area.Left, area.Top, area.Width, 2), color);
		Main.spriteBatch.Draw(pixel, new Rectangle(area.Left, area.Bottom - 2, area.Width, 2), color);
		Main.spriteBatch.Draw(pixel, new Rectangle(area.Left, area.Top, 2, area.Height), color);
		Main.spriteBatch.Draw(pixel, new Rectangle(area.Right - 2, area.Top, 2, area.Height), color);
	}

	// ---- Hover tooltip ----------------------------------------------------------

	/// <summary>Item icon, label and colour for each unfinished part under the mouse.</summary>
	static List<(int Item, string Text, Color Color)> HoverLines()
	{
		var lines = new List<(int, string, Color)>();
		if (!GhostsVisible || Main.LocalPlayer.mouseInterface || Main.mouseText) return lines;
		// Main.MouseWorld is in UI coordinates while interface layers draw; the tile target is not.
		var mouse = new Point(Player.tileTargetX, Player.tileTargetY);
		foreach (var projection in Projections) {
			if (!projection.Bounds.Contains(mouse)) continue;
			var plan = projection.Plan;
			int relY = mouse.Y - plan.OriginY;
			if (!RowVisible(projection.Blueprint, relY)) continue;
			var cell = projection.Blueprint.At(mouse.X - plan.OriginX, relY);
			if (cell == null) continue;
			foreach (var (part, state) in Parts(plan, cell)) {
				var (item, name) = part switch {
					Part.Wall => (MaterialCatalog.ItemForWall(cell.Wall), WallName(cell.Wall)),
					Part.Tile => (MaterialCatalog.ItemForTile(cell.Type, cell.U, cell.V), TileName(cell)),
					_ => (LiquidItem(cell.LiquidType), ""),
				};
				if (item > 0) name = Lang.GetItemNameValue(item);
				if (state == CellState.Blocked) lines.Add((item, Language.GetTextValue("Mods.TerraWorldBlueprint.UI.HoverBlocked", name), BlockedColor));
				else if (item <= 0) lines.Add((item, Language.GetTextValue("Mods.TerraWorldBlueprint.UI.HoverNoItem", name), Color.Orange));
				else lines.Add(CountLine(projection, item, name));
				if (part == Part.Tile && cell.TileColor > 0 && MaterialCatalog.ItemForPaint(cell.TileColor) is > 0 and var paint)
					lines.Add(CountLine(projection, paint, Lang.GetItemNameValue(paint)));
			}
			break;
		}
		return lines;
	}

	static readonly Color EnoughColor = new(120, 255, 160);

	/// <summary>"Name   have / need": green once the inventory covers what the visible ghost still needs.</summary>
	static (int, string, Color) CountLine(Projection projection, int item, string name)
	{
		int have = Main.LocalPlayer.CountItem(item);
		int need = Math.Max(1, Remaining(projection).GetValueOrDefault(item));
		string text = Language.GetTextValue("Mods.TerraWorldBlueprint.UI.HoverCount", name, have, need);
		return (item, text, have >= need ? EnoughColor : have > 0 ? Color.White : new Color(200, 200, 200));
	}

	static Projection countedProjection;
	static (bool, int, int, bool, bool, bool) countedView;
	static uint countedAt;
	static readonly Dictionary<int, int> remaining = new();

	/// <summary>
	/// Items the visible part of a projection still needs (the current layer in layer mode).
	/// Multi-tile objects count once. Rebuilt at most twice a second.
	/// </summary>
	static Dictionary<int, int> Remaining(Projection projection)
	{
		var view = (LayerMode, Layer, LayerRows, ShowTiles, ShowWalls, ShowLiquids);
		if (projection == countedProjection && view == countedView && Main.GameUpdateCount - countedAt < 30) return remaining;
		countedProjection = projection;
		countedView = view;
		countedAt = Main.GameUpdateCount;
		remaining.Clear();
		var plan = projection.Plan;
		foreach (var cell in projection.Blueprint.Cells) {
			if (!RowVisible(projection.Blueprint, cell.Y)) continue;
			foreach (var (part, _) in Parts(plan, cell)) {
				int item = part switch {
					Part.Wall => MaterialCatalog.ItemForWall(cell.Wall),
					Part.Tile => MaterialCatalog.ObjectOffset(cell.Type, cell.U, cell.V, out _) == (0, 0) ? MaterialCatalog.ItemForTile(cell.Type, cell.U, cell.V) : 0,
					_ => LiquidItem(cell.LiquidType),
				};
				if (item > 0) remaining[item] = remaining.GetValueOrDefault(item) + 1;
				if (part == Part.Tile && cell.TileColor > 0 && MaterialCatalog.ItemForPaint(cell.TileColor) is > 0 and var paint)
					remaining[paint] = remaining.GetValueOrDefault(paint) + 1;
			}
		}
		return remaining;
	}

	static string TileName(BlueprintCell cell) => TileLoader.GetTile(cell.Type)?.Name ?? TileID.Search.GetName(cell.Type);
	static string WallName(int wall) => WallLoader.GetWall(wall)?.Name ?? WallID.Search.GetName(wall);

	static int LiquidItem(int type) => type switch {
		LiquidID.Lava => ItemID.LavaBucket,
		LiquidID.Honey => ItemID.HoneyBucket,
		LiquidID.Shimmer => ItemID.BottomlessShimmerBucket,
		_ => ItemID.WaterBucket,
	};

	/// <summary>Drawn in a UI-scaled interface layer.</summary>
	public static void DrawHoverTooltip(SpriteBatch spriteBatch)
	{
		var lines = HoverLines();
		if (lines.Count == 0) return;
		var font = FontAssets.MouseText.Value;
		const int rowHeight = 30;
		int width = (int)lines.Max(line => font.MeasureString(line.Text).X) + 52;
		int height = lines.Count * rowHeight + 10;
		var position = Main.MouseScreen + new Vector2(20, 20);
		float screenWidth = Main.screenWidth / Main.UIScale, screenHeight = Main.screenHeight / Main.UIScale;
		if (position.X + width > screenWidth) position.X = Main.MouseScreen.X - width - 8;
		if (position.Y + height > screenHeight) position.Y = Main.MouseScreen.Y - height - 8;

		Utils.DrawInvBG(spriteBatch, new Rectangle((int)position.X, (int)position.Y, width, height), new Color(33, 43, 79) * 0.92f);
		for (int i = 0; i < lines.Count; i++) {
			var (item, text, color) = lines[i];
			var row = position + new Vector2(8, 5 + i * rowHeight);
			if (item > 0) {
				Main.instance.LoadItem(item);
				ItemSlot.DrawItemIcon(ContentSamples.ItemsByType[item], ItemSlot.Context.InWorld, spriteBatch, row + new Vector2(14, 14), 1f, 26f, Color.White);
			}
			Utils.DrawBorderString(spriteBatch, text, row + new Vector2(36, 4), color);
		}
	}
}
