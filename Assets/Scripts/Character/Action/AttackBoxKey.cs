using System;
using UnityEngine;

/// <summary>
/// 攻击盒轨迹的一个关键帧：从 `startFromFrame` 这一拍起，盒子摆在这个局部位置、这么大。
///
/// 【为什么要这东西】：源工程（BeatEmUp）全场只有一只判定盒，每招靠 .anim 里的位置/缩放曲线
/// 把它挪到拳头或脚上。本工程的真源在表里（`Doc/decision/0001`），
/// 所以盒子"什么时候在哪"必须一起搬进表——否则窗口在表里、位置在动画里，
/// "这一拳在哪一帧打到谁"仍然是半真半假，改动画就悄悄改了判定。
///
/// 表里只填碰过的帧（稀疏），读表期烘焙成按帧号直取的稠密数组，运行期只剩一次数组下标，零分配。
/// </summary>
[Serializable]
public struct AttackBoxKey
{
    /// <summary>从本工程的第几拍开始用这一条（0-based 的姿势帧号与拍号是恒等的，见导出工具的头注释）</summary>
    public int startFromFrame;

    /// <summary>盒子的局部位置（相对它挂着的父节点）</summary>
    public Vector3 position;

    /// <summary>盒子的局部尺寸，只有 BoxCollider 用得上（源工程那些盒子都是矩形）。
    /// 【填的是最终尺寸不是缩放】：导出工具给的是 m_LocalScale 曲线，写表时要按
    /// 「精灵 size × 缩放」乘成尺寸（Doc/plan/beatemup-anim-dump.md 每节都写了这个公式），
    /// 因为运行期直接把它喂给 BoxCollider.size，而 size 才是盒子的本体规格、缩放留给父节点
    /// </summary>
    public Vector3 size;

    /// <summary>
    /// 稀疏关键帧 → 两根稠密数组，下标就是逻辑帧号，长度都是 durationFrames + 1。
    /// 语义跟动画里的 constant 曲线一致：没走到下一条关键帧就沿用上一条；
    /// 第一条关键帧之前的帧也填它的值——那几帧窗口本来就不该开着（窗口和轨迹是同一条表项），
    /// 留一个"未定义"状态只会让热路径多一个分支。
    /// 【返回 false】表示这条表项没有轨迹，运行期根本不该碰盒子的变换
    /// </summary>
    /// <param name="keys">表里的稀疏关键帧</param>
    /// <param name="durationFrames">本动作的时长，稠密数组以它为界</param>
    /// <param name="positions">按帧号的位置</param>
    /// <param name="sizes">按帧号的尺寸</param>
    /// <param name="ownerId">只为了报错时知道是哪条表项</param>
    public static bool Bake(AttackBoxKey[] keys, int durationFrames, out Vector3[] positions,
        out Vector3[] sizes, string ownerId)
    {
        positions = null;
        sizes = null;
        if (keys == null || keys.Length == 0) return false;

        int length = durationFrames + 1;
        Vector3[] pos = new Vector3[length];
        Vector3[] siz = new Vector3[length];

        for (int i = 0; i < keys.Length; i++)
        {
            //帧号写乱不会崩，只会让轨迹倒放或者卡在第一条上，而这种错在画面上完全看不出所以非喊不可
            if (i > 0 && keys[i].startFromFrame < keys[i - 1].startFromFrame)
                Debug.LogError(ownerId + " 的盒子轨迹关键帧没按帧号递增写（第 " + i + " 条是 " +
                               keys[i].startFromFrame + "，前一条是 " + keys[i - 1].startFromFrame + "），先排好序");

            int from = Mathf.Clamp(keys[i].startFromFrame, 0, durationFrames);
            int to = i + 1 < keys.Length ? Mathf.Clamp(keys[i + 1].startFromFrame - 1, from, durationFrames) : durationFrames;
            AttackBoxKey key = keys[i];
            for (int f = from; f <= to; f++)
            {
                pos[f] = key.position;
                siz[f] = key.size;
            }
        }

        //第一条之前的帧：用第一条顶上（那几帧窗口不该开着，见方法注释）
        for (int f = 0; f < Mathf.Clamp(keys[0].startFromFrame, 0, durationFrames); f++)
        {
            pos[f] = keys[0].position;
            siz[f] = keys[0].size;
        }

        positions = pos;
        sizes = siz;
        return true;
    }
}
