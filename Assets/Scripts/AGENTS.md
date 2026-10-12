# Assets/Scripts 模块指南

动作系统的逻辑与表现代码：`Character/`（角色、动作控制器、判定盒）、`Methods/`（表数据里的脚本函数表）、`GameMain.cs`。

## 热路径（每帧执行的代码）

- 每帧路径**禁止**堆分配：不许出现 `new` 数组 / `List` / 字典字面量、`string` 拼接、值类型装箱。表里的字符串一律在 `GameData.Load()` 里烘焙成数值之后再用（例：`ActionInfo.rootMotionParam`、`AttackBoxTurnOnInfo.tagMask`）。
- 往 `ActionInfo` 及其阶段结构体里加"运行期算出来的"字段时，必须标 `[NonSerialized]` 并在 `GameData.Load()` 里赋值：`Resources/GameData/Action.json` 是策划表，不往里塞派生字段。
- `foreach` 遍历结构体数组拿到的是拷贝，改它的成员会直接编译不过（CS1654）：改表数据就先 `ActionInfo info = source;` 拷一份再改，改数组元素（数组本身是引用）就用带索引的 `for`。

## 碰撞盒标签

- 只有 `BoxTagRegistry` 允许认识标签字符串，新增标签一律走 `MaskOf`；判定处只许位与 `(a & b) != 0`，不许再比字符串。
- 标签的位号按注册先后分配，**同一次运行内稳定、跨运行会变**（各 prefab 的 Awake 顺序不固定）：日志和断言里只打标签名，别把掩码数字当常量。
- 盒子组件的 `TagMask` 由 `CharacterObj.Awake` 顺带烘焙（那里本来就要遍历子组件认主人），必须早于任何 Update。
- 标签总数上限 31，超了 `MaskOf` 报错并返回 0（该标签从此判定不到），加标签时看 Console。

## 分类索引

- `GameData.ActionsOfCatalog` 的倒排索引是对整张动作表建的，前提是所有角色共用同一份表（`GameMain` 把 `AllActions()` 的同一个 List 交给每个 `ActionController`）。角色一旦有了自己的动作子集，索引必须下移到 `ActionController`。

## 性能验证

- 每帧分配用 `GcProbe.cs` 的分区探针量，判据是**分区 0 B/帧**而不是整帧 0 B（调试 UI 的残留见 `Doc/issue/ui-inputtext-per-frame-alloc.md`）。
- Unity 2023.2 已经没有 `Profiler.GetAllocatedBytesForCurrentThread`，所以 GcProbe 取 `Profiler.GetMonoUsedSizeLong()` 的区间差值；一帧之内不会跑 GC，差值就是这段代码分配的量。

相关文档：`Doc/plan/refactor.md`（十项妥协重构规划，Phase 1 已实施）
