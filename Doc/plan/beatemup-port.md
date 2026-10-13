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
| 相机 | 正交（`orthographic: 1`、size 2.5 —— 2026-10-13 从透视改的，理由见 `../decision/0002` 的"修订"） | 正交 + `PixelPerfectCamera` |
| 世界轴 | X 横向、**Y 高度**（重力与地面探测）、Z 纵深 | X 横向、**Y 纵深车道**、跳跃高度是"贴图飞起来"再靠 `CapsuleCollider2D.offset.y` 把碰撞体拽回地面 |
| 时基 | 自建 60Hz 整型逻辑帧时钟，Animator 是被时钟奴役的从属 | Unity 的 `Update/FixedUpdate` + `Time.time` + 协程 + `Time.timeScale` 慢镜头 |
| 招式数据 | 一张表（2026-10 起真源是 `ActionTable.asset`，见 `action-editor.md`），窗口存绝对帧号 | 预制体内联的 `AttackData` + **画在 `.anim` 曲线里的命中帧** + 6 个动画事件 |
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
| F | 命中相交的粒度是物理步而不是逻辑帧 | 删掉 `AttackHitBox.cs:81-98` 的 `OnTriggerEnter/Exit`，在 `TickAll` 的 `TickBoxes` 之后、`DealWithAttacks` 之前插 `SampleOverlaps()`：每逻辑帧 `Physics.SyncTransforms()` + 按形状 `OverlapSphere/Capsule/BoxNonAlloc` 到 Awake 预分配的常驻缓冲 | 结掉 `Doc/issue/hit-overlap-physics-step-granularity.md`。角色根节点是直写 `transform.position`，而 `m_AutoSyncTransforms: 0`，所以必须先同步；`queriesHitTriggers` 默认为真，触发盒照样被查得到 |

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
| 1.3 | 命中改逻辑帧采样（F）+ 阵营 layer + 距离²粗筛 | **先写用例再改实现**：窄窗口 `[4,5]` 恰好命中一次且不重复；Profiler 量 `SyncTransforms` + 每盒一次 overlap 的 CPU；删掉 issue 文件与索引行 |
| 1.4 | 导出工具 `Assets/Editor/Tools/ACT/AnimClipToActionTable.cs`，把 17 条攻击 clip 的窗口/盒子轨迹/cue 转成表 | 工具报告零可疑值；逐招与 BeatEmUp 对拍命中帧集合与画面位置；`Tools/ACT/打印动作时长` 全 ✓ |
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

---

## 七、风险

1. **真源迁移是单向门**：表一旦成为真源，改 `.anim` 就必须重跑导出工具。缓解：工具做成 `Tools/ACT` 菜单，并把"表里的窗口 vs clip 实际曲线"变成 EditMode 断言。
2. **1.3 判定根换血**是本期最大风险：必须先写用例再改实现，否则"看起来一样"的漏判会静默留在游戏里。
3. HD-2D 的镜头标定（PPU 50 / 透视 FOV / 车道远近缩放）决定像素会不会发虚。**已定：投影改正交**，同轨的可读性与 `PixelPerfectCamera` 两个理由一起压过了透视的氛围感，推导与三个数字见 `../decision/0002` 的"修订"一节；size 仍要等 1.7 的像素对拍按参考分辨率重算。
4. BeatEmUp 的"下一键随时采样、动画结束才消费"与本工程的 `validInFrames` 回看窗**语义不完全等价**，对拍时可能发现手感偏移——那时应该改表，不该改代码。
5. 同屏 20+ 敌兵时 `SyncTransforms` + 每盒一次 overlap 的开销待实测（`GcProbe` 只量分配不量 CPU）。
6. BeatEmUp 的敌兵与玩家**共用同一个 AnimatorController**（`Enemy.prefab` 的 `m_Controller` GUID 与 `Player.controller` 相同，敌兵招式表里填的就是玩家状态名）⇒ 做外观/招式不同的 Boss 要复制控制器并同步状态名，而"所有角色共用一张动作表"的前提一旦动摇，`GameData.ActionsOfCatalog` 的倒排索引必须下移到 `ActionController`（`GameData.cs:64-66`）。

## 八、Step 1 实施记录（滚动更新）

### 切片 1.1 面向抽象 · 已实施 · 2026-10

