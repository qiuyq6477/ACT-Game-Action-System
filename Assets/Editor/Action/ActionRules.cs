namespace CombatFramework
{
    ///<summary>
    ///时间轴窗口的判窗规则，全仓库只有这一条：`ActionBoxSampler` 的判窗与窗口的预览取样都走它。
    ///
    ///运行期不看这里——运行期的开窗比的是整数帧号（`Assets/Scripts/Character/Action/FrameRange`），
    ///这条半开秒口径只服务于编辑期的预览、线框与校验。
    ///</summary>
    public static class ActionRules
    {
        ///<summary>
        ///[min, max) 这段窗在本帧扫过的区间 [was, now) 里是否开着：两段半开区间有交 ⇔ `min &lt; now ∧ max &gt; was`。
        ///三个边界各有含义——窗头恰在本帧末点 ⇒ 还没开；窗尾恰在本帧起点 ⇒ 已经关；
        ///短于一帧的窗会被这一帧整段扫到（点取会整窗漏掉，这正是 ACT 修过的那个 bug）。
        ///
        ///`was == now` 是两种退化情形（身体冻结的那一帧、以及不在 tick 里的即时判据如意图入口）：
        ///扫过的是一个"点"而非区间，按这一刻本身判（`min ≤ t < max`），否则 t=0 那一瞬间没有任何窗开着。
        ///</summary>
        public static bool WindowOpen(float min, float max, float was, float now){
            if (was == now) return min <= was && max > was;
            return min < now && max > was;
        }
    }
}
