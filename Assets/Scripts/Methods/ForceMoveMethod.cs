using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 强制位移的方法
/// 参数1: ForceMove这条数据是什么
/// 返回值：当前移动量
/// </summary>
public static class ForceMoveMethod
{
    /// <summary>
    /// "这一记没有位移"用的常量委托。
    /// 【为什么不就地写 lambda】：它不捕获任何变量，所以编译器把它缓存成一个静态委托，
    /// 全工程共用一个实例——击退那一拍一次都不分配
    /// </summary>
    public static readonly Func<ForceMove, Vector3> NoMove = _ => Vector3.zero;

    public static Dictionary<string, Func<ForceMove, Vector3>> Methods =
        new Dictionary<string, Func<ForceMove, Vector3>>()
        {
            {
                "Slowly",
                (forceMove) =>
                {
                    if (forceMove.Data.inFrames <= 0) return Vector3.zero;
                    float wasPec = Mathf.Clamp01((float)forceMove.WasFramesElapsed / forceMove.Data.inFrames);
                    float curPec = Mathf.Clamp01((float)forceMove.FramesElapsed / forceMove.Data.inFrames);
                    //因为愚蠢的unity坐标系和正常游戏坐标系是反过来的，所以y负数向上，但是游戏开发的宇宙标准是y正向上
                    //所以我们得为策划填表做个翻译，就是给他的y反个向
                    float wasRate = 1.00f - Mathf.Pow(1.00f - wasPec, 3);
                    float curRate = 1.00f - Mathf.Pow(1.00f - curPec, 3);
                    Vector3 was = forceMove.Data.moveDistance * wasPec;
                    Vector3 cur = forceMove.Data.moveDistance * curPec;
                    return cur - was;
                }
            },
            {
                //被扔出去的人：横向匀着走，纵向走一条抛物线。
                //4h·t(1−t) 在 t=0 与 t=1 都是 0、在 t=0.5 取到 h，所以表里的 y 读作"最高点比手边高多少米"，
                //起点和落点都回到原来的高度——最后那半米由重力收尾（ThisTickMove 里强制位移优先于重力，
                //所以飞行期间不会被地面探测打断，走完这一程才开始下落）
                //"Arc" 的 y 是向上为正，不做那个反向翻译：被扔的人是往上飞的，跟策划填击退表时的直觉一致
                "Arc",
                (forceMove) =>
                {
                    if (forceMove.Data.inFrames <= 0) return Vector3.zero;
                    float wasPec = Mathf.Clamp01((float)forceMove.WasFramesElapsed / forceMove.Data.inFrames);
                    float curPec = Mathf.Clamp01((float)forceMove.FramesElapsed / forceMove.Data.inFrames);
                    Vector3 d = forceMove.Data.moveDistance;
                    Vector3 was = new Vector3(d.x * wasPec, 4f * d.y * wasPec * (1f - wasPec), d.z * wasPec);
                    Vector3 cur = new Vector3(d.x * curPec, 4f * d.y * curPec * (1f - curPec), d.z * curPec);
                    return cur - was;
                }
            }
        };

    /// <summary>
    /// 开机把表里写的曲线名过一遍：空串合法（那一记没有位移），认不出来才喊。
    /// 【为什么要喊】：认不出来时 `ForceMove.FromData` 静默落到 `NoMove`，表现是"打中人一动不动"，
    /// 而这条静默路径能把一个拼写错误藏到永远。本工程真撞过一次：
    /// `rootMotionTween.method` 的 GoStraight 在 `RootMotionMethod` 里有、在这张表里没有
    /// </summary>
    /// <param name="tweenMethod">表里填的名字</param>
    /// <param name="where">说清楚是哪一行的哪个字段，别让读日志的人再去猜</param>
    public static void Validate(string tweenMethod, string where)
    {
        if (string.IsNullOrEmpty(tweenMethod) || Methods.ContainsKey(tweenMethod)) return;
        Debug.LogError("表里 " + where + " 的 tweenMethod 写的是 「" + tweenMethod + "」，" +
                       "ForceMoveMethod 的函数表里没有这一条，于是这一记位移会一动不动。" +
                       "现有可选：" + string.Join("、", Methods.Keys) + "（留空表示没有位移）");
    }
}
