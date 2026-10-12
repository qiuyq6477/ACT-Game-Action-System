# BeatEmUp 移植方案：用 ACT 框架重实现清版动作

> 状态：规划中 · 2026-10

这份方案解决的是一个问题：**怎么把 `/Users/zlqiu/Documents/GitHub/BeatEmUp` 那套能玩的清版动作玩法，搬到本工程的动作框架上重做**——素材与玩法内容来自 BeatEmUp，动作系统、时基、判定、性能红线全部用本工程的。两边框架不兼容的地方，按本工程的规矩给本工程加功能，而不是把 BeatEmUp 的实现搬过来将就。

看完这一篇应该能回答三件事：为什么要重做而不是搬代码、哪些是框架缺口、分几步验收。

---

## 一、两个工程的现状

| | 本工程（宿主） | BeatEmUp（内容来源） |
| :--- | :--- | :--- |
| Unity 版本 | `6000.3.10f1` | `6000.3.10f1`（相同） |
| 渲染管线 | Built-in RP | 装了 URP 17.3 但**没启用**（`ProjectSettings/GraphicsSettings.asset:40` 的 `m_CustomRenderPipeline: {fileID: 0}`），精灵实际走内置 `Sprites/Default` |
| 角色 | 3D 骨骼模型（SkinnedMesh + Avatar） | 逐帧像素 PNG（`PlayerSpriteSheet.png` 1024×512、106 张子图、PPU 50） |
| 相机 | 透视（`orthographic: 0`） | 正交 + `PixelPerfectCamera` |
| 世界轴 | X 横向、**Y 高度**（重力与地面探测）、Z 纵深 | X 横向、**Y 纵深车道**、跳跃高度是"贴图飞起来"再靠 `CapsuleCollider2D.offset.y` 把碰撞体拽回地面 |
| 时基 | 自建 60Hz 整型逻辑帧时钟，Animator 是被时钟奴役的从属 | Unity 的 `Update/FixedUpdate` + `Time.time` + 协程 + `Time.timeScale` 慢镜头 |
| 招式数据 | `Resources/GameData/Action.json` 一张表，窗口存绝对帧号 | 预制体内联的 `AttackData` + **画在 `.anim` 曲线里的命中帧** + 6 个动画事件 |
| 规模 | — | 82 个 .cs / 约 4900 行、41 条 .anim、36 个预制体、12 个场景、88 张 PNG、31 个 WAV |

管线与版本都不是障碍，**障碍在下面四条冲突**。

---

## 二、四条正面冲突

### 冲突 1：谁是真源（这一条决定了工作量）

BeatEmUp 的帧数据不在配置里，而在**动画资产**里：

- 17 条攻击 clip 各自用曲线关键帧驱动子物体 `HitBox` 的 `m_IsActive` 与 `m_Enabled`，并同时关键帧它的位置与缩放。例：`PlayerPunch1.anim` 的三个关键帧是 0→关、0.0667→开、0.1667→关，按 60fps 采样就是**第 4~9 帧有效**。这就是它的 startup / active / recovery，全部在 Animation 窗口里编辑，没有任何帧表。
- 招式时长靠 `Time.time - stateStartTime > GetAnimDuration()`，而 `GetAnimDuration` 读的是 `animator.GetCurrentAnimatorStateInfo(0).length`（`UnitActions.cs:301`）——向动画反查进度。
- 音效、特效、震屏、前冲、生成弹头是 `.anim` 上的 `m_Events`，六个函数名：`PlaySFX`(14 处) / `ShowEffect`(6) / `CamShake`(4) / `Footstep`(3) / `AddForce`(3) / `SpawnProjectile`(1)。

本工程的铁律恰好相反：**时长真源只有表里的 `durationFrames`，不许读 Animator 进度当逻辑真源**（`Assets/Scripts/AGENTS.md`），Animator 在 `ActionController.Awake` 里就被 `enabled = false`，只在时钟推进那一拍被 `anim.Update(LogicClock.Tick)` 步进。

⇒ 迁移方向是**单向**的：动画里所有时机信息搬进表，动画退回成纯表现。决策见 `../decision/0001-帧数据真源在表不在动画.md`。

