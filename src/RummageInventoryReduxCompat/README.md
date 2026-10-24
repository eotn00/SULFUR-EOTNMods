# Inventory Redux - Rummage Compatibility

Independent UI compatibility patch 0.1.6 for Rummage 1.0.0 and Inventory Redux. Both original DLLs remain unchanged.

## Behavior

- Moves Rummage's search bar and results outside Inventory Redux's clipped grid.
- Anchors to the visible backpack viewport, not its full capacity or scrolled content.
- Search bar and results move together using Inventory Redux's layout drag controls (Alt + left mouse by default).
- Stores panel offsets in this patch's own BepInEx configuration, with screen bounds clamping.
- Places the backpack pager above the search bar and preserves Rummage's above/below results preference.
- Preserves search clicks in the stash interface without overriding unrelated windows and buttons.
- Prevents search typing and panel dragging from triggering Inventory Redux item actions.
- Hides the panel while another inventory input-blocking UI is open.
- Keeps Rummage's search, localization, highlighting and counts unchanged.

## Scope

0.1.6 removes experimental MiniMap/third-party shortcut guards and temporary input tracing. It only integrates Inventory Redux and Rummage. It is not a global input manager and cannot prevent unrelated mods from responding to their own global shortcuts.

The patch does not edit saves, quantities, recipes, locks or inventory capacity. It does not change other mod DLLs, disable keyboard devices or force-enable input maps.

## Installation and Removal

Requires BepInEx 5, Rummage 1.0.0 and Inventory Redux. Close SULFUR and put `InventoryRedux.RummageCompat.dll` in `BepInEx/plugins`, alongside the originals. Keep only one copy of the compatibility DLL.

Remove only this compatibility DLL while the game is closed to uninstall. The optional `local.sulfur.inventoryredux.rummagecompat.cfg` stores panel offsets.

Missing mods leave the patch inactive. Unsupported Rummage versions or changed interfaces produce a warning rather than blocking saves.

## Validation

Compilation and offline geometry/API checks do not replace live Unity testing. Verify backpack and stash search, Chinese typing, Escape, category/clear buttons, expanded result scrolling, Alt-dragging, paging, F6/F8 overlays and normal item actions after leaving search focus.
