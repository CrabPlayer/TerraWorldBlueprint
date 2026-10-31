using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using TerraWorldBlueprint.Blueprints;
using TerraWorldBlueprint.Content.Tiles;
using TerraWorldBlueprint.Systems;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;

namespace TerraWorldBlueprint.UI;

/// <summary>One panel with two views: the tablet's blueprint picker and the builder station.</summary>
public sealed class BlueprintPanel : UIState
{
	static readonly Color Accent = new(150, 210, 255);
	static readonly Color Good = new(120, 255, 160);
	static readonly Color Bad = new(255, 110, 110);

	UIPanel root;
	UIText title;
	UIList list;
	BlueprintBuilderEntity builder;
	Requirements requirements;
	int refreshTimer;

	static string Text(string key) => Language.GetTextValue($"Mods.TerraWorldBlueprint.UI.{key}");
	static string Text(string key, params object[] args) => Language.GetTextValue($"Mods.TerraWorldBlueprint.UI.{key}", args);

	public override void OnInitialize()
	{
		root = new UIPanel();
		root.Width.Set(480, 0);
		root.Height.Set(540, 0);
		root.HAlign = 0.5f;
		root.VAlign = 0.45f;
		root.BackgroundColor = new Color(33, 43, 79) * 0.92f;
		Append(root);

		title = new UIText("", 0.6f, true);
		title.Top.Set(2, 0);
		root.Append(title);

		var close = new UITextPanel<string>("X");
		close.Width.Set(34, 0);
		close.Height.Set(30, 0);
		close.HAlign = 1;
		close.OnLeftClick += (_, _) => BlueprintUISystem.Close();
		root.Append(close);

		list = new UIList { ListPadding = 4, ManualSortMethod = _ => { } };
		list.Top.Set(44, 0);
		list.Width.Set(-26, 1);
		list.Height.Set(-44, 1);
		root.Append(list);

		var scrollbar = new UIScrollbar();
		scrollbar.Top.Set(48, 0);
		scrollbar.Height.Set(-52, 1);
		scrollbar.HAlign = 1;
		root.Append(scrollbar);
		list.SetScrollbar(scrollbar);
	}

	public bool ShouldClose()
	{
		if (builder == null) return false;
		if (!TileEntity.ByID.ContainsKey(builder.ID)) return true;
		return Main.LocalPlayer.Distance(builder.Position.ToWorldCoordinates(16, 16)) > 16 * 12;
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
		if (root.ContainsPoint(Main.MouseScreen)) Main.LocalPlayer.mouseInterface = true;
		if (builder != null && ++refreshTimer >= 30) {
			refreshTimer = 0;
			ShowBuilder(builder);
		}
	}

	// ---- Tablet -------------------------------------------------------------