### 冲突 2：竖轴的含义

本工程 Y=高度、Z=纵深；BeatEmUp Y=纵深车道、跳跃靠贴图偏移。移植的本质是**轴的改名**：纵深从 Y 变成 Z，"贴图飞起来"变成真正的 `transform.position.y`。

好处是本工程的 `CharacterObj.ProbeGround`（`CharacterObj.cs:237-256`，从脚底上方起投、探测距离不含半径、起点取移动前后较高那个）**本来就是 BeatEmUp 那套 collider-offset 补偿的正规版本**，所以 `col2D.offset` 那个 hack 不搬。纵深排序公式（`ObjectSorting.sortingOrder = y × -50`）保留，但按 Z 计算。

### 冲突 3：面向怎么落地

BeatEmUp 用 `localRotation = Euler(0,180,0)` 翻转贴图；本工程 `ActionController.cs:389` 写死 `transform.eulerAngles = new Vector3(0, command.inversed ? 270 : 90, 0)`。一张面向镜头的精灵四边形绕 Y 转 90° 会侧对镜头——所以那行代码在精灵角色上是错的。

⇒ 逻辑真源仍是 `inversed` 布尔（镜像判定 `GetKeyInput`、位移符号 `NatureMove` 本来就只用它），只有"落地成什么变换"这一步交给表现层：3D 模型走 Y 旋转，精灵走 `flipX`。

### 冲突 4：输入模型

| BeatEmUp | 本工程 |
| :--- | :--- |
| 新版 Input System，六个动作：八向移动（摇杆 Y = 纵深）、拳、脚、**格挡（长按）**、抓取、跳 | Legacy `Input.GetButton/GetAxis`，`KeyMap` 是小键盘 1..9 的 3×3 方向 + Punch/Kick，**竖轴是上段/下段** |
| 连招 = `attackList` 前缀匹配 + `comboResetTime` 0.55 秒 | 连招 = `cancelTag` / `beCancelledTag` 标签链 + `commands` 有序子序列 + `validInFrames` 回看窗 |
| 无输入缓冲概念（下一键整段动画里随时采样，动画结束才消费） | 有回看窗与取消窗口 |

本工程的连招表达力更强，所以方向是**用本工程的机制重写 BeatEmUp 的连招**（见第四节映射表）。竖轴语义要改：本期素材里没有蹲防也没有上段招式（`Player.controller` 的 31 个状态里没有高低之分），所以**竖轴改为纵深**，`KeyMap.Up/Duck` 保留但本期没有内容消费者。

---

## 三、框架缺口与要加的功能

本期（Step 1）只补下面六项；弹射物/场景物件入实体表、关卡流程与 UI 排到 Step 2。

