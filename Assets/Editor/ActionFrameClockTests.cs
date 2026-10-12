using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase 2 第3步的验收用例。跑法：Window → General → Test Runner → EditMode → Run All（不用按Play）。
///
/// 解决的问题：这一改的全部收益是"动作在第几帧判到、能不能接上"跟渲染帧率无关，
/// 而这件事肉眼一点都看不出来（差的都是16.7毫秒的零头），所以只能用例来钉。
///
/// 钉的四件事：
/// 1. 判定盒实际生效的帧 == 表里填的帧区间（窗口从百分比换成绝对帧号，不能换错）；
/// 2. 连招窗口在边界上是精确的——早一帧接不上、晚一帧接不上，中间每一帧都接得上；
/// 3. 顿挫（硬直）期间判定盒保持不动，但Cancel照常可用；
/// 4. 渲染帧率到逻辑帧率的换算，以及RootMotion位移，都只取决于帧号而不取决于时间。
///
/// 用例的宿主是隐藏的一个Player克隆（HideFlags.DontSaveInEditor），跑完连根销毁，
/// 不新建场景也不写进你正开着的场景。判定盒刷新和命中结算两拍不在这里跑——它们读的就是这里断言的那两个掩码。
/// </summary>
public class ActionFrameClockTests
{
    private const string PrefabPath = "Assets/Resources/Player.prefab";

    private GameObject _rig;
    private ActionController _action;
    private InputToCommand _input;