`ActionController.cs` 里写死的 `transform.eulerAngles = (0, 270|90, 0)` 换成角色身上的 `FacingApply`（两种落地：`ModelYaw` / `SpriteScaleX`）。选 `localScale.x` 取反而不是 `SpriteRenderer.flipX`，是因为判定盒和将来的武器挂点都是子节点——flipX 只翻贴图不翻子树，出招的盒子会留在错误的一侧。**换动作那一拍的时机逐字未动**（仍在 `_wasFrame = CurrentFrame = startFrame` 之后、`aInfo.flip` 之前），所以 `flip` 语义不变。

拆出去的一条：原计划把"精灵贴到 3D 平面 + 按 Z 排序 + PPU 标定"也放在 1.1，但没有真实素材无法验收，挪进了 1.7。

### 切片 1.2 纵深车道 · 已实施 · 2026-10

**没有新增 KeyMap**——这是与本文件第三节原方案的一处有意偏离，原因是发现竖轴可以直接复用：竖轴采样本来产出 `KeyMap.Up`(8) / `KeyMap.Duck`(2)，而这两个值①本来就不参与 `GetKeyInput` 的正反手镜像（正是纵深需要的性质），②表里用它们的招式（TwinKick 的 `[2,3,6,101]`）因此照常可搓，语义变成"从近侧车道绕过来"。再加一对 `DepthFar/DepthNear` 只会让同一根轴有两套名字。

落地清单：`NatureMove` 的 z 项 = `(Up?1:Duck?-1:0) × moveSpeed × depthSpeedRate × MoveInputAcceptance × Tick + RootMotionMove.z`，横向那个 `Inversed` 符号不乘它；`GoStraight` 增加可选 `param[3]`（纵深米数，缺省 0），13 条老表项的逐帧位移逐项不变；`CharacterObj.SeparateLanes` 成为 `TickAll` 的第六拍（只动 z，完全重合时方向由数组顺序定死以保证确定性；这一拍 2026-10-13 起摘掉了，见切片 1.2b）；新建 `LevelBounds`，为 null 时不夹，所以老场景与编辑期用例的行为一项没变。

**验证**：`ActionFrameClockTests` 从 9 条加到 15 条全绿。其中两条是踩出来的，值得留给后来人：

- 只按竖轴测不出纵深——`BoxingMoveForward` 的 `autoTerminate` 是 true，没有匹配的 Forward 指令时第一拍就跳回 `BoxingStand`（acceptance 0），纵深只走一拍。用例必须连方向键一起按。**（已消解：那条补丁遮住的其实是"表里没有人消费竖轴"，见下面的切片 1.2b）**
- 三段不能共用一个 rig：输入历史保留 72 个逻辑帧，中途换正反手后上一段按过的键还会参与指令匹配，把下一段的期望值污染掉（实测少走两拍）。所以拆成三个独立用例。

Play Mode 稳态（约 30 秒、三个窗口）八个分区全部 `avg=0 max=0`，含新增的 `LaneSeparate`（n=172），自检 1056B 证明计数器是活的。启动那两个窗口里的非零都是常驻 List 的头几次扩容（`max=1056` 正好是 64 个 16 字节的 `KeyInputRecord` 加对象头）。

### 切片 1.2b 八向移动：竖轴终于有消费者 · 已实施 · 2026-10

1.2 只接通了管道（采样→意图→z 位移→同轨推挤→边界），**表里没有任何动作被 `[8]`/`[2]` 触发**，而 `CalculateInputAcceptance` 只对配了 `inputAcceptance` 的动作给倍率——`BoxingStand` 是空数组，倍率 0。于是站在原地按 W/S，纵深位移被乘成 0：功能上"上下移动"根本不存在，斜向同理。这一片补的就是那最后一格，仍然零新素材、逻辑层只动 `NatureMove` 一个函数。

落地清单：

