using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 碰撞盒标签的位索引表。
/// 解决的问题：判断"某个盒子的标签有没有被当前动作开启"原本每帧都要走一遍
/// List 的 Contains 字符串扫描，扫描前还得先 new 一个临时 List 把标签收集起来；
/// 这里给每个标签分配掩码里单独的一位，判断就变成一条位与 (boxMask & activeMask) != 0，
/// 运行期既不扫字符串也不分配。
/// 【注意1】位号按注册先后单调分配，所以同一个标签在不同运行里可能落在不同的位上，
/// 一次运行之内是稳定的。要打日志就说标签名，别说掩码数字。
/// 【注意2】标签总量超过 31 个就装不下，此时 MaskOf 报错并返回 0（该标签从此判定不到），
/// 这是配表期就该发现的问题，新增标签时留意 Console。
/// </summary>
public static class BoxTagRegistry
{
    private const int MaxTags = 31;

    private static readonly Dictionary<string, int> _bits = new Dictionary<string, int>();

    /// <summary>
    /// 单个标签的掩码；空标签算0，也就是不参与任何判定
    /// </summary>
    /// <param name="tag">标签名</param>
    /// <returns></returns>
    public static int MaskOf(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return 0;
        if (_bits.TryGetValue(tag, out int bit)) return 1 << bit;
        if (_bits.Count >= MaxTags)
        {
            Debug.LogError("碰撞盒标签数量超过" + MaxTags + "个，位掩码装不下了：" + tag);
            return 0;
        }

        int newBit = _bits.Count;
        _bits.Add(tag, newBit);
        return 1 << newBit;
    }

    /// <summary>
    /// 一组标签合成一个掩码
    /// </summary>
    /// <param name="tags">标签数组，可以是null（表里没填）</param>
    /// <returns></returns>
    public static int MaskOf(string[] tags)
    {
        if (tags == null) return 0;
        int mask = 0;
        for (int i = 0; i < tags.Length; i++) mask |= MaskOf(tags[i]);
        return mask;
    }
}
