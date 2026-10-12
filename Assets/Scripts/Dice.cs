using UnityEngine;

/// <summary>
/// 逻辑层唯一的随机数源。解决的问题：这工程的立身之本是"同样的输入序列必然得到同样的局面"
/// （帧号时钟、整型硬直、AddInput 入口全都为它服务），而 `UnityEngine.Random` 靠墙钟、
/// `System.Random` 的算法随运行时版本变——回放、用例、以后做联网校验时全都会对不上。
///
/// 用的是 xorshift128：四个 uint 的状态、每次四步移位加异或，纯整数运算，
/// 任何平台任何版本逐位一致，成本比 `Random.value` 还低。
///
/// 【用法】开局 `Dice.Seed(某数)`，之后逻辑层每需要一次随机就 `Next` / `Chance` 一次。
/// 随机只能由**逻辑帧**触发，不能由渲染帧触发——不然同一份输入换台机器
/// 会因为帧率不同而抽到不同的数，这是本工程最不能容忍的不确定
/// </summary>
public static class Dice
{
    private static uint _x = 123456789u;
    private static uint _y = 362436069u;
    private static uint _z = 521288629u;
    private static uint _w = 88675123u;

    /// <summary>
    /// 默认种子。写成常量是为了"什么都不填也每次运行得到同一个局面"——
    /// 调手感的时候能一帧不差地复现同一连招，比每次换个随机数有用得多。
    /// 想要真随机就在 GameMain 上把 UseDefaultSeed 关掉
    /// </summary>
    public const uint DefaultSeed = 20261013u;

    /// <summary>
    /// 四个状态字必须都被铺满：全 0 的 xorshift 会永远输出 0（它是退化点，不是"随机得慢"），
    /// 所以用 splitmix32 从一个种子推出四个互不相同的数，seed=0 也不退化
    /// </summary>
    public static void Seed(uint seed)
    {
        uint s = seed;
        _x = SplitMix(ref s);
        _y = SplitMix(ref s);
        _z = SplitMix(ref s);
        _w = SplitMix(ref s);
        //再空转十次，让四个字的位彻底混开（splitmix 出来的相邻字只在高位有关联）
        for (int i = 0; i < 10; i++) Next();
    }

    private static uint SplitMix(ref uint state)
    {
        state += 0x9E3779B9u;
        uint z = state;
        z = (z ^ (z >> 16)) * 0x85EBCA6Bu;
        z = (z ^ (z >> 13)) * 0xC2B2AE35u;
        return z ^ (z >> 16);
    }

    /// <summary>
    /// 下一个 32 位随机数（xorshift128，Marsaglia 2003 的标准形式）
    /// </summary>
    public static uint Next()
    {
        uint t = _x ^ (_x << 11);
        _x = _y;
        _y = _z;
        _z = _w;
        _w = _w ^ (_w >> 19) ^ t ^ (t >> 8);
        return _w;
    }

    /// <summary>
    /// [0, maxExclusive) 的整数。maxExclusive 必须为正数。
    /// 【为什么不用取模】：`Next() % max` 和这里用的"乘完取高位"都有理论上的微小不均
    /// （2^32 不被 max 整除，量级 1e-8，对手感毫无影响），真正的区别是**调用次数**：
    /// 常见的写法是"拒绝掉超出范围的那部分再重抽"，那会让一次判定抽走几个数取决于运气，
    /// 于是同一份输入在另一台机器上会因为某个拒抽多抽了一次而从此岔开——
    /// 本工程要的是可回放，所以定死"每次调用只推进一次状态"
    /// </summary>
    public static int Next(int maxExclusive)
    {
        if (maxExclusive <= 0)
        {
            Debug.LogError("Dice.Next 的范围必须为正数，传进来的是 " + maxExclusive + "，返回 0");
            return 0;
        }
        return (int)(((ulong)Next() * (ulong)(uint)maxExclusive) >> 32);
    }

    /// <summary>
    /// 百分之几的概率成立。分母定死 100 是为了表里能直接写"30"表示 30%——
    /// 浮点阈值（`Next01() < 0.3f`）会让 0.3 这个数在二进制里无限循环，
    /// 边界上的抽样结果依赖实现，整数百分比没有这个问题
    /// </summary>
    public static bool Chance(int percent)
    {
        if (percent <= 0) return false;
        if (percent >= 100) return true;
        return Next(100) < percent;
    }
}
