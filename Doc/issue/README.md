# 已知问题索引

> 状态：规划中 · 2026-10

这里登记"做别的任务时顺带发现、但不属于那个任务"的问题。一个问题一个文件，
修掉之后就删掉文件和下表对应的那一行（详细复盘叙述放 `Doc/bugfix/`，不放这里）。

| 问题 | 现象 | 影响 | 状态 |
| :--- | :--- | :--- | :--- |
| [输入调试UI每帧重建字符串](ui-inputtext-per-frame-alloc.md) | 每帧 new 一个字典并做十次字符串拼接 | Phase 1 之后剩下的最大每帧堆分配 | 未处理 |
| [命中那一拍有堆分配](hit-event-heap-alloc.md) | 打中人的那一帧 `ActionControllerUpdate` 单次最高 4724 B、`CharacterTick` 940 B | 每记一次命中分配一次，群战时是持续的 GC 压力 | 已实施待复验（1.9 把 `ForceMove`/`HitRecord` 转成 struct，并加了 `ChangeAction`/`DamageSettle` 两个分区） |
| [按键采样率受渲染帧限制](input-sample-rate-render-frame.md) | 旧 Input 每渲染帧只 poll 一次，两次 poll 之间按完松开的键从来没被看到过 | 连招手感"偶尔差一帧"的唯一来源，表里 `validInFrames` 留余量就是在兜它 | 未处理（建议与输入改造一起做） |
| [导入的 clip 还带着源工程的 AnimationEvent](clip-animation-events-not-stripped.md) | 每出一记招 Console 就一条 `AnimationEvent 'PlaySFX' … has no receiver` | Console 噪声；以后任何跑到出招的 PlayMode 用例都会被这条 Error 判失败；Step 2 池化 cue 接收者落地后会变成"同一个音效响两次" | 未处理（修法：让 1.4 的导出工具顺手清空，并把"片子里不许有事件"做成报告的可疑值） |
