# 命中"相交"的粒度是物理步，不是逻辑帧

> 状态：未处理 · 2026-10

解决的问题：Phase 2 把逻辑层统一到了 60Hz 的整型帧时钟上，但"这一拳到底打到了没有"仍然不走这个时钟，所以它是逻辑一致性上剩下的唯一一处时基漏洞。

## 现象

判定盒**开不开**在逻辑帧上：`ActionController.Tick()` 按表里的帧区间算出 `ActiveAttackBoxMask`，`GameMain.TickBoxes()` 据此把盒子的 `enabled` 打开或关掉，一个逻辑帧一次，跨渲染帧率完全一致。

判定盒**打到人没有**不在逻辑帧上：`AttackHitBox.OnTriggerEnter` / `OnTriggerExit`（`Assets/Scripts/Character/Boxes/AttackHitBox.cs`）是 Unity 物理回调，触发频率是物理步，由 `ProjectSettings/TimeManager.asset` 的 `Fixed Timestep: 0.02` 决定，也就是 50Hz——跟 60Hz 的逻辑帧对不齐，两者互相滑移。

## 后果

- 盒子只开一两个逻辑帧的窄窗口，恰好赶上物理步没有采到重叠，这一拳就静默打空；反过来也可能在盒子已经关掉的当帧报一次交。
- 位移越快越容易漏。击退、冲刺这类大幅位移下，两个物理步之间角色能走几十厘米，触发盒是"穿过去"而不是"重叠过"。
- 这个不稳定与 `targetFrameRate` 无关，改渲染帧率测不出来，所以 Phase 2 的六个用例覆盖不到它。

## 为什么这期不动

Phase 2 只换时基与地形，相交检测的形态属于 Phase 3 打击管线（`Doc/plan/refactor.md` 妥协 5）。要修就得一起决定下面两件事，单独动其中一件都会和 Phase 3 冲突：

1. 把 `Fixed Timestep` 从 `0.02` 对齐到 `1/60`，让物理步和逻辑帧同频。风险是全局设置：`Player.prefab` 里 `BeHitBox_Body` 与 `BeHitBox_Parry` 两个受击盒上各挂着一个**非 kinematic、关重力**的 Rigidbody（触发回调需要至少一侧有 Rigidbody，所以不能直接删），改步长会改变它们的求值节奏。
2. 或者绕开物理回调，在逻辑帧里自己解析相交（盒子都是 `BoxCollider`，用当前的位置/旋转做一次 OBB 相交测试即可，输入全是整数帧驱动的量，天然可测）。这条路彻底但要多写一个相交测试。

登记人：Phase 2 第 3 步实施过程中发现，与当次任务无关，按 `AGENTS.md` 的规则单独登记不阻塞进度。
