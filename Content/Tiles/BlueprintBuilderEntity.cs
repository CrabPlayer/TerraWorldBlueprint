using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using TerraWorldBlueprint.Blueprints;
using TerraWorldBlueprint.Systems;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace TerraWorldBlueprint.Content.Tiles;

public sealed class Requirements
{
	public readonly Dictionary<int, int> Needed = new();
	public int Done, Missing, Blocked, Unobtainable;
	public int Total => Done + Missing + Blocked + Unobtainable;
}

public sealed class BlueprintBuilderEntity : ModTileEntity
{
	public int ProjectionId = -1;
	/// <summary>Deposited materials by item type.</summary>
	public readonly Dictionary<int, int> Storage = new();
	public bool Building { get; private set; }
	/// <summary>Where Deposit and building pull missing materials from.</summary>
	public bool UseInventory = true, UseBanks = true, UseMagicStorage = true;
	List<BuildStep> queue;
	/// <summary>Items the sources ran out of during this build pass, so they are not searched again.</summary>
	readonly HashSet<int> exhausted = new();
	TileEntity heart;
	int cursor, built, skipped;
	float budget;

	public Projection Projection => ProjectionSystem.Find(ProjectionId);

	public static bool TryGet(int i, int j, out BlueprintBuilderEntity entity)
	{
		var tile = Main.tile[i, j];
		int x = i - tile.TileFrameX % 36 / 18, y = j - tile.TileFrameY % 36 / 18;
		entity = TileEntity.ByPosition.TryGetValue(new Point16(x, y), out var found) ? found as BlueprintBuilderEntity : null;
		return entity != null;
	}

	public override bool IsTileValidForEntity(int x, int y)
	{
		var tile = Main.tile[x, y];
		return tile.HasTile && tile.TileType == ModContent.TileType<BlueprintBuilderTile>() && tile.TileFrameX % 36 == 0 && tile.TileFrameY % 36 == 0;
	}

	public void Link(Projection projection)
	{
		ProjectionId = projection?.Id ?? -1;
		StopBuilding();
	}

	/// <summary>What the linked projection still needs from the world as it is now.</summary>
	public Requirements Evaluate()
	{
		var result = new Requirements();
		var plan = Projection?.Plan;
		if (plan == null) return result;
		var needs = new List<(int Item, int Count)>();
		foreach (var step in plan.Steps()) {
			switch (plan.Evaluate(step, needs, planning: true)) {
				case CellState.Done: result.Done++; break;
				case CellState.Blocked: result.Blocked++; break;
				case CellState.Unobtainable: result.Unobtainable++; break;
				default:
					result.Missing++;
					foreach (var (item, count) in needs) result.Needed[item] = result.Needed.GetValueOrDefault(item) + count;
					break;
			}
		}
		return result;
	}

	public int Stored(int item) => Storage.GetValueOrDefault(item);

	public bool HasEverything(Requirements requirements) => requirements.Needed.All(pair => Stored(pair.Key) >= pair.Value);

	/// <summary>Pulls every still-missing material from the linked sources.</summary>
	public int Deposit(Player player, Requirements requirements)
	{
		int moved = 0;
		var heart = UseMagicStorage ? MagicStorageBridge.NearestHeart(Position) : null;
		foreach (var (item, needed) in requirements.Needed) moved += Pull(player, heart, item, needed - Stored(item));
		return moved;
	}

	/// <summary>
	/// Moves up to <paramref name="count"/> items into Storage: inventory first, then the player's
	/// Piggy Bank, Safe, Defender's Forge and Void Vault, then the nearest Magic Storage network.
	/// Favourited items are never taken.
	/// </summary>
	int Pull(Player player, TileEntity heart, int type, int count)
	{
		if (count <= 0) return 0;
		int moved = 0;
		if (UseInventory) moved += TakeFrom(player.inventory, 58, type, count - moved);
		if (UseBanks) {
			foreach (var bank in Banks(player)) {
				if (moved >= count) break;
				moved += TakeFrom(bank, bank.Length, type, count - moved);
			}
		}
		if (heart != null && moved < count) moved += MagicStorageBridge.Withdraw(heart, type, count - moved);
		if (moved > 0) Storage[type] = Stored(type) + moved;
		return moved;
	}

	static IEnumerable<Item[]> Banks(Player player) => new[] { player.bank.item, player.bank2.item, player.bank3.item, player.bank4.item };

	static int TakeFrom(Item[] items, int length, int type, int count)
	{
		int taken = 0;
		for (int slot = 0; slot < length && taken < count; slot++) {
			var item = items[slot];
			if (item.IsAir || item.favorited || item.type != type) continue;
			int take = System.Math.Min(item.stack, count - taken);
			item.stack -= take;
			if (item.stack <= 0) item.TurnToAir();
			taken += take;
		}
		return taken;
	}

