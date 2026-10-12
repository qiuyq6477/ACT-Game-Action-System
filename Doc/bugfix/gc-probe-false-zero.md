# GC 探针量不到分配，却把六个分区全报成 0 B

> 状态：已修复 · 2026-10

## 一句话

`GcProbe` 用来验证"热路径每帧 0 分配"，但它取的计数来源在编辑器里读不出增量，
于是六个分区整整齐齐报了 `avg=0 max=0`——**看起来像优化已经达标，实际是探针根本啥也没量到**。

## 发生了什么

第一版探针（提交 `b8b80e5`）用 `UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong()`
（托管堆"已用字节"）在分区前后各取一次、相减当作分配量。理由听起来成立：一帧之内不会跑 GC，
所以堆用量的增加就等于这段代码分配的量。

实测结果是：

```
[GcProbe] ... | ActionControllerUpdate avg=0 max=0 n=900 | CalculateBoxInfo avg=0 max=0 n=900 |
ChangeByCatalog avg=0 max=0 n=2 | CanAttackTargetNow avg=0 max=0 n=1200 | NatureMove avg=0 max=0 n=625 |
InputUpdate avg=0 max=0 n=900 || 自检 0B(应约1024，为0说明Profiler分配计数没生效)
```

`n` 是对的（调用次数量没问题），但字节数全 0，而**自检也是 0**——自检是新分配一块 1024 字节的数组再测差值，
它理应当读出 1024。自检 0 就等于是探针在喊"我这条路是瞎的"。
原因是 `GetMonoUsedSizeLong()` 在编辑器里一帧之内根本不刷新（它靠分析器在帧边界上采集），
前后两次拿到同一个数，差值恒为 0，任何分配都测不出来。

顺带一条踩坑记录：Unity 2023.2 已经没有 `Profiler.GetAllocatedBytesForCurrentThread()`
（在 `Editor/Data/Managed/UnityEngine` 各程序集里搜不到这个名字），
所以不能退回那个更合适的 API。

## 怎么修的

改用分析器的内存计数器 **`GC Allocated In Frame`**，它记的就是"本帧到此刻为止累计分配了多少字节"，
用 `Unity.Profiling.ProfilerRecorder` 读：

```csharp
_gcAllocInFrame = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
long bytes = _gcAllocInFrame.CurrentValue;   // 区间两端各取一次，相减就是这段的分配量
```

比换 API 更重要的是**把"取不到"和"没有分配"这两种情况在输出上分开**，这是这次事故真正该记住的一条：

- `Sample()` 在计数器 `Valid == false` 时返回 -1，`End` 遇到负的端值就**不记录**，而不是记一个 0；
- 报告第一行固定打印 `计数器 GC Allocated In Frame:可用/取不到` 和 `自检:NNNN B(应为1024)`，
  两者任一不成立就追加一句"←计数没生效，下面的数字全部不可信"。

任何"全 0"的测量结果，都必须先看它有没有自带一个已知非零的对照量。

## 状态说明

`GC Allocated In Frame` 能否在同一帧内实时读出区间增量，还需要一次播放模式确认：
播放场景后看第一行里的 `自检`，等于 1024 才算这条计数路径是通的；
如果显示"可用"但自检仍是 0，就退而求其次只按**整帧**计数（帧末读一次该计数器），
用"逐个还原某项改动、看整帧数字是否上涨"的方式做 A/B 定位。

- post-mortem 相关代码：`Assets/Scripts/GcProbe.cs`
- 规划与验收判据：`Doc/plan/refactor.md`（Phase 1）
