# 反手时 PhysX 每帧喊 BoxCollider 负缩放警告并在热路径分配

> 状态：未处理 · 2026-10-12 发现（切片 1.8）

## 现象

精灵玩家（`Assets/Resources/BeatEmUpPlayer.prefab`）朝左站着出招时，Console 每隔几帧就冒一条：

```
BoxCollider does not support negative scale or size.
The effective box size has been forced positive and is likely to give unexpected collision geometry.
Scene hierarchy path "GameLayer/BeatEmUpPlayer/BeatHitBox"
```

同时 `GcProbe` 的 `BoxPose` / `ActionControllerUpdate` 分区在**出招的那几拍**读到非零（一次实测 `BoxPose avg=2 max=300`、
`ActionControllerUpdate avg=29 max=2395`），空跑稳态仍然是 0。一条 `Debug.LogWarning` 连栈一起提取，分配就在这一拍上。

## 根因

`FacingApply` 的 `SpriteScaleX` 后端用 `localScale.x = -1` 镜像精灵（1.1 的决定，理由写在它的注释里：
绕 Y 转 90° 会让一张精灵侧对镜头，而 `flipX` 只翻贴图不翻子节点，判定盒会留在错误的一侧）。
判定盒与受击盒都是根节点的孩子，于是它们的 `lossyScale.x` 也是负的——
矩形和胶囊的形状其实完全不受符号影响（对称的），但 PhysX 不认，每次这个碰撞体的变换被更新过就喊一声。

1.7 没暴露它，是因为那会儿玩家几乎一直朝右；`PlayerThrow` 带了 `flip: true`，玩家第一次会长期停在反手，
于是这条警告从"偶发"变成"每次投掷之后一直在"。

## 建议修法

在 `FacingApply.Apply` 翻完根节点之后，把**碰撞体那一层**的 x 符号抵消掉：
父节点负责镜像位置（`localPosition` 乘父缩放），节点自己的缩放只影响形状大小，抵消之后判定几何一个数都没变。
`Collider[]` 要在第一次拿到时缓存住（`GetComponentsInChildren<Collider>(true)` 一次就是一次数组分配，
而 `Apply` 每换一次动作就跑一次）。补丁草稿已经写过一遍，13 行：
认全一次 `_shapes`，然后 `s.x = inversed ? -Mathf.Abs(s.x) : Mathf.Abs(s.x)` 逐个写回。

## 为什么当时没直接提上去

工程的闸门是"改完必须 EditMode 全绿 + Play Mode 稳态分区 0 B"。这条修改要过这两道，
而 Unity Editor 当时卡在 Play 模式里（`run_tests` 排在播放模式后面不返回，Pipeline 命令队列被堵住，
`editor_stop` 也进不去），没法验证，所以先登记成问题，等 Editor 能跑闸门时连同验证一起做。
它不影响切片 1.8 的验收判据（稳态 0 B 与逐拍跟随都成立），所以按"不属于当前任务"处理。

## 修好之后要一起看的

- `Assets/Scripts/AGENTS.md` 里"面向与纵深"那条已经写了这条代价与抵消做法，改完之后把这段从"规则"降级成"已实现"就行；
  在那之前，**这条规则暂时不成立**——见该文件对应行的说明。
- 反手出招那几拍的 `BoxPose` 分区应当从非零回到 0，可以顺手把 `Doc/issue/hit-event-heap-alloc.md` 一起对一遍。
