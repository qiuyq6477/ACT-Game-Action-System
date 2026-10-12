using System;
using UnityEngine;

/// <summary>
/// 帧事件的派发口。逻辑层只负责"第几拍响了哪一条"，谁去放声音、开特效、抖镜头由表现层注册进来。
///
/// 【为什么是一个静态委托而不是事件/接口注入】：这条路径每动作跑几次，不是每帧，
/// 但它在 `ActionController.Tick` 里面，所以照样不许分配——`Handler(info, cue)` 直接调用目标方法，
/// 既没有装箱也没有临时对象；用 `+=` 的 C# event 也是同一个委托链，语义上没多什么，
/// 却会让人以为可以挂一堆监听者然后把结算顺序搞乱。本工程只允许一个消费者（`GameMain`）。
///
/// 【注册方必须池化】：源工程每响一次就 `new GameObject + AudioSource`（特效同理），
/// 照搬进来就撞热路径 0 分配的红线——这是 `Doc/plan/beatemup-port.md` 第五节把 cue 派发
/// 与对象池绑在一起的原因。切片 1.7 接素材时实现真正的接收者。
/// </summary>
public static class CueDispatch
{
    /// <summary>
    /// 唯一的消费者。没注册的时候 cue 照样按帧求值（用例钉的就是这个时机），
    /// 只是没人听——空着不等于出错，表里有 cue 而没人处理也不该刷日志，
    /// 因为一段动作有几条 cue 是策划决定的事
    /// </summary>
    public static Action<ActionInfo, ActionCue> Handler;

    public static void Fire(ActionInfo info, ActionCue cue)
    {
        Action<ActionInfo, ActionCue> handler = Handler;
        if (handler == null) return;
        handler(info, cue);
    }
}
