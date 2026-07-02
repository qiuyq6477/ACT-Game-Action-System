using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 动作管理器，是一个核心组件
/// </summary>
public class ActionController : MonoBehaviour
{
    public PlayableAnimationPlayer playablePlayer { get; private set; }

    private void Awake()
    {
        playablePlayer = GetComponent<PlayableAnimationPlayer>();
        if (playablePlayer == null)
            playablePlayer = gameObject.AddComponent<PlayableAnimationPlayer>();
        playablePlayer.Initialize(anim);
    }

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
    /// 这个动作在上一个 Tick 经历了多少逻辑帧
    /// </summary>
    private int _lastFrame = 0;

    /// <summary>
    /// 当前动作的RootMotion方法
    /// 参数float：当前动作进行到的百分比
    /// 参数string[]：配置在ActionInfo表里actionInfo.rootMotionTween`的param部分
    /// 返回值：Vector3，偏移量，假设起始的时候坐标为zero，到normalized==参数float的时候，当时的偏移值
    /// </summary>
    private ScriptMethodInfo _rootMotion = new ScriptMethodInfo();

    /// <summary>
    /// 当前激活的攻击盒tag
    /// 这算是一个内存换芯片，就是我先每帧算好了储存下来哪些框开启
    /// </summary>
    public List<string> ActiveAttackBoxTag { get; private set; } = new List<string>();
    //同上，只是储存的是具体信息
    public List<AttackBoxTurnOnInfo> ActiveAttackBoxInfo { get; private set; } = new List<AttackBoxTurnOnInfo>();

    /// <summary>
    /// 当前帧的移动速度百分比
    /// </summary>
    public float MoveInputAcceptance { get; private set; } = 0;
    
    /// <summary>
    /// 当前激活的受击盒tag
    /// </summary>
    public List<string> ActiveBeHitBoxTag { get; private set; } = new List<string>();
    public List<BeHitBoxTurnOnInfo> ActiveBeHitBoxInfo { get; private set; } = new List<BeHitBoxTurnOnInfo>();
    
    /// <summary>
    /// 当前帧的RootMotion信息
    /// </summary>
    public Vector3 RootMotionMove { get; private set; } = Vector3.zero;

    /// <summary>
    /// 硬直（卡帧）逻辑帧数
    /// </summary>
    private int _freezing = 0;
    /// <summary>
    /// 是否在硬直或者卡帧
    /// </summary>
    public bool Freezing => _freezing > 0;

    /// <summary>
    /// 更换动作的时候的回调函数
    /// 参数1：ActionInfo：更换之前的action
    /// 参数2：ActionInfo：更换之后的action
    /// 只有在ChangeAction时才会调用
    /// </summary>
    private Action<ActionInfo, ActionInfo> _onChangeAction = null;

    /// <summary>
    /// 当前动作已播放的逻辑帧数
    /// </summary>
    private int _curFrame = 0;
    
