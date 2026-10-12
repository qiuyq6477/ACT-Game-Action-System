using UnityEngine;

/// <summary>
/// 角色的战斗属性组件：血量、攻击力、防御力。
/// 【为什么挂在角色身上而不是塞进 CharacterObj】：场上的角色并不都需要它——灰盒靶子、
/// 以后的道具和可破坏物各有各的存活规则，把它们都塞进角色类就是一堆"这个可能没有"的空判断。
/// 【为什么处处判 _maxHp <= 0】：这个组件是可选件，没配血量（Inspector 留 0）就等于"这个角色不参与数值结算"，
/// 动作、硬直、击退照旧——老 demo 角色的行为一个字节都不变。
/// </summary>
public class CombatStats : MonoBehaviour
{
    [Tooltip("最大血量，填 0 表示这个角色没有血量，一切数值结算跳过")]
    public float maxHp = 0;

    [Tooltip("攻击力，伤害公式里的那个乘数")]
    public float attack = 1;

    [Tooltip("防御力，从伤害里减掉")]
    public float defense = 0;

    /// <summary>
    /// 当前血量。只有 TakeDamage 和 Revive 会改它
    /// </summary>
    public float Hp { get; private set; }

    /// <summary>
    /// 有没有配血量（配了才谈得上"死"）
    /// </summary>
    public bool HasHp => maxHp > 0;

    public bool IsDead => HasHp && Hp <= 0;

    private void Awake()
    {
        Revive();
    }

    /// <summary>
    /// 血回满。由 CharacterObj.BindBoxes 顺带调一次——EditMode 不跑 Awake，
    /// 而用例和编辑期用例都得有一个"满血"的起点；Step 2 的对象池唤醒时也要调它
    /// </summary>
    public void Revive()
    {
        Hp = maxHp;
    }

    /// <summary>
    /// 掉血，并把血量夹在 0。
    /// </summary>
    /// <param name="amount">已经算完的最终伤害</param>
    /// <returns>这一记是不是把他打死了（本来就已经死了、或者压根没配血量，都返回 false）</returns>
    public bool TakeDamage(float amount)
    {
        if (!HasHp || IsDead) return false;
        Hp = Mathf.Max(0, Hp - amount);
        return Hp <= 0;
    }
}
