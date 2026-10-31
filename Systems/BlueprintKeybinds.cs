using Terraria.GameInput;
using Terraria.ModLoader;

namespace TerraWorldBlueprint.Systems;

/// <summary>Steps the layer view without opening the tablet.</summary>
public sealed class BlueprintKeybinds : ModSystem
{
	public static ModKeybind LayerUp, LayerDown;

	public override void Load()
	{
		LayerUp = KeybindLoader.RegisterKeybind(Mod, "LayerUp", "PageUp");
		LayerDown = KeybindLoader.RegisterKeybind(Mod, "LayerDown", "PageDown");
	}

	public override void Unload()
	{
		LayerUp = null;
		LayerDown = null;
	}
}

public sealed class BlueprintKeybindPlayer : ModPlayer
{
	public override void ProcessTriggers(TriggersSet triggersSet)
	{
		if (ProjectionSystem.Projections.Count == 0) return;
		int step = (BlueprintKeybinds.LayerUp.JustPressed ? 1 : 0) - (BlueprintKeybinds.LayerDown.JustPressed ? 1 : 0);
		if (step == 0) return;
		// The first press turns layer mode on at the lowest band that still needs work.
		ProjectionSystem.SetLayer(ProjectionSystem.LayerMode ? ProjectionSystem.Layer + step : ProjectionSystem.LowestUnfinishedLayer());
		ProjectionSystem.AnnounceLayer();
	}
}