    [SetUp]
    public void SetUp()
    {
        GameData.Load();
        //宿主用 HideFlags.DontSaveInEditor 的隐藏物体：不新建场景（当前场景没存过时 NewScene(Additive) 会被Unity拒绝），
        //也不会写进你的场景文件，TearDown里连根销毁
        _rig = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        _rig.name = "ACT-Test-Rig";
        _rig.hideFlags = HideFlags.DontSaveInEditor | HideFlags.HideInHierarchy;
        CharacterObj character = _rig.GetComponent<CharacterObj>();
        _action = character.action;
        _input = character.input;
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_rig);
    }

    /// <summary>
    /// 走一个逻辑帧。顺序照GameMain.TickAll的前两拍：时钟前进→输入登记→动作推进。
    /// 参数是这一帧按下的键，不传就是这一帧什么都没按
    /// </summary>
    private void Tick(params KeyMap[] pressed)
    {
        LogicClock.Advance();
        _input.Tick();
        for (int i = 0; i < pressed.Length; i++) _input.AddInput(pressed[i]);
        _action.Tick();
    }

    private void StartAction(string id)
    {
        _action.SetAllActions(GameData.AllActions(), id);
    }

    /// <summary>
    /// 一个键都不按，空跑到当前动作的第frame帧。
    /// 次数是算好的而不是"没到就继续跑"，万一时钟不对，让断言去报错，不要把测试 runner 挂在这里
    /// </summary>
    private void RunToFrame(int frame)
    {
        for (int i = _action.CurrentFrame; i < frame; i++) Tick();
    }

    /// <summary>
    /// 在第frame这一个逻辑帧上按下这些键。
    /// 【为什么是跑到frame-1】：Tick自己就要把时钟推进一格，按下按键的那一帧就是它推进完的那一帧
    /// </summary>
    private void PressOnFrame(int frame, params KeyMap[] pressed)
    {
        RunToFrame(frame - 1);
        Tick(pressed);
    }

    /// <summary>
    /// 表里那个动作
    /// </summary>
    private static ActionInfo Info(string id)
    {
        return GameData.Actions[id];
    }

    /// <summary>
    /// 窗口 [min,max] 按"这一段被跨过就算数"的语义，实际生效的帧
    /// </summary>
    private static bool LiveAt(FrameRange range, int frame)
    {
        return frame >= range.min && frame - 1 <= range.max;
    }

    //==================================== 1. 判定盒的帧 ====================================

    [Test]
    public void BoxesOpenExactlyOnTheFramesListedInTable()
    {
        foreach (ActionInfo info in GameData.AllActions())
        {
            List<string> first = CollectBoxTrace(info.id);
            List<string> second = CollectBoxTrace(info.id);
            Assert.AreEqual(first, second,
                info.id + "：同一个动作跑两遍，逐帧结果必须一字不差（动作时钟不认表，只认这是第几个逻辑帧）");

            //没有输入时，autoTerminate的动作第一帧就被换掉，其余的必须正好跑满表里的durationFrames
            int expectedTicks = info.autoTerminate ? 1 : info.durationFrames;
            Assert.AreEqual(expectedTicks, first.Count,
                info.id + "：动作应当跑" + expectedTicks + "个逻辑帧（autoTerminate=" + info.autoTerminate +
                "，durationFrames=" + info.durationFrames + "）");
        }
    }

    /// <summary>
    /// 把这个动作从第1帧跑到它被换掉，逐帧断言两个掩码跟表里的窗口对得上。
    /// 换动作的那一tick例外：ChangeAction 会把两个掩码清零（判定盒当帧就该是关的），
    /// 所以那一tick断言的是0，同时它也是本动作逐帧区间的收尾
    /// </summary>
    private List<string> CollectBoxTrace(string id)
    {
        ActionInfo info = Info(id);
        StartAction(id);

        List<string> trace = new List<string>();
        for (int frame = 1; frame <= info.durationFrames; frame++)
        {
            Tick();

            //帧号对不上，说明这一tick的末尾动作被换掉或回绕了
            bool keptRunning = _action.CurrentFrame == frame;
            if (keptRunning)
            {
                Assert.AreEqual(ExpectedAttackMask(info, frame), _action.ActiveAttackBoxMask,
                    id + " 第" + frame + "帧的攻击盒掩码");
                Assert.AreEqual(ExpectedBeHitMask(info, frame), _action.ActiveBeHitBoxMask,
                    id + " 第" + frame + "帧的受击盒掩码");
            }
            else
            {
                Assert.AreEqual(0, _action.ActiveAttackBoxMask, id + " 换动作那一帧不该留着上一个动作的攻击盒");
                Assert.AreEqual(0, _action.ActiveBeHitBoxMask, id + " 换动作那一帧不该留着上一个动作的受击盒");
            }
            trace.Add(frame + "|" + _action.ActiveAttackBoxMask + "|" + _action.ActiveBeHitBoxMask +
                      "|" + keptRunning);

            if (!keptRunning) break;
        }

        return trace;
    }

    private static int ExpectedAttackMask(ActionInfo info, int frame)
    {
        int mask = 0;
        if (info.attackPhase == null) return mask;
        for (int i = 0; i < info.attackPhase.Length; i++)
            foreach (FrameRange range in info.attackPhase[i].inFrames)
                if (LiveAt(range, frame)) mask |= info.attackPhase[i].tagMask;
        return mask;
    }

    private static int ExpectedBeHitMask(ActionInfo info, int frame)
    {
        int mask = 0;
        if (info.defensePhase == null) return mask;
        for (int i = 0; i < info.defensePhase.Length; i++)
            foreach (FrameRange range in info.defensePhase[i].inFrames)
                if (LiveAt(range, frame)) mask |= info.defensePhase[i].tagMask;
        return mask;
    }

    //==================================== 2. 连招窗口的边界 ====================================

    /// <summary>
    /// Kick1的表里写着：第25到37帧可以被"TwinKickWhileKick1"取消，
    /// 按"跨过即算"的语义，实际接得上的输入落在第25到38帧，第24帧和第39帧都不行。
    /// TwinKick自己的招式是 ↓ ↘ → + 脚，startFromFrame是3
    /// </summary>
    [Test]
    public void Kick1IntoTwinKickLandsInsideTheWindowAndOnlyInsideIt()
    {
        KeyMap[] twinKickMotion = { KeyMap.Duck, KeyMap.DuckForward, KeyMap.Forward, KeyMap.Kick };

        //窗口还没开
        StartAction("Kick1");
        PressOnFrame(24, twinKickMotion);
        Assert.AreEqual("Kick1", _action.CurrentAction.id, "第24帧搓出来还接不上（窗口第25帧才开）");

        //窗口第一帧
        StartAction("Kick1");
        PressOnFrame(25, twinKickMotion);
        Assert.AreEqual("TwinKick", _action.CurrentAction.id, "窗口开的第25帧必须接得上");
        Assert.AreEqual(3, _action.CurrentFrame, "TwinKick的startFromFrame是3，换过去就该落在第3帧");

        //窗口最后一帧
        StartAction("Kick1");
        PressOnFrame(38, twinKickMotion);
        Assert.AreEqual("TwinKick", _action.CurrentAction.id, "跨过第37帧的那一tick（也就是第38帧）还接得上");

        //窗口已经关了
        StartAction("Kick1");
        PressOnFrame(39, twinKickMotion);
        Assert.AreEqual("Kick1", _action.CurrentAction.id, "第39帧就接不上了");
    }

    /// <summary>
    /// 挨打才开的临时窗口：表里PunchCombo1的长度是2帧，在第4帧末尾登记，区间就成了[4,6]。
    /// 按"跨过即算"的语义，能用上它的输入落在第5、6、7帧（登记当帧已经过去了，所以从第5帧起），
    /// 第8帧就晚了
    /// </summary>
    [Test]
    public void TempCancelWindowStartsWhereItWasTurnedOnAndExpiresOnTime()
    {
        //窗口的第一帧
        StartAction("Punch1");
        RunToFrame(4);
        _action.AddTempBeCancelledTag("PunchCombo1");
        Tick(KeyMap.Punch);
        Assert.AreEqual("Punch2", _action.CurrentAction.id, "临时窗口开着的第5帧应当接得上");

        //窗口的最后一帧
        StartAction("Punch1");
        RunToFrame(4);
        _action.AddTempBeCancelledTag("PunchCombo1");
        Tick();
        Tick();
        Tick(KeyMap.Punch);
        Assert.AreEqual("Punch2", _action.CurrentAction.id, "第7帧是这段窗口最后一帧，还接得上");

        //已经出窗
        StartAction("Punch1");
        RunToFrame(4);
        _action.AddTempBeCancelledTag("PunchCombo1");
        Tick();
        Tick();
        Tick();
        Tick(KeyMap.Punch);
        Assert.AreEqual("Punch1", _action.CurrentAction.id, "第8帧窗口已经过了，不该再接");
    }

    //==================================== 3. 顿挫 ====================================

    /// <summary>
    /// 顿挫期间动作时钟一格都不走，所以判定盒保持原样；但Cancel扫描照旧每帧都跑，
    /// 连招不会因为自己在顿挫里就丢掉——这正是改造前用 anim.speed = 0 做不到的精确停止
    /// </summary>
    [Test]
    public void HitstopFreezesTheFrameButNotTheCancel()
    {
        //顿挫不动盒子：Punch1的左拳在第4到第6帧有效
        StartAction("Punch1");
        RunToFrame(4);
        int boxBefore = _action.ActiveAttackBoxMask;
        Assert.AreNotEqual(0, boxBefore, "第4帧本来就该开着左拳盒");

        _action.SetFreezing(3);
        for (int i = 0; i < 3; i++)
        {
            Tick();
            Assert.AreEqual(4, _action.CurrentFrame, "顿挫的第" + (i + 1) + "个逻辑帧，动作时钟不该动");
            Assert.AreEqual(boxBefore, _action.ActiveAttackBoxMask, "顿挫期间判定盒必须保持");
        }

        Tick();
        Assert.AreEqual(5, _action.CurrentFrame, "顿挫走满3帧，第4个逻辑帧才继续");

        //顿挫期间照样能接招，而且换动作会把剩下的顿挫清掉（保持改造前的行为）
        StartAction("Punch1");
        RunToFrame(4);
        _action.AddTempBeCancelledTag("PunchCombo1");
        _action.SetFreezing(3);
        Tick(KeyMap.Punch);
        Assert.AreEqual("Punch2", _action.CurrentAction.id, "顿挫期间按下的拳照样要接上");
        Assert.AreEqual(0, _action.CurrentFrame, "换过去的这一帧落在新动作的起始帧");
        Tick();
        Assert.AreEqual(1, _action.CurrentFrame, "换动作把剩下的顿挫清掉了，下一帧就该往前走");
    }

    //==================================== 4. 走位能撑满整条动作 ====================================

    /// <summary>
    /// 按住前进时，BoxingMoveForward靠"自己取消自己"保持连贯，一路到循环点都不该被换成站立；
    /// 顺便钉住循环的那一tick：帧号回绕到0，而_wasFrame停在末帧，
    /// 所以从第0帧就开的窗口（这里的受击盒）不会在循环处漏掉一帧
    /// </summary>
    [Test]
    public void HoldingForwardKeepsTheWalkAliveAcrossTheLoopPoint()
    {
        StartAction("BoxingMoveForward");
        int hurtMask = ExpectedBeHitMask(Info("BoxingMoveForward"), 1);
        Assert.AreNotEqual(0, hurtMask, "这条动作的受击盒是全程开着的");

        for (int tick = 1; tick <= 100; tick++)
        {
            Tick(KeyMap.Forward);
            Assert.AreEqual("BoxingMoveForward", _action.CurrentAction.id, "第" + tick + "帧不该被换成别的动作");
            Assert.AreEqual(tick % 48, _action.CurrentFrame, "第" + tick + "帧的帧号：48帧一循环");
            Assert.AreEqual(1f, _action.MoveInputAcceptance, 0f, "前进的接受度按表里是1");
            Assert.AreEqual(hurtMask, _action.ActiveBeHitBoxMask, "循环点也不该漏掉受击盒");
        }
    }

    //==================================== 5. 渲染帧率到逻辑帧率 ====================================

    /// <summary>
    /// 30/60/120三档渲染帧率下，同样的1秒必须都走满60个逻辑帧；
    /// 卡半秒也只补4帧，剩下的时间丢掉，不欠债
    /// </summary>
    [Test]
    public void AnyRenderFramerateProducesSixtyLogicTicksPerSecond()
    {
        Assert.AreEqual(60, TicksInOneSecond(1f / 30f), "30FPS渲染");
        Assert.AreEqual(60, TicksInOneSecond(1f / 60f), "60FPS渲染");
        Assert.AreEqual(60, TicksInOneSecond(1f / 120f), "120FPS渲染");

        double accumulator = 0;
        Assert.AreEqual(LogicClock.MaxTicksPerFrame, LogicClock.TicksDue(ref accumulator, 0.5f),
            "卡了半秒，最多也只能补" + LogicClock.MaxTicksPerFrame + "帧");
        Assert.AreEqual(1, LogicClock.TicksDue(ref accumulator, 1f / 60f),
            "卡顿之后恢复正常帧率，不能突然爆出一串帧来追");
    }

    private static int TicksInOneSecond(float renderDelta)
    {
        double accumulator = 0;
        int ticks = 0;
        int frames = Mathf.RoundToInt(1f / renderDelta);
        for (int i = 0; i < frames; i++) ticks += LogicClock.TicksDue(ref accumulator, renderDelta);
        return ticks;
    }

    //==================================== 6. RootMotion只认帧号 ====================================

    /// <summary>
    /// Punch2表里是"走0.2米，第5帧起、第8帧满"（38帧的动作）。
    /// 逐帧位移必须只由帧号决定，加起来的总位移必须正好等于表里那个米数
    /// </summary>
    [Test]
    public void RootMotionDisplacementDependsOnlyOnFrameNumber()
    {
        RootMotionParam param = RootMotionMethod.Bake("GoStraight", new[] { "0.2", "5", "8" }, 38);
        RootMotionFunc func = RootMotionMethod.Methods["GoStraight"];

        float walked = 0;
        for (int frame = 1; frame <= 38; frame++)
        {
            float step = (func.Eval(frame, param) - func.Eval(frame - 1, param)).x;
            walked += step;
            if (frame <= 5) Assert.AreEqual(0f, step, 0f, "第5帧之前不前进");
            if (frame > 8) Assert.AreEqual(0f, step, 0f, "走满之后不再前进");
        }

        Assert.AreEqual(0.2f, walked, 1e-5f, "整条动作累计必须正好是表里的0.2米");
        //第6帧这一步是个跳变：线性段从0起算而不是从startFrame起算。
        //这是表作者当年的写法，Phase 2 刻意原样保留，所以这里把它钉住，改动它必须是故意的
        Assert.AreEqual(6f / 38f * 0.2f, (func.Eval(6, param) - func.Eval(5, param)).x, 1e-5f);
    }
}
