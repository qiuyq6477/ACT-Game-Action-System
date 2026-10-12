# Assets/Scripts 模块指南

动作系统的逻辑与表现代码：`Character/`（角色、动作控制器、判定盒）、`Methods/`（表数据里的脚本函数表）、`GameMain.cs`。

## 热路径（每帧执行的代码）

- 每帧路径**禁止**堆分配：不许出现 `new` 数组 / `List` / 字典字面量、`string` 拼接、值类型装箱。表里的字符串一律在 `GameData.Load()` 里烘焙成数值之后再用（例：`ActionInfo.rootMotionParam`、`AttackBoxTurnOnInfo.tagMask`）。往 `ActionInfo` 及其阶段结构体里加"运行期算出来的"字段时，必须标 `[NonSerialized]` 并在 `GameData.Load()` 里赋值：`Resources/GameData/Action.json` 是策划表，不往里塞派生字段。
- `foreach` 遍历结构体数组拿到的是拷贝，改它的成员会直接编译不过（CS1654）：改表数据就先 `ActionInfo info = source;` 拷一份再改，改数组元素（数组本身是引用）就用带索引的 `for`。

## 碰撞盒标签与命中采样

- 只有 `BoxTagRegistry` 允许认识标签字符串，新增标签一律走 `MaskOf`；判定处只许位与 `(a & b) != 0`，不许再比字符串。标签总数上限 31，超了 `MaskOf` 报错并返回 0（该标签从此判定不到），加标签时看 Console。
- 标签的位号按注册先后分配，**同一次运行内稳定、跨运行会变**（各 prefab 的 Awake 顺序不固定）：日志和断言里只打标签名，别把掩码数字当常量。
- 盒子组件的绑定（认主人、烘焙 `TagMask`、认领碰撞体、建好每拍重填的接触列表）全在 `CharacterObj.BindBoxes()` 里，`Awake` 只调它：**EditMode 下 `Instantiate` 不触发 `Awake`**，用例要直接叫 `BindBoxes`。绑定必须早于第一个逻辑帧。
- 命中相交给逻辑帧主动采样（`GameMain.TickAll` 的相交采样拍 → `CharacterObj.SampleOverlaps` → `AttackHitBox.SampleTouches`），**不许**再写 `OnTriggerEnter/Exit`：物理回调按 `Fixed Timestep 0.02`（50Hz）触发，与 60Hz 的逻辑帧错频，窄窗口静默打空、大位移直接穿过（`Doc/plan/beatemup-port.md` 切片 1.3）。
- 相交查询只认 `BeHitBox` 层（`BeHitBox.QueryMask`），受击盒必须归到这一层，否则采不到；地面与道具因此天然被排除在缓冲区之外。
- 盒子「在哪」和帧事件同样是表数据，不许留回动画：`attackPhase[].track` 是稀疏关键帧，`GameData.Load` 摊平成按帧号直取的 `bakedPosition/bakedSize`，只有盒子开着的那一拍才写 `localPosition` + `BoxCollider.size`（写局部坐标，面向镜像因此自动生效；没填 track 就一个数都不碰）；`ActionInfo.cues` 由 `ActionController.CalculateCues` 按**游标**派发而不是区间比较（循环回绕那一拍区间是倒的，点会被整段跳过），换动作置 `startFrame-1`、回绕置 `-1`，硬直期间不响。派发口是 `CueDispatch.Handler`（只允许一个消费者），**消费端必须池化**——源工程每响一次就 `new GameObject + AudioSource`，照搬就撞 0 分配。两者的拍号与窗口 `min` 同一个基准（0-based 恒等）。

## 逻辑时钟与单位

