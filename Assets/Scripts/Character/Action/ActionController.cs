using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 动作管理器，是一个核心组件
/// </summary>
public class ActionController : MonoBehaviour
{
    /// <summary>
    /// 角色的animator，要通过这个来播放角色动作的
    /// </summary>
    [Tooltip("角色的animator")] public Animator anim;

    /// <summary>
    /// 即使不是玩家控制，也可以有这个组件，ai也可以通过发送操作指令来驱动角色
    /// 尽管ai有aiCommand这个组件
    /// </summary>
    [Tooltip("指令输入的input")] public InputToCommand command;
    
    /// <summary>
    /// 当前正在做的动作的信息
    /// </summary>
    public ActionInfo CurrentAction { get; private set; }

    /// <summary>
    /// 当前激活的BeCancelledTag
    /// </summary>
    public List<BeCancelledTag> CurrentBeCancelledTag { get; private set; } = new List<BeCancelledTag>();

    /// <summary>
    /// 角色所有会的动作
    /// </summary>
    public List<ActionInfo> AllActions { get; private set; } = new List<ActionInfo>();

    /// <summary>
    /// 当前帧的动画切换请求，如果一个也没有，则会继续当前的动作
    /// </summary>
    private List<PreorderActionInfo> _preorderActions = new List<PreorderActionInfo>();

    /// <summary>
    /// 当前动作走到第几个逻辑帧（0起，动作自己的时钟）
    /// </summary>
    public int CurrentFrame { get; private set; }

    /// <summary>
    /// 推进本帧之前时钟在哪一帧，窗口判定用的是"这一段区间被跨过没有"，所以两个都要
    /// </summary>
    private int _wasFrame = 0;

    /// <summary>
    /// 当前动作一共多少逻辑帧，换动作时从表里取，是全工程唯一的动作时长真源
    /// </summary>
    private int _durationFrames = 1;

    /// <summary>
    /// 当前动作的RootMotion求值器：输入动作走到第几个逻辑帧，输出从动作起点累计到该帧的偏移量。
    /// null代表这个动作没有RootMotion位移。
    /// 换动作的时候查一次表存下来，免得每帧拿方法名去字典里翻两遍
    /// </summary>
    private Func<int, RootMotionParam, Vector3> _rootMotionEval = null;

    /// <summary>
    /// 当前动作的RootMotion数值，在GameData.Load里就烘焙好了，这里只是把结构体拷一份
    /// </summary>
    private RootMotionParam _rootMotionParam = new RootMotionParam();

    /// <summary>
    /// 当前动作的帧事件发到第几拍了（见 CalculateCues）。
    /// 换动作时置成 startFrame - 1，循环回绕时置成 -1，于是"发过的不再发、跳过的下一拍补上"
    /// </summary>
    private int _cueCursor = -1;

    /// <summary>
    /// 当前激活的攻击盒tag，合成一个位掩码（每个tag占 BoxTagRegistry 里的一位）
    /// 这算是一个内存换芯片，就是我先每帧算好了储存下来哪些框开启
    /// </summary>
    public int ActiveAttackBoxMask { get; private set; }
    //同上，只是储存的是具体信息
    public List<AttackBoxTurnOnInfo> ActiveAttackBoxInfo { get; private set; } = new List<AttackBoxTurnOnInfo>();

    /// <summary>
    /// 当前帧的移动速度百分比
    /// </summary>
    public float MoveInputAcceptance { get; private set; } = 0;
    
    /// <summary>
    /// 当前激活的受击盒tag，合成一个位掩码（每个tag占 BoxTagRegistry 里的一位）
    /// </summary>
    public int ActiveBeHitBoxMask { get; private set; }
    public List<BeHitBoxTurnOnInfo> ActiveBeHitBoxInfo { get; private set; } = new List<BeHitBoxTurnOnInfo>();
    
    /// <summary>
    /// 当前帧的RootMotion信息
    /// </summary>
    public Vector3 RootMotionMove { get; private set; } = Vector3.zero;

    /// <summary>
    /// 硬直（卡帧）还剩多少逻辑帧
    /// </summary>
    private int _freezeFrames = 0;
    /// <summary>
    /// 是否在硬直或者卡帧
    /// </summary>
    public bool Freezing => _freezeFrames > 0;

