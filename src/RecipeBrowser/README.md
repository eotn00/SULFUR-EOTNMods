# 我食纪事 / what's this 0.2.2

Hover an item to see how many products use it and how many recipes produce it.
Press F8 for grouped products and recipe variants, with quantities, native artwork
and item tooltips. Chinese/English follows the game language.

悬停物品查看制作关联数量，按 F8 查看详情。按成品归类展示配方变体，材料和成品使用数字及原版贴图，悬停可查看说明。
F8 页面内可切换“查看所有配方”；默认关闭，只显示已解锁配方。

Show All Recipes defaults off. It only changes visibility, never unlocks recipes
or writes saves. OpenKey and Enabled remain configurable in
BepInEx/config/local.sulfur.recipebrowser.cfg.

Save and exit, back up plugins/config, then install whats_this.dll in BepInEx/plugins.
Remove an older CraftingReference.dll or duplicate copy before installing.
Requires BepInEx 5 and the current SULFUR APIs; does not require RPG or remote crafting.
Offline index/API/modal tests passed; live rendering still depends on game/mod versions.
