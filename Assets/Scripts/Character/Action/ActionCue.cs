using System;

/// <summary>
/// 帧事件（BeatEmUp 的 AnimationEvent）在本工程里的种类。
/// 六个值一一对应源工程 clip 里出现过的六个函数（PlaySFX / ShowEffect / CamShake /
/// Footstep / AddForce / SpawnProjectile），不是照着想象凑的清单。
/// 源工程把它们写成"动画里调方法"，本工程把它们挪进表里由逻辑帧派发（`Doc/decision/0001`），
/// 于是"第 4 帧响的挥空声"这种数据第一次变成了可以 diff、可以测的东西。
/// </summary>
public enum CueKind
{
    /// <summary>音效。param = 源工程的声音名（Whoosh、Land…）</summary>
    Sfx,
    /// <summary>特效。param = 特效名（SmokePuffJump、DefendEffect…）</summary>
    Effect,
    /// <summary>镜头震动。源工程的 CamShake 不带参数</summary>
    CameraShake,
    /// <summary>脚步音</summary>
    Footstep,
    /// <summary>给自身一个力（源工程用它做落地弹起之类的一下子位移）。本工程走 ForceMove</summary>
    Force,
    /// <summary>生成弹射物。param = 弹射物名（KnifeProjectile…）</summary>
    Projectile,
}

/// <summary>
/// 一条帧事件：本工程的第几拍、什么种类、带什么参数。
///
/// 【param 为什么还是字符串】：热路径禁止比字符串的那条规矩针对的是每帧都跑的判定
/// （见 `Assets/Scripts/AGENTS.md`），而一条 cue 一个动作里最多响几次，
/// 拿它去查音效表/特效表是一次字典查找，不分配。真烘焙成 id 反而要把所有名字先注册一遍，
/// 而名字的唯一消费者就是那两个表。
/// </summary>
[Serializable]
public struct ActionCue
{
    /// <summary>派发的拍号（0-based 姿势帧号与拍号恒等，见导出工具的头注释）</summary>
    public int frame;

    public CueKind kind;

    /// <summary>种类相关的名字，没有就留空</summary>
    public string param;
}
