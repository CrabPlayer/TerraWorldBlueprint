using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace TerraWorldBlueprint;

public sealed class BlueprintConfig : ModConfig
{
	public override ConfigScope Mode => ConfigScope.ServerSide;

	/// <summary>Grass, pots, natural walls and other tiles with no item. Off: they are skipped.</summary>
	[DefaultValue(false)]
	public bool PlaceUnobtainableForFree;

	/// <summary>Liquids cost one bucket per cell.</summary>
	[DefaultValue(false)]
	public bool PlaceLiquids;

	[DefaultValue(40)]
	[Range(1, 2000)]
	public int BuildStepsPerSecond;

	[DefaultValue(30)]
	[Range(0, 100)]
	[Slider]
	public int GhostOpacity;
}
