using UnityEngine;

/// <summary>
/// 一记命中的结算：数值（伤害/削韧/格挡/死亡）和由它决定的那几处反应。
/// 【为什么单独一个静态类】：`GameMain.TickAll` 只管拍序，打中人之后发生的事全在这里——
/// 它不依赖任何场景状态，只有两个角色和两行表数据，所以编辑态用例能直接调用它。
/// （切片 1.9 之前这一段长在 `GameMain.DoAttack` 里，用例碰不到，只能在测试里照抄一遍。）
/// 【伤害公式用的是早就躺在表里、以前没人消费的四个数】：
/// `攻击力(攻击方的 CombatStats.attack) × 表里的 attack × 攻击盒的 damageRate × 受击盒的 meat(肉质) − 防御力(受击方)`，
/// 减完夹在 0（防御高过伤害就是打不动，不掉血但该有的硬直照给——这是数值问题不是框架问题，
/// 策划要靠装备和肉质去配，框架不必替他猜一个"至少 1 点"）。
/// </summary>
public static class CombatPipeline
{
    /// <summary>
    /// 打死之后要路由到的动作分类。
    /// 【为什么写死在代码里而不是表上】：每一记攻击的"致死之后干什么"都是同一件事，
    /// 挂到表上等于给 40 行各抄一份一模一样的字段。分类下的具体哪一行（PlayerDeath 还是以后的 EnemyDeath）
    /// 仍然由 ChangeByCatalog 在那个人自己那份动作表里挑，所以角色之间的差别还是表说了算
    /// </summary>
    public const string DeathCatalog = "Death";

    /// <summary>
    /// 死亡预约的优先级。表上的 priority 目前最大是 30，取一个够压倒、又加上被选那行的 priority
    /// 之后不会溢出的数（PreorderActionByActionChangeInfo 里两者要相加）
    /// </summary>
    private const int DeathPriority = 1000;