| # | 缺口 | 加什么 | 为什么这样加 |
| :--- | :--- | :--- | :--- |
| A | 判定盒只能整体开/关，没有位移 | 表里给每个攻击盒一条轨迹（关键帧数组），`GameData.Load` 烘焙成 `Vector3[]`，`ActionController.Tick` 按整帧号写 `localPosition/localScale` | 读表期分配、运行期零分配，合红线；顺带闭环 `AttackHitBox.cs:11` 那句 `TODO: 攻击盒与动画解耦` |
| B | 没有帧事件 | `ActionInfo.cues[] = {int frame, CueKind, int bakedParam}`，求值复用窗口那套跨帧语义（`frame >= cue.frame && wasFrame < cue.frame`） | 不新增第三种判定；字符串参数在 `GameData.Load` 烘焙成索引。**cue 在逻辑 tick 里派发 ⇒ 音频/特效必须池化**（BeatEmUp 每次播放 `new GameObject + AudioSource`，照搬直接撞 0 B/帧） |
| C | 没有"动作在什么状态下才允许"的门禁 | 新增 `Methods/CancelConditionMethod.cs` 函数表，内置 `StateIs`（地面/空中）与 `NearbyUnitInAction`（倒地追击用） | 与已有三张表 `RootMotionMethod`/`ForceMoveMethod`/`PickActionMethod` 同构，不开新概念；`NearbyUnitInAction` 走常驻注册表，不许 `FindGameObjectsWithTag` |
| D | 跨单位位置绑定（抓取） | `CharacterObj.SetHoldConstraint(victim, offset)` / `ReleaseHold()`，在 `TickAll` 移动拍之后统一应用 | BeatEmUp 是**一次性传送、之后不跟随**（`PlayerGrabEnemy.cs:17` → `EnemyGrabbed.cs:28`），它自己的 README 就列为缺陷，移植要做对 |
| E | 没有长按语义 | 把 `Block` 当**方向键**登记（每 tick 都记、不吃 `NonDirectionInputCooldownFrames` 节流、不被换动作清理），长按 = `IsKeyHeld(Block)`，松手 = 该帧无记录 | `InputToCommand.cs:76-79` 自己承认没做 tap/hold 是偷懒；改动局限在采样与注册，判定层不动 |
| F | 命中相交的粒度是物理步而不是逻辑帧 | 删掉 `AttackHitBox` 的 `OnTriggerEnter/Exit`，在 `TickAll` 的 `TickBoxes` 之后、`DealWithAttacks` 之前插 `SampleOverlaps()`：每逻辑帧 `Physics.SyncTransforms()` + 按形状 `OverlapSphere/Capsule/BoxNonAlloc` 到 Awake 预分配的常驻缓冲 | ✅ 已由切片 1.3 结掉（原 `Doc/issue/hit-overlap-physics-step-granularity.md` 已删，顺带登记 `Doc/issue/hit-event-heap-alloc.md`）。角色根节点是直写 `transform.position`，而 `m_AutoSyncTransforms: 0`，所以必须先同步；`queriesHitTriggers` 默认为真，触发盒照样被查得到 |

另外顺带落地 `refactor.md` 里 **Phase 3**（伤害/霸体/粗筛）与 **Phase 4**（实体与对象池，Step 2）：伤害公式要的 `AttackHitBox.damageRate`（`AttackHitBox.cs:36`）与 `BeHitBox.meat`（`BeHitBox.cs:28`，MH 式部位肉质）**两个字段早就存在但从来没被消费**，只差 `GameMain.cs:221` 那行 `//造成伤害 //todo demo里就先不做了`。

---

## 四、BeatEmUp 状态 → 本工程动作表的映射

`Player.controller` 有 31 个状态、零参数、state→clip 一一 对应；代码用 `Animator.Play(name,0,0)` 直接点状态。

| BeatEmUp | 本工程的表达 |
| :--- | :--- |
| `PlayerIdle` 与 `PlayerMove`（两处输入分派 if-chain 完全重复，其架构文档 7.2 已列为待改） | 一条 `Idle` 动作 + `inputAcceptance.rate > 0` 就能走位（`MoveInputAcceptance` 已内建）。要让贴图切 Run，就让 `Idle ↔ Run` **互为自取消**：Run 的 `commands = [{[Forward], validInFrames=1}]`，Idle 的 `commands = [{[NoDirection], validInFrames=1}]`。零框架改动，且消灭那处重复 |
| `PlayerAttack` 前缀匹配连招（`attackList` 长度当进度、`comboResetTime` 从 Enter 起算、`continueComboOnHit` 未命中就清历史） | 逐招拆成独立 `ActionInfo`，用 `cancelTag`/`beCancelledTag` 标签链（拳拳拳 = P1→P2→P3 各自的窗口与优先级）；`comboResetTime` → `cancelWindowFrames`；`continueComboOnHit` → `AddTempBeCancelledTag`（已有） |
| `UnitKnockDown`（103 行手写抛物线，动画状态名当帧由 `yForce` 符号决定） | 三段拆成三条动作（`KnockdownUp`/`KnockdownDown`/`KnockdownGrounded`），轨迹走已有 `ForceMoveMethod` + `pushPower.moveDistance`（本来就是整型帧驱动），躺地时长进表（替代 `knockDownFloorTime`） |
| `UnitHit` / `UnitDefend` / `UnitStandUp` / `UnitDeath` | `hitStunFrames` + 受击 catalog 选招（`PickActionMethod` 里 Hurt/Beaten 两个 picker 已有，受力方向仍是 Forward/Backward） |
| `PlayerJump`/`PlayerLand`/`JumpAttackPunch`/`JumpAttackKick`（31/16/26/30 逻辑帧，**素材现成**） | 三段 + 空中攻击，走新增的 `StateIs` 门禁与 `CharacterObj.Launch` 起跳冲量。**空中必须用独立的 `jumpGravity`**：`weight = 4.5/逻辑帧` 等价 270 m/s²，拿来做跳跃会飞出十几米，而 Punch2 那 0.2 米、两三个逻辑帧的击退是用例钉住的行为，不能改 |
| `PlayerGrab*` / `PlayerThrowEnemy` / `EnemyGrabbed` | 上表 C + D |
| `UnitDefend` 的格挡结算（`UnitActions.cs:90` 是**全额免伤、无格挡硬直、无推开**，敌兵在判定里裸调 `Random.Range` 自动举盾） | 补 `blockStunFrames` / `blockPush` 两个表字段（与 `hitStunFrames`/`pushPower` 同构）；RNG 换成**种子化随机**，否则 30/60/120fps 一致性与回放、测试全部失效 |
| `ThrowKnife` + `KnifeProjectile`、武器耐久/掉落、可破坏物、球、`Surface` 脚步音效、波次、选关、HUD | **Step 2** |
| `PlayerDefendHit.anim`（只有 1 帧）与控制器里的 `DefendGetHit` 状态没有任何播放者 | 死资产，不搬 |

