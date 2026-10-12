using System;

/// <summary>
/// CancelTag和BeCancelledTag是一对用于动作切换的核心数据
/// 在Unity或者UE的Montage中，我们都用指定一段时间，在这段时间内
/// 开启BeCancelledTag，让CancelTag对应的动作有可能在这段时间内可以Cancel当前动作
/// </summary>
[Serializable]
public struct CancelTag
{
    /// <summary>
    /// 这个tag的字符串，可以理解为id
    /// </summary>
    public string tag;

    /// <summary>
    /// 这个动作会从第几个逻辑帧开始播放
    /// </summary>
    public int startFromFrame;

    /// <summary>
    /// 动画融合进来的帧数，以本动作的时长为标尺
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
    /// 时间段（逻辑帧）
    /// </summary>
    public FrameRange cancelWindowFrames;

    /// <summary>
    /// 可以Cancel的CancelTag
    /// </summary>
    public string[] cancelTag;

    /// <summary>
    /// 动画融合出去的帧数，以挂着这个tag的动作自己的时长为标尺
    /// </summary>
    public int fadeOutFrames;
    
    /// <summary>
    /// 当从这里被Cancel，动作会增加多少优先级
    /// </summary>
    public int priority;

    /// <summary>
    /// 根据TempBeCancelledTag和产生这个Tag的那一帧，算出一个新的BeCancelledTag
    /// </summary>
    /// <param name="tempTag">表里配好的临时tag</param>
    /// <param name="fromFrame">从当前动作的第几帧开始往后开</param>
    /// <param name="durationFrames">当前动作的总帧数，用来钳区间</param>
    /// <returns></returns>
    public static BeCancelledTag FromTemp(TempBeCancelledTag tempTag, int fromFrame, int durationFrames) => new BeCancelledTag
    {
        cancelWindowFrames = new FrameRange(fromFrame, fromFrame + tempTag.lengthFrames, durationFrames),
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
    /// 在当前动作中，从开启的那一帧往后开多少个逻辑帧
    /// </summary>
    public int lengthFrames;
    
    /// <summary>
    /// 可以Cancel的CancelTag
    /// </summary>
    public string[] cancelTag;

    /// <summary>
    /// 动画融合出去的帧数，以当前动作的时长为标尺
    /// </summary>
    public int fadeOutFrames;
    
    /// <summary>
    /// 当从这里被Cancel，动作会增加多少优先级
    /// </summary>
    public int priority;
}
