# ACT 动作系统十项妥协工业级重构规划方案

> 状态：Phase 1、2 已实施 · 2026-10（Phase 3~4 仍为规划中）
>
> Phase 3（伤害/霸体/粗筛）与 Phase 4（实体与对象池）随 [BeatEmUp 移植方案](beatemup-port.md) 落地。该方案同时引入"纵深车道"这条新轴——它不在下面十项妥协之内，是移植带来的新增项。

本文档针对原工程中存在的十项工程与架构妥协，制定渐进式、低风险、面向商用级动作游戏（如《鬼泣》、《战神》、《街霸》）的工业级重构方案。

---

## 一、 重构设计原则

1. **不破坏原有动作配置数据**：原有 `GameData/Action.json` 保持向后兼容，所有性能优化（预烘焙、倒排索引、位掩码）均在数据加载初始化期完成。
2. **遵守 0 GC 性能红线**：所有每帧执行的热点循环（Tick、Move、Collision、CancelMatch）绝不允许产生堆内存分配（0 B/Frame）。
3. **分层清晰（逻辑 vs 表现）**：逻辑层负责输入、判定、帧推进、物理位移；表现层（Animator、音效、粒子）作为观察者仅负责状态插值与呈现。

---

## 二、 阶段规划与重构方案详情

我们将十个妥协按系统耦合度由低到高划分为四个推进阶段：

```
[Phase 1: 内存与数据热点 0 GC 治理] (妥协 6, 7, 8, 9)
                  ↓
[Phase 2: 定点逻辑时钟与物理/地形解耦] (妥协 1, 2, 4)
                  ↓
[Phase 3: 战斗打击、数值与霸体管线健全] (妥协 5, 10)
                  ↓
[Phase 4: 实体生命周期与对象池架构] (妥协 3)
```

---

### Phase 1：内存与数据热点 0 GC 治理（低侵入、收益最高）

本阶段目标：消除全部热点路径（每帧执行代码）中的临时对象分配与字符串重复计算。

#### 1. 妥协 6：RootMotion 字符串重复解析重构 (`RootMotionMethod.cs`)
* **现状**：每帧位移计算时调用 `float.Parse(param[0])`，造成 CPU 字符串解析与临时开销。
* **重构方案**：
  - 定义结构体 `struct BakedRootMotionParam { public float totalDis; public float startPec; public float endPec; }`；
  - 在 `ActionInfo` 加载完成时烘焙（Bake），方法执行时直接读取数值字段，零解析开销。

#### 2. 妥协 7：移动意图属性临时数组分配重构 (`CharacterObj.cs`)
* **现状**：`WishToMoveForward` 属性每次被读取均 `new KeyMap[] { KeyMap.Forward }`。
* **重构方案**：
  - 声明 `static readonly ActionCommand CmdForward = new ActionCommand { keySequence = new[] { KeyMap.Forward } };` 静态复用；
  - 或在 `InputToCommand` 中增加专有极速方法 `public bool HasDirectionInput(KeyMap key)`，直接位比对。

#### 3. 妥协 8：受击动作全量扫描与临时列表重构 (`ActionController.cs`)
* **现状**：`ChangeByCatalog` 每次受击均 `new List<ActionInfo>()` 并线性遍历 `AllActions`。
* **重构方案**：
  - 在 `CharacterObj` / `GameData` 初始化期构建分类倒排索引：`Dictionary<string, ActionInfo[]> _catalogIndex`；
  - 受击触发时通过字典 $O(1)$ 索引获取只读数组，杜绝临时列表创建。

#### 4. 妥协 9：碰撞受击标签临时分配重构 (`CharacterObj.cs`)
* **现状**：判定盒相交时每帧 `new List<string>()` 收集 `activeBoxTag` 并线性 `Contains` 比对。
* **重构方案**：
  - 为碰撞标签建立 32 位掩码（`int BoxTagMask`）；
  - 激活时按位或 `activeMask |= info.TagMask`，相交判定时仅执行单条位与指令 `(hitMask & beHitMask) != 0`，内存分配从几百字节降为 0。

#### Phase 1 实施记录（2026-10）

四项都按上面的方案落地了，另外顺带修了方案里没写的一处每帧分配。实际改动：

