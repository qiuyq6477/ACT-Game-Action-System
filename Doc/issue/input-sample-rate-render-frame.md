# 按键采样率受渲染帧限制，快按会被整颗丢掉

> 状态：未处理 · 2026-10

解决的问题：Phase 2 之后，"第几帧按了什么"在逻辑层已经精确到整型帧，但**"什么时候按的"这件事本身**仍然可能记错甚至记不到，这一层任何逻辑帧率都救不回来。

## 现象

Unity 的旧 Input（`Input.GetButton` / `Input.GetAxis`）每**渲染帧**只更新一次状态，所以 `InputToCommand.SampleRenderFrame()` 必须在逻辑时钟之外、每渲染帧调用一次，把这一份快照交给该渲染帧里跑的所有逻辑帧（`Assets/Scripts/Character/InputToCommand.cs`，`GameMain.Update` 的时钟之前）。

于是：

- 30 FPS 下一份快照被两个逻辑帧共用，两次按键如果落在同一个渲染帧里就分不出先后。
- 120 FPS 下有些逻辑帧看到的还是上一帧的快照。
- 最糟的一种：玩家在两次 poll 之间按完并松开（60 FPS 下就是 16.7 毫秒以内），这颗按键**从来没有被看到过**。这不是延迟，是丢失，加大逻辑帧率（例如上 120Hz）也解决不了。
- 方向键用的是 `Input.GetAxis("Horizontal"/"Vertical")`，而 `ProjectSettings/InputManager.asset` 里 Horizontal 的 `sensitivity: 3`——这个值不是 1 就意味着轴是**渐变**的，代码再用 `deadArea 0.2` 卡一下：极短地点一下方向，轴值可能还没爬过阈值就回去了，方向输入同样会整颗丢。

## 后果

动作侧的窗口是按逻辑帧精确的（表里连招窗口最窄的只有 2~3 帧），但输入侧的到达时间有 1~2 个渲染帧的不确定，这就是现在连招手感"偶尔差一帧"的唯一来源。表里 `validInFrames` 一律留 3~6 帧的余量，就是在给这一层兜底。

## 为什么这期不动

Phase 2 的范围是时基、重力和地形；输入设备层是另一件事，而且改法会牵动整个输入结构：

1. 换到 Input System 的 `InputAction` 回调（工程已经装了 `com.unity.inputsystem 1.7.0`），按键变成带时间戳的事件，自己按 `LogicClock.Tick` 量化成帧号，快按就不会丢。
2. 或者留在旧 Input，但每渲染帧扫两次状态、自己维护 down/up 的边沿队列——治标，120Hz 屏上仍然有半颗按键的误差。

建议跟 Phase 3 之后的输入改造一起做。登记人：Phase 2 第 1 步实施过程中确认，与当次任务无关，按 `AGENTS.md` 的规则单独登记不阻塞进度。
