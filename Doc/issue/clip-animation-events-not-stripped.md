# 导入的 clip 还带着源工程的 AnimationEvent，每次出招都喊一条"no receiver"

> 状态：未处理 · 2026-10

解决的问题：帧事件的真源已经在 1.5 换成了招式表里的 `cues`（见 `Doc/decision/0001-帧数据真源在表不在动画.md`），
但**片子自己身上那些 legacy `AnimationEvent` 没有被摘掉**，于是 Animator 每求值一次就照着旧地址喊一嗓子。

## 现象

批处理 Play 探针（跑真实的 `Assets/Scenes/GameMain.unity`）里，玩家每出一记 `PlayerPunch1` 就有一条 Error：

```
'BeatEmUpPlayer' AnimationEvent 'PlaySFX' on animation 'PlayerPunch1' has no receiver! Are you missing a component?
UnityEngine.Animator:Update (single)
ActionController:Tick () (at Assets/Scripts/Character/Action/ActionController.cs:185)
GameMain:TickAll () (at Assets/Scripts/GameMain.cs:127)
```

调用栈很明确：是我们自己每逻辑帧一次的那次 `anim.Update(LogicClock.Tick)` 把事件踩出来的，不是物理回调之类的旁路。

## 根因

BeatEmUp 的 `.anim` 里本来就挂着 `AnimationEvent`（源工程用它们放音效与特效），1.4 的导出工具
把 `AnimationEvents` 读成表里的 `cues` 行，但**只读不删**——导入进来的片子仍然带着原样的事件。
源工程里这些事件有接收者（`PlayerSfx` 之类的组件），本工程没有搬那些组件，于是每次踩到都报 no receiver。

## 影响

1. **Console 噪声**：一记攻击一条 Error，出招越密集越糊。
2. **Play Mode 用例会被它绊倒**：Unity 的测试框架把测试期间出现的 Error 日志算失败，
   所以以后任何"跑到玩家出招"的 PlayMode 用例都会因为这条无关的 Error 而红。
3. **Step 2 会把它变成真 bug**：cue 的池化接收者（音效/特效）落地之后，
   同一记挥拳的同一个音效会响两次——一次来自片子里的旧事件（如果那时顺手补了个接收组件），
   一次来自表。真源只能有一个。

## 建议怎么修

一次性把导入进来的 clip 的 `AnimationEvent` 清空，并让它成为**导出流程的一部分**而不是手工活：

- 在 `Assets/Editor/AnimClipToActionTable.cs`（1.4 那个工具）里，读完 `clip.events` 之后
  顺手 `clip.events = new AnimationEvent[0]` 并 `EditorUtility.SetDirty(clip)` + `AssetDatabase.SaveAssets()`；
  工具本来就要跑一遍所有片子，顺手清最省事。
- 同时把"片子里不该有 AnimationEvent"做成导出报告里的一条可疑值（现在报告只查空窗口/越界窗口那一类），
  这样美术重导片子之后不一致会立刻显形——跟 `AGENTS.md` 里"表 vs clip 曲线不一致要做成断言"是同一条要求。

## 备注

发现于切片 1.9 的验收（批处理 Play 探针），与"伤害/霸体/格挡"这件事本身无关，
按 `AGENTS.md` 的规则单独登记、不阻塞当次任务。
