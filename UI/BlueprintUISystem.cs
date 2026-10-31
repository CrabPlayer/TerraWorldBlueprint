using System.Collections.Generic;
using Microsoft.Xna.Framework;
using TerraWorldBlueprint.Content.Tiles;
using TerraWorldBlueprint.Systems;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;

namespace TerraWorldBlueprint.UI;

public sealed class BlueprintUISystem : ModSystem
{
	static UserInterface userInterface;
	static BlueprintPanel panel;

	public override void Load()
	{
		if (Main.dedServ) return;
		userInterface = new UserInterface();
		panel = new BlueprintPanel();
		panel.Activate();
	}

	public override void Unload()
	{
		userInterface = null;
		panel = null;
	}

	public static void OpenTablet() => Open(() => panel.ShowTablet());

	public static void OpenBuilder(BlueprintBuilderEntity entity) => Open(() => panel.ShowBuilder(entity));

	static void Open(System.Action show)
	{
		if (userInterface == null) return;
		Main.playerInventory = true;
		show();
		userInterface.SetState(panel);
	}

	public static void Close() => userInterface?.SetState(null);

	public override void UpdateUI(GameTime gameTime)
	{
		if (userInterface?.CurrentState == null) return;
		if (!Main.playerInventory || panel.ShouldClose()) {
			Close();
			return;
		}
		userInterface.Update(gameTime);
	}

	public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
	{
		int index = layers.FindIndex(layer => layer.Name == "Vanilla: Mouse Text");
		if (index < 0) return;
		layers.Insert(index, new LegacyGameInterfaceLayer("TerraWorldBlueprint: Ghost Tooltip", () => {
			if (userInterface?.CurrentState == null) ProjectionSystem.DrawHoverTooltip(Main.spriteBatch);
			return true;
		}, InterfaceScaleType.UI));
		layers.Insert(index + 1, new LegacyGameInterfaceLayer("TerraWorldBlueprint: Panel", () => {
			if (userInterface?.CurrentState != null) userInterface.Draw(Main.spriteBatch, new GameTime());
			return true;
		}, InterfaceScaleType.UI));
	}

	public override void ClearWorld() => Close();
}
