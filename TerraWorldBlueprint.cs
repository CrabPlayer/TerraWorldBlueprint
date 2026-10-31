using TerraWorldBlueprint.Blueprints;
using Terraria.ModLoader;

namespace TerraWorldBlueprint;

public sealed class TerraWorldBlueprint : Mod
{
	public static TerraWorldBlueprint Instance { get; private set; }

	public override void Load() => Instance = this;

	public override void PostSetupContent()
	{
		MaterialCatalog.Build();
		BlueprintLibrary.Refresh();
	}

	public override void Unload()
	{
		MaterialCatalog.Clear();
		MagicStorageBridge.Unload();
		Instance = null;
	}
}
