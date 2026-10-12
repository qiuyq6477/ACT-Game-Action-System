using UnityEngine;

/// <summary>
/// 敌兵的决策——源工程 `EnemyBehaviour` + 那五个状态在本工程里的重做。
///
/// 解决的问题：场上的敌兵不会动。源工程用一整套状态机（Idle / MoveToTargetAndAttack / Attack /
/// KeepDistance / MoveTo）在 `Update` 里按墙钟跑，本工程要的是"第几拍想了什么、第几拍出拳"
/// 这种只取决于逻辑帧号的东西，所以这里一行 `Update` 都不写，改由 `GameMain.TickAll` 的 AI 拍调用。
///
/// 【为什么不是状态机】：源工程的状态类各自持有"怎么走、走多久"的位移逻辑，
/// 而本工程的位移真源在招式表里（`NatureMove` + `GoStraight`）。这里只输出**按键**：
/// 想往前走就登记 `KeyMap.Forward`，想出手就登记 `KeyMap.Punch`——
/// 与玩家走同一个口子（`InputToCommand.AddInput`，切片 1.6 当初就是为 AI/回放留的），
/// 于是"敌兵会不会动"与"这一招出不出得来"是两件事，后者仍然完全由表说了算。
/// 好处是敌兵用的就是他自己那份 `actionSubset`，策划给他换招式不用改这里。
///
/// 【数值哪来的】：全部照源工程 `Enemy.prefab` 上序列化的那一份搬，单位从秒换成逻辑帧
/// （1 秒 = 60 拍，见 `Assets/Scripts/AGENTS.md` 的单位法则）：
/// delayBeforeStart 1s→60 拍、decisionInterval 1s→60 拍、randomizeAmount 1s→60 拍、
/// enemyPauseBeforeAttack .3s→18 拍、maxAttackRange (1.2, .1) 米、理想站位 1 米、
/// 进攻配额 0 人进攻时 75% / 已有 1~2 人时 25% / 3 人以上时一律不进攻只走位。
///
/// 【配额为什么是外面数进来的】：源工程数的是"tag 是 Enemy 且状态在进攻的人"。
/// 这里如果自己去翻 `CharacterObj.Units`，会把**玩家自己**那一拳也算成一个名额（他也在打），
/// 而"谁是被管的敌兵"是 `GameMain` 那份 `_minds` 列表才知道的事，所以由调用方每拍算一次传进来：
/// 一遍循环代替 N×M 次互查，也没有全局注册表在编辑器用例里的残留问题。
/// 【随机只走 Dice】：源工程用 `Time.time + Random.Range` 排下一次决策，帧率一变节奏就变，
/// 而且不可复现。这里下一次决策的间隔 = 定长 + `Dice.Next(jitter)`，
/// 于是同一颗种子（`GameMain.randomSeed`）跑出来的敌兵行为逐拍一致——回放和对账的前提。
/// </summary>
public class EnemyMind : MonoBehaviour
{
    /// <summary>这一只敌兵</summary>
    public CharacterObj self;

    /// <summary>他要打的人。本工程暂时只有一个玩家</summary>
    public CharacterObj target;

    [Header("节奏（逻辑帧，60 帧 = 1 秒）")]
    /// <summary>出场后先愣几拍。源工程 delayBeforeStart 1s：免得一波四五只同时扑上来</summary>
    public int delayBeforeStartFrames = 60;

    /// <summary>决策周期。源工程 decisionInterval 1s</summary>
    public int decisionIntervalFrames = 60;

    /// <summary>决策周期的抖动上限。源工程 randomizeAmount 1s，于是实际周期是 1~2 秒</summary>
    public int decisionJitterFrames = 60;

    /// <summary>站到理想位置之后、按键之前的静止拍数。源工程 enemyPauseBeforeAttack .3s，就是前摇</summary>
    public int telegraphFrames = 18;

    [Header("距离（米）")]
    /// <summary>横向进入这个距离以内才算"够得着"。源工程 maxAttackRange.x 1.2</summary>
    public float attackRangeX = 1.2f;

    /// <summary>纵深差在这个距离以内才算"同一条车道"。源工程 maxAttackRange.y 0.1</summary>
    public float attackRangeZ = 0.1f;

    /// <summary>打完一站想站多远。源工程 attackDistance 1</summary>
    public float idealDistanceX = 1.0f;

    [Header("出手配额（源工程那三档概率）")]
    /// <summary>现在没人进攻时的出手概率（%）</summary>
    public int attackChanceWhenIdle = 75;

    /// <summary>已经有一两个人在进攻时的出手概率（%）</summary>
    public int attackChanceWhenSomeBusy = 25;