    /// <summary>
    /// 60Hz 逻辑 Tick，更新动作状态与切换逻辑
    /// </summary>
    public void LogicTick(float delta)
    {
        //没有动画就不会工作
        if (AllActions.Count <= 0) return;
        
        //扣减硬直逻辑帧数
        if (_freezing > 0) _freezing--;
        
        // 手动递增当前逻辑帧
        _lastFrame = _curFrame;
        if (!Freezing)
        {
            _curFrame++;
            if (_curFrame > CurrentAction.maxFrames)
            {
                _curFrame = CurrentAction.maxFrames;
            }
        }
        
        // 转换为百分比，仅用于表现层动画采样和部分插值位移
        float pec = CurrentAction.maxFrames > 0 ? Mathf.Clamp01((float)_curFrame / CurrentAction.maxFrames) : 1f;
        float wasPec = CurrentAction.maxFrames > 0 ? Mathf.Clamp01((float)_lastFrame / CurrentAction.maxFrames) : 0f;
        
        //算一下攻击盒跟受击盒
        CalculateBoxInfo(_lastFrame, _curFrame);
        
        //移动输入接受
        CalculateInputAcceptance(_lastFrame, _curFrame);
        
        //算一下2帧之间的RootMotion变化
        if (!String.IsNullOrEmpty(_rootMotion.method) && RootMotionMethod.Methods.ContainsKey(_rootMotion.method))
        {
            Vector3 rmThisTick = RootMotionMethod.Methods[_rootMotion.method](pec, _rootMotion.param);
            Vector3 rmLastTick = RootMotionMethod.Methods[_rootMotion.method](wasPec, _rootMotion.param);
            RootMotionMove = rmThisTick - rmLastTick;
        }else RootMotionMove = Vector3.zero;
        
        //开始观察每个动作，如果他们可以cancel当前动作，并且操作存在，那么就会添加到预约列表里面
        foreach (ActionInfo action in AllActions)
        {
            if (CanActionCancelCurrent(action, _curFrame, true, out BeCancelledTag bcTag, out CancelTag cancelTag))
            {
                float targetLength = playablePlayer.GetClipLength(action.animKey);
                int targetMax = Mathf.RoundToInt(targetLength * 60f);
                float fromNormalized = targetMax > 0 ? (float)cancelTag.startFrom / targetMax : 0f;
                float transitionNormalized = (float)cancelTag.fadeIn / 60f;

                _preorderActions.Add(new PreorderActionInfo(action.id, bcTag.priority + cancelTag.priority + action.priority,
                    transitionNormalized, fromNormalized));
            }
        }
        
        //如果要更换了就预约下一个动作
        if (_preorderActions.Count <= 0 && (_curFrame >= CurrentAction.maxFrames || ShouldTerminateMovementAction()))
        {
            _preorderActions.Add(new PreorderActionInfo(CurrentAction.autoNextActionId));
        }
        
        //冒泡所有的候选动作，得出应该切换的动作
        if (_preorderActions.Count > 0)
        {
            //有需要更换的动画就更换
            _preorderActions.Sort(
                (candidate1, candidate2) => candidate1.Priority > candidate2.Priority ? -1 : 1
                );
            if (_preorderActions[0].ActionId == CurrentAction.id)
                KeepAction();
            else
                ChangeAction(_preorderActions[0].ActionId, _preorderActions[0].TransitionNormalized,
                    _preorderActions[0].FromNormalized, _preorderActions[0].FreezingAfterChangeAction);
        }
        
        //清理一下预约列表
        _preorderActions.Clear();

        //同步进度给 PlayablePlayer
        playablePlayer.SetPec(pec);
    }

    /// <summary>
    /// 更换动作的回调函数
    /// </summary>
    /// <param name="onActionChanged"></param>
    public void Set(Action<ActionInfo, ActionInfo> onActionChanged)
    {
        _onChangeAction = onActionChanged;
    }

    public void CalculateInputAcceptance(int lastFrame, int curFrame)
    {
        MoveInputAcceptance = 0;
        if (CurrentAction.inputAcceptance == null) return;
        foreach (MoveInputAcceptance acceptance in CurrentAction.inputAcceptance)
        {
            if (acceptance.range.minFrame <= curFrame && acceptance.range.maxFrame >= lastFrame &&
                (MoveInputAcceptance <= 0 || acceptance.rate < MoveInputAcceptance))
                MoveInputAcceptance = acceptance.rate;
        }
    }

