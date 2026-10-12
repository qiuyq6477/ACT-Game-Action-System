using System.Text;
using Unity.Profiling;
using UnityEngine;

/// <summary>
/// 每帧热点路径的堆分配探针。
/// 解决的问题：Doc/plan/refactor.md 的 Phase 1 要求热点路径每帧分配为 0 Byte，
/// 但 Profiler 窗口只给出整帧的 GC Alloc，看不出这些字节是哪一段代码产生的，
/// 所以这里按分区（一个方法或方法里的一小段）各自计数，每 300 帧汇总一次。
/// 计数来源是 Unity.Profiling 的 "GC Allocated In Frame" 计数器：
/// 它记的是"本帧到此刻为止累计分配了多少字节"，取区间两端的差值就是这段代码的分配量。
/// 【读报告须知】0 只有在"计数器可用 + 自检≈1024"两个条件都成立时才是真 0；
/// 报告第一行就把这两项写出来了，不成立时后面所有的 0 一律不可信。
/// （别用 Profiler.GetMonoUsedSizeLong 做差值：编辑器里它在一帧之内不动，量出来永远是 0；
/// Unity 2023.2 也已经没有 Profiler.GetAllocatedBytesForCurrentThread 了。）
/// 用法：成对调用 Begin/End，中间是被测代码；Enabled 改成 false 后两个方法都是空实现。
/// </summary>
public static class GcProbe
{
    public enum Region
    {
        ActionControllerUpdate,
        CalculateBoxInfo,
        CueFire,
        ChangeByCatalog,
        CanAttackTargetNow,
        NatureMove,
        InputUpdate,
        CharacterTick,
        BoxPose,
        SampleOverlaps,
        LaneSeparate,
        HoldApply,
        ChangeAction,
        DamageSettle,
        PoolTick,
        Count
    }

    /// <summary>
    /// 统计开关，必须是 const：false 时调用点被编译器直接消除，
    /// 不至于为了测量而反过来影响被测代码
    /// </summary>
    public const bool Enabled = true;

    /// <summary>
    /// 汇总间隔帧数。拼报告和 Debug.Log 本身要分配，打得太频繁会盖过被测代码的量
    /// </summary>
    private const int ReportEveryFrames = 300;

    private static readonly long[] _total = new long[(int)Region.Count];
    private static readonly long[] _max = new long[(int)Region.Count];
    private static readonly int[] _calls = new int[(int)Region.Count];
    private static int _lastReportFrame = -1;

    private static ProfilerRecorder _gcAllocInFrame;
    private static bool _recorderTried;

    /// <summary>
    /// 自检那块数组得被引用住，不然连分配都可能被优化掉，自检就永远"正常"了
    /// </summary>
    private static long[] _sink;

#pragma warning disable CS0162 // Enabled 是 const，关的那条分支本来就到不了，这正是我们要的编译期消除
    /// <summary>
    /// 给一段代码起表；返回负数表示计数器没起来，这段的量不会被计入统计
    /// </summary>
    /// <param name="region">分区</param>
    /// <returns></returns>
    public static long Begin(Region region)
    {
        if (!Enabled) return -1;
        return Sample();
    }

    /// <summary>
    /// 收表并把区间差值记进对应分区
    /// </summary>
    /// <param name="region">分区</param>
    /// <param name="before">Begin 的返回值</param>
    public static void End(Region region, long before)
    {
        if (!Enabled) return;

        long now = Sample();
        //取不到就不记：把"量不到" report 成 "0 分配"是最糟糕的假消息
        if (before >= 0 && now >= before)
        {
            int i = (int)region;
            long bytes = now - before;
            _total[i] += bytes;
            _calls[i]++;
            if (bytes > _max[i]) _max[i] = bytes;
        }

        //只在最外层分区收尾时汇总：CalculateBoxInfo 等内层分区包在 ActionControllerUpdate 里，
        //换别的分区触发的话，打印的分配会被算进还没收尾的外层区间
        if (region != Region.ActionControllerUpdate) return;
        int frame = Time.frameCount;
        if (frame % ReportEveryFrames != 0 || frame == _lastReportFrame) return;
        _lastReportFrame = frame;
        Report();
    }
#pragma warning restore CS0162

    private static long Sample()
    {
        if (!_recorderTried)
        {
            _recorderTried = true;
            _gcAllocInFrame = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
        }

        return _gcAllocInFrame.Valid ? _gcAllocInFrame.CurrentValue : -1;
    }

    private static void Report()
    {
        //自检放在拼串之前：它分配一块已知大小的东西，量不出来就说明整条计数路径是假的
        long selfTest = SelfTest();
        bool counting = _gcAllocInFrame.Valid && selfTest > 0;

        StringBuilder sb = new StringBuilder();
        sb.Append("[GcProbe] 最近").Append(ReportEveryFrames).Append("帧 单次调用分配(Byte)");
        sb.Append(" || 计数器 GC Allocated In Frame:").Append(_gcAllocInFrame.Valid ? "可用" : "取不到");
        sb.Append(" 自检:").Append(selfTest).Append("B(应为1024)");
        if (!counting) sb.Append(" ←计数没生效，下面的数字全部不可信");

        for (int i = 0; i < (int)Region.Count; i++)
        {
            int calls = _calls[i];
            sb.Append(" | ").Append((Region) i).Append(" avg=").Append(calls > 0 ? _total[i] / calls : 0)
                .Append(" max=").Append(_max[i]).Append(" n=").Append(calls);
            _total[i] = 0;
            _max[i] = 0;
            _calls[i] = 0;
        }

        Debug.Log(sb.ToString());
    }

    private static long SelfTest()
    {
        long before = Sample();
        _sink = new long[128];
        long now = Sample();
        return before >= 0 && now >= before ? now - before : 0;
    }
}
