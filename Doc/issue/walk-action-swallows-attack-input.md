# 按住方向键走位时，拳脚输入被走位动作吃掉

> 状态：未处理 · 2026-10

解决的问题：走位和进攻不能同时发生——按住方向键时按拳按脚，输入被整拍丢掉。

现象：按着 D（前进）或 A（后退）的时候按拳、按脚，招完全出不来；松开方向键再按就正常。
影响：清版游戏最核心的"边走边打"今天不成立。这不是八向移动引入的，改造前就存在——只是当时竖轴根本走不动，没人拿方向键去试出拳。

## 为什么会这样（照着能复现推理）

1. 走位动作靠"自己取消自己"来保持连贯。`BoxingMoveForward.commands = [{[6]}]`，只要还按着 →，它每一拍都作为候选出现（`ActionController.Tick` 在 :176-184 扫全表问"谁能取消当前动作"）。
2. 攻击动作同样是候选。`Punch1.commands = [{[100]}]`，它的 `cancelTag.tag` 是 `InitAction`，而 `BoxingMoveForward.beCancelledTag` 唯一的窗口声明的也正是 `InitAction` ⇒ 两个都合格。
3. 打分公式是 `bcTag.priority + cancelTag.priority + action.priority`。两边命中的是**同一个** `bcTag`（那个 `priority: 1` 的窗口，对所有候选同等加成，所以它区分不了候选之间）、`cancelTag.priority` 都没填、`action.priority` 都是 0 ⇒ **完全同分**。
4. 同分时 `ActionController.cs:197-205` 用的是严格 `>`，留下的是先加入的那一个，而加入顺序就是 `Action.json` 的数组顺序 ⇒ `BoxingMoveForward`（第 2 行）永远赢 `Punch1`（第 9 行）。
5. 赢家恰好是当前动作本身，于是走 `keepPlayingAnim` 的 `KeepAction()`：动作不换、`ChangeAction` 根本不执行，那一拍的拳就被丢了。

## 要修该怎么修，以及为什么这次没修

方向是一行的事：给攻击行的 `action.priority` 填 1，让它压过走位的自取消。
但不能顺手就填——`TwinKick`(1)、`TwinKickAutoCombo`(2)、`Hurt`(10)、`Beaten`(11) 已经排在同一条竞争序列里，动攻击行的分会同时改变"连招能不能接上""受击能不能打断出招"，那是连招那一轮（`../../Doc/plan/beatemup-port.md` 的切片 1.6）要一起定的事。

## 八向移动为它做的避让（已知，别再踩回来）

新增的两条纵深走位动作 `BoxingMoveFar` / `BoxingMoveNear` 的 `priority` 填的是 **-1**，所以"只按 ↑/↓ 时按拳"是能出拳的（`Punch1` 得 0 > -1）。
副作用是纵深的平手必须靠这个 -1 定死：按住两轴时 `InputToCommand` 会同时登记单轴记录（6/4/8/2）和斜键记录，"只按 ↑"和"↑+→"在 `keySequence` 里区分不开，所以斜向时得让带横向成分的那条赢——用例 `ForwardDiagonalWalkPicksForwardAction` / `BackwardDiagonalWalkPicksBackwardAction` 钉的就是它。有人把 -1 删了或者重排表，那两条会红。
