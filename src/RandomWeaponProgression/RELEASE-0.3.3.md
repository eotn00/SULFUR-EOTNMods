# 更RPG的火湖 / sulfur-RPG 0.3.3

This is the latest local 3.0.3 feature set, renumbered to 0.3.3 at the author's request.
It is not a data-format downgrade. Plugin ID, configuration and saved IDs remain unchanged.

- Retains weapon progression, equipment qualities, Charisma fixes and encoding/maintenance caches.
- Unreal equipment quality: independent 1/10,000 roll, then ordinary weights on a miss.
- Supported original numeric benefits double and penalties become zero. Special effects and discrete counts retain their existing handling.
- Armor maximum durability doubles without repairing current durability.
- Five affixes: maximum configured all-weapon damage and critical chance at double strength, followed by three applicable random affixes at double strength.
- Luck and extra jumps retain +1 per affix; duplicate affixes remain allowed.
- Existing IDs, quality and affixes do not reroll.

本版包含超现实红色品质、魅力修复、编码缓存与养护缓存优化。版本按作者要求改为 0.3.3，功能与存档格式不回退。

Exit and back up saves/config. Install only sulfur-RPG.dll; remove the old unified
更rpg的火湖.dll / RandomWeaponProgression.dll and older standalone RPG components.
Keep existing configuration and migration state. New unreal records require this
build or the local 3.0.3 implementation; do not load them with older implementations.
Restore matching save/config backups when rolling back.

Offline weapon/equipment tests passed. Actual game interaction and compatibility with
other mods still require play testing. No game assemblies are included.
