using UnityEngine;

namespace CombatFramework
{
    ///<summary>取消标签（资产层）：本动作能取消掉带同 tag 的动作。</summary>
    [System.Serializable]
    public class CancelTagAsset{
        [Tooltip("tag 字符串（与被取消方的 cancelTag 列表配对；写错不报编译错，构建期校验存在性）")]
        public string tag = "";
        [Tooltip("切进本动作时身体时钟落在哪一秒（跳过前摇用；单位：秒）")]
        public float startFrom = 0f;
        [Tooltip("从这条 tag 取消时的分数增量")]
        public int priority = 0;

        [Tooltip("切入方淡入多少逻辑帧（表里叫 fadeInFrames，单位是帧不是秒）")]
        public int fadeInFrames = 0;
    }

    ///<summary>被取消段（资产层）：本动作在 [windowMin, windowMax) 内可被带 tag 的动作取消。</summary>
    [System.Serializable]
    public class BeCancelledTagAsset{
        [Tooltip("窗开（秒，含）")]
        public float windowMin = 0f;
        [Tooltip("窗闭（秒，不含——半开：窗尾那一帧已经关）")]
        public float windowMax = 0f;
        [Tooltip("允许哪些 cancelTag 取消自己。表序即语义：同一个 cancelTag 被两对匹配到时，进分数的是先出现的那对")]
        public string[] cancelTag = new string[0];
        [Tooltip("从这里被取消时的分数增量")]
        public int priority = 0;

        [Tooltip("被取消时当前动作淡出多少逻辑帧（表里叫 fadeOutFrames，单位是帧）")]
        public int fadeOutFrames = 0;
    }

    ///<summary>临时被取消窗模板（资产层）：按 id 点亮，存活期是本招的 duration 之外的独立时间。</summary>
    [System.Serializable]
    public class TempBeCancelledTagAsset{
        [Tooltip("模板 id（点亮时按它查表；跨动作切换后读的是届时那一招的同名模板）")]
        public string id = "";
        [Tooltip("点亮后开多久（秒）。换算成逻辑帧存活，所以顿帧不会把它拉长，动作切换也不会把它清掉")]
        public float duration = 0f;
        [Tooltip("开启期间允许哪些 cancelTag 取消自己")]
        public string[] cancelTag = new string[0];
        [Tooltip("分数增量")]
        public int priority = 0;

        [Tooltip("模板点亮后动作淡出多少逻辑帧（表里叫 fadeOutFrames，单位是帧）")]
        public int fadeOutFrames = 0;
    }

    ///<summary>移动接受度分段（资产层）：本动作这段时间里走路输入按 rate 缩放。</summary>
    [System.Serializable]
    public class MoveInputAcceptanceAsset{
        [Tooltip("段开（秒，含）")]
        public float windowMin = 0f;
        [Tooltip("段闭（秒，不含）")]
        public float windowMax = 0f;
        [Tooltip("走路输入倍率：0=这段完全定住脚，1=照常走。重叠段取小；没配任何段=全程 1")]
        public float rate = 1f;
    }

    ///<summary>
    ///动作盒的一帧位姿（资产层）：字段与 `Utils/Geo3/Obb` 一一对应。
    ///跟骨骼的轨全是编辑期烘出来的（`Editor/ActionBoxBake.cs`，入口在动作时间轴窗口），美术不手填数字——手填等于让人
    ///报出"第 N 帧这根骨骼在世界里的位置"，那是唯一不该由人算的东西。
    ///</summary>
    [System.Serializable]
    public class ActionBoxKeyAsset{
        [Tooltip("采样时刻（秒，动作钟口径；已量化到逻辑帧）")]
        public float t = 0f;

        [Tooltip("盒心（单位局部系：相对 prefab 根节点、按朝向归零）")]
        public Vector3 center;

        [Tooltip("三个基向量（单位局部系，**单位长**）。`Obb` 按“正交单位基 + 三半边长”说话，"
            + "所以骨骼链上的缩放不进轴里，而是乘进下面那份半边长")]
        public Vector3 axisX, axisY, axisZ;

        [Tooltip("三半边长（米，沿上面三个轴）。= 盒上填的边长 ÷ 2 × 该轴的累积缩放，烘培时算一次")]
        public Vector3 half;
    }

