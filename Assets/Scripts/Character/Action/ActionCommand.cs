using System;

/// <summary>
/// 根据命令得出的操作
/// </summary>
[Serializable]
public struct ActionCommand
{
    /// <summary>
    /// 按键顺序
    /// </summary>
    public KeyMap[] keySequence;

    /// <summary>
    /// 检查的按键最远的一次操作距离现在多少个逻辑帧。
    /// 0 表示只看最短的那一档（实际回看一下个逻辑帧，见 InputToCommand.ActionOccur）
    /// </summary>
    public int validInFrames;
}


