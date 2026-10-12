
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class GameMain : MonoBehaviour
{
    /// <summary>
    /// 使用的主要相机
    /// </summary>
    public Camera usingCam;
    /// <summary>
    /// 玩家角色，其实应该是动态创建的，不过这个demo里面就偷个懒了
    /// </summary>
    public CharacterObj player;
    /// <summary>
    /// 敌人，也是偷懒的，正确的做法，应该是把所有人放在一个列表里统一处理
    /// 至少是走代码创建，而不是直接丢在场景里，然后拖到inspector
    /// </summary>
    public List<CharacterObj> enemy;

    public Text inputText;

    /// <summary>
    /// 敌兵会不会自己动。true 是移植的目标（清版动作当然要打），关掉就退回 demo 那种"站桩靶子"，
    /// 于是同一份表既能演"AI 出不出招"也能演"这记打击手感对不对"
    /// </summary>
    public bool enemiesFightBack = true;

    /// <summary>
    /// 场上的人由波次表生成（`Resources/GameData/Wave.json`）。false 就退回场景里预摆好的那几只，
    /// 于是 demo 那关还能原样跑；true 的时候预摆的那些会让位，免得同一批兵出现两次
    /// </summary>
    public bool useWaveTable = true;

    /// <summary>推哪一关。表里没有这个 id 就退回场景预摆的那几只（并喊一条）</summary>
    public string firstLevelId = "Level1";

    /// <summary>
    /// 限定渲染帧率，验收Phase 2用的：填30/60/120各跑一次，逻辑行为必须完全一致，0表示不限制。
    /// 必须是真正的限帧——编辑器的Playback Rate只改Time.timeScale，那会连逻辑时钟一起放慢，测不出东西
    /// </summary>
    public int targetFrameRate = 0;

    /// <summary>
    /// 渲染帧与逻辑帧之间的差额，只有这里认识Time.deltaTime（换算本身在 LogicClock.TicksDue）
    /// </summary>
    private double _accumulator = 0;

    /// <summary>
    /// 场上所有人的合成，为了每个逻辑帧按同一顺序走一遍。第 0 个永远是玩家，其余是敌兵。
    /// 是 List 而不是数组：波次表会在战斗中往里加人（`SpawnUnit`），而热路径只读不写。
    /// 每拍遍历用 foreach（List 的迭代器是结构体，不装箱不分配）
    /// </summary>
    private readonly List<CharacterObj> _all = new List<CharacterObj>(16);

    /// <summary>
    /// 相交采样的常驻缓冲区。命中采样每逻辑帧要查若干个攻击盒，NonAlloc系列必须由调用方备好数组；
    /// 容量64是"同屏受击盒总数"的上限，一个角色2个受击盒的话能撑到32个角色，
    /// 真溢出了AttackHitBox会当场喊（它只报一次，不会刷日志）
    /// </summary>
    private Collider[] _boxBuffer;

    /// <summary>
    /// 逻辑层随机数的种子（敌兵概率格挡这类判定只准从 `Dice` 抽，见那个文件的说明）。
    /// 默认是个常量，于是"什么都不改"的情况下每次运行抽到的都是同一串数——
    /// 调手感时能一帧不差地复现同一局，这比每次换一种随机分布有用得多。
    /// 想要每次不一样就随便改一个数，或者在这里换成 `(uint)System.Environment.TickCount`
    /// </summary>
    public uint randomSeed = Dice.DefaultSeed;

    /// <summary>
    /// 相机在场景里摆好的那个位置，Awake 里记一次：LateUpdate 每帧都要改写相机位置，
    /// 而它只该动横向，高度与纵深必须有个不被污染的原始值可回读。
    /// HD-2D 的标定就体现在这里——距离与 fov 决定"一米在屏幕上占多少像素"，
    /// 现在这台相机（fov 69.1°、离角色所在那一列 4 米）在竖直方向上给出约 5.5 个世界单位，
    /// 正好是源工程那台正交相机（size 2.8，也就是 5.6 个单位）的取景高度
    /// </summary>
    private Vector3 _camBase;

    /// <summary>
    /// 帧事件的接收者（声音、特效、镜头震动）。代码里挂而不是场景里摆：
    /// 它不需要在 Inspector 上调任何东西，而手改 .unity 在编辑器不可用的时候是唯一会写坏场景的做法
    /// </summary>
    private CueReceiver _cueReceiver;

    /// <summary>
    /// 场上敌兵的决策器。代码里挂（跟接收者同一个理由：不动 .unity 也能跑起来），
    /// 次序按 `_all` 里入场的先后，所以每一只拿到的随机与配额都只取决于入场次序，不取决于 Awake 次序
    /// </summary>
    private readonly List<EnemyMind> _minds = new List<EnemyMind>(8);

    /// <summary>
    /// 波次推进。总是存在，没表的时候 `level` 与 `spawn` 都是空的、每一拍立刻返回——
    /// 于是"没有波次表的老场景"与"有波次表的新关卡"是同一份代码
    /// </summary>
    private readonly WaveManager _waves = new WaveManager();

    /// <summary>
    /// 波次表里会出现的预制体，Awake 里一次性载好。
    /// 【为什么不留到生成那一拍再载】：那会把一次磁盘 IO 塞进"上一波最后一个人倒下"那一拍，
    /// 手感上就是清空那一下卡半秒——与 `CueReceiver` 预热全表同一个理由
    /// </summary>
    private readonly Dictionary<string, GameObject> _spawnPrefabs = new Dictionary<string, GameObject>();

    void Awake()
    {
        //随机源必须在第一个逻辑帧之前定好种子，否则头几次抽样用的还是静态初始值
        Dice.Seed(randomSeed);

        //限帧必须在关垂直同步之后才生效
        if (targetFrameRate > 0)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = targetFrameRate;
        }

        //就暂时写在这里读取吧，demo自然就偷懒了
        GameData.Load();
        //接收者必须在表读完之后才挂：它的 Awake 要扫一遍全表，把会被响到的音效载好、把特效的池备够
        _cueReceiver = gameObject.AddComponent<CueReceiver>();
        _cueReceiver.Init();
        CueDispatch.Handler = _cueReceiver.OnCue;
        //同样是偷懒，把数据全塞给角色。每个角色拿到的是**自己会的那一份**：
        //actionSubset 留空就是全表（demo 那两个 3D 角色），填了 id 就只有那几招（移植进来的精灵玩家）
        GiveActionsTo(player, "BoxingStand");
        _all.Add(player);

        WaveTable.Load();
        LevelRow level = useWaveTable ? WaveTable.Get(firstLevelId) : null;
        if (useWaveTable && level == null)
            Debug.LogWarning("[波次] 勾了 useWaveTable 但表里没有关卡 「" + firstLevelId
                             + "」，退回场景里预摆的那几只");

        if (level != null)
        {
            WarmSpawnPrefabs(level);
            _waves.level = level;
            _waves.spawn = SpawnUnit;
            _waves.StartFirstWave();
        }
        else
        {
            for (int i = 0; i < enemy.Count; i++)
            {
                if (enemy[i] == null) continue;
                GiveActionsTo(enemy[i], "TurnBack");
                _all.Add(enemy[i]);
                AttachMind(enemy[i]);
            }
        }

        _boxBuffer = new Collider[64];

        //相机自己在场景里摆好的位置先记下来，LateUpdate 之后每帧只改横向那一个分量
        _camBase = usingCam.transform.position;
    }

    private void Update()
    {
        //Unity的按键状态每渲染帧只能poll一次，所以采样在逻辑时钟之外
        foreach (CharacterObj c in _all)
            c.input.SampleRenderFrame();

        int ticks = LogicClock.TicksDue(ref _accumulator, Time.deltaTime);
        for (int i = 0; i < ticks; i++)
        {
            LogicClock.Advance();
            TickAll();
        }
        
        //UI简单处理
        inputText.text = player.input.InputText();
    }

    /// <summary>
    /// 推进一个逻辑帧，顺序是钉死的：输入→动作→判定盒→相交采样→命中结算→计时与移动→同轨推挤→抓取约束。
    /// 以前这五家各写各的Update，谁先谁后由Unity决定，同一份代码换台机器就会差一帧。
    /// 命中结算放在动作推进之后，所以一记打击派生出来的动作要等下一个逻辑帧才生效（约16.7毫秒）。
    /// 相交采样夹在判定盒与命中结算之间：盒子刚被刷新过（开关和轨迹摆位都在那一拍），查到的就是"这一拍开着的那些盒"，
    /// 而它读的位置是上一拍Move()写下的，这个滞后和以前物理回调的滞后一致
    /// 同轨推挤放在移动之后：所有角色都写完了本帧的位置才推得准，而且它只动 z，
    /// 地面探测算出的 groundY 与 z 无关，所以推完不用再探地
    /// 抓取约束排在最后：被举者的坐标要按举人者**这一帧**的最终位置算，推挤已经把他排除在外了，
    /// 再往前放就会被推挤或移动拍改写一次，手里的人每拍抖一下
    /// </summary>
    private void TickAll()
    {
        //波次拍：上一波清空了就按表生成下一批。排在整条链最前面，
        //于是新生成的人这一拍就能走完输入→AI→动作→…，不会白站一拍
        _waves.Tick(LogicClock.Frame);

        foreach (CharacterObj c in _all)
            c.input.Tick();

        //AI 拍：决策器往 `_input` 里登记按键，登记的时机与玩家那一拍同一次序、同一个口子，
        //所以它在动作拍之前——这一拍的输入被这一拍的动作读到，不给 AI 单独多一拍迟钝
        if (_minds.Count > 0)
        {
            long countProbe = GcProbe.Begin(GcProbe.Region.EnemyCount);
            //先数"已经有几只扑上去了"（当前动作是攻击分类的敌兵），再逐只做决策。
            //数在前面是因为所有人要看到的是同一个数——逐个决策时边数边打，第 4 只会看见第 3 只还没进去的状态
            int busy = 0;
            for (int i = 0; i < _minds.Count; i++)
            {
                ActionInfo current = _minds[i].self.action.CurrentAction;
                if (current.catalog == "Punch" || current.catalog == "Kick" || current.catalog == "Skill") busy++;
            }
            GcProbe.End(GcProbe.Region.EnemyCount, countProbe);

            long aiProbe = GcProbe.Begin(GcProbe.Region.EnemyThink);
            for (int i = 0; i < _minds.Count; i++) _minds[i].Tick(busy);
            GcProbe.End(GcProbe.Region.EnemyThink, aiProbe);
        }

        foreach (CharacterObj c in _all)
            c.action.Tick();
        foreach (CharacterObj c in _all)
            TickBoxes(c);

        //相交采样：命中不再等物理回调，每个逻辑帧主动查一次
        CharacterObj.SampleOverlaps(_all, _boxBuffer);

        //处理攻击和碰撞（敌兵现在会先动，判的就是他动完之后这一拍的位置）
        DealWithAttacks();

        foreach (CharacterObj c in _all)
        {
            long gcProbe = GcProbe.Begin(GcProbe.Region.CharacterTick);
            c.Tick();
            GcProbe.End(GcProbe.Region.CharacterTick, gcProbe);
        }

        CharacterObj.SeparateLanes(_all);

        //最后一拍：被举者跟着举人者摆到手边，同时判断约束该不该解除
        CharacterObj.ApplyHolds(_all);

        //表现回收：到点的特效关掉、镜头震动退掉。放在所有逻辑之后是因为它读的
        //是这一拍派发出去的 cue 的到期时间，先逻辑后表现才不会有"这一拍的 cue 下一拍才被记进池子"
        long cueProbe = GcProbe.Begin(GcProbe.Region.PoolTick);
        _cueReceiver.Tick();
        GcProbe.End(GcProbe.Region.PoolTick, cueProbe);
    }

    /// <summary>
    /// 把一个角色"会的动作"交给它的控制器。
    /// 【默认动作优先取 prefab 上填的】：移植进来的精灵玩家开场是 PlayerIdle，跟 demo 的 BoxingStand 不是一回事；
    /// 没填就退回 GameMain 里原来写死的那两个，免得为了加角色去改场景
    /// </summary>
    private static void GiveActionsTo(CharacterObj c, string defaultFallback)
    {
        string startId = string.IsNullOrEmpty(c.defaultActionId) ? defaultFallback : c.defaultActionId;
        c.action.SetAllActions(GameData.ActionsFor(c.actionSubset), startId);
    }

    /// <summary>
    /// 按表里的名字生成一个敌兵。走的是与场景里那几只完全相同的路：认盒子、给他自己那份子集、挂决策器
    /// </summary>
    private CharacterObj SpawnUnit(string prefabName, Vector3 at)
    {
        GameObject prefab;
        if (!_spawnPrefabs.TryGetValue(prefabName, out prefab) || prefab == null) return null;
        CharacterObj unit = PlaceCharacter(prefab, at, "TurnBack");
        _all.Add(unit);
        AttachMind(unit);
        return unit;
    }

    /// <summary>
    /// 把一个预制体放上场的全部动作：实例化、认判定盒、交给他自己那一份动作表。
    /// 【为什么是静态的】：这样用例能直接打它，而不是在测试里照抄一份生产代码
    /// （`CombatPipeline.ResolveHit` 同一 lessons，见 `Assets/Scripts/AGENTS.md`）
    /// </summary>
    public static CharacterObj PlaceCharacter(GameObject prefab, Vector3 at, string fallbackDefaultId)
    {
        GameObject go = Instantiate(prefab, at, Quaternion.identity);
        CharacterObj c = go.GetComponent<CharacterObj>();
        //播放模式里 Instantiate 已经把 Awake 跑完了，Awake 里就调 BindBoxes；
        //编辑模式（用例）不跑 Awake，所以这里补一次。BindBoxes 自己认幂等（Units 里查重），重复调无害
        if (c.AttackBoxes == null) c.BindBoxes();
        GiveActionsTo(c, fallbackDefaultId);
        return c;
    }

    private void AttachMind(CharacterObj e)
    {
        if (!enemiesFightBack) return;
        EnemyMind mind = e.gameObject.AddComponent<EnemyMind>();
        mind.self = e;
        mind.target = player;
        _minds.Add(mind);
    }

    /// <summary>波次推进的状态，给调试 UI 与以后的 HUD 看（也给了批处理探针一个不碰私有字段的口子）</summary>
    public WaveManager Waves => _waves;

    private void WarmSpawnPrefabs(LevelRow level)
    {
        for (int w = 0; w < level.waves.Length; w++)
        {
            SpawnRow[] rows = level.waves[w].enemies;
            for (int i = 0; i < rows.Length; i++)
            {
                if (string.IsNullOrEmpty(rows[i].prefab) || _spawnPrefabs.ContainsKey(rows[i].prefab)) continue;
                //WaveTable.Load 已经校验过取不取到，这里只是把那次载入的结果存下来给生成那一拍用
                _spawnPrefabs[rows[i].prefab] = Resources.Load<GameObject>(rows[i].prefab);
            }
        }
        //场景里预摆的那几只让位：波次表就是它们的替代品，留着等于同一批兵出现两次
        for (int i = 0; i < enemy.Count; i++)
            if (enemy[i] != null) Destroy(enemy[i].gameObject);
    }

    private static void TickBoxes(CharacterObj c)
    {
        long gcProbe = GcProbe.Begin(GcProbe.Region.BoxPose);
        foreach (AttackHitBox box in c.AttackBoxes) box.Tick();
        foreach (BeHitBox box in c.BeHitBoxes) box.Tick();
        GcProbe.End(GcProbe.Region.BoxPose, gcProbe);
    }

    private void LateUpdate()
    {
        //只跟横向。高度跟着抬的话跳起来等于没跳（相机一起升），
        //纵深跟着走的话远车道的人永远和近车道一样大，HD-2D 的透视就白给了。
        //高度与纵深就用场景里摆好的那一个值，真正的跟随相机（含关卡边界推进）在 Step 2
        usingCam.transform.position = new Vector3(_camBase.x + player.transform.position.x,
            _camBase.y + (_cueReceiver ? _cueReceiver.ShakeOffsetY : 0f), _camBase.z);
    }

    /// <summary>
    /// 处理每个角色的本帧攻击
    /// </summary>
    private void DealWithAttacks()
    {
        //过的是 `_all` 而不是场景里那份 `enemy`：波次表生成的人不在 Inspector 上，
        //第 0 个是玩家自己所以从 1 开始
        for (int index = 1; index < _all.Count; index++)
        {
            CharacterObj ene = _all[index];
            if (ene == null) continue;
            //玩家对敌人
            if (player.CanAttackTargetNow(ene, out AttackInfo pAttackPhase, out BeHitBoxTurnOnInfo eDefenseInfo,
                    out AttackHitBox pAttackBox, out BeHitBox eneBox)) 
            {
                //先判断，如果命中记录表示没法命中，那么这个框的信息无效
                if (player.CanHitTargetAgain(ene, pAttackPhase.phase))
                {
                    CombatPipeline.ResolveHit(player, ene, in pAttackPhase, in eDefenseInfo, pAttackBox, eneBox);
                }
            }
            //敌人对玩家
            if (ene.CanAttackTargetNow(player, out AttackInfo eAttackPhase, out BeHitBoxTurnOnInfo pDefenseInfo,
                    out AttackHitBox eAttackBox, out BeHitBox playerBox)) 
            {
                //先判断，如果命中记录表示没法命中，那么这个框的信息无效
                if (ene.CanHitTargetAgain(player, eAttackPhase.phase))
                {
                    CombatPipeline.ResolveHit(ene, player, in eAttackPhase, in pDefenseInfo, eAttackBox, playerBox);
                }
            }
        }
    }
}
