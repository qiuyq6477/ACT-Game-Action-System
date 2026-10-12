using System;
using UnityEngine;

/// <summary>
/// 逻辑帧区间。
/// 动作内部的窗口在表里存的是绝对帧号（"这一脚的命中框在第4到第5帧"），不是归一化百分比，
/// 所以判定只剩整数比较，动作编辑器里存回什么数就是什么数，不会有 0.35 / 0.35000001 这种复现不出来的值。
/// </summary>
[Serializable]
public struct FrameRange
{
    public int min;
    public int max;

    /// <summary>
    /// 钳到 [0, durationFrames]。
    /// 【注意】只有代码现场构造的区间会走这里——JsonUtility.FromJson 不调构造函数，
    /// 表里直接填的区间不经过任何钳制，填错了靠 GameData.Load 的时长校验兜底。
    /// </summary>
    public FrameRange(int minFrame, int maxFrame, int durationFrames)
    {
        min = Mathf.Clamp(minFrame, 0, durationFrames);
        max = Mathf.Clamp(maxFrame, 0, durationFrames);
    }
}