| 妥协 | 落点 |
| :--- | :--- |
| 6 RootMotion | `RootMotionMethod.cs` 改成"每个方法一条注册项：`Bake`（读表期把 `string[]` 翻译成数值）+ `Eval`（每帧按百分比给偏移）"，数值存进 `ActionInfo.rootMotionParam`（`[NonSerialized]`，`GameData.Load` 里赋值）；`ActionController` 换动作时查一次表缓存求值器和数值，每帧两次调用不再有解析和字典查找 |
| 7 移动意图 | `InputToCommand.IsKeyHeld(KeyMap)`：只看当前这一帧的输入记录（倒序扫到窗口外就收工），保留正反手镜像和 `_newInputs` 兜底，与 `ActionOccur` 单键判定语义一致；`CharacterObj` 的两个 Wish 属性改用它，不再 `new KeyMap[]` |
| 8 受击动作扫描 | `GameData` 加载期建 `_catalogIndex`（`Dictionary<string, ActionInfo[]>`），查询走 `ActionsOfCatalog`，未命中返回静态空数组；`PickActionMethod` 的候选参数由 `List<ActionInfo>` 改成 `ActionInfo[]` |
| 9 碰撞标签 | 新增 `BoxTagRegistry` 把标签名单调分配成 32 位掩码里的一位；`Attack/BeHitBoxTurnOnInfo` 加 `[NonSerialized] tagMask`（`GameData.Load` 烘焙），盒子组件的 `TagMask` 由 `CharacterObj.Awake` 顺带烘焙；`ActionController` 的两个 `List<string>` 激活标签换成 `ActiveAttackBoxMask` / `ActiveBeHitBoxMask` |
| 顺带 | `InputToCommand.cs` 每帧 `new bool[4]` 的 `dirDown` 展开成 4 个局部变量 |

方案里有两处估计需要订正，以免后来的人按错的预期去找收益：

- **妥协 9 的真实 0 GC 收益只有一处**：`CharacterObj.CanAttackTargetNow` 里那个每帧每敌人一次的 `new List<string>()`。其余的 `List<string>.Contains` 和三层字符串比对是**不分配的纯 CPU 开销**，掩码化属于 CPU 优化，不是分配优化。
- 妥协 6 的 `float.Parse` 同样不产生分配（string 早已存在），它省的是 CPU 和一次区域文化敏感的扫描。
- `foreach` 遍历结构体数组时**不允许**改元素的成员（CS1654），所以烘焙 RootMotion 数值必须先拷一份局部变量；改阶段数组的元素则用带索引的 `for`（数组本身是引用类型，改元素就改到了共享的那份表）。

已知未处理项：调试用的输入面板每帧重建字符串（`GameMain.cs:67` → `InputToCommand.InputText()`），是 Phase 1 之后最大的每帧分配源，属表现层，登记在 `Doc/issue/ui-inputtext-per-frame-alloc.md`。

#### Phase 1 验收实测

Unity 2023.2.0f1 编辑器播放 `Assets/Scenes/GameMain.unity`（玩家 + 2 个敌人同屏，连击、受击、转身、前后移动），
`GcProbe` 每 300 帧汇总一次。**稳态结论：六个分区 avg=0、max=0**，最后一个窗口分别是
`ActionControllerUpdate n=900`、`CalculateBoxInfo n=900`、`ChangeByCatalog n=0`（该窗口没有受击发生）、
`CanAttackTargetNow n=1200`、`NatureMove n=900`、`InputUpdate n=900`，全部 0 Byte。
报告里的"自检 1056B"是新分配一块 1024 字节数组加对象头的量，它是判定"这些 0 是真 0"的对照量（详见 `Doc/bugfix/gc-probe-false-zero.md`）。

判据要按**稳态**看，进播放的头两个 300 帧窗口不是 0，但都不是每帧分配：

| 观测到的非零 | 量 | 性质 |
| :--- | :--- | :--- |
| `InputUpdate` max=4128 / 8224 | 启动期 | `_input` / `_newInputs` 这些常驻 List 第一次扩容（容量按倍增，涨到位之后就不再分配） |
| `ActionControllerUpdate` / `CalculateBoxInfo` max=512 / 416 / 160 | 启动期 | `_preorderActions`、`ActiveAttackBoxInfo` 等常驻 List 的首次扩容 |
| `ChangeByCatalog` avg=max=106，`n=1` | 全会话第一次按分类选动作 | `PickActionMethod` 的静态构造（字典 + 两个委托对象）在这第一次才被摸到；之后 `n=1`、`n=2` 的窗口里 max 都是 0 |
| `CanAttackTargetNow` max=48，`n=1200` avg=0 | 第一次命中那一帧 | 一次性暖机，1200 次调用里只有 1 次 |

