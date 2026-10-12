using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;    //LogAssert：故意触发的 Debug.LogError 要认下来，否则用例算失败

/// <summary>
/// Phase 2 第3步的验收用例。跑法：Window → General → Test Runner → EditMode → Run All（不用按Play）。
///
/// 解决的问题：这一改的全部收益是"动作在第几帧判到、能不能接上"跟渲染帧率无关，
/// 而这件事肉眼一点都看不出来（差的都是16.7毫秒的零头），所以只能用例来钉。
///
/// 钉的十七件事：
/// 1. 判定盒实际生效的帧 == 表里填的帧区间（窗口从百分比换成绝对帧号，不能换错）；
/// 2. 连招窗口在边界上是精确的——早一帧接不上、晚一帧接不上，中间每一帧都接得上；
/// 3. 顿挫（硬直）期间判定盒保持不动，但Cancel照常可用；
/// 4. 渲染帧率到逻辑帧率的换算，以及RootMotion位移，都只取决于帧号而不取决于时间。
/// 5. 纵深车道（BeatEmUp 移植新增）：z 只由竖轴驱动、不随正反手镜像，且与横向共用同一个
///    inputAcceptance；RootMotion 的第四个参数缺省为 0（老表逐项不变）；同轨推挤只动 z，
///    重合时的方向由数组顺序定死。
/// 6. 相交采样（BeatEmUp 移植新增）："这一拳打到谁"按逻辑帧主动查询，不再挂在物理步上——
///    命中帧恰好等于表里窗口被跨过的帧、接触列表每拍清空重填、自己的盒子不算靶子、
///    离开采样层的盒子查不到、沿纵深快速穿过也不漏拍。
///    【这一组以前根本写不出来】：EditMode 不跑物理模拟，OnTriggerEnter 一次都不会来。
/// 7. 帧事件与盒子轨迹（BeatEmUp 移植切片 1.5）：cue 在该响的那一拍响一次、顿挫只推迟不吞掉、
///    循环每圈重放一次；有轨迹的盒子每一拍摆到表里的位置和尺寸（沿上一条、关掉就不写），
///    没填轨迹的盒子留在 prefab 的位置上，同一盒子被两条表项开启时由优先级高的说了算。
///    【这一组的表项是现造的】：真表里此刻还没有 track/cues，往 Action.json 塞测试行会污染策划的表。
///    【这一组以前根本写不出来】：盒子在哪只由 .anim 曲线说了算，表管不着，用例也无从问起。
/// 8. 派生门禁、长按与确定性随机（BeatEmUp 移植切片 1.6）：同一张表同一个输入，站着能接的招
///    在半空接不出来；追击条件既查距离也查对方此刻的动作；Block 每拍登记所以按住=每拍重新成立，
///    松手后按回看窗口留两拍尾巴；换动作清历史时 Block 留下而 Punch 被清掉；
///    随机数只由种子和抽样次数决定（换帧率分法、换种子、退化点、取值范围全钉住）；
///    函数名写错时门禁按"不成立"处理而不是放行。
/// 9. 每角色自己的动作子集（切片 1.7 的前置）：分类候选建在角色自己那份表上，
///    不会把"这个角色根本不会的招式"端上候选。
/// 10. 跳跃与落地（BeatEmUp 移植切片 1.7）：表里的 jumpUpSpeed 在换动作那一拍就把人带离地面
///    （而不是被地面探测当成「走下平台」清零）、滞空拍数与最高点由 weight 和初速度一起标定出来、
///    一次起跳只落地一次、落地那一拍切到 landActionId；真表里站着按拳和边跑边按拳都出得来
///    （平局必须由 priority 裁决，不能由填表顺序）；空中再按跳跃不出二段跳、按拳出空中技。
///    【这一区的表行是真表】：1.7 之后「按拳出不出来」已经是表数据决定的事，拿现造的表项测等于没测。
/// 11. 抓取约束与投掷（BeatEmUp 移植切片 1.8）：被举者**每一拍**都跟着举人者摆（源工程是摆一次，
///    玩家一动敌人就飘在原地）；偏移的 x 随正反手镜像而 z 不镜像；抓取系内部换动作不解约束、
///    挨一记就解；holdFrames 走完就撒手；投掷那一记的合成盒真的打得到手里的人，解约束之后按 Arc
///    抛物线飞出去并落回地面；Arc 这条曲线本身两头同高、最高点就是表里那个 y。
/// 12. 伤害、霸体与格挡（BeatEmUp 移植切片 1.9）：伤害 = 攻击力 × 表里倍率 × 攻击盒倍率 × 肉质 − 防御
///    （减成负数夹在 0）；韧性没被打空就只掉血——不换动作、不硬直、不推开；正面格挡全额免伤但罚
///    blockStunFrames 拍并推开 blockPush 那段距离，背后的那一记挡不住；打空血路由到这个人自己那份
///    子集里的 Death 行，而死人不再被 CanAttackTargetNow 挑中；没挂 CombatStats 的角色行为与 1.9 之前
///    一字不差（这条是兼容闸门）；韧性只在隔满恢复期之后一拍一点地回，回满就停。
///    【分类挑选的兜底也被第12区钉住了】：Hurt/Beaten 两支挑选函数认不出正反手 id 时必须落到候选第一行，
///    返回空 ActionInfo 会让移植家的角色挨打一动不动。
/// 13. 帧事件的接收者（BeatEmUp 移植切片 2.1）：表里每一条 cue 要的名字都真能拿到素材、
///    烟放在放它的人脚下并在换算出来的拍数上收回、超过容量只回头复用而绝不 Instantiate、
///    镜头震动十八拍退干净、名字对不上时安静跳过而不是抛或刷 Error。
/// 14. 敌兵的决策（BeatEmUp 移植切片 2.2）：够不着就按前进、目标在身后先转身再前进、车道没对上时
///    只按纵深键、决定出手之后隔满 `telegraphFrames` 拍才把键按下去、场上攻击中的人达到配额就不许再上、
///    玩家死了整族停摆、他自己挨着打的那几拍既不思考也不走位。
///    【AI 只登记按键，不写位移】：出不出招仍然由表说了算，所以这一区断言的是"按了什么"。
/// 15. 波次表与关卡推进（BeatEmUp 移植切片 2.3）：表里每一只都取得到预制体**而且挂了 CombatStats**
///    （没挂就是死局：他不会死，这一波永远清不完）、上一波还剩一个人就不许开下一波、清完之后隔满
///    `framesBetweenWavesFrames` 那一拍才开、最后一波清完报 LevelCleared 且之后再也不多发一个人、
///    按表生成一个人要走完"认盒子 + 只拿自己那份子集 + 按表里的坐标（含纵深）站好"这三步。
/// 16. 关卡边界与血量条（BeatEmUp 移植切片 2.3 后半）：相机在关卡两头都被夹住、区间内不干涉、
///    关卡窄于一屏（边界算反了）时退回不夹而不是把相机按死；波次表里每一只都生在边界以内；
///    血条按 hp 走、没挂组件是满格而不是空条、越界的数夹在 0~1、代码搭 UI 搭得出来。
///
/// 用例的宿主是隐藏的一个Player克隆（HideFlags.DontSaveInEditor），跑完连根销毁，
/// 不新建场景也不写进你正开着的场景。第1到10区只跑到动作拍为止，判定盒刷新和命中结算读的就是这里断言的那两个掩码；
/// 第11区（相交采样）另外把判定盒拍和采样拍也跑起来，因为它断言的就是"打到没有"；
/// 第12区把判定盒拍也跑起来，因为它断言的是盒子被摆到哪；
/// 第13区大多只跑到动作拍，只有制造滞空那一处要额外走一拍移动拍（地面探测在那一拍里写 State）；
/// 第15区整条抛物线都要跑，所以它既跑移动拍，也自己摆一块 Ground 层的面板当地面——
/// EditMode 没有场景，不造地面角色就一路往下掉；
/// 第16区跑满整条链（移动、同轨推挤、抓取约束三拍都在内），因为"被举者这一拍站在哪"就是最后那一拍写的；
/// 第19区只跑到动作拍为止（它断言的是"AI 按了什么键、第几拍按的"，位移归表管）；
/// 第17区也跑满整条链，而且它打的是真结算入口（CombatPipeline.ResolveHit）——"挡住之后动作一动不动"
/// 这件事只有把整条链跑起来才看得见。
/// </summary>
public class ActionFrameClockTests
{
    private const string PrefabPath = "Assets/Resources/Player.prefab";

