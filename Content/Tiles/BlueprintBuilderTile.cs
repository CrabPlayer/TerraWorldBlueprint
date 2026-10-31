using Microsoft.Xna.Framework;
using TerraWorldBlueprint.Content.Items;
using TerraWorldBlueprint.UI;
using Terraria;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.GameContent.ObjectInteractions;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace TerraWorldBlueprint.Content.Tiles;

/// <summary>2x2 station that stores materials for a linked projection and builds it.</summary>
public sealed class BlueprintBuilderTile : ModTile
{
	public override void SetStaticDefaults()
	{
		Main.tileFrameImportant[Type] = true;
		Main.tileNoAttach[Type] = true;
		Main.tileLavaDeath[Type] = false;
		TileID.Sets.HasOutlines[Type] = true;
		TileID.Sets.DisableSmartCursor[Type] = true;

		TileObjectData.newTile.CopyFrom(TileObjectData.Style2x2);
		TileObjectData.newTile.Origin = new Point16(0, 1);
		TileObjectData.newTile.CoordinateHeights = new[] { 16, 18 };
		TileObjectData.newTile.HookPostPlaceMyPlayer = ModContent.GetInstance<BlueprintBuilderEntity>().Generic_HookPostPlaceMyPlayer;
		TileObjectData.newTile.AnchorBottom = new AnchorData(AnchorType.SolidTile | AnchorType.SolidWithTop | AnchorType.Table, 2, 0);
		TileObjectData.addTile(Type);

		AddMapEntry(new Color(70, 130, 200), CreateMapEntryName());
		DustType = DustID.WoodFurniture;
	}

	public override bool HasSmartInteract(int i, int j, SmartInteractScanSettings settings) => true;

	public override void MouseOver(int i, int j)
	{
		var player = Main.LocalPlayer;
		player.noThrow = 2;
		player.cursorItemIconEnabled = true;
		player.cursorItemIconID = ModContent.ItemType<BlueprintBuilderItem>();
	}

	public override bool RightClick(int i, int j)
	{
		if (BlueprintBuilderEntity.TryGet(i, j, out var entity)) BlueprintUISystem.OpenBuilder(entity);
		return true;
	}

	public override void KillMultiTile(int i, int j, int frameX, int frameY)
	{
		if (BlueprintBuilderEntity.TryGet(i, j, out var entity)) entity.DropStorage();
		ModContent.GetInstance<BlueprintBuilderEntity>().Kill(i, j);
	}
}
