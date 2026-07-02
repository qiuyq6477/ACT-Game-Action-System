using System;

/// <summary>
/// 开启一些受击盒子的信息（帧单位）
/// 可以理解为“一部分防御”
/// </summary>
[Serializable]
public struct BeHitBoxTurnOnInfo
{
    /// <summary>
    /// 开启的帧区域，可以分为多段开启
    /// </summary>
    public FrameRange[] inFrames;
    
    /// <summary>
    /// 要开启的盒子的tag
    /// </summary>
    public string[] tag;

    /// <summary>
    /// 这样开启的盒子，优先级会发生怎样的临时变化
    /// </summary>
    public int priority;
    
    /// <summary>
    /// 如果命中了这里的受击框，就会临时开启一些tempBeCancelledTag，这里用id去索引
    /// </summary>
    public string[] tempBeCancelledTagTurnOn;
    
    /// <summary>
    /// 与攻击框不同，受击框本身会决定这次受到攻击的时候双方的动作。
    /// </summary>
    public ActionChangeInfo attackerActionChange;
    
    /// <summary>
    /// 与攻击框不同，受击框本身会决定这次受到攻击的时候双方的动作。
    /// </summary>
    public ActionChangeInfo selfActionChange;
}
