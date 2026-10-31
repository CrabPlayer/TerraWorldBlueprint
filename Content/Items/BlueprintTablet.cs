using Microsoft.Xna.Framework;
using TerraWorldBlueprint.Systems;
using TerraWorldBlueprint.UI;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerraWorldBlueprint.Content.Items;

/// <summary>Right-click: choose a blueprint. Left-click: project it where the mouse points.</summary>
public sealed class BlueprintTablet : ModItem
{
	public override void SetDefaults()
	{
		Item.width = 28;
		Item.height = 30;
		Item.useStyle = ItemUseStyleID.HoldUp;
		Item.useTime = 20;
		Item.useAnimation = 20;
		Item.autoReuse = false;
		Item.rare = ItemRarityID.Blue;
		Item.value = Item.buyPrice(silver: 10);
	}

	public override bool AltFunctionUse(Player player) => true;

	public override bool CanUseItem(Player player)
	{
		if (player.whoAmI != Main.myPlayer) return true;
		if (player.altFunctionUse == 2) {
			BlueprintUISystem.OpenTablet();
			return false;
		}
		if (ProjectionSystem.Selected == null) {
			Main.NewText(Mod.GetLocalization("Messages.NoBlueprintSelected").Value, Color.Orange);
			return false;
		}
		return true;
	}

	public override bool? UseItem(Player player)
	{
		if (player.whoAmI != Main.myPlayer || player.altFunctionUse == 2) return true;
		var blueprint = ProjectionSystem.Selected;
		var origin = ProjectionSystem.PreviewOrigin(blueprint);
		ProjectionSystem.Add(blueprint, origin.X, origin.Y);
		// One placement per pick, so the cursor preview stops cluttering the projected ghost.
		ProjectionSystem.Selected = null;
		Main.NewText(Mod.GetLocalization("Messages.Projected").Format(blueprint.Name, origin.X, origin.Y), new Color(150, 210, 255));
		return true;
	}

	public override void AddRecipes()
	{
		CreateRecipe()
			.AddRecipeGroup(RecipeGroupID.Wood, 10)
			.AddTile(TileID.WorkBenches)
			.Register();
	}
}