---

## 五、分期与验收

### Step 1：框架改造 + 玩家内核（灰盒场景内），每片一个 commit

| 片 | 内容 | 验收判据 |
| :--- | :--- | :--- |
| 1.1 | 面向抽象（冲突 3）+ HD-2D 世界约定：精灵贴到 3D 平面、纵深排序按 Z、透视相机按 PPU 50 标定 | 灰盒肉眼朝向正确、跳起来不穿背景；`Assets/Editor/ActionFrameClockTests.cs` 九个原用例全绿 |
| 1.2 | 轴改名 + 纵深车道：`NatureMove` 加 Z、竖轴改纵深、`GoStraight` 加可选 `param[3]`、同轨 pairwise 推挤、新建 `LevelBounds` | 新用例：`Inversed=true` 时纵深位移符号不变；13 条旧表项逐 tick 位移不变（原用例⑦须原样绿）；`NatureMove` 分区 0 B |
| 1.3 ✅ | 命中改逻辑帧采样（F）+ `BeHitBox` layer（阵营层与距离²粗筛都记为偏离，见第八节） | **先写用例再改实现**：命中帧恰好等于表里 `[4,5]` 被跨过的帧、走开当帧立刻打不着、自己的盒子不算靶子、离开采样层查不到、每帧 0.5 米穿过不漏拍；`SampleOverlaps` 分区稳态 0 B；issue 文件与索引行已删 |
| 1.4 ✅ | 导出工具 `Assets/Editor/AnimClipToActionTable.cs`（菜单 `Tools/ACT/导出 BeatEmUp 帧数据草稿`），把 41 条 clip 的窗口/盒子轨迹/cue 转成整数逻辑帧，产出 `beatemup-anim-dump.md` | 可疑值 0 条；命中帧集合按源工程的读法逐招对过（见第八节）；20 条编辑期用例全绿 |
| 1.5 | 盒子轨迹（A）+ 帧事件表（B）+ 池化的 cue 派发 | cue 触发路径 GcProbe 新分区 0 B/帧 |
| 1.6 | `CancelConditionMethod` 表（C）+ 长按（E）+ 种子化随机 | 用例：空中不接地面招；倒地追击只在目标处于 `KnockdownGrounded` 时成立；同种子同帧率逐 tick 一致 |
| 1.7 | 导入素材（连 `.meta`，保留 guid）+ 玩家整套招式表 | 逐招手感与 BeatEmUp 对拍；`GameMain.targetFrameRate` 30/60/120 三档行为一致 |
| 1.8 | 抓取约束 + 投掷（D） | 被举者**逐 tick 跟随**（不复现一次性传送）；"扔敌人砸敌人"在解约束后仍能命中第三方 |
| 1.9 | 伤害/HP/霸体/格挡结算 | 用例：霸体未破→掉血但不切 Hurt；`hp<=0`→Death；格挡免伤但有 blockstun 与推开 |

