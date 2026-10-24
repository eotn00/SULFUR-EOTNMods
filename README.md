# SULFUR-EOTNMods

EOTN为火湖制作的mod存放地：更 RPG 的火湖与实用 Mod。

七个 BepInEx 5 插件与兼容补丁。本项目只提供自制插件及源码，不包含游戏本体、第三方 Mod、个人存档或配置。

| Mod | 版本 | 插件文件 |
| --- | --- | --- |
| 更RPG的火湖 / sulfur-RPG | 0.3.3 | `sulfur-RPG.dll` |
| 库存重构 / Inventory Redux | 0.1.5 | `InventoryRedux.dll` |
| Trader Framework | 0.1.4 | `TraderFramework.dll` |
| Inventory Redux - Rummage Compatibility | 0.1.6 | `InventoryRedux.RummageCompat.dll` |
| 我食纪事 / what's this | 0.2.2 | `whats_this.dll` |
| 存档兼容修复 | 1.0.0 | `SaveCompatibilityFix.dll` |
| 教堂仓库制作 | 1.0.0 | `ChurchStashCrafting.dll` |

## 下载与安装

最新下载：[sulfur-RPG 0.3.3](releases/sulfur-RPG-0.3.3.zip)、[Inventory Redux 0.1.5](releases/InventoryRedux-0.1.5.zip)、[what's this 0.2.2](releases/whats_this-0.2.2.zip)。也可从 [`plugins/`](plugins/) 单独获取 DLL。历史压缩包保留，但不要重复安装旧 DLL。

配套下载：[Trader Framework 0.1.4](releases/TraderFramework-0.1.4.zip)（需要 Inventory Redux 0.1.5+）、[Rummage 兼容补丁 0.1.6](releases/InventoryRedux-Rummage-Compatibility-0.1.6.zip)（另需 Rummage 1.0.0）。三个压缩包分别安装，不捆绑第三方 Mod。

新版说明：[RPG](src/RandomWeaponProgression/RELEASE-0.3.3.md)、[Inventory Redux](src/InventoryRedux/README.md)、[我食纪事](src/RecipeBrowser/README.md)。RPG 从本地 3.0.3 重新编号为 0.3.3，并非功能降级；内部 ID、配置和记录格式不变。

1. 安装 BepInEx 5，以及所选插件需要的第三方 Mod。
2. **正常保存并退出游戏，备份整个存档目录及插件配置。** 默认存档位于 `%USERPROFILE%/AppData/LocalLow/Perfect Random/Sulfur/`。
3. 将需要的 DLL 放入游戏的 `BepInEx/plugins/`，不需要全部安装。
4. 重新启动游戏。首次新增插件时出现 Mod 变更提醒属于正常现象。

已在 SULFUR 0.19.10、BepInEx 5.4.23.5 的本地环境验证。插件依赖当前游戏和第三方 Mod 的内部接口，不能保证兼容其他版本；更新前请备份。

## 更RPG的火湖 / sulfur-RPG 0.3.3

- 枪械六维随机属性：伤害、射速、换弹、精准、控枪和最大耐久；旧五维记录补零耐久维度，不重抽。
- 每升一级随机提升 1 至 2 项属性，保存逐级记录，读档不重抽。
- 武器详情显示初始词条、累计升级收益和逐级历史。
- 编号与属性记录随武器保存。首次迁移现有武器时会创建迁移计划与备份。
- 护甲和饰品支持六档品质，以及精良/史诗/传奇的 1/2/3 条额外词条；原有正负属性按有益方向处理。
- 枪械与装备功能合并在一个插件中，可以分别开关；支持中文与英文界面，以及品质名称着色。
- 魅力参与品质浮动；已有装备保留编号、品质、属性和额外词条。
- **0.3.3 性能优化**：缓存重复的属性记录编码，减少保存流程中的重复处理；缓存养护减耗计算结果，减少扣耐久时的重复装备遍历。实际收益取决于装备数量与使用场景，不承诺固定帧率提升。

Firearms receive six birth rolls and random per-rank growth. Armor and accessories receive quality adjustments and additional affixes, including Charisma support. Both modules can be toggled independently and support Chinese and English. Version 0.3.3 reduces repeated record encoding and equipment scans through encoding and maintenance caches, without rerolling existing records. Performance gains depend on the workload.

枪械升级使用原“武器拓展/武器位阶”Mod 提供的位阶与经验系统，本插件不改变经验曲线。OptionsScreen 设置插件只提供游戏内设置页；没有它仍可使用 Mod 并编辑配置文件。

升级时移除旧独立 `EquipmentQualityTest.dll`、`RankBonuses.dll`、`SingleWeaponSaveTest.dll` 和旧名称 `RandomWeaponProgression.dll`，替换旧主插件，只保留一份 RPG DLL。保留 `BepInEx/config/RandomWeaponProgression/` 和 `BepInEx/config/EquipmentQuality/` 的迁移状态，恢复旧存档时同时恢复相应状态。统一配置为 `BepInEx/config/local.sulfur.randomweaponprogression.cfg`。

当前更新与安装说明：[0.3.3 说明](src/RandomWeaponProgression/RELEASE-0.3.3.md)。[3.0.1 说明](MoreRPGSulfur-3.0.1-README.txt)及 [2.0 武器成长说明](src/RandomWeaponProgression/README.md) 仅供历史参考。

## 库存重构 / Inventory Redux 0.1.5

- 将背包与仓库扩容、分页、滚动、整理、锁定和堆叠整合到一个插件中；装备不堆叠。
- F6 配置容量、每页行数、页数、可见行数、缩放和操作键位；支持拖动容器、提示、弹药与货币栏的位置。
- Shift 框选后拖动任意选中物品即可整组移动，并显示目标落点预览；尽量保留相对位置，无法保留时尝试空位排布。
- 指向选中物品按 L 批量锁定；全部已锁定时批量解锁。锁定物品不被自动整理搬走，但可接收兼容堆叠，也可手动移动与拆分。
- 支持拆分半堆、只取一个、手持堆叠连续放一个及单次放一半；新分出的物品不继承锁定。
- 仓库界面提供“全部存入”和“补充堆叠”；支持右键划过连续转移、Ctrl + 左键划过连续丢弃。
- 读档核对数量与布局，差异显示减少的种类与总数量；读档差异或交易回滚失败改为提醒，玩家自行决定是否保存退出。完整[更新说明](src/InventoryRedux/RELEASE-0.1.5.md)。

One plugin for backpack/stash capacity, equal-sized pages, scrolling, group dragging with previews, locking, sorting and non-equipment stacking. Includes batch lock/unlock, split/pour controls, deposit/top-up actions and configurable layout. Locked stacks stay in place during sorting but can receive compatible items. Back up saves before installation and updates.

安装前移出冲突的旧背包、仓库、锁定、堆叠与框选插件，只保留一份 `InventoryRedux.dll`。从旧名升级时移出 `UnifiedInventory.dll`，保留原配置。详见[完整说明](src/InventoryRedux/README.md)。

## 商人与搜索兼容 / Trading and Search Compatibility

Trader Framework 支持右键购买整组、Ctrl + 右键买一个、Alt + 右键买一半；修复购入物品不能合堆和出售堆叠价格重复乘数量的问题。0.1.4 将购买回滚失败改为提醒。需 Inventory Redux 0.1.5+。详见[安装说明](src/TraderFramework/RELEASE-0.1.4.md)。

The trader module supports whole-stack, single and half-stack purchases, with compatible purchased stacks and corrected per-unit pricing. Failed purchase recovery warns without blocking subsequent saving.

独立 Rummage 兼容补丁修正搜索栏被裁切、仓库界面点击和输入问题，支持 Alt + 左键一起拖动搜索栏与结果。需要另外安装 Rummage 1.0.0 和 Inventory Redux。原版 DLL 不修改，不包含全局按键管理。详见[补丁说明](src/RummageInventoryReduxCompat/README.md)。

The independent Rummage patch fixes clipped panels and stash interaction, and supports moving search/results together. Requires both original mods. It does not include a global shortcut manager.

## 我食纪事 / what's this 0.2.2

- 悬停物品时，在说明里显示它能制作多少种成品、拥有多少个制作来源，并提示按 F8 查看详情。
- F8 页面按成品分组展示具体配方变体，材料与产物使用数量和原版贴图，悬停图标可查看物品说明。
- 默认只查看玩家已解锁的配方；页面内可切换“查看所有配方”，仅影响显示，不实际解锁配方。
- 支持滚动浏览，中英文随游戏语言适配；快捷键可配置。
- 只读查询，不修改存档、物品或制作流程，不依赖 RPG 和远程制作插件。

Hover an item for recipe counts, then press F8 for grouped recipe variants with quantities, native artwork and tooltips. Learned recipes are shown by default; an in-window option displays all recipes without unlocking them. Chinese/English follows the game language. Read-only, with a configurable hotkey.

安装 `whats_this.dll`，移出旧名称 `CraftingReference.dll` 或其他重复副本。详见[完整说明](src/RecipeBrowser/README.md)。

## 存档兼容修复

针对当前“锁定格子”和“OptionsScreen 设置项”两个插件：

- 读档重建物品期间放行锁定检查，避免锁定物品丢失、只留下空锁格；正常操作仍保留锁定保护。
- 用存档槽独立的旁路文件保存已识别 Mod 列表，避免旧 `.es3` 迁移导致每次都提示全部新增。
- **首次运行会把当前插件清单认可为现有存档的基线**；之后真正增删插件仍会提醒。第一次运行前务必确认所需插件齐全。

依赖现有“锁定格子”和“OptionsScreen 设置项”。记录位于存档目录的 `BepInExPluginBaselines/`，备份存档时一并保留。本插件不会找回安装前已经消失的物品。

## 教堂仓库制作

- 教堂内使用原配方列表制作时，优先消耗背包材料，其次制作台，最后箱子。
- 统计真实堆叠数量，不消耗锁定、装备中或正在拖动的物品。
- 成品进入背包，可合并已有堆叠并使用大背包其他页；不自动装备。
- 放不下整批成品时不扣材料；制作过程包含放置失败的回滚处理。
- 与本地“堆叠”“大背包”“大仓库”插件做了适配，不替换这些插件。外出时不能远程使用箱子。

必需“堆叠 Mod”和“锁定格子”插件。快捷合成打开的原生配方列表也适用；手动往制作格放材料的原生流程保持不变。

## 编译与检查

Windows 下需要 .NET Framework C# 编译器；测试另需 .NET 9 SDK。引用合法安装的游戏/BepInEx 本机程序集，不随仓库分发。

```powershell
.\build.ps1 -GameRoot '你的 SULFUR 游戏目录'
.\test.ps1 -GameRoot '你的 SULFUR 游戏目录'
```

编译输出在 `build/`。仓库的 `plugins/` 是已部署实测版本，编译不会自动替换它们。

自动检查不能代替 Unity 内物品生命周期、制作和保存重载测试。反馈问题时请附版本、步骤及脱敏日志，不要公开上传个人存档或账号凭据。