    /// <summary>几个人以上在进攻就一律不出手、只走位。源工程是 ≥3</summary>
    public int maxAttackers = 3;

    [Header("招式")]
    /// <summary>会哪几招就由这几下按键决定——抽一招出手。本工程敌兵的家底是拳两下脚两下</summary>
    public KeyMap[] attackKeys = { KeyMap.Punch, KeyMap.Punch, KeyMap.Kick, KeyMap.Kick };

    [Header("回防（源工程 UnitSettings.defendChance / defendDuration）")]
    /// <summary>
    /// 看见玩家起手时抽中的概率（%），抽中就举盾 <see cref="defendHoldFrames"/> 拍。
    /// 源工程这个数默认是 0（只有配了的敌兵才会挡），这里给 30 是因为本工程的敌兵就是那一只灰盒。
    /// 【为什么是"起手时抽一次"而不是"挨打瞬间抽"】：源工程在 `IsDefending` 里抽，也就是被打的那一拍才决定，
    /// 而本工程的格挡是表里的一行动作（受击阶段挂着 block），动作要下一拍才生效——
    /// 抽在起手那一刻既复现了"有概率挡住"这件事，又不会让"挡住"依赖一个刚被随机出来的状态
    /// </summary>
    public int defendChance = 30;

    /// <summary>举盾按着格挡键多少拍。源工程 defendDuration 0.5 秒 = 30 拍</summary>
    public int defendHoldFrames = 30;

    /// <summary>横向在这个距离以内才算"那一记是冲我来的"。比进攻射程宽一点：挡要提前，等走进射程再抽就晚了</summary>
    public float defendRangeX = 1.6f;

    /// <summary>
    /// 还在举盾的那几拍。允许外面读写（调试 UI 与用例都靠这个），>0 期间这一拍只登记格挡键
    /// </summary>
    public int GuardLeft { get; set; }

    /// <summary>
    /// 已经决定出手之后还剩几拍按下键。>0 就是"正在前摇"。
    /// 与 `FramesToDecision` 一样允许外面写：调试 UI 要能"现在就让他打出去"，用例要能清掉上一段的前摇
    /// </summary>
    public int TelegraphLeft { get; set; }

    /// <summary>
    /// 距离下一次决策还剩几拍。写它也是允许的（调试 UI 与用例都靠这个把"现在就想过一次"表达出来），
    /// 0 就代表"下一拍就决策"
    /// </summary>
    public int FramesToDecision { get; set; }

    private bool _started;
    private KeyMap _pending = KeyMap.NoInput;
    private bool _guardRolled;

    /// <summary>
    /// 每逻辑帧一拍，由 `GameMain.TickAll` 在输入拍之后、动作拍之前调用。
    /// 排在输入之后是因为登记进去的按键要在同一个逻辑帧里被动作拍读到——
    /// 与玩家按键的时机完全一致（玩家在 `input.Tick` 里登记，AI 在这里登记）
    /// </summary>
    public void Tick(int attackersCommitting)
    {
        if (!self || !target) return;

        //玩家死了就别围着他打：源工程 PlayerDeath 里那句 DisableAllEnemyAI 就是这个意思
        if (target.IsDead) return;

        if (!_started)
        {
            if (delayBeforeStartFrames > 0)
            {
                delayBeforeStartFrames--;
                return;
            }
            _started = true;
            FramesToDecision = 0;      //愣完立刻想一次，不然第一波出手要拖到两秒之后
        }

        //正在前摇的那几拍：站住（一帧都不登记方向键），到点把按键交出去
        if (TelegraphLeft > 0)
        {
            TelegraphLeft--;
            if (TelegraphLeft == 0)
            {
                self.input.AddInput(_pending);
                _pending = KeyMap.NoInput;
            }
            return;
        }

        //挨打、被打飞、倒地这些状态里不思考也不走位：他自己那份表的优先级会把他带回站立
        if (!FreeToAct()) return;

        //回防：抽中之后就站进防御姿势那几拍，连走位都不做（源工程 defendDuration 内他就是站着挡）。
        //排在走位之前、决策心跳之后，所以它不需要等到下一次心跳才反应——看见起手就要动
        RollGuard();
        if (GuardLeft > 0)
        {
            self.input.AddInput(KeyMap.Block);
            GuardLeft--;
            return;
        }

        StepIntoPlace();

        if (FramesToDecision > 0) { FramesToDecision--; return; }
        Decide(attackersCommitting);
    }

