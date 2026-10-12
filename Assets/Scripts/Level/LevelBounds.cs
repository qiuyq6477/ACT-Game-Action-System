using UnityEngine;

/// <summary>
/// 关卡的可玩矩形（横向 x 与纵深 z）。
/// 解决的问题：角色走出关卡没有任何意义——镜头不跟、敌人不会在那里、地面也可能根本没有——
/// 所以位移写完位置之前先夹一道。镜头和 AI 读的是同一份范围，不再各写各的边界数字。
/// 只管 x/z：y 是高度，归 CharacterObj 的地面探测管。
/// </summary>
public class LevelBounds : MonoBehaviour
{
    /// <summary>
    /// 当前生效的关卡边界。为 null 时不夹——老场景、灰盒、以及编辑期用例都走这一条，
    /// 所以本工程改造前的行为逐项不变。Destroy 之后 Unity 的 == 会判成假，不用手动清
    /// </summary>
    public static LevelBounds Current;

    [Tooltip("可玩区最小值：x = 最左，y = 最近（靠镜头这侧的车道）")]
    public Vector2 min = new Vector2(-20f, -4f);
    [Tooltip("可玩区最大值：x = 最右，y = 最远（画面里侧的车道）")]
    public Vector2 max = new Vector2(20f, 4f);

    private void Awake()
    {
        Current = this;
    }

    /// <summary>
    /// 把点夹进可玩区。传引用不复制 Vector3，热路径上不留任何临时对象
    /// </summary>
    public void Clamp(ref Vector3 p)
    {
        p.x = Mathf.Clamp(p.x, min.x, max.x);
        p.z = Mathf.Clamp(p.z, min.y, max.y);
    }
}
