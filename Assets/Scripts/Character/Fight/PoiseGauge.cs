using UnityEngine;

/// <summary>
/// 霸体条（韧性）：挨打时先掉它，它掉空了角色才会被受击动作打断。
/// 【解决什么问题】：ACT 里"连打不断"这件事必须有个数值依据。以前框架只有一刀切的做法——
/// 命中就换受击动作，所以做不出"重甲兵挨两拳不回头、第三拳才崩防"这种手感
/// （Doc/plan/refactor.md 的 Phase 3 第1点）。
/// 【三条约定】：
/// 1. maxPoise 留 0 = 没有霸体，Consume 永远返回"打空了"，于是所有攻击照常打断，行为与装这个组件之前一致；
/// 2. 攻击那一方表里 poiseDamage 留 0 = 这一记不削韧，同样照常打断——
///    漏填一个字段就得到"谁都打不动他"是最难查的那种坑，所以缺省必须站在老行为这一边；
/// 3. 挨打之后要先过完 recoveryDelayFrames 才开始回，边挨打边回满等于没有霸体。
/// </summary>
public class PoiseGauge : MonoBehaviour
{
    [Tooltip("韧性上限，填 0 表示这个人没有霸体")]
    public float maxPoise = 0;

    [Tooltip("挨打之后隔多少个逻辑帧才开始恢复")]
    public int recoveryDelayFrames = 60;

    [Tooltip("恢复期里每一逻辑帧回多少点")]
    public float recoveryPerFrame = 0.5f;

    /// <summary>
    /// 当前韧性。只有 Consume 和 Tick 会改它
    /// </summary>
    public float Poise { get; private set; }

    /// <summary>
    /// 最近一次被削韧是第几逻辑帧（LogicClock 的长整型帧号），-1 表示还没挨过打
    /// </summary>
    private long _lastHitFrame = -1;

    private void Awake()
    {
        Refill();
    }

    /// <summary>
    /// 韧性回满，并清掉"刚挨过打"的记录。由 CharacterObj.BindBoxes 顺带调一次（EditMode 不跑 Awake），
    /// Step 2 的对象池唤醒时也要调它
    /// </summary>
    public void Refill()
    {
        Poise = maxPoise;
        _lastHitFrame = -1;
    }

    /// <summary>
    /// 扣一笔韧性。
    /// 【注意这一眼看不出来的地方】：它会直接改状态，不是"问一问"——所以一记命中只能调一次，
    /// 重复调用会把同一段伤害削两遍（GameMain 那一拍里 HitRecord 已经保证了不会重复结算）
    /// </summary>
    /// <param name="amount">这一记的削韧点数</param>
    /// <returns>true = 韧性被打空，这一记该打断他</returns>
    public bool Consume(float amount)
    {
        if (amount <= 0f) return true;
        _lastHitFrame = LogicClock.Frame;
        if (Poise > amount)
        {
            Poise -= amount;
            return false;
        }
        Poise = 0;
        return true;
    }

    /// <summary>
    /// 恢复一拍，由 CharacterObj.Tick 在"不在硬直"那一段里调用
    /// </summary>
    public void Tick()
    {
        if (Poise >= maxPoise) return;
        if (_lastHitFrame >= 0 && LogicClock.Frame - _lastHitFrame < recoveryDelayFrames) return;
        Poise = Mathf.Min(maxPoise, Poise + recoveryPerFrame);
    }
}
