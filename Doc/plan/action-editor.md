# 动作时间轴窗口与判定盒

> 状态：已实施 · 2026-10

这份文档说明三件事怎么咬合：**动作资产长什么样**、**时间轴窗口改它的哪一半**、**判定盒是怎么从动画烘出来、又是怎么被用起来的**。
改窗口、改时钟、改烘培之前先读这里，改完之后按最后一节的清单跑用例。

## 数据形状：三份东西各管一段

| 位置 | 是什么 | 谁读它 |
| :--- | :--- | :--- |
| `Assets/Resources/GameData/ActionTable.asset` | 容器，`actions[]` 指向下面那些行资产 | `GameData.Load`（运行期真源） |
| `Assets/Tables/Actions/*.asset` | 一行一个 `ActionAsset`，一条招的全部窗口与轨道 | 同上，逐条 `ActionAssetConvert.ToInfo` |
| `Assets/Tables/Snapshot/Action.json` | **从资产导出的只读快照** | 运行期不读；给人看、给 git 读 diff、误改时对照还原 |

几何那一半不在表里：`UnitActionBox`（`Assets/Tables/UnitActionBox.cs`）挂在角色 prefab 的骨骼上，只管"这个盒叫什么、多大、偏多少"。
时序与规则那一半在表里，而且攻击侧与受击侧是**两个类型**（共用 `ActionBoxTrackBase` 上的 tag/窗/`priority`/`keys`）：
`AttackBoxTrackAsset` 只多一个 `ruleIndex`——"这只拳从第几帧开到第几帧、打中走哪条命中规则"；
`HitBoxTrackAsset` 只多受击侧那三样——被打通时点亮哪些临时窗（`tempBeCancelledTagTurnOn`）、双方各切进哪一招
（`attackerActionChange`/`selfActionChange`）。分成两个类型之后，"给攻击轨配受击改写"这种没人读的组合填都填不出来。
两边唯一的对账键是 **`trackTag` 与轨道的 `tag` 逐字相同**——写错一个字母不会报错，症状是"这一拳永远打不到"，
所以窗口（`MountTagHint`）与烘培（`no UnitActionBox tagged`）都会当场点名。

## 单位：一个资产里两种单位并存

- **秒**（编辑器口径）：`duration`、`cancelTags[].startFrom`、`beCancelledTags[].windowMin/Max`、
  `tempBeCancelledTags[].duration`、`inputAcceptance[].windowMin/Max`、`inputValidSec`、轨道的 `openMin/openMax`。
- **逻辑帧**（本工程口径，名字里带 Frames 或本来就是 int）：`fadeInFrames`/`fadeOutFrames`、`lengthFrames` 系、
  轨道的 `priority`、攻击轨的 `ruleIndex`、`attacks[]`、`rootMotionTween`。

秒→帧**只准在 `ActionAssetConvert.ToInfo` 里换算一次**（`RoundToInt(sec / LogicClock.Tick)`，与 `ActionDurationDumper` 同一把尺）。
帧长只有一个来源：`LogicClock.Tick`（1/60 秒），`ActionBoxBake.StepSec` 和窗口的 `FrameSec` 都是它的别名。
任何地方再写一个秒常数（`1/50`、`0.02`…）就是给这张表添第二个真相，窗口里每一个窗都会跟着漂。

资产为什么存秒而不是帧：窗口的吸附粒度、烘培的采样步长、预览模型 `ActionModel` 全按秒走，
"帧/秒"只是窗口的显示外壳（默认按帧，落盘永远是秒）。

## 窗口：菜单 `Combat Framework/Action Timeline`

一条身体时钟轴 + 六条可拖的区间 lane：Cancel Out / Cancel In / Temp Cancel In / Move Accept / Attack Boxes / Hit Boxes。
拖条两端吸附一个逻辑帧；`Quantize to frames` 把这一招所有窗边界一次性吸到帧格上；`Validate` 查的是"tag 拼错不报编译错"那一类。

**窗口只画它认识的这六条 lane。** 下面这些字段在窗口里看不见，只能在 Inspector 编——别以为没显示就是没数据：
`autoTerminate`、`flip`、`rootMotionTween`、`attacks[]` 本身、`说明`、`fadeInFrames`/`fadeOutFrames`。

两条盒轨 lane 的明细面板画的是各自那个类型上真被读的字段：共同的 tag 与窗、`priority`，
攻击轨再画 `ruleIndex`，受击轨再画 `tempBeCancelledTagTurnOn` 与两个动作改写。

`ruleIndex` 填的是 `attacks[]` 里那条的 **phase 号，不是数组下标**（运行期 `ActionController.IndexOfAttack` 按 phase
线性查第一条）。出货数据里 phase 全从 0 起，"phase 0"与"下标 0"恰好重合——正因为看不出差别，这一格配错的代价
不是报错而是静默：命中照样成立，结算却拿默认的 `AttackInfo`（0 伤害、0 硬直、0 推力）。所以它有三道防线：
新建攻击轨时默认指 `attacks[0].phase`（`AddEntry`）、面板配不到就红字（`DrawRulePicker`）、
`Validate` 里 `CheckAttackRules` 单独一轮（指针悬空、phase 重复、有轨而规则表为空、有规则没人指都报出来）。
命中结算的真源始终是这条动作的 `attacks[]`（`hitStunFrames`/`freezeFrames`/`canHitSameTarget`/
`hitSameTargetDelayFrames`，int 帧，在 `GameMain` 里结算），轨上只有这一个指针。

