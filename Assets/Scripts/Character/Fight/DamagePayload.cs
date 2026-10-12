using UnityEngine;

/// <summary>
/// 一记命中结算完之后"这一下到底有多重"的结论，全部是值类型，躺在调用栈上。
/// 【为什么是 struct】：它每次命中生成一次、只活过那一拍。做成 class 就给"打中人"那一拍加了
/// 一次堆分配，而那一拍本来是一帧里最忙的时候（同 HitRecord、ForceMove 的理由，
/// 见 Doc/issue/hit-event-heap-alloc.md）。
/// 【为什么不只让受击方自己算】：同一份结论有三个读者——受击方要拿它决定要不要打断自己、
/// 攻击方要拿它决定卡肉、以后的飘字和音效要拿它决定响什么。让它当返回值，三个读者读同一次结算。
/// </summary>
public struct DamagePayload
{
    /// <summary>
    /// 最终掉血：已经乘过攻击盒倍率、攻击力、肉质，也减过防御。格挡是 0
    /// </summary>
    public float Damage;

    /// <summary>
    /// 削韧点数（表里的 poiseDamage）。格挡是 0
    /// </summary>
    public float PoiseDamage;

    /// <summary>
    /// 这一记被正面格挡了
    /// </summary>
    public bool Blocked;

    /// <summary>
    /// 这一记要不要打断受击方当前的动作。
    /// 【真】普通命中、破霸体、以及受击方压根没装属性组件（老 demo 角色一字不改的老行为）；
    /// 【假】被格挡、以及霸体硬抗（韧性没被打空）
    /// </summary>
    public bool Interrupts;

    /// <summary>
    /// 打完这一记受击方就死了
    /// </summary>
    public bool Lethal;

    /// <summary>
    /// 受击方该硬直多少逻辑帧：普通命中=hitStunFrames，格挡=blockStunFrames，霸体硬抗=0
    /// </summary>
    public int StunFrames;

    /// <summary>
    /// 受击方该被推开多少：普通命中=pushPower，格挡=blockPush，霸体硬抗=MoveInfo.Zero
    /// </summary>
    public MoveInfo Push;
}