    /// <summary>
    /// 施加卡肉顿挫（HitStop）或受击硬直（HitStun），单位是逻辑帧。
    /// 取较大者而不是叠加：连续命中只是把剩下的硬重置满，不会越打越长。
    /// 和SetFreezing的区别就在这里——这个方法立即生效，不等到换动作才写
    /// </summary>
    /// <param name="durationFrames">持续多少个逻辑帧</param>
    public void ApplyHitStop(int durationFrames)
    {
        _freezeFrames = Mathf.Max(_freezeFrames, durationFrames);
    }

    /// <summary>
    /// 更换动作的时候的回调函数
    /// 参数1：ActionInfo：更换之前的action
    /// 参数2：ActionInfo：更换之后的action
    /// 只有在ChangeAction时才会调用
    /// </summary>
    private Action<ActionInfo, ActionInfo> _onChangeAction = null;

    /// <summary>
    /// 面向怎么落地，由角色自己身上的 FacingApply 决定（逻辑层只持有 inversed 这个真源）
    /// </summary>
    private FacingApply _facing;

    private void Awake()
    {
        //Animator降级成本时钟的从属：不再由Unity按渲染帧自己推进，只在Tick里被显式步进一个逻辑帧。
        //于是"逻辑认为在第几帧"和"屏幕上是第几帧的姿势"是同一个数，漂移在构造上为0，
        //硬直也变成精确的"不步进"（原来是 anim.speed = 0，还会照常求值一遍姿势）
        anim.enabled = false;

        _facing = GetComponent<FacingApply>();
        if (!_facing)
            Debug.LogError("角色 " + name + " 的根节点上没有 FacingApply：面向不会落地。" +
                           "请在 prefab 根节点补一个——3D 骨骼模型选 ModelYaw，贴在 3D 平面上的精灵选 SpriteScaleX");
    }

    /// <summary>
    /// 推进一个逻辑帧。这里不再是Unity的Update回调，而由GameMain的时钟按固定顺序调用，
    /// 顺序是：输入 → 本方法 → 判定盒 → 命中结算 → 角色计时与移动
    /// </summary>
    public void Tick()
    {
        //没有动画就不会工作
        if (AllActions.Count <= 0) return;
        long gcProbe = GcProbe.Begin(GcProbe.Region.ActionControllerUpdate);
        
        //硬直：本tick既不推进动作时钟，也不步进Animator。先取快照再倒数，为的是和今天
        //"这一帧的speed由这一帧开头的_freezing决定"对齐
        bool frozen = Freezing;
        if (_freezeFrames > 0) _freezeFrames--;
        if (!frozen)
        {
            anim.Update(LogicClock.Tick);
            CurrentFrame++;
        }
        
        //算一下攻击盒跟受击盒
        CalculateBoxInfo(_wasFrame, CurrentFrame);

        //帧事件（音效/特效/震镜头/弹射物）：源工程写在 .anim 的 AnimationEvent 里，这里按拍派发
        CalculateCues();
        
        //移动输入接受
        CalculateInputAcceptance(_wasFrame, CurrentFrame);
        
        //算一下2帧之间的RootMotion变化
        if (_rootMotionEval != null)
        {
            Vector3 rmThisTick = _rootMotionEval(CurrentFrame, _rootMotionParam);
            Vector3 rmLastTick = _rootMotionEval(_wasFrame, _rootMotionParam);
            RootMotionMove = rmThisTick - rmLastTick;
            //Debug.Log("RootMotion distance " + RootMotionMove + "=>" + CurrentFrame + " - " + _wasFrame);
        }else RootMotionMove = Vector3.zero;
        
        //动作是否要更换了：观察每个动作，如果他们可以cancel当前动作，并且操作存在，
        //那么就会添加到预约列表里面。硬直期间这个扫描照旧每tick都跑（今天就是这个行为）
        foreach (ActionInfo action in AllActions)
        {
            if (CanActionCancelCurrent(action, CurrentFrame, true, out BeCancelledTag bcTag, out CancelTag cancelTag))
            {
                _preorderActions.Add(new PreorderActionInfo(action.id, bcTag.priority + cancelTag.priority + action.priority,
                    Mathf.Min((float)bcTag.fadeOutFrames / _durationFrames, (float)cancelTag.fadeInFrames / action.durationFrames),
                    cancelTag.startFromFrame));
            }
        }
        
        //如果要更换了就预约下一个动作
        if (_preorderActions.Count <= 0 && (CurrentFrame >= _durationFrames || CurrentAction.autoTerminate))
        {
            _preorderActions.Add(new PreorderActionInfo(CurrentAction.autoNextActionId));
        }
        
        _wasFrame = CurrentFrame;   //先设置这个，之后可能会被ChangeAction所改变
        bool decidedAction = _preorderActions.Count > 0;
        if (decidedAction)
        {
            int bestIndex = 0;
            int maxPriority = _preorderActions[0].Priority;
            for (int i = 1; i < _preorderActions.Count; i++)
            {
                if (_preorderActions[i].Priority > maxPriority)
                {
                    maxPriority = _preorderActions[i].Priority;
                    bestIndex = i;
                }
            }

            PreorderActionInfo best = _preorderActions[bestIndex];
            if (best.ActionId == CurrentAction.id && CurrentAction.keepPlayingAnim)
                KeepAction();
            else
                ChangeAction(best.ActionId, best.TransitionNormalized,
                    best.FromFrame, best.FreezingAfterChangeAction);
        }
        
        //清理一下预约列表
        _preorderActions.Clear();
        //换了动作（或循环回绕）就当帧用0步长求值一次，把新姿势立刻摆出来。
        //少了这一步就没有打击感了：顿挫期间本来不步进Animator，受击姿势要等整个hitStun走完才亮出来，
        //而改造前 anim.speed = 0 只冻进度、不冻"进入新状态"这件事，挨一下当场就是受击姿势
        if (decidedAction) anim.Update(0f);
        GcProbe.End(GcProbe.Region.ActionControllerUpdate, gcProbe);
    }

