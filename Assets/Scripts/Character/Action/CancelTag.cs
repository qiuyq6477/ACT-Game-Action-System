using System;

/// <summary>
/// CancelTag 和 BeCancelledTag 是一对用于动作切换的核心逻辑数据（帧单位）
/// </summary>
[Serializable]
public struct CancelTag
{
    /// <summary>
    /// 这个tag的字符串，可以理解为id
    /// </summary>
    public string tag;

    /// <summary>
    /// 这个动作会从第几帧开始播放
    /// </summary>
    public int startFromFrame;

    /// <summary>
    /// 动画融合进来的帧数长度
    /// </summary>
    public int fadeInFrames;
    
    /// <summary>
    /// 当从这里Cancel动作时，优先级变化
    /// </summary>
    public int priority;
}

[Serializable]
public struct BeCancelledTag
{
    /// <summary>
    /// 逻辑帧区间
    /// </summary>
    public FrameRange frameRange;

    /// <summary>
    /// 可以Cancel的CancelTag
    /// </summary>
    public string[] cancelTag;

    /// <summary>
    /// 动画融合出去的帧数
    /// </summary>
    public int fadeOutFrames;
    
    /// <summary>
    /// 当从这里被Cancel，动作会增加多少优先级
    /// </summary>
    public int priority;

    /// <summary>
    /// 根据 TempBeCancelledTag 和产生这个 Tag 的逻辑帧时间点，算出一个新的 BeCancelledTag
    /// </summary>
    public static BeCancelledTag FromTemp(TempBeCancelledTag tempTag, int fromFrame) => new BeCancelledTag
    {
        frameRange = new FrameRange(fromFrame, fromFrame + tempTag.durationFrames),
        cancelTag = tempTag.cancelTag,
        fadeOutFrames = tempTag.fadeOutFrames,
        priority = tempTag.priority
    };
}

[Serializable]
public struct TempBeCancelledTag
{
    /// <summary>
    /// 因为需要被索引，所以需要一个id
    /// </summary>
    public string id;
    
    /// <summary>
    /// 开启的帧数长度
    /// </summary>
    public int durationFrames;
    
    /// <summary>
    /// 可以Cancel的CancelTag
    /// </summary>
    public string[] cancelTag;

    /// <summary>
    /// 动画融合出去的帧数
    /// </summary>
    public int fadeOutFrames;
    
    /// <summary>
    /// 当从这里被Cancel，动作会增加多少优先级
    /// </summary>
    public int priority;
}