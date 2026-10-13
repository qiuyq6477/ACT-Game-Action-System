
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
    /// 限定渲染帧率，验收Phase 2用的：填30/60/120各跑一次，逻辑行为必须完全一致，0表示不限制。
    /// 必须是真正的限帧——编辑器的Playback Rate只改Time.timeScale，那会连逻辑时钟一起放慢，测不出东西
    /// </summary>
    public int targetFrameRate = 0;

    /// <summary>
    /// 渲染帧与逻辑帧之间的差额，只有这里认识Time.deltaTime（换算本身在 LogicClock.TicksDue）
    /// </summary>
    private double _accumulator = 0;

    /// <summary>
    /// player和enemy的合成，为了每个逻辑帧能按同一顺序走一遍（Awake里拼好，不在热路径里分配）
    /// </summary>
    private CharacterObj[] _all;

    void Awake()
    {
        //限帧必须在关垂直同步之后才生效
        if (targetFrameRate > 0)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = targetFrameRate;
        }

        //就暂时写在这里读取吧，demo自然就偷懒了
        GameData.Load();
        //同样是偷懒，把数据全塞给角色
        player.action.SetAllActions(GameData.AllActions(), "BoxingStand");
        foreach (CharacterObj e in enemy)
        {
            e.action.SetAllActions(GameData.AllActions(), "TurnBack");
        }

        _all = new CharacterObj[enemy.Count + 1];
        _all[0] = player;
        enemy.CopyTo(_all, 1);
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
    /// 推进一个逻辑帧，顺序是钉死的：输入→动作→判定盒→命中结算→计时与移动。
    /// 以前这五家各写各的Update，谁先谁后由Unity决定，同一份代码换台机器就会差一帧。
    /// 命中结算放在动作推进之后，所以一记打击派生出来的动作要等下一个逻辑帧才生效（约16.7毫秒）
    /// 同轨推挤（`CharacterObj.SeparateLanes`）2026-10-13 摘掉了：玩家走到敌兵旁边就把人蹭走，
    /// 而敌兵今天还不会自己走位（prefab 上 controller=0），观感就是推土机。
    /// 函数和它的用例都留着，接回来只需要一行调用——接的时候要先定谁让路（今天是一人一半）
    /// </summary>
    private void TickAll()
    {
        foreach (CharacterObj c in _all)
            c.input.Tick();
        foreach (CharacterObj c in _all)
            c.action.Tick();
        foreach (CharacterObj c in _all)
            TickBoxes(c);

        //处理攻击和碰撞
        DealWithAttacks();

        foreach (CharacterObj c in _all)
        {
            long gcProbe = GcProbe.Begin(GcProbe.Region.CharacterTick);
            c.Tick();
            GcProbe.End(GcProbe.Region.CharacterTick, gcProbe);
        }
    }

    private static void TickBoxes(CharacterObj c)
    {
        foreach (AttackHitBox box in c.AttackBoxes) box.Tick();
        foreach (BeHitBox box in c.BeHitBoxes) box.Tick();
    }

    private void LateUpdate()
    {
        //正交相机（见 Doc/decision/0002 的 amendment）：纵深不再改变屏幕上的尺度，
        //同轨判定与眼睛读到的画面才一致，像素精灵将来才有 PixelPerfectCamera 可用。
        //base.y 与 pitch 是绑在一起的：脚下在画面里的高度 = -base.y×cos(pitch) + (角色z - base.z)×sin(pitch)，
        //按 pitch=25°、size=2.5 反算出 2.75，角色就还是原来那个占比。
        //z 不跟角色：跟着一起平移等于屏幕上原地不动；正交下可见纵深带很宽（≈ 2×size/sin(pitch) 米），
        //可玩车道纯由场景里的 LevelBounds 定，不再是镜头约束
        Vector3 baseCamPos = new Vector3(2, 2.75f, -4);
        usingCam.transform.position = new Vector3(baseCamPos.x + player.transform.position.x,
            baseCamPos.y + player.transform.position.y, baseCamPos.z);
    }

    /// <summary>
    /// 处理每个角色的本帧攻击
    /// </summary>
    private void DealWithAttacks()
    {
        foreach (CharacterObj ene in enemy)
        {
            //玩家对敌人
            if (player.CanAttackTargetNow(ene, out AttackInfo pAttackPhase, out BeHitBoxTurnOnInfo eDefenseInfo,
                    out AttackHitBox pAttackBox, out BeHitBox eneBox)) 
            {
                //先判断，如果hitRecord表示没法命中，那么这个框的信息无效
                HitRecord hRec = player.GetHitRecord(ene, pAttackPhase.phase);
                if (hRec == null || (hRec.CooldownFrames <= 0 && hRec.CanHitTimes > 0))
                {
                    DoAttack(player, ene, pAttackPhase, eDefenseInfo);
                }
            }
            //敌人对玩家
            if (ene.CanAttackTargetNow(player, out AttackInfo eAttackPhase, out BeHitBoxTurnOnInfo pDefenseInfo,
                    out AttackHitBox eAttackBox, out BeHitBox playerBox)) 
            {
                //先判断，如果hitRecord表示没法命中，那么这个框的信息无效
                HitRecord hRec = ene.GetHitRecord(player, eAttackPhase.phase);
                if (hRec == null || (hRec.CooldownFrames <= 0 && hRec.CanHitTimes > 0))
                {
                    DoAttack(ene, player, eAttackPhase, pDefenseInfo);
                }
            }
        }
    }

    /// <summary>
    /// 根据是否翻转获得方向
    /// </summary>
    /// <param name="dir"></param>
    /// <param name="inversed"></param>
    /// <returns></returns>
    private static ForceDirection GetForceDirection(ForceDirection dir, bool inversed)
    {
        if (!inversed) return dir;
        switch (dir)
        {
            case ForceDirection.Forward: return ForceDirection.Backward;
            case ForceDirection.Backward: return ForceDirection.Forward;
        }
        return dir;
    }
    
    /// <summary>
    /// 发动攻击
    /// </summary>
    /// <param name="attacker"></param>
    /// <param name="defender"></param>
    /// <param name="attackInfo"></param>
    /// <param name="defensePhase"></param>
    private void DoAttack(CharacterObj attacker, CharacterObj defender, AttackInfo attackInfo, BeHitBoxTurnOnInfo defensePhase)
    {
        //动作改变，各自动作各自占优，所以>=和>的区别就在这里了
        ActionChangeInfo attackerChange =
            attackInfo.selfActionChange.priority >= defensePhase.attackerActionChange.priority
                ? attackInfo.selfActionChange
                : defensePhase.attackerActionChange;
        ForceDirection attackerDir = GetForceDirection(attackInfo.forceDir, attacker.Inversed); 
        attacker.action.PreorderActionByActionChangeInfo(attackerChange, attackerDir);
        
        ActionChangeInfo defenderChange =
            attackInfo.targetActionChange.priority > defensePhase.selfActionChange.priority
                ? attackInfo.targetActionChange
                : defensePhase.selfActionChange;
        //两人相向则翻转，否则不翻转
        defender.action.PreorderActionByActionChangeInfo(defenderChange, attackerDir, attackInfo.hitStunFrames);
        //硬直每记命中都要刷新，不能只挂在换动作上：受击方已经在Hurt里的时候，
        //它自己的selfActionChange（Keep，优先级和攻击的targetActionChange相等）会赢下上面那个平手，
        //于是预约是空的、ChangeAction不会发生、hitStunFrames就没人写了——连击的第二下变成零硬直。
        //ApplyHitStop取较大者而不是叠加，所以重复命中只是把剩余硬重置满，不会越打越长。
        defender.action.ApplyHitStop(attackInfo.hitStunFrames);
        
        //攻击方卡帧
        attacker.action.SetFreezing(attackInfo.freezeFrames);
        
        //受击方位移（攻击方位移发生在动作本身了）
        Vector3 moveDis = new Vector3(
            attackInfo.pushPower.moveDistance.x * (attackerDir == ForceDirection.Forward ? 1 : -1),
            attackInfo.pushPower.moveDistance.y,
            attackInfo.pushPower.moveDistance.z
        );
        defender.SetForceMove(new MoveInfo
        {
            inFrames = attackInfo.pushPower.inFrames,
            moveDistance = moveDis,
            tweenMethod = attackInfo.pushPower.tweenMethod
        });
        
        //CancelTag开启
        foreach (string cTag in attackInfo.tempBeCancelledTagTurnOn)
        {
            attacker.action.AddTempBeCancelledTag(cTag);
        }
        foreach (string cTag in defensePhase.tempBeCancelledTagTurnOn)
        {
            defender.action.AddTempBeCancelledTag(cTag);
        }
        
        //造成伤害
        //todo demo里就先不做了
        
        //增加命中记录，确保不连续命中
        attacker.AddHitRecord(defender, attackInfo.phase);
    }
}