### Step 2：世界与流程

输入采样层换成新版 Input System（`AddInput` 仍是唯一逻辑入口，改动隔离在 `SampleRenderFrame`）、敌兵 AI（决策心跳 + 进攻排队 + FOV，全部走 `AddInput` 进来）、`WaveManager`、一个正式关卡 + `LevelBound` 镜头推进、HUD 与选关、武器/道具/弹射物/可破坏物/`Surface`、视差背景、白闪 shader、`EntityManager` 与对象池。

---

## 六、前置事项（不做完整件事会中途卡住）

1. **敌兵贴图在这份副本里是缺的**：`EnemyIdle/Punch/Walk.anim` 引用的纹理 guid `afc72b9957692184e9d5b329a78409f3` 在 BeatEmUp 的整个 `Assets` 下**没有 .meta**，也就是说当前敌兵没有美术。要么找回原始素材包，要么 Step 2 先用灰盒方块顶着。
2. **授权**：BeatEmUp 仓库里**没有任何 LICENSE 文件**，只有 `README.md` 的「致谢」写着素材与代码原始版权归 Osarion / MoolTools（作者 Mikel Fick）所有、该仓库仅为二次开发工作副本。把素材复制进本工程前，先确认手上的购买授权允许这种使用。
3. **导入方式**：必须连 `.meta` 一起复制以保留 guid，否则 `Player.controller`、`.anim`、预制体内部的引用会断。本工程**不需要**装 URP。
   已做（切片 1.4 的前置）：`Animation/`（41 条 clip）与 `Sprites/` 已连 `.meta` 落到 `Assets/Arts/BeatEmUp/`，
   两边 guid 零冲突，导入后逐字节与源片一致（Unity 没重序列化，所以曲线里的精灵引用一个没丢）。
   `Audio/`（9.5 MB）与 `Prefabs/` 还没导：前者等 1.5 的 cue 派发（不池化就不该引进来），后者只当文本读过规格。

---

## 七、风险

1. **真源迁移是单向门**：表一旦成为真源，改 `.anim` 就必须重跑导出工具。缓解：工具做成 `Tools/ACT` 菜单，并把"表里的窗口 vs clip 实际曲线"变成 EditMode 断言。
2. **1.3 判定根换血**是本期最大风险：必须先写用例再改实现，否则"看起来一样"的漏判会静默留在游戏里。
3. HD-2D 的镜头标定（PPU 50 / 透视 FOV / 车道远近缩放）决定像素会不会发虚，需要一次专门对拍，**结论有可能反过来要求镜头改回正交**。
4. BeatEmUp 的"下一键随时采样、动画结束才消费"与本工程的 `validInFrames` 回看窗**语义不完全等价**，对拍时可能发现手感偏移——那时应该改表，不该改代码。
5. 同屏 20+ 敌兵时 `SyncTransforms` + 每盒一次 overlap 的开销待实测（`GcProbe` 只量分配不量 CPU）。
6. BeatEmUp 的敌兵与玩家**共用同一个 AnimatorController**（`Enemy.prefab` 的 `m_Controller` GUID 与 `Player.controller` 相同，敌兵招式表里填的就是玩家状态名）⇒ 做外观/招式不同的 Boss 要复制控制器并同步状态名，而"所有角色共用一张动作表"的前提一旦动摇，`GameData.ActionsOfCatalog` 的倒排索引必须下移到 `ActionController`（`GameData.cs:64-66`）。

## 八、Step 1 实施记录（滚动更新）

### 切片 1.1 面向抽象 · 已实施 · 2026-10

`ActionController.cs` 里写死的 `transform.eulerAngles = (0, 270|90, 0)` 换成角色身上的 `FacingApply`（两种落地：`ModelYaw` / `SpriteScaleX`）。选 `localScale.x` 取反而不是 `SpriteRenderer.flipX`，是因为判定盒和将来的武器挂点都是子节点——flipX 只翻贴图不翻子树，出招的盒子会留在错误的一侧。**换动作那一拍的时机逐字未动**（仍在 `_wasFrame = CurrentFrame = startFrame` 之后、`aInfo.flip` 之前），所以 `flip` 语义不变。