---

### Phase 2：定点逻辑时钟与物理/地形解耦（核心稳定性）

本阶段目标：彻底解除帧率对跳跃、动作取消时序、受击掉落手感的影响。

#### 5. 妥协 1：物理重力与抛物线拟合重构 (`CharacterObj.cs`)
* **现状**：使用 `Update` 中的 `weight * delta` 拟合下落速度，帧率波动导致跳跃轨迹与手感变异。
* **重构方案**：
  - 将角色物理更新迁移至固定逻辑时钟（`FixedTick`，60Hz，步长 0.01667s）；
  - 重力公式严格采用离散积分：
    $$\begin{aligned}
    v_{y}^{(t+1)} &= v_{y}^{(t)} - g \\
    y^{(t+1)} &= y^{(t)} + v_{y}^{(t+1)} \cdot \Delta t_{\text{fixed}}
    \end{aligned}$$
  - `Update` 仅使用 `Vector3.Lerp` 对 Transform 进行表现层平滑插值，实现 144Hz 超高刷与 30Hz 弱机手感绝对一致。

#### 6. 妥协 2：动画状态机（Animator）过度依赖与反向猜进度重构 (`ActionController.cs`)
* **现状**：依赖 `Animator.GetCurrentAnimatorStateInfo(0).normalizedTime`，融合时反猜下一个动画并强行 `Clamp01`。
* **重构方案**：
  - 确立“**逻辑推进帧数，逻辑驱动动画**”标准模式；
  - 控制器内部维护独立的定点逻辑计时器：`CurrentFrame`（整数）与 `DurationFrames`；
  - 每次切换动作由逻辑层直接向表现层发送 `PlayAnimation(name, fadeDuration)` 指令；
  - 取消窗口与判定盒完全由自主整型逻辑帧判定，彻底与 Animator 的异步状态解耦。

#### 7. 妥协 4：地形系统与垂直空间检测重构 (`GameMain.cs`)
* **现状**：仅以 `position.y < 0` 判定着地，无斜坡、台阶和空中状态分立。
* **重构方案**：
  - 引入地面探测器（Ground Probe）：利用 `Physics.SphereCastNonAlloc` 向下发射短射线；
  - 显式定义角色运动状态机枚举：
    - `Grounded`（在地面：支持自然奔跑、地面出招）
    - `Airborne`（滞空：开启重力加速度积分、仅允许空招与下落动作）
    - `Landing`（着地缓冲：播放微小落地后摇或无缝 Cancel 翻滚）。

#### Phase 2 实施记录（2026-10）

分四步落地：① 定点时钟骨架 + 时长单位统一 ② 地面探测与 Grounded/Airborne ③ 整型动作帧时钟 + 表窗口迁移 + Animator 变从属 ④ 本记录。实际改动：

| 妥协 | 落点 |
| :--- | :--- |
| 1 物理 | 新增 `LogicClock.cs`：`Tick = 1/60`、逻辑帧号、`TicksDue(ref accumulator, deltaTime)`（渲染帧到逻辑帧的换算全工程只有这一处）。`GameMain.Update` 每渲染帧只读一次 `Time.deltaTime`，然后按固定顺序显式调五家的 `Tick()`（原来的五个 `Update()` 回调全删）；重力改成"每 tick 加 `weight`、只在换成位移那一处乘 `Tick`" |
| 2 Animator | `ActionController` 不再向动画反要进度：`GetCurrentAnimatorStateInfo` / `GetNextAnimatorStateInfo` / `Clamp01` / `anim.speed` 全删，换成 `CurrentFrame` / `_wasFrame` / `_durationFrames` 自己数帧。Animator 降级成从属——`Awake` 里 `enabled = false`，只在时钟推进那一拍被 `anim.Update(Tick)` 步进，硬直时两者一起不前进 |
| 4 地形 | `CharacterObj` 加 `MoveState {Grounded, Airborne}` + 单 tick 的 `JustLanded`；`GameMain` 里 `Falling = y > 0` 和 `Mathf.Max(0, ...)` 那段删除，改成向下 `Physics.SphereCast` 探测 `Ground` 层并贴地。资产：`TagManager.asset` 加 Ground 层，`GameMain.unity` 加一个顶面严格 `y = 0` 的 `GroundPlane` |
| 表 | `Action.json` 里 13 个动作全部补 `durationFrames`，所有百分比窗口（`inFrames` / `cancelWindowFrames` / `startFromFrame` / `fadeInFrames` / `fadeOutFrames` / `lengthFrames` / `fromFrame` / rootMotion 的起止）换成绝对帧号，`PercentageRange` → `FrameRange {int min, max}`。换算规则：下界 `ceil(pec × Dur)`、上界 `floor(pec × Dur)`，于是"生效区间"跟改造前的浮点判定逐 tick 相同，与原值最大偏差半个 tick |

