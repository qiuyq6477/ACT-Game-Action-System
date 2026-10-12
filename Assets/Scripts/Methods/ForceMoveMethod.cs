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
}