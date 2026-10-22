# 更RPG的火湖 / sulfur-RPG 0.3.3

This is the latest local 3.0.3 feature set, renumbered to 0.3.3 at the author's request.
It is not a data-format downgrade. Plugin ID, configuration and saved IDs remain unchanged.

- Performance update: caches repeated attribute-record encoding to reduce repeated work during saving.
- Caches maintenance reduction calculations to reduce equipment scans when durability is consumed.
- Retains weapon progression, equipment qualities and Charisma fixes.
- Existing IDs, quality and affixes do not reroll.

本版为性能优化更新：通过属性记录编码缓存和养护减耗缓存，减少重复编码与装备遍历。保留已有枪械成长、装备品质和魅力修复，已有记录不重抽。实际收益取决于使用场景，不承诺固定帧率提升。

Exit and back up saves/config. Install only sulfur-RPG.dll; remove the old unified
更rpg的火湖.dll / RandomWeaponProgression.dll and older standalone RPG components.
Keep existing configuration and migration state. Older implementations may not
support all records written by this build; do not downgrade without a backup.
Restore matching save/config backups when rolling back.

Offline weapon/equipment tests passed. Actual game interaction and compatibility with
other mods still require play testing. No game assemblies are included.