- **表**：新增 `BoxingMoveFar`（`commands [[8]]`、`animKey BoxingForward`）和 `BoxingMoveNear`（`commands [[2]]`、`animKey BoxingBackward`），其余字段逐条照抄 `BoxingMoveForward`（含 `autoTerminate` ⇒ 松开所有方向键那一拍自动回 `BoxingStand`），两行的 `priority` 填 **-1**。`BoxingMoveBackward.inputAcceptance.rate` 0.7 → 1（后退不再减速，八向等速不能留例外）。
- **为什么是负 priority，而不是把 `[8]`/`[2]` 并进横向两行**：按住两轴时 `InputToCommand` 会**同时**登记单轴记录（6/4/8/2）和斜键记录（9/3/7/1），所以"只按 ↑"和"↑+→"在 `keySequence` 里区分不开，平手只能靠打分定。而把竖轴塞进横向行，就得给 `BoxingMoveBackward` 抬 `action.priority`，那会扩散到 `TwinKick`(1)/`TwinKickAutoCombo`(2)/`Hurt`(10) 那条竞争序列里去。仲裁规则本身见 `Assets/Scripts/AGENTS.md`，两个已知后果见 `../issue/walk-action-swallows-attack-input.md`、`../issue/turn-back-is-dead-code.md`。
- **归一化**：`NatureMove` 先把无量纲的意图 `(±1, ±1 × depthSpeedRate)` 用 `Vector2.ClampMagnitude(…, 1)` 夹一遍，**再**乘 `moveSpeed × MoveInputAcceptance × Tick`。纯横 1.0、纯纵 0.8（透视压缩的语义原样保留）、斜向从 √1.64=1.28 夹回 1.0。夹在乘之后长度只剩 0.066，永远够不到 1，归一化会静默失效；`RootMotionMove` 留在夹之外。
- **镜头**：`GameMain.LateUpdate` 不再让相机跟 z（跟着就等于原地不动），并把投影从透视换成**正交**（size 2.5、pitch 25°、base.y 2.75）。理由和这三个数的推导在 `../decision/0002` 的"修订"一节，这里只留一句结论：透视下"1 米车道位移"的屏幕位移只有 sin(10°)=0.174，而且远车道还会缩小，眼睛读不出"我们同不同轨"；正交下这个位移对每条车道都相同。不跟 z 是静态丢弃、不是插值，不吃渲染帧率。
- **摘掉的一拍**：`CharacterObj.SeparateLanes`（同轨推挤）从 `TickAll` 的顺序里去掉了。原因是敌兵今天还不会自己走位（prefab 上 `controller: 0`），玩家一走近就把人蹭走，观感像推土机。函数、`laneRadius` 和用例 `SeparateLanesPushesAlongZOnlyAndIsDeterministic` 全部留着，接回来是一行调用——接的时候要重新定"谁让路"，今天是两人各推一半、没有重量差。
- **边界**：`LevelBounds` 第一次真的进了场景（`Assets/Scenes/GameMain.unity`，手写的 GameObject），`min = (-20, -1.5)`、`max = (20, 1.5)`，即 3 米 ≈ 5 条车道（车道宽 = 两个 `laneRadius` = 0.6 米）。正交的可见纵深带 ≈ 2×size / sin(pitch) = 11.8 米，比车道带宽得多，**所以这个范围是玩法决定的，不再受镜头约束**——透视那套"近侧只剩 1.56 米余量"的推导随投影一起作废。

**验证状态**：`ActionFrameClockTests` 15 → 21 个用例。三条纵深用例撤掉了"必须连横向键一起按"的补丁、改成只按竖轴并新增"横向不许漂"的断言；新增斜向等速（期望值用 `Vector2.ClampMagnitude` 现算，不写死 0.78）、斜向落到哪条横向动作（↑+→ 和 ↑+← 各一条）、竖轴从站立进纵深动作（↑ 和 ↓ 各一条，因为 72 帧的输入历史会让上一段按过的键污染下一段）、后退不再减速。Play 模式已确认编译通过、八向都能走（2026-10-13）；**但这 21 条 EditMode 用例还没有跑绿的记录**——改动做完时 Unity 编辑器正开着工程，不能起第二个实例抢 project lock，Test Runner 里跑完再回来改这句。正交那三个数（size / pitch / base.y）同样要 Play 模式眼校，判据是"斜穿整个车道带，画面里大约挪四分之一屏高，角色在 z 的两端都不出画"。

### 顺带：恢复闸门

回归时撞上 `RepeatedHitRefreshesHitstopWithoutChangingAction` 在 HEAD 上就是红的（做过 A/B：摘掉移植改动重跑，同样红，与本次工作无关）。根因是用例从第 449 行起把被冻住的拍也算进了前进数，四处期望常数各多一拍；实现本身自洽、且符合 `refactor.md` 写的"取较大者只是把剩余硬重置满"，所以只改测试常数。九个用例恢复全绿，后面顿挫相关的切片（1.3、1.5、1.9）才有闸门可用。

---

相关文档：`refactor.md`（Phase 3/4 随本期落地）、`../decision/0001-帧数据真源在表不在动画.md`、`../decision/0002-HD-2D世界约定.md`