    /// <summary>
    /// 更换动作的回调函数
    /// </summary>
    /// <param name="onActionChanged"></param>
    public void Set(Action<ActionInfo, ActionInfo> onActionChanged)
    {
        _onChangeAction = onActionChanged;
    }

    public void CalculateInputAcceptance(int wasFrame, int frame)
    {
        MoveInputAcceptance = 0;
        if (CurrentAction.inputAcceptance == null) return;
        foreach (MoveInputAcceptance acceptance in CurrentAction.inputAcceptance)
        {
            if (acceptance.range.min <= frame && acceptance.range.max >= wasFrame &&
                (MoveInputAcceptance <= 0 || acceptance.rate < MoveInputAcceptance))
                MoveInputAcceptance = acceptance.rate;
        }
    }

    /// <summary>
    /// 计算当前动作帧的盒子信息
    /// </summary>
    /// <param name="wasFrame">上一帧的帧号</param>
    /// <param name="frame">当前帧号</param>
    private void CalculateBoxInfo(int wasFrame, int frame)
    {
        long gcProbe = GcProbe.Begin(GcProbe.Region.CalculateBoxInfo);
        ActiveAttackBoxInfo.Clear();
        ActiveAttackBoxMask = 0;
        foreach (AttackBoxTurnOnInfo aBox in CurrentAction.attackPhase)
        {
            bool open = false;
            foreach (FrameRange range in aBox.inFrames)
            {
                if (frame >= range.min && wasFrame <= range.max)
                {
                    open = true;
                    break;
                }
            }

            if (open)
            {
                ActiveAttackBoxMask |= aBox.tagMask;
                ActiveAttackBoxInfo.Add(aBox);
            }
        }
        
        ActiveBeHitBoxInfo.Clear();
        ActiveBeHitBoxMask = 0;
        foreach (BeHitBoxTurnOnInfo bHitBox in CurrentAction.defensePhase)
        {
            bool open = false;
            foreach (FrameRange range in bHitBox.inFrames)
            {
                if (frame >= range.min && wasFrame <= range.max)
                {
                    open = true;
                    break;
                }
            }

            if (open)
            {
                ActiveBeHitBoxMask |= bHitBox.tagMask;
                ActiveBeHitBoxInfo.Add(bHitBox);
            }
        }
        GcProbe.End(GcProbe.Region.CalculateBoxInfo, gcProbe);
    }

