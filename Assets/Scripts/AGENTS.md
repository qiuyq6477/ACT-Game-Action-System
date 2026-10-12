# Assets/Scripts 模块指南

动作系统的逻辑与表现代码：`Character/`（角色、动作控制器、判定盒）、`Methods/`（表数据里的脚本函数表）、`GameMain.cs`。

## 热路径（每帧执行的代码）

- 每帧路径**禁止**堆分配：不许出现 `new` 数组 / `List` / 字典字面量、`string` 拼接、值类型装箱。表里的字符串一律在 `GameData.Load()` 里烘焙成数值之后再用（例：`ActionInfo.rootMotionParam`、`AttackBoxTurnOnInfo.tagMask`）。
- 往 `ActionInfo` 及其阶段结构体里加"运行期算出来的"字段时，必须标 `[NonSerialized]` 并在 `GameData.Load()` 里赋值：`Resources/GameData/Action.json` 是策划表，不往里塞派生字段。
- `foreach` 遍历结构体数组拿到的是拷贝，改它的成员会直接编译不过（CS1654）：改表数据就先 `ActionInfo info = source;` 拷一份再改，改数组元素（数组本身是引用）就用带索引的 `for`。

## 碰撞盒标签

- 只有 `BoxTagRegistry` 允许认识标签字符串，新增标签一律走 `MaskOf`；判定处只许位与 `(a & b) != 0`，不许再比字符串。
- 标签的位号按注册先后分配，**同一次运行内稳定、跨运行会变**（各 prefab 的 Awake 顺序不固定）：日志和断言里只打标签名，别把掩码数字当常量。
- 盒子组件的 `TagMask` 由 `CharacterObj.Awake` 顺带烘焙（那里本来就要遍历子组件认主人），必须早于第一个逻辑帧（盒子开关由 `GameMain` 的时钟刷新，已经没有各自的 Update 了）。
- 标签总数上限 31，超了 `MaskOf` 报错并返回 0（该标签从此判定不到），加标签时看 Console。

## 逻辑时钟与单位

- 逻辑层的"时长"一律是 `int` 逻辑帧（`durationFrames`、`freezeFrames`、`hitStunFrames`、`inFrames`、`validInFrames`、`CooldownFrames`…），不许再出现按秒累加的字段。动作内部的窗口存**绝对帧号**（`FrameRange`），判定只剩整数比较。
- 一个动作的时长真源只有表里的 `durationFrames`：窗口上界、RootMotion 的线性段、CrossFade 的 offset 全以它为标尺。美术重导 clip 之后这个数必须跟着改——跑菜单 `Tools/ACT/打印动作时长` 校对（跟实际长度对不上打 ✗，顺带查空窗口和越界窗口）。
- `Time.deltaTime` 只准在 `GameMain.Update` 里读一次，"该跑几个逻辑帧"的换算归 `LogicClock.TicksDue`。`LogicClock.Tick` 全工程只准用两处——把"米/秒"的速率换算成每帧位移、以及步进 Animator。速率（`moveSpeed`、`weight`）仍然带秒量纲。
- Animator 是从属：`ActionController.Awake` 里 `anim.enabled = false`，只在时钟推进的那一拍被 `anim.Update(LogicClock.Tick)` 步进，硬直时两者一起不前进。**但换动作/循环那一 tick 必须额外补一次 `anim.Update(0f)`**（0 步长只求值不推时间）——否则顿挫期间根本摆不出新姿势，受击动作要等整个 hitStun 走完才露出来，打击感就这么丢掉（见 `Doc/plan/refactor.md` 的 Phase 2 实施记录）。不许再读 `GetCurrentAnimatorStateInfo` 之类的动画进度当逻辑真源，也不许再用 `anim.speed`。
- 面向与纵深都是"逻辑一个布尔 + 表现各自落地"：面向的真源是 `InputToCommand.inversed`（输入镜像、位移符号、受力翻转三处都只用它），落地交给角色身上的 `FacingApply`——3D 模型绕 Y 转 90/270，贴在 3D 平面上的精灵只能翻 `localScale.x`（绕 Y 转 90° 会侧对镜头），逻辑层**不许**再写 `transform.eulerAngles = …90/270…`。纵深（`KeyMap.Up`/`Duck`，屏幕纵向 = 车道远近）是**屏幕空间轴，永不镜像**：`NatureMove` 里横向乘 `Inversed` 符号、纵深项不乘。
- 六个 Tick（`InputToCommand` / `ActionController` / `AttackHitBox` / `BeHitBox` / `CharacterObj` / `CharacterObj.SeparateLanes`）**不许**写 Unity 的 `Update()` 回调：由 `GameMain.TickAll()` 按固定顺序调用（输入→动作→判定盒→命中结算→计时与移动→同轨推挤）。改成别处调用的顺序就要重新想一遍谁先谁后。
- Unity 的按键状态每渲染帧只能 poll 一次，所以 `SampleRenderFrame()` 在时钟之外、每渲染帧一次；`Tick()` 里的判定全是整数帧号比较。模拟"某一刻按了什么"一律走 `InputToCommand.AddInput`（测试和以后的 AI、回放都从这里进）。
- 改动窗口语义、动作时钟或顿挫，必须跑 `Assets/Editor/ActionFrameClockTests.cs`（Test Runner → EditMode）。它钉的是"第几帧判到、第几帧接得上"，这些差的都是 16.7 毫秒的零头，肉眼看不出来。

## 分类索引

- `GameData.ActionsOfCatalog` 的倒排索引是对整张动作表建的，前提是所有角色共用同一份表（`GameMain` 把 `AllActions()` 的同一个 List 交给每个 `ActionController`）。角色一旦有了自己的动作子集，索引必须下移到 `ActionController`。

## 性能验证

- 每帧分配用 `GcProbe.cs` 的分区探针量，判据是**分区 0 B/帧**而不是整帧 0 B（调试 UI 的残留见 `Doc/issue/ui-inputtext-per-frame-alloc.md`）。
- 分配计数只认 `Unity.Profiling.ProfilerRecorder` 的 `ProfilerCategory.Memory / "GC Allocated In Frame"`；`Profiler.GetMonoUsedSizeLong()` 在编辑器里一帧之内不动，用它做差值会量到恒 0（Unity 2023.2 也已无 `GetAllocatedBytesForCurrentThread`）。
- 任何"全 0"的测量结论，必须自带一个已知非零的对照量（GcProbe 的自检是 1024 字节的分配）；对照量为 0 就说明量的是个瞎指标。详见 `Doc/bugfix/gc-probe-false-zero.md`。
- 0 B/帧的判据看**稳态**：常驻 List 的头几次扩容、某个静态类第一次被摸到时的构造（如 `PickActionMethod` 的字典与委托）都会记出一次性非零，别当回归去追。

相关文档：`Doc/plan/refactor.md`（十项妥协重构规划，Phase 1、2 已实施）、`Doc/plan/beatemup-port.md`（BeatEmUp 移植方案）、`Doc/decision/`（真源与坐标轴的不可逆决定）