    /// <summary>
    /// 一记命中的完整结算：数值 → 双方的动作 → 硬直 → 卡肉 → 位移 → 抓取 → 临时取消标签 → 命中记录。
    /// 【为什么整段从 GameMain 搬进来】：这一段以前是 GameMain 的私有方法，编辑态用例碰不到，
    /// 只能把它里面的行照抄一份到测试里（第16区就这么干了，抄漏一行就测不到那一行）。
    /// 搬成静态入口之后，用例打的是真结算，"格挡不切动作""打空血就死亡"这种事才真的被钉住
    /// </summary>
    /// <param name="attackBox">命中的那个攻击盒，伤害公式取它的 damageRate</param>
    /// <param name="targetBox">命中的那个受击盒，伤害公式取它的 meat（肉质）</param>
    public static void ResolveHit(CharacterObj attacker, CharacterObj defender, in AttackInfo attackInfo,
                                  in BeHitBoxTurnOnInfo defensePhase, AttackHitBox attackBox, BeHitBox targetBox)
    {
        //伤害、削韧、格挡、死亡都在这里判完（血和韧性这一行就真的扣掉了）。
        //【为什么放在最前面】：下面三处反应——换不换动作、硬直给 hitStunFrames 还是 blockStunFrames、
        //推开给 pushPower 还是 blockPush——都要读它的结论，留在函数末尾就只能是注释
        DamagePayload hit = Settle(attacker, defender, in attackInfo, in defensePhase, attackBox, targetBox);

        //掉了血就说话：白闪 + 头上顶个数字。这两个都是"请求"，没有渲染器/没绑飘字就是空操作，
        //所以带灰盒的老工程一行行为都不会变（缺省站在老行为那一边）。
        //格挡的那一记伤害是 0——挡住不该冒一个「0」出来，那是在告诉玩家"你白防了"
        if (hit.Damage > 0f)
        {
            defender.BeginHitFlash(HitFlash.DefaultFrames);
            DamagePopups.Show(defender.transform.position, hit.Damage);
        }

        //动作改变，各自动作各自占优，所以>=和>的区别就在这里了
        ActionChangeInfo attackerChange =
            attackInfo.selfActionChange.priority >= defensePhase.attackerActionChange.priority
                ? attackInfo.selfActionChange
                : defensePhase.attackerActionChange;
        ForceDirection attackerDir = Mirror(attackInfo.forceDir, attacker.Inversed);
        attacker.action.PreorderActionByActionChangeInfo(attackerChange, attackerDir);

        ActionChangeInfo defenderChange =
            attackInfo.targetActionChange.priority > defensePhase.selfActionChange.priority
                ? attackInfo.targetActionChange
                : defensePhase.selfActionChange;
        if (hit.Lethal)
        {
            //打死这一记不再挑"受击/被打倒"，直接按分类路由到他自己会的那一行 Death
            defender.action.PreorderActionByActionChangeInfo(DeathChange(), attackerDir);
        }
        else if (hit.Interrupts)
        {
            //两人相向则翻转，否则不翻转
            defender.action.PreorderActionByActionChangeInfo(defenderChange, attackerDir, hit.StunFrames);
            //硬直每记命中都要刷新，不能只挂在换动作上：受击方已经在Hurt里的时候，
            //它自己的selfActionChange（Keep，优先级和攻击的targetActionChange相等）会赢下上面那个平手，
            //于是预约是空的、ChangeAction不会发生、hitStunFrames就没人写了——连击的第二下变成零硬直。
            //ApplyHitStop取较大者而不是叠加，所以重复命中只是把剩余硬重置满，不会越打越长。
            defender.action.ApplyHitStop(hit.StunFrames);
        }
        else if (hit.StunFrames > 0)
        {
            //格挡：动作一动不动（还站在防御姿势里），只把时钟冻住 blockStunFrames 拍。
            //霸体硬抗走的是另一条——那边给的是 0，这一段整个跳过，所以它既不换动作也不停顿
            defender.action.ApplyHitStop(hit.StunFrames);
        }

        //攻击方卡帧
        attacker.action.SetFreezing(attackInfo.freezeFrames);

        //受击方位移（攻击方位移发生在动作本身了）
        //位移读的是 hit.Push 而不是表上的 pushPower：格挡给 blockPush，霸体硬抗给的是 MoveInfo.Zero
        Vector3 moveDis = new Vector3(
            hit.Push.moveDistance.x * (attackerDir == ForceDirection.Forward ? 1 : -1),
            hit.Push.moveDistance.y,
            hit.Push.moveDistance.z
        );
        defender.SetForceMove(new MoveInfo
        {
            inFrames = hit.Push.inFrames,
            moveDistance = moveDis,
            tweenMethod = hit.Push.tweenMethod
        });

        //抓取：表里这一记写了 holdFrames 就顺手把人举起来，约束本身在 TickAll 最后一拍统一生效。
        //被格挡的这一记不抓——挡住的手不能凭空把人举起来
        if (!hit.Blocked) attacker.TryGrab(defender, attackInfo);

        //CancelTag开启
        foreach (string cTag in attackInfo.tempBeCancelledTagTurnOn)
        {
            attacker.action.AddTempBeCancelledTag(cTag);
        }
        //【这一处 null 判断不是防御式编程】：`BeHitBoxTurnOnInfo` 是结构体，而"这一记打在一个没有对应
        //受击阶段的盒子上"是正常路径——`CanAttackTargetNow` 与 `GetDefensePhaseByBeHitBox` 在那种时候
        //给回的就是一个默认值，它的数组字段是 null。默认值走完整条结算必须安全，
        //否则"打中一个没配受击阶段的部位"就会把整拍结算炸掉
        if (defensePhase.tempBeCancelledTagTurnOn != null)
        {
            foreach (string cTag in defensePhase.tempBeCancelledTagTurnOn)
            {
                defender.action.AddTempBeCancelledTag(cTag);
            }
        }

        //增加命中记录，确保不连续命中
        attacker.AddHitRecord(defender, attackInfo.phase);
    }

