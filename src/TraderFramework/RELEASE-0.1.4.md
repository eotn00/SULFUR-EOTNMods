# Trader Framework 0.1.4

需要 / Requires: BepInEx 5 + Inventory Redux 0.1.5 or later.

关闭游戏后，将 BepInEx 文件夹合并到游戏目录，替换旧 TraderFramework.dll，只保留一份。不要同时安装 ImprovedTraders 或 TraderFramework.Probe.dll。

Close the game and merge the BepInEx folder into the game directory. Replace the old TraderFramework.dll and keep only one copy. Do not combine with ImprovedTraders or TraderFramework.Probe.dll.

## 更新 / Changes

- 购买回滚失败改为提醒，不再因此禁止后续保存；是否保存退出由玩家决定。
- 保留数量、金币核对和回滚尝试；提醒不会主动保存。
- 包含此前购买物品不能合堆、出售后价格重复乘数量的修复。

- Failed purchase recovery now warns without blocking subsequent saving; players decide whether to save and exit.
- Quantity/currency checks and rollback attempts remain. Warnings do not initiate saving.
- Includes the earlier fixes for purchased goods not merging and sold stack prices being multiplied by quantity twice.

右键购买整组，Ctrl + 右键买一个，Alt + 右键买一半。Rummage 兼容补丁无需更新。

Right-click buys the stack; Ctrl + right-click buys one; Alt + right-click buys half. No Rummage compatibility patch update is required.

投掷物生成分类修正已记录为后续更新，本版未包含。初始化或商品生成异常的检查不属于本次交易回滚提醒调整。

The throwable stock-classification fix is deferred. Initialization and stock-generation failure checks are separate from this transaction-recovery update.
