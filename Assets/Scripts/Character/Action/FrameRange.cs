using System;

/// <summary>
/// 逻辑帧区间
/// </summary>
[Serializable]
public struct FrameRange
{
    public int minFrame;
    public int maxFrame;

    public FrameRange(int minFrame, int maxFrame)
    {
        this.minFrame = minFrame;
        this.maxFrame = maxFrame;
    }
}