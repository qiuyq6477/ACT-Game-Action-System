using System.Text;
using UnityEngine;
using UnityEngine.Profiling;

/// <summary>
/// 每帧热点路径的堆分配探针。
/// 解决的问题：Doc/plan/refactor.md 的 Phase 1 要求热点路径每帧分配为 0 Byte，
/// 但 Profiler 窗口只给出整帧的 GC Alloc，看不出这些字节是哪一段代码产生的，
/// 所以这里按分区（一个方法或方法里的一小段）各自计数，每 300 帧汇总一次。
/// 原理是 Profiler.GetMonoUsedSizeLong()（托管堆已用字节）的区间差值：
/// 一帧之内不会有 GC，所以差值就是这段代码分配出来的量。
/// 用法：成对调用 Begin/End，中间是被测代码；把 Enabled 改成 false 后两个方法都是空实现。
/// </summary>
public static class GcProbe
{
    public enum Region
    {
        ActionControllerUpdate,
        CalculateBoxInfo,
        ChangeByCatalog,
        CanAttackTargetNow,
        NatureMove,
        InputUpdate,
        Count
    }

    /// <summary>
    /// 关掉统计用的开关，必须是 const：这样 false 时调用点会被编译器整段消除，
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

    /// <summary>
    /// 自检分配出来的数组得被引用住，不然可能连分配都被优化掉，自检就永远"正常"了
    /// </summary>
    private static long[] _sink;

#pragma warning disable CS0162 // Enabled 是 const，关的那条分支本来就进不去，这正是我们要的编译期消除
    public static long Begin(Region region)
    {
        if (!Enabled) return 0;
        return Profiler.GetMonoUsedSizeLong();
    }

    public static void End(Region region, long before)
    {
        if (!Enabled) return;

        long bytes = Profiler.GetMonoUsedSizeLong() - before;
        //万一增量回收真的在这个区间里跑了一趟，已用字节反而变小，这次的差值不可信，丢掉
        if (bytes >= 0)
        {
            int i = (int)region;
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

    private static void Report()
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("[GcProbe] 最近").Append(ReportEveryFrames).Append("帧 单次调用分配(Byte)");
        for (int i = 0; i < (int)Region.Count; i++)
        {
            int calls = _calls[i];
            sb.Append(" | ").Append((Region) i).Append(" avg=").Append(calls > 0 ? _total[i] / calls : 0)
                .Append(" max=").Append(_max[i]).Append(" n=").Append(calls);
            _total[i] = 0;
            _max[i] = 0;
            _calls[i] = 0;
        }

        sb.Append(" || 自检 ").Append(SelfTest()).Append("B(应约1024，为0说明Profiler分配计数没生效)");
        Debug.Log(sb.ToString());
    }

    private static long SelfTest()
    {
        long before = Profiler.GetMonoUsedSizeLong();
        _sink = new long[128];
        return Profiler.GetMonoUsedSizeLong() - before;
    }
}
