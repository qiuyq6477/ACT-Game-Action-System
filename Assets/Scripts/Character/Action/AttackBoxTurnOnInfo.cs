using System;
using UnityEngine;

/// <summary>
/// 攻击盒开启的信息
/// 可以视为“一段攻击”
/// </summary>
[Serializable]
public struct AttackBoxTurnOnInfo
{
    /// <summary>
    /// 开启的时间段（逻辑帧），可以分为多段
    /// </summary>
    public FrameRange[] inFrames;

    /// <summary>
    /// 要开启的攻击盒的tag
    /// </summary>
    public string[] tag;

    /// <summary>
    /// 上面那组tag在 GameData.Load 里烘焙成的位掩码，运行期只用它做交集判断。
    /// 【不是填表数据】：JsonUtility 会跳过 [NonSerialized]，表里不用填
    /// </summary>
    [NonSerialized] public int tagMask;

    /// <summary>
    /// 这段攻击的逻辑数据是ActionInfo中的哪个AttackInfo
    /// </summary>
    public int attackPhase;
    
    /// <summary>
    /// 这样开启的盒子，优先级会发生怎样的临时变化
    /// </summary>
    public int priority;

    /// <summary>
    /// 这段攻击里盒子的轨迹（稀疏关键帧，只在盒子动过的帧上填）。
    /// 【为什么窗口之外还要管位置】：源工程（BeatEmUp）全场只有一只判定盒，每招靠 .anim 里的
    /// 位置/缩放曲线把它挪到拳头或脚上。本工程的真源在表里（`Doc/decision/0001`），
    /// 于是"盒子在第几帧开"和"盒子第几帧在哪"必须一起进表——
    /// 否则窗口在表里、位置在动画里，"这一拳在哪一帧打到谁"仍然是半真半假，改动画就悄悄改了判定。
    /// 留空就是不动：盒子待在 prefab 里摆好的位置（本工程原来的做法）
    /// </summary>
    public AttackBoxKey[] track;

    /// <summary>
    /// track 在 GameData.Load 里烘焙成的按帧号直取的位置，null = 这条表项没有轨迹。
    /// 【不是填表数据】：JsonUtility 会跳过 [NonSerialized]，表里不用填
    /// </summary>
    [NonSerialized] public Vector3[] bakedPosition;

    /// <summary>
    /// 同上，烘焙出来的按帧号直取的尺寸（直接喂给 BoxCollider.size）
    /// </summary>
    [NonSerialized] public Vector3[] bakedSize;
}