拆出去的一条：原计划把"精灵贴到 3D 平面 + 按 Z 排序 + PPU 标定"也放在 1.1，但没有真实素材无法验收，挪进了 1.7。

### 切片 1.2 纵深车道 · 已实施 · 2026-10

**没有新增 KeyMap**——这是与本文件第三节原方案的一处有意偏离，原因是发现竖轴可以直接复用：竖轴采样本来产出 `KeyMap.Up`(8) / `KeyMap.Duck`(2)，而这两个值①本来就不参与 `GetKeyInput` 的正反手镜像（正是纵深需要的性质），②表里用它们的招式（TwinKick 的 `[2,3,6,101]`）因此照常可搓，语义变成"从近侧车道绕过来"。再加一对 `DepthFar/DepthNear` 只会让同一根轴有两套名字。

落地清单：`NatureMove` 的 z 项 = `(Up?1:Duck?-1:0) × moveSpeed × depthSpeedRate × MoveInputAcceptance × Tick + RootMotionMove.z`，横向那个 `Inversed` 符号不乘它；`GoStraight` 增加可选 `param[3]`（纵深米数，缺省 0），13 条老表项的逐帧位移逐项不变；`CharacterObj.SeparateLanes` 成为 `TickAll` 的第六拍（只动 z，完全重合时方向由数组顺序定死以保证确定性）；新建 `LevelBounds`，为 null 时不夹，所以老场景与编辑期用例的行为一项没变。

**验证**：`ActionFrameClockTests` 从 9 条加到 15 条全绿。其中两条是踩出来的，值得留给后来人：

- 只按竖轴测不出纵深——`BoxingMoveForward` 的 `autoTerminate` 是 true，没有匹配的 Forward 指令时第一拍就跳回 `BoxingStand`（acceptance 0），纵深只走一拍。用例必须连方向键一起按。
- 三段不能共用一个 rig：输入历史保留 72 个逻辑帧，中途换正反手后上一段按过的键还会参与指令匹配，把下一段的期望值污染掉（实测少走两拍）。所以拆成三个独立用例。

Play Mode 稳态（约 30 秒、三个窗口）八个分区全部 `avg=0 max=0`，含新增的 `LaneSeparate`（n=172），自检 1056B 证明计数器是活的。启动那两个窗口里的非零都是常驻 List 的头几次扩容（`max=1056` 正好是 64 个 16 字节的 `KeyInputRecord` 加对象头）。

### 切片 1.3 命中按逻辑帧采样 · 已实施 · 2026-10

`AttackHitBox` 的 `OnTriggerEnter/OnTriggerExit` 删掉，换成 `TickAll` 里新的一拍：`CharacterObj.SampleOverlaps(all, buffer)` 每逻辑帧先 `Physics.SyncTransforms()` 一次（角色根节点是直写 `transform.position` 的，而 `m_AutoSyncTransforms: 0`，不同步就查到的是上一个物理步的旧位置），清空每个人的接触列表，再让**开启中的攻击盒**按形状分派 `OverlapSphere/Box/CapsuleNonAlloc`。`Tick()`（盒子开关）一字未动。

落在采样拍而不是别处的理由：盒子刚被刷新过，查到的就是"这一拍开着的那些盒"；而它读的位置仍来自上一拍的 `Move()`，这个滞后和以前物理回调读到的一样——换时基不该顺手改掉命中的早晚，那是另一件事。

三处与原方案的有意偏离：

