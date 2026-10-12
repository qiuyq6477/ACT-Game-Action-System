# 换波那一拍，AI 拍上有 2592 B 的一次性分配

> 状态：未处理（已经分成两种解释并排掉了其中一个）· 2026-10

解决的问题：本工程的判据是"稳态每逻辑帧 0 B"，而切片 2.3 之后场上的人由波次表生成，
生成那一拍附近的分配到底记在谁头上、是不是又一次性头部扩容，现在说不清。

## 现象

批处理 Play 探针（真实场景 `Assets/Scenes/GameMain.unity`，70000 渲染帧 / 820 逻辑帧）里，
按逻辑帧开的 300 拍窗口中：

```
EnemyThink avg=8 max=2592 n=300 | EnemyCount avg=0 max=0 n=300 | PoolTick avg=0 max=0 | ... 其余全 0
```

另一个包含"下一波生成 + 探针自己打印"的窗口里，`ActionControllerUpdate max=472`、
`CueFire max=240`、`ChangeAction avg=7 max=288`、`ChangeByCatalog avg=26`、`DamageSettle avg=3`
这些零散非零同时出现。切片 2.1 那一次同样的探针是"全部分区 avg=0 max=0"。

## 两种解释，还没分辨出来

1. **又一次性头部扩容**（可接受）：新生成的那个人第一次走完整条链时，某些常驻结构还在长。
   `InputToCommand` 两条列表与 `ActionController` 三条已经按满员预置了（切片 2.2 顺带做的），
   但 `ActionController.AllActions`（`new List<ActionInfo>()`，没给容量）与 `EnemyMind` 的
   `attackKeys` 数组仍然是"用到才长"。2592 B 这个数与"一个角色的几份小数组"量级吻合。
2. **探针自己的分配被算进了分区**（测量假象）：`GC Allocated In Frame` 是**按渲染帧**清零的，
   而一个 300 逻辑帧的窗口里两者并不对齐。探针每 3000 渲染帧打印一行，那行是字符串拼接——
   它落在哪一拍，字节就被算进那一拍正在收尾的那个分区。切片 2.3 给打印加了「波次=x/y」，
   正好让这件事比 2.1 那次更容易发生。

## 建议怎么修

先把两种解释分开，再动手：

- 让探针**不要在测量帧打印**（把要打印的东西先攒进定长缓冲，退出时一次写完），或者
  把 `GcProbe` 的窗口改成"只统计没有打印的那几拍"。分区里的零散非零如果消失，就是第 2 种。
- 如果还剩一个 2592 的尖峰：给 `ActionController.AllActions` 按 `actionSubset.Length` 预置容量、
  `EnemyMind.attackKeys` 改成静态共享数组（四下按键不需要每人一份），再看尖峰是否归零。

## 切片 2.4 复验（把两种解释分开了一点）

加了血条/飘字/白闪之后重跑同一支探针，两个窗口长这样：

- **安静窗口**（这一段时间没有换波、没有命中）：`EnemyThink avg=0 max=0`、`PoolTick avg=0 max=0`、
  其余分区除了 `SampleOverlaps max=108` 之外全 0。
- **换波 + 打死人那一段**：`EnemyThink avg=8 max=2592`、`ChangeByCatalog avg=316 max=1266 n=4`、
  `ChangeAction avg=7 max=288`。

于是第 1 种解释（新生成的人身上的冷结构在第一次走链时长起来）站得住：尖峰只出现在换波那一段，
安静窗口是干净的。第 2 种（探针打印落在同一个渲染帧）也仍然存在——`ChangeByCatalog` 那 1266 B
发生在"打死那一记"的换动作里，而 `ActionController.ChangeAction` 的注释早就写了它的大头是
引擎内部的 `anim.CrossFade` + 随后那次 `anim.Update`，不是我们写的代码。

还值得试的只剩一件：给 `ActionController.AllActions` 按 `actionSubset.Length` 预置容量
（现在是默认容量，每个新生成的人都要连着扩容几次）。归到这一条 issue 里，不阻塞任何一片。

## 备注

发现于切片 2.3 的验收。波次推进本身是对的（表里坐标、全灭才推、间隔走完才开下一波，
都由第20区用例钉住），这条只关于"分配算在谁头上"，按 `AGENTS.md` 的规则单独登记、不阻塞当次任务。