**必须先订正三处事实**，原文与代码不符：

- **妥协 1 不是"拟合"，是双重帧率依赖**。`_curWeight += weight * delta` 把它当速度累积，应用时却是**不乘 delta 的位移**（`action.RootMotionMove.y - _curWeight`）。有效重力 = `weight / dt`：60 FPS 约 270 m/s²，30 FPS 约 540 m/s²，下落速度直接随帧率翻倍。改造后 `weight` 的语义定成"每个逻辑帧加上的速度"（等价于 270 m/s²），数值一个没动，所以 60 FPS 手感逐项相同，同时变成帧率无关。
- **妥协 4 的现状不是 `position.y < 0`**：实际是 `Falling = pWas.y > 0`（取**移动前**高度）配 `Mathf.Max(0, pWas.y + pMoved.y)`，地面是硬编码字面量 `0`，出现四次、无常量无字段。
- **工程里当时根本没有可探测的地面**：角色根节点无 Collider 无 Rigidbody，场景里的 `Ground` 只是个 Transform 容器，全工程 `Physics.` 调用 0 次。所以原文的 `SphereCastNonAlloc` 不能照抄，而且球从 `y = 0` 起投会与地面相切漏检——实现里起点必须抬到"脚底 + 半径 + 缝隙"，且探测距离**不算半径**（算进去的话被击退抛起几十厘米仍被判成在地上，飞不起来）。

**方案里没照做的三条，都是故意的**：

- **不做表现层插值**（原文第 5 条要 `Vector3.Lerp` 平滑）。判定盒、模型、位移必须严丝合缝，插值会把"看起来在第几帧"和"判到在第几帧"重新拆开；120 Hz 屏上角色位置实际 60 Hz 更新可以接受。
- **`Landing` 状态这期不做**（原文要求三态）。现在没有跳跃，唯一滞空是 Punch2 击退的 0.2 米、约 2~3 tick，`Landing` 既没动画 key 也没表条目，做出来是没有消费者的分支；探测结构已经能承载它。
- **`durationFrames` 的校验放在编辑期而不是运行期**。运行期拿不到"状态名 → clip 长度"（`animKey` 是 Animator **状态名**，不是 clip 名，`BoxingStand` 对应 `idle_A`），所以校对做成了菜单 `Tools/ACT/打印动作时长`：表值和实际 `clip.length × 60` 对不上打 ✗，顺带查空窗口（`min > max`）和越界窗口。美术重导动画之后必须重跑一次。