    ///<summary>
    ///盒轨道的公共形状（资产层）：**这一招用一个盒的一份方式**——在哪个骨骼挂点（tag）取位姿、
    ///哪段时间算数。同一个 tag 可以建多条轨（同一段招式里这个盒先后/并列开多次），各开各的窗。
    ///增删轨与拖窗在 Action 时间轴窗口里做；跟骨骼的 `keys` 由烘培按 tag 找挂点填入，窗与结算字段烘培一概不碰。
    ///
    ///结算在两个子类上分岔：攻击轨只带一个指向 `attacks[]` 的指针，受击轨只带受击侧的点亮与动作改写。
    ///分成两个类型是为了让"给攻击轨配受击改写"这种没人读的组合在形状上就填不出来。
    ///因此字段声明一律用**具体类型**（`AttackBoxTrackAsset[]` / `HitBoxTrackAsset[]`）——
    ///数组写成基类类型时 Unity 只序列化基类自己那半，子类多出来的字段会静默丢掉，所以基类只准出现在参数上。
    ///</summary>
    [System.Serializable]
    public abstract class ActionBoxTrackBase{
        [Tooltip("轨道标识：填 prefab 上某个 UnitActionBox 的名字（攻击盒建议按挂点骨骼命名，受击盒按部位"
            + " Head/Chest/Arm…）。烘培按它找挂点采样位姿；运行期命中事件把它原样带出去，"
            + "部位倍率/肉质那套在宿主侧查它。\n同一个 tag 可以有多条轨，各开各的窗")]
        public string tag = "";

        [Tooltip("开启区间（秒，动作钟口径，半开）：这段时间里这条轨才参与判定。"
            + "在 Action 时间轴窗口里拖轨条两端编辑，吸附一个逻辑帧")]
        public float openMin = 0f;
        public float openMax = 0f;

        [Tooltip("这一段窗的优先级增量（表里 attackPhase/defensePhase 的 priority，整数）")]
        public int priority = 0;

        [Tooltip("逐帧位姿（编辑期烘）。轨迹近乎不动时会被压成一键")]
        public ActionBoxKeyAsset[] keys = new ActionBoxKeyAsset[0];
    }

    ///<summary>攻击盒的一条轨道：一扇窗 + 本招 `attacks[]` 里那条命中规则的指针。</summary>
    [System.Serializable]
    public class AttackBoxTrackAsset : ActionBoxTrackBase{
        [Tooltip("这一窗打中时走 `attacks[]` 里哪一条规则——填那条的 **phase 号，不是数组下标**"
            + "\n（运行期 `ActionController.IndexOfAttack` 按 phase 线性查，查不到就当这一拳没有结算）。"
            + "\n伤害/顿帧/推力/硬直/连击限制全在那条 attacks 里，轨上只有这一个指针。")]
        public int ruleIndex = -1;
    }

    ///<summary>受击盒的一条轨道：一扇窗 + 这扇窗被打通之后往哪里去。</summary>
    [System.Serializable]
    public class HitBoxTrackAsset : ActionBoxTrackBase{
        [Tooltip("这一段窗开着时点亮哪些临时连招窗（表里 tempBeCancelledTagTurnOn）")]
        public string[] tempBeCancelledTagTurnOn = new string[0];

        [Tooltip("打中我的人被改成哪个动作（表里 attackerActionChange；Keep=不改）")]
        public ActionChangeInfo attackerActionChange;

        [Tooltip("我自己被改成哪个动作（表里 selfActionChange；Keep=不改）")]
        public ActionChangeInfo selfActionChange;
    }

    ///<summary>
    ///动作数据资产：一个单位"会做的一招"。窗、段、时长全部以**秒**落盘（不用动画百分比——
    ///百分比的分母是播放器的时钟，逻辑侧要的是能独立推进的身体时钟），编辑窗口的百分比模式只是
    ///换算外壳，写进来的永远是秒。
    ///
    ///它实质是**按骨骼**的：同一招在两套骨架上关键帧位置不同，所以跨单位只复用 id 命名，
    ///不复用烘培结果（盒轨道同理，见动作模块方案的烘培一节）。
    ///</summary>
    [CreateAssetMenu(fileName = "NewAction", menuName = "Combat Framework/Action", order = 27)]
    public class ActionAsset : ScriptableObject{
        [Tooltip("动作 ID（全库唯一；仲裁平手时按它定序，所以改名等于改优先级）")]
        public string id = "";

        [Tooltip("动画 key：运行期直接拿它 `anim.CrossFade`（= Animator 里的 clip 名或状态名；空=不投动画）")]
        public string animKey = "";

        [Tooltip("动作分类（挨打/防御一类，'按分类挑招'用；可为空）")]
        public string catalog = "";

        [Tooltip("动作时长（秒）：身体时钟到点即收尾，接 autoNextActionId")]
        public float duration = 0f;

        [Tooltip("基础优先级（与两侧 tag 的增量相加后比大小）")]
        public int priority = 0;

        [Tooltip("自然播完后的下一招（空=停在收尾态等外部意图）")]
        public string autoNextActionId = "";

