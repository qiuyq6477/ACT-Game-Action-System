using System;
using UnityEngine;

/// <summary>
/// 角色移动的信息
/// </summary>
[Serializable]
public struct MoveInfo
{
    /// <summary>
    /// 最终移动这么多距离
    /// </summary>
    public Vector3 moveDistance;

    /// <summary>
    /// 在这多少个逻辑帧内完成移动
    /// </summary>
    public int inFrames;

    /// <summary>
    /// 移动函数，从 `Methods/ForceMoveMethod` 这张表里按名字取。
    /// 【认不出来 = 一动不动】：`ForceMove.FromData` 落到 `NoMove`，而 `GameData.Load` 会为此喊一条
    /// LogError——这条静默路径能把一个拼写错误藏到永远。空串是合法的，表示"这一记没有位移"。
    /// 【注意有两张撞名的函数表】：`rootMotionTween.method` 走的是 `RootMotionMethod`（那里面有 GoStraight），
    /// 这张表里只有 Slowly / Arc，抄错表就是"打中人不动"
    /// </summary>
    public string tweenMethod;

    public static MoveInfo Zero => new MoveInfo
    {
        moveDistance = Vector3.zero,
        inFrames = 0,
        tweenMethod = ""
    };
}
