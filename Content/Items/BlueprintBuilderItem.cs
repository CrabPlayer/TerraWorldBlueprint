using TerraWorldBlueprint.Content.Tiles;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerraWorldBlueprint.Content.Items;

public sealed class BlueprintBuilderItem : ModItem
{
	public override void SetDefaults()
	{
		Item.DefaultToPlaceableTile(ModContent.TileType<BlueprintBuilderTile>());
		Item.width = 32;
		Item.height = 32;
		Item.rare = ItemRarityID.Green;
		Item.value = Item.buyPrice(silver: 50);
	}

	public override void AddRecipes()
	{
		CreateRecipe()
			.AddRecipeGroup(RecipeGroupID.Wood, 20)
			.AddRecipeGroup(RecipeGroupID.IronBar, 5)
			.AddIngredient<BlueprintTablet>()
			.AddTile(TileID.Anvils)
			.Register();
	}
}