- **没做距离²粗筛**。PhysX 的查询自带 broadphase，自己再算一遍 sqrt-free 的圆-圆测试只是把引擎已经做的事重做一遍；先量了再说（`GcProbe` 新增 `SampleOverlaps` 分区，稳态 `avg=0 max=0`，同屏 3 个角色每帧 4 次查询）。
- **没加 `CampPlayer`/`CampEnemy` 两个层**。阵营在代码里还不存在（`GameMain` 是硬写的"玩家×每个敌兵"双向结算），为将来的概念先占两个层位属于投机；改成加一个 `BeHitBox` 层（`ProjectSettings/TagManager.asset` 第 8 个，`Player.prefab` 里两个受击盒已归位），先把"地面/道具/别人的攻击盒不许挤占那 64 格缓冲区"这条真正需要的过滤钉住。等 1.9 有阵营时再把掩码拆成玩家/敌兵两档，届时用例里那条"离开采样层就查不到"要跟着改。
- **缓冲区溢出只报一次日志**（`AttackHitBox._bufferOverflowLogged`），不静默丢也不扩容。

一个原本没预料到的坑，值得留给后面的切片：**EditMode 下 `Object.Instantiate` 不触发 `Awake`**（Unity 只在 Play Mode 调回调）。盒子的绑定（认主人、烘焙 tag、认领碰撞体、建接触列表）原来全长在 `Awake` 里，于是采样用例一跑就是 `NullReferenceException`。把它们抽成 `CharacterObj.BindBoxes()`（幂等，`Awake` 调它，用例也调它）。以前 15 条用例没暴露这件事，是因为它们只断言掩码和帧号，从来不碰盒子对象。

顺带记下的两件事：`BeHitBox_Body`/`BeHitBox_Parry` 上那两个非 kinematic、关重力的 Rigidbody 现在**没有任何人需要了**（触发回调已删，直接查询不要 Rigidbody），但它是既有数据、删了会改变物理求值，本期只登记不动；命中那一拍的堆分配另见 `Doc/issue/hit-event-heap-alloc.md`。

**验证**：`ActionFrameClockTests` 从 15 条加到 20 条全绿，新增的第 11 区五条用例是**旧实现根本写不出来**的——EditMode 不跑物理模拟，`OnTriggerEnter` 一次都不会来。用例靠"把靶子的受击盒中心压到拳盒中心、再沿 z 退开 dz"自己算期望值（胶囊轴正好穿过球心，于是重叠的充要条件就是 `|dz| ≤ 0.6`），不去回头问实现：① 命中帧恰好等于表里窗口 `[4,5]` 被跨过的帧；② 靶子走开的那一拍立刻打不着、走回来立刻打得着（清空重填）；③ 拳盒压在自己的受击盒上不算靶子（主人过滤）；④ 离开 `BeHitBox` 层就查不到（顺带验了 prefab 的层真的归位了）；⑤ 每帧 0.5 米（合 30 米/秒）沿纵深穿过拳头，重叠的三帧一帧不漏。

Play Mode 稳态九个分区全部 `avg=0 max=0`（含新的 `SampleOverlaps`，n=231），自检 1056B。端到端也验了一遍真的打到了：编辑期 eval 把敌兵摆到拳心、给玩家塞一记 Punch1，读回 `hitRec=1 enemyAct=HurtFromForward enemyHitStop=True`，击退正好走了表里的 0.1 米。

`Doc/issue/hit-overlap-physics-step-granularity.md` 随本切片删除。

### 切片 1.4 `.anim` → 表 的导出工具 · 已实施 · 2026-10

工具在 `Assets/Editor/AnimClipToActionTable.cs`，菜单 `Tools → ACT → 导出 BeatEmUp 帧数据草稿（.anim → 表）`，产出 `Doc/plan/beatemup-anim-dump.md`（335 行，41 条片子逐条：时长、判定盒窗口、精灵可见窗口、盒子位置/缩放轨迹、cue、可疑值）。**输出是草稿不是成品表**——伤害/取消标签/指令/受击阶段在源 clip 里根本没有，那些是玩法决定，由 1.7 填。

换算公式（写在工具头部注释和草稿开头，因为 1.7 填表时没人能独立验它）：本工程第 n 拍覆盖 [(n-1)/60, n/60)，clip 是 60fps 的 0-based ⇒ clip 第 f 帧 = 本工程第 f+1 拍；表里的窗口是「跨过就算数」（生效 = min..max+1）⇒ **min = 抬起帧 + 1，max = 落下帧 − 1**。这样生效集合正好等于源片子里盒子开着的那几帧。

