# 命中那一拍有堆分配（击退、命中记录、换动作）

> 状态：未处理 · 2026-10

解决的问题：Phase 1 把逻辑热路径压到了每帧 0 Byte，但"打中人了"这一拍走的是另一条路径，
它从来没被测到过——因为判定盒错频时窄窗口经常静默打空。切片 1.3 把命中改成按逻辑帧主动采样之后，
这一拍终于每帧都发生，Play Mode 的 GcProbe 当场把它记了出来。

## 现象

Play Mode 里打出一记 Punch1 命中敌兵，那一帧的 GcProbe 报告：

| 分区 | 均值 | 单次最大 |
| :--- | :--- | :--- |
| `ActionControllerUpdate` | 75 B | 4724 B |
| `CharacterTick` | 4 B | 940 B |
| `SampleOverlaps` | 0 B | 0 B |

不打好吗的那几个窗口，九个分区全是 `avg=0 max=0`——所以这些字节确实只出现在命中那一拍。

## 来源

工程自己 `new` 的有两处，都在命中结算的下游：

- `Assets/Scripts/Character/ForceMove.cs:17` `FromData`：`ForceMove` 是 class，
  一次击退就分配一个对象（`GameMain.DoAttack` 末尾 `defender.SetForceMove(new MoveInfo{…})` 调它）。
- `CharacterObj.AddHitRecord`：`HitRecords.Add(new HitRecord(…))`，每条命中记录一个对象。

剩下那几千字节大概率来自 Animator 换状态（受击方切到 `HurtFromForward` 那一拍，
`ActionController.Tick` 的区间里会走 `anim.Update`），属于引擎内部，不由我们控制。

## 影响

不是每帧分配，所以没有违反"热路径 0 Byte"那条规则；它是**每记一次命中一次**的分配。
单挑时看不出来，清版游戏同屏十几个敌兵一起挨打就是持续的 GC 压力，
GC 一来就要回收，回收一卡就是几十毫秒——而逻辑时钟的设计初衷恰恰是不许卡。

## 建议怎么修

Phase 3 / `Doc/plan/beatemup-port.md` 的切片 1.9 本来就要重做伤害结算，顺手一起处理：

1. `ForceMove` 改成 struct 存进 `CharacterObj`（它只有两个 int、一个 MoveInfo 结构、一个委托），
   或者把 `MoveTween` 从"每对象一个委托"改成"按 tweenMethod 查函数表"，那样连委托都能省掉。
2. `HitRecord` 换成对象池，或者干脆做成 `CharacterObj` 上预分配的定长数组（一个动作最多几个阶段是已知的）。
3. Animator 那部分先量准再说：`GcProbe` 加一个 `ChangeAction` 分区，把"引擎给的"和"我们写的"分开算，
   不然永远在猜。

登记人：切片 1.3（命中按逻辑帧采样）验证时，Play Mode 的 GcProbe 报出来的。与当次任务无关，
按 `AGENTS.md` 的规则单独登记，不阻塞进度。