	/// <summary>How many of each item the linked sources could still supply.</summary>
	public Dictionary<int, int> AvailableInSources(Player player)
	{
		var totals = new Dictionary<int, int>();
		void Count(Item[] items, int length)
		{
			for (int slot = 0; slot < length; slot++) {
				var item = items[slot];
				if (!item.IsAir && !item.favorited) totals[item.type] = totals.GetValueOrDefault(item.type) + item.stack;
			}
		}
		if (UseInventory) Count(player.inventory, 58);
		if (UseBanks) foreach (var bank in Banks(player)) Count(bank, bank.Length);
		if (UseMagicStorage) {
			foreach (var (type, count) in MagicStorageBridge.Contents(MagicStorageBridge.NearestHeart(Position)))
				totals[type] = totals.GetValueOrDefault(type) + count;
		}
		return totals;
	}

	public void Withdraw(Player player)
	{
		foreach (var (type, count) in Storage) GiveItems(player.GetSource_TileInteraction(Position.X, Position.Y), player.Center, type, count);
		Storage.Clear();
	}

	public void DropStorage()
	{
		var center = Position.ToWorldCoordinates(16, 16);
		foreach (var (type, count) in Storage) GiveItems(new EntitySource_TileBreak(Position.X, Position.Y), center, type, count);
		Storage.Clear();
	}

	static void GiveItems(IEntitySource source, Vector2 position, int type, int count)
	{
		int max = System.Math.Max(1, ContentSamples.ItemsByType[type].maxStack);
		while (count > 0) {
			int stack = System.Math.Min(count, max);
			Item.NewItem(source, position, type, stack);
			count -= stack;
		}
	}

	public void StartBuilding()
	{
		var plan = Projection?.Plan;
		if (plan == null) return;
		queue = plan.Steps();
		exhausted.Clear();
		heart = UseMagicStorage ? MagicStorageBridge.NearestHeart(Position) : null;
		cursor = built = skipped = 0;
		budget = 0;
		Building = true;
	}

	public void StopBuilding()
	{
		Building = false;
		queue = null;
	}

	public override void Update()
	{
		if (!Building) return;
		var plan = Projection?.Plan;
		if (plan == null || queue == null) { StopBuilding(); return; }

		budget += ModContent.GetInstance<BlueprintConfig>().BuildStepsPerSecond / 60f;
		var needs = new List<(int Item, int Count)>();
		// Finished cells cost nothing, so scanning past them is capped separately from building.
		for (int scanned = 0; budget >= 1 && scanned < 2000 && cursor < queue.Count; scanned++) {
			var step = queue[cursor++];
			if (plan.Evaluate(step, needs) != CellState.Missing) continue;
			foreach (var (item, count) in needs) {
				int missing = count - Stored(item);
				if (missing > 0 && !exhausted.Contains(item) && Pull(Main.LocalPlayer, heart, item, missing) < missing) exhausted.Add(item);
			}
			if (!needs.All(need => Stored(need.Item) >= need.Count)) { skipped++; continue; }
			foreach (var (item, count) in needs) {
				Storage[item] -= count;
				if (Storage[item] <= 0) Storage.Remove(item);
			}
			plan.Apply(step);
			built++;
			budget -= 1;
		}
		if (cursor < queue.Count) return;
		StopBuilding();
		if (Main.netMode != NetmodeID.Server)
			Main.NewText(Mod.GetLocalization("Messages.BuildFinished").Format(Projection?.Blueprint.Name, built, skipped), new Color(120, 255, 160));
	}

	public override void SaveData(TagCompound tag)
	{
		tag["projection"] = ProjectionId;
		tag["useInventory"] = UseInventory;
		tag["useBanks"] = UseBanks;
		tag["useMagicStorage"] = UseMagicStorage;
		tag["storage"] = Storage.Select(pair => new TagCompound { ["item"] = ItemIO.Save(new Item(pair.Key)), ["count"] = pair.Value }).ToList();
	}

	public override void LoadData(TagCompound tag)
	{
		ProjectionId = tag.GetInt("projection");
		UseInventory = !tag.ContainsKey("useInventory") || tag.GetBool("useInventory");
		UseBanks = !tag.ContainsKey("useBanks") || tag.GetBool("useBanks");
		UseMagicStorage = !tag.ContainsKey("useMagicStorage") || tag.GetBool("useMagicStorage");
		Storage.Clear();
		foreach (var entry in tag.GetList<TagCompound>("storage")) {
			var item = ItemIO.Load(entry.GetCompound("item"));
			if (!item.IsAir) Storage[item.type] = Stored(item.type) + entry.GetInt("count");
		}
	}
}