窗口的对外契约是它读的那些 `SerializedProperty` 名字（`tag`/`windowMin`/`openMax`/`keys`/`center`…）——
字段改名等于改契约，`ActionEditorLayoutTests` 与 `TimelineGraphViewportTests` 会红。

## 判定盒烘培：`Tools/ACT` 之外的 `Bake Keys` 按钮

`ActionBoxBake.Run(prefab, clip, target, setDurationToClip, lines)`：克隆 prefab → 用 `AnimationMode` 逐逻辑帧
采每个 `UnitActionBox` 的位姿 → 换算成"相对根节点、朝向归零"的单位局部系 → 按 tag 填进**已建好的**轨道的 `keys`。

- **只写 keys**：窗、命中规则、轨道增删都归窗口，烘培一概不碰。
- `setDurationToClip` 为真时会把 `duration` 覆成 clip 向上取整到帧格的长度（末键正好落在时长上；向下取整会让最后一键越界）。
- 相邻两帧盒心位移超过 2cm 才记一个键，所以"轨迹近乎不动"的轨只有一两个键，这是设计不是漏烘。
- 写盘前分三条网格吸附（位置/半长 3 位小数、轴与时刻 4 位小数）——不吸附的话浮点噪声会让每次重烘的 diff 满屏。
- 采样用 `AnimationMode.SampleAnimationClip`，**不是** `Animator.Play`/`Update`：后者在 EditMode 不评估人形曲线，
  用它的结果是整条轨都停在第 0 帧，而线框照样画得出来。这条由 `ActionBoxBakeTests` 钉住。

**烘出来的 keys 目前运行期没人读**：命中判定走的仍是 prefab 上的 `SphereCollider`/`CapsuleCollider` trigger，
`ToInfo` 也不带 keys。接上它们是 `beatemup-port.md` 的 A/F 两行；在那之前，线框（`DrawWireObb`）是 keys 唯一的消费者，
所以要真看到东西得先在窗口里烘一次——仓库里 15 份动作资产的 `keys` 全是空的，这不是坏了。

## 已知限制

- **标尺刻度按秒**。`TimelineGraphGUI.DrawRuler` 的步长取"整齐的" 1/2/5×10ⁿ，天生是秒；换成帧格要动被视口用例钉着的公共换算。
  窗边界本身已经吸到帧格上，只是刻度读数不显示整帧。
- **`说明` 不参与逐字段对拍**：它在快照里一行是裸字符串、一行是字符串数组，`JsonUtility` 吃不下两种形状（会直接忽略），
  所以只比多重集（`CommentsSurviveAsMultiset`）。
- **编辑窗口与运行期的开窗判据不是同一套**：窗口/资产按半开秒窗（`ActionRules.WindowOpen`），
  运行期比的是整数帧号（`FrameRange`，`ActionController.CalculateBoxInfo`）。两边各自的语义都写在自己的注释里，
  "拖出来的 N 帧"与"实际开着的 N 帧"是否逐格对齐，还没有一条常驻用例对账过——动窗口语义前先补这条对账。
- **2D 精灵的镜像缺口**：`FacingApply.SpriteScaleX` 是反射不是旋转，烘出来的局部位姿在角色反手时会留在错误一侧，
  见 `Doc/issue/sprite-box-mirroring-deferred.md`（工程里今天还没有精灵角色）。

## 改完之后跑什么

| 用例 | 钉住的东西 |
| :--- | :--- |
| `Assets/Editor/ActionFrameClockTests.cs`（21 条） | 运行期的"第几帧判到、第几帧接得上"：时钟、窗口、顿挫、走位 |
| `Assets/Editor/Tests/ActionEditorLayoutTests.cs`（12 条） | 车道布局、秒↔像素映射、盒轨标签与"没烘到就标红" |
| `Assets/Editor/Tests/TimelineGraphViewportTests.cs`（22 条） | 缩放/平移/播放头/拖拽落点，以及 `ToWorldObb` 与 `Quaternion.Euler` 同向 |
| `Assets/Editor/Tests/ActionTableEquivalenceTests.cs`（5 条） | 资产 ↔ 快照逐字段相等 + 行序（平手 tie-break）+ 时长换算 |
| `Assets/Editor/Tests/ActionBoxBakeTests.cs`（2 条） | 两次烘培位级一致；EditMode 真的驱动骨骼（出拳手必须走 ≥3cm） |
| `Assets/Editor/Tests/AttackRuleTests.cs`（3 条） | `ruleIndex` 这根指针：15 份资产全部指得着、新建轨的默认值、空 keys 也吞不掉校验 |

再加两个菜单：`Tools/ACT/打印动作时长`（表里 `durationFrames` ↔ Animator 实际长度，差一帧所有窗口就全偏）、
`Tools/ACT/动作表资产 → 导出 JSON 快照`（改完资产必须跑，否则 `ActionTableEquivalenceTests` 红——这是有意的）。

相关：`Assets/Tables/AGENTS.md`（表这一层的规矩）、`Assets/Scripts/AGENTS.md`（运行期时钟与热路径）、
`Doc/decision/0001-帧数据真源在表不在动画.md`（帧数据为什么以表为准）。
