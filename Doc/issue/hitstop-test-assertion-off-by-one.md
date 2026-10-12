# 重复命中用例的断言常数，从第 449 行起各多算了一拍

> 状态：未处理 · 2026-10

解决的问题：`Assets/Editor/ActionFrameClockTests.cs` 的第九条用例 `RepeatedHitRefreshesHitstopWithoutChangingAction` 是红的，而这条用例正是"改完动作时钟、窗口或顿挫之后必须跑"的那道闸门（见 `Assets/Scripts/AGENTS.md`）。闸门里有一条常红的用例，剩下八条绿不绿就没人信了。

## 结论先说

**实现是对的，用例的期望值错了一格。**不用改产品代码，只改用例里四个常数。

## 现象

```
ActionFrameClockTests.RepeatedHitRefreshesHitstopWithoutChangingAction
  第二次命中之后的7拍同样冻着（没叠成14）
  Expected: 15   But was: 14        ← ActionFrameClockTests.cs:456
```

在 HEAD（`368661d`）上跑也是这个结果，与 BeatEmUp 移植的任何改动无关（做过 A/B：把改动摘掉重跑，同样红）。

## 实测的帧号轨迹

在活编辑器里照该用例的调用序列逐拍打点（`LogicClock.Advance() → input.Tick() → action.Tick()`，`HurtFromBackward` 的 `durationFrames = 18`，全程没换过动作，所以不是自动派生在捣乱）：

| 步骤 | 实际帧号 |
| :--- | :--- |
| 跑到第 12 帧 | 12 |
| `PreorderActionByActionChangeInfo(Keep, …, 7)` 那一拍 | 13 |
| `ApplyHitStop(7)` 之后走满 7 拍 | 13（冻住，符合预期）|
| 第 8 拍 | 14（用例第 446 行在这里是对的）|
| 第二次 `ApplyHitStop(7)`，消耗 2 拍 | 14 |
| 第三次 `ApplyHitStop(7)`，再冻 7 拍 | 14 |
| 下一拍 | 15 |

## 为什么会错

`ApplyHitStop(N)` 的语义是"随后的 N 个逻辑帧里动作时钟一格都不走，第 N+1 拍才前进"。用例前半段（第 436~446 行）按这个模型写，所以绿；从第 449 行开始，作者把第 450、451 那两拍算成了会前进的拍——可那两拍恰恰正被冻着，而"冻着"就是这条用例要钉的行为本身。于是第 449 行之后的四处期望值各多了一拍：

| 位置 | 现在写的 | 应当是 |
| :--- | :--- | :--- |
| 第 456 行 | `frameBefore + 3` | `frameBefore + 2` |
| 第 459 行 | `frameBefore + 4` | `frameBefore + 3` |
| 第 470 行 | `frameBefore + 5` | `frameBefore + 3` |
| 第 473 行 | `frameBefore + 6` | `frameBefore + 4` |

注意第 470/473 那两处属于"还剩几拍时来一个短硬直"那段：按实现，7 拍消耗掉 3 拍之后**剩 4 拍**而不是作者算的 5 拍，`ApplyHitStop(2)` 取较大者所以仍是 4 拍，这跟第一段的偏差是同一个根因。

NUnit 在第一个断言失败处就中断了，所以第 459/470/473 三处从来没被执行过——修的时候要四处一起改完再整条重跑，不要只改第 456 行就交差。

## 为什么不移植任务里顺手改

BeatEmUp 移植的切片 1.1 只动面向的落地方式，与硬直无关。按 `AGENTS.md` 的规矩，任务外发现的问题单独登记、不阻塞、也不夹在无关改动里修。这条要在动顿挫相关的切片（1.3、1.5、1.9）之前单独修掉，否则那道闸门用不了。

登记人：切片 1.1（面向抽象）跑回归时发现。
