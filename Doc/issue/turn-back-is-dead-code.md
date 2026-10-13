# TurnBack 抢不到动作，角色永远不转身

> 状态：未处理 · 2026-10

解决的问题：角色永远不转身，整套正反手逻辑（输入镜像、位移符号、受力翻转）实际上一路没被跑到过。

现象：无论后退多久，角色都不会转身朝左；一次运行里 `InputToCommand.inversed` 恒为 `false`。
影响比看上去大：全工程**只有** `ActionController.cs:400` 的 `if (aInfo.flip) command.inversed = !command.inversed;` 会写这个布尔，而带 `flip: true` 的表行只有 `TurnBack` 一条。它抢不到 ⇒ 三处依赖它的逻辑全是死的：输入镜像（`GetKeyInput` 的 Forward↔Backward）、`NatureMove` 的横向符号、`GameMain.GetForceDirection` 的受力翻转，表现层的 `FacingApply` 也永远只摆一个朝向。

## 为什么会这样（照着能复现推理）

1. `TurnBack.commands = [{[4]}]`（按后退触发），但它的 `cancelTag.tag` 是 `"TurnBack"`——只有**当前动作**的 `beCancelledTag` 里出现这个标签时它才算合格。
2. 全表只有 `BoxingMoveBackward.beCancelledTag[1] = {窗口 [39,48], cancelTag:["TurnBack"]}` 声明了它。也就是说表作者的设计是"后退走满 39 拍才允许转身"，这个设计本身没问题。
3. 走到那三拍时，`BoxingMoveBackward` 自己也在候选里：它的 `cancelTag:"InitAction"` 命中同一动作的 `beCancelledTag[0]`（窗口 0-48），而命令 `[4]` 也还按着。
4. 两者同分：两个窗口的 `priority` 都没填（0）、两条 `cancelTag.priority` 都没填、两条 `action.priority` 都没填 ⇒ 平手取数组靠前的 ⇒ `BoxingMoveBackward`（第 3 行）赢 `TurnBack`（第 8 行）。
   注意 `CanActionCancelCurrent` 是外层遍历 `beCancelledTag`、命中就 return，所以 `TurnBack` 候选拿到的 `bcTag` 确实是那个 [39,48] 窗口——分还是 0，问题只在平手。

## 要修该怎么修

把 `BoxingMoveBackward.beCancelledTag` 里那条 TurnBack 窗口的 `priority` 填 1：这是窗口自己的分，只把"转身"这一路抬起来，不影响其它取消。

修的时候要注意：`inversed` 会被真的翻动，这是 `Doc/decision/0002-HD-2D世界约定.md` 里"纵深不镜像、横向镜像"那条性质第一次被实际跑到（现在只有 `DepthDoesNotMirrorWhenFacingLeft` 手工设 `_character.Inversed` 在测）。所以这一条要单独一轮做，并且把所有跟面向有关的用例一起重跑，不要塞进移动改动里。