    /// <summary>
    /// 计算当前动画帧的判定盒开关信息
    /// </summary>
    private void CalculateBoxInfo(int lastFrame, int curFrame)
    {
        ActiveAttackBoxInfo.Clear();
        ActiveAttackBoxTag.Clear();
        foreach (AttackBoxTurnOnInfo aBox in CurrentAction.attackPhase)
        {
            bool open = false;
            foreach (FrameRange range in aBox.inFrames)
            {
                if (curFrame >= range.minFrame && lastFrame <= range.maxFrame)
                {
                    open = true;
                    break;
                }
            }

            if (open)
            {
                foreach (string aTag in aBox.tag)
                    if (!ActiveAttackBoxTag.Contains(aTag))
                        ActiveAttackBoxTag.Add(aTag);
                ActiveAttackBoxInfo.Add(aBox);
            }
        }
        
        ActiveBeHitBoxInfo.Clear();
        ActiveBeHitBoxTag.Clear();
        foreach (BeHitBoxTurnOnInfo bHitBox in CurrentAction.defensePhase)
        {
            bool open = false;
            foreach (FrameRange range in bHitBox.inFrames)
            {
                if (curFrame >= range.minFrame && curFrame <= range.maxFrame)
                {
                    open = true;
                    break;
                }
            }

            if (open)
            {
                foreach (string bTag in bHitBox.tag)
                    if (!ActiveBeHitBoxTag.Contains(bTag))
                        ActiveBeHitBoxTag.Add(bTag);
                ActiveBeHitBoxInfo.Add(bHitBox);
            }
        }
    }