	public void ShowTablet()
	{
		builder = null;
		title.SetText(Text("TabletTitle"));
		list.Clear();

		var actions = Row();
		AddButton(actions, Text("OpenFolder"), 0, 0.32f, BlueprintLibrary.OpenFolder);
		AddButton(actions, Text("Reload"), 0.34f, 0.3f, () => { BlueprintLibrary.Refresh(); ShowTablet(); });
		AddButton(actions, Text(ProjectionSystem.ShowGhosts ? "GhostsOn" : "GhostsOff"), 0.66f, 0.34f, () => { ProjectionSystem.ShowGhosts = !ProjectionSystem.ShowGhosts; ShowTablet(); });
		list.Add(actions);

		var links = Row();
		AddButton(links, Text("OpenTerraWorld"), 0, 0.49f, () => Utils.OpenToURL("https://terraworld.dev"));
		AddButton(links, Text("OpenStructureStudio"), 0.51f, 0.49f, () => Utils.OpenToURL("https://terraworld.dev/structure-studio"));
		list.Add(links);

		var parts = Row();
		AddButton(parts, Text(ProjectionSystem.ShowTiles ? "TilesOn" : "TilesOff"), 0, 0.32f, () => { ProjectionSystem.ShowTiles = !ProjectionSystem.ShowTiles; ShowTablet(); });
		AddButton(parts, Text(ProjectionSystem.ShowWalls ? "WallsOn" : "WallsOff"), 0.34f, 0.3f, () => { ProjectionSystem.ShowWalls = !ProjectionSystem.ShowWalls; ShowTablet(); });
		AddButton(parts, Text(ProjectionSystem.ShowLiquids ? "LiquidsOn" : "LiquidsOff"), 0.66f, 0.34f, () => { ProjectionSystem.ShowLiquids = !ProjectionSystem.ShowLiquids; ShowTablet(); });
		list.Add(parts);

		AddHeading(Text("LayersHeading"));
		var layerMode = Row();
		AddButton(layerMode, Text(ProjectionSystem.LayerMode ? "LayersOnButton" : "LayersOffButton"), 0, 0.32f, () => {
			if (ProjectionSystem.LayerMode) ProjectionSystem.LayerMode = false;
			else ProjectionSystem.SetLayer(ProjectionSystem.LowestUnfinishedLayer());
			ShowTablet();
		});
		AddButton(layerMode, Text("RowsPerLayer", ProjectionSystem.LayerRows), 0.34f, 0.3f, () => { ProjectionSystem.CycleRows(); ShowTablet(); });
		AddButton(layerMode, Text("LowestUnfinished"), 0.66f, 0.34f, () => { ProjectionSystem.SetLayer(ProjectionSystem.LowestUnfinishedLayer()); ShowTablet(); });
		list.Add(layerMode);
		if (ProjectionSystem.LayerMode) {
			var stepper = Row();
			AddButton(stepper, Text("LayerDown"), 0, 0.24f, () => { ProjectionSystem.SetLayer(ProjectionSystem.Layer - 1); ShowTablet(); });
			var status = new UIText(Text("LayerStatus", ProjectionSystem.Layer + 1, ProjectionSystem.MaxLayerCount, ProjectionSystem.LayerRows), 0.85f) { HAlign = 0.5f, VAlign = 0.5f };
			stepper.Append(status);
			AddButton(stepper, Text("LayerUp"), 0.76f, 0.24f, () => { ProjectionSystem.SetLayer(ProjectionSystem.Layer + 1); ShowTablet(); });
			list.Add(stepper);
		}
		AddLine(Text("LayerHint"), Color.LightGray);

		AddHeading(Text("BlueprintsHeading"));
		if (BlueprintLibrary.Entries.Count == 0) AddLine(Text("NoBlueprints", BlueprintLibrary.Folder), Color.LightGray);
		foreach (var entry in BlueprintLibrary.Entries) {
			if (entry.Blueprint == null) {
				AddLine($"{entry.FileName}: {entry.Error}", Bad);
				continue;
			}
			var blueprint = entry.Blueprint;
			bool selected = ProjectionSystem.Selected?.Hash == blueprint.Hash;
			string label = $"{(selected ? "> " : "")}{blueprint.Name}  ({blueprint.Width}x{blueprint.Height})";
			AddClickable(label, selected ? Good : Color.White, () => {
				ProjectionSystem.Selected = blueprint;
				SoundEngine.PlaySound(SoundID.MenuTick);
				ShowTablet();
			});
			if (blueprint.MissingContent.Count > 0)
				AddLine(Text("MissingContent", string.Join(", ", blueprint.MissingContent.Take(4))), Color.Orange);
		}
		if (ProjectionSystem.Selected != null) AddLine(Text("PlaceHint"), Accent);

		AddHeading(Text("ProjectionsHeading"));
		if (ProjectionSystem.Projections.Count == 0) AddLine(Text("NoProjections"), Color.LightGray);
		foreach (var projection in ProjectionSystem.Projections.ToList()) {
			var row = Row();
			var label = new UIText($"{projection.Blueprint.Name}  @ {projection.Plan.OriginX}, {projection.Plan.OriginY}", 0.85f);
			label.VAlign = 0.5f;
			row.Append(label);
			AddButton(row, Text("Remove"), 0.74f, 0.26f, () => { ProjectionSystem.Remove(projection); ShowTablet(); });
			list.Add(row);
		}
	}

	// ---- Builder ------------------------------------------------------------

