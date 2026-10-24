# Inventory Redux 0.1.5

## 更新内容 / Changes

- 读档物品变化改为提醒，显示减少的物品种类和总数量，不再因此禁止保存。
- 合堆、拆堆和移动位置不会被计为物品数量丢失。
- 交易或物品操作失败且恢复不完整时，也改为提醒，由玩家决定是否保存退出。
- 中英文提醒随游戏语言适配，并提供更多日志用于定位问题。

- Inventory changes after loading now warn instead of blocking saves, with missing item types and total quantities.
- Merging, splitting or moving stacks is not counted as quantity loss.
- Failed trade or inventory recovery also warns without blocking subsequent saving. Players decide whether to save and exit.
- Notices follow the game's Chinese/English language, with additional diagnostic logging.

## 安装 / Installation

关闭游戏，备份当前存档，将压缩包内的 BepInEx 文件夹合并到游戏目录。替换旧 InventoryRedux.dll，只保留一份。需要 BepInEx 5。

Close the game, back up your current save, and merge the archive's BepInEx folder into the game directory. Replace the old InventoryRedux.dll and keep only one copy. Requires BepInEx 5.

F6 配置背包和仓库。商人定量购买模块为可选插件；Trader Framework 0.1.4 需要本版或更新的 Inventory Redux。Rummage 用户继续使用独立兼容补丁 0.1.6，补丁无需更新。

Press F6 to configure backpack/stash settings. Trader Framework is optional; version 0.1.4 requires Inventory Redux 0.1.5 or later. Rummage users can keep compatibility patch 0.1.6 unchanged.

## 提醒 / Notice

用户可选择保存，或不保存退出以避免覆盖旧状态。Alt+F4 可能触发原版退出保存；如需跳过退出回调，可在任务管理器“详细信息”中结束游戏进程。此前已经发生的自动保存无法撤销。

You can choose to save, or exit without saving to avoid overwriting the previous state. Alt+F4 may trigger the game's exit save. Ending the game process in Task Manager > Details skips exit callbacks, but cannot undo earlier autosaves.

没有新增退出按钮。配置锁定物品参与制作的功能已推迟，不包含在本版中。

No custom exit button is added. The proposed locked-ingredient crafting setting is deferred and is not included.
