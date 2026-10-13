using UnityEngine;

namespace CombatFramework
{
    ///<summary>
    ///取消标签（挂在"发起方"动作上）：本动作能取消掉带同 tag 的当前动作。
    ///</summary>
    public struct ActionCancelTag{
        ///<summary>tag 字符串（两方表靠它配对；拼错不报编译错，`CombatValidator` 查全库存在性）</summary>
        public string tag;

        ///<summary>进入本动作的起始时刻（秒，动作钟口径）：从当前动作的这一刻接着播，用于跳过前摇</summary>
        public float startFrom;

        ///<summary>从这条 tag 取消时的分数增量（与对方 tag 的 priority、本动作 priority 相加）</summary>
        public int priority;
    }

    ///<summary>
    ///被取消标签（挂在"可被取消"的动作上）：本动作在 [windowMin, windowMax) 这段里可以被带 tag 的动作取消。
    ///</summary>
    public struct ActionBeCancelledTag{
        ///<summary>窗开（秒，含）</summary>
        public float windowMin;

        ///<summary>窗闭（秒，不含）——半开口径见 `ActionRules.WindowOpen`</summary>
        public float windowMax;

        ///<summary>允许哪些 cancelTag 取消自己（表序即语义：配对取首匹配）</summary>
        public string[] cancelTag;

        ///<summary>从这里被取消时的分数增量</summary>
        public int priority;
    }

    ///<summary>
    ///临时被取消标签的模板：命中之类的瞬间按 id 点亮一个窗。
    ///本工程把点亮算成**绝对到期帧**（`CancelTag.FromTemp` + `ActionController.AddTempBeCancelledTag`），
    ///不用"当前动作进度的百分比段"——那种口径在一次动作切换之后就作废，还会读到新动作的过期进度。
    ///</summary>
    public struct ActionTempBeCancelledTag{
        ///<summary>模板 id（点亮时按它查表；跨动作存活后读的是"当前这一招"的同名模板）</summary>
        public string id;

        ///<summary>点亮后开启多久（秒，换算成逻辑帧存入组件）</summary>
        public float duration;

        ///<summary>开启期间允许哪些 cancelTag 取消自己</summary>
        public string[] cancelTag;

        ///<summary>分数增量</summary>
        public int priority;
    }

    ///<summary>
    ///移动接受度分段：本动作 [windowMin, windowMax) 期间，走路输入按 rate 缩放。
    ///重叠段取小，没被任何段覆盖时取 **0（=双脚钉住，不是 1.0）**——运行期的真源是
    ///`ActionController.CalculateInputAcceptance`，这里是它的编辑期镜像。
    ///</summary>
    public struct ActionMoveAcceptance{
        ///<summary>段开（秒，含）</summary>
        public float windowMin;

        ///<summary>段闭（秒，不含）</summary>
        public float windowMax;

        ///<summary>走路输入倍率（0=这段完全定住脚，1=照常走）</summary>
        public float rate;
    }

    ///<summary>
    ///动作盒的一帧位姿（编辑期烘出的浮点值）。三个基向量是**单位方向**——骨骼链的累积缩放不乘在轴上，
    ///而是乘进 `half`（见 `ActionBoxBake` 的 `scaledHalf`），所以 `axis × half` 就是盒的一个角。
    ///</summary>
    public struct ActionBoxKey{
        ///<summary>采样时刻（秒，动作钟口径，已量化到逻辑帧）</summary>
        public float t;

        ///<summary>盒心（单位局部系：相对 prefab 根、按朝向归零）</summary>
        public Vector3 center;

        ///<summary>三个单位方向（世界系，与 prefab 根的朝向无关）</summary>
        public Vector3 axisX, axisY, axisZ;

        ///<summary>三半边长（米，沿上面三个轴；已含骨骼链的累积缩放）</summary>
        public Vector3 half;
    }

    ///<summary>
    ///一条盒轨道：这一招里某个盒（拳头/小臂/一块受击部位）"哪段时间算数 + 这段时间里的位姿轨迹"。
    ///同一招里同一个 tag 可以有多条轨（先后或并列开窗），各开各的窗。
    ///`tag` 既是挂点 `UnitActionBox` 的名字，也是命中事件带出去的部位标识（肉质/背刺那套倍率在宿主侧查它）。
    ///</summary>
    public struct ActionBoxTrack{
        ///<summary>轨道标识（=挂点 UnitActionBox 的名字）</summary>
        public string tag;

        ///<summary>开启窗（秒，动作钟口径，半开 [openMin, openMax)）</summary>
        public float openMin;
        public float openMax;

