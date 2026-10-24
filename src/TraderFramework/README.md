# Trader Framework 0.1.4 功能测试版

现在提供功能测试版，不再是只读诊断探针。需要配套 Inventory Redux 0.1.5。
This is a functional test build, not the read-only probe. Requires Inventory Redux 0.1.5.

0.1.4 将购买回滚失败改为提醒，失败的交易结束后不再因此禁止后续保存，由用户自行检查物品、金币并决定是否保存退出。回滚和数量/金币核对仍执行，不会因为提醒而主动保存。启动或生成库存失败的检查保持独立。Rummage 兼容补丁无需更新。
0.1.4 turns failed purchase recovery into a warning. Once the failed transaction ends, it no longer blocks subsequent saving; players check items/currency and decide whether to save and exit. Rollback and conservation checks still run. Warnings do not initiate saving. Initialization and stock-generation failures remain separate. No Rummage compatibility patch update is required.

0.1.3 修复商人购买物品无法和普通物品合堆，以及整组出售后价格再次乘数量的问题。普通可堆叠物品恢复原版序列化不保留购入价的规则，卖价按普通同类物品计算；旧版购入价在物品加载时清理，不改物品数量、位置、属性或锁定记录。商人本次回购物品保留正确单价，重新买回背包后清除临时交易历史，支持再次合堆。装备价格规则不变。这次只需替换 TraderFramework.dll，Inventory Redux 不需要更新。
0.1.3 fixes purchased goods refusing to merge with ordinary goods, and sold stack prices being multiplied by quantity a second time. Fungible stackable goods follow native serialization's purchase-history reset and use the ordinary resale value for that item type. Old purchase history is cleared as items load, without changing quantities, positions, attributes or locks. Merchant buyback stock retains the correct per-unit quote; repurchased goods discard temporary trade history and can merge again. Equipment pricing is unchanged. Only TraderFramework.dll needs replacing; Inventory Redux does not need an update.

0.1.2 修复购买物品落在背包底部：优先可见区域、当前页其余区域，再找其他页。修复 RPG 品质定义副本导致的商品核对误报，使用稳定 ItemId，装备仍然逐件保留，不合并品质属性。
0.1.2 prioritizes visible backpack slots, then the rest of the current page, then other pages. Stock validation uses stable ItemIds to support runtime quality-definition clones. Equipment stays separate with its attributes intact.

0.1.1 修复购买时 InventoryData 无默认构造函数导致的回滚；改为扣款前完成显式数据副本。配件、卷轴、魔油和枪膛凿子不再翻倍。
0.1.1 fixes purchase rollback caused by JSON.NET being unable to construct InventoryData. Data copies now use explicit constructors before payment. Attachments, scrolls, oils and chamber chisels are no longer doubled.

## 下次更新待办 / Planned Changes

- 修正投掷物分类：商人生成规则当前把所有 WeaponSO 都归为不可翻倍、不可堆叠装备。改为与 Inventory Redux 的可堆叠判断协调，让符合条件的投掷物正常翻倍并合堆，枪械、护甲、饰品继续保持原规则。仅为待办，本次未修改或安装插件。
  Fix throwable classification: the stock generator currently treats every WeaponSO as nonstackable equipment. Align it with Inventory Redux's stackability rules so eligible throwables double and stack, while firearms, armor and accessories retain their existing rules. Planned only; no plugin change or installation yet.

## 已确认玩法

- 武器、护甲、饰品的生成数量保持原版，不堆叠装备。
- 枪械配件、卷轴、魔油、枪膛凿子数量保持原版；其余普通商品生成数量乘 2，兼容的重复商品合并堆叠。
- 右键购买整组，Ctrl + 右键购买一个，Alt + 右键购买一半（向下取整，至少一个）。
- 购买操作与 Inventory Redux 的拆分、价格和入库逻辑协调。

## 安装与测试 / Installation And Testing

1. 关闭游戏并备份存档和插件；合并安装包的 BepInEx 文件夹到游戏目录。
   Close the game, back up saves/plugins, and merge the included BepInEx folder into the game directory.
2. 移出 ImprovedTraders.dll 和 TraderFramework.Probe.dll，避免重复接管。
   Remove ImprovedTraders.dll and TraderFramework.Probe.dll from the plugins directory.