**验收怎么做的**：跨帧率一致这件事肉眼看不出来（差的都是 16.7 毫秒的零头），所以用 `Assets/Editor/ActionFrameClockTests.cs` 的九个 EditMode 用例来钉：① 13 个动作逐个从第 1 帧跑到被换掉，逐帧断言两个判定盒掩码 == 表里窗口推出的生效帧，且同一动作跑两遍结果一字不差；② Kick1→TwinKick 的 `[25,37]` 窗口在第 24 帧接不上、25/38 接得上、39 接不上；③ 临时窗口按登记那一帧起算，第 5/7 帧接得上、第 8 帧不进；④ 顿挫 3 tick 期间帧号不动、盒子保持，但 Cancel 照旧生效；⑤ 按住前进时走位靠"自己取消自己"活过循环点，循环那一 tick 帧号回绕而 `_wasFrame` 停在末帧；⑥ 30/60/120 三档模拟一秒都正好产出 60 个逻辑帧，卡 0.5 秒只补 4 帧且下一帧不追；⑦ RootMotion 逐帧位移只由帧号决定、累计正好等于表里的米数；⑧ 挨打当帧 Animator 已经切进受击状态（`IsInTransition` 为假、状态名对得上），且顿挫 7 拍里 `normalizedTime` 一动不动、第 8 拍才往前走；⑨ 受击方已在 Hurt 里时 `Keep` 赢下平手、不换动作，此时硬直必须由 `ApplyHitStop` 当场施加，并且它取较大者不叠加。另外 `SetAllActions` 被测试抓出一个真 bug：它原来第一行 `AllActions.Clear()`，而传进来的是 `GameData` 的共享表——同一个控制器第二次调用会把全工程的动作表擦掉（生产里每个角色只设一次所以从没暴露，Phase 4 做"分角色动作子集"时必炸），现在改成只换引用。

**已知偏差与遗留**：

- 钉死执行顺序本身就是一次行为变更（原来五个 `Update()` 的顺序由 Unity 决定），所以本阶段的承诺只是"改造后跨帧率一致"，不是"与改造前逐帧一致"。
- **顿挫期间必须显式求值一次姿势**。Animator 变从属之后，硬直那几拍根本不进 `anim.Update`，于是"换动作当帧摆出新姿势"这件事也没了：挨一下之后受击姿势要等整个 hitStun（7 tick ≈ 117 ms）走完才亮出来，打击感直接塌掉——实测就是"顿帧不明显"。修法是决策完动作的那一 tick 补一次 `anim.Update(0f)`（0 步长只求值不推时间）。改造前 `anim.speed = 0` 只冻进度、不冻"进入新状态"，所以老手感里挨打当场就是受击姿势。
- 改造前 `_pec` 是从 Animator 读的，逻辑进度比屏幕上的姿势**滞后一个渲染帧**，于是命中当帧冻住的姿势比现在深一帧。现在逻辑与姿势同帧，冻结点正好落在判定盒开启的那一帧。想让顿挫抓在更深的姿势上，就改表把窗口往后推一帧，别改代码。
- **受击硬直每记命中都要刷，不能只挂在换动作上**。原来 `hitStunFrames` 只经由 `PreorderActionByActionChangeInfo` → `ChangeAction` 那一条路写进 `_freezeFrames`，而受击方已经在 `Hurt` 里时，它自己的 `defensePhase.selfActionChange`（`Keep`，优先级 5）跟攻击方的 `targetActionChange`（优先级 5）打平，平手判定让 `Keep` 赢 → 预约是空的 → 不换动作 → 硬直也没人写。表现就是"第一拳有顿帧、第二拳没有"。现在 `DoAttack` 里对防守方无条件 `ApplyHitStop(hitStunFrames)`（取较大者，不叠加），`Doc/issue/` 里那条"重复命中"的疑问一并解决。
- 动画全是 30 帧 authored，逻辑 60 Hz ⇒ 窗口只会落在偶数帧。这是既有事实，动作编辑器实现时要照此把窗口对齐偶数 tick。
- 命中相交的粒度仍是物理步（`Fixed Timestep 0.02`）而不是逻辑帧；`Input.GetButton` 每渲染帧只 poll 一次导致的采样丢失任何帧率都救不回来。两条分别登记在 `Doc/issue/`。


---

### Phase 3：战斗打击、数值与霸体管线健全（打击感精细化）

本阶段目标：让战斗系统具备商用级 ACT 的打击反馈、多段判定去重与属性计算。

#### 8. 妥协 5：战斗伤害数值与霸体/防御结算 (`GameMain.cs` & `CombatPipeline`)
* **现状**：`//todo demo 里就先不做了`，仅有判定盒相交，无数值与硬直结算。
* **重构方案**：
  - 引入攻击载荷结构体 `DamagePayload`：包含攻击者 ID、基础伤害数值、击退矢量（`Knockback`）、削韧点数（`PoiseDamage`）、卡肉帧数（`HitStopFrames`）；
  - 角色挂载属性组件（`CombatStats`）：包含生命值、防御力、霸体条（`PoiseGauge`）；
  - 结算流程：
    1. 削韧判断：若 `PoiseGauge > 0` 且未被击破，角色受击扣血但**不产生受击硬直**（产生霸体闪白效果）；
    2. 破霸体：韧性清零时，强制打断当前出招并切入受击硬直动作；
    3. 顿挫感：攻击者执行微秒 `ApplyHitStop`，受击者执行击退插值。