        ///<summary>逐帧位姿（轨迹近乎不动时只有一帧）</summary>
        public ActionBoxKey[] keys;
    }

    ///<summary>
    ///动作模板（编辑期形态：`CombatDataBuilder.ToModel` 从 `ActionAsset` 转出，字段全浮点）。
    ///运行期不经过这里（它读 `ActionInfo`）；唯一的读者是时间轴窗口的盒线框预览，它只取 `attackBoxes`/`hitBoxes`
    ///两条盒轨。其余字段是资产的同形镜像，今天没有读者——转出来顺带把整条资产的区间与形状过一遍校验，
    ///填错了这里抛，窗口的盒预览退场（时间轴与动画预览照旧）。
    ///
    ///`duration` 与所有窗边界以秒（动作钟时间）表达，且**应当**落在 `LogicClock.Tick`（1/60 秒）的整数倍上：
    ///边界夹在两帧中间时"第几帧开窗"取决于身体时钟的偏移，而一次带非帧接手点的切换会把这个偏移
    ///原样带进之后所有窗（数出来的帧数与实际开窗那一帧对不上，就是这么来的）。量化在编辑期做——
    ///窗口拖拽吸到帧、`CombatValidator` 对非帧边界报警；运行期不拒（读的是同一份序列化值，只是数不准）。
    ///</summary>
    public struct ActionModel{
        ///<summary>动作注册名（全库唯一，也是仲裁平手全序的第二判据）</summary>
        public string id;

        ///<summary>动画 key：运行期直接就是 `anim.CrossFade(aInfo.animKey, …)` 的实参（`ActionController`），
        ///编辑窗口的预览也按同一个名字去 Animator 控制器里找 clip。</summary>
        public string animKey;

        ///<summary>分类（挨打/防御一类，"按分类挑招"用；可为空串）</summary>
        public string catalog;

        ///<summary>动作时长（秒）：timeElapsed 到即结束（走 autoNextActionId 或原地收尾）</summary>
        public float duration;

        ///<summary>基础优先级（平手时还有 id 字典序兜底）</summary>
        public int priority;

        ///<summary>自然播完后的下一招（空串=不切）</summary>
        public string autoNextActionId;

        ///<summary>切到同名动作时保持进度与动画（自取消连段：逻辑上重来，观感上连续）</summary>
        public bool keepPlayingAnim;

        ///<summary>本动作能取消掉哪些动作</summary>
        public ActionCancelTag[] cancelTags;

        ///<summary>本动作静态可被取消的段</summary>
        public ActionBeCancelledTag[] beCancelledTags;

        ///<summary>本动作的临时窗模板（按 id 点亮）</summary>
        public ActionTempBeCancelledTag[] tempBeCancelledTags;

        ///<summary>移动接受度分段（空=全程钉住不能走，见 `ActionMoveAcceptance`）</summary>
        public ActionMoveAcceptance[] inputAcceptance;

        ///<summary>
        ///搓招序列：每一项是一个 `ActionKeyBits` 位掩码，**顺序即语义**（`[Left, Down, Button1]` 是
        ///"←↓ A"，与 `[Button1, Down, Left]` 是两招）。相邻两项允许同一帧同时满足（按住 ↘ 再按 A
        ///一次就收下"方向 + 键"），所以这是**非递减**的时序而非严格递增。匹配在 `InputToCommand.ActionOccur`。
        ///空数组 = 玩家搓不出来，只有程序预约进得去（`ActionController.PreorderActionByActionChangeInfo`，
        ///受击反应走的就是这条路）——挨打这类被动招就该留空。
        ///</summary>
        public int[] inputSequence;

        ///<summary>
        ///整条序列的有效期（秒，按逻辑帧计，不吃动作钟倍率、也不被顿帧拉长）：从本帧往回数这么多帧
        ///之内的输入记录才算数。换算链是 `ActionAssetConvert.FramesOf` → `ActionInfo.validInFrames` →
        ///`InputToCommand` 的回看长度；超过输入环 `InputToCommand.RecordKeepFrames`（72 帧）就等于整环。
        ///</summary>
        public float inputValidSec;

        ///<summary>攻击盒轨道（编辑期烘）。窗口按播放头取样；烘出来的 keys 运行期暂时没人读
        ///（命中判定仍走 prefab 上的 trigger），资产 ↔ 快照的对拍在 `ActionTableEquivalenceTests`，不含 keys。</summary>
        public ActionBoxTrack[] attackBoxes;

        ///<summary>受击盒轨道（编辑期烘）：这一招进行中的这具身体，各部位占的是哪个盒</summary>
        public ActionBoxTrack[] hitBoxes;
    }
}
