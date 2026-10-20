# SULFUR-EOTNMods

EOTN为火湖制作的mod存放地：更 RPG 的火湖与实用 Mod。

三个独立的 BepInEx 5 插件。本项目只提供自制插件及源码，不包含游戏本体、第三方 Mod、个人存档或配置。

| Mod | 版本 | 插件文件 |
| --- | --- | --- |
| 更rpg的火湖 | 2.0.0 | `更rpg的火湖.dll` |
| 存档兼容修复 | 1.0.0 | `SaveCompatibilityFix.dll` |
| 教堂仓库制作 | 1.0.0 | `ChurchStashCrafting.dll` |

## 下载与安装

下载 [`releases/SULFUR-EOTNMods-2026.10.20.zip`](releases/SULFUR-EOTNMods-2026.10.20.zip)，或从 [`plugins/`](plugins/) 获取需要的 DLL。

1. 安装 BepInEx 5，以及所选插件需要的第三方 Mod。
2. **正常保存并退出游戏，备份整个存档目录及插件配置。** 默认存档位于 `%USERPROFILE%/AppData/LocalLow/Perfect Random/Sulfur/`。
3. 将需要的 DLL 放入游戏的 `BepInEx/plugins/`，不需要全部安装。
4. 重新启动游戏。首次新增插件时出现 Mod 变更提醒属于正常现象。

已在 SULFUR 0.19.10、BepInEx 5.4.23.5 的本地环境验证。插件依赖当前游戏和第三方 Mod 的内部接口，不能保证兼容其他版本；更新前请备份。

## 更rpg的火湖

- 每把有效枪械生成独立编号和五项初始随机属性，可增益也可减益。
- 每升一级随机提升 1 至 2 项属性，保存逐级记录，读档不重抽。
- 武器详情显示初始词条、累计升级收益和逐级历史。
- 编号与属性记录随武器保存。首次迁移现有武器时会创建迁移计划与备份。

依赖原“武器拓展/武器位阶”Mod 提供的位阶与经验系统；它不改变经验曲线。设置页使用现有 OptionsScreen 设置插件。

升级时移除旧 `RankBonuses.dll`、`SingleWeaponSaveTest.dll` 和旧名称 `RandomWeaponProgression.dll`，避免重复加成。保留 `BepInEx/config/RandomWeaponProgression/` 的迁移状态，恢复旧存档时同时恢复相应状态。

详情：[武器成长说明](src/RandomWeaponProgression/README.md)。

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
