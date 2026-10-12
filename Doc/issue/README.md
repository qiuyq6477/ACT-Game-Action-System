# 已知问题索引

> 状态：规划中 · 2026-10

这里登记"做别的任务时顺带发现、但不属于那个任务"的问题。一个问题一个文件，
修掉之后就删掉文件和下表对应的那一行（详细复盘叙述放 `Doc/bugfix/`，不放这里）。

| 问题 | 现象 | 影响 | 状态 |
| :--- | :--- | :--- | :--- |
| [输入调试UI每帧重建字符串](ui-inputtext-per-frame-alloc.md) | 每帧 new 一个字典并做十次字符串拼接 | Phase 1 之后剩下的最大每帧堆分配 | 未处理 |
| [命中"相交"的粒度是物理步，不是逻辑帧](hit-overlap-physics-step-granularity.md) | 盒子开关在 60Hz 逻辑帧上，`OnTriggerEnter` 却按 `Fixed Timestep 0.02`（50Hz）报 | 窄窗口、大幅位移下命中静默打空，且换渲染帧率测不出来 | 未处理（等 Phase 3 定形态） |
| [按键采样率受渲染帧限制](input-sample-rate-render-frame.md) | 旧 Input 每渲染帧只 poll 一次，两次 poll 之间按完松开的键从来没被看到过 | 连招手感"偶尔差一帧"的唯一来源，表里 `validInFrames` 留余量就是在兜它 | 未处理（建议与输入改造一起做） |