#### 9. 妥协 10：碰撞对决缺乏空间加速结构的暴力检测 (`GameMain.cs`)
* **现状**：双重 `foreach` 循环全局所有角色进行相交计算，计算复杂度为 $O(N^2)$。
* **重构方案**：
  - 阵营碰撞分层（Layer Matrix）：玩家属于 `Player` 层，敌人属于 `Enemy` 层，同阵营判定盒直接由物理掩码跳过；
  - 空间粗筛（Broadphase）：优先检测双方角色根节点距离（`DistanceSquared < MaxAttackRange^2`），仅在接近时才遍历判定盒（Narrowphase）。

---

### Phase 4：实体生命周期与场景管理（工程扩展性）

本阶段目标：解耦静态拖拽，支持动态刷怪、角色复用与关卡流程。

#### 10. 妥协 3：实体生命周期与场景管理静态拖拽 (`GameMain.cs`)
* **现状**：玩家与敌人在 Inspector 静态拖拽赋值，数据初始化强耦合在单例中。
* **重构方案**：
  - 创建 `EntityManager` 服务类，提供 `SpawnCharacter(characterId, position, camp)` 接口；
  - 角色实体引入对象池（Object Pool）：敌人死亡后回收至池中，刷怪时唤醒复用，杜绝运行时 `Instantiate` / `Destroy` 引起的内存碎片与掉帧卡顿。

---

## 三、 执行落地计划与验证准则

| 阶段 | 实施任务 | 预计改动代码 | 验证方式 |
| :--- | :--- | :--- | :--- |
| **Phase 1** | 治理妥协 6、7、8、9（0 GC 热点优化）· **已实施** | `RootMotionMethod.cs`<br>`CharacterObj.cs`<br>`ActionController.cs`<br>`GameData.cs`<br>`InputToCommand.cs`<br>`Boxes/BoxTagRegistry.cs` | 用 `Assets/Scripts/GcProbe.cs` 的分区探针看：动作推进、判定盒计算、按分类选动作、命中判定、移动意图、输入刷新这 6 个分区 **max = 0 B/帧**。判据是分区归零而不是整帧归零——调试用的输入面板仍在每帧拼字符串（`Doc/issue/ui-inputtext-per-frame-alloc.md`）。Unity 2023.2 已经没有 `Profiler.GetAllocatedBytesForCurrentThread`，而 `Profiler.GetMonoUsedSizeLong()` 在编辑器里一帧之内不动（用它做差值会恒 0），所以探针取 `ProfilerRecorder` 的 `GC Allocated In Frame` 计数器，详见 `Doc/bugfix/gc-probe-false-zero.md`。 |
| **Phase 2** | 治理妥协 1、2、4（时基、物理、地形）· **已实施** | `LogicClock.cs`<br>`GameMain.cs`<br>`ActionController.cs`<br>`CharacterObj.cs`<br>`Action/FrameRange.cs` 等表结构<br>`GameData/Action.json`<br>`Assets/Editor/`（校对菜单 + EditMode 用例） | 见上文的 Phase 2 实施记录。判据不靠肉眼：`Assets/Editor/ActionFrameClockTests.cs` 六个 EditMode 用例钉住"第几帧判到、第几帧接得上、顿挫保持、三档渲染帧率各产出 60 个逻辑帧"；滞空轨迹用 Punch2 的击退（工程里没有跳跃）在 `GameMain.targetFrameRate` 30/60/120 三档下人工复核一次。 |
| **Phase 3** | 治理妥协 5、10（伤害、霸体、碰撞加速） | `GameMain.cs`<br>`CharacterObj.cs` | 攻击敌人时出现伤害飘字、韧性打崩击退、以及霸体霸体硬抗动作反馈。 |
| **Phase 4** | 治理妥协 3（实体管理与对象池） | `GameMain.cs`<br>`EntityManager.cs` | 场景可动态生成并回收 20+ 个怪物，帧率平稳不卡顿。 |
