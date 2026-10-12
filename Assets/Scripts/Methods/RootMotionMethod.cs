using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// RootMotion函数需要的数值，也就是表里那串param翻译之后的样子
/// 原来每帧位移都要把字符串 float.Parse 一遍（一帧两次调用就是六次解析），
/// 现在改在 GameData.Load 里用 RootMotionMethod.Bake 一次算好
/// </summary>
public struct RootMotionParam
{
    /// <summary>
    /// 走完的总位移，横版里就是x方向的米数
    /// </summary>
    public float totalDis;

    /// <summary>
    /// 动作百分比到多少才开始位移（前摇）
    /// </summary>
    public float startPec;

    /// <summary>
    /// 动作百分比到多少位移就走满（后摇之后不再前进）
    /// </summary>
    public float endPec;
}

/// <summary>
/// 一个RootMotion函数的两半：读表期的烘焙器 + 每帧用的求值器
/// </summary>
public struct RootMotionFunc
{
    public Func<string[], RootMotionParam> Bake;
    public Func<float, RootMotionParam, Vector3> Eval;
}

/// <summary>
/// 动作的RootMotion的移动函数
/// 表里策划填的是字符串参数，运行期直接解析既费CPU又白付分配，所以先在初始化期Bake成数值，
/// 每帧只走Eval：输入动作进行到的百分比，输出从动作起点到该百分比的累计偏移量。
/// 新增一个移动函数就在Methods里加一条，Bake负责认参数，Eval负责按百分比给偏移。
/// </summary>
public static class RootMotionMethod
{
    public static Dictionary<string, RootMotionFunc> Methods = new Dictionary<string, RootMotionFunc>
    {
        //---------------------------直线向前----------------------------------------
        {
            "GoStraight",
            new RootMotionFunc {Bake = BakeGoStraight, Eval = EvalGoStraight}
        },
    };

    /// <summary>
    /// 初始化期把某动作的rootMotionTween.param翻译成数值
    /// 动作没配rootMotionTween的时候param是null（JsonUtility对缺失字段只给默认值），得还给个默认
    /// </summary>
    /// <param name="method">函数名，即ScriptMethodInfo.method</param>
    /// <param name="param">表里的字符串参数</param>
    /// <returns></returns>
    public static RootMotionParam Bake(string method, string[] param)
    {
        if (string.IsNullOrEmpty(method)) return new RootMotionParam();
        return Methods.TryGetValue(method, out RootMotionFunc func) ? func.Bake(param) : new RootMotionParam();
    }

    private static RootMotionParam BakeGoStraight(string[] param)
    {
        //缺省值必须跟原来每帧解析时的缺省值一个不差：endPec缺省是1，
        //当成0的话Kick1那种只配了一个参数的动作会在动作开头就把位移走完
        RootMotionParam p = new RootMotionParam {endPec = 1};
        if (param == null || param.Length == 0) return p;
        p.totalDis = ParseAt(param, 0, 0);
        p.startPec = ParseAt(param, 1, 0);
        p.endPec = ParseAt(param, 2, 1);
        return p;
    }

    private static Vector3 EvalGoStraight(float pec, RootMotionParam p)
    {
        return pec <= p.startPec ? Vector3.zero :
            pec >= p.endPec ? new Vector3(p.totalDis, 0, 0) : new Vector3(pec * p.totalDis, 0, 0);
    }

    private static float ParseAt(string[] param, int index, float defaultValue)
    {
        return index < param.Length ? float.Parse(param[index]) : defaultValue;
    }
}