- 逻辑层的"时长"一律是 `int` 逻辑帧（`durationFrames`、`freezeFrames`、`hitStunFrames`、`inFrames`、`validInFrames`、`CooldownFrames`…），不许再出现按秒累加的字段。动作内部的窗口存**绝对帧号**（`FrameRange`），判定只剩整数比较。
- 一个动作的时长真源只有表里的 `durationFrames`：窗口上界、RootMotion 的线性段、CrossFade 的 offset 全以它为标尺。美术重导 clip 之后这个数必须跟着改——跑菜单 `Tools/ACT/打印动作时长` 校对（跟实际长度对不上打 ✗，顺带查空窗口和越界窗口）。从 `.anim` 曲线迁帧数据同样**不许手数**：跑 `Tools/ACT/导出 BeatEmUp 帧数据草稿`（产出 `Doc/plan/beatemup-anim-dump.md`），换算公式 `min = 抬起帧`、`max = 落下帧 − 2`（第 n 拍摆的就是 clip 第 n 帧，是恒等映射；窗口按跨帧语义生效 min..max+1），推导写在工具头注释里；工具同时产出可以直接粘的 `track`（位置/尺寸已按帧合并，尺寸 = 缩放曲线 × 精灵 size）与 `cues`（`kind` 是 `CueKind` 的序号）两段 JSON。
- `Time.deltaTime` 只准在 `GameMain.Update` 里读一次，"该跑几个逻辑帧"的换算归 `LogicClock.TicksDue`。`LogicClock.Tick` 全工程只准用两处——把"米/秒"的速率换算成每帧位移、以及步进 Animator。速率（`moveSpeed`、`weight`）仍然带秒量纲。
- Animator 是从属：`ActionController.Awake` 里 `anim.enabled = false`，只在时钟推进的那一拍被 `anim.Update(LogicClock.Tick)` 步进，硬直时两者一起不前进。**但换动作/循环那一 tick 必须额外补一次 `anim.Update(0f)`**（0 步长只求值不推时间）——否则顿挫期间根本摆不出新姿势，受击动作要等整个 hitStun 走完才露出来，打击感就这么丢掉（见 `Doc/plan/refactor.md` 的 Phase 2 实施记录）。不许再读 `GetCurrentAnimatorStateInfo` 之类的动画进度当逻辑真源，也不许再用 `anim.speed`。
- 面向与纵深都是"逻辑一个布尔 + 表现各自落地"：面向的真源是 `InputToCommand.inversed`（输入镜像、位移符号、受力翻转三处都只用它），落地交给角色身上的 `FacingApply`——3D 模型绕 Y 转 90/270，贴在 3D 平面上的精灵只能翻 `localScale.x`（绕 Y 转 90° 会侧对镜头），逻辑层**不许**再写 `transform.eulerAngles = …90/270…`。纵深（`KeyMap.Up`/`Duck`，屏幕纵向 = 车道远近）是**屏幕空间轴，永不镜像**：`NatureMove` 里横向乘 `Inversed` 符号、纵深项不乘。
- 六个 Tick（`InputToCommand` / `ActionController` / `AttackHitBox` / `BeHitBox` / `CharacterObj` / `CharacterObj.SeparateLanes`）**不许**写 Unity 的 `Update()` 回调：由 `GameMain.TickAll()` 按固定顺序调用（输入→动作→判定盒→相交采样→命中结算→计时与移动→同轨推挤）。采样那一拍是静态入口 `CharacterObj.SampleOverlaps`，不是组件回调。改成别处调用的顺序就要重新想一遍谁先谁后。
- Unity 的按键状态每渲染帧只能 poll 一次，所以 `SampleRenderFrame()` 在时钟之外、每渲染帧一次；`Tick()` 里的判定全是整数帧号比较。模拟"某一刻按了什么"一律走 `InputToCommand.AddInput`（测试和以后的 AI、回放都从这里进）。
- 长按语义 = `KeyMap.Block` **每拍登记**（不吃非方向键的 6 拍节流，否则 `IsKeyHeld` 那一拍宽的窗口必然读成松手）+ **换动作不清历史**（`KeepAcrossActionChange` 单独放行，它不是方向键，别去改脏 `IsDirectionInput`）。于是"按住"就是"每拍这条指令都成立"，不需要额外的 hold 状态机。
- 逻辑层的随机**只准**走 `Dice`（种子 `GameMain.randomSeed`，默认常量，所以不改就能复现同一串），**不许** `UnityEngine.Random`（墙钟）或 `System.Random`（算法随运行时版本变），且只能由**逻辑帧**触发——掺进任何时间量，回放和联网校验就废了。
- 改动窗口语义、动作时钟、顿挫、命中采样、盒子轨迹、帧事件或派生门禁，必须跑 `Assets/Editor/ActionFrameClockTests.cs`（Test Runner → EditMode）。它钉的是"第几帧判到、第几帧接得上、第几帧打到、第几帧摆到哪、第几拍响哪条"，这些差的都是 16.7 毫秒的零头，肉眼看不出来。
- 用例里比较"累加出来的位置"和"每拍位移 × 拍数"必须带容差（`1e-6f`）：两种算法天然差 1 ULP，写死精确相等的话编译器一融合乘加就假红。

## 表函数、场上查询与分类索引

- 第四张函数表 `Methods/CancelConditionMethod.cs`：派生在「窗口 + 标签 + 指令」之后再过一道门禁（`StateIs` 地面/空中、`NearbyUnitInAction` 近处有谁），表里填 `CancelTag.cancelCondition = {method, param}`；**留空 = 没有门禁**，现有表项行为一字不变。函数名写错按**不成立**处理（默认放行等于悄悄拆门禁），参数在 `GameData.Load` 的 `Validate` 里检查，不留到战斗中间让 `float.Parse` 抛。
- 查"场上有谁"只走 `CharacterObj.Units`（注册在 `BindBoxes`、注销在 `OnDestroy`），**禁** `FindGameObjectsWithTag`。列表次序就是 Awake 次序、跨运行不稳定，平局裁决必须自己按位置或 id 排。
- 分类倒排索引建在 `ActionController` 上（`SetAllActions` 里随"这个角色的那份表"一起重建），每个角色只会一份子集（`CharacterObj.actionSubset`，留空 = 全表）。**不许**把索引挪回 `GameData` 按全表建：那会把角色根本不会的招式端上 byCatalog 候选，"挨一下切到什么动作"就取决于全表的行序。开场 id 同理走 `CharacterObj.defaultActionId`，没填才退回 `GameMain` 里写死的那两个。

## 性能验证

- 每帧分配用 `GcProbe.cs` 的分区探针量，判据是**分区 0 B/帧**而不是整帧 0 B（调试 UI 的残留见 `Doc/issue/ui-inputtext-per-frame-alloc.md`）。
- 分配计数只认 `Unity.Profiling.ProfilerRecorder` 的 `ProfilerCategory.Memory / "GC Allocated In Frame"`；`Profiler.GetMonoUsedSizeLong()` 在编辑器里一帧之内不动，用它做差值会量到恒 0（Unity 2023.2 也已无 `GetAllocatedBytesForCurrentThread`）。
- 任何"全 0"的测量结论，必须自带一个已知非零的对照量（GcProbe 的自检是 1024 字节的分配）；对照量为 0 就说明量的是个瞎指标。详见 `Doc/bugfix/gc-probe-false-zero.md`。
- 0 B/帧的判据看**稳态**：常驻 List 的头几次扩容、某个静态类第一次被摸到时的构造（如 `PickActionMethod` 的字典与委托）都会记出一次性非零，别当回归去追。

相关文档：`Doc/plan/refactor.md`（十项妥协重构规划，Phase 1、2 已实施）、`Doc/plan/beatemup-port.md`（BeatEmUp 移植方案）、`Doc/decision/`（真源与坐标轴的不可逆决定）
