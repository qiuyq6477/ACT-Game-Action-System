# Assets/Tables 模块指南

动作表的**真源**（`ActionAsset` / `ActionTableAsset`，ScriptableObject）与它到逻辑层 `ActionInfo` 之间唯一的换算处。
这里的代码是运行期代码（无 asmdef → `Assembly-CSharp`），但只有 `GameData.Load` 一个消费者，不进每帧热路径。
编辑它的窗口、判定盒的烘培与线框，设计说明在 `Doc/plan/action-editor.md`。

## 单位：一个资产里两种单位并存

- **编辑器口径的字段是秒**：`duration`、`cancelTags[].startFrom`、`beCancelledTags[].windowMin/Max`、
  `tempBeCancelledTags[].duration`、`inputAcceptance[].windowMin/Max`、`inputValidSec`、轨道的 `openMin/openMax`。
- **本工程补的字段是逻辑帧**（名字里带 Frames 或本来就是 int）：`fadeInFrames`、`fadeOutFrames`、
  `lengthFrames` 系、轨道的 `priority`、攻击轨的 `ruleIndex`（它是 `attacks[]` 那条的 **phase 号**，不是下标）、
  `attacks[]`、`rootMotionTween`。盒轨分两个类型：`AttackBoxTrackAsset` 只有这一个结算指针，
  受击侧的 `tempBeCancelledTagTurnOn`/`attackerActionChange`/`selfActionChange` 只在 `HitBoxTrackAsset` 上。
- 秒→帧**只准在 `ActionAssetConvert.ToInfo` 里换算一次**（`RoundToInt(sec / LogicClock.Tick)`，与
  `ActionDurationDumper` 同一把尺）。除此之外任何地方再算一次秒，就等于给这张表添了第二个真相。
- 为什么资产存秒而不是帧：窗口的吸附粒度、烘培的采样步长、预览模型 `ActionModel` 全按秒走
  （`ActionBoxBake.StepSec` 就是本工程的 1/60）。窗口里"帧/秒"只是显示外壳，落盘永远是秒。

## 必须守的几条

- **帧长只有一个来源：`LogicClock.Tick`**（`ActionBoxBake.StepSec`、窗口的 `FrameSec` 都是它的别名）。
  任何地方再写一个秒常数（`1/50`、`0.02`…）都会让每一个窗漂走一格。
- **容器数组的顺序就是数据**：抢动作平手取靠前的一条（`ActionController` 用严格 `>`），
  `ActionTableAsset.actions[]` 的顺序必须与快照 JSON 的行序一致，新增一律追加末尾。
- **`trackTag` 与表的 tag 必须逐字相同**：`UnitActionBox.trackTag` 是挂点与轨道唯一的对账键，
  表侧 `attacks`/`attackPhase` 的 tag 由 `BoxTagRegistry` 认（只有它许认识标签字符串）。
  两边写错一个字母的症状不是报错而是"这一拳永远打不到"，所以窗口与烘培都会当场点名。
- **摆盒位置一律用 `UnitActionBox.offset`，不要改节点 transform**：受击盒挂在根节点下，
  靠 offset 顶到腰胸高度（等价于原来那颗 `m_Center {y:1}` 的胶囊），不填烘出来会贴地。
- **改了动作资产必须重导快照**：菜单 `Tools/ACT/动作表资产 → 导出 JSON 快照`，否则
  `ActionTableEquivalenceTests.SnapshotMatchesAssets` 会红（这是有意的：快照落后于真源就是回归）。
- 判定盒烘出来的 `keys` 目前**运行期无人消费**（等 `Doc/plan/beatemup-port.md` 的 A/F 两行接上）：
  现在它只喂编辑器的预览与线框。命中判定走的仍是 prefab 上的 Sphere/Capsule trigger。
  而且仓库里 15 份动作资产的 `keys` **全是空的**——线框要先在窗口里烘一次才画得出东西，别以为它坏了。
- **攻击轨只带一个结算指针**：`ruleIndex` 填的是 `attacks[]` 里那条的 **`phase` 号**（不是数组下标，
  `ActionController.IndexOfAttack` 按它线性查），伤害/顿帧/连击限制/动作改写全在 `attacks[]`（int 帧）；
  受击轨才带 `tempBeCancelledTagTurnOn`/`attackerActionChange`/`selfActionChange`。指针配错不报错，
  命中却按默认 `AttackInfo` 结算（0 伤害 0 硬直），所以 `CombatValidator.CheckAttackRules` 专门查它。
- 时间轴窗口只画它认识的六条 lane。`autoTerminate`/`flip`/`attacks[]`/`rootMotionTween`/`说明`/`fadeIn-OutFrames`
  这些**在窗口里看不见**，只能在 Inspector 编——别以为窗口里没显示就是没数据。

相关文档：`Doc/plan/action-editor.md`（窗口、单位口径、烘培、已知限制、改完跑哪些用例）、
`Doc/issue/sprite-box-mirroring-deferred.md`（2D 精灵的镜像缺口）。