    private GameObject _rig;
    private ActionController _action;
    private InputToCommand _input;
    private CharacterObj _character;

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
        _character = character;
        _action = character.action;
        _input = character.input;
        //EditMode里 Instantiate 不触发 Awake，判定盒的绑定要自己叫（第11区的采样用例离不开它）
        character.BindBoxes();
    }

    [TearDown]
    public void TearDown()
    {
        //派发口是静态的：留着不动，下一个用例就会收到上一个角色的 cue
        CueDispatch.Handler = null;
        if (_targetRig != null) Object.DestroyImmediate(_targetRig);
        if (_groundRig != null) Object.DestroyImmediate(_groundRig);
        if (_poiseRig != null) Object.DestroyImmediate(_poiseRig);
        if (_receiverGo != null) Object.DestroyImmediate(_receiverGo);
        for (int i = 0; i < _waveRigs.Count; i++) if (_waveRigs[i] != null) Object.DestroyImmediate(_waveRigs[i]);
        for (int i = 0; i < _hudRoots.Count; i++) if (_hudRoots[i] != null) Object.DestroyImmediate(_hudRoots[i]);
        if (_popupGo != null) Object.DestroyImmediate(_popupGo);
        _hudRoots.Clear();
        _waveRigs.Clear();
        _waveUnits.Clear();
        _spawnCalls = 0;
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

    /// <summary>
    /// 用真表把某个动作开跑。
    /// 【给的是子集而不是全表】：demo 的拳击行和移植进来的玩家行共用同一批 cancel tag 字面量
    /// （demo 的 Punch2 与玩家的 PlayerPunch2 都挂 "Punch2"），全表在场时玩家那一行优先级更高，
    /// 会把 demo 的连招抢走。这不是框架的错，是"每个角色只该拿自己那几行"的又一次证明——
    /// 运行期由 prefab 上的 actionSubset 说清楚，用例这边按 id 前缀分家
    /// </summary>
    private void StartAction(string id)
    {
        _action.SetAllActions(GameData.ActionsFor(FamilyOf(id)), id);
    }

    /// <summary>
    /// 全表按 id 前缀分成两家：Player 打头的是移植进来的玩家招式，其余是 demo 的拳击招式。
    /// 【为什么按前缀而不是写两份清单】：这两家本来就是靠命名分开的，写死清单等于再多一个会忘的地方
    /// </summary>
    private static string[] FamilyOf(string id)
    {
        bool ported = id.StartsWith("Player");
        List<string> ids = new List<string>();
        foreach (ActionInfo info in GameData.AllActions())
            if (info.id.StartsWith("Player") == ported) ids.Add(info.id);
        return ids.ToArray();
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

            //帧号对不上只有两种可能，而它们的掩码规矩正好相反：
            //【真换了动作】ChangeAction 把两个掩码清零（判定盒当帧就该是关的）；
            //【同一行回绕】keepPlayingAnim 的行自己接自己，走的是 KeepAction 而不是 ChangeAction，
            //  盒子还是那一行的盒子，掩码必须留着——清掉就等于站着的人受击盒每二十几帧闪一帧
            bool keptRunning = _action.CurrentFrame == frame;
            if (keptRunning)
            {
                Assert.AreEqual(ExpectedAttackMask(info, frame), _action.ActiveAttackBoxMask,
                    id + " 第" + frame + "帧的攻击盒掩码");
                Assert.AreEqual(ExpectedBeHitMask(info, frame), _action.ActiveBeHitBoxMask,
                    id + " 第" + frame + "帧的受击盒掩码");
            }
            else if (_action.CurrentAction.id == id)
            {
                Assert.AreEqual(0, _action.CurrentFrame, id + "：这一拍是回绕，帧号该回到起点而不是别的动作的帧号");
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

        Assert.AreEqual(0.2f, walked, "整条动作累计必须正好是表里的0.2米");
        //第6帧这一步是个跳变：线性段从0起算而不是从startFrame起算。
        //这是表作者当年的写法，Phase 2 刻意原样保留，所以这里把它钉住，改动它必须是故意的
        Assert.AreEqual(6f / 38f * 0.2f, (func.Eval(6, param) - func.Eval(5, param)).x, 1e-5f);
    }

    //==================================== 7. 顿挫期间摆的是哪套姿势 ====================================

    /// <summary>
    /// 受击方是"换成Hurt动作 + 带7帧顿挫"这一条路。这条用例量的是姿势层的时序，回答两个问题：
    /// ① 换动作那一拍，Animator 是否真的已经切进受击状态（`anim.Update(0f)` 补的那一次顶不顶用）——
    ///    没切过去的话，顿挫冻住的就是挨打之前的姿势，看上去就是"打了没反应"；
    /// ② 顿挫这7拍里状态时间不许前进，第8拍才继续往前走。
    /// 【只在测试里读 Animator】：产品代码禁止拿动画进度当逻辑真源，这里纯粹是为了把姿势量出来。
    /// </summary>
    [Test]
    public void HitstopHoldsTheHurtPoseInsteadOfThePreHitOne()
    {
        Animator anim = _action.anim;
        StartAction("BoxingStand");
        RunToFrame(3);

        //照 GameMain 的受击结算那样预约：换成Hurt这一类，并带7帧顿挫
        //(受击走向来自攻击方的 targetActionChange，不是防守方自己的 selfActionChange——表里后者是Keep)
        ActionChangeInfo change = Info("Punch1").attacks[0].targetActionChange;
        _action.PreorderActionByActionChangeInfo(change, ForceDirection.Backward, 7);
        Tick();

        Assert.AreEqual("Hurt", _action.CurrentAction.catalog, "按受力方向应当挑出受击动作");
        AnimatorStateInfo state = anim.GetCurrentAnimatorStateInfo(0);
        Assert.IsFalse(anim.IsInTransition(0),
            "表里的 transFrames 全是0，融合应当当帧就完成；还停在融合里说明补的那次求值没生效");
        Assert.AreEqual(Animator.StringToHash(_action.CurrentAction.animKey), state.shortNameHash,
            "换动作当帧 Animator 必须已经切进受击状态。当前状态名hash=" + state.shortNameHash +
            "（0 表示 Animator 在编辑期根本没绑定，测不出来）");

        float held = state.normalizedTime;
        for (int i = 0; i < 7; i++)
        {
            Tick();
            Assert.AreEqual(held, anim.GetCurrentAnimatorStateInfo(0).normalizedTime, 1e-6f,
                "顿挫的第" + (i + 1) + "拍姿势必须原地不动");
        }

        Tick();
        Assert.Greater(anim.GetCurrentAnimatorStateInfo(0).normalizedTime, held,
            "顿挫走满7拍，第8拍受击动画才继续往前");
    }

    //==================================== 8. 重复命中的硬直从哪来 ====================================

    /// <summary>
    /// 连着打第二下的时候，受击方已经在Hurt里了，而表里 Hurt 的 defensePhase.selfActionChange 是
    /// Keep、优先级5，跟攻击方 targetActionChange 的优先级5正好打平；GameMain 的平手判定是"各自动作占优"，
    /// 所以 Keep 赢，预约是空的、ChangeAction 不发生。硬直如果只挂在 ChangeAction 上，第二下就是零硬直——
    /// 这就是"punch1 有顿帧、punch2 没有"的成因。这条用例钉住修法依赖的三点契约。
    /// </summary>
    [Test]
    public void RepeatedHitRefreshesHitstopWithoutChangingAction()
    {
        StartAction("HurtFromBackward");
        RunToFrame(12);

        ActionChangeInfo keep = Info("HurtFromBackward").defensePhase[0].selfActionChange;
        Assert.AreEqual(ActionChangeType.Keep, keep.changeType, "表里受击动作自己是要保持的");
        Assert.AreEqual(Info("Punch1").attacks[0].targetActionChange.priority, keep.priority,
            "平手——所以GameMain那个严格大于的判定会让Keep赢");

        //平手被Keep赢下之后：动作不换、硬直也没人写，时钟照走。这正是"第二下没顿帧"
        int frameBefore = _action.CurrentFrame;
        _action.PreorderActionByActionChangeInfo(keep, ForceDirection.Backward, 7);
        Tick();
        Assert.AreEqual("HurtFromBackward", _action.CurrentAction.id, "Keep就是什么都不换");
        Assert.AreEqual(frameBefore + 1, _action.CurrentFrame, "预约空跑一帧，硬直没被写进去");

        //所以硬直必须由ApplyHitStop当场施加：冻住时钟，判定盒保持
        _action.ApplyHitStop(7);
        int boxMask = _action.ActiveBeHitBoxMask;
        Assert.AreNotEqual(0, boxMask, "第12帧起受击盒是开着的，用它确认盒子没被顿挫弄丢");
        for (int i = 0; i < 7; i++)
        {
            Tick();
            Assert.AreEqual(frameBefore + 1, _action.CurrentFrame, "硬直的第" + (i + 1) + "拍时钟不许动");
            Assert.AreEqual(boxMask, _action.ActiveBeHitBoxMask, "硬直期间受击盒必须保持");
        }
        Tick();
        Assert.AreEqual(frameBefore + 2, _action.CurrentFrame, "7拍满了，第8拍才继续");

        //取较大者而不是叠加：打到一半再挨一记，只是把剩余硬重置满，不会越打越长。
        //拍数算式：ApplyHitStop(N) 冻住的是随后的 N 拍，第 N+1 拍才前进——所以下面那两拍（消耗掉的部分）
        //同样一帧都不走，第二次 ApplyHitStop(7) 之后还要再冻满 7 拍，中间时钟总共只前进过一次（就是第446行那次）
        _action.ApplyHitStop(7);
        Tick();
        Tick();
        _action.ApplyHitStop(7);
        for (int i = 0; i < 7; i++)
        {
            Tick();
            Assert.AreEqual(frameBefore + 2, _action.CurrentFrame, "第二次命中之后的7拍同样冻着（没叠成14）");
        }
        Tick();
        Assert.AreEqual(frameBefore + 3, _action.CurrentFrame, "第二次命中也没把硬直累加");

        //短的一记不能把长的截掉：7拍冻掉3拍之后还剩4拍，这时来一个2拍的，仍然要等那4拍走完
        _action.ApplyHitStop(7);
        Tick();
        Tick();
        Tick();
        _action.ApplyHitStop(2);
        for (int i = 0; i < 4; i++)
        {
            Tick();
            Assert.AreEqual(frameBefore + 3, _action.CurrentFrame, "2拍的那记不该把剩下的4拍提前放行");
        }
        Tick();
        Assert.AreEqual(frameBefore + 4, _action.CurrentFrame, "7拍走满（3+4）才继续");
    }

    //==================================== 10. 纵深车道（BeatEmUp 移植新增） ====================================

    /// <summary>
    /// 走满一整拍：连角色的移动也跑，顺序照 GameMain.TickAll（输入→动作→计时与移动）。
    /// 前面的用例只跑到动作拍为止，因为它们断言的是盒子和帧号；这里要断言的是坐标
    /// </summary>
    private void TickWhole(params KeyMap[] pressed)
    {
        LogicClock.Advance();
        _input.Tick();
        for (int i = 0; i < pressed.Length; i++) _input.AddInput(pressed[i]);
        _action.Tick();
        _character.Tick();
    }

    /// <summary>
    /// 正手：按着前进与屏幕上方十拍，纵深走满 10×每拍位移（屏幕上方 = 远离镜头 = +z），横向仍按原公式走。
    /// 【为什么要连方向键一起按】：表里 BoxingMoveForward 的 autoTerminate 是 true，
    /// 没有匹配的 Forward 指令时第一拍就跳回 BoxingStand（acceptance 0），纵深只会走一拍。
    /// 【为什么拆成三个用例而不是一个用例走三段】：输入历史保留 72 个逻辑帧，同一个 rig 里
    /// 换了正反手，上一段按过的键还会继续参与指令匹配，第二段的期望值会被它污染
    /// </summary>
    [Test]
    public void DepthMovesAwayFromCameraWhenFacingRight()
    {
        float perTick = _character.moveSpeed * _character.depthSpeedRate * LogicClock.Tick;
        float walkPerTick = _character.moveSpeed * LogicClock.Tick;

        StartAction("BoxingMoveForward");
        Vector3 from = _rig.transform.position;
        for (int i = 0; i < 10; i++) TickWhole(KeyMap.Forward, KeyMap.Up);
        //【为什么这几条要带 1e-6 容差】：期望值是"每拍位移 × 10"，实现是"每拍加十次"，
        //两种算法天然差最后一位（1 ULP，量级 5e-7）。这条钉的是纵深走满没走满、横向掺没掺进去，
        //不是 IEEE 结合律——写死精确相等的话，编译器一换内联决策就假红了
        Assert.AreEqual(from.z + 10 * perTick, _rig.transform.position.z, 1e-6f, "按住屏幕上方十拍，纵深必须走满 10×每拍位移");
        Assert.AreEqual(from.x + 10 * walkPerTick, _rig.transform.position.x, 1e-6f, "横向仍按原来的公式走，纵深项不该掺进去");
    }

    /// <summary>
    /// 反手（面朝左）：纵深仍然是 +z，只有横向翻成 -x。
    /// 这条钉的是 Doc/decision/0002 里"纵深是屏幕空间轴、永不镜像"那句——
    /// NatureMove 里横向那项乘了 Inversed 的符号，纵深项没乘。
    /// 写反了角色一转身就跑进错误的车道，而这种错肉眼只看得出"手感怪"，是最难查的一类
    /// </summary>
    [Test]
    public void DepthDoesNotMirrorWhenFacingLeft()
    {
        float perTick = _character.moveSpeed * _character.depthSpeedRate * LogicClock.Tick;
        float walkPerTick = _character.moveSpeed * LogicClock.Tick;

        _character.Inversed = true;
        StartAction("BoxingMoveForward");
        Vector3 from = _rig.transform.position;
        //反手时物理上按的是 Backward，经 GetKeyInput 镜像成大脑血管里的"前进"，所以动作照样能自取消撑住
        for (int i = 0; i < 10; i++) TickWhole(KeyMap.Backward, KeyMap.Up);
        Assert.AreEqual(from.z + 10 * perTick, _rig.transform.position.z, 1e-6f, "反手时纵深不镜像，符号不能翻");
        Assert.AreEqual(from.x - 10 * walkPerTick, _rig.transform.position.x, 1e-6f, "反手时前进应当往 -x 走");
    }

    /// <summary>
    /// 屏幕下方 = 靠近镜头 = -z
    /// </summary>
    [Test]
    public void DepthMovesTowardCameraWhenHoldingDown()
    {
        float perTick = _character.moveSpeed * _character.depthSpeedRate * LogicClock.Tick;

        StartAction("BoxingMoveForward");
        Vector3 from = _rig.transform.position;
        for (int i = 0; i < 10; i++) TickWhole(KeyMap.Forward, KeyMap.Duck);
        Assert.AreEqual(from.z - 10 * perTick, _rig.transform.position.z, 1e-6f, "按住屏幕下方应当往镜头这侧走");
    }

    /// <summary>
    /// 纵深和横向共用同一个 inputAcceptance：一个"停止双腿"的动作同时锁住两根轴。
    /// 表里 Punch1 没有 inputAcceptance（倍率 0），所以按住任何方向都不该动
    /// </summary>
    [Test]
    public void DepthRespectsTheSameInputAcceptanceAsHorizontal()
    {
        StartAction("Punch1");
        Assert.AreEqual(0f, _action.MoveInputAcceptance, 1e-6f, "出招当中移动接受度是 0，前提先钉住");

        Vector3 from = _rig.transform.position;
        for (int i = 0; i < 6; i++) TickWhole(KeyMap.Up, KeyMap.Forward);
        Assert.AreEqual(from.z, _rig.transform.position.z, 1e-6f, "接受度为 0 的时候纵深不许动");
        Assert.AreEqual(from.x, _rig.transform.position.x, 1e-6f, "接受度为 0 的时候横向也不许动");
    }

    /// <summary>
    /// GoStraight 的第四个参数是纵深米数。【老表只填到第三个参数，缺省必须是 0】——
    /// 现有 13 条动作的逐帧位移一个都不许变，否则第 6 条用例钉住的手感就作废了
    /// </summary>
    [Test]
    public void GoStraightCarriesDepthOnlyWhenTheFourthParamIsGiven()
    {
        RootMotionFunc func = RootMotionMethod.Methods["GoStraight"];

        RootMotionParam old = RootMotionMethod.Bake("GoStraight", new[] { "0.2", "5", "8" }, 38);
        for (int frame = 1; frame <= 38; frame++)
            Assert.AreEqual(0f, func.Eval(frame, old).z, 0f, "三个参数的老写法，纵深恒为 0");

        RootMotionParam deep = RootMotionMethod.Bake("GoStraight", new[] { "0.2", "5", "8", "0.1" }, 38);
        Assert.AreEqual(0.1f, func.Eval(38, deep).z, "走满之后纵深应当正好是表里的 0.1 米");
        Assert.AreEqual(func.Eval(38, deep).x * 0.5f, func.Eval(38, deep).z,
            "x 与 z 走同一条比例曲线：同一时刻两者的进度比例相同");
        //前摇那一拍两者都还是 0，斜向冲刺不会在起跳前偷跑
        Assert.AreEqual(0f, func.Eval(5, deep).z, 0f, "第 5 帧之前纵深不前进");
    }

    /// <summary>
    /// 同轨推挤：只动 z、不动 x/y，两个角色各推一半，合起来刚好隔出一个 need。
    /// 完全重合时方向由数组顺序定死——同一份输入必须每次跑出同样的结果，
    /// 这是本工程最硬的约束（换台机器、换渲染帧率都不许变）
    /// </summary>
    [Test]
    public void SeparateLanesPushesAlongZOnlyAndIsDeterministic()
    {
        GameObject secondRig = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        secondRig.name = "ACT-Test-Rig-2";
        secondRig.hideFlags = HideFlags.DontSaveInEditor | HideFlags.HideInHierarchy;
        try
        {
            CharacterObj a = _character;
            CharacterObj b = secondRig.GetComponent<CharacterObj>();
            float need = a.laneRadius + b.laneRadius;

            //不同轨（横向已经隔开）：不该推
            a.transform.position = new Vector3(0f, 0f, 0f);
            b.transform.position = new Vector3(need, 0f, 0f);
            CharacterObj.SeparateLanes(new[] { a, b });
            Assert.AreEqual(0f, a.transform.position.z, 1e-6f, "横向没贴近就算不同轨，不用推");

            //同一轨、纵向重合：各推一半，只动 z
            a.transform.position = new Vector3(1.5f, 0.25f, -2f);
            b.transform.position = new Vector3(1.5f, 0.25f, -2f);
            CharacterObj.SeparateLanes(new[] { a, b });
            Assert.AreEqual(1.5f, a.transform.position.x, 1e-6f, "推挤不许动 x");
            Assert.AreEqual(0.25f, a.transform.position.y, 1e-6f, "推挤不许动 y（高度归地面探测管）");
            Assert.AreEqual(-2f - need * 0.5f, a.transform.position.z, "数组里靠前的那个往近侧退");
            Assert.AreEqual(-2f + need * 0.5f, b.transform.position.z, "靠后的那个往远侧退");
            Assert.AreEqual(need, b.transform.position.z - a.transform.position.z, 1e-6f, "两人正好隔开一个 need");

            //再推一次不该继续叠加
            CharacterObj.SeparateLanes(new[] { a, b });
            Assert.AreEqual(need, b.transform.position.z - a.transform.position.z, 1e-6f, "已经隔开了就不再推");
        }
        finally
        {
            Object.DestroyImmediate(secondRig);
        }
    }

    //==================================== 11. 相交采样：命中按逻辑帧算 ====================================

    private GameObject _targetRig;
    private CharacterObj _target;
    private CharacterObj[] _pair;
    private readonly Collider[] _hitBuffer = new Collider[64];
    private AttackHitBox _fist;
    private BeHitBox _targetBody;
    private Collider _fistCol;
    private Collider _bodyCol;

    /// <summary>
    /// 立第二个角色当靶子，从 Player.prefab 现取（所以受击盒在不在采样层上，这里一并被验到）。
    /// 靶子只跑输入/动作/判定盒/采样四拍，不跑移动拍和推挤拍——那两拍会自己改坐标，
    /// 而本组用例要把手动摆放的位置原封不动地保持住
    /// </summary>
    private void BuildTarget()
    {
        _targetRig = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        _targetRig.name = "ACT-Test-Target";
        _targetRig.hideFlags = HideFlags.DontSaveInEditor | HideFlags.HideInHierarchy;
        _target = _targetRig.GetComponent<CharacterObj>();
        _target.BindBoxes();
        _target.action.SetAllActions(GameData.AllActions(), "BoxingStand");
        _pair = new[] { _character, _target };

        int punchTag = BoxTagRegistry.MaskOf("LeftPunch");
        foreach (AttackHitBox b in _character.AttackBoxes)
            if ((b.TagMask & punchTag) != 0) _fist = b;
        int hurtTag = BoxTagRegistry.MaskOf("Hurt");
        foreach (BeHitBox b in _target.BeHitBoxes)
            if ((b.TagMask & hurtTag) != 0) _targetBody = b;
        Assert.IsNotNull(_fist, "前提：Player.prefab 里得有 tag 为 LeftPunch 的攻击盒，表里 Punch1 开的就是它");
        Assert.IsNotNull(_targetBody, "前提：靶子得有 tag 为 Hurt 的受击盒，BoxingStand 全程开着它");

        _fistCol = _fist.GetComponent<Collider>();
        _bodyCol = _targetBody.GetComponent<Collider>();
        Assert.IsNotNull(_fistCol, "前提：攻击盒身上必须有碰撞体，主动采样查的就是它");
        Assert.IsNotNull(_bodyCol, "前提：受击盒身上必须有碰撞体，否则它在采样里等于不存在");
        Assert.AreEqual(LayerMask.NameToLayer("BeHitBox"), _targetBody.gameObject.layer,
            "Player.prefab 里受击盒得归在 BeHitBox 层上，采样只查这一层");
    }

    /// <summary>
    /// 盒子的世界中心。Collider本身没有center字段，只有派生形状有；
    /// 这里不用 bounds.center，因为 bounds 读的是物理场景，而本帧的骨骼位移还没同步进去（同步是采样那一步才做的）
    /// </summary>
    private static Vector3 WorldCenter(Collider col)
    {
        Transform t = col.transform;
        SphereCollider sphere = col as SphereCollider;
        if (sphere) return t.TransformPoint(sphere.center);
        CapsuleCollider capsule = col as CapsuleCollider;
        if (capsule) return t.TransformPoint(capsule.center);
        BoxCollider box = col as BoxCollider;
        if (box) return t.TransformPoint(box.center);
        Assert.Fail("用例只认 Sphere/Capsule/Box 三种盒子，遇到的是 " + col.GetType().Name);
        return Vector3.zero;
    }

    /// <summary>
    /// 把靶子的受击盒中心压到拳盒中心上，再沿 z 退开 dz 米。
    /// 胶囊的轴正好穿过球心，于是"两个盒子重叠"的充要条件就是 |dz| ≤ 两个半径之和（0.3+0.3=0.6），
    /// 期望值可以自己算出来，不用反过来问实现——这才是用例
    /// 【为什么每帧都要摆】：拳盒挂在骨骼上，动画一跑它就动，摆一次追不住；
    /// 而本组用例要断言的只有"盒子开在哪几拍"，几何重叠必须是恒真式（dz=0）
    /// </summary>
    private void PlaceTargetAtFistDepth(float dz)
    {
        Vector3 fist = WorldCenter(_fistCol);
        Vector3 body = WorldCenter(_bodyCol);
        _targetRig.transform.position += fist - body + new Vector3(0f, 0f, dz);
    }

    /// <summary>
    /// 走一拍战斗链，顺序照 GameMain.TickAll：时钟→输入→动作→判定盒→相交采样。
    /// 之后由用例自己决定要不要摆位置（摆了就得在采样之前）
    /// </summary>
    private void TickCombat(params KeyMap[] pressed)
    {
        LogicClock.Advance();
        _input.Tick();
        _target.input.Tick();
        for (int i = 0; i < pressed.Length; i++) _input.AddInput(pressed[i]);
        _action.Tick();
        _target.action.Tick();
        RefreshBoxes(_character);
        RefreshBoxes(_target);
        CharacterObj.SampleOverlaps(_pair, _hitBuffer);
    }

    private static void RefreshBoxes(CharacterObj c)
    {
        foreach (AttackHitBox b in c.AttackBoxes) b.Tick();
        foreach (BeHitBox b in c.BeHitBoxes) b.Tick();
    }

    private bool PunchLanded()
    {
        return _character.CanAttackTargetNow(_target, out AttackInfo _, out BeHitBoxTurnOnInfo _,
            out AttackHitBox _, out BeHitBox _);
    }

    /// <summary>
    /// 命中发生在表里开着盒子的那几拍，一拍不多一拍不少。
    /// 【这条用例在改之前必然红】：旧实现靠 OnTriggerEnter，而 EditMode 根本不跑物理模拟，
    /// 回调一次都不会来——换句话说"打到人"这件事以前整个在物理时钟上，用例连测都测不到它。
    /// 现在它是逻辑帧上的整数比较，跟 BoxTagRegistry 里的窗口一样钉得住
    /// </summary>
    [Test]
    public void HitsLandOnExactlyTheFramesTheTableOpensTheBox()
    {
        BuildTarget();
        ActionInfo punch = Info("Punch1");
        FrameRange window = punch.attackPhase[0].inFrames[0];
        StartAction("Punch1");

        for (int frame = 1; frame <= punch.durationFrames; frame++)
        {
            TickCombat();
            PlaceTargetAtFistDepth(0f);
            CharacterObj.SampleOverlaps(_pair, _hitBuffer);
            Assert.IsTrue(_targetBody.Active,
                "第" + frame + "帧靶子的受击盒是关的：BoxingStand 的 defensePhase 覆盖全程，" +
                "这条一旦不成立，下面的断言就变成在测受击盒开关而不是测采样");
            Assert.AreEqual(LiveAt(window, frame), PunchLanded(),
                "第" + frame + "帧：表里 Punch1 的窗口是[" + window.min + "," + window.max +
                "]，命中必须恰好落在被跨过的那几拍（旧实现落在物理步上，错频时会漏拍也会多拍）");
        }
    }

    /// <summary>
    /// 接触列表每拍清空重填：靶子走开的那一拍就必须打不着，走回来那一拍又立刻打得着。
    /// 旧的 OnTriggerEnter/Exit 是增量语义，Exit 因为错频没来，一条早就解除的接触就会永久脏在表里，
    /// 表现是"人已经跑开了还能被连上"——那种 bug 只在看回放时才看得出来
    /// </summary>
    [Test]
    public void TouchIsRebuiltEveryFrameNotIncrementallyMaintained()
    {
        BuildTarget();
        StartAction("Punch1");
        FrameRange window = Info("Punch1").attackPhase[0].inFrames[0];
        //窗口按跨帧语义实际是第4/5/6帧，正好够三步：打着→走开→回来
        Assert.GreaterOrEqual(window.max - window.min + 2, 3, "前提：窗口至少跨三拍，否则本用例走不完三步");

        for (int i = 0; i < 4; i++) TickCombat();
        PlaceTargetAtFistDepth(0f);
        CharacterObj.SampleOverlaps(_pair, _hitBuffer);
        Assert.IsTrue(PunchLanded(), "第4帧：窗口开着而且重叠着，必须打着");

        //沿 z 退开 2 米（半径之和只有 0.6），这一步之后几何上确实不再重叠
        PlaceTargetAtFistDepth(2f);
        TickCombat();
        Assert.IsFalse(PunchLanded(), "第5帧：靶子走开了就必须立刻打不着，接触不许留在表里");

        PlaceTargetAtFistDepth(0f);
        TickCombat();
        Assert.IsTrue(PunchLanded(), "第6帧：走回来要立刻重新打上，说明清空重填不是清了就没了");
    }

    /// <summary>
    /// 自己的拳盒压在自己的受击盒上也算没打到。采样按盒子的主人过滤（bhb.master != master），
    /// 而物理查询本身可不管是谁——两个盒子同层、同一个人身上，天然重叠
    /// </summary>
    [Test]
    public void TheAttackersOwnHurtBoxesNeverCountAsATarget()
    {
        BuildTarget();
        StartAction("Punch1");
        BeHitBox own = null;
        foreach (BeHitBox b in _character.BeHitBoxes)
            if ((b.TagMask & BoxTagRegistry.MaskOf("Hurt")) != 0) own = b;
        Collider ownCol = own.GetComponent<Collider>();

        for (int i = 0; i < 4; i++) TickCombat();
        //把拳盒按到自己的受击盒中心上：几何上真重叠了，下面那条断言测的才是主人过滤
        _fistCol.transform.position = WorldCenter(ownCol);
        CharacterObj.SampleOverlaps(new[] { _character }, _hitBuffer);
        Assert.IsTrue(_fist.Active, "前提：这一拍拳盒是开着的，否则什么都没测到");
        Assert.IsTrue(own.Active, "前提：这一拍自己的受击盒也是开着的");
        Assert.IsFalse(_character.CanAttackTargetNow(_character, out AttackInfo _, out BeHitBoxTurnOnInfo _,
                out AttackHitBox _, out BeHitBox _),
            "自己打自己：受击盒属于攻击方自己，采样必须把它滤掉");
    }

    /// <summary>
    /// 大幅快速位移下不漏拍：靶子每帧沿 z 走 0.5 米（合 30 米/秒，比击退和冲刺还快），
    /// 穿过拳头的那几帧必须一帧不落地记下来。
    /// 以前这条根本无从谈起——物理步是 50Hz（0.02 秒），两个物理步之间角色能走 0.6 米，
    /// 盒子是"穿过去"而不是"重叠过"，窄窗口就静默打空。
    /// 期望值 |dz| ≤ 0.6 是按几何自己算的（两个半径之和），没有回头问实现
    /// </summary>
    [Test]
    public void FastPassAlongDepthIsCaughtOnEveryOverlappingFrame()
    {
        BuildTarget();
        Vector3 scale = _fistCol.transform.lossyScale;
        Assert.Less(Mathf.Abs(1f - Mathf.Abs(scale.x)) + Mathf.Abs(1f - Mathf.Abs(scale.y)) +
                    Mathf.Abs(1f - Mathf.Abs(scale.z)), 0.01f, "前提：拳盒所在骨骼不带缩放，半径才是世界单位");

        float reach = ((SphereCollider)_fistCol).radius + ((CapsuleCollider)_bodyCol).radius;
        Assert.AreEqual(0.6f, reach, "前提：接触厚度=两个半径之和");

        ActionInfo punch = Info("Punch1");
        FrameRange window = punch.attackPhase[0].inFrames[0];
        StartAction("Punch1");

        //第1帧从 +1.5 米开始，每帧往 -z 走 0.5 米，七帧穿到 -1.5 米：途中正好三帧在接触厚度以内
        for (int frame = 1; frame <= 7; frame++)
        {
            float dz = 1.5f - 0.5f * (frame - 1);
            TickCombat();
            PlaceTargetAtFistDepth(dz);
            CharacterObj.SampleOverlaps(_pair, _hitBuffer);
            bool expected = Mathf.Abs(dz) <= reach && LiveAt(window, frame);
            Assert.AreEqual(expected, PunchLanded(),
                "第" + frame + "帧靶子在 dz=" + dz + " 处，重叠=" + (Mathf.Abs(dz) <= reach) +
                "、窗口开着=" + LiveAt(window, frame) + "，两个条件同时成立才算打到");
        }
    }

    /// <summary>
    /// 采样只查 BeHitBox 那一层：把受击盒挪回 Default 层，几何上照样重叠但什么都查不到。
    /// 这条钉的是"地面、道具、别的攻击盒不会来挤占查询缓冲区"这层过滤——
    /// 缓冲区只有 64 格，一个场景里的大碰撞体要是能进来，角色一多就溢出漏判
    /// </summary>
    [Test]
    public void HurtBoxesOnAnotherLayerAreInvisibleToSampling()
    {
        BuildTarget();
        StartAction("Punch1");
        for (int i = 0; i < 4; i++) TickCombat();
        PlaceTargetAtFistDepth(0f);
        CharacterObj.SampleOverlaps(_pair, _hitBuffer);
        Assert.IsTrue(PunchLanded(), "前提：还在采样层上时是打着的，否则下面那条断言什么都没测到");

        _targetBody.gameObject.layer = LayerMask.NameToLayer("Default");
        TickCombat();
        PlaceTargetAtFistDepth(0f);
        CharacterObj.SampleOverlaps(_pair, _hitBuffer);
        Assert.IsFalse(PunchLanded(), "第5帧：盒子离开采样层就必须查不到，层掩码是查询的第一道过滤");
    }

    //==================================== 12. 帧事件与盒子轨迹（切片 1.5） ====================================
    //
    // 【这一区的表项是现造的】：往 Action.json 里塞测试行会污染策划的表，而且真表里此刻还没有
    // 带 track/cues 的行。所以用例自己 new 一张 List 交给 SetAllActions（它只换引用不 Clear，
    // 全工程共享的那份表不受影响），烘焙两步（tag→位掩码、track→稠密数组）照 GameData.Load 的做法补一遍。

    private List<string> _fired;
    private AttackHitBox _trackBox;
    private BoxCollider _trackCol;
    private Vector3 _trackHome = new Vector3(0.3f, 0.4f, 0f);

    private static ActionCue Cue(int frame, CueKind kind)
    {
        return new ActionCue { frame = frame, kind = kind, param = "" };
    }

    private static FrameRange Win(int min, int max)
    {
        return new FrameRange { min = min, max = max };
    }

    private static AttackBoxKey Key(int frame, float x, float y, float sizeX, float sizeY)
    {
        return new AttackBoxKey
        {
            startFromFrame = frame,
            position = new Vector3(x, y, 0f),
            size = new Vector3(sizeX, sizeY, 0.5f),
        };
    }

    private static AttackBoxTurnOnInfo Phase(int priority, AttackBoxKey[] track, params FrameRange[] window)
    {
        return new AttackBoxTurnOnInfo
        {
            tag = new[] { TrackTag },
            inFrames = window,
            priority = priority,
            track = track,
        };
    }

    private const string TrackTag = "TestTrack";

    /// <summary>
    /// 现造一条表项，并把运行期要用到的两样东西烘焙好（顺序照 GameData.Load）
    /// </summary>
    private static ActionInfo Row(string id, int durationFrames, ActionCue[] cues,
        params AttackBoxTurnOnInfo[] phases)
    {
        for (int i = 0; i < phases.Length; i++)
        {
            phases[i].tagMask = BoxTagRegistry.MaskOf(phases[i].tag);
            if (AttackBoxKey.Bake(phases[i].track, durationFrames, out Vector3[] pos, out Vector3[] size, id))
            {
                phases[i].bakedPosition = pos;
                phases[i].bakedSize = size;
            }
        }

        return new ActionInfo
        {
            id = id,
            animKey = id,
            durationFrames = durationFrames,
            cues = cues ?? new ActionCue[0],
            attackPhase = phases,
            defensePhase = new BeHitBoxTurnOnInfo[0],
            beCancelledTag = new BeCancelledTag[0],
            tempBeCancelledTag = new TempBeCancelledTag[0],
            cancelTag = new CancelTag[0],
            commands = new ActionCommand[0],
            inputAcceptance = new MoveInputAcceptance[0],
            attacks = new AttackInfo[0],
        };
    }

    /// <summary>把现造的表项灌进控制器，第一行动作立刻生效</summary>
    private void StartCustom(params ActionInfo[] rows)
    {
        _action.SetAllActions(new List<ActionInfo>(rows), rows[0].id);
    }

    /// <summary>
    /// 监听 cue：记下"哪一条"和"在哪一拍被派发"，于是时机和内容一起断言
    /// </summary>
    private void RecordCues()
    {
        _fired = new List<string>();
        CueDispatch.Handler = (owner, info, cue) => _fired.Add(cue.frame + "/" + info.id + "@" + _action.CurrentFrame);
    }

    /// <summary>
    /// 现挂一只带 BoxCollider 的攻击盒。
    /// 【为什么不用 prefab 上现成的那只】：它在骨骼上，Animator 每拍都改写骨骼的 localPosition，
    /// "没有轨迹就不该碰变换"这条断言会被动画自己打破；这里是根节点的野儿子，动画不管它
    /// </summary>
    private void BuildTrackBox()
    {
        GameObject go = new GameObject("TestTrackBox");
        go.transform.SetParent(_rig.transform, false);
        go.transform.localPosition = _trackHome;
        _trackCol = go.AddComponent<BoxCollider>();
        _trackCol.size = Vector3.one;
        _trackBox = go.AddComponent<AttackHitBox>();
        _trackBox.master = _character;
        _trackBox.tags = new[] { TrackTag };
        _trackBox.BakeTagMask();
        _trackBox.BindCollider(_trackCol);
    }

    /// <summary>走一拍：动作拍 + 判定盒拍（顺序照 GameMain.TickAll 的第三拍）</summary>
    private void TickBox()
    {
        Tick();
        _trackBox.Tick();
    }

    [Test]
    public void CuesFireOnTheTickTheirFrameIsReachedAndNeverTwice()
    {
        RecordCues();
        StartCustom(Row("CueTest", 10, new[]
        {
            Cue(0, CueKind.Sfx), Cue(3, CueKind.Effect), Cue(7, CueKind.CameraShake),
        }));

        for (int frame = 1; frame <= 10; frame++)
        {
            Tick();
            Assert.AreEqual(frame, _action.CurrentFrame, "前提：这一拍时钟确实走到了 " + frame);
        }

        //"@后面的数"就是派发那一拍的帧号：cue 落在哪一拍就该在那一拍响，
        //第0拍的 cue 在 CF=1 那拍响——跟窗口 [0,x] 实际生效的第一拍是同一个时机
        Assert.AreEqual(new[] { "0/CueTest@1", "3/CueTest@3", "7/CueTest@7" }, _fired.ToArray(),
            "三条 cue 必须各自只响一次、响在该响的那一拍");
    }

    /// <summary>
    /// 卡肉的三拍里 cue 一条都不响，顿挫结束的第一拍补上——
    /// 既不提前（挥空声抢在卡肉之前响会削掉打击感），也不丢（丢了就是少个声音，查不到）
    /// </summary>
    [Test]
    public void HitstopDelaysCuesInsteadOfDroppingOrDoublingThem()
    {
        RecordCues();
        StartCustom(Row("CueFreeze", 20, new[] { Cue(3, CueKind.Sfx) }));
        Tick();
        Assert.AreEqual(1, _action.CurrentFrame);

        _action.ApplyHitStop(3);
        for (int i = 0; i < 3; i++)
        {
            Tick();
            Assert.AreEqual(1, _action.CurrentFrame, "顿挫的第" + (i + 1) + "拍时钟不该前进");
        }
        Assert.IsEmpty(_fired, "硬直期间一条 cue 都不该派发");

        Tick();
        Assert.AreEqual(2, _action.CurrentFrame, "三拍硬直用完，这一拍恢复推进");
        Assert.IsEmpty(_fired, "时钟还没跨过第3拍，不该响");

        Tick();
        Assert.AreEqual(new[] { "3/CueFreeze@3" }, _fired.ToArray(), "顿挫结束后第一拍就该把欠的那条补上");
        for (int i = 0; i < 5; i++) Tick();
        Assert.AreEqual(1, _fired.Count, "补过一次之后不能再重复");
    }

    /// <summary>
    /// 循环动作每圈把 cue 重放一次、且只一次。
    /// 【为什么单独立一条】：回绕那一拍的 wasFrame/frame 是倒着的（末帧 → 0），
    /// 拿窗口那套跨区间比较判断"有没有跨过这一拍"会整条跳过，跑步脚步声就是这么丢的
    /// </summary>
    [Test]
    public void LoopingActionReplaysEachCueOncePerLap()
    {
        RecordCues();
        ActionInfo loop = Row("CueLoop", 4, new[] { Cue(0, CueKind.Footstep), Cue(2, CueKind.Footstep) });
        loop.autoNextActionId = "CueLoop";
        loop.keepPlayingAnim = true;
        StartCustom(loop);

        for (int i = 0; i < 8; i++) Tick();

        Assert.AreEqual(new[]
        {
            "0/CueLoop@1", "2/CueLoop@2",     //第一圈
            "0/CueLoop@1", "2/CueLoop@2",     //第二圈，回绕后重新数
        }, _fired.ToArray(), "八拍正好两圈，每圈两条 cue 各响一次");
    }

    /// <summary>
    /// 有轨迹的盒子每一拍被摆到表里指定的位置和尺寸：窗口开着才动，没走到下一条关键帧就沿用上一条，
    /// 关掉的那一拍一个数都不写。
    /// 【这条以前根本不存在】：盒子在哪只由 .anim 曲线决定，表里管不着，
    /// 于是"这一拳在哪一帧打到谁"有一半真源在动画里——改动画就悄悄改了判定
    /// </summary>
    [Test]
    public void TrackPosesTheBoxOnEveryFrameTheWindowIsOpen()
    {
        BuildTrackBox();
        StartCustom(Row("Track1", 10, null,
            Phase(0, new[] { Key(2, 0.5f, 0.5f, 1f, 1f), Key(4, 1.5f, 0.2f, 2f, 0.5f) }, Win(2, 3))));

        TickBox();
        Assert.AreEqual(_trackHome, _trackBox.transform.localPosition,
            "第1拍窗口还没开，盒子必须留在 prefab 的位置上");

        TickBox();
        Assert.AreEqual(new Vector3(0.5f, 0.5f, 0f), _trackBox.transform.localPosition, "第2拍用第一条关键帧");
        Assert.AreEqual(new Vector3(1f, 1f, 0.5f), _trackCol.size, "尺寸同样来自表");

        TickBox();
        Assert.AreEqual(new Vector3(0.5f, 0.5f, 0f), _trackBox.transform.localPosition,
            "第3拍没有新的关键帧，要沿用上一条（constant 曲线就是这个语义）");

        TickBox();
        Assert.AreEqual(new Vector3(1.5f, 0.2f, 0f), _trackBox.transform.localPosition, "第4拍换第二条");
        Assert.AreEqual(new Vector3(2f, 0.5f, 0.5f), _trackCol.size, "第二条关键帧把尺寸一起换了");
        Assert.IsTrue(_trackBox.Active, "前提：第4拍还在窗口里（生效 = min..max+1）");

        TickBox();
        Assert.IsFalse(_trackBox.Active, "第5拍窗口关了");
        //故意把盒子挪到一个谁都不在的值上：如果关掉还在写轨迹，它会被拽回第4拍的位置
        _trackBox.transform.localPosition = new Vector3(9f, 9f, 9f);
        _trackBox.Tick();
        Assert.AreEqual(new Vector3(9f, 9f, 9f), _trackBox.transform.localPosition,
            "关着的盒子不许被轨迹摆位：否则读它位置的东西会看到一帧本该看不见的判定");
    }

    /// <summary>
    /// 表项不填 track 就是"盒子不动"，本工程原有的做法一个字节都不变
    /// </summary>
    [Test]
    public void PhaseWithoutTrackLeavesTheBoxWhereThePrefabPutIt()
    {
        BuildTrackBox();
        StartCustom(Row("Track0", 6, null, Phase(0, null, Win(1, 3))));

        for (int i = 0; i < 3; i++) TickBox();
        Assert.IsTrue(_trackBox.Active, "前提：这一拍盒子是开着的，否则下面那条断言什么都没测到");
        Assert.AreEqual(_trackHome, _trackBox.transform.localPosition, "没填轨迹就一个数都不该写");
        Assert.AreEqual(Vector3.one, _trackCol.size, "尺寸也不该被默认的零值糊上去");
    }

    /// <summary>
    /// 两条表项同一拍开启同一个盒子时，轨迹由优先级高的那条说了算——
    /// 跟 GetAttackBoxTempPriority 同一个规则。取第一条的话"谁说了算"就取决于策划的填表顺序
    /// </summary>
    [Test]
    public void HighestPriorityPhaseOwnsTheBoxPose()
    {
        BuildTrackBox();
        StartCustom(Row("Track2", 8, null,
            Phase(1, new[] { Key(1, 0.1f, 0.1f, 1f, 1f) }, Win(1, 4)),
            Phase(9, new[] { Key(1, 0.9f, 0.2f, 3f, 1f) }, Win(1, 4))));

        TickBox();
        TickBox();
        Assert.AreEqual(new Vector3(0.9f, 0.2f, 0f), _trackBox.transform.localPosition,
            "高优先级那条排在数组第二位也得它说了算");
        Assert.AreEqual(new Vector3(3f, 1f, 0.5f), _trackCol.size, "尺寸同样跟着高优先级那条");
    }

    //==================================== 13. 派生门禁、长按、确定性随机（切片 1.6） ====================================
    //
    // 表项同样是现造的（理由见第12区那段）。这一区钉的是「标签对上了、指令也搓对了，
    // 但此刻到底准不准」这一层——以前框架里根本没有这一层，配不出来也配不了。

    private static ScriptMethodInfo Cond(string method, params string[] param)
    {
        return new ScriptMethodInfo { method = method, param = param };
    }

    /// <summary>
    /// 一个「想挤掉别人」的动作：靠 byKey 这条指令进来，外面套一道门禁
    /// </summary>
    private static ActionInfo Comeback(string id, string gate, ScriptMethodInfo condition, KeyMap byKey)
    {
        ActionInfo info = Row(id, 20, null);
        info.cancelTag = new[]
        {
            new CancelTag {tag = gate, startFromFrame = 0, fadeInFrames = 0, priority = 0,
                cancelCondition = condition},
        };
        info.commands = new[]
        {
            new ActionCommand {keySequence = new[] {byKey}, validInFrames = 2},
        };
        return info;
    }

    /// <summary>
    /// 一个「等着被挤掉」的动作：整段都开着那道门禁的窗口，40 拍，用例期间不会自己结束
    /// </summary>
    private static ActionInfo Waiting(string id, string gate)
    {
        ActionInfo info = Row(id, 40, null);
        info.beCancelledTag = new[]
        {
            new BeCancelledTag
            {
                cancelWindowFrames = new FrameRange {min = 0, max = 38},
                cancelTag = new[] {gate},
                fadeOutFrames = 0,
                priority = 0,
            },
        };
        return info;
    }

    /// <summary>
    /// 历史里还剩不还剩这个键，用一条 60 帧的回看窗口去问。
    /// 【为什么不用 IsKeyHeld】：它的窗口只有两拍，刚按下的 Punch 本来就还在窗口里，
    /// 那样「清理生效了没」这条断言会恒真
    /// </summary>
    private bool InHistory(KeyMap key)
    {
        return _input.ActionOccur(new ActionCommand
        {
            keySequence = new[] {key},
            validInFrames = 60,
        });
    }

    /// <summary>
    /// 地面/空中门禁：同一张表、同一个输入，状态不同结果就不同。
    /// 【为什么滞空那段要动位置】：State 只由地面探测写，而 EditMode 里 Awake 没跑过，
    /// `_groundMask` 还是 0——射线查不到任何层，于是走过一拍移动就必然算成滞空。
    /// 这个前提在下面用一条断言当场钉住，不靠「大概是这样」
    /// </summary>
    [Test]
    public void CancelConditionDecidesWhetherAGroundMoveCanFireInTheAir()
    {
        StartCustom(Waiting("Long", "GroundGate"),
            Comeback("GroundPunch", "GroundGate", Cond("StateIs", "Grounded"), KeyMap.Punch));
        //一直没跑过 CharacterObj.Tick，State 还是字段初始值，也就是「在地面上」
        Assert.AreEqual(MoveState.Grounded, _character.State, "前提：还没走过移动拍，角色算站在地上");
        Tick(KeyMap.Punch);
        Assert.AreEqual("GroundPunch", _action.CurrentAction.id, "站着的时候这招必须接得出来");

        //重新来一遍，这次先把角色挪到五米高的半空走一拍
        StartCustom(Waiting("Long", "GroundGate"),
            Comeback("GroundPunch", "GroundGate", Cond("StateIs", "Grounded"), KeyMap.Punch));
        _rig.transform.position = new Vector3(0f, 5f, 0f);
        _character.Tick();
        Assert.AreEqual(MoveState.Airborne, _character.State, "前提：脚下没有任何层可探，这一拍必须算滞空");

        Tick(KeyMap.Punch);
        Assert.AreEqual("Long", _action.CurrentAction.id,
            "空中按同一个键必须接不出地面招——这就是「空中不许接地面招」，以前框架里没有这道门禁");
    }

    /// <summary>
    /// 「近处有一个当前动作属于 Beaten 分类的对手」才允许的追击。
    /// 距离只算 x 与 z：横向隔开三米、或者同侧前后错开三米，都不该算近
    /// </summary>
    [Test]
    public void PursuitConditionNeedsALyingTargetWithinTheRange()
    {
        BuildTarget();
        ActionInfo[] table =
        {
            Waiting("Long", "PursuitGate"),
            Comeback("GroundPursuit", "PursuitGate", Cond("NearbyUnitInCatalog", "Beaten", "1.2"),
                KeyMap.Kick),
        };
        PutTargetIn("Beaten", new Vector3(0.8f, 0f, 0f));
        Assert.IsTrue(CharacterObj.AnyOtherInCatalog(_character, "Beaten", 1.2f),
            "前提：靶子确实躺在 1.2 米以内，位置 " + _rig.transform.position + " / " +
            _targetRig.transform.position + "——不成立就说明注册表或距离算错了，跟门禁无关");

        StartCustom(table);
        Tick(KeyMap.Kick);
        Assert.AreEqual("GroundPursuit", _action.CurrentAction.id, "零点八米外躺着一个人，追击必须成立");

        StartCustom(table);
        PutTargetIn("Beaten", new Vector3(3f, 0f, 0f));
        Tick(KeyMap.Kick);
        Assert.AreEqual("Long", _action.CurrentAction.id, "横向三米已经超出 1.2 米半径，不该追");

        StartCustom(table);
        PutTargetIn("Beaten", new Vector3(0.8f, 0f, 3f));
        Tick(KeyMap.Kick);
        Assert.AreEqual("Long", _action.CurrentAction.id,
            "纵深错开三米是另一个车道的人，横版里不该算近——所以距离必须算 x 和 z 两根轴");

        StartCustom(table);
        PutTargetIn("Stand", new Vector3(0.8f, 0f, 0f));
        Tick(KeyMap.Kick);
        Assert.AreEqual("Long", _action.CurrentAction.id,
            "人还站着（当前动作不属于 Beaten 分类）就不许追击倒地技——条件查的是动作，不只是距离");
    }

    /// <summary>
    /// 把靶子摆到"离我 offset 那么远"、并让它当前动作属于某个分类。
    /// 【相对位置】：靶子的绝对坐标要跟着宿主走，写死绝对值等于假设 prefab 的原点在 (0,0,0)，
    /// 那是 prefab 的事而不是用例的事
    /// </summary>
    private void PutTargetIn(string catalog, Vector3 offset)
    {
        _targetRig.transform.position = _rig.transform.position + offset;
        ActionInfo down = Row(catalog + "Row", 60, null);
        down.catalog = catalog;
        _target.action.SetAllActions(new List<ActionInfo> { down }, down.id);
    }

    /// <summary>
    /// 长按格挡：Block 每拍登记，于是「按住」就是「每一拍这条指令都成立」。
    /// 用例把 Block 一直喂给 AddInput，看动作是不是每拍都被重新授予一次；停手之后看它是不是立刻断。
    /// 【为什么要留着那两拍尾巴】：指令的回看窗口是 2 拍（validInFrames），
    /// 松手后头两拍仍然能匹配到之前那两条记录，这是输入缓冲本来的行为，
    /// 不是长按失效——把它一起断言进来，免得下次有人以为是 bug
    /// 【采样那半边测不到】：SampleRenderFrame 要真键盘，EditMode 里 poll 不到，
    /// 所以「Block 不吃 6 拍节流」这条只能靠代码里的位置和注释保证，是 Step 2 换 Input System 时要接回去的活
    /// </summary>
    [Test]
    public void HeldBlockReGrantsTheDerivationEveryTickAndStopsAfterTheBufferRunsOut()
    {
        ActionInfo blocking = Comeback("Blocking", "BlockGate", new ScriptMethodInfo(), KeyMap.Block);
        //自己能取消自己，而且不保持播放：每拍都重新成立的话，帧号就会被一直打回起点
        blocking.beCancelledTag = new[]
        {
            new BeCancelledTag
            {
                cancelWindowFrames = new FrameRange {min = 0, max = 58},
                cancelTag = new[] {"BlockGate"},
                fadeOutFrames = 0,
                priority = 0,
            },
        };
        blocking.durationFrames = 60;
        StartCustom(Waiting("Long", "BlockGate"), blocking);

        Tick(KeyMap.Block);
        Assert.AreEqual("Blocking", _action.CurrentAction.id, "第一拍按下去就该进格挡");
        Assert.AreEqual(0, _action.CurrentFrame, "从第 0 帧开始");

        for (int i = 0; i < 5; i++)
        {
            Tick(KeyMap.Block);
            Assert.AreEqual(0, _action.CurrentFrame,
                "按住的第" + (i + 2) + "拍又该被重新授予一次，帧号必须停在起点；" +
                "它要是往上涨，就说明这一拍没登记上");
        }

        for (int i = 0; i < 6; i++) Tick();
        Assert.AreEqual("Blocking", _action.CurrentAction.id, "松手不该切到别的动作，只是不再重新成立");
        Assert.AreEqual(4, _action.CurrentFrame,
            "松手六拍：头两拍还落在回看窗口里（继续被打回起点），后四拍才真的往前走");
    }

    /// <summary>
    /// 换动作不许清掉 Block 的历史：格挡是持续状态，清了就会在派生那一刻自己泄掉。
    /// 同一次清理里 Punch 必须被清掉——那是「洗掉非方向键」本来的用途，防止按一下就全自动连招
    /// </summary>
    [Test]
    public void ActionChangeKeepsBlockHistoryButDropsPunch()
    {
        Tick(KeyMap.Punch);
        Tick(KeyMap.Block);
        Assert.IsTrue(InHistory(KeyMap.Block), "前提：刚登记的 Block 当然在历史里");
        Assert.IsTrue(InHistory(KeyMap.Punch), "前提：刚登记的 Punch 也在历史里，否则下面的断言什么都没测到");

        _input.CleanNonDirectionInputs();
        Assert.IsTrue(InHistory(KeyMap.Block), "换动作清历史时 Block 必须留下，长按才不会断");
        Assert.IsFalse(InHistory(KeyMap.Punch), "Punch 就该被清掉，不然全自动连招又回来了");
    }

    /// <summary>
    /// 随机数必须只由种子和抽样次数决定。
    /// 【为什么不用 System.Random / UnityEngine.Random】：前者算法随运行时版本变，后者靠墙钟——
    /// 回放和以后做联网校验时全都会对不上，理由写在 Dice 的头注释里
    /// </summary>
    [Test]
    public void DiceIsFullyDeterminedBySeedAndDrawCount()
    {
        const int draws = 120;
        int[] first = Draw(7, draws);

        Assert.AreEqual(first, Draw(7, draws), "同种子必须逐位一样，不然「同种子逐tick一致」根本无从谈起");
        Assert.AreNotEqual(first[0], Draw(8, 1)[0], "换个种子就该换一串数");

        //同一批逻辑帧换一种「每渲染帧跑几拍」的分法：30fps 每帧 2 拍、120fps 每帧 8 拍，
        //累计到同样的 120 个逻辑帧，结果必须还是那一串。Dice 里只要掺进任何时间量这条就红
        int[] eightPerFrame = new int[draws];
        Dice.Seed(7);
        for (int render = 0; render < draws / 8; render++)
            for (int tick = 0; tick < 8; tick++)
                eightPerFrame[render * 8 + tick] = Dice.Next(1000);
        Assert.AreEqual(first, eightPerFrame, "抽样只认逻辑帧数，不认它被怎么切进渲染帧");

        //退化点要挡住：全 0 状态的 xorshift 会永远吐 0，而「随机数怎么都是 0」这种事故最难发现
        Dice.Seed(0);
        bool anyNonZero = false;
        for (int i = 0; i < 8; i++)
            if (Dice.Next() != 0) anyNonZero = true;
        Assert.IsTrue(anyNonZero, "seed=0 也必须正常出数，不能落在 xorshift 的退化点上");

        Dice.Seed(1);
        for (int i = 0; i < 500; i++)
        {
            int roll = Dice.Next(6);
            Assert.GreaterOrEqual(roll, 0);
            Assert.Less(roll, 6, "Next(max) 的取值范围是 [0,max)");
        }
        Assert.IsFalse(Dice.Chance(0), "0% 永远不成立");
        Assert.IsTrue(Dice.Chance(100), "100% 永远成立");
    }

    private static int[] Draw(uint seed, int times)
    {
        Dice.Seed(seed);
        int[] res = new int[times];
        for (int i = 0; i < times; i++) res[i] = Dice.Next(1000);
        return res;
    }

    /// <summary>
    /// 认不出的门禁函数名必须当场否掉并喊出来：默认允许的话，函数名打错一个字母
    /// 就等于悄悄把门禁拆了，而「这招在空中也能出地面招」这种手感问题没人会怀疑到表里。
    /// 这条会真的走一次 Debug.LogError，所以要用 LogAssert 把它认下来——
    /// 用例里"没预期的 Error"本身就是失败
    /// </summary>
    [Test]
    public void UnknownCancelConditionDeniesInsteadOfAllowing()
    {
        LogAssert.Expect(UnityEngine.LogType.Error,
            new System.Text.RegularExpressions.Regex("找不到取消条件函数.*"));
        StartCustom(Waiting("Long", "TypoGate"),
            Comeback("AnyPunch", "TypoGate", Cond("StateIsNot", "Grounded"), KeyMap.Punch));
        Tick(KeyMap.Punch);
        Assert.AreEqual("Long", _action.CurrentAction.id, "写错函数名时这次派生按不成立处理");

        //留空则是「没有门禁」，现有 13 条动作走的正是这条路径，行为不许有任何变化
        StartCustom(Waiting("Long", "NoGate"), Comeback("AnyPunch", "NoGate", new ScriptMethodInfo(), KeyMap.Punch));
        Tick(KeyMap.Punch);
        Assert.AreEqual("AnyPunch", _action.CurrentAction.id, "没配条件的动作行为不许有任何变化");
    }

    //==================================== 14. 每角色自己的动作子集（切片 1.7 的前置） ====================================

    /// <summary>
    /// 分类索引建在角色自己那份表上，而不是建在全工程那张大表上。
    /// 【为什么这是一条独立的用例】：移植进来的玩家招式与 demo 的拳击招式一旦同表，
    /// "敌兵不会 Hurt 那一类"和"玩家不会 TurnBack"这种事必须由子集说清楚；
    /// 索引还在全局的话，byCatalog 会把角色根本不会的招式端上候选，
    /// 于是"挨了一下切到什么动作"取决于全表的行序，而那是查不出来的错
    /// </summary>
    [Test]
    public void CatalogLookupFollowsTheCharactersOwnActionSubset()
    {
        StartAction("Punch1");
        _action.PreorderActionByActionChangeInfo(HurtByCatalog(), ForceDirection.Forward);
        Tick();
        Assert.AreEqual("HurtFromForward", _action.CurrentAction.id,
            "前提：整张表在场时 byCatalog 会挑到 Hurt 那一类");

        int allBefore = GameData.AllActions().Count;
        _action.SetAllActions(GameData.ActionsFor(new[] {"BoxingStand", "Punch1"}), "Punch1");
        Assert.AreEqual(2, _action.AllActions.Count, "这个角色现在只会两招");
        Assert.AreEqual(allBefore, GameData.AllActions().Count,
            "ActionsFor 只挑不改：全表行数不许因为挑了一次子集就变（它返回的是新建的 List，不是共享那一份）");

        _action.PreorderActionByActionChangeInfo(HurtByCatalog(), ForceDirection.Forward);
        Tick();
        Assert.AreEqual("Punch1", _action.CurrentAction.id,
            "这个角色的子集里没有 Hurt 那一类，候选必须是空的，动作不许被换掉");
    }

    private static ActionChangeInfo HurtByCatalog()
    {
        return new ActionChangeInfo {changeType = ActionChangeType.ChangeByCatalog, param = "Hurt"};
    }

    //==================================== 15. 跳跃、落地与派生优先级（切片 1.7） ====================================
    //
    // 这一区是真表上场的第一处：1.7 把玩家招式灌进 Action.json 之后，
    // 「站着按拳出不出来」「空中能不能再跳一次」这种事已经由**表数据**决定了，
    // 用现造的表项测就等于没测——所以这几条都从 GameData 里取真行。

    private GameObject _groundRig;

    /// <summary>
    /// 第17区那条韧性条用例的临时宿主：一个空 GameObject 上只挂 PoiseGauge，不牵角色进来
    /// </summary>
    private GameObject _poiseRig;

    /// <summary>
    /// 在脚下摆一块 Ground 层的面板，上表面正好在 surfaceY。
    /// 【为什么用例要自己造地面】：EditMode 没有场景，prefab 底下什么都是空的，
    /// 而这一区要断言的是「起飞—滞空—落地」这条只有地形才能走完的路
    /// </summary>
    private void BuildGround(float surfaceY)
    {
        int layer = LayerMask.NameToLayer("Ground");
        Assert.AreNotEqual(-1, layer, "前提：工程里要有 Ground 层，否则地面探测永远落空");
        _groundRig = new GameObject("ACT-Test-Ground");
        _groundRig.hideFlags = HideFlags.DontSaveInEditor | HideFlags.HideInHierarchy;
        _groundRig.layer = layer;
        //盒子中心在地面下方半个厚度处：上表面就正好是 surfaceY
        _groundRig.transform.position = new Vector3(0f, surfaceY - 0.5f, 0f);
        BoxCollider col = _groundRig.AddComponent<BoxCollider>();
        col.size = new Vector3(60f, 1f, 60f);
    }

    /// <summary>
    /// 跑完整一拍：时钟→输入→动作→角色计时与移动。
    /// 顺序照 GameMain.TickAll，只略掉了与这一区无关的相交采样与命中结算两拍
    /// </summary>
    private void TickWithMove(params KeyMap[] pressed)
    {
        Tick(pressed);
        _character.Tick();
    }

    /// <summary>
    /// 表里的 jumpUpSpeed 在换动作那一拍变成真的初速度，飞行由重力积分，落地那一拍切到 landActionId。
    /// 【这一条钉的是三件事】：初速度没被地面探测抹掉、滞空拍数与 weight/jumpUpSpeed 的标定一致、
    /// 落地不等片子播完。三件事以前都没有消费者，也就都没有约束
    /// </summary>
    [Test]
    public void JumpRisesByTableSpeedAndLandsIntoTheLandAction()
    {
        BuildGround(_rig.transform.position.y);
        //重力由用例自己标，不靠 prefab 上的值：滞空拍数就是这两个数除出来的
        _character.weight = 0.8f;
        ActionInfo up = Comeback("Up", "JumpGate", Cond("StateIs", "Grounded"), KeyMap.Jump);
        up.durationFrames = 31;
        up.jumpUpSpeed = 12.4f;
        up.landActionId = "Down";
        StartCustom(Waiting("Idle", "JumpGate"), up, Row("Down", 16, null));

        float footY = _rig.transform.position.y;
        TickWithMove(KeyMap.Jump);
        Assert.AreEqual("Up", _action.CurrentAction.id, "按跳跃的那一拍就该进起跳动作");
        Assert.AreEqual(MoveState.Airborne, _character.State, "起跳这一拍就要算空中");
        Assert.Greater(_rig.transform.position.y, footY,
            "起跳的第一拍必须离地。Move() 里「这一列探不到地面」那条会把下落速度清零（那是给走下平台用的），" +
            "而起飞的那一拍同样探不到地面——不先把状态改成 Airborne，初速度会在同一拍被自己抹掉，人只会原地抖一下");

        float highest = 0f;
        int landCount = 0;
        int landedOnTick = -1;
        for (int i = 0; i < 50; i++)
        {
            TickWithMove();
            highest = Mathf.Max(highest, _rig.transform.position.y - footY);
            if (_character.JustLanded)
            {
                landCount++;
                if (landedOnTick < 0) landedOnTick = i;
            }
        }

        Assert.AreEqual(1, landCount, "一次起跳只该落地一次，落地标志必须只在一拍为真");
        //v0=12.4、每拍加 0.8 → 第 30 拍恰好回到地面（12.4·n = 0.4·n(n+1) 的整数解），最高点在第 15 拍、1.5 米。
        //断言留一格余量：高度是连加出来的，最后一位的浮点差别会决定它是第 29 拍还是第 30 拍贴地
        Assert.GreaterOrEqual(landedOnTick, 27, "滞空只有 " + (landedOnTick + 2) + " 拍：重力或初速度与动画时长脱钩了");
        Assert.LessOrEqual(landedOnTick, 30, "滞空拖到 " + (landedOnTick + 2) + " 拍：PlayerJump 的 31 拍早就该被落地掐断");
        Assert.AreEqual(1.5f, highest, 0.05f, "最高点就该是 1.5 米，这是 weight 与 jumpUpSpeed 一起标定的结果");
        Assert.AreEqual(footY, _rig.transform.position.y, 1e-4f, "落地那一拍要贴回地面，不许陷进地里或悬着");

        TickWithMove();
        Assert.AreEqual("Down", _action.CurrentAction.id,
            "落地那一拍的下一拍切到 landActionId：标志在移动拍里写、动作拍在它之后，所以差一拍，" +
            "而那已经贴地的 16 毫秒里没有任何视觉破绽");
        Assert.AreEqual(MoveState.Grounded, _character.State, "落地之后状态要翻回地面，不然会一直飘");
    }

    /// <summary>
    /// 站着按拳、边跑边按拳，都必须真的出拳。
    /// 【这条不是假想病】：PlayerIdle 一开始跟别的地面招一样挂着 InitAction 和一条 [NoDirection] 指令，
    /// 于是它每一拍都能挤掉自己一次；而冒泡比优先级时同分取的是 AllActions 里靠前的那条，
    /// PlayerIdle 恰好是全表第一条——结果站着按拳一个都出不来。demo 的 BoxingStand 从来不挂 tag，就是这个道理
    /// </summary>
    [Test]
    public void RealTableAttackBeatsTheStandingAndRunningSelfCancel()
    {
        _action.SetAllActions(GameData.ActionsFor(new[] {"PlayerIdle", "PlayerRun", "PlayerPunch1"}), "PlayerIdle");
        Tick();
        Assert.AreEqual("PlayerIdle", _action.CurrentAction.id, "开场动作就是站立");

        Tick(KeyMap.Punch);
        Assert.AreEqual("PlayerPunch1", _action.CurrentAction.id, "站着按拳必须出拳");

        //重新开场再试跑动：上一拍那记拳还开着，PlayerPunch1 的窗口要到第 9 拍才允许派生，
        //而它此刻正是当前动作——不从站立重来一遍，测到的是连招窗口而不是优先级
        _action.SetAllActions(GameData.ActionsFor(new[] {"PlayerIdle", "PlayerRun", "PlayerPunch1"}), "PlayerIdle");
        Tick(KeyMap.Forward);
        Assert.AreEqual("PlayerRun", _action.CurrentAction.id, "前提：按住方向键就该进跑动");
        Tick(KeyMap.Forward, KeyMap.Punch);
        Assert.AreEqual("PlayerPunch1", _action.CurrentAction.id,
            "跑动每拍都自取消一次（这正是它的 autoTerminate 能认出「松手了」的手段），" +
            "所以派生必须靠 priority 压过它，而不是靠填表顺序——表顺序是最查不出来的一种平局");
    }

    /// <summary>
    /// 空中再按一次跳跃不许变成二段跳，但空中按拳要出空中技。
    /// 【两条一起断言】：它们共用同一个 tag（AirAttackTag），把跳跃自己也算上就得到前者，
    /// 忘了把跳跃的 beCancelledTag 打开就得到后者的反面
    /// </summary>
    [Test]
    public void AirborneDoesNotReJumpButDoesAirAttack()
    {
        _action.SetAllActions(
            GameData.ActionsFor(new[] {"PlayerIdle", "PlayerJump", "PlayerJumpAttackPunch"}), "PlayerIdle");
        Assert.AreEqual(MoveState.Grounded, _character.State, "前提：还没走移动拍，算站在地上");

        Tick(KeyMap.Jump);
        Assert.AreEqual("PlayerJump", _action.CurrentAction.id, "真表里跳跃键（103）指令对应的就是这一行");
        Assert.AreEqual(MoveState.Airborne, _character.State, "表里的 jumpUpSpeed 在换动作那一拍就把状态改成了空中");

        Tick(KeyMap.Jump);
        Assert.AreEqual("PlayerJump", _action.CurrentAction.id,
            "空中再按一次不许再跳：起跳那行自己不带 AirAttackTag，所以它挤不掉自己。" +
            "带了的话跳跃键按两下就能一路往上窜，而这件事在 30fps 下还特别难看出来");

        Tick(KeyMap.Punch);
        Assert.AreEqual("PlayerJumpAttackPunch", _action.CurrentAction.id,
            "空中攻击才是 AirAttackTag 的消费者：跳跃的 beCancelledTag 只开这一个口子");
    }

    //==================================== 16. 抓取约束与投掷（切片 1.8） ====================================
    //
    // 这一区走的全是真表：PlayerGrab / PlayerHold / PlayerGrabPunch / PlayerThrow / Grabbed
    // 都是 1.8 写进 Action.json 的行。结算那一步从切片 1.9 起打的是真入口
    // （`CombatPipeline.ResolveHit`，见下面的 ApplyHit），"按下 E 真的举起、真的把人摔出去"
    // 整条链另外在 Play Mode 里端到端验。

    /// <summary>表里 PlayerGrab 那一记的 holdOffset，x/y 分开写是为了让断言读得懂是哪一头错了</summary>
    private const float HoldX = 0.93f;

    private const float HoldY = 0.6f;

    /// <summary>
    /// 完整战斗链的一拍：输入→动作→判定盒→相交采样→移动→同轨推挤→抓取约束。
    /// 【为什么后三拍一拍都不能少】：本区断言的坐标正是最后那一拍写的，而推挤那一拍会不会把被举者推歪，
    /// 也只有跑到他之后才看得出来
    /// </summary>
    private void TickFight(params KeyMap[] pressed)
    {
        LogicClock.Advance();
        _input.Tick();
        _target.input.Tick();
        for (int i = 0; i < pressed.Length; i++) _input.AddInput(pressed[i]);
        _action.Tick();
        _target.action.Tick();
        RefreshBoxes(_character);
        RefreshBoxes(_target);
        CharacterObj.SampleOverlaps(_pair, _hitBuffer);
        _character.Tick();
        _target.Tick();
        CharacterObj.SeparateLanes(_pair);
        CharacterObj.ApplyHolds(_pair);
    }

    /// <summary>
    /// 只给这个角色会的那几行，起点是 startId
    /// </summary>
    private void StartRows(string startId, params string[] ids)
    {
        _action.SetAllActions(GameData.ActionsFor(ids), startId);
    }

    /// <summary>
    /// 结算一次命中：双方换动作、硬直、卡帧、击退、抓取、命中记录。
    /// 【打的是真入口】：`CombatPipeline.ResolveHit`（切片 1.9 之前它是 GameMain 的私有方法，
    /// 这里只能照抄一份，抄漏一行就测不到那一行）。
    /// 方向由攻击方的正反手镜像得出（表里这几招的 forceDir 都是 Forward）。
    /// 【defensePhase 要能传进来】：格挡与否就写在这一份表数据上，用它默认的 null 结构体就等于
    /// "对方没有在防御"，那正是本区第17区两条格挡用例要区分的东西
    /// </summary>
    private void ApplyHit(CharacterObj attacker, CharacterObj defender, AttackInfo attack,
                          BeHitBoxTurnOnInfo defensePhase = default)
    {
        CombatPipeline.ResolveHit(attacker, defender, in attack, in defensePhase, _fist, _targetBody);
    }

    /// <summary>
    /// 从"站着按 E"走到"人已经举在手里"：起手、走到窗口开着的那一拍、结算一次。
    /// 【走到第 6 拍而不是随便跑几拍】：表里 PlayerGrab 的盒子第 6 拍才开，早一拍结算就等于在测一记没命中的攻击
    /// </summary>
    private void GrabTheTarget()
    {
        BuildGround(_rig.transform.position.y);
        StartRows("PlayerIdle", "PlayerIdle", "PlayerRun", "PlayerGrab", "PlayerHold",
            "PlayerGrabPunch", "PlayerThrow", "PlayerHit");
        TickFight(KeyMap.Grab);
        Assert.AreEqual("PlayerGrab", _action.CurrentAction.id, "抓取键（104）在真表里就是起手这一行");

        FrameRange open = Info("PlayerGrab").attackPhase[0].inFrames[0];
        for (int guard = 0; guard < 12 && _action.CurrentFrame < open.min; guard++) TickFight();
        Assert.AreEqual(open.min, _action.CurrentFrame, "前提：要走到窗口开的那一拍，跑过了就得重来");
        ApplyHit(_character, _target, Info("PlayerGrab").attacks[0]);

        TickFight();
        Assert.AreEqual("PlayerHold", _action.CurrentAction.id,
            "抓到人的那一拍由 selfActionChange 把自己交给 PlayerHold——autoNextActionId 那条只在不中时走");
        Assert.AreEqual("Grabbed", _target.action.CurrentAction.id, "被举者走 byCatalog Grabbed 挑出来的那一行");
        Assert.AreEqual(_target, _character.Holding, "举人者这一头要记着手里是谁");
        Assert.AreEqual(_character, _target.HeldBy, "被举者这一头也要记着，否则他的移动拍不会让位");
    }

    /// <summary>
    /// 被举者每一拍都跟着举人者走。
    /// 【这一条正是源工程那个缺陷的反面】：BeatEmUp 在进举起状态那一刻把敌人 transform.position 摆一次，
    /// 玩家一动敌人就留在原地飘着。摆一次与每拍算一次，在这条用例里差得清清楚楚
    /// </summary>
    [Test]
    public void HeldUnitFollowsTheHolderEveryTickNotJustOnce()
    {
        BuildTarget();
        GrabTheTarget();

        for (int i = 1; i <= 6; i++)
        {
            _rig.transform.position += new Vector3(0.05f, 0f, 0.02f);
            TickFight();
            Assert.AreEqual(_rig.transform.position.x + HoldX, _target.transform.position.x, 1e-4f,
                "第" + i + "拍没跟上：举人者已经往前走了，被举者还留在上一拍的位置");
            Assert.AreEqual(_rig.transform.position.y + HoldY, _target.transform.position.y, 1e-4f,
                "第" + i + "拍高度不对：y 由 holdOffset 给，地面探测不许来抢（被举者的 Move() 整拍跳过）");
            Assert.AreEqual(_rig.transform.position.z, _target.transform.position.z, 1e-4f,
                "第" + i + "拍车道错了：同轨推挤那一拍必须把被举者排除在外，否则他每拍被推一下再被拉回来");
        }
    }

    /// <summary>
    /// 偏移的 x 随正反手镜像，z 不镜像。
    /// 【z 为什么不镜像】：z 是屏幕纵向的车道，跟脸朝哪边无关（NatureMove 里纵深那一项同理）。
    /// 写反了的后果是"朝左举人时手里的人跑到身后"，而这种错在正手时完全看不出来
    /// </summary>
    [Test]
    public void HoldOffsetMirrorsForwardAxisButNotTheLane()
    {
        BuildTarget();
        GrabTheTarget();

        _character.Inversed = true;
        _rig.transform.position = new Vector3(_rig.transform.position.x, _rig.transform.position.y, 1.2f);
        TickFight();
        Assert.AreEqual(_rig.transform.position.x - HoldX, _target.transform.position.x, 1e-4f,
            "反手时偏移的 x 要取负：手里的人始终在身前，不是身后");
        Assert.AreEqual(1.2f, _rig.transform.position.z, 1e-4f, "前提：宿主这一拍确实挪到 z=1.2 这个车道上");
        Assert.AreEqual(0f, _target.transform.position.z - _rig.transform.position.z, 1e-4f,
            "车道偏移是表里的 0，而且 z 不许被镜像——反手时把 0 变成 -0 没有差别，" +
            "但把车道的绝对位置当成偏移来镜像，手里的人就会跑到另一个车道上去");

        _character.Inversed = false;
        TickFight();
        Assert.AreEqual(_rig.transform.position.x + HoldX, _target.transform.position.x, 1e-4f,
            "转回正手又要回到身前——镜像读的是当前朝向，不是建立约束那一刻的朝向");
    }

    /// <summary>
    /// 抓取系内部换动作不解约束，而一出这个系（挨了一记）就解。
    /// 【keepHold 就是这条规则的表写法】：解不解不由代码里的 if 决定，由"新动作承不承认手里有人"决定
    /// </summary>
    [Test]
    public void GrabFamilyKeepsTheHoldButAHitDropsIt()
    {
        BuildTarget();
        GrabTheTarget();

        TickFight(KeyMap.Punch);
        Assert.AreEqual("PlayerGrabPunch", _action.CurrentAction.id,
            "举着人按拳出抓取拳：同一个键在门禁 HoldingUnit With 成立时才压得过普攻");
        Assert.AreEqual(_target, _character.Holding, "抓取拳结束回到 PlayerHold，手里的人不该掉");

        Vector3 held = _target.transform.position;
        TickFight();
        Assert.AreEqual(_rig.transform.position.x + HoldX, _target.transform.position.x, 1e-4f,
            "打人的那几拍约束照旧每拍重算（" + held + " → 现在）");

        _character.action.PreorderActionByActionChangeInfo(
            new ActionChangeInfo {changeType = ActionChangeType.ChangeToActionId, param = "PlayerHit", priority = 5},
            ForceDirection.Forward, 7);
        TickFight();
        Assert.AreEqual("PlayerHit", _action.CurrentAction.id, "前提：举人者挨了一记，动作被换到受击");
        Assert.IsNull(_character.Holding,
            "受击那一条没写 keepHold，所以约束在这一拍解除——不然会出现「人躺在地上、手里还举着敌人」这种状态");
        Assert.IsNull(_target.HeldBy, "两端要一起解，只解自己这头等于被举者还以为自己悬在半空");
    }

    /// <summary>
    /// 投掷：解约束、按抛物线飞出去、最后落回地面。
    /// 【这条同时钉住四个数】：flip 之后打的方向、Arc 的最高点、横向总位移、以及落地。
    /// 【结算仍然由 ApplyHit 手动给，没等几何相交】：宿主 prefab 的攻击盒挂在骨骼上，
    /// 而表里的轨迹是给"盒子是根节点的儿子"那套精灵 rig 用的（切片 1.7 的 BeatEmUpPlayer.prefab），
    /// 两者的局部坐标基准不同。"盒子的摆位罩得住手里的人"这件事由
    /// ThrowHitBoxIsPlacedOnTheHeldUnit 按数据验，"按 E 真的把人摔出去"在 Play Mode 用精灵 rig 端到端验
    /// </summary>
    [Test]
    public void ThrowReleasesIntoAnArcAndLandsBackOnTheGround()
    {
        BuildTarget();
        GrabTheTarget();
        float groundY = _rig.transform.position.y;

        TickFight(KeyMap.Grab);
        Assert.AreEqual("PlayerThrow", _action.CurrentAction.id, "举着人再按同一个键出投掷，不是再抓一次");
        Assert.IsTrue(_character.Inversed, "前提：表里 flip true，投掷一进去就该转到反方向");

        AttackInfo throwAttack = Info("PlayerThrow").attacks[0];
        FrameRange open = Info("PlayerThrow").attackPhase[0].inFrames[0];
        for (int guard = 0; guard < 12 && _action.CurrentFrame < open.min; guard++) TickFight();
        Assert.AreEqual(open.min, _action.CurrentFrame, "前提：走到投掷那记盒子开着的第" + open.min + "拍");
        ApplyHit(_character, _target, throwAttack);

        TickFight();
        Assert.AreEqual("Beaten", _target.action.CurrentAction.catalog,
            "被摔出去走的是 Beaten 那一类（表里投掷那记的 targetActionChange）");
        Assert.IsNull(_target.HeldBy, "被举者一换动作，约束那一拍就该发现他不再是 Grabbed 了");

        float releaseY = _target.transform.position.y;
        float startX = _target.transform.position.x;
        float highest = releaseY;
        float furthestX = startX;
        for (int i = 0; i < 140; i++)
        {
            TickFight();
            Assert.IsNull(_target.HeldBy, "第" + (i + 1) + "拍又被人举起来了：解除必须是永久的");
            highest = Mathf.Max(highest, _target.transform.position.y);
            furthestX = Mathf.Min(furthestX, _target.transform.position.x);   //反手投掷，往 -x 飞
        }

        Assert.AreEqual(releaseY + 1.2f, highest, 0.1f,
            "最高点就是表里 pushPower 的 y（Arc 的 4h·t(1−t) 在 t=0.5 取到 h）");
        Assert.AreEqual(-2.5f, furthestX - startX, 0.15f,
            "横向总位移就是表里的 2.5 米，方向是转到反方向之后的身前");
        Assert.AreEqual(groundY, _target.transform.position.y, 1e-3f,
            "抛物线走完就交还给重力，最后必须贴回地面，不然人被扔进地里或者停在半空");
    }

    /// <summary>
    /// holdFrames 走完就撒手，哪怕举人者那条动作还没结束。
    /// 【这条测的是 SetHoldConstraint 这个原语】：表里的 180 拍要跑到，用例不该等 3 秒
    /// </summary>
    [Test]
    public void HoldExpiresAfterTheTableSaidSo()
    {
        BuildTarget();
        StartRows("PlayerIdle", "PlayerIdle", "PlayerHold");
        _character.SetHoldConstraint(_target, new Vector3(HoldX, HoldY, 0f), 4);

        for (int i = 1; i <= 3; i++)
        {
            TickFight();
            Assert.IsNotNull(_character.Holding, "第" + i + "拍还没到 4 拍，不该撒手");
        }

        TickFight();
        Assert.IsNull(_character.Holding, "第 4 拍走完 holdFrames，两边都得放下");
        Assert.IsNull(_target.HeldBy);
    }

    /// <summary>
    /// 投掷那记合成盒的摆位，要正好罩住被 holdOffset 举着的人。
    /// 【这条是数据耦合的闸门】：两处分别是"手在哪"和"盒子打到哪"，改一处不改另一处，
    /// 投掷就会静默打空（人举在手里不放也摔不出去），而这种错在 Play Mode 里只表现为"按了没反应"。
    /// 中心高度按靶子 prefab 上那只受击盒的 center.y 算，不假设 0.6
    /// </summary>
    [Test]
    public void ThrowHitBoxIsPlacedOnTheHeldUnit()
    {
        BuildTarget();
        Vector3 hold = Info("PlayerGrab").attacks[0].holdOffset;
        Assert.AreEqual(180, Info("PlayerGrab").attacks[0].holdFrames, "前提：起手那一记才是要抓人的");
        Assert.AreEqual(0, Info("PlayerThrow").attacks[0].holdFrames, "投掷自己不重新抓人");

        CapsuleCollider body = _targetBody.GetComponent<CapsuleCollider>();
        Assert.IsNotNull(body, "前提：靶子的受击盒是胶囊，中心高度从这里读");
        Vector3 victimCenter = new Vector3(hold.x, hold.y + body.center.y, hold.z);

        AttackBoxKey pose = Info("PlayerThrow").attackPhase[0].track[0];
        Assert.Less(Mathf.Abs(pose.position.x - victimCenter.x), pose.size.x * 0.5f,
            "盒子横向罩不住手里的人：投掷会打空");
        Assert.Less(Mathf.Abs(pose.position.y - victimCenter.y), pose.size.y * 0.5f,
            "盒子纵向罩不住手里的人：被举者的中心在 holdOffset.y 之上再加受击盒自己的 center.y");
        Assert.Less(Mathf.Abs(pose.position.z - victimCenter.z), pose.size.z * 0.5f,
            "盒子的车道厚度不够：0.5 米以内必须包住，不然人稍微错开一个车道就摔不出去");
    }

    /// <summary>
    /// Arc 这一条曲线的形状：横向匀着走、纵向是个对称抛物线，两头都回到起跳高度。
    /// 【直接问函数表而不是问角色】：它是个纯函数，把"最高点恰好在表里的 y"这件事和角色系统分开钉
    /// </summary>
    [Test]
    public void ArcTweenGoesUpAndBackDownWithinTheFramesListed()
    {
        ForceMove move = ForceMove.FromData(new MoveInfo
        {
            inFrames = 20,
            moveDistance = new Vector3(2f, 1f, 0f),
            tweenMethod = "Arc",
        });

        float x = 0f, y = 0f, highest = 0f;
        //多走一拍：MoveTween 是先取本拍再 Advance，所以走完 inFrames 拍时进度只到 (inFrames-1)/inFrames，
        //最后那一小段要等下一拍才落地——真实移动拍也是这个节奏，用例就照它走 21 拍
        for (int i = 0; i <= 20; i++)
        {
            Vector3 step = move.MoveTween(move);
            x += step.x;
            y += step.y;
            highest = Mathf.Max(highest, y);
            move.Advance();
        }

        Assert.AreEqual(2f, x, 1e-4f, "横向走满就是表里的 moveDistance.x");
        Assert.AreEqual(0f, y, 1e-4f, "抛物线两头都在同一个高度：走完这一程不能把人留在半空");
        Assert.AreEqual(1f, highest, 0.06f, "最高点就是表里的 y（4h·t(1−t) 在 t=0.5 取到 h）");
    }

    //==================================== 17. 伤害、霸体与格挡结算（切片 1.9） ====================================
    //
    // 这一区打的是真入口 `CombatPipeline.Settle` / `ResolveHit`（1.9 之前这段长在 GameMain.DoAttack 里，
    // 用例碰不到，也就没人钉）。【为什么非要编辑态钉】：这些规则全在"打中人"那一拍里，
    // 而肉眼能看见的只有血条少了一格——"霸体没破却不该切受击动作""挡住的一记不掉血但罚六拍"
    // "打死那一记要路由到他自己会的那一行 Death"，这三件事在 Play Mode 里全都长得一样。

    /// <summary>属性组件挂上以后要重新认一次：BindBoxes 里读组件那几行在 early-return 之前，现挂现认。
    /// 【先摘掉 prefab 上可能已有的那一个】：GetComponent 只给第一个，留着它的话这里改的是另一份</summary>
    private CombatStats GiveStats(CharacterObj c, float maxHp, float attack, float defense)
    {
        if (c.Stats) Object.DestroyImmediate(c.Stats);
        //prefab 现在真的带着霸体条（1.9 之后落盘了），而这一族用例里"多少攻多少血"必须是从零说起的，
        //顺手把条也摘掉：要霸体的用例接着用 GivePoise 装回去
        if (c.Poise) Object.DestroyImmediate(c.Poise);
        CombatStats stats = c.gameObject.AddComponent<CombatStats>();
        stats.maxHp = maxHp;
        stats.attack = attack;
        stats.defense = defense;
        c.BindBoxes();
        Assert.AreEqual(maxHp, c.Stats.Hp, 1e-4f, "前提：BindBoxes 认到组件就得把血填满，不然第一记就判定成已经死了");
        return stats;
    }

    private PoiseGauge GivePoise(CharacterObj c, float maxPoise, int delayFrames, float perFrame)
    {
        if (c.Poise) Object.DestroyImmediate(c.Poise);
        PoiseGauge gauge = c.gameObject.AddComponent<PoiseGauge>();
        gauge.maxPoise = maxPoise;
        gauge.recoveryDelayFrames = delayFrames;
        gauge.recoveryPerFrame = perFrame;
        c.BindBoxes();
        return gauge;
    }

    /// <summary>把两个倍率钉成 1：公式那条用例自己改它们，其余用例不许被 prefab 上的 Inspector 值牵动</summary>
    private void FlatBoxes()
    {
        _fist.damageRate = 1f;
        _targetBody.meat = 1f;
    }

    /// <summary>只给靶子他自己会的那几行，起点是 startId</summary>
    private void StartTargetRows(string startId, params string[] ids)
    {
        _target.action.SetAllActions(GameData.ActionsFor(ids), startId);
    }

    /// <summary>
    /// 让攻击方正处在"持有这一记攻击的那一行"上。
    /// 【为什么必须】：EditMode 不跑 Awake，`SetAllActions` 没人叫的话 `CurrentAction` 就是一个默认结构体，
    /// 它的 `tempBeCancelledTag` 是 null，于是命中结算里"开启临时取消标签"那一段会炸在
    /// `AddTempBeCancelledTag` 里——那正是运行期不可能出现的状态（角色总得先有个动作才谈得上出招）
    /// </summary>
    private void AttackerIn(string id)
    {
        _action.SetAllActions(GameData.ActionsFor(FamilyOf(id)), id);
    }

    /// <summary>
    /// 摆成正对：靶子在 x=0 面向 +x，攻击者在它面前 0.5 米、面向 -x。
    /// 【为什么用例要自己摆】：格挡判的是"打没打在正脸上"，那是位置关系，而 prefab 里两个角色是重叠的
    /// </summary>
    private void PlaceFaceToFace()
    {
        _target.Inversed = false;
        _character.Inversed = true;
        _target.transform.position = new Vector3(0f, _target.transform.position.y, 0f);
        _character.transform.position = new Vector3(0.5f, _character.transform.position.y, 0f);
    }

    /// <summary>把攻击者挪到靶子背后（靶子的面向不变，还是朝着 +x）</summary>
    private void PlaceBehindTheTarget()
    {
        PlaceFaceToFace();
        _character.transform.position = new Vector3(-0.5f, _character.transform.position.y, 0f);
    }

    /// <summary>
    /// 伤害 = 攻击力 × 表里的 attack × 攻击盒 damageRate × 受击盒 meat − 防御。
    /// 【四个乘数一个都不许少】：attack 和 meat 这两个字段从项目建起来就躺在表与盒子上，
    /// 一直到 1.9 才有消费者；它们一旦被漏掉，"打腿比打头轻"这种设计就只是注释里的愿望
    /// </summary>
    [Test]
    public void DamageMultipliesAttackerPowerBoxRateMeatMinusDefense()
    {
        BuildTarget();
        GiveStats(_character, 100f, 10f, 0f);
        GiveStats(_target, 100f, 5f, 3f);
        _fist.damageRate = 2f;
        _targetBody.meat = 0.5f;

        AttackInfo attack = Info("PlayerPunch1").attacks[0];        // 表里 attack = 1.0
        BeHitBoxTurnOnInfo defensePhase = Info("BoxingStand").defensePhase[0];
        DamagePayload p = CombatPipeline.Settle(_character, _target, in attack, in defensePhase, _fist, _targetBody);

        Assert.AreEqual(7f, p.Damage, 1e-4f, "10 攻 × 1.0 倍率 × 2 盒倍率 × 0.5 肉质 − 3 防 = 7");
        Assert.AreEqual(93f, _target.Stats.Hp, 1e-4f, "Settle 是真的把血扣掉了，不是只算一个数给调用方看");
        Assert.IsFalse(p.Blocked, "BoxingStand 的受击阶段没写 block");
        Assert.IsTrue(p.Interrupts, "没挂霸体条就是没有霸体，每一记都打断");
        Assert.IsFalse(p.Lethal);

        //防御高过伤害时夹在 0：不掉血，但硬直与记录照旧（那是策划的数值问题，框架不替他猜"至少 1 点"）
        _target.Stats.defense = 999f;
        DamagePayload chipped = CombatPipeline.Settle(_character, _target, in attack, in defensePhase, _fist, _targetBody);
        Assert.AreEqual(0f, chipped.Damage, 1e-4f, "减完是负数就夹在 0");
        Assert.AreEqual(93f, _target.Stats.Hp, 1e-4f, "夹到 0 之后一滴血都没掉");
        Assert.IsTrue(chipped.Interrupts, "打不动也得打断他——这是硬直，不是伤害");
    }

    /// <summary>
    /// 韧性没被打空：掉血，但不换动作、不硬直、不推开。
    /// 【这一条就是"霸体硬抗"的全部含义】：refactor.md Phase 3 的第1点，以前框架里没有这个概念
    /// </summary>
    [Test]
    public void UnbrokenPoiseSoaksTheBlowWithoutTouchingTheActionOrPushing()
    {
        BuildTarget();
        BuildGround(_targetRig.transform.position.y);
        FlatBoxes();
        GiveStats(_character, 100f, 10f, 0f);
        GiveStats(_target, 100f, 0f, 0f);
        GivePoise(_target, 20f, 60, 0.5f);
        AttackerIn("PlayerPunch1");
        StartTargetRows("PlayerIdle", FamilyOf("PlayerIdle"));
        PlaceFaceToFace();

        AttackInfo attack = Info("PlayerPunch1").attacks[0];        // 表里 poiseDamage = 8
        ApplyHit(_character, _target, attack);

        Assert.AreEqual(90f, _target.Stats.Hp, 1e-4f, "血照掉（10 攻 × 1.0，防御 0）");
        Assert.AreEqual(12f, _target.Poise.Poise, 1e-4f, "韧性被真的扣掉 8 点");
        Assert.IsFalse(_target.action.Freezing, "硬抗不该有任何硬直：他这一拍本该继续做自己的事");

        float x = _target.transform.position.x;
        for (int i = 0; i < 8; i++) TickFight();
        Assert.AreEqual("PlayerIdle", _target.action.CurrentAction.id,
            "韧性没破就不该切受击动作——切了就等于霸体条只是条装饰");
        Assert.AreEqual(x, _target.transform.position.x, 1e-4f, "硬抗也不该被推开");
    }

    /// <summary>
    /// 正面格挡：不掉血，但罚 blockStunFrames 拍、推开 blockPush 那段距离，动作一动不动。
    /// 【源工程没有这一段】：BeatEmUp 的格挡是全额免伤且挡完立刻能动，白挡；
    /// 这两格新字段就是给"挡住也要付出代价"留的口子
    /// </summary>
    [Test]
    public void BlockedBlowTakesNoDamageButPaysBlockStunAndBlockPush()
    {
        BuildTarget();
        BuildGround(_targetRig.transform.position.y);
        GiveStats(_target, 100f, 0f, 0f);
        AttackerIn("Punch1");
        StartTargetRows("PlayerDefend", FamilyOf("PlayerDefend"));
        PlaceFaceToFace();

        AttackInfo attack = Info("Punch1").attacks[0];              // blockStun 6、blockPush 0.4 米 / 6 拍
        BeHitBoxTurnOnInfo defendPhase = Info("PlayerDefend").defensePhase[0];
        Assert.IsTrue(defendPhase.block, "前提：1.9 给 PlayerDefend 的受击阶段挂上了 block");

        DamagePayload p = CombatPipeline.Settle(_character, _target, in attack, in defendPhase, _fist, _targetBody);
        Assert.IsTrue(p.Blocked, "正脸挨的这一记该被挡住");
        Assert.AreEqual(0f, p.Damage, 1e-4f, "格挡全额免伤");
        Assert.IsFalse(p.Interrupts, "挡住不是被打断：防御姿势不许被换掉");
        Assert.AreEqual(6, p.StunFrames, "格挡硬直来自新字段 blockStunFrames，不是 hitStunFrames(7)");
        Assert.AreEqual(0.4f, p.Push.moveDistance.x, 1e-4f, "推开来自新字段 blockPush，不是 pushPower(0.1)");

        ApplyHit(_character, _target, attack, defendPhase);
        Assert.AreEqual(100f, _target.Stats.Hp, 1e-4f, "结算跑完也一滴血不掉");
        Assert.IsTrue(_target.action.Freezing, "挡住当拍就该被冻住");
        int frame = _target.action.CurrentFrame;
        for (int i = 0; i < 5; i++) TickFight();
        Assert.AreEqual(frame, _target.action.CurrentFrame,
            "blockStun 的这 5 拍里动作时钟一步都不许走，否则罚的就是零拍");
        Assert.AreEqual("PlayerDefend", _target.action.CurrentAction.id, "挡住之后他还站在防御姿势里");
        float x = _target.transform.position.x;
        for (int i = 0; i < 15; i++) TickFight();
        Assert.Less(_target.transform.position.x, x - 0.3f,
            "罚完之后 blockPush 要真的把他推开（攻击者在正前方，所以推的是 -x）");
    }

    /// <summary>
    /// 打在背后的那一记不算挡住：伤害照掉，而且受击动作照常切。
    /// 【顺带钉住分类挑选的兜底】：靶子这份子集里 Hurt 分类只有 PlayerHit 一行，
    /// 而挑选函数过去只认 demo 那两条 id（HurtFromForward/Backward），认不出就返回一个空 ActionInfo，
    /// 于是移植家的角色挨打一动不动——这条断言现在才拦得住那种错
    /// </summary>
    [Test]
    public void BlowFromBehindIsNotBlockedAndStillSwitchesToTheHurtRow()
    {
        BuildTarget();
        BuildGround(_targetRig.transform.position.y);
        FlatBoxes();
        GiveStats(_target, 100f, 0f, 0f);
        AttackerIn("Punch1");
        StartTargetRows("PlayerDefend", FamilyOf("PlayerDefend"));
        PlaceBehindTheTarget();

        AttackInfo attack = Info("Punch1").attacks[0];
        BeHitBoxTurnOnInfo defendPhase = Info("PlayerDefend").defensePhase[0];
        DamagePayload p = CombatPipeline.Settle(_character, _target, in attack, in defendPhase, _fist, _targetBody);
        Assert.IsFalse(p.Blocked, "背后挨刀不该算格挡");
        Assert.Greater(p.Damage, 0f, "没挡住就要掉血");
        Assert.AreEqual(attack.hitStunFrames, p.StunFrames, "没挡住就回到 hitStunFrames");

        ApplyHit(_character, _target, attack, defendPhase);
        TickFight();
        Assert.AreEqual("PlayerHit", _target.action.CurrentAction.id,
            "byCatalog Hurt 在这个角色自己的子集里就该落到他唯一的那一行受击");
    }

    /// <summary>
    /// 打空了血就路由到 Death，而死人不再挨第二下。
    /// 【Death 分类挑的是这个人自己会的那一行】：移植家的是 PlayerDeath，demo 家压根没有 Death 行，
    /// 于是那边预约为空、什么也不发生——这正是"全角色共用一张表、按角色分子集"想要的效果
    /// </summary>
    [Test]
    public void LethalBlowRoutesToDeathAndTheCorpseIsNotAttackedAgain()
    {
        BuildTarget();
        BuildGround(_targetRig.transform.position.y);
        FlatBoxes();
        GiveStats(_character, 100f, 10f, 0f);
        GiveStats(_target, 15f, 0f, 0f);
        AttackerIn("PlayerPunch1");
        StartTargetRows("PlayerIdle", FamilyOf("PlayerIdle"));
        PlaceFaceToFace();

        //两记才打死：第一记只掉血（15 → 5），第二记才是致死那一记。
        //【为什么不是一次结算就打死】：`Settle` 是真的扣血而不是问一问，所以"先问一次结论、再打一遍真的"
        //这种写法会把同一记结算跑两遍——第一遍就已经把人打死了，第二遍读到的是"本来就死了"，
        //于是 Lethal 变成 false、路由到 Death 那一条分支根本不会走。这条用例第一次就是这么红的
        AttackInfo attack = Info("PlayerPunch1").attacks[0];        // 10 点伤害
        BeHitBoxTurnOnInfo defensePhase = Info("PlayerIdle").defensePhase[0];
        DamagePayload first = CombatPipeline.Settle(_character, _target, in attack, in defensePhase, _fist, _targetBody);
        Assert.IsFalse(first.Lethal, "15 血挨 10 点不该就算致死");
        Assert.AreEqual(5f, _target.Stats.Hp, 1e-4f);

        ApplyHit(_character, _target, attack);
        Assert.AreEqual(0f, _target.Stats.Hp, 1e-4f, "血量夹在 0，不许出现 -5");
        TickFight();
        Assert.AreEqual("PlayerDeath", _target.action.CurrentAction.id, "致死那一记路由到 Death 分类");

        Assert.IsTrue(_target.IsDead);
        Assert.IsFalse(_character.CanAttackTargetNow(_target, out AttackInfo deadPhase, out BeHitBoxTurnOnInfo deadDefense,
                out AttackHitBox deadBox, out BeHitBox deadTargetBox),
            "死人不再挨第二下：少了这一条，一记多段的大招会在好几拍里反复结算同一个人，" +
            "断气之后还会再被路由一次 Death");
    }

    /// <summary>
    /// 没挂 CombatStats 的角色：一滴血不掉，但打断、硬直、推开与 1.9 之前一字不差。
    /// 【这条是兼容性的闸门】：场上多的是灰盒靶子和不需要数值的角色，
    /// 数值结算必须是可选件，而且"可选"的另一面是"没装就等于老行为"
    /// </summary>
    [Test]
    public void CharacterWithoutStatsKeepsThePreDamageBehaviour()
    {
        BuildTarget();
        //prefab 上以后可能会挂上属性组件（Play Mode 要验伤害就得挂），这条用例要的是"没装"那种角色
        if (_target.Stats) Object.DestroyImmediate(_target.Stats);
        if (_target.Poise) Object.DestroyImmediate(_target.Poise);
        _target.BindBoxes();
        Assert.IsNull(_target.Stats, "前提：摘掉之后 BindBoxes 要重新认一次，角色这一头不能留着旧的引用");

        AttackInfo attack = Info("PlayerPunch1").attacks[0];
        BeHitBoxTurnOnInfo defensePhase = Info("BoxingStand").defensePhase[0];
        DamagePayload p = CombatPipeline.Settle(_character, _target, in attack, in defensePhase, _fist, _targetBody);

        Assert.AreEqual(0f, p.Damage);
        Assert.IsFalse(p.Blocked);
        Assert.IsTrue(p.Interrupts, "没装组件不是有霸体，是压根不参与结算");
        Assert.AreEqual(attack.hitStunFrames, p.StunFrames);
        Assert.AreEqual(attack.pushPower.moveDistance.x, p.Push.moveDistance.x, 1e-4f);
        Assert.AreEqual(attack.pushPower.inFrames, p.Push.inFrames);
    }

    /// <summary>
    /// 韧性被打空的那一记才算打断：前一记硬抗、后一记崩，两记之间只有韧性条在动。
    /// 【为什么单独一条】：`UnbrokenPoise...` 钉的是"没破不该断"，这一条钉的是"破了就该断"，
    /// 少任何一半都可以写成"永远不断"或"永远断"那种实现。
    /// 【两记都直接走 ApplyHit】：真游戏里同一个人同一记攻击有 `CanHitTargetAgain` 挡着（60 拍冷却），
    /// 这里要测的是"同一份表数据连打两下"，所以绕开那一道门，不影响结论
    /// </summary>
    [Test]
    public void PoiseBreaksOnTheBlowThatRunsItOutAndRestoresTheStagger()
    {
        BuildTarget();
        BuildGround(_targetRig.transform.position.y);
        FlatBoxes();
        GiveStats(_character, 100f, 10f, 0f);
        GiveStats(_target, 100f, 0f, 0f);
        GivePoise(_target, 10f, 6000, 1f);       // 恢复期给得很长，本条用例里不许它偷偷回
        AttackerIn("PlayerPunch1");
        StartTargetRows("PlayerIdle", FamilyOf("PlayerIdle"));
        PlaceFaceToFace();

        AttackInfo attack = Info("PlayerPunch1").attacks[0];      // poiseDamage 8、hitStun 7、push 0.35
        ApplyHit(_character, _target, attack);
        Assert.AreEqual(2f, _target.Poise.Poise, 1e-4f, "10 点韧性挨 8 点，剩 2 点：还没破");
        Assert.AreEqual(90f, _target.Stats.Hp, 1e-4f, "血照掉");
        float xAfterFirst = _target.transform.position.x;
        for (int i = 0; i < 4; i++) TickFight();
        Assert.AreEqual("PlayerIdle", _target.action.CurrentAction.id, "第一记没破霸体，不该切受击");

        ApplyHit(_character, _target, attack);
        Assert.AreEqual(0f, _target.Poise.Poise, 1e-4f, "第二记的 8 点大于剩下的 2 点：清零并打崩");
        Assert.AreEqual(80f, _target.Stats.Hp, 1e-4f);
        TickFight();
        Assert.AreEqual("PlayerHit", _target.action.CurrentAction.id, "崩防的那一记该照常切受击动作");
        for (int i = 0; i < 12; i++) TickFight();
        Assert.Less(_target.transform.position.x, xAfterFirst - 0.2f,
            "崩防之后击退也要照常回来（硬抗那几拍是一毫米都不推的）");
    }

    /// <summary>
    /// 韧性只在"隔了 recoveryDelayFrames 之后"才开始回，一回就是一拍一点，回到上限就停住。
    /// 【直接问组件而不是问角色】：恢复节奏是个纯计数，跟动作、地面、朝向都没关系，
    /// 扯进角色链里就看不出到底是哪一格错了
    /// </summary>
    [Test]
    public void PoiseRefillsOnlyAfterTheDelayAndThenOneStepPerFrame()
    {
        _poiseRig = new GameObject("ACT-Test-Poise");
        _poiseRig.hideFlags = HideFlags.DontSaveInEditor | HideFlags.HideInHierarchy;
        PoiseGauge gauge = _poiseRig.AddComponent<PoiseGauge>();
        gauge.maxPoise = 20f;
        gauge.recoveryDelayFrames = 10;
        gauge.recoveryPerFrame = 1f;
        gauge.Refill();

        Assert.IsFalse(gauge.Consume(8f), "20 点韧性挨 8 点不该被打断");
        Assert.AreEqual(12f, gauge.Poise, 1e-4f);

        for (int i = 0; i < 9; i++)
        {
            LogicClock.Advance();
            gauge.Tick();
        }
        Assert.AreEqual(12f, gauge.Poise, 1e-4f, "恢复期没到就一点都不回：边挨打边回满等于没有霸体");

        LogicClock.Advance();
        gauge.Tick();
        Assert.AreEqual(13f, gauge.Poise, 1e-4f, "隔满 recoveryDelayFrames 的那一拍开始回");

        for (int i = 0; i < 30; i++)
        {
            LogicClock.Advance();
            gauge.Tick();
        }
        Assert.AreEqual(20f, gauge.Poise, 1e-4f, "回到上限就停住，不许溢出");

        Assert.IsTrue(gauge.Consume(0f), "poiseDamage 留空(0)的攻击一律打断——缺省必须站在老行为那一边");
        Assert.AreEqual(20f, gauge.Poise, 1e-4f, "而那一记其实一点韧性都没扣");
    }

    //================================ 第18区：帧事件的接收者（切片 2.1） ====================================
    //表里那 23 条 cue 从 1.5 起就按拍求值了，却一直没有听众。这一区钉的就是那个听众：
    //名字必须对得上素材、烟摆在该摆的地方、多少拍收回、池子只会转不会长、震手十八拍归位。
    //【为什么不测"真的响了"】：AudioSource.PlayOneShot 在编辑器里放不出声音也查不到状态，
    //能查的是"载到了没有"和"没载到会不会炸"，而 0 分配那一条由 Play Mode 的 PoolTick/CueFire 分区钉

    private GameObject _receiverGo;

    private CueReceiver MakeReceiver()
    {
        _receiverGo = new GameObject("ACT-Test-Receiver");
        _receiverGo.hideFlags = HideFlags.DontSaveInEditor | HideFlags.HideInHierarchy;
        //预热要扫全表，所以 GameData.Load 必须已经跑过（SetUp 负责）。
        //初始化是显式叫的：EditMode 里 AddComponent 不触发 Awake，本工程的角色向来如此（BindBoxes 同理）
        CueReceiver r = _receiverGo.AddComponent<CueReceiver>();
        r.Init();
        return r;
    }

    private static ActionCue Cue(CueKind kind, string param)
    {
        ActionCue c = new ActionCue();
        c.kind = kind;
        c.param = param;
        return c;
    }

    /// <summary>
    /// 表 vs 素材库的一致性：每一条 cue 要的名字都得真能拿到东西。
    /// 这是"真源在表"之后必须补上的那半条断言——表里写了个库里没有的名字，运行期只是安静跳过，
    /// 靠这一条在开机前就红，而不是等验收时"怎么没有挥空声"
    /// </summary>
    [Test]
    public void EveryCueNameInTheTableHasItsAsset()
    {
        CueReceiver r = MakeReceiver();
        foreach (ActionInfo info in GameData.AllActions())
        {
            if (info.cues == null) continue;
            foreach (ActionCue c in info.cues)
            {
                if (c.kind == CueKind.Sfx)
                    Assert.IsTrue(r.HasClip(c.param), info.id + " 的音效 「" + c.param + "」载不到");
                else if (c.kind == CueKind.Effect)
                    Assert.Greater(r.PoolCapacity(c.param), 0, info.id + " 的特效 「" + c.param + "」没建出池");
            }
        }
    }

    [Test]
    public void PuffIsPlacedAtTheOwnerAndTakenBackWhenItsFramesAreUp()
    {
        CueReceiver r = MakeReceiver();
        Vector3 at = new Vector3(3.25f, 0.4f, -1.5f);
        _character.transform.position = at;

        r.OnCue(_character, new ActionInfo(), Cue(CueKind.Effect, "SmokePuffLand"));

        Assert.AreEqual(1, r.ActiveEffectCount("SmokePuffLand"), "响一次就该有一团烟亮起来");
        Vector3 puff = r.PooledInstance("SmokePuffLand", 0).transform.position;
        Assert.AreEqual(at.x, puff.x, 1e-4f, "烟要摆在放它的那个人脚下，不是世界原点");
        Assert.AreEqual(at.y, puff.y, 1e-4f);
        Assert.AreEqual(at.z, puff.z, 1e-4f, "纵深也要一起，否则近车道的烟会画到远车道上");

        int life = r.EffectLifeFrames("SmokePuffLand");
        Assert.Greater(life, 0, "寿命是从片子自己的粒子时长换算来的，不许是 0");

        for (int i = 0; i < life - 1; i++)
        {
            LogicClock.Advance();
            r.Tick();
        }
        Assert.AreEqual(1, r.ActiveEffectCount("SmokePuffLand"), "还没到点就收回去，烟会短一截");

        LogicClock.Advance();
        r.Tick();
        Assert.AreEqual(0, r.ActiveEffectCount("SmokePuffLand"), "到点那一拍正好关掉");
    }

    /// <summary>
    /// 池子只会转不会长：超过容量就回头复用最早那一槽（把它没放完的烟冲掉）。
    /// 这条钉的是"绝不 Instantiate"——Instantiate 一次就是热路径上的一次分配
    /// </summary>
    [Test]
    public void PoolRecyclesInsteadOfGrowing()
    {
        CueReceiver r = MakeReceiver();
        int capacity = r.PoolCapacity("SmokePuffLand");
        Assert.AreEqual(CueReceiver.EffectCapacity, capacity);

        for (int i = 0; i < capacity + 1; i++)
        {
            _character.transform.position = new Vector3(i, 0f, 0f);
            r.OnCue(_character, new ActionInfo(), Cue(CueKind.Effect, "SmokePuffLand"));
        }

        Assert.AreEqual(capacity, r.ActiveEffectCount("SmokePuffLand"), "响 7 次也只亮 6 团：多的那次是复用");
        //第 7 次（x = 6）回绕用到第 0 槽：容量 6 个槽，前面 6 次刚好把 0..5 用光
        Assert.AreEqual(capacity, r.PooledInstance("SmokePuffLand", 0).transform.position.x, 1e-4f,
            "回绕到第 0 槽，并且把第七次的位置写了进去");
        //池里除了这一名烟还有别的名（预热按表里出现的名字逐个建），所以这里比的是"从头到尾没长过"
        Assert.AreEqual(12, _receiverGo.transform.childCount, "响 7 次也没有多 Instantiate 一个：实例数与预热时一样");
    }

    [Test]
    public void CameraShakeDecaysOverEighteenLogicFrames()
    {
        CueReceiver r = MakeReceiver();
        Assert.AreEqual(0f, r.ShakeOffsetY, "没震就是 0");

        r.OnCue(_character, new ActionInfo(), Cue(CueKind.CameraShake, ""));

        LogicClock.Advance();
        r.Tick();
        float first = r.ShakeOffsetY;
        Assert.AreNotEqual(0f, first, "震的那一拍就该有偏移");
        Assert.LessOrEqual(Mathf.Abs(first), CueReceiver.ShakeIntensity + 1e-4f, "幅度不许超过标定值");

        LogicClock.Advance();
        r.Tick();
        Assert.AreNotEqual(first, r.ShakeOffsetY, "左右交替，不是往一边歪着不放");

        for (int i = 0; i < CueReceiver.ShakeFrames; i++)
        {
            LogicClock.Advance();
            r.Tick();
        }
        Assert.AreEqual(0f, r.ShakeOffsetY, "十八拍之后相机回到标定高度");
    }

    /// <summary>
    /// 名字对不上（库里没有这个特效、表里写了个没导入的音效）时安静跳过：
    /// 不抛、不放、也不留 Error——策划在表里多填一条没人听的 cue 不该把测试跑刷成一堆红
    /// </summary>
    [Test]
    public void UnknownCueNameIsSkippedSilently()
    {
        CueReceiver r = MakeReceiver();
        r.OnCue(_character, new ActionInfo(), Cue(CueKind.Effect, "NoSuchPuff"));
        r.OnCue(_character, new ActionInfo(), Cue(CueKind.Sfx, "NoSuchSfx"));
        r.OnCue(_character, new ActionInfo(), Cue(CueKind.Force, ""));

        Assert.AreEqual(0, r.ActiveEffectCount("NoSuchPuff"), "没有池就什么都不放，而不是放一个空的");
        Assert.IsFalse(r.HasClip("NoSuchSfx"));
    }

    //================================ 第19区：敌兵的决策（切片 2.2） ====================================
    //AI 只干一件事：往同一个 `_input` 里登记按键。所以这一区断言的全是"他按了什么、什么时候按"，
    //而"按下之后出不出招"仍然是表说了算——那部分是第7到第10区的事，这里不重复钉。
    //角色也照生产那样只拿自己那一份子集（`GiveActionsTo`），不然全表在场时 [Forward] 会派生出别家的行

    private EnemyMind MakeMind(CharacterObj who, CharacterObj whom)
    {
        //敌兵用他自己那一家：站立 / 前进 / 后退 / 拳 / 脚 / 转身，跟 demo 场景里的那两只灰盒一致
        string[] subset = who.actionSubset;
        who.action.SetAllActions(GameData.ActionsFor(subset), "BoxingStand");
        EnemyMind m = who.gameObject.AddComponent<EnemyMind>();
        m.self = who;
        m.target = whom;
        m.delayBeforeStartFrames = 0;
        //概率这一档在这里定成 100%，因为要测的是"决定出手之后隔几拍按下去"；
        //概率本身由下面配额那两条用例钉
        m.attackChanceWhenIdle = 100;
        m.attackChanceWhenSomeBusy = 100;
        return m;
    }

    /// <summary>
    /// 一拍：玩家输入拍 → AI 拍 → 两个角色的动作拍。移动拍不跑，这一区断言的是按键与派生。
    /// `busy` 就是 GameMain 那一拍算出来再传进来的"已经有几只扑上去了"，
    /// 配额是调用方数的（`EnemyMind` 的类注释里写了为什么），所以用例可以直接给它一个数
    /// </summary>
    private void TickMind(EnemyMind m, int busy, params KeyMap[] playerKeys)
    {
        LogicClock.Advance();
        _input.Tick();
        m.self.input.Tick();
        for (int i = 0; i < playerKeys.Length; i++) _input.AddInput(playerKeys[i]);
        m.Tick(busy);
        _action.Tick();
        m.self.action.Tick();
    }

    [Test]
    public void MindClosesTheGapByPressingForward()
    {
        BuildTarget();
        EnemyMind m = MakeMind(_target, _character);
        m.self.transform.position = new Vector3(0f, 0f, 0f);
        _character.transform.position = new Vector3(3f, 0f, 0f);

        TickMind(m, 0);

        Assert.IsFalse(m.self.Inversed, "目标在 +x 一侧，面向就不该反着");
        Assert.AreEqual("BoxingMoveForward", m.self.action.CurrentAction.id, "够不着就往前走，这是表里那条前进行");
    }

    [Test]
    public void MindTurnsToFaceATargetBehindItAndStillWalksIn()
    {
        BuildTarget();
        EnemyMind m = MakeMind(_target, _character);
        m.self.transform.position = new Vector3(3f, 0f, 0f);
        _character.transform.position = new Vector3(0f, 0f, 0f);

        TickMind(m, 0);

        Assert.IsTrue(m.self.Inversed, "目标在 -x 一侧，TurnTo 要把面向翻过去");
        Assert.AreEqual("BoxingMoveForward", m.self.action.CurrentAction.id,
            "转完身之后按的仍然是前进：AI 要的是「走向他」，不是「背对着他退」");
    }

    [Test]
    public void MindMatchesTheLaneBeforeCommitting()
    {
        BuildTarget();
        EnemyMind m = MakeMind(_target, _character);
        m.self.transform.position = new Vector3(0.8f, 0f, -1.2f);
        _character.transform.position = new Vector3(0f, 0f, 0f);

        TickMind(m, 0);

        Assert.AreEqual("BoxingStand", m.self.action.CurrentAction.id,
            "纵深没对上之前不横着凑距离——横向已经在射程里，只欠车道");
        //敌兵在 z=-1.2、玩家在 z=0：玩家在他 +z 那一侧（远离镜头），所以按 Up
        Assert.IsTrue(m.self.input.IsKeyHeld(KeyMap.Up), "目标在远车道就按 Up（+z = 远离镜头，见 KeyMap.Up 那条注释）");
    }

    /// <summary>
    /// 前摇 = 表上那个 `telegraphFrames`。源工程是站到理想位置之后停 .3 秒再出手，
    /// 这 .3 秒就是"看得见但来得及躲"的那一段，拍数错了手感就整个变
    /// </summary>
    [Test]
    public void TelegraphHoldsTheKeyForTheTableFrames()
    {
        BuildTarget();
        EnemyMind m = MakeMind(_target, _character);
        m.telegraphFrames = 18;
        //默认那四是两拳两脚随机抽，这里要钉的是"第几拍按下去"而不是"按的是哪一下"，所以钉死成拳
        m.attackKeys = new[] { KeyMap.Punch };
        m.self.transform.position = new Vector3(1f, 0f, 0f);
        _character.transform.position = new Vector3(0f, 0f, 0f);

        TickMind(m, 0);                    //这一拍末尾决策一次，前摇开始
        Assert.AreEqual(18, m.TelegraphLeft, "决定出手的那一拍，前摇从整段开始记");

        for (int i = 0; i < 17; i++) TickMind(m, 0);
        Assert.AreEqual("BoxingStand", m.self.action.CurrentAction.id, "前摇没走完就不该出招");

        TickMind(m, 0);
        Assert.AreEqual("Punch", m.self.action.CurrentAction.catalog, "第 18 拍键按下去，当拍就派生出拳");
    }

    /// <summary>
    /// 进攻配额：源工程那三档——没人进攻 75%、1~2 个人在打 25%、3 个以上一律不打。
    /// 这里把概率拉到 100% 只测"该不该轮到我"这一半
    /// </summary>
    [Test]
    public void QuotaRefusesWhenEnoughAlreadyBusy()
    {
        BuildTarget();
        EnemyMind m = MakeMind(_target, _character);
        m.maxAttackers = 3;
        m.attackChanceWhenSomeBusy = 100;
        m.self.transform.position = new Vector3(1f, 0f, 0f);
        _character.transform.position = new Vector3(0f, 0f, 0f);

        TickMind(m, 3, KeyMap.NoInput);                    //已经有 3 只在打：不许再上
        Assert.AreEqual(0, m.TelegraphLeft, "场上进攻中的人数到达配额，这一轮不出手");
        Assert.AreEqual(0, m.LastPercent, "配额满了，概率直接是 0 而不是抽一次");

        m.FramesToDecision = 0;
        TickMind(m, 2);                                    //撤掉一只，配额重新放行
        Assert.AreEqual(m.telegraphFrames, m.TelegraphLeft, "没满配额就轮得到他出手，而且走的是 25% 那一档");

        m.FramesToDecision = 0;
        m.TelegraphLeft = 0;
        TickMind(m, 0);
        Assert.AreEqual(m.telegraphFrames, m.TelegraphLeft, "一只都没有就是 75% 那一档，100% 概率下必然出手");
        

    }

    [Test]
    public void MindStopsThinkingOnceThePlayerIsDead()
    {
        BuildTarget();
        EnemyMind m = MakeMind(_target, _character);
        m.self.transform.position = new Vector3(0f, 0f, 0f);
        _character.transform.position = new Vector3(3f, 0f, 0f);
        _character.Stats.TakeDamage(1000f);
        Assert.IsTrue(_character.IsDead, "前提：玩家得真的死了");

        for (int i = 0; i < 30; i++) TickMind(m, 0);

        Assert.AreEqual("BoxingStand", m.self.action.CurrentAction.id,
            "源工程玩家一死就 DisableAllEnemyAI，这里同理：别围着一具尸体挥拳");
    }

    [Test]
    public void MindHoldsStillWhileTheEnemyIsStunned()
    {
        BuildTarget();
        EnemyMind m = MakeMind(_target, _character);
        m.self.action.SetAllActions(GameData.ActionsFor(_target.actionSubset), "HurtFromForward");
        m.self.transform.position = new Vector3(0f, 0f, 0f);
        _character.transform.position = new Vector3(3f, 0f, 0f);

        TickMind(m, 0);

        Assert.AreEqual("HurtFromForward", m.self.action.CurrentAction.id,
            "挨着打的那几拍不思考也不走位：他的动作由受击行自己带回去，AI 不许插手");
    }

    //================================ 第20区：波次表与关卡推进（切片 2.3） ====================================
    //波次这一整套是"从表里生成人"，所以它必须能在没有场景的情况下被钉住：
    //`WaveManager.spawn` 是一个委托，测试塞假的进来就能只问"第几波什么时候开、一共开几个"，
    //而"生成一个人到底要几步"交给 `GameMain.PlaceCharacter` 那条真入口自己测

    private readonly List<GameObject> _waveRigs = new List<GameObject>(8);
    private readonly List<GameObject> _hudRoots = new List<GameObject>(4);
    private readonly List<CharacterObj> _waveUnits = new List<CharacterObj>(8);
    private int _spawnCalls;

    /// <summary>假的生成器：打真入口 `GameMain.PlaceCharacter`，这样"能不能死"也是真的</summary>
    private CharacterObj WaveSpawn(string prefabName, Vector3 at)
    {
        CharacterObj unit = GameMain.PlaceCharacter(
            AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), at, "TurnBack");
        _waveRigs.Add(unit.gameObject);
        _waveUnits.Add(unit);
        _spawnCalls++;
        return unit;
    }

    private void TickWave(WaveManager mgr, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            LogicClock.Advance();
            mgr.Tick(LogicClock.Frame);
        }
    }

    /// <summary>把已经生成的所有人都打死（测试里没有战斗，只能直接扣血）</summary>
    private void KillAllSpawned()
    {
        for (int i = 0; i < _waveUnits.Count; i++)
            if (_waveUnits[i] != null && !_waveUnits[i].IsDead) _waveUnits[i].Stats.TakeDamage(100000f);
    }

    /// <summary>
    /// 表 vs 素材库的第二次一致性检查：波次表里的每一只都要取得到预制体，而且**必须挂 CombatStats**。
    /// 少了后一半就会出一个查不出来的死局：那个人根本不会死，于是这一波永远清不完、关卡卡在第二波
    /// </summary>
    [Test]
    public void EveryWaveRowResolvesToAPrefabThatCanDie()
    {
        WaveTable.Load();
        Assert.Greater(WaveTable.Count, 0, "前提：Resources/GameData/Wave.json 读得出关卡");
        LevelRow level = WaveTable.Get("Level1");
        Assert.NotNull(level, "GameMain.firstLevelId 默认写的是 Level1");

        for (int w = 0; w < level.waves.Length; w++)
        {
            SpawnRow[] rows = level.waves[w].enemies;
            Assert.Greater(rows.Length, 0, "第 " + (w + 1) + " 波是空的");
            for (int i = 0; i < rows.Length; i++)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/" + rows[i].prefab + ".prefab");
                Assert.NotNull(prefab, rows[i].prefab + " 取不到预制体");
                Assert.NotNull(prefab.GetComponent<CombatStats>(), rows[i].prefab + " 没挂 CombatStats，他不会死");
            }
        }
    }

    [Test]
    public void NextWaveOpensOnlyAfterTheLastOneDiesAndTheGapIsUp()
    {
        WaveTable.Load();
        LevelRow level = WaveTable.Get("Level1");
        WaveManager mgr = new WaveManager();
        mgr.spawn = WaveSpawn;
        mgr.level = level;

        mgr.StartFirstWave();
        Assert.AreEqual(1, mgr.WaveIndex, "开波之后 WaveIndex 是已经开过的波数");
        Assert.AreEqual(level.waves[0].enemies.Length, mgr.AliveInWave);
        Assert.AreEqual(_spawnCalls, mgr.AliveInWave);

        //只打死第一波里的一只：剩下的人还站着，就不该有下一波
        _waveUnits[0].Stats.TakeDamage(100000f);
        Assert.AreEqual(1, mgr.AliveInWave, "同波还剩一个人，场上就该还有一个");
        TickWave(mgr, 200);
        Assert.AreEqual(level.waves[0].enemies.Length, _spawnCalls, "没清完就不许开下一波");

        KillAllSpawned();
        int gap = level.framesBetweenWavesFrames;
        //gap 而不是 gap-1：清完之后的第一拍只用来"排期"，那不算在间隔里
        TickWave(mgr, gap);
        Assert.AreEqual(level.waves[0].enemies.Length, _spawnCalls, "间隔没走完就不开：这是给玩家喘的那口气");

        TickWave(mgr, 1);
        Assert.AreEqual(level.waves[0].enemies.Length + level.waves[1].enemies.Length, _spawnCalls,
            "清完之后隔满 framesBetweenWavesFrames 的那一拍开出第二波");
        Assert.AreEqual(2, mgr.WaveIndex);
        Assert.IsFalse(mgr.LevelCleared);
    }

    [Test]
    public void LevelClearsAfterTheLastWaveAndNeverSpawnsBeyondTheTable()
    {
        WaveTable.Load();
        LevelRow level = WaveTable.Get("Level1");
        WaveManager mgr = new WaveManager();
        mgr.spawn = WaveSpawn;
        mgr.level = level;
        int total = 0;
        for (int w = 0; w < level.waves.Length; w++) total += level.waves[w].enemies.Length;

        mgr.StartFirstWave();
        for (int guard = 0; guard < 20 && !mgr.LevelCleared; guard++)
        {
            KillAllSpawned();
            TickWave(mgr, level.framesBetweenWavesFrames + 2);
        }

        Assert.IsTrue(mgr.LevelCleared, "最后一波也清完，就该报「这一关过了」");
        Assert.AreEqual(total, _spawnCalls, "一共只生成表里写的那几个人，之后一个都不许多开");
        Assert.AreEqual(level.waves.Length, mgr.WaveIndex);

        //推完之后再来一百拍也不该多发一个人
        TickWave(mgr, 100);
        Assert.AreEqual(total, _spawnCalls);
    }

    /// <summary>
    /// 生成一个人到底做了几步：认盒子、拿到**他自己那一份**动作子集、能被打死。
    /// 这一步少了任何一条，表现就是"场上多了一个不会动的假人"或者"他的招式跟玩家串了"
    /// </summary>
    [Test]
    public void PlaceCharacterBindsBoxesAndGivesHisOwnFamily()
    {
        CharacterObj unit = GameMain.PlaceCharacter(
            AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), new Vector3(12.5f, 0f, 0.9f), "TurnBack");
        _waveRigs.Add(unit.gameObject);

        Assert.NotNull(unit.BeHitBoxes, "判定盒没认到：采样拍查不到他");
        Assert.Greater(unit.BeHitBoxes.Length, 0);
        Assert.AreEqual(unit.actionSubset.Length, unit.action.AllActions.Count,
            "他只会 actionSubset 里那几招，不许把全表都端上来（切片 1.7 的那条规矩）");
        //prefab 上填了 defaultActionId 就优先用它，没填才轮到调用方给的兜底（切片 1.7 那条规矩）
        string expectedStart = string.IsNullOrEmpty(unit.defaultActionId) ? "TurnBack" : unit.defaultActionId;
        Assert.AreEqual(expectedStart, unit.action.CurrentAction.id, "开场动作要按 defaultActionId 的优先级来");
        Assert.AreEqual(new Vector3(12.5f, 0f, 0.9f), unit.transform.position,
            "出生坐标要按表里的来——z 是纵深车道，写 0 就等于所有人都挤在同一条线上");
        Assert.NotNull(unit.Stats, "带着 CombatStats 才能被打死，波次才推得动");
        Assert.IsTrue(unit.Stats.TakeDamage(100000f), "一记就打死要返回致死，路由才会走 Death");
        Assert.IsTrue(unit.IsDead);
    }

    //================================ 第21区：关卡边界与血量条（切片 2.3 后半） ====================================
    //边界是波次表里的四个数，血量条是代码搭的 UI。两片都不碰热路径，所以这里钉的是
    //"表里的数自洽"与"数值到条子上是对的那一条边"

    [Test]
    public void CameraIsClampedAtBothEndsAndNotInvertedWhenTheLevelIsNarrow()
    {
        //关卡 0~30、半屏宽 4 米 → 相机中心只能在 4~26
        Assert.AreEqual(4f, GameMain.ClampCameraX(-10f, 4f, 26f), 1e-4f, "贴着左边界走也不该把关卡外推进画面");
        Assert.AreEqual(26f, GameMain.ClampCameraX(40f, 4f, 26f), 1e-4f, "右边界同理");
        Assert.AreEqual(12f, GameMain.ClampCameraX(12f, 4f, 26f), 1e-4f, "区间内不干涉");

        //关卡比一屏还窄时（右算出来比左还小），Clamp 会把相机钉死在 left，所以那种情况得退回不夹
        Assert.AreEqual(40f, GameMain.ClampCameraX(40f, 26f, 4f), 1e-4f, "边界反了就不夹，而不是把相机按在某个数上");
        Assert.AreEqual(40f, GameMain.ClampCameraX(40f, float.MinValue, float.MaxValue), 1e-4f, "没有关卡边界时原样给回");
    }

    /// <summary>
    /// 表里的人不能出生在墙外：夹是每拍在 `CharacterObj.Move` 里做的，
    /// 于是"生成坐标越界"表现成"那个人一出场就被弹回边界上"，看上去像移动出了 bug
    /// </summary>
    [Test]
    public void EveryWaveRowSpawnsInsideTheDeclaredLevelBounds()
    {
        WaveTable.Load();
        LevelRow level = WaveTable.Get("Level1");
        Assert.IsTrue(level.HasBounds, "前提：Level1 填了边界四个数");

        for (int w = 0; w < level.waves.Length; w++)
        {
            SpawnRow[] rows = level.waves[w].enemies;
            for (int i = 0; i < rows.Length; i++)
            {
                Assert.GreaterOrEqual(rows[i].x, level.playMinX, "第 " + (w + 1) + " 波第 " + (i + 1) + " 只生在左墙外");
                Assert.LessOrEqual(rows[i].x, level.playMaxX, "第 " + (w + 1) + " 波第 " + (i + 1) + " 只生在右墙外");
                Assert.GreaterOrEqual(rows[i].z, level.laneMinZ, "第 " + (w + 1) + " 波第 " + (i + 1) + " 只生在车道外");
                Assert.LessOrEqual(rows[i].z, level.laneMaxZ);
            }
        }
    }

    [Test]
    public void HudBarFillFollowsHpAndStaysFullWithoutStats()
    {
        BuildTarget();
        Assert.AreEqual(1f, HudBar.FillFor(null), 1e-4f, "没挂数值组件就是满格：不装等于不参与，不该显示成空条");

        GiveStats(_target, 100f, 1f, 0f);
        Assert.AreEqual(1f, HudBar.FillFor(_target.Stats), 1e-4f);
        _target.Stats.TakeDamage(40f);
        Assert.AreEqual(0.6f, HudBar.FillFor(_target.Stats), 1e-4f, "掉四十血就该是六成");
        _target.Stats.TakeDamage(1000f);
        Assert.AreEqual(0f, HudBar.FillFor(_target.Stats), 1e-4f, "打死就是空条，不许是负的");
    }

    /// <summary>
    /// 血条是代码搭的，所以"搭出来不炸、写两次同样的数只脏一次"要有一个冒烟用例。
    /// 它不检查像素——那件事归 Play 模式的眼睛
    /// </summary>
    [Test]
    public void HudBarBuildsFromCodeAndIgnoresAnUnchangedValue()
    {
        HudBar bar = HudBar.Build("ACT-Test-HpBar", new Vector2(0f, 1f), new Vector2(180f, 16f), Color.magenta);
        Assert.NotNull(bar, "代码搭 UI 得真能搭出来（场景里没有 Canvas 时要自己建一个）");
        _hudRoots.Add(bar.gameObject);

        bar.SetValue(0.5f);
        bar.SetValue(0.5f);           //第二次是同样的数：SetValue 里直接 return，不脏 canvas
        bar.SetValue(-2f);            //越界的数要夹住而不是把 fillAmount 设成负的
        Assert.AreEqual(0f, bar.Value, 1e-4f, "夹在 0~1");
        bar.SetValue(3f);
        Assert.AreEqual(1f, bar.Value, 1e-4f);
    }

    //================================ 第22区：受击白闪与伤害飘字（切片 2.4） ====================================
    //这两件都是"打到了"的第三条通道：顿挫说"停了一下"、受击姿势说"打中了谁"、
    //亮一下和顶个数字说"这一下有多狠"。它们全部由逻辑帧计数，所以拍数能钉死。

    private GameObject _popupGo;
    private DamagePopups MakePopups()
    {
        _popupGo = new GameObject("ACT-Test-Popups");
        _popupGo.hideFlags = HideFlags.DontSaveInEditor | HideFlags.HideInHierarchy;
        DamagePopups p = _popupGo.AddComponent<DamagePopups>();
        p.Init();
        return p;
    }

    [Test]
    public void HitFlashCountsDownInLogicFramesAndCleansItself()
    {
        BuildTarget();                       //BindBoxes 里就把 HitFlash 挂好了，命中那一拍不再 AddComponent
        Assert.NotNull(_target.GetComponent<HitFlash>(), "组件该在绑定的时候就备好");

        _target.BeginHitFlash(HitFlash.DefaultFrames);
        HitFlash flash = _target.GetComponent<HitFlash>();
        Assert.AreEqual(HitFlash.DefaultFrames, flash.FramesLeft, "开始闪就是整段拍数");

        for (int i = 0; i < HitFlash.DefaultFrames - 1; i++)
        {
            LogicClock.Advance();
            _target.TickHitFlash();
        }
        Assert.AreEqual(1, flash.FramesLeft, "还没到点");

        LogicClock.Advance();
        _target.TickHitFlash();
        Assert.AreEqual(0, flash.FramesLeft, "到点自己退回来，不是一直白着");

        _target.BeginHitFlash(0);
        Assert.AreEqual(0, flash.FramesLeft, "0 拍就是不许闪——格挡那一记走的正是这条");
    }

    /// <summary>
    /// 飘字的数字是开机烤好的一张表，运行期只按下标取，所以 `Show` 一次都不分配字符串。
    /// 0 那一格是空串：挡住的一记伤害是 0，冒一个「0」出来等于告诉玩家"你白防了"
    /// </summary>
    [Test]
    public void DamagePopupNumbersComeFromAPreBakedTable()
    {
        Assert.AreEqual(0, DamagePopups.NumberIndex(0f), "没掉血就是第 0 格（空串）");
        Assert.AreEqual(7, DamagePopups.NumberIndex(7.4f), "小数按四舍五入，7.4 是 7");
        Assert.AreEqual(8, DamagePopups.NumberIndex(7.6f));
        Assert.AreEqual(DamagePopups.MaxNumber + 1, DamagePopups.NumberIndex(120f), "超出表尾就归到 99+ 那一格");

        DamagePopups p = MakePopups();
        DamagePopups.Show(new Vector3(1f, 0f, 0f), 120f);
        Assert.AreEqual("99+", p.TextAt(0));

        DamagePopups.Show(new Vector3(2f, 0f, 0f), 0f);
        Assert.AreEqual(1, p.ActiveCount, "伤害是 0 的一记根本不冒字：格挡那一记走的正是这条");
    }

    [Test]
    public void DamagePopupRisesAndIsTakenBackOnLifeFrames()
    {
        DamagePopups p = MakePopups();
        Vector3 at = new Vector3(5f, 0f, -2f);
        DamagePopups.Show(at, 7f);

        Assert.AreEqual(1, p.ActiveCount);
        Assert.AreEqual("7", p.TextAt(0));
        float startY = p.TextAt(0) != null ? _popupGo.transform.GetChild(0).position.y : 0f;
        Assert.GreaterOrEqual(startY, at.y + DamagePopups.StartHeight - 0.5f, "字要从头顶开始，不是压在脚上");

        for (int i = 0; i < DamagePopups.LifeFrames - 1; i++)
        {
            LogicClock.Advance();
            p.Tick();
        }
        Assert.AreEqual(1, p.ActiveCount, "差一拍就收，太急了看不清");
        float risenY = _popupGo.transform.GetChild(0).position.y;
        Assert.Greater(risenY, startY, "这一路是在往上飘");

        LogicClock.Advance();
        p.Tick();
        Assert.AreEqual(0, p.ActiveCount, "满 LifeFrames 那一拍自己收回");
    }

    [Test]
    public void PopupPoolRecyclesInsteadOfGrowing()
    {
        DamagePopups p = MakePopups();
        for (int i = 0; i < DamagePopups.Capacity + 4; i++)
            DamagePopups.Show(new Vector3(i, 0f, 0f), 3f);

        Assert.AreEqual(DamagePopups.Capacity, p.ActiveCount, "同屏最多就是池里那几个");
        Assert.AreEqual(DamagePopups.Capacity, _popupGo.transform.childCount, "一次都没有多 Instantiate");
    }

    /// <summary>
    /// 打真入口：掉血的那一记要同时点亮闪和飘字，而被挡下的那一记两个都不许出现。
    /// 这一条是"结算说话"与"表现说话"之间唯一的接线，所以必须用 `ResolveHit` 来验
    /// </summary>
    [Test]
    public void ResolveHitDrivesFlashButBlockedBlowStaysQuiet()
    {
        BuildTarget();
        DamagePopups p = MakePopups();
        GiveStats(_character, 100f, 10f, 0f);
        GiveStats(_target, 100f, 1f, 0f);
        //攻击者得真的站在出手那一行里：`AddHitRecord` 要在**当前动作**上按 phase 找那一记，
        //站在 BoxingStand 上打一拳，命中记录那条路径会直接摸到空数组
        AttackerIn("PlayerPunch1");

        AttackInfo attack = Info("PlayerPunch1").attacks[0];
        BeHitBoxTurnOnInfo plain = Info("BoxingStand").defensePhase[0];
        CombatPipeline.ResolveHit(_character, _target, in attack, in plain, _fist, _targetBody);
        Assert.AreEqual(HitFlash.DefaultFrames, _target.GetComponent<HitFlash>().FramesLeft, "掉了血就该闪");
        Assert.AreEqual(1, p.ActiveCount, "掉了血就该飘一个数字");

        //第二记：格挡成立（受击方那一行写了 block，而且两人面对面），伤害是 0。
        //受击阶段是 `ResolveHit` 的参数，不必真把角色切到那一行去——切了反而要动 prefab 的动作子集
        BeHitBoxTurnOnInfo blocked = Info("PlayerDefend").defensePhase[0];
        Assert.IsTrue(blocked.block, "前提：那一行写的是挡得住");
        PlaceFaceToFace();
        for (int i = 0; i < HitFlash.DefaultFrames; i++)   //把第一记的闪走完，第二记才好判断"没闪"
        {
            LogicClock.Advance();
            _target.TickHitFlash();
        }
        Assert.AreEqual(0, _target.GetComponent<HitFlash>().FramesLeft, "前提：第一记的闪已经退干净");
        int before = p.ActiveCount;
        CombatPipeline.ResolveHit(_character, _target, in attack, in blocked, _fist, _targetBody);
        Assert.AreEqual(0, _target.GetComponent<HitFlash>().FramesLeft, "挡住的一记不闪");
        Assert.AreEqual(before, p.ActiveCount, "挡住的一记也不飘字");
    }
}
