# Church Stash Crafting 1.0.0

Local BepInEx 5 / Harmony plugin for the installed SULFUR build.

## Behavior

- Only runs in ChurchHub, in the native recipe-list crafting workflow (including Quick Craft).
- Ingredients: backpack first, crafting grid second, then loaded church stash grids.
- Uses actual stack quantities, aggregates repeated ingredients, ignores equipped, dragged and locked items.
- Uses live grids rather than editing or resizing Big Backpack / Big Stash storage.
- Outputs merge into unlocked compatible stacks, or occupy free backpack cells across logical pages.
- Items cannot cross backpack page boundaries. Output auto-equip is disabled.
- Plans the whole batch before consuming anything, including cells freed by spent ingredients.
- Keeps spent ingredient objects alive until all outputs are placed; restores them on placement failure.
- Synchronizes touched stash lists, including an emptied stash, to prevent Big Stash's empty-save guard from restoring old contents.
- Leaves normal save-to-disk timing to the game; no per-craft forced disk save.
- Native manual-grid crafting outside the recipe list is unchanged. No remote storage access outside church.

## Build And Test

From the repository root, run `build.ps1 -GameRoot <game-directory>` and `test.ps1 -GameRoot <game-directory>`.
Tests exercise pure planning and check API contracts against the actual installed mod DLLs.
Unity object lifecycle, rollback and persistence still require in-game verification.

## Install / Remove

Copy `plugins/ChurchStashCrafting.dll` (or your own `build/ChurchStashCrafting.dll`) into the game's `BepInEx/plugins` while the game is closed.
Requires the installed Stacking Mod and Inventory Slot Lock. Big Backpack and Big Stash are supported without replacing either DLL.
Remove only `ChurchStashCrafting.dll` while the game is closed to disable this feature.
No custom item identifiers or new save-file format are introduced.
