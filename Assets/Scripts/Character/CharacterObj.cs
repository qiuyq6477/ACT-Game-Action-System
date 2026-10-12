using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CharacterObj : MonoBehaviour
{
    /// <summary>
    /// 动作管理
    /// </summary>
    public ActionController action;
    /// <summary>
    /// 输入系统，通过这个可以获得一部分移动信息
    /// </summary>
    public InputToCommand input;

    /// <summary>
    /// 重量：每个逻辑帧给下落速度加上的量，不是米/秒²那样的加速度。
    /// 以前用Update靠deltaTime去拟合，帧率一变下落就变（见Doc/plan/refactor.md的Phase 2），
    /// 现在按逻辑帧累加，只在换算成位移那一处乘LogicClock.Tick——60FPS下的轨迹与改造前逐项相同
    /// </summary>
    public float weight = 4.5f;

    /// <summary>
    /// 是否正在下落
    /// </summary>
    private bool _falling = false;
    /// <summary>
    /// 下落的重量
    /// </summary>
    private float _curWeight = 0;

    /// <summary>
    /// 当前是否下降这个东西，并不是由角色自己说了算的，这是绝大多数游戏教程，也包括UE的游戏框架在内，完全做错的地方
    /// 因为是否下落取决于地形等游戏中的其他元素，不仅仅取决于角色自身，甚至几乎不取决于角色自己
    /// 而角色不应该依赖于其他这么多的元素，所以应该由“最小弟”的GameMain这一层来告诉我，我该不该下落
    /// </summary>
    public bool Falling
    {
        get => _falling;
        set
        {
            if (value && !_falling)
            {
                //得初始化一下当前重量         
                _curWeight = 0;
            }
            _falling = value;
        }
    }

    /// <summary>
    /// 碰撞盒的碰撞信息，现在这里缓存一下
    /// </summary>
    private Dictionary<AttackHitBox, List<BeHitBox>> _boxTouches = new Dictionary<AttackHitBox, List<BeHitBox>>();
    
    /// <summary>
    /// 当前动作的命中记录，更换动作就清空了这个列表了
    /// </summary>
    public List<HitRecord> HitRecords { get; private set; } = new List<HitRecord>();

    /// <summary>
    /// 身上的攻击盒与受击盒，Awake里收集好，供GameMain的时钟按固定顺序刷新
    /// </summary>
    public AttackHitBox[] AttackBoxes { get; private set; }
    public BeHitBox[] BeHitBoxes { get; private set; }

    
    
    /// <summary>
    /// 被强制移动，最多只有一个被强制移动
    /// </summary>
    private ForceMove _forceMove = ForceMove.NoForceMove;

    private bool UnderForceMove => _forceMove.FramesElapsed <= _forceMove.Data.inFrames;
    
    public bool Inversed
    {
        get => input.inversed;
        set => input.inversed = value;
    }

    /// <summary>
    /// 移动速度（米/秒）
    /// 这原本应该是角色属性中的一个，但是这个demo并不打算做数值部分，所以就暴露在外
    /// </summary>
    public float moveSpeed;

    private bool WishToMoveForward => input.IsKeyHeld(KeyMap.Forward);

    private bool WishToMoveBackward => input.IsKeyHeld(KeyMap.Backward);

    /// <summary>
    /// 自然的移动，而非ForceMoved。返回的是这一个逻辑帧的位移
    /// </summary>
    public Vector3 NatureMove()
    {
        long gcProbe = GcProbe.Begin(GcProbe.Region.NatureMove);
        //moveSpeed是米/秒、_curWeight是米/逻辑帧累加出来的速度，都只在算位移这一处乘Tick
        Vector3 moved = new Vector3(
            ((WishToMoveForward ? (moveSpeed * action.MoveInputAcceptance * LogicClock.Tick) :
                WishToMoveBackward ? (-moveSpeed * action.MoveInputAcceptance * LogicClock.Tick) : 0) + action.RootMotionMove.x) *
            (Inversed ? -1 : 1),
            action.RootMotionMove.y - _curWeight * LogicClock.Tick,   //unity的坐标系y是反过来的
            action.RootMotionMove.z
        );
        GcProbe.End(GcProbe.Region.NatureMove, gcProbe);
        return moved;
    }

    
    
    private void Awake()
    {
        //set一下components
        action.Set(((wasAction, toAction) =>
        {
            //更换动作时去掉所有命中记录
            HitRecords.Clear();
        }));
        
        //开始的时候初始化一下必要的Components
        List<AttackHitBox> attackBoxes = new List<AttackHitBox>();
        List<BeHitBox> beHitBoxes = new List<BeHitBox>();
        Transform[] transforms = GetComponentsInChildren<Transform>();
        foreach (Transform trans in transforms)
        {
            AttackHitBox ahb = trans.GetComponent<AttackHitBox>();
            if (ahb)
            {
                ahb.master = this;
                //顺便把Inspector上的tag换成位掩码，Awake一定早于所有Update，判定不会踩到没烘焙的盒子
                ahb.BakeTagMask();
                attackBoxes.Add(ahb);
            }
            BeHitBox bhb = trans.GetComponent<BeHitBox>();
            if (bhb)
            {
                bhb.master = this;
                bhb.BakeTagMask();
                beHitBoxes.Add(bhb);
            }
        }

        //盒子由GameMain的时钟按固定顺序刷新，所以这里要把两组盒子留在身上，免得每帧再遍历一次子节点
        AttackBoxes = attackBoxes.ToArray();
        BeHitBoxes = beHitBoxes.ToArray();
    }

    /// <summary>
    /// 推进一个逻辑帧：目前只有命中记录的冷却，移动和地面在GameMain的时钟里同帧另外处理
    /// </summary>
    public void Tick()
    {
        //以下内容只有不在硬直才会执行
        if (!action.Freezing)
        {
            //HitRecords
            foreach (HitRecord record in HitRecords)
                record.Tick();
        }
        
    }

    /// <summary>
    /// 攻击盒子当前是否激活？
    /// </summary>
    /// <param name="boxMask">攻击盒自己的TagMask</param>
    /// <returns></returns>
    public bool ShouldAttackBoxActive(int boxMask)
    {
        //没有tag的盒子掩码是0，跟谁相与都是0，也就永远不开启，和原来空数组的行为一致
        return (boxMask & action.ActiveAttackBoxMask) != 0;
    }

    /// <summary>
    /// 挨打盒子当前是否激活？
    /// </summary>
    /// <param name="boxMask">受击盒的tag掩码</param>
    /// <returns></returns>
    public bool ShouldBeHitBoxActive(int boxMask)
    {
        return (boxMask & action.ActiveBeHitBoxMask) != 0;
    }

    /// <summary>
    /// 攻击盒当前的临时优先级：开启它的攻击阶段里优先级最高的那个
    /// 取最高而不是求和，因为两个阶段同时开启同一个盒子本身就是配表事故，求和只会把错误放大
    /// </summary>
    /// <param name="boxMask">攻击盒的tag掩码</param>
    /// <returns></returns>
    public int GetAttackBoxTempPriority(int boxMask)
    {
        int res = 0;
        bool found = false;
        foreach (AttackBoxTurnOnInfo info in action.ActiveAttackBoxInfo)
        {
            //这个开启的阶段跟盒子没有共同tag，就与它无关
            if ((info.tagMask & boxMask) == 0) continue;
            if (!found || info.priority > res)
            {
                res = info.priority;
                found = true;
            }
        }

        return res;
    }

    /// <summary>
    /// 追加一条攻击框命中受击框的信息
    /// 这里可不管框active与否
    /// </summary>
    /// <param name="attackHitBox">攻击框</param>
    /// <param name="targetBox">受击框</param>
    public void OnAttackBoxHit(AttackHitBox attackHitBox, BeHitBox targetBox)
    {
        if (!_boxTouches.ContainsKey(attackHitBox))
            _boxTouches.Add(attackHitBox, new List<BeHitBox>());
        
        if (!_boxTouches[attackHitBox].Contains(targetBox))
            _boxTouches[attackHitBox].Add(targetBox);
    }

    /// <summary>
    /// 当攻击框脱离了受击框
    /// </summary>
    /// <param name="attackHitBox"></param>
    /// <param name="beHitBox"></param>
    /// <returns></returns>
    public void OnAttackBoxExit(AttackHitBox attackHitBox, BeHitBox beHitBox)
    {
        if (!_boxTouches.ContainsKey(attackHitBox)) return;
        _boxTouches[attackHitBox].Remove(beHitBox);
    }

    private BeHitBoxTurnOnInfo GetDefensePhaseByBeHitBox(BeHitBox box)
    {
        //按defensePhase的表顺序取第一个跟这个盒子有共同tag的阶段。
        //原来是拿盒子的tag当外层去比，只有当一个盒子带多个tag、而这些tag又被不同阶段开启时
        //两者才会挑到不同的阶段，而那种配表本来就被声明为"听天由命"（见ActionInfo的defensePhase注释）
        foreach (BeHitBoxTurnOnInfo info in action.ActiveBeHitBoxInfo)
        {
            if ((info.tagMask & box.TagMask) != 0)
                return info;
        }
        return new BeHitBoxTurnOnInfo();
    }

    /// <summary>
    /// 现在是否能殴打某个人，这里不判断HitRecord
    /// </summary>
    /// <param name="target">谁挨打？</param>
    /// <param name="attackPhase">这是攻击阶段信息</param>
    /// <param name="defensePhase">受击的时候的阶段信息</param>
    /// <param name="attackBox">命中的是哪个攻击框</param>
    /// <param name="targetBox">目标的那个受击框被命中</param>
    /// <returns></returns>
    public bool CanAttackTargetNow(CharacterObj target, out AttackInfo attackPhase, out BeHitBoxTurnOnInfo defensePhase, out AttackHitBox attackBox, out BeHitBox targetBox)
    {
        attackBox = null;
        targetBox = null;
        attackPhase = new AttackInfo();
        
        defensePhase = new BeHitBoxTurnOnInfo();
        if (!target) return false;
        long gcProbe = GcProbe.Begin(GcProbe.Region.CanAttackTargetNow);
        
        //目标当前开启着哪些受击盒tag：ActionController每帧已经算好了，这里直接取掩码
        int activeBoxMask = target.action.ActiveBeHitBoxMask;
        
        //命中对方的所有攻击框，冒泡出最有价值的攻击框和受击框
        AttackHitBox bestAttackBox = null;
        BeHitBox bestTargetBox = null;
        AttackBoxTurnOnInfo bestBoxInfo = new AttackBoxTurnOnInfo();
        BeHitBoxTurnOnInfo bestDefense = new BeHitBoxTurnOnInfo();
        int bestAttackScore = int.MinValue;
        int bestDefenseScore = int.MinValue;
        
        foreach (AttackBoxTurnOnInfo boxInfo in action.ActiveAttackBoxInfo)
        {
            foreach (KeyValuePair<AttackHitBox,List<BeHitBox>> touch in _boxTouches)
            {
                //没有启动的攻击框不会判断命中
                if (!touch.Key.Active) continue;
                
                //先在这个攻击框命中的一堆受击框里挑出最有价值的那个
                BeHitBox best = null;
                int defenseScore = int.MinValue;
                BeHitBoxTurnOnInfo defense = new BeHitBoxTurnOnInfo();
                foreach (BeHitBox hitBox in touch.Value)
                {
                    if (!hitBox.Active || hitBox.master != target || !hitBox.TagHit(activeBoxMask)) continue;
                    BeHitBoxTurnOnInfo info = GetDefensePhaseByBeHitBox(hitBox);
                    int thisPriority = hitBox.Priority + info.priority;
                    if (!best || thisPriority > defenseScore)
                    {
                        best = hitBox;
                        defenseScore = thisPriority;
                        defense = info;
                    }
                }
                //一个没找到，当然就……
                if (!best) continue;
                
                //攻击框的分优先，同分才比受击框的分
                int attackScore = touch.Key.Priority;
                bool better = !bestAttackBox || attackScore > bestAttackScore ||
                              (attackScore == bestAttackScore && defenseScore > bestDefenseScore);
                if (!better) continue;
                
                bestAttackBox = touch.Key;
                bestTargetBox = best;
                bestBoxInfo = boxInfo;
                bestDefense = defense;
                bestAttackScore = attackScore;
                bestDefenseScore = defenseScore;
            }
        }

        if (!bestAttackBox)
        {
            GcProbe.End(GcProbe.Region.CanAttackTargetNow, gcProbe);
            return false;
        }
        
        attackBox = bestAttackBox;
        targetBox = bestTargetBox;
        defensePhase = bestDefense;
        if (bestBoxInfo.attackPhase >= 0 && bestBoxInfo.attackPhase < action.CurrentAction.attacks.Length)
            attackPhase = action.CurrentAction.attacks[bestBoxInfo.attackPhase];
        GcProbe.End(GcProbe.Region.CanAttackTargetNow, gcProbe);
        return true;
    }
    

    /// <summary>
    /// 攻击框命中了对手哪些受击框，一个也没的话就会是空数组了
    /// </summary>
    /// <param name="attackBox">攻击框</param>
    /// <param name="target">对手</param>
    /// <returns></returns>
    private List<BeHitBox> AttackBoxHitTargetIn(AttackHitBox attackBox, CharacterObj target)
    {
        List<BeHitBox> res = new List<BeHitBox>();
        if (!_boxTouches.ContainsKey(attackBox)) return res;
        foreach (BeHitBox hitBox in _boxTouches[attackBox])
        {
            if (hitBox.master == target)
                res.Add(hitBox);
        }
        return res;
    }
    
    /// <summary>
    /// 添加命中记录
    /// </summary>
    /// <param name="target">谁被命中</param>
    /// <param name="attackPhase">算是第几阶段的攻击命中的</param>
    /// <returns></returns>
    public void AddHitRecord(CharacterObj target, int attackPhase)
    {
        int idx = action.IndexOfAttack(attackPhase);
        if (idx < 0) return;    //没有这个伤害阶段，结束
        
        HitRecord rec = GetHitRecord(target, attackPhase);
        if (rec == null)
        {
            HitRecords.Add(new HitRecord(target, attackPhase, action.CurrentAction.attacks[idx].canHitSameTarget - 1,
                action.CurrentAction.attacks[idx].hitSameTargetDelayFrames));
        }
        else
        {
            rec.CooldownFrames = action.CurrentAction.attacks[idx].hitSameTargetDelayFrames;
            rec.CanHitTimes -= 1;
        }
    }
    
    /// <summary>
    /// 找出关于目标的HitRecord，如果没有就是null
    /// </summary>
    /// <param name="target">谁被命中</param>
    /// <param name="phase">算是第几阶段的攻击命中的</param>
    /// <returns></returns>
    public HitRecord GetHitRecord(CharacterObj target, int phase)
    {
        foreach (HitRecord record in HitRecords)
        {
            if (record.UniqueId == target.gameObject.GetInstanceID() && record.Phase == phase)
                return record;
        }

        return null;
    }
    
    /// <summary>
    /// 这一个逻辑帧的移动 todo 目前只有NatureMove，还没有Forced
    /// </summary>
    public Vector3 ThisTickMove()
    {
        //硬直时候不做移动
        if (action.Freezing) return Vector3.zero;
        //有强制位移有限强制位移
        if (UnderForceMove)
        {
            Vector3 fMove = _forceMove.MoveTween(_forceMove);
            _forceMove.Advance();
            return fMove;
        }
        //下落重量按逻辑帧累加，换算成位移时才乘Tick（不在Update里做，由时钟调用的地方统一推进）
        if (_falling) _curWeight += weight;
        else _curWeight = 0;
        //有强制位移的时候强制位移，没有的时候自然移动
        return  NatureMove();
    }

    /// <summary>
    /// 设置强制移动，只接受最新的一个
    /// </summary>
    /// <param name="force"></param>
    public void SetForceMove(MoveInfo force)
    {
        _forceMove = ForceMove.FromData(force);
    }
}