    /// <summary>
    /// 帧事件派发：时钟跨过第几拍，就把落在其中的 cue 交出去一次。
    /// 【为什么用游标而不是窗口那套 wasFrame/frame 比较】：cue 是一个"点"不是一段"区间"，
    /// 套区间语义就得给每条造一个 min==max 的 FrameRange；而循环动作回绕的那一拍区间是倒着的
    /// （wasFrame=末帧、frame=0），点会被整个跳过——脚步声这种"每循环响一次"的 cue 就这样丢了。
    /// 游标跟着回绕复位，循环一次就正好响一次。
    /// 【硬直期间不响】：卡肉的那几拍 CurrentFrame 不前进，游标也不前进，cue 顺延到顿挫结束。
    /// 这是刻意的：挥空的声音抢在卡肉之前响会削掉打击感，而顿挫本来就是"表现层的时间被冻住"
    /// </summary>
    private void CalculateCues()
    {
        long gcProbe = GcProbe.Begin(GcProbe.Region.CueFire);
        ActionCue[] cues = CurrentAction.cues;
        if (cues != null)
        {
            for (int i = 0; i < cues.Length; i++)
            {
                ActionCue cue = cues[i];
                if (cue.frame > _cueCursor && cue.frame <= CurrentFrame)
                    CueDispatch.Fire(CurrentAction, cue);
            }
        }

        _cueCursor = CurrentFrame;
        GcProbe.End(GcProbe.Region.CueFire, gcProbe);
    }

    /// <summary>
    /// 在当前的情况下，是否能Cancel掉CurrentAction
    /// </summary>
    /// <param name="action">动画</param>
    /// <param name="currentFrame">当前动作走到了第几帧</param>
    /// <param name="checkCommand">是否检查输入，true代表要输入也合法才行</param>
    /// <param name="beCancelledTag">符合条件的BeCancelledTag</param>
    /// <param name="foundTag">符合条件的CancelTag</param>
    /// <returns></returns>
    private bool CanActionCancelCurrent(ActionInfo action, int currentFrame, bool checkCommand, out BeCancelledTag beCancelledTag, out CancelTag foundTag)
    {
        foundTag = new CancelTag();
        beCancelledTag = new BeCancelledTag();
        foreach (BeCancelledTag bcTag in CurrentBeCancelledTag)
        {
            //帧号符合的情况下，才可能有效
            if (!(bcTag.cancelWindowFrames.max >= _wasFrame && bcTag.cancelWindowFrames.min <= currentFrame)) continue;
            
            //判断CancelTag是否有交集，没有交集，说明也不能cancel
            bool tagFit = false;
            foreach (string cTag in bcTag.cancelTag)
            {
                foreach (CancelTag cancelTag in action.cancelTag)
                {
                    // 【工业级重构优化】：采用序数比对（Ordinal），规避区域文化敏感性字符串扫描开销
                    if (string.Equals(cancelTag.tag, cTag, StringComparison.Ordinal))
                    {
                        tagFit = true;
                        beCancelledTag = bcTag;
                        foundTag = cancelTag;
                        break;
                    }
                }

                if (tagFit) break;
            }
            if (!tagFit) continue;
            
            //检查输入
            if (checkCommand)
            {
                foreach (ActionCommand ac in action.commands)
                {
                    //任何一条操作符合，就算符合
                    if (command.ActionOccur(ac)) return true;
                }
            }
            else return true;   //不检查输入，到这里就直接符合了
        }

        return false;   //很遗憾，找不到
    }