	public void ShowBuilder(BlueprintBuilderEntity entity)
	{
		builder = entity;
		title.SetText(Text("BuilderTitle"));
		list.Clear();

		if (Main.netMode == NetmodeID.MultiplayerClient) {
			AddLine(Text("SinglePlayerOnly"), Color.Orange);
			return;
		}

		var projection = entity.Projection;
		if (projection == null) {
			AddHeading(Text("LinkHeading"));
			if (ProjectionSystem.Projections.Count == 0) AddLine(Text("NoProjections"), Color.LightGray);
			var here = entity.Position.ToVector2();
			foreach (var candidate in ProjectionSystem.Projections.OrderBy(p => Vector2.Distance(here, p.Bounds.Center.ToVector2()))) {
				AddClickable($"{candidate.Blueprint.Name}  @ {candidate.Plan.OriginX}, {candidate.Plan.OriginY}", Color.White, () => {
					entity.Link(candidate);
					ShowBuilder(entity);
				});
			}
			return;
		}

		requirements = entity.Evaluate();
		AddLine(Text("Linked", projection.Blueprint.Name), Accent);
		AddLine(Text("Progress", requirements.Done, requirements.Total), requirements.Missing == 0 ? Good : Color.White);
		if (requirements.Blocked > 0) AddLine(Text("Blocked", requirements.Blocked), Bad);
		if (requirements.Unobtainable > 0) AddLine(Text("Unobtainable", requirements.Unobtainable), Color.Orange);
		if (entity.Building) AddLine(Text("Building"), Good);

		var actions = Row();
		AddButton(actions, Text("Deposit"), 0, 0.24f, () => Deposit(entity));
		AddButton(actions, Text("BuildNow"), 0.25f, 0.24f, () => { entity.StartBuilding(); ShowBuilder(entity); });
		AddButton(actions, Text("Withdraw"), 0.5f, 0.24f, () => { entity.Withdraw(Main.LocalPlayer); ShowBuilder(entity); });
		AddButton(actions, Text("Unlink"), 0.75f, 0.25f, () => { entity.Link(null); ShowBuilder(entity); });
		list.Add(actions);

		AddHeading(Text("SourcesHeading"));
		var sources = Row();
		bool magicStorage = MagicStorageBridge.Available;
		float width = magicStorage ? 0.32f : 0.49f;
		AddButton(sources, Text(entity.UseInventory ? "InventoryOn" : "InventoryOff"), 0, width, () => { entity.UseInventory = !entity.UseInventory; ShowBuilder(entity); });
		AddButton(sources, Text(entity.UseBanks ? "BanksOn" : "BanksOff"), width + 0.02f, width, () => { entity.UseBanks = !entity.UseBanks; ShowBuilder(entity); });
		if (magicStorage)
			AddButton(sources, Text(entity.UseMagicStorage ? "MagicStorageOn" : "MagicStorageOff"), 0.68f, 0.32f, () => { entity.UseMagicStorage = !entity.UseMagicStorage; ShowBuilder(entity); });
		list.Add(sources);
		AddLine(Text(magicStorage ? "SourcesHint" : "SourcesHintNoMagicStorage"), Color.LightGray);

		AddHeading(Text("MaterialsHeading"));
		if (requirements.Needed.Count == 0) AddLine(Text(requirements.Missing == 0 ? "Complete" : "NothingToPay"), Good);
		var available = entity.AvailableInSources(Main.LocalPlayer);
		foreach (var (item, needed) in requirements.Needed.OrderBy(pair => entity.Stored(pair.Key) >= pair.Value).ThenBy(pair => pair.Key)) {
			int stored = entity.Stored(item);
			int inSources = available.GetValueOrDefault(item);
			string line = $"[i:{item}] {Lang.GetItemNameValue(item)}   {Math.Min(stored, needed)} / {needed}";
			if (stored < needed && inSources > 0) line += "   " + Text("InSources", inSources);
			var color = stored >= needed ? Good : stored + inSources >= needed ? Accent : Color.White;
			AddLine(line, color);
		}
	}

	void Deposit(BlueprintBuilderEntity entity)
	{
		int moved = entity.Deposit(Main.LocalPlayer, entity.Evaluate());
		if (moved > 0) SoundEngine.PlaySound(SoundID.Grab);
		var after = entity.Evaluate();
		// Everything is paid for: build without waiting for a click.
		if (!entity.Building && after.Missing > 0 && entity.HasEverything(after)) entity.StartBuilding();
		ShowBuilder(entity);
	}

	// ---- Layout helpers -----------------------------------------------------

	static UIElement Row()
	{
		var row = new UIElement();
		row.Width.Set(0, 1);
		row.Height.Set(32, 0);
		return row;
	}

	static void AddButton(UIElement row, string label, float left, float width, Action onClick)
	{
		var button = new UITextPanel<string>(label, 0.8f);
		button.Left.Set(0, left);
		button.Width.Set(0, width);
		button.Height.Set(30, 0);
		button.OnMouseOver += (_, element) => ((UIPanel)element).BackgroundColor = new Color(73, 94, 171);
		button.OnMouseOut += (_, element) => ((UIPanel)element).BackgroundColor = new Color(63, 82, 151) * 0.7f;
		button.OnLeftClick += (_, _) => { SoundEngine.PlaySound(SoundID.MenuTick); onClick(); };
		row.Append(button);
	}

	void AddHeading(string text)
	{
		var heading = new UIText(text, 0.95f) { TextColor = Accent };
		heading.Height.Set(26, 0);
		heading.Width.Set(0, 1);
		list.Add(heading);
	}

	void AddLine(string text, Color color)
	{
		var line = new UIText(text, 0.85f) { TextColor = color, IsWrapped = true };
		line.Width.Set(0, 1);
		list.Add(line);
	}

	void AddClickable(string text, Color color, Action onClick)
	{
		var entry = new UITextPanel<string>(text, 0.85f) { TextColor = color };
		entry.Width.Set(0, 1);
		entry.Height.Set(32, 0);
		entry.OnMouseOver += (_, element) => ((UIPanel)element).BorderColor = Color.Gold;
		entry.OnMouseOut += (_, element) => ((UIPanel)element).BorderColor = Color.Black;
		entry.OnLeftClick += (_, _) => onClick();
		list.Add(entry);
	}
}
