# TerraWorld Blueprints

A tModLoader mod that loads `.twpack` structure files exported from
[TerraWorld Structure Studio](https://terraworld.dev/structure-studio) and projects them into a
Terraria world as ghost blueprints, which you can build by hand or with an automatic builder.

> **This is an example mod, not an officially developed product.**
> It exists to show how a mod can read the `.twpack` format and turn it into tiles in a world.
> It is not maintained as a full release, comes with no support guarantees, and may change or be
> abandoned at any time. Feel free to read it, copy the parts you need, or build your own mod on it.

## What it demonstrates

- **Reading a `.twpack`** ([`Blueprints/Blueprint.cs`](Blueprints/Blueprint.cs)): open the ZIP, check the
  manifest, decode the binary tile records and resolve mod tiles and walls by name.
- **Mapping tiles back to items** ([`Blueprints/MaterialCatalog.cs`](Blueprints/MaterialCatalog.cs)): find the
  item that places a given tile style, wall, paint or coating, and locate a cell inside a multi-tile object.
- **Planning and placing** ([`Blueprints/BuildPlan.cs`](Blueprints/BuildPlan.cs)): order the work (walls,
  tiles bottom-up, decorations, liquids), work out what each step costs, and write tiles, chests, signs and
  vanilla tile entities into the world.
- **Drawing ghosts** ([`Systems/ProjectionSystem.cs`](Systems/ProjectionSystem.cs)): draw the projection
  over the world, filter it by layer, and show a hover tooltip with have / need counts.
- **Optional cross-mod support** ([`Blueprints/MagicStorageBridge.cs`](Blueprints/MagicStorageBridge.cs)):
  take materials from Magic Storage through reflection, without a hard dependency.

## The `.twpack` format (schema version 1)

A `.twpack` is a ZIP archive (stored, not compressed) with these entries:

| Entry | Content |
| --- | --- |
| `manifest.json` | `format: "terraworld-data-package"`, `schemaVersion: 1`, `kind: "structure"` |
| `structure.json` | Name, description, width and height |
| `payload/tiles.bin` | Magic `TWST`, version `1`, record size `28`, then one record per non-empty cell |
| `payload/entities.json` | `chests`, `signs`, `modTiles` and `modWalls` (mod content by `modName` / `contentName`) |

Each 28-byte little-endian record in `tiles.bin`:

| Field | Type | Notes |
| --- | --- | --- |
| relX, relY | u32, u32 | Cell position from the top-left |
| type, wall | u16, u16 | Vanilla tile and wall IDs |
| u, v | i16, i16 | Frame; negative when the tile is re-framed in the world |
| liquidAmount, liquidType | u8, u8 | |
| wires | u8 | Bit flags: red 1, blue 2, green 4, yellow 8 |
| tileColor, wallColor | u8, u8 | Paint IDs |
| brickStyle | u8 | 0 full, 1 half block, 2 and 3 slopes (world-file values) |
| flags | u16 | active, actuator, inActive, invisibleBlock, invisibleWall, fullBrightBlock, fullBrightWall |

Chest contents are never stored, only chest names and sign text.

## Playing with it

1. Export a `.twpack` from Structure Studio and put it in
   `Documents/My Games/Terraria/tModLoader/TerraWorldBlueprints/`.
   The tablet's **Open folder** button opens this folder, and **Reload** rescans it.
2. **Blueprint Tablet** (10 Wood at a Work Bench):
   - Right-click it to pick a blueprint, then left-click to project it. The cursor marks the bottom centre.
   - Picking a blueprint lets you place it once, so the ghost following the cursor goes away after placing.
   - Projections are saved with the world.
3. Ghosts show what is left to build:
   - Blue cells still need building.
   - Red cells are blocked by something else.
   - Finished cells disappear.

   You can build by hand by following the ghost.
   - Hover a ghost cell to see the item it needs, with how many you carry against how many are still needed.
   - The tablet can show or hide blocks, walls and liquids, and has a **Layers** view that shows one band of rows at a time, from the bottom up.
   - PageUp and PageDown step through the bands. You can change these keys under Controls → Mod keybinds.
4. **Blueprint Builder** (20 Wood, 5 Iron or Lead Bars and the tablet, at an Anvil). Link it to a projection.
   - **Pull materials** collects what the projection needs from these sources, each of which can be switched off:
     - your inventory;
     - your Piggy Bank, Safe, Defender's Forge and Void Vault;
     - the nearest Magic Storage network, if that mod is enabled.
   - Favourited items are never taken.
   - The Builder starts building as soon as everything is paid for.
5. The tablet panel has buttons that open TerraWorld and Structure Studio in your browser.

Every cell is paid for with the item that places it. Blocks already in the way are never destroyed.
Tiles that no item can place (grass, pots, natural walls) are skipped unless the server config allows them.

## Known limits

- The mod must be installed on the server and on every client.
- The Blueprint Builder works in single-player only. In multiplayer, the ghosts still work for building by hand.
- Tiles from other mods need those mods enabled. Their tile entities are not recreated.
- English text only.

## Building from source

1. In tModLoader, open **Workshop → Develop Mods** once. This creates `ModSources/tModLoader.targets`.
2. Put this folder in `ModSources/TerraWorldBlueprint`, either by copying it or with a link.
3. Build it, either from the in-game Develop Mods menu or with:

```powershell
dotnet build
```

`dotnet build` writes `TerraWorldBlueprint.tmod` into the tModLoader `Mods` folder.
- Close tModLoader first, or the build will be refused.
- If your `tModLoader.targets` is somewhere else, pass `-p:tMLTargets=<path>`.

## Links

- Structure Studio: https://terraworld.dev/structure-studio
- TerraWorld: https://terraworld.dev
