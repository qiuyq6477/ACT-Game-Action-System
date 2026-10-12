using System;

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
}