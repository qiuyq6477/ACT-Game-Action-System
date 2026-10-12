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
    /// 贴地探测用的球半径（米）
    /// </summary>
    public float groundProbeRadius = 0.3f;

    /// <summary>
    /// 站在地面上时允许留的缝隙（米），也等于能自动爬上来的最大台阶高度
    /// </summary>
    public float groundSnapTolerance = 0.02f;

    /// <summary>
    /// 地面所在层。探测只认这一层，角色身上那一堆触发盒因此天然被排除；
    /// 要放斜坡得用BoxCollider或者凸的MeshCollider——形状投射打不到非凸的网格碰撞体
    /// </summary>
    private const string GroundLayerName = "Ground";
    /// <summary>
    /// 地面层的掩码，Awake里解析：LayerMask.NameToLayer不许出现在字段初始化器里（那等于在.cctor里调），
    /// 而且层不存在时它返回-1，1 << -1在C#里是未定义行为，所以只能在Awake里判完再生成
    /// </summary>
    private int _groundMask;

    /// <summary>
    /// 探测距离的余量（米），免得正好站在地面上时压在PhysX的边界上，理由见 ProbeGround
    /// </summary>
    private const float ProbeMargin = 0.001f;

    /// <summary>
    /// 在地面上还是在空中。是否下落从来不是角色自己说了算的（这是绝大多数游戏教程、
    /// 也包括UE的游戏框架在内做错的地方），它取决于地形，所以由地面探测决定；
    /// 眼下它的消费者是重力，空中出招、落地后摇这些以后也要靠它
    /// </summary>
    public MoveState State { get; private set; } = MoveState.Grounded;

    /// <summary>
    /// 只在落地的那一个逻辑帧为true（每次Tick开头清零），留给以后的落地动作或者音效
    /// </summary>
    public bool JustLanded { get; private set; }

    /// <summary>
    /// 下落的重量
    /// </summary>
    private float _curWeight = 0;

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

    /// <summary>
    /// 纵深（车道）移动速度相对 moveSpeed 的比例。
    /// 纵深位移在屏幕上被透视压缩过，1:1 会显得比横向快；BeatEmUp 的原版把输入的 y 乘 0.8，这里沿用
    /// </summary>
    public float depthSpeedRate = 0.8f;

    /// <summary>
    /// 角色在车道上的半径（米）：横向多近算"同一轨"、纵向至少要隔多远，都由它和对方的它相加得出。
    /// 角色根节点上没有碰撞体（位移是直写 transform 的），所以同轨推挤只能自己算
    /// </summary>
    public float laneRadius = 0.3f;

    private bool WishToMoveForward => input.IsKeyHeld(KeyMap.Forward);

    private bool WishToMoveBackward => input.IsKeyHeld(KeyMap.Backward);

    /// <summary>
    /// 屏幕上方 = 远离镜头 = +z（HD-2D 的纵深车道，见 Doc/decision/0002）。
    /// 用的还是 KeyMap.Up/Duck 这两个值：竖轴本来就是"屏幕纵向"，横版里地面上一切纵向输入都是纵深。
    /// 它们不参与正反手镜像（GetKeyInput 里只有 Forward↔Backward），所以朝左朝右走出来的车道方向一致
    /// </summary>
    private bool WishToMoveFar => input.IsKeyHeld(KeyMap.Up);

    private bool WishToMoveNear => input.IsKeyHeld(KeyMap.Duck);

    /// <summary>
    /// 自然的移动，而非ForceMoved。返回的是这一个逻辑帧的位移
    /// </summary>
    public Vector3 NatureMove()
    {
        long gcProbe = GcProbe.Begin(GcProbe.Region.NatureMove);
        //moveSpeed是米/秒、_curWeight是米/逻辑帧累加出来的速度，都只在算位移这一处乘Tick
        //纵深项不乘 Inversed 的符号：那是屏幕空间轴，跟角色朝哪边无关
        float depthMove = (WishToMoveFar ? 1f : WishToMoveNear ? -1f : 0f) *
                          moveSpeed * depthSpeedRate * action.MoveInputAcceptance * LogicClock.Tick;
        Vector3 moved = new Vector3(
            ((WishToMoveForward ? (moveSpeed * action.MoveInputAcceptance * LogicClock.Tick) :
                WishToMoveBackward ? (-moveSpeed * action.MoveInputAcceptance * LogicClock.Tick) : 0) + action.RootMotionMove.x) *
            (Inversed ? -1 : 1),
            action.RootMotionMove.y - _curWeight * LogicClock.Tick,   //unity的坐标系y是反过来的
            depthMove + action.RootMotionMove.z
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

        //这一层不存在的话探测永远落空，角色会一路往下掉，而这种错法本来没有任何报错，所以这里必须喊一声
        int groundLayer = LayerMask.NameToLayer(GroundLayerName);
        if (groundLayer < 0)
            Debug.LogError("工程里没有 " + GroundLayerName + " 这一层：请在 Project Settings → Tags and Layers 里加出来，" +
                           "并把地面碰撞体放到这一层，否则角色踩不到任何东西");
        else
            _groundMask = 1 << groundLayer;
    }

    /// <summary>
    /// 推进一个逻辑帧：命中记录的冷却、移动积分、地面探测，最后写出位置
    /// </summary>
    public void Tick()
    {
        JustLanded = false;
        //以下内容只有不在硬直才会执行
        if (!action.Freezing)
        {
            //HitRecords
            foreach (HitRecord record in HitRecords)
                record.Tick();
        }

        Move();
    }

    /// <summary>
    /// 算出这一逻辑帧的位移，再用地面探测决定脚落在哪里
    /// </summary>
    private void Move()
    {
        Vector3 was = transform.position;
        Vector3 moved = ThisTickMove();
        Vector3 to = was + moved;

        //关卡边界要先夹，再拿夹过的坐标去探测脚下那一列——反过来会探到一列根本不存在的地面
        if (LevelBounds.Current) LevelBounds.Current.Clamp(ref to);

        float groundY;
        if (ProbeGround(was, to, out groundY))
        {
            to.y = groundY;   //贴地，斜坡和台阶就靠这一行
            if (State == MoveState.Airborne)
            {
                State = MoveState.Grounded;
                JustLanded = true;
                _curWeight = 0;
            }
        }
        else if (State == MoveState.Grounded)
        {
            //走下平台或者被打飞：刚开始下落，速度从0算起
            State = MoveState.Airborne;
            _curWeight = 0;
        }

        transform.position = to;
    }

    /// <summary>
    /// 脚下这一列有没有地面，有的话地面在多高。
    /// 三条不能想当然的地方：
    /// 【起点取移动前后较高的那个脚底高度】下降时要从移动前起投，扫过的才是本帧掉下来的那一截——
    ///   只从移动后起投会把这一截漏掉，掉得快时球一开始就已经在碰撞体里面，PhysX只给一个distance=0的命中，
    ///   反算出来的"地面"比人还低，人就一路往地下钻；上升时又必须从移动后起投，
    ///   否则击退把人抛起来的那段会被"我原来站在地上"给判回去，人根本飞不起来；
    /// 【要抬高一个缝隙】球的最低点悬在脚底上方一道缝隙处——球跟地面相切时PhysX算不命中，
    ///   角色就会隔一帧以为自己在空中，而每次以为开始下落都清零速度，人就再也掉不下来；
    /// 【探测距离不算半径】可用距离只有"缝隙+余量+本帧掉下的距离"。把半径也算进去的话（0.3米），
    ///   角色刚被抛起几十厘米依然被判成还在地上。
    /// 横向一律用移动完的位置，这样走下边缘时脚下那一列就是真的那一列。
    /// </summary>
    /// <param name="was">移动前的位置</param>
    /// <param name="to">移动完的位置</param>
    /// <param name="groundY">地面高度，只有返回true时有效</param>
    /// <returns></returns>
    private bool ProbeGround(Vector3 was, Vector3 to, out float groundY)
    {
        float radius = groundProbeRadius;
        float fallen = Mathf.Max(0f, was.y - to.y);
        float originY = Mathf.Max(was.y, to.y) + radius + groundSnapTolerance;
        //ProbeMargin是给正好站在地上那一档留的余量：那时要走的距离恰好等于缝隙，不留余量就压在PhysX的边界上。
        //地面高度是按hit.distance反算的，余量不会把结果带偏
        float distance = groundSnapTolerance + ProbeMargin + fallen;

        RaycastHit hit;
        if (!Physics.SphereCast(new Vector3(to.x, originY, to.z), radius, Vector3.down,
                out hit, distance, _groundMask))
        {
            groundY = 0;
            return false;
        }

        groundY = originY - radius - hit.distance;
        return true;
    }

    /// <summary>
    /// 同一条纵深车道上的角色互相推开，只动 z。
    /// 为什么要推：清版游戏里小兵叠成一团，既看不出还剩几个，也让"要站对齐了才打得到"这条手感失效。
    /// BeatEmUp 原版不分离，只靠 AI 各自挑车道错峰，人一多照样重影。
    /// 【跑在所有角色的 Move() 之后】：这里改的是 z，而地面探测只按 x/z 取脚下那一列、
    /// 算出的 groundY 与 z 无关，所以推完不必再探一次地。但关卡边界要到下一拍的 Move() 里才会重新夹住
    /// </summary>
    /// <param name="all">场上全部角色，顺序即优先级：完全重位时靠这个顺序定死谁往后退</param>
    public static void SeparateLanes(CharacterObj[] all)
    {
        long gcProbe = GcProbe.Begin(GcProbe.Region.LaneSeparate);
        for (int i = 0; i < all.Length; i++)
        {
            CharacterObj a = all[i];
            if (!a) continue;
            for (int j = i + 1; j < all.Length; j++)
            {
                CharacterObj b = all[j];
                if (!b) continue;

                float need = a.laneRadius + b.laneRadius;
                Vector3 pa = a.transform.position;
                Vector3 pb = b.transform.position;
                //两个轴用的是同一个方形footprint：横向没贴近就当不同轨，纵向已经隔开就不用推
                if (Mathf.Abs(pa.x - pb.x) >= need || Mathf.Abs(pa.z - pb.z) >= need) continue;

                float dz = pb.z - pa.z;
                //两人完全重合时方向是未定义的，用数组顺序定死一个符号，
                //否则同一份输入换台机器可能推出两种结果，这是本工程最不能容忍的不确定
                float sign = dz > 0f ? 1f : dz < 0f ? -1f : 1f;
                float push = (need - Mathf.Abs(dz)) * 0.5f;
                a.transform.position = new Vector3(pa.x, pa.y, pa.z - sign * push);
                b.transform.position = new Vector3(pb.x, pb.y, pb.z + sign * push);
            }
        }
        GcProbe.End(GcProbe.Region.LaneSeparate, gcProbe);
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
        //【阶段一定要从盒子主人身上取】盒子属于挨打的一方，而调用本函数的this是打人的一方；
        //攻击盒的OnTriggerEnter从来不会把主人自己的受击盒记进_boxTouches，所以读this那份列表永远不可能读到开启这个盒子的阶段
        foreach (BeHitBoxTurnOnInfo info in box.master.action.ActiveBeHitBoxInfo)
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
        //attackPhase存的是AttackInfo的phase号而不是attacks数组下标，和AddHitRecord一样按phase查
        //（表里两者恰好一直是0，所以拿它当过下标也没出事）
        int attackIdx = action.IndexOfAttack(bestBoxInfo.attackPhase);
        if (attackIdx >= 0) attackPhase = action.CurrentAction.attacks[attackIdx];
        GcProbe.End(GcProbe.Region.CanAttackTargetNow, gcProbe);
        return true;
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
        //下落重量按逻辑帧累加，换算成位移时才乘Tick；空中才积，一落地就归零
        if (State == MoveState.Airborne) _curWeight += weight;
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

/// <summary>
/// 角色的移动状态。
/// 规划里的第三个状态Landing（落地后摇）这期没做：现在整个工程里没有跳跃，
/// 唯一能离地的是一记击退的0.2米、统共两三个逻辑帧，落地动作既没动画也没表条目，
/// 做出来是个没有消费者的分支。等Phase 3把跳跃或者落地动作带出来再接。
/// </summary>
public enum MoveState
{
    /// <summary>
    /// 在地面上：可以自然奔跑，重力不积分
    /// </summary>
    Grounded,
    /// <summary>
    /// 滞空：重力按逻辑帧积分
    /// </summary>
    Airborne,
}
