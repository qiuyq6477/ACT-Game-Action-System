using System;
using UnityEngine;

/// <summary>
/// 强制位移信息
/// </summary>
public class ForceMove
{
    public MoveInfo Data;
    /// <summary>
    /// 已经走了多少个逻辑帧，推进只由 Advance 做（逻辑层的时长一律是 int 帧，见 LogicClock）
    /// </summary>
    public int FramesElapsed = 0;
    public int WasFramesElapsed = 0;
    public Func<ForceMove, Vector3> MoveTween = _ => Vector3.zero;

    public static ForceMove FromData(MoveInfo data) => new ForceMove
    {
        Data = data,
        FramesElapsed = 0,
        WasFramesElapsed = 0,
        MoveTween = ForceMoveMethod.Methods.ContainsKey(data.tweenMethod)
            ? ForceMoveMethod.Methods[data.tweenMethod]
            : _ => Vector3.zero
    };

    public static ForceMove NoForceMove => new ForceMove
    {
        Data = new MoveInfo(),
        WasFramesElapsed = int.MaxValue,
        FramesElapsed = int.MaxValue,
        MoveTween = _ => Vector3.zero
    };

    public void Advance()
    {
        WasFramesElapsed = FramesElapsed;
        FramesElapsed++;
    }
}