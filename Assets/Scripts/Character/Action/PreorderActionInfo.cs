/// <summary>
/// 预约一个Action，作为下一个Action的候选人
/// </summary>
public struct PreorderActionInfo
{
    /// <summary>
    /// 这个action的id
    /// </summary>
    public string ActionId;

    /// <summary>
    /// 这条预约信息的优先级，最后冒泡出来最高的就是要换成的
    /// </summary>
    public int Priority;

    /// <summary>
    /// 动画融合长度，已经是"占目标动作时长的比例"，直接喂CrossFade。
    /// 【为什么不是逻辑帧】：它是表现层API的实参量纲；算它要把被取消动作的fadeOut和新动作的fadeIn取小，
    /// 两者各以自己的时长为标尺，只有归一化后才可比，再乘回目标时长只是除一遍又乘一遍。
    /// </summary>
    public float TransitionNormalized;

    /// <summary>
    /// 新的action对应的动画，从第几个逻辑帧开始播放
    /// </summary>
    public int FromFrame;

    /// <summary>
    /// 当切换动作后，会立即硬直多久
    /// 值得一提的是：这玩意儿在这个demo是个凑效果的东西，他的准确做法并不是这样的。
    /// 我们需要这个值在这个demo里，是为了挨揍之后的hitStun，也就是挨揍动作做完之后，要卡一会，增加打击感。
    /// 这个“凑效果”的做法，虽然可以做大卡顿而提高打击感，也就是大多国产动作游戏水平。
    /// 但实际上这是不够的，比如街霸（steam上可以玩到5和6）等游戏，他们受击的时候，角色卡顿的时候还会有个抖动。
    /// 这个抖动产生了角色受到力后冲击的打击感，这种打击感的效果，是类似于UE的Montage的做法（RE中也提供了相应的功能，这里只用大家容易接触到的UE说）
    /// 就是做个专门的Montage来让整个角色随着受击（左、右，或者说前、后）方向抖动，然后把这个montage“盖上”
    /// 同时让硬直也发生在这个montage里面，就有这个效果了。
    /// </summary>
    public int FreezingAfterChangeAction;

    public PreorderActionInfo(string actionId, int priority = 0, float transitionNormalized = 0,
        int fromFrame = 0, int freezingAfterChangeAction = 0)
    {
        ActionId = actionId;
        Priority = priority;
        TransitionNormalized = transitionNormalized;
        FromFrame = fromFrame;
        FreezingAfterChangeAction = freezingAfterChangeAction;
    }

}