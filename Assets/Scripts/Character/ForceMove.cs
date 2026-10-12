using System;
using UnityEngine;

/// <summary>
/// 强制位移信息。
/// 【为什么是 struct】：以前它是个 class，每记击退都要 `new` 一个（`GameMain.DoAttack` 末尾的
/// `SetForceMove`），于是"打中人"那一拍带上一次堆分配（切片 1.3 把命中搬进逻辑帧之后，
/// GcProbe 当场把它记了出来，见 `Doc/issue/hit-event-heap-alloc.md`）。
/// 现在整条数据是一个结构体，直接躺在 `CharacterObj` 的字段里，`Advance()` 改的就是那个字段本身。
/// 【结构体的坑】：把它当参数传出去再改，改的是拷贝——所以只有 `CharacterObj._forceMove` 这一份是真源，
/// 传给 tween 函数只读不写。
/// </summary>
public struct ForceMove
{
    public MoveInfo Data;
    /// <summary>
    /// 已经走了多少个逻辑帧，推进只由 Advance 做（逻辑层的时长一律是 int 帧，见 LogicClock）
    /// </summary>
    public int FramesElapsed;
    public int WasFramesElapsed;
    public Func<ForceMove, Vector3> MoveTween;

    public static ForceMove FromData(MoveInfo data)
    {
        ForceMove move = new ForceMove();
        move.Data = data;
        //认不出来的 tweenMethod 就留常量委托：它不捕获任何变量，编译期就缓存成一个静态委托，不分配
        move.MoveTween = ForceMoveMethod.Methods.TryGetValue(data.tweenMethod, out Func<ForceMove, Vector3> tween)
            ? tween
            : ForceMoveMethod.NoMove;
        return move;
    }

    /// <summary>
    /// "没有被强制位移"这个状态：两个计数器都摆在 int.MaxValue，于是 `FramesElapsed <= inFrames` 恒假。
    /// 返回的是结构体，取一次就是一次值拷贝，没有分配
    /// </summary>
    public static ForceMove NoForceMove
    {
        get
        {
            ForceMove move = new ForceMove();
            move.WasFramesElapsed = int.MaxValue;
            move.FramesElapsed = int.MaxValue;
            move.MoveTween = ForceMoveMethod.NoMove;
            return move;
        }
    }

    public void Advance()
    {
        WasFramesElapsed = FramesElapsed;
        FramesElapsed++;
    }
}
