using System;
using UnityEngine;

/// <summary>
/// 造成的伤害信息
/// </summary>
[Serializable]
public struct AttackInfo
{
    /// <summary>
    /// 一个伤害信息都会有一个phase，也就是证明他是第几段的
    /// </summary>
    public int phase;
    
    /// <summary>
    /// 伤害值，这不一定是一个最终值，他可以是一个倍率，看AttackInfo用在哪儿
    /// 比如用在动作游戏中，就相当于mh的伤害倍率，很好理解，动作游戏都有这个
    /// 精致一些的话会做在每一帧，但既然用了Unity和UE要尊重他们的框架就只能………
    /// 按动作问题也不大，毕竟“玩家好理解”。
    /// </summary>
    public float attack;

    /// <summary>
    /// 这段攻击的方向算是哪儿（会被角色的方向修正得出最终攻击方向）
    /// </summary>
    public ForceDirection forceDir;

    /// <summary>
    /// 推动力，目标会收到这个力的影响
    /// 这是一个ForceMove，不受标准移动力影响
    /// </summary>
    public MoveInfo pushPower;

    /// <summary>
    /// 目标的硬直帧数（逻辑帧）
    /// </summary>
    public int hitStunFrames;

    /// <summary>
    /// 攻击者自身的卡帧帧数（逻辑帧）
    /// </summary>
    public int freezeFrames;

    /// <summary>
    /// 这个攻击在动作变换之前可以命中同一个目标多少次
    /// </summary>
    public int canHitSameTarget;

    /// <summary>
    /// 如果超过1次，那么每2次之间间隔多少个逻辑帧
    /// </summary>
    public int hitSameTargetDelayFrames;
    
    /// <summary>
    /// 命中之后把打中的这个人举起来，最多举多少个逻辑帧。0（默认）= 这一记不抓人。
    /// 【为什么一格就够、不用再开个"是否抓取"的开关】：holdFrames 只有 >0 才有意义，
    /// 两个字段能表达的组合其实只有一个，多一个开关就多一种自相矛盾的填法
    /// 【为什么是 180 而不是"永久"】：源工程用 grabDuration = 3 秒定时撒手
    /// （Time.time 差值），换成逻辑帧就是 180 拍，错频时一样长
    /// </summary>
    public int holdFrames;

    /// <summary>
    /// 被举者相对举人者脚底的偏移（米）：x 是身前（按朝向镜像）、y 是高度、z 是车道（不镜像）。
    /// 【和攻击盒的 track 位置是同一个数】：投掷那一记的盒子要正好罩住手里的人才能打得到他，
    /// 盒子的摆位就是这里的 x/y（再算上被举者自己受击盒的中心高度），两处不一致投掷就会静默打空
    /// </summary>
    public Vector3 holdOffset;

    /// <summary>
    /// 当命中的时候，自身会发生的变化
    /// 这里值得注意的是，这未必是动作的属性，我在这里只是做的“粗糙”了
    /// 精细一点的应该是不同帧不同的攻击框都有不同的值
    /// </summary>
    public ActionChangeInfo selfActionChange;
    
    /// <summary>
    /// 命中时候对手的动作变化
    /// </summary>
    public ActionChangeInfo targetActionChange;
    
    /// <summary>
    /// 如果攻击命中了，就会临时开启一些tempBeCancelledTag，这里用id去索引
    /// </summary>
    public string[] tempBeCancelledTagTurnOn;

}

/// <summary>
/// 可以理解为力的方向，实际上是攻击的判定方向
/// </summary>
[Serializable]
public enum ForceDirection
{
    /// <summary>
    /// 向前的
    /// 在我这个demo里面，说实话方向只有前后，这确实有些偷懒的，就是意思意思吧
    /// 根据动作游戏的具体设计不同，方向枚举自然是不同的。
    /// </summary>
    Forward,
    /// <summary>
    /// 向后的
    /// </summary>
    Backward,
    /// <summary>
    /// 虽然在demo用不上，但是不意打、锁骨割这些Overhead（特指站着攻击却不能蹲防的那些动作）
    /// 都是通过方向来实现的
    /// </summary>
    Overhead,
}