    /// <summary>
    /// 玩家这一记的起手刚刚被我看见，就抽一次格挡。
    /// 【"这一记"是怎么认出来的】：他离开攻击分类（Punch/Kick/Skill/Grab 那几家）就把标记清掉，
    /// 所以连打三拳是三次抽样，而同一拳的那十几拍里只抽一次——每拍都抽的话，
    /// 一个 30% 会被十几拍叠成"几乎必挡"，那已经不是 defendChance 了
    /// </summary>
    private void RollGuard()
    {
        if (!TargetIsCommitting())
        {
            _guardRolled = false;
            return;
        }
        if (_guardRolled) return;
        _guardRolled = true;
        if (defendChance > 0 && Dice.Chance(defendChance)) GuardLeft = defendHoldFrames;
    }

    /// <summary>
    /// 玩家此刻正处在某一记攻击的那几拍里，而且那一记够得着我
    /// </summary>
    private bool TargetIsCommitting()
    {
        string catalog = target.action.CurrentAction.catalog;
        if (catalog != "Punch" && catalog != "Kick" && catalog != "Skill" && catalog != "Grab") return false;

        Vector3 s = self.transform.position;
        Vector3 t = target.transform.position;
        return Mathf.Abs(t.x - s.x) <= defendRangeX && Mathf.Abs(t.z - s.z) <= attackRangeZ + 0.3f;
    }

    /// <summary>
    /// 当前这一行是不是"还能自己决定干什么"的状态：只看分类，不看动作 id
    /// （敌兵躺下是 `BeatenFromForward`、玩家躺下是 `PlayerKnockDownGrounded`，id 不同而 catalog 同为 Beaten）
    /// </summary>
    private bool FreeToAct()
    {
        ActionInfo current = self.action.CurrentAction;
        if (current.catalog == null) return false;
        return current.catalog == "Stand";
    }

    /// <summary>
    /// 走位：先把脸转向目标，再把横向收到理想站位、把纵深收到同一条车道。
    /// 只登记按键与要求面向，具体走多快归表里那行的 `moveSpeed` 管。
    /// 【转向为什么走 `ActionController.TurnTo` 而不是"按后退让表里的转身行把他转过来"】：
    /// demo 那一张表里 `[Backward]` 同时是 `BoxingMoveBackward` 和 `TurnBack` 的命令，
    /// 同分时赢的是表里靠前的后退行，于是"按后退"退出去的是距离不是面向；
    /// 而 AI 要的只是"脸朝着人"这一件事，交给框架的那一个真源（`command.inversed`）最省事也最准
    /// </summary>
    private void StepIntoPlace()
    {
        Vector3 s = self.transform.position;
        Vector3 t = target.transform.position;
        float dx = t.x - s.x;
        float dz = t.z - s.z;

        //差得太小就不折腾，免得贴着人每拍扭一次头
        if (Mathf.Abs(dx) > 0.25f) self.action.TurnTo(dx < 0f);

        //登记的是**世界方向**的那两个键（Forward = 世界 +x），不是"我脸朝的那一边"：
        //`InputToCommand.GetKeyInput` 会在比对表里的命令时按 `inversed` 镜像一次，
        //所以键存成世界方向、表里读成正反手，才是这张表本来给玩家用的那条路。
        //AI 要是直接存"我正脸朝的方向"，转过去身之后就会变成"背对着人退开"
        bool towardPlusX = dx > 0f;
        if (Mathf.Abs(dx) > attackRangeX)
            self.input.AddInput(towardPlusX ? KeyMap.Forward : KeyMap.Backward);
        else if (Mathf.Abs(dx) < idealDistanceX - 0.25f)
            self.input.AddInput(towardPlusX ? KeyMap.Backward : KeyMap.Forward);

        if (dz > attackRangeZ) self.input.AddInput(KeyMap.Up);          //屏幕上方 = +z = 远离镜头
        else if (dz < -attackRangeZ) self.input.AddInput(KeyMap.Duck);
    }

    /// <summary>
    /// 决策一次：先看现在有几个人已经扑上去了，按源工程那三档概率决定"这次轮不轮到我出手"，
    /// 然后把下一次决策排到 `周期 + 抖动` 拍之后
    /// </summary>
    private void Decide(int attackersCommitting)
    {
        int percent;
        if (attackersCommitting <= 0) percent = attackChanceWhenIdle;
        else if (attackersCommitting < maxAttackers) percent = attackChanceWhenSomeBusy;
        else percent = 0;             //够人了，这一轮谁都不许再上

        LastPercent = percent;
        if (percent > 0 && Dice.Chance(percent))
        {
            _pending = attackKeys[Dice.Next(attackKeys.Length)];
            TelegraphLeft = telegraphFrames;
        }

        FramesToDecision = decisionIntervalFrames + Dice.Next(decisionJitterFrames + 1);
    }

    /// <summary>这一轮拿到的出手概率（%）。调试 UI 与用例看这个</summary>
    public int LastPercent { get; private set; }
}
