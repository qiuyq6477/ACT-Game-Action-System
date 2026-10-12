# 输入调试 UI 每帧重建字符串：Phase 1 之后剩下的最大每帧堆分配

> 状态：未处理 · 2026-10

## 这一条解决什么问题

`Doc/plan/refactor.md` 的 Phase 1 把动作、输入、碰撞三条**每帧执行**的逻辑路径清理成了 0 分配，
但整个游戏的每帧分配并没有归零——剩下最大的一块在调试用的输入面板上。
它是表现层（UI）的代码，跟四个妥协都没有关系，所以按根 `AGENTS.md` 的规则单独登记在这里，没有夹带进 Phase 1 的改动。

## 现在是怎么跑的

调用链只有一处，在每帧的末尾：

- `Assets/Scripts/GameMain.cs:67`：`inputText.text = player.input.InputText();`
- `Assets/Scripts/Character/InputToCommand.cs:240 InputText()`：
  - 先 `new Dictionary<KeyMap, int>`，装 10 个按键的计数；
  - 然后遍历输入历史累加计数；
  - 最后对 10 个键各做一次 `res += keyText + " x " + pair.Value + " 次" + ... + "\n"`。

问题出在第三段。C# 的 `string` 是不可变的，`res += ...` 每次都新建一个字符串对象并把旧内容整个抄一遍：
第一次抄 3 个字符，第二次抄十几二十个……到第十次，前九次生成的字符串全部当场变成垃圾。
粗算下来一帧产生 1 个字典（10 个槽位）、约 10 个中间字符串、若干个数字装箱的对象，量级在 1～3 KB/帧。
数字得由 `Assets/Scripts/GcProbe.cs` 实测补上：InputText 不在探针的任何分区里，
要看它的量，把 `GameMain.cs:67` 那一行临时包一对 `GcProbe.Begin/End`（可以新加一个 Region）就行。

顺带一提，`InputToCommand.cs:150` 的 `int idx = 0;` 也早于本次改动就没被用过（编译器一直在报 CS0219），
它是 `ActionOccur` 早期版本留下的，跟这里说的问题无关，看到了可以顺手删。

## 修的时候建议怎么修

这条不该每帧重画：输入历史变了才需要刷新。两种做法都比现在的强：

1. 用固定的 `StringBuilder` 复用同一块缓冲，拼完 `SetText` 给 UI（改动最小，一帧 0 分配）；
2. 只在 `InputText` 的调用方检测到"本帧有新输入"时才刷新文本（顺便解决文字每帧重画带来的 TMP 重解析开销）。

不管选哪种，都要留一个开关让它在正式包里彻底关掉——这只是个调试面板。
