/// <summary>
/// 逻辑层的时钟。解决的问题：以前动作和物理都靠 Time.deltaTime 推进，帧率一变手感就跟着变。
/// 规则：逻辑层里凡是"时长"一律用 int 逻辑帧表示，只有这里认识秒；
/// Tick 全工程只许出现在两个地方——把"米/秒"的速率换算成每帧位移、以及驱动 Animator 步进表现层。
/// </summary>
public static class LogicClock
{
    /// <summary>
    /// 一个逻辑帧的时长（秒），全工程唯一的时间常量。
    /// 动画全部是30帧authored的，60Hz的时钟下每个动画帧正好占2个逻辑帧，所以表里的帧数不会有取整误差
    /// </summary>
    public const float Tick = 1f / 60f;

    /// <summary>
    /// 逻辑层的"现在"。输入记录拿它当时间戳，所以输入窗口全是整数比较，跨帧率完全确定
    /// </summary>
    public static long Frame { get; private set; }

    /// <summary>
    /// 推进一个逻辑帧，只准GameMain的时钟循环调用
    /// </summary>
    public static void Advance()
    {
        Frame++;
    }
}
