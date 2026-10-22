# 库存重构 / Inventory Redux 0.1.0

Unified backpack and stash capacity, independent pages and scrolling, stacking,
locking, sorting, splitting and group movement. Equipment never stacks.

## 使用 / Controls

- F6: configure capacity, equal-sized pages, visible rows, scaling and bindings.
- Alt + left drag: move containers, hints, ammo/currency bars and stash buttons.
- Wheel / scrollbar: scroll; Alt + wheel: scale.
- Shift + drag: select items. Drag any selected item to move the group, with preview.
- L over a selected item: lock all if any are unlocked; otherwise unlock all.
- P: sort and merge compatible stacks. Locked stacks stay in place and can receive items.
- Alt + right-click: split half; Ctrl + right-click: split one. Bindings are configurable.
- While holding a stack: hold right-click to place individual units; Alt + right-click places half once.
- Manually split portions start unlocked; the original remainder keeps its lock.
- Stash buttons: deposit all unlocked items, or top up existing compatible stacks without new slots.
- Right-button sweep transfers items in stash view; Ctrl + left-button sweep drops items.

锁定物品可手动移动和拆分，不能被自动整理搬走。锁定堆叠可接收新拾取、整理和补充堆叠的兼容物品。
框选后指向其中一个物品按 L，混合状态统一锁定，全部锁定时统一解锁。
仓库批量存入跳过锁定与穿戴物品；装备永不堆叠。

## 安装与安全 / Installation and Safety

Exit the game and back up saves and configs first. Install only InventoryRedux.dll.
Remove UnifiedInventory.dll and conflicting Big Backpack, Big Stash, Scroll Bags,
Inventory Slot Lock, InventoryLockScopes, Stacking Mod, Inventory Box Select,
old automatic sorter and Better Move Item plugins. Do not delete their backups.

Internal ID/config remains local.sulfur.unifiedinventory. Existing settings survive renaming.
Native item records are retained; locks use extra JSON metadata. Loading checks quantity,
position, dimensions and rotation. Unsafe load/placement states block saving.
This is not a universal transaction system: backups remain necessary, and external
mod callbacks or older save damage are not guaranteed recoverable.

用户已验证主要交互；离线算法和接口检查通过，但不能代替所有组合的 Unity 实测。
Source is in this folder; use the repository build.ps1 with -GameRoot.
