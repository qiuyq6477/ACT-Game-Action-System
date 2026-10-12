# ACT 动作系统十项妥协工业级重构规划方案

> 状态：Phase 1 已实施 · 2026-10（Phase 2~4 仍为规划中）

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
| **Phase 1** | 治理妥协 6、7、8、9（0 GC 热点优化）· **已实施** | `RootMotionMethod.cs`<br>`CharacterObj.cs`<br>`ActionController.cs`<br>`GameData.cs`<br>`InputToCommand.cs`<br>`Boxes/BoxTagRegistry.cs` | 用 `Assets/Scripts/GcProbe.cs` 的分区探针看：动作推进、判定盒计算、按分类选动作、命中判定、移动意图、输入刷新这 6 个分区 **max = 0 B/帧**。判据是分区归零而不是整帧归零——调试用的输入面板仍在每帧拼字符串（`Doc/issue/ui-inputtext-per-frame-alloc.md`）。Unity 2023.2 已经没有 `Profiler.GetAllocatedBytesForCurrentThread`，所以探针取 `Profiler.GetMonoUsedSizeLong()` 的区间差值。 |
| **Phase 2** | 治理妥协 1、2、4（时基、物理、地形） | `CharacterObj.cs`<br>`ActionController.cs`<br>`GameMain.cs` | 在 30 FPS、60 FPS 与 120 FPS 下测试跳跃高点与 Cancel 窗口，手感完全无缝且判定一致。 |
| **Phase 3** | 治理妥协 5、10（伤害、霸体、碰撞加速） | `GameMain.cs`<br>`CharacterObj.cs` | 攻击敌人时出现伤害飘字、韧性打崩击退、以及霸体霸体硬抗动作反馈。 |
| **Phase 4** | 治理妥协 3（实体管理与对象池） | `GameMain.cs`<br>`EntityManager.cs` | 场景可动态生成并回收 20+ 个怪物，帧率平稳不卡顿。 |
