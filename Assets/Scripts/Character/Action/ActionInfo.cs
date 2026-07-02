using System;

/// <summary>
/// 阶段性输入允许的逻辑帧区间
/// </summary>
[Serializable]
public struct MoveInputAcceptance
{
    /// <summary>
    /// 在逻辑帧区间
    /// </summary>
    public FrameRange range;
    /// <summary>
    /// 允许移动的比例 (0.0f - 1.0f)
    /// </summary>
    public float rate;
}

/// <summary>
/// 角色的一个动作逻辑数据（完全以 60Hz 帧为单位运行，对齐 JSON 反序列化）
/// </summary>
[Serializable]
public struct ActionInfo
{
    /// <summary>
    /// 一个动作的id，每个动作都有一个id，必须是唯一的，同名的action会互相覆盖
    /// 也因为同名的action互相覆盖，所以才可以好好利用这个来做一些玩法，比如MHR中的替换技
    /// </summary>
    public string id;

    /// <summary>
    /// 绑定的动画剪辑名称/状态名
    /// </summary>
    public string animKey;

    /// <summary>
    /// 动作的分类
    /// </summary>
    public string catalog;

    /// <summary>
    /// 该动作的最大逻辑帧数 (AnimationClip.length * 60)
    /// </summary>
    public int maxFrames;

    /// <summary>
    /// 这个动作的 Cancel 连招信息
    /// </summary>
    public CancelTag[] cancelTag;

    /// <summary>
    /// 这个动作可以被 Cancel 的被动配置
    /// </summary>
    public BeCancelledTag[] beCancelledTag;

    /// <summary>
    /// 临时的被 Cancel 信息（如命中时开启）
    /// </summary>
    public TempBeCancelledTag[] tempBeCancelledTag;

    /// <summary>
    /// 触发此动作需要的按键指令
    /// </summary>
    public ActionCommand[] commands;

    /// <summary>
    /// 本动作期间允许移动输入的比例控制
    /// </summary>
    public MoveInputAcceptance[] inputAcceptance;

    /// <summary>
    /// 下一个动作的id
    /// 这个id是当动作自然播放完毕之后转向的那个动作，所以必须是一个严格的id
    /// 【注意1】
    /// 在标准的动作游戏中，所有的问题是“下一帧是什么”，所以应该是autoNextFrame，但是我们都被Unity和UE教化了
    /// 或者说不服从于Unity和UE的规范，我们要花更多的时间去搞定用帧而非Update的正确做法，在这个demo里面就不去做了
    /// 所以我们用类似的手法，设计这个autoNextActionId，也就是动作播放完毕之后自动换成什么动作，也更符合现代人的理解
    /// 【注意2】
    /// 由此，你应该发现，类似怪物猎人的斩击斧，是不需要一个状态记录剑形态和斧形态的
    /// 正如拔刀和非拔刀一样，利用好这个autoNextActionId就能做到，比如RT的拔刀动作autoNextActionId是剑形态站立
    /// 而三角的拔刀动作的autoNextActionId是斧形态站立，就能产生出这个效果。
    /// 【注意3】
    /// 类似街霸的格斗游戏中，蹲下这个动作的autoNextActionId应该等于站立动作的
    /// 之所以蹲着，是因为按住了下，导致下蹲动作自己cancel了自己，所以保持蹲着
    /// 因为同一个动作cancel自己，虽然会导致逻辑上这个动作从头开始了，但是由于播放动画走的是Update而非逻辑帧，所以播放动作是单独的，他可以继续播放下去
    /// 所以才会看起来蹲是保持的（因为动作是连贯的，而非重新开始播放），但实际上确实是“新动作”，当然动作游戏里面的核心问题还是“下一帧”。
    /// </summary>
    public string autoNextActionId;

    /// <summary>
    /// 档切换到自己这个动作的时候，是否保持继续播放
    /// 这个意思是：比如移动会被移动自己cancel，这时候移动动作应该继续播放，而不是重置，所以要有这个true
    /// 原本在帧为单位的时候，cancel关系都是nextFrame所以可以通过frame之间的连接关系来，现在要以动作为单位，就靠这个凑出这个效果了
    /// </summary>
    public bool keepPlayingAnim;

    /// <summary>
    /// 是否当没有收到命令的时候，就自动走向autoNext了
    /// 这并不是一个好的做法，正确的做法，应该是在动作过程中设置某些帧
    /// 在这些帧去判断是否还有对应的command，如果没有了就终止了
    /// UE的Montage里面可以用多个NotifyState分布在动画过程
    /// Unity的话得自己做个编辑工具，所以我这个demo就先偷懒了——如果true，就是每一帧都做这个检查
    /// 其实从逻辑结构来说，我可以写成一个float[]，每个float代表一个percentage检查一下
    /// 但是这个填表………不是地球人可以轻易做到的（烦得很）………所以就先偷懒了，但是意思是一样的
    /// </summary>
    public bool autoTerminate;
    
    /// <summary>
    /// 造成的伤害数据信息
    /// </summary>
    public AttackInfo[] attacks;

    /// <summary>
    /// 每一段攻击判定盒的开启帧范围
    /// </summary>
    public AttackBoxTurnOnInfo[] attackPhase;

    /// <summary>
    /// 受击判定盒的开启帧范围
    /// </summary>
    public BeHitBoxTurnOnInfo[] defensePhase;

    /// <summary>
    /// 动作期间的逻辑位移函数名称
    /// </summary>
    public ScriptMethodInfo rootMotionTween;

    /// <summary>
    /// 动作基础优先级
    /// </summary>
    public int priority;

    /// <summary>
    /// 是否翻转角色的朝向，在这个demo里面，角色面向是一个严肃的属性
    /// 并不是每个游戏的角色面向都是如此严肃的，具体看游戏设计
    /// 所以有些动作会改变角色的面向，他未必是转身动画，而是可能动作中转身、再转身
    /// 而转身到再转身之间的那段的cancel你输入招式就要反手搓，愚蠢的欧美人认为这就叫操作
    /// 在这个游戏里面，角色是会后退一段路然后转身的，这是一种动作游戏的风格
    /// 如果采用这种风格，最好有一些攻击动作是有转身版的，虽然玩家理解是同一个动作，比如kick
    /// 但实际上按住后再按kick和按kick本来就是两个动作了对吧，他们只是“大部分相似”而已
    /// </summary>
    public bool flip;
}

/// <summary>
/// 用于从 JSON 序列化加载 ActionInfo 的容器
/// </summary>
[Serializable]
public class ActionInfoContainer
{
    public ActionInfo[] data;
}