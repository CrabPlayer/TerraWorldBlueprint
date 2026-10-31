using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerraWorldBlueprint.Blueprints;

/// <summary>
/// Optional Magic Storage support through reflection, so the mod neither requires nor references it.
/// Uses TEStorageHeart.GetStoredItems() and TEStorageHeart.TryWithdraw(Item, bool, bool).
/// </summary>
public static class MagicStorageBridge
{
	static bool initialized;
	static Type heartType;
	static MethodInfo getStoredItems, tryWithdraw;

	public static bool Available => Init();

	static bool Init()
	{
		if (initialized) return heartType != null;
		initialized = true;
		if (!ModLoader.TryGetMod("MagicStorage", out var mod)) return false;
		var type = mod.Code?.GetType("MagicStorage.Components.TEStorageHeart");
		getStoredItems = type?.GetMethod("GetStoredItems", Type.EmptyTypes);
		tryWithdraw = type?.GetMethod("TryWithdraw", new[] { typeof(Item), typeof(bool), typeof(bool) });
		if (getStoredItems == null || tryWithdraw == null) {
			TerraWorldBlueprint.Instance.Logger.Warn("Magic Storage is loaded but its storage heart API changed; Magic Storage support is off.");
			return false;
		}
		heartType = type;
		return true;
	}

	public static void Unload()
	{
		initialized = false;
		heartType = null;
		getStoredItems = tryWithdraw = null;
	}

	/// <summary>The storage heart closest to a tile, or null.</summary>
	public static TileEntity NearestHeart(Point16 from)
	{
		if (!Init()) return null;
		return TileEntity.ByID.Values
			.Where(heartType.IsInstanceOfType)
			.OrderBy(entity => Math.Abs(entity.Position.X - from.X) + Math.Abs(entity.Position.Y - from.Y))
			.FirstOrDefault();
	}

	/// <summary>Stack totals by item type in a storage network.</summary>
	public static Dictionary<int, int> Contents(TileEntity heart)
	{
		var totals = new Dictionary<int, int>();
		if (heart == null || !Init()) return totals;
		foreach (var item in (IEnumerable<Item>)getStoredItems.Invoke(heart, null)) {
			if (!item.IsAir && !item.favorited) totals[item.type] = totals.GetValueOrDefault(item.type) + item.stack;
		}
		return totals;
	}

	/// <returns>How many items were taken out of the network, up to <paramref name="count"/>.</returns>
	public static int Withdraw(TileEntity heart, int type, int count)
	{
		if (heart == null || count <= 0 || !Init()) return 0;
		int taken = 0;
		int maxStack = Math.Max(1, ContentSamples.ItemsByType[type].maxStack);
		// Each call returns at most one stack; the cap guards against an API that keeps returning nothing useful.
		for (int attempt = 0; attempt < 64 && taken < count; attempt++) {
			var lookFor = new Item(type, Math.Min(count - taken, maxStack));
			var result = (Item)tryWithdraw.Invoke(heart, new object[] { lookFor, false, false });
			if (result == null || result.IsAir || result.type != type) break;
			taken += result.stack;
		}
		return taken;
	}
}