    /// <summary>
    /// 攻击方向按攻击者此刻的正反手镜像（Overhead 不动，那是打高打低的，跟站哪边无关）
    /// </summary>
    private static ForceDirection Mirror(ForceDirection dir, bool inversed)
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
    /// 结算一记命中，并把血和韧性真的扣掉（不是问一问）。
    /// </summary>
    /// <param name="attackBox">命中的那个攻击盒，取它的 damageRate</param>
    /// <param name="targetBox">命中的那个受击盒，取它的 meat（肉质）</param>
    public static DamagePayload Settle(CharacterObj attacker, CharacterObj defender, in AttackInfo attackInfo,
                                       in BeHitBoxTurnOnInfo defensePhase,
                                       AttackHitBox attackBox, BeHitBox targetBox)
    {
        long probe = GcProbe.Begin(GcProbe.Region.DamageSettle);
        DamagePayload payload = Compute(attacker, defender, in attackInfo, in defensePhase, attackBox, targetBox);
        GcProbe.End(GcProbe.Region.DamageSettle, probe);
        return payload;
    }

    private static DamagePayload Compute(CharacterObj attacker, CharacterObj defender, in AttackInfo attackInfo,
                                         in BeHitBoxTurnOnInfo defensePhase,
                                         AttackHitBox attackBox, BeHitBox targetBox)
    {
        //起手就是"老行为"：打断、按表硬直、按表推开。下面每一条分支只能把某一项改掉，
        //于是"没装属性组件的角色"走满全程也还是改动之前的行为
        DamagePayload p = new DamagePayload
        {
            Interrupts = true,
            StunFrames = attackInfo.hitStunFrames,
            Push = attackInfo.pushPower,
        };

        CombatStats stats = defender.Stats;
        if (stats == null || !stats.HasHp) return p;

        //格挡：受击方这一头挂着 block 的受击阶段正开着，而且打在他正脸那一侧。
        //【为什么要问朝向】：源工程的格挡就是 IsDefending(invertedDir)——背后挨刀不该算挡住。
        //用几何位置而不是表里的 forceDir：forceDir 是"这一记算从哪儿打"的设计值，
        //两个人实际站哪儿才是能不能挡住的依据
        p.Blocked = defensePhase.block && defender.FacesToward(attacker);
        if (p.Blocked)
        {
            p.Damage = 0;
            p.PoiseDamage = 0;
            p.Interrupts = false;
            p.StunFrames = attackInfo.blockStunFrames;
            p.Push = attackInfo.blockPush;
            return p;
        }

        //攻击力是攻击方的属性：他没挂 CombatStats 就当 1 倍，让表里的 attack 自己表示伤害。
        //【为什么不在这里就整个跳过结算】：要不要掉血取决于受击方——灰盒靶子可以挨打不掉血，
        //而打人那一头有没有属性组件不该决定对方死不死
        float power = attacker.Stats ? attacker.Stats.attack : 1f;
        float raw = power * attackInfo.attack * attackBox.damageRate * targetBox.meat - stats.defense;
        p.Damage = Mathf.Max(0, raw);
        p.PoiseDamage = attackInfo.poiseDamage;
        p.Interrupts = defender.Poise == null || defender.Poise.Consume(p.PoiseDamage);
        if (!p.Interrupts)
        {
            //霸体硬抗：掉血，但不换动作、不硬直、不被推开（refactor.md Phase 3 的第1点）
            p.StunFrames = 0;
            p.Push = MoveInfo.Zero;
        }

        p.Lethal = stats.TakeDamage(p.Damage);
        //致死那一记不论霸体不霸体都要倒下，否则重甲兵会一边掉血一边站着把血掉空
        if (p.Lethal) p.Interrupts = true;
        return p;
    }

    /// <summary>
    /// 死亡的动作预约：按分类挑这个人自己会的那一行 Death。
    /// 【transFrames 留 0】：`TransitionNormalized = transFrames / durationFrames`，0 除出来还是 0，
    /// 就是"当场切，不做融合"——人已经死了，不该再花几拍淡入
    /// </summary>
    public static ActionChangeInfo DeathChange()
    {
        return new ActionChangeInfo
        {
            changeType = ActionChangeType.ChangeByCatalog,
            param = DeathCatalog,
            priority = DeathPriority,
        };
    }
}