    /// <summary>
    /// 更换到某个action
    /// </summary>
    /// <param name="actionId">目标actionId</param>
    /// <param name="transitionNormalized">融合长度（占目标动作时长的比例，CrossFade的实参量纲）</param>
    /// <param name="startFrame">从目标动作的第几个逻辑帧开始播放</param>
    /// <param name="freezingAfterChange">切换动作后，硬直多少逻辑帧</param>
    private void ChangeAction(string actionId, float transitionNormalized, int startFrame, int freezingAfterChange)
    {
        ActionInfo aInfo = GetActionById(actionId, out bool foundAction);
        if (foundAction)
        {
            startFrame = Mathf.Clamp(startFrame, 0, aInfo.durationFrames);
            //清除掉非方向操作，连招手感得这么保障，当然刻意为了更容易连招，可以去掉这个
            command.CleanNonDirectionInputs();
            
            _onChangeAction?.Invoke(CurrentAction, aInfo);
            //表里存的是帧号，只有喂给Unity的这两个实参要换成归一化——这是边界换算，不是时间的表示
            anim.CrossFade(aInfo.animKey, transitionNormalized, 0, startFrame / (float)aInfo.durationFrames);
            CurrentAction = aInfo;
            _durationFrames = aInfo.durationFrames;
            //默认的cancelTag都可以加上
            CurrentBeCancelledTag.Clear();
            foreach (BeCancelledTag beCancelledTag in aInfo.beCancelledTag)
            {
                CurrentBeCancelledTag.Add(beCancelledTag);
            }

            _freezeFrames = freezingAfterChange;
            
            ActiveBeHitBoxInfo.Clear();
            ActiveBeHitBoxMask = 0;
            ActiveAttackBoxMask = 0;
            ActiveAttackBoxInfo.Clear();
            
            //RootMotion：换动作的时候查一次表，数值是在GameData.Load里烘焙好的，这里只拷结构体
            _rootMotionEval = null;
            string rootMotionMethod = aInfo.rootMotionTween.method;
            if (!string.IsNullOrEmpty(rootMotionMethod) &&
                RootMotionMethod.Methods.TryGetValue(rootMotionMethod, out RootMotionFunc rootMotionFunc))
            {
                _rootMotionEval = rootMotionFunc.Eval;
                _rootMotionParam = aInfo.rootMotionParam;
            }
            
            _wasFrame = CurrentFrame = startFrame;
            //cue 游标停在起点的前一拍：从 startFrame 起（含）的每一条都还没发过，
            //而 startFrame 之前的不补发——从第20帧插入的招式不该把前面的挥空声重放一遍
            _cueCursor = startFrame - 1;
            //顺便修一下面向。落地方式交给角色身上的 FacingApply：
            //3D 模型是绕 Y 转 90/270，精灵则是 localScale.x 取反——写死欧拉角的话精灵会侧对镜头变成一条线
            if (_facing) _facing.Apply(command.inversed);
            //修正完毕才接受新的是否要转向，因为可能这个动作本身自带转向
            if (aInfo.flip) command.inversed = !command.inversed;
            
        }
    }

    /// <summary>
    /// 保持继续播放动作
    /// 为什么这也需要一个专门的函数？因为要处理循环问题
    /// 【_wasFrame 要停在最后一帧，不能跟着回绕】：循环的那一tick要经历区间[末帧, 0]，
    /// 像[0, x]这种从开头就开的窗口才会照今天的样子"关掉再开"一次，而不是凭空多开一帧
    /// </summary>
    private void KeepAction()
    {
        if (CurrentFrame >= _durationFrames)
        {
            anim.CrossFade(CurrentAction.animKey, 0, 0, 0);
            CurrentFrame = 0;
            //游标跟着回绕：下一拍要经历区间[末帧, 1]，倒着走没法用比较判断跨过了谁，
            //而循环的动作正是靠这一步才能每一圈都把手上的 cue 重放一次（比如跑步的脚步声）
            _cueCursor = -1;
        }
            
    }

    /// <summary>
    /// 从allActions(已经学会的动作)中抽出第一个id符合条件的动作，如果没有，就会返回当前的动作
    /// </summary>
    /// <param name="actionId"></param>
    /// <param name="found">是否找到了合适的</param>
    /// <returns></returns>
    private ActionInfo GetActionById(string actionId, out bool found)
    {
        found = false;
        foreach (ActionInfo action in AllActions)
        {
            if (action.id == actionId)
            {
                found = true;
                return action;
            }
        }

        return CurrentAction;
    }

