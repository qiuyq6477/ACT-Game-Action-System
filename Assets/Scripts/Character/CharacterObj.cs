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
    /// 地面层的掩码，BindBoxes里解析：LayerMask.NameToLayer不许出现在字段初始化器里（那等于在.cctor里调），
    /// 而且层不存在时它返回-1，1 << -1在C#里是未定义行为，所以只能在运行期判完再生成
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
    /// 只在落地的那一个逻辑帧为true（每次Tick开头清零）。
    /// 消费者是 ActionController：落地那一拍按表里的 landActionId 换动作（落地后摇、落地音效都从这儿走）
    /// </summary>
    public bool JustLanded { get; private set; }

    /// <summary>
    /// 下落的重量
    /// </summary>
    private float _curWeight = 0;

    /// <summary>
    /// 攻击框 → 这一逻辑帧查到的受击框。
    /// 【每拍清空重填，不是增量维护】：以前的OnTriggerEnter/Exit是增量语义，物理步与逻辑帧错频时
    /// Exit可能永远不来，一条已经解除的接触就脏在表里了。清空重填让"这一拍打到谁"
    /// 完全等于"这一拍查询的结果"。
    /// 【列表在Awake里一次性建齐，运行期只Clear】：Clear保留容量，所以跑热之后一帧都不分配
    /// </summary>
    private Dictionary<AttackHitBox, List<BeHitBox>> _boxTouches = new Dictionary<AttackHitBox, List<BeHitBox>>();
    
    /// <summary>
    /// 当前动作的命中记录，更换动作就清空了这个列表了。
    /// 【元素是 struct，列表只在角色构造时分配这一次】：8 的容量按"一个动作能记下几个目标"给，
    /// 真超了才扩容一次（那是一次性的，不是每记命中一个对象——以前就是那种，
    /// 见 `Doc/issue/hit-event-heap-alloc.md`）
    /// </summary>
    public List<HitRecord> HitRecords { get; private set; } = new List<HitRecord>(8);

    /// <summary>
    /// 数值组件：血量、攻击力、防御。没有这个组件的角色不参与伤害结算（demo 里那些只会挥拳的
    /// 靶子因此可以一直挨打不掉血）。在 `BindBoxes` 里认一次，编辑态用例也走这条路
    /// </summary>
    public CombatStats Stats { get; private set; }

    /// <summary>
    /// 霸体条。没有这个组件 = 没有霸体，每一记都打断动作
    /// </summary>
    public PoiseGauge Poise { get; private set; }

    /// <summary>
    /// 血打空了。命中查询一看到它就直接收工——死人不再挨第二下，
    /// 也就不会被重复结算、更不会在断气之后再被路由一次 Death
    /// </summary>
    public bool IsDead => Stats && Stats.IsDead;

    /// <summary>
    /// 身上的攻击盒与受击盒，Awake里收集好，供GameMain的时钟按固定顺序刷新
    /// </summary>
    public AttackHitBox[] AttackBoxes { get; private set; }
    public BeHitBox[] BeHitBoxes { get; private set; }

    /// <summary>
    /// 场上所有角色的常驻注册表。取消条件（"近处有没有一个倒在地的敌人"）、以后的 AI 决策
    /// 和"离我最近的目标"这类查询都从这儿走，**不许**用 `FindGameObjectsWithTag`：
    /// 它每次调用都全场景扫一遍并 new 一个数组，热路径红线。
    /// 【注册点在 BindBoxes】：EditMode 不跑 Awake，而 `BindBoxes` 已经是"用例可以自己叫的初始化入口"；
    /// Add 自带去重，所以 Play Mode 里 Awake→BindBoxes 也只留一条。
    /// 【顺序是各角色 Awake 的顺序，跨运行不稳定】：现在的消费者只问"有没有"所以无所谓，
    /// 以后的 AI 排队、目标选择一定要自己按位置或 id 排序，别拿这个列表的次序当平局裁决
    /// </summary>
    private static readonly List<CharacterObj> _units = new List<CharacterObj>();

    /// <summary>
    /// 只读视图，给以后的 AI 和 HUD 用。拿到它**不许**增删——注册表只能由注册/注销改
    /// </summary>
    public static IReadOnlyList<CharacterObj> Units => _units;

    /// <summary>
    /// 这个角色只会的动作 id。留空 = 会全表（demo 里玩家与敌兵就是这样）。
    /// 【为什么要有这个】：全工程一张动作表是"所有角色都会所有招"的偷懒假设，
    /// 移植进来的玩家招式表和敌兵的招式表一旦同表，同一个按键会被两边抢（谁赢取决于填表顺序），
    /// 而 byCatalog 挑受击动作时也会把"敌兵根本不会的招式"端上当候选。
    /// 子集在这里声明，`GameMain` 在 Awake 里据此把每一份表交给各自的 `ActionController.SetAllActions`
    /// </summary>
    [Tooltip("这个角色只会的动作 id，留空表示会全表")]
    public string[] actionSubset;

    /// <summary>开场动作 id</summary>
    [Tooltip("开场动作的 id")]
    public string defaultActionId;
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
    /// 那个人在我的正脸那一侧吗（只看横向：纵深是车道，与"面向"无关，移植后面向仍然只有两向）。
    /// 【格挡要用它】：源工程的格挡是 `IsDefending(invertedDir)`，背后挨刀不算挡住；
    /// 这里不看表里的 forceDir，因为 forceDir 是"这一记设计上算从哪儿打"，
    /// 而能不能挡住取决于两个人此刻实际站哪儿
    /// </summary>
    /// <summary>
    /// 受击白闪。组件是这里按需挂的（跟 `CombatStats` 的"prefab 上有就认"不同）：
    /// 它不需要美术在 prefab 上调任何东西，而 prefab 归编辑器管、动它就得走一次导入
    /// </summary>
    private HitFlash _hitFlash;

    public void BeginHitFlash(int frames)
    {
        //组件在 BindBoxes 里就挂好了：命中那一拍只许改几个数，不许 AddComponent、也不许 GetComponent
        if (frames <= 0 || _hitFlash == null) return;
        _hitFlash.Begin(frames);
    }

    /// <summary>每逻辑帧一拍，由 `GameMain` 的表现回收拍统一叫</summary>
    public void TickHitFlash()
    {
        if (_hitFlash != null) _hitFlash.Tick();
    }

    private void EnsureFlash()
    {
        if (_hitFlash == null)
        {
            _hitFlash = GetComponent<HitFlash>();
            if (_hitFlash == null)
            {
                _hitFlash = gameObject.AddComponent<HitFlash>();
                _hitFlash.Init();
            }
        }
    }

    public bool FacesToward(CharacterObj other)
    {
        if (!other) return false;
        float facing = Inversed ? -1f : 1f;
        //正好重叠（横向差 0）算在正脸：宁可放过挨刀的那一下，也不要"明明对着站却挡不住"
        return (other.transform.position.x - transform.position.x) * facing >= 0f;
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

        BindBoxes();
    }

    /// <summary>
    /// 认下身上的判定盒：谁是主人、tag的位掩码、各自的碰撞体，以及每拍重填的接触列表。
    /// 【为什么单独成一个公开方法而不是留在Awake里】：EditMode下 Instantiate 不触发 Awake
    /// （Unity只在Play Mode里调回调），而相交采样的用例必须在编辑态把这套东西立起来
    /// ——见 Assets/Editor/ActionFrameClockTests 的第11区。幂等，Awake和用例都能调
    /// </summary>
    public void BindBoxes()
    {
        //地面层在这里解析，不在Awake里：跳跃与落地的用例同样要在编辑态跑，
        //而地面向的探测取的是这个掩码——留在Awake里意味着编辑态永远踩不到地面（见 Units 的同款理由）
        //放在early-return之前：NameToLayer是纯查表，重复调几次初始化期的开销可以忽略
        //这一层不存在的话探测永远落空，角色会一路往下掉，而这种错法本来没有任何报错，所以这里必须喊一声
        int groundLayer = LayerMask.NameToLayer(GroundLayerName);
        if (groundLayer < 0)
            Debug.LogError("工程里没有 " + GroundLayerName + " 这一层：请在 Project Settings → Tags and Layers 里加出来，" +
                           "并把地面碰撞体放到这一层，否则角色踩不到任何东西");
        else
            _groundMask = 1 << groundLayer;

        //数值与霸体组件同样在这里认一次（同一个理由：编辑态用例不跑 Awake，而结算用例要能在编辑态跑）。
        //两个都是可选的——没挂 CombatStats 的角色不参与伤害结算，没挂 PoiseGauge 就是没有霸体。
        //【位置在 early-return 之前】：用例和 Step 2 的对象池都会"先有角色、后挂组件"，
        //放在护栏后面就再也认不到了；而满值必须跟认组件在同一次里给，否则编辑态读到的 Hp 是 0，
        //一记打上去就是"已经死了"
        Stats = GetComponent<CombatStats>();
        Poise = GetComponent<PoiseGauge>();
        EnsureFlash();
        //幂等赋值，重复调没有副作用（组件自己的 Awake 在 Play Mode 里还会再来一次，同样只是填回满值）
        if (Stats) Stats.Revive();
        if (Poise) Poise.Refill();

        if (AttackBoxes != null) return;

        //注册表也在这里挂上：取消条件那条路径要用（见 Units 的注释），而 EditMode 不跑 Awake
        if (!_units.Contains(this)) _units.Add(this);

        BeHitBox.EnsureQueryMask();
        List<AttackHitBox> attackBoxes = new List<AttackHitBox>();
        List<BeHitBox> beHitBoxes = new List<BeHitBox>();
        Transform[] transforms = GetComponentsInChildren<Transform>();
        foreach (Transform trans in transforms)
        {
            AttackHitBox ahb = trans.GetComponent<AttackHitBox>();
            if (ahb)
            {
                ahb.master = this;
                //顺便把Inspector上的tag换成位掩码，绑定一定早于所有Tick，判定不会踩到没烘焙的盒子
                ahb.BakeTagMask();
                //命中采样要按形状查，把碰撞体一起认下来，免得运行期再GetComponent
                ahb.BindCollider(trans.GetComponent<Collider>());
                //接触列表在这里建齐，运行期只Clear不Add键，这是热点路径0分配的前提
                _boxTouches.Add(ahb, new List<BeHitBox>());
                attackBoxes.Add(ahb);
            }
            BeHitBox bhb = trans.GetComponent<BeHitBox>();
            if (bhb)
            {
                bhb.master = this;
                bhb.BakeTagMask();
                Collider col = trans.GetComponent<Collider>();
                bhb.RegisterCollider(col);
                //受击盒必须由物理碰撞体撑着：查询返回的是Collider，没有碰撞体的盒子在这套采样里等于不存在。
                //只配了tag没配碰撞体是配表事故，而它的表现是"这一拳莫名其妙打不着"，所以这里要当场喊
                if (!col) Debug.LogError(name + " 的受击盒 " + bhb.name + " 身上没有碰撞体，命中采样查不到它");
                beHitBoxes.Add(bhb);
            }
        }

        //盒子由GameMain的时钟按固定顺序刷新，所以这里要把两组盒子留在身上，免得每帧再遍历一次子节点
        AttackBoxes = attackBoxes.ToArray();
        BeHitBoxes = beHitBoxes.ToArray();
    }

    /// <summary>
    /// 从注册表里摘掉自己。销毁是唯一的注销时机：不在 OnDisable 里摘，
    /// 因为"暂时看不见"和"不在这局里"是两件事，被隐藏的目标仍然算对手在场（追击它的条件不该凭空失效）
    /// </summary>
    private void OnDestroy()
    {
        _units.Remove(this);
    }

    /// <summary>
    /// 除自己以外，近处有没有一个当前动作属于某个分类的角色。
    /// 【匹配的是 catalog 而不是动作 id】：同一件"被打倒在地上"的事，玩家和敌兵是两行不同的动作
    /// （PlayerKnockDownGrounded / 敌兵那行），门禁要问的是"有没有人躺着"，不是"有没有人正播这一行动作"。
    /// 用 id 匹配等于把这条门禁绑死在某一行的名字上，敌兵改个 id 就悄悄失效
    /// 【距离只算 x 与 z】：y 归地面探测管，用它做距离会把"同一个车道前后站"算成很远
    /// 【用平方距离】：比较大小不需要开方，每拍都 sqrt 一次不值
    /// </summary>
    /// <param name="self">发起查询的角色，它自己不算</param>
    /// <param name="catalog">要匹配的动作分类</param>
    /// <param name="range">半径（米）</param>
    /// <returns></returns>
    public static bool AnyOtherInCatalog(CharacterObj self, string catalog, float range)
    {
        if (!self || string.IsNullOrEmpty(catalog)) return false;
        Vector3 from = self.transform.position;
        float sqrRange = range * range;
        for (int i = 0; i < _units.Count; i++)
        {
            CharacterObj other = _units[i];
            if (other == null || other == self) continue;
            //还没绑定过的角色（比如同一拍里刚生成、Awake 没跑）不该被当成"场上的敌人"
            if (other.action == null) continue;
            if (other.action.CurrentAction.catalog != catalog) continue;
            Vector3 d = other.transform.position - from;
            if (d.x * d.x + d.z * d.z <= sqrRange) return true;
        }

        return false;
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
            //HitRecords：struct 在 List 里改的是取出来的那份拷贝，所以必须写回去（见 HitRecord 的类注释）
            for (int i = 0; i < HitRecords.Count; i++)
            {
                HitRecord record = HitRecords[i];
                record.Tick();
                HitRecords[i] = record;
            }
            //韧性也在硬直期外才回：正被打得站不稳的人不该一边挨打一边把霸体回满
            if (Poise) Poise.Tick();
        }

        Move();
    }

    /// <summary>
    /// 算出这一逻辑帧的位移，再用地面探测决定脚落在哪里
    /// </summary>
    private void Move()
    {
        //被举着的人不自己算位置：地面探测会把他snap回地面，而约束要的是"悬在举人者手边"，
        //两件事同拍发生就会被抹平。他的坐标由 ApplyHolds 那一拍统一写
        if (HeldBy != null) return;

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
    public static void SeparateLanes(IReadOnlyList<CharacterObj> all)
    {
        long gcProbe = GcProbe.Begin(GcProbe.Region.LaneSeparate);
        for (int i = 0; i < all.Count; i++)
        {
            CharacterObj a = all[i];
            if (!a) continue;
            for (int j = i + 1; j < all.Count; j++)
            {
                CharacterObj b = all[j];
                if (!b) continue;
                //被举着的人不参与推挤：他的位置由约束那一拍说了算，推一下会被下一拍的约束拉回来，
                //表现是"手里的人在抖"
                if (a.HeldBy != null || b.HeldBy != null) continue;

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
    /// 攻击盒这一拍该摆在哪、摆多大：从当前动作里开着这个盒子的攻击阶段读烘焙好的轨迹。
    /// 【false = 别动这个盒子】：要么没有表项开启它，要么开启它的表项没填轨迹（本工程原有的做法，
    /// 盒子就待在 prefab 里摆好的位置上）。
    /// 【多个阶段同时开启同一个盒子时取优先级最高的那条】，跟 GetAttackBoxTempPriority 同一个规则——
    /// 取第一条会让"哪条表项说了算"取决于策划的填表顺序，那是查不出来的错
    /// </summary>
    /// <param name="boxMask">攻击盒的tag掩码</param>
    /// <param name="position">该摆的局部位置</param>
    /// <param name="size">该摆的局部尺寸</param>
    /// <returns></returns>
    public bool TryGetAttackBoxPose(int boxMask, out Vector3 position, out Vector3 size)
    {
        position = Vector3.zero;
        size = Vector3.zero;
        bool found = false;
        int best = 0;
        foreach (AttackBoxTurnOnInfo info in action.ActiveAttackBoxInfo)
        {
            if ((info.tagMask & boxMask) == 0 || info.bakedPosition == null) continue;
            if (found && info.priority <= best) continue;

            //帧号要夹住：没有 autoNext 的动作会一路数过 durationFrames，而轨迹数组只到那一帧
            int frame = Mathf.Clamp(action.CurrentFrame, 0, info.bakedPosition.Length - 1);
            position = info.bakedPosition[frame];
            size = info.bakedSize[frame];
            best = info.priority;
            found = true;
        }

        return found;
    }

    /// <summary>
    /// 追加一条攻击框命中受击框的信息
    /// 这里可不管框active与否
    /// </summary>
    /// <param name="attackHitBox">攻击框</param>
    /// <param name="targetBox">受击框</param>
    public void OnAttackBoxHit(AttackHitBox attackHitBox, BeHitBox targetBox)
    {
        //键在Awake里已经建齐了，这里只有查表和Add，一次分配都没有
        if (!_boxTouches.TryGetValue(attackHitBox, out List<BeHitBox> list)) return;
        if (!list.Contains(targetBox)) list.Add(targetBox);
    }

    /// <summary>
    /// 每逻辑帧一次的相交采样：把角色的位移同步进物理场景，清空上一拍的接触，
    /// 再让每个开启中的攻击盒自己去查一遍受击盒。
    /// 【为什么放在TickBoxes之后、命中结算之前】：盒子的Active刚被刷新，查出来的就是"这一拍开着的那些盒"；
    /// 【为什么在移动拍之前】：位置来自上一拍的Move()，和以前物理回调读到的滞后量一致，
    /// 换时基不该顺手改掉命中早晚，那是另一件事
    /// </summary>
    /// <param name="all">场上全部角色</param>
    /// <param name="buffer">常驻缓冲区，所有角色共用一个就够：一次查询只涉及一个攻击盒</param>
    public static void SampleOverlaps(IReadOnlyList<CharacterObj> all, Collider[] buffer)
    {
        long gcProbe = GcProbe.Begin(GcProbe.Region.SampleOverlaps);
        //角色是直写transform.position的，而工程里m_AutoSyncTransforms=0，
        //不手动同步的话查询看到的还是上一个物理步留下的旧位置，快位移照样会穿盒
        Physics.SyncTransforms();

        for (int i = 0; i < all.Count; i++)
        {
            CharacterObj c = all[i];
            if (!c) continue;
            foreach (KeyValuePair<AttackHitBox, List<BeHitBox>> touch in c._boxTouches)
                touch.Value.Clear();
            for (int b = 0; b < c.AttackBoxes.Length; b++) c.AttackBoxes[b].SampleTouches(buffer);
        }
        GcProbe.End(GcProbe.Region.SampleOverlaps, gcProbe);
    }

    private BeHitBoxTurnOnInfo GetDefensePhaseByBeHitBox(BeHitBox box)
    {
        //按defensePhase的表顺序取第一个跟这个盒子有共同tag的阶段。
        //原来是拿盒子的tag当外层去比，只有当一个盒子带多个tag、而这些tag又被不同阶段开启时
        //两者才会挑到不同的阶段，而那种配表本来就被声明为"听天由命"（见ActionInfo的defensePhase注释）
        //【阶段一定要从盒子主人身上取】盒子属于挨打的一方，而调用本函数的this是打人的一方；
        //采样只会把"别人的受击盒"记进自己的表（bhb.master != master那条），所以读this那份列表永远不可能读到开启这个盒子的阶段
        foreach (BeHitBoxTurnOnInfo info in box.master.action.ActiveBeHitBoxInfo)
        {
            if ((info.tagMask & box.TagMask) != 0)
                return info;
        }
        return new BeHitBoxTurnOnInfo();
    }

    /// <summary>
    /// 这一拍有没有"只打自己人"的攻击开着 —— 也就是这具身体此刻正被当成武器摔着。
    /// 【为什么要单独问一句】：`GameMain.DealWithAttacks` 的常规两条循环只配"玩家↔敌兵"，
    /// 同边的人互相不打。多一种"打自己人"的攻击不能每次都把全场两两配一遍（二十几个敌兵就是几百次查询），
    /// 所以先用这一个便宜的判断（只走当前动作那几条开着的攻击阶段）把参与者筛出来
    /// </summary>
    public bool HasOwnSideAttackOpen()
    {
        AttackInfo[] attacks = action.CurrentAction.attacks;
        if (attacks == null) return false;
        foreach (AttackBoxTurnOnInfo info in action.ActiveAttackBoxInfo)
        {
            int idx = action.IndexOfAttack(info.attackPhase);
            if (idx >= 0 && attacks[idx].hitOwnSideOnly) return true;
        }
        return false;
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
        //死人不再挨第二下：一记多段的大招会在好几拍里反复查到同一个靶子，
        //少了这一条就会在断气之后再路由一次 Death、再扣一次血
        if (target.IsDead) return false;
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
        AttackInfo attack = action.CurrentAction.attacks[idx];

        int at = IndexOfRecord(target, attackPhase);
        if (at < 0)
        {
            HitRecords.Add(new HitRecord(target, attackPhase, attack.canHitSameTarget - 1, attack.hitSameTargetDelayFrames));
            return;
        }
        //struct 元素改的是取出来的那份拷贝，写回去才算数
        HitRecord rec = HitRecords[at];
        rec.CooldownFrames = attack.hitSameTargetDelayFrames;
        rec.CanHitTimes -= 1;
        HitRecords[at] = rec;
    }

    /// <summary>
    /// 按这一记的"同目标可命中次数"记录，此刻还能不能再打他。
    /// 【取代原来的 GetHitRecord(…) == null 判法】：HitRecord 现在是 struct，
    /// 一个找不到的查询不可能返回"空的 struct"和"真记录"两种含义，所以把查询收成一个布尔问题
    /// </summary>
    /// <param name="target">谁被命中</param>
    /// <param name="phase">算是第几阶段的攻击命中的</param>
    public bool CanHitTargetAgain(CharacterObj target, int phase)
    {
        int at = IndexOfRecord(target, phase);
        if (at < 0) return true;    //从没打过他，当然能打
        HitRecord rec = HitRecords[at];
        return rec.CooldownFrames <= 0 && rec.CanHitTimes > 0;
    }

    /// <summary>
    /// 关于某个目标某个阶段的记录在列表里的下标，没有就返回 -1
    /// </summary>
    private int IndexOfRecord(CharacterObj target, int phase)
    {
        int id = target.gameObject.GetInstanceID();
        for (int i = 0; i < HitRecords.Count; i++)
        {
            if (HitRecords[i].UniqueId == id && HitRecords[i].Phase == phase) return i;
        }
        return -1;
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

    /// <summary>
    /// 起跳：给一个向上的初速度（米/秒），并把状态改成空中。0 或负数什么都不做。
    /// 【为什么要手动把状态改成 Airborne】：Move() 里"这一列探不到地面"那条会把 _curWeight 清零
    /// （那是给"走下平台边缘"用的：下落速度从0算起），而起跳的第一拍恰好也探不到地面——
    /// 不先改状态，初速度会在同一拍被自己抹掉，角色只会原地抖一下
    /// </summary>
    /// <param name="upSpeed">向上的初速度，来自表里的 jumpUpSpeed</param>
    public void Jump(float upSpeed)
    {
        if (upSpeed <= 0f) return;
        _curWeight = -upSpeed;   //正数表示往下，所以起跳取负
        State = MoveState.Airborne;
    }

    /// <summary>
    /// 我此刻举着谁，没举着就是 null。派生门禁 HoldingUnit 读的就是它
    /// </summary>
    public CharacterObj Holding => _holding;

    /// <summary>
    /// 谁举着我。不为 null 时自己的移动拍与同轨推挤都要让位，位置由约束那一拍统一写
    /// </summary>
    public CharacterObj HeldBy { get; private set; }

    private CharacterObj _holding;
    private Vector3 _holdOffset;
    private int _holdFramesLeft;
    private long _holdSetFrame = -1;

    /// <summary>
    /// 两端被举起来那一刻各自的动作 id，是"什么时候算挣脱"的基准。
    /// null 表示还没记：约束是在命中结算那一拍建立的，而被举者的换动作只是排进了预约列表，
    /// 要等下一拍的动作拍才真的生效——当场就记的话记到的是挨打之前那个动作，下一拍必然误判成"挣脱了"
    /// </summary>
    private string _heldActionId;

    private string _holdHolderActionId;

    /// <summary>
    /// 表里这一记攻击写了 holdFrames，就打人之后顺手把人举起来。
    /// 不是抓人的攻击（holdFrames 为 0）、或者对面已经被别人举着，就什么都不做
    /// </summary>
    /// <param name="defender">被打中的人，也就是要被举起来的</param>
    /// <param name="attack">这一记攻击的表数据</param>
    public void TryGrab(CharacterObj defender, AttackInfo attack)
    {
        if (attack.holdFrames <= 0) return;
        //一个人不能被两个人同时举着：那样他的坐标每拍被写两次，两人在他身上一人拽一把
        if (defender.HeldBy != null) return;
        SetHoldConstraint(defender, attack.holdOffset, attack.holdFrames);
    }

    /// <summary>
    /// 把 victim 挂到我身上：此后每个逻辑帧他的位置 = 我的位置 + offset。
    /// 【为什么每拍重算而不是摆一次就完】：源工程是在进入举起状态那一刻把敌人摆到位
    /// （PlayerGrabEnemy.Enter 里一次 transform.position），于是玩家一动敌人就留在原地飘着。
    /// 这里改成每拍跟着算，玩家怎么走、被举者就怎么跟，台阶和边界也一样
    /// </summary>
    /// <param name="victim">被举者</param>
    /// <param name="offset">相对我脚底的偏移，x 是"身前"（会按我的朝向镜像），y 是高度，z 是车道（不镜像）</param>
    /// <param name="holdFrames">最多举多少逻辑帧，来自表里的 holdFrames</param>
    public void SetHoldConstraint(CharacterObj victim, Vector3 offset, int holdFrames)
    {
        if (victim == null || victim == this) return;
        //手里已经有人：先放下旧的，一个人一次只举一个
        if (_holding != null) ReleaseHold();

        _holding = victim;
        victim.HeldBy = this;
        _holdOffset = offset;
        _holdFramesLeft = holdFrames;
        _holdSetFrame = LogicClock.Frame;
        _heldActionId = null;
        _holdHolderActionId = null;
    }

    /// <summary>
    /// 解除约束，两端都要解——只解自己这头，被举者还以为自己悬在别人手里，移动拍会一直空着
    /// </summary>
    public void ReleaseHold()
    {
        if (_holding == null) return;
        _holding.HeldBy = null;
        _holding = null;
        _holdFramesLeft = 0;
        _heldActionId = null;
        _holdHolderActionId = null;
    }

    /// <summary>
    /// 抓取约束那一拍，TickAll 的最后一拍：所有角色都写完了本帧坐标，被举者才按举人者的**本帧**位置摆。
    /// 排在同轨推挤之后是必须的，推挤那一拍已经不承认被举着的人（见 SeparateLanes）。
    /// 【三种解除】被举者换了动作（被摔出去、被打死、自己挣脱，在表里都长成"动作 id 变了"这一件事）、
    /// 举人者换的动作没写 keepHold（挨了一记受击、或者投掷已经出手）、表里的 holdFrames 走完。
    /// 【那一拍只摆位不记 id】：见 _heldActionId 的说明
    /// </summary>
    /// <param name="all">GameMain 手里那份全体角色</param>
    public static void ApplyHolds(IReadOnlyList<CharacterObj> all)
    {
        long gcProbe = GcProbe.Begin(GcProbe.Region.HoldApply);
        for (int i = 0; i < all.Count; i++)
        {
            CharacterObj holder = all[i];
            if (!holder) continue;
            CharacterObj victim = holder._holding;
            if (victim == null) continue;

            if (--holder._holdFramesLeft <= 0)
            {
                holder.ReleaseHold();
                continue;
            }

            string holderId = holder.action.CurrentAction.id;
            string victimId = victim.action.CurrentAction.id;
            if (holder._heldActionId == null)
            {
                if (LogicClock.Frame > holder._holdSetFrame)
                {
                    holder._heldActionId = victimId;
                    holder._holdHolderActionId = holderId;
                }
            }
            else if (victimId != holder._heldActionId)
            {
                holder.ReleaseHold();
                continue;
            }
            else if (holderId != holder._holdHolderActionId)
            {
                //举人者还在抓取系动作里（举起→抓取拳→回到举起），约束继续，只把基准换成新动作
                if (!holder.action.CurrentAction.keepHold)
                {
                    holder.ReleaseHold();
                    continue;
                }

                holder._holdHolderActionId = holderId;
            }

            //x 按朝向镜像，z 不镜像：x 是"身前身后"这个面向空间的轴，z 是屏幕纵向的车道，
            //跟朝哪边无关（同 NatureMove 里对纵深那一项的处理）
            Vector3 p = holder.transform.position;
            float forward = holder.Inversed ? -holder._holdOffset.x : holder._holdOffset.x;
            victim.transform.position = p + new Vector3(forward, holder._holdOffset.y, holder._holdOffset.z);
        }

        GcProbe.End(GcProbe.Region.HoldApply, gcProbe);
    }
}

/// <summary>
/// 角色的移动状态。
/// 跳跃已经由招式表带出来了（ActionInfo.jumpUpSpeed / landActionId），
/// 但这里仍然只有两个状态：落地后摇是**一行动作**（PlayerLand，硬直和镜头抖都写在它的 cue 里），
/// 不是一种地形状态——脚在地面上，这就够了。
/// 规划里的第三个状态Landing要是哪天出现"落地瞬间可以超级装甲"这类需求，再从 JustLanded 里长出来
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