3. 让商人正常补货/刷新库存。旧存货不会因为读档或重新开店就翻倍；单纯关闭再打开也不会刷新。
   Trigger a native merchant restock. Loading/reopening does not double saved stock or trigger a restock.
4. 右键购买整组；Ctrl + 右键购买一个；Alt + 右键购买一半，向下取整、至少一个。Ctrl 优先，按住不连买。
   Right-click buys the stack. Ctrl + right-click buys one. Alt + right-click buys half, rounded down, minimum one. Ctrl takes priority. No hold-to-buy.
5. 先小额购买，再核对金币、商人余量和背包增加量，并检查退出重进后的保存结果。
   Test a small purchase first, verify payment/stock/received quantities, then check save/reload consistency.

## 安全与范围 / Safety And Scope

- 只修改原版 ShopKeeper 新生成商品，不改随机池和权重。普通/隐藏库存分别保存。
  Only newly generated native ShopKeeper stock changes. Selection pools/weights and separate normal/hidden inventories stay native.
- 可堆叠的重复商品合并；装备、自动消耗物和带耐久物不堆叠。玩家堆叠上限为 1 时商人也不堆叠。
  Stackable duplicates are grouped. Equipment, auto-consumed items and durable goods remain separate. A stack limit of one disables grouping.
- 购买前检查完整金额和背包空间，优先补充兼容堆叠（包括锁定目标），再占空位。条件不足不部分购买。
  Checks full funds/capacity before purchase. Fills compatible stacks, including locked receivers, before using empty slots. No partial purchase on failure.
- 保留原版折扣、储存金币/随身金币扣款顺序；普通可堆叠物品不按购买来源区分卖价，装备等特殊单件商品保留原版购买回调和购入价。
  Retains native discounts and coin payment order. Fungible stackable goods use the ordinary resale value regardless of acquisition source; equipment retains purchase history and native buy callbacks.
- 堆叠购买过程中暂缓写盘；数量和金额核对后才保存。异常时尝试恢复物品、金币及内存存档；恢复失败则提醒，后续是否保存由用户决定。
  Stack purchases defer disk writes until checks pass. Failures attempt to restore inventory/currency/save fields. Failed recovery warns without blocking subsequent saving.
- 原版商人格子容量有限；若翻倍后的新货放不下，会安全停止并阻止保存，而非悄悄删除溢出商品。
  The merchant grid is finite. Overflow stops the plugin and blocks saving instead of silently discarding generated stock.
- 不接管邮票、打印和独立弹药服务，没有每帧库存扫描。
  Postal, printing and independent ammo services are not replaced. No per-frame stock scanning.
- 无法保证撤销第三方回调的一切外部副作用，如音效、统计或其他模组的额外操作。
  Sounds, analytics and arbitrary third-party callback side effects cannot all be undone.

## 历史只读探针 / Historical Probe

文件：build/TraderFramework.Probe.dll。功能版安装时会移出插件目录并保留备份。
只监听原版 ShopKeeper 的 AddItems(VendorTable)、AddItemsFromData、ResetInventory、ResetVendorShops。
不调用交易、物品创建/销毁、存档写入，也不增加每帧扫描。
日志写到 BepInEx 的常规日志，由其既有配置决定输出位置。

记录示例格式（示例中的数量不是实测）：

```text
BEGIN vendor=<id> event=generate channel=normal-or-merged fixed=... weighted=... draws=...
END vendor=<id> event=generate ... offers=... units=... distinct=... syncMs=... result=ok
```

generate 是新生成商品，load-saved 是恢复已有库存。
restock-request 是请求刷新，restock-actual 才是真正补货。
一次实际补货可能依次生成普通和隐藏两套库存。
syncMs 只计算该次同步调用，不包含后续帧初始化和完整交互延迟。
探针不会自动记录按键、截图或完整存档。

## 构建和离线检查

```powershell
.\build.ps1
dotnet run --project .\tests\Validation.csproj -- .\build 'D:\game\online\Steam\steamapps\common\SULFUR'
dotnet run --project ..\UnifiedInventory\tests\Validation.csproj -- .\build\InventoryRedux.dll
```

算法和当前二进制接口离线检查已通过。Unity 实机交易、原版回调、第三方兼容和实际写盘仍需游戏内验证。
Offline algorithms and binary contracts pass. Live trading, callbacks, third-party compatibility and actual disk writes still need verification.