        [Tooltip("切到同名动作时保持进度与动画（自取消连段：逻辑上重来，观感上连续——'按住蹲着'就是这么做的）")]
        public bool keepPlayingAnim = false;

        [Tooltip("本动作能取消掉哪些动作")]
        public CancelTagAsset[] cancelTags = new CancelTagAsset[0];

        [Tooltip("本动作静态可被取消的段")]
        public BeCancelledTagAsset[] beCancelledTags = new BeCancelledTagAsset[0];

        [Tooltip("本动作的临时窗模板（命中确认之类）")]
        public TempBeCancelledTagAsset[] tempBeCancelledTags = new TempBeCancelledTagAsset[0];

        [Tooltip("移动接受度分段（**空=全程钉住不能走**，不是“能走”："
            + "`ActionController.CalculateInputAcceptance` 没匹配到段时把倍率清 0，`BoxingStand` 就靠这个定住脚）")]
        public MoveInputAcceptanceAsset[] inputAcceptance = new MoveInputAcceptanceAsset[0];

        [Tooltip("搓招序列，按顺序填位名（Up/UpRight/Right/DownRight/Down/DownLeft/Left/UpLeft/Button1..Button8，见 ActionKeyBits）。"
            + "\n同一帧可连着满足相邻两项（按住 ↘ 时按下 Button1，等于 [Down,Right,Button1] 一次成立）。"
            + "\n留空=玩家搓不出来，只有程序直接预约的进得去（`ActionController.PreorderActionByActionChangeInfo`，"
            + "受击反应就是这么进的）——挨打这类被动招就该留空。")]
        public string[] inputSequence = new string[0];

        [Tooltip("攻击盒轨道：这一招的每只拳/每段刀身一条（Action 时间轴窗口里增删）。跟骨骼的位姿由烘培按 tag 填入。\n"
            + "\n开窗由 `ActionController.CalculateBoxInfo` 每逻辑帧现算，吃的就是**这一招自己的身体时钟**；"
            + "不再单独挂一份盒——盒的窗是照这一招配的，拿另一个动作的时刻去播它就是配错。\n"
            + "\n目前烘出来的 keys 运行期没人读，命中判定仍走 prefab 上的 Sphere/Capsule trigger。")]
        public AttackBoxTrackAsset[] attackBoxes = new AttackBoxTrackAsset[0];

        [Tooltip("受击盒轨道：这具身体在这一招里各处部位占的是哪个盒，逐帧跟着动画走")]
        public HitBoxTrackAsset[] hitBoxes = new HitBoxTrackAsset[0];

        [Tooltip("整条搓招序列的有效期（秒，按逻辑帧计：顿帧期间照样过去）。0.1 = 提前 6 帧内按下都算数。"
            + "\n只在 inputSequence 非空时有意义；回看长度封顶在输入环 `InputToCommand.RecordKeepFrames`（72 帧），"
            + "\n超过它等于整环，不报错")]
        public float inputValidSec = 0f;

        //──────────── 本工程独有字段 ────────────
        //这些字段时间轴窗口**不画**（窗口只认它自己的六条 lane），只能在 Inspector 里编。
        //它们的单位一律是**逻辑帧**（与本工程的表口径一致），上面那些编辑器字段才是秒。
        //唯一的一次秒→帧换算在 `Assets/Tables/ActionAssetConvert.ToInfo`，运行期只看到 int 帧。

        [Tooltip("没收到命令时是否自动走向 autoNextActionId（表里 autoTerminate）")]
        public bool autoTerminate = false;

        [Tooltip("这一招是否翻转角色朝向（表里 flip；朝向真源见 InputToCommand.inversed）")]
        public bool flip = false;

        [Tooltip("位移表函数，指向 Methods/RootMotionMethods（表里 rootMotionTween）。"
            + "\n它的 param 在 GameData.Load 里烘焙成 rootMotionParam，不进资产")]
        public ScriptMethodInfo rootMotionTween;

        [Tooltip("命中规则表，与运行期的 `attacks[]` 逐字段同形（伤害/顿帧/推力/受击硬直/连击限制/动作改写），"
            + "\n整块是**逻辑帧**口径。攻击轨用 `ruleIndex` 按 **phase 号**指过来（不是数组下标），"
            + "\n一条规则可以被多条轨/多个窗共用；限次与再命中间隔也记在这侧（键=目标×phase）。")]
        public AttackInfo[] attacks = new AttackInfo[0];

        [Tooltip("策划注释（表里的 `说明`）。原表里它有两种形态：一行是字符串、一行是字符串数组，"
            + "所以这里统一成数组承载；回写 JSON 时单元素数组还原成字符串。不参与任何逻辑。")]
        public string[] 说明 = new string[0];
    }
}