这条公式不是推出来的，是对着源工程的读法对出来的：`UnitActions.cs:344` 在 MonoBehaviour 的 Update 里读 `hitBox.gameObject.activeSelf`，而 Animator 在同一帧更早的阶段求值曲线，所以 clip 第 f 帧恰好在第 f 个渲染帧被读到。逐招核过：Punch1 活跃帧 4..9 → 表 [5,9]；Kick1 2..11 → [3,11]；Kick3 4..12 → [5,12]；GroundPunch 10..18 → [11,18]，四个都对得上"生效帧数相等且首帧对齐"。

三处值得记的做法：

- **读的是 .anim 的文本而不是 AnimationUtility**。盒子的位置/缩放是 typed 曲线（`m_PositionCurves` / `m_ScaleCurves`），Unity 6 的 AnimationUtility 只把浮点曲线暴露在 `GetCurveBindings` 里，transform 那几组没有公开读法（`GetPositionCurves` / `GetPositionCurve` 都不存在）；而 `m_EditorCurves` 里还有一份编辑器副本，混着读会把每条曲线数两遍。按文本一次覆盖四种块。
- **行尾必须规范化**：`.anim` 是 LF 而 `.prefab` 是 CRLF，按整行匹配的查找在 CRLF 上全都命中不了，失败还只是安静地留空。
- **`{x: , y: , z: }` 只在本对花括号内取数**。早期写法是"跳过 `key: {` 之后固定几个字符"，切歪一个字符就把 `m_LocalScale` 的 x 当成 `m_LocalPosition` 的 x——数值看着正常，谁也不会发现。同类问题还有 SpriteRenderer 块里第一个 `guid:` 是内置材质（解析成 unity_builtin_extra），必须从 `m_Sprite:` 那行往后找。

顺带把源工程那只唯一 HitBox 的规格算成了本工程的 3D 盒子：局部位置 (0.57, 0.48, 0)、`SpriteRenderer.size` (0.64, 0.64) × localScale.x 1.2342 ⇒ **size (0.7899, 0.64, 0.5)**，厚度 0.5 米来自源工程 `targetInZRange(0.5)`。这条正是 1.5「盒子轨迹」的输入。

分类结果：41 条里 15 条带判定盒窗口、20 条带 cue、**可疑值 0 条**。两条倒地片子（`PlayerKnockDownGrounded`/`_Up`）的 m_IsActive 全程是 0——源片子里这条动作本来就打不着人，工具把它记成"备注"而不是"可疑"，免得把真实玩法当成数据缺失去追。敌兵三条片子（EnemyIdle/Punch/Walk）没有盒子曲线：它们的攻击靠 `AttackData.inflictor` 生成物结算，归 Step 2 处理。

同时修掉了三处"动画全是 30 帧 authored"的旧断言（`LogicClock.cs:10`、`ActionDurationDumper` 的警告文案、`refactor.md` 第 165 行）：BeatEmUp 的 clip 是 60 帧 authored，窗口可以落在任意帧。仍成立的是"两种都是 1/60 秒的整数倍，所以帧数没有取整误差"。

**验证**：编辑期用例 20/20 全绿（本切片没动运行期代码）；工具产出的窗口逐招与曲线原文核过（上面四例）。


### 顺带：恢复闸门

回归时撞上 `RepeatedHitRefreshesHitstopWithoutChangingAction` 在 HEAD 上就是红的（做过 A/B：摘掉移植改动重跑，同样红，与本次工作无关）。根因是用例从第 449 行起把被冻住的拍也算进了前进数，四处期望常数各多一拍；实现本身自洽、且符合 `refactor.md` 写的"取较大者只是把剩余硬重置满"，所以只改测试常数。九个用例恢复全绿，后面顿挫相关的切片（1.3、1.5、1.9）才有闸门可用。

---

相关文档：`refactor.md`（Phase 3/4 随本期落地）、`../decision/0001-帧数据真源在表不在动画.md`、`../decision/0002-HD-2D世界约定.md`、`beatemup-anim-dump.md`（切片 1.4 由工具生成的帧数据草稿）