    public int IndexOfAttack(int attackPhase)
    {
        for (int i = 0; i < CurrentAction.attacks.Length; i++)
        {
            if (CurrentAction.attacks[i].phase == attackPhase)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// 初始化：设置所有的动作
    /// 【只能换引用，不能Clear】：传进来的是 GameData.AllActions() 的那一份共享表，
    /// 对着同一个控制器第二次调用时，Clear 就把全工程的表一起擦了
    /// </summary>
    /// <param name="actions"></param>
    /// <param name="defaultActionId"></param>
    public void SetAllActions(List<ActionInfo> actions, string defaultActionId)
    {
        if (actions != null) AllActions = actions;
        ChangeAction(defaultActionId, 0, 0, 0);
    }

    /// <summary>
    /// 预约一个动作
    /// </summary>
    /// <param name="acInfo">变换动作信息</param>
    /// <param name="forceDir">如有必要（其实就是byCatalog）得给个动作受力方向</param>
    /// <param name="freezing">如果切换到这个动作，硬直多少个逻辑帧</param>
    public void PreorderActionByActionChangeInfo(ActionChangeInfo acInfo, ForceDirection forceDir, int freezing = 0)
    {
        switch (acInfo.changeType)
        {
            case ActionChangeType.Keep: 
                //既然保持，就啥也不做了
                break;
            case ActionChangeType.ChangeByCatalog:
                long gcProbe = GcProbe.Begin(GcProbe.Region.ChangeByCatalog);
                ActionInfo[] actions = GameData.ActionsOfCatalog(acInfo.param);
                if (actions.Length > 0)
                {
                    ActionInfo picked = actions[0];
                    //如果有策划设计的脚本，那就走脚本拿到数据
                    if (PickActionMethod.Methods.ContainsKey(acInfo.param))
                    {
                        picked = PickActionMethod.Methods[acInfo.param](actions, forceDir);
                    }
                    _preorderActions.Add(new PreorderActionInfo
                    {
                        ActionId = picked.id,
                        FromFrame = acInfo.fromFrame,
                        Priority = acInfo.priority + picked.priority,
                        TransitionNormalized = acInfo.transFrames / (float)picked.durationFrames,
                        FreezingAfterChangeAction = freezing
                    });
                }
                GcProbe.End(GcProbe.Region.ChangeByCatalog, gcProbe);
                break;
            case ActionChangeType.ChangeToActionId:
                //找到对应id的动作，如果有的话
                ActionInfo aInfo = GetActionById(acInfo.param, out bool found);
                if (found)
                {
                    _preorderActions.Add(new PreorderActionInfo
                    {
                        ActionId = aInfo.id,
                        FromFrame = acInfo.fromFrame,
                        Priority = acInfo.priority + aInfo.priority,
                        TransitionNormalized = acInfo.transFrames / (float)aInfo.durationFrames,
                        FreezingAfterChangeAction = freezing
                    });
                }
                break;
        }
    }

    /// <summary>
    /// 加入卡帧，卡帧会叠加，但是最多不会超过一个值，并且越接近的时候增加量越少
    /// 注意，这只能是攻击方的卡帧freezing，因为它会叠加；受击方的hitStun走ApplyHitStop（取较大者，不叠加）
    /// </summary>
    /// <param name="freezingFrames"></param>
    public void SetFreezing(int freezingFrames)
    {
        if (_freezeFrames < 0) _freezeFrames = 0;   //清理一下
        const int maxFreezingFrames = 30;   //卡帧、硬直上限，30个逻辑帧 = 0.5秒
        float addRate = Mathf.Clamp(maxFreezingFrames - _freezeFrames, 0, maxFreezingFrames) / (float)maxFreezingFrames;
        _freezeFrames += Mathf.RoundToInt(freezingFrames * addRate);
    }

    /// <summary>
    /// 开启临时的CancelTag
    /// </summary>
    /// <param name="beCancelledTag"></param>
    public void AddTempBeCancelledTag(TempBeCancelledTag beCancelledTag)
    {
        CurrentBeCancelledTag.Add(BeCancelledTag.FromTemp(beCancelledTag, CurrentFrame, _durationFrames));
    }

    /// <summary>
    /// 根据TempBeCancelledTag的id来开启
    /// </summary>
    /// <param name="tempTagId"></param>
    public void AddTempBeCancelledTag(string tempTagId)
    {
        foreach (TempBeCancelledTag beCancelledTag in CurrentAction.tempBeCancelledTag)
        {
            if (beCancelledTag.id == tempTagId)
            {
                AddTempBeCancelledTag(beCancelledTag);
                return;
            }
        }
    }
    
    
}