    /// <summary>
    /// 在当前逻辑帧下，是否能 Cancel 掉 CurrentAction
    /// </summary>
    private bool CanActionCancelCurrent(ActionInfo action, int curFrame, bool checkCommand, out BeCancelledTag beCancelledTag, out CancelTag foundTag)
    {
        foundTag = new CancelTag();
        beCancelledTag = new BeCancelledTag();
        foreach (BeCancelledTag bcTag in CurrentBeCancelledTag)
        {
            // 在逻辑帧区间内，才可能有效
            if (!(bcTag.frameRange.maxFrame >= _lastFrame && bcTag.frameRange.minFrame <= curFrame)) continue;
            
            //判断CancelTag是否有交集，没有交集，说明也不能cancel
            bool tagFit = false;
            foreach (string cTag in bcTag.cancelTag)
            {
                foreach (CancelTag cancelTag in action.cancelTag)
                {
                    if (cancelTag.tag == cTag)
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
    private void ChangeAction(string actionId, float transitionNormalized, float fromNormalized, int freezingAfterChange)
    {
        ActionInfo aInfo = GetActionById(actionId, out bool foundAction);
        if (foundAction)
        {
            //清除掉非方向操作，连招手感得这么保障，当然刻意为了更容易连招，可以去掉这个
            command.CleanNonDirectionInputs();
            
            _onChangeAction?.Invoke(CurrentAction, aInfo);
            playablePlayer.PlayClip(aInfo.animKey, transitionNormalized, fromNormalized);
            CurrentAction = aInfo;
            //默认的cancelTag都可以加上
            CurrentBeCancelledTag.Clear();
            foreach (BeCancelledTag beCancelledTag in aInfo.beCancelledTag)
            {
                CurrentBeCancelledTag.Add(beCancelledTag);
            }

            _freezing = freezingAfterChange;
            
            ActiveBeHitBoxInfo.Clear();
            ActiveBeHitBoxTag.Clear();
            ActiveAttackBoxTag.Clear();
            ActiveAttackBoxInfo.Clear();
            
            _rootMotion = aInfo.rootMotionTween;
            
            int fromFrame = Mathf.RoundToInt(fromNormalized * aInfo.maxFrames);
            _lastFrame = fromFrame;
            _curFrame = fromFrame;
            //顺便修一下面向
            transform.eulerAngles = new Vector3(0, command.inversed ? 270 : 90, 0);
            //修正完毕才接受新的是否要转向，因为可能这个动作本身自带转向
            if (aInfo.flip) command.inversed = !command.inversed;
            
        }
    }

    private void KeepAction()
    {
        if (_curFrame >= CurrentAction.maxFrames)
        {
            playablePlayer.PlayClip(CurrentAction.animKey, 0, 0);
            _curFrame = 0;
            _lastFrame = 0;
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
    /// </summary>
    /// <param name="actions"></param>
    /// <param name="defaultActionId"></param>
    public void SetAllActions(List<ActionInfo> actions, string defaultActionId)
    {
        AllActions.Clear();
        if (actions != null) AllActions = actions;
        ChangeAction(defaultActionId, 0, 0, 0);
    }

    /// <summary>
    /// 预约一个动作
    /// </summary>
    /// <param name="acInfo">变换动作信息</param>
    /// <param name="forceDir">如有必要（其实就是byCatalog）得给个动作受力方向</param>
    /// <param name="freezing">如果切换到这个动作，硬直逻辑帧数</param>
    public void PreorderActionByActionChangeInfo(ActionChangeInfo acInfo, ForceDirection forceDir, int freezing = 0)
    {
        switch (acInfo.changeType)
        {
            case ActionChangeType.Keep: 
                //既然保持，就啥也不做了
                break;
            case ActionChangeType.ChangeByCatalog:
                List<ActionInfo> actions = new List<ActionInfo>();
                foreach (ActionInfo info in AllActions)
                    if (info.catalog == acInfo.param)
                        actions.Add(info);
                if (actions.Count > 0)
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
                        FromNormalized = acInfo.fromNormalized,
                        Priority = acInfo.priority + picked.priority,
                        TransitionNormalized = acInfo.transNormalized,
                        FreezingAfterChangeAction = freezing
                    });
                }
                break;
            case ActionChangeType.ChangeToActionId:
                //找到对应id of action，如果有的话
                ActionInfo aInfo = GetActionById(acInfo.param, out bool found);
                if (found)
                {
                    _preorderActions.Add(new PreorderActionInfo
                    {
                        ActionId = aInfo.id,
                        FromNormalized = acInfo.fromNormalized,
                        Priority = acInfo.priority + aInfo.priority,
                        TransitionNormalized = acInfo.transNormalized,
                        FreezingAfterChangeAction = freezing
                    });
                }
                break;
        }
    }

    /// <summary>
    /// 加入卡帧，卡帧会叠加，但是最多不会超过一个值，并且越接近的时候增加量越少
    /// 注意，这只能是卡帧freezing，因为他会立即暂停角色动作，而受击的hitStun是在切换动作之后，切勿走这里
    /// </summary>
    /// <param name="freezing"></param>
    public void SetFreezing(int freezing)
    {
        if (_freezing < 0) _freezing = 0;   //清理一下
        int maxFreezing = 30;   //卡帧、硬直上限 (30 帧)
        float addRate = Mathf.Clamp((float)(maxFreezing - _freezing), 0f, (float)maxFreezing) / maxFreezing;
        _freezing += Mathf.RoundToInt(freezing * addRate);
    }

    private bool ShouldTerminateMovementAction()
    {
        if (CurrentAction.commands == null || CurrentAction.commands.Length == 0) return false;
        
        bool allCommandsAreDirections = true;
        foreach (var cmd in CurrentAction.commands)
        {
            foreach (var key in cmd.keySequence)
            {
                if (!InputToCommand.IsDirectionInput(key))
                {
                    allCommandsAreDirections = false;
                    break;
                }
            }
            if (!allCommandsAreDirections) break;
        }

        if (!allCommandsAreDirections) return false;

        foreach (var cmd in CurrentAction.commands)
        {
            if (command.ActionOccur(cmd)) return false;
        }

        return true;
    }

    /// <summary>
    /// 开启临时的CancelTag
    /// </summary>
    /// <param name="beCancelledTag"></param>
    public void AddTempBeCancelledTag(TempBeCancelledTag beCancelledTag)
    {
        CurrentBeCancelledTag.Add(BeCancelledTag.FromTemp(beCancelledTag, _curFrame));
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
