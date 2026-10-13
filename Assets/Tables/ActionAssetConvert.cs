using UnityEngine;

namespace CombatFramework
{
    ///<summary>
    ///动作资产（秒）→ 运行期表结构（int 逻辑帧）的**唯一**换算处。
    ///
    ///为什么需要它：时间轴编辑器与它的资产按秒说话（窗与时长落盘为秒，见 `ActionAsset` 的类注释），
    ///而本工程的逻辑层红线是"时长一律 int 逻辑帧"（`Assets/Scripts/AGENTS.md`）。两边都要，所以边界只留一处：
    ///`GameData.Load` 走这里，转换器与对拍也走这里，舍入规则与 `ActionDurationDumper` 完全同一把尺
    ///（`RoundToInt(sec / LogicClock.Tick)`），不然后者校对出来的帧数和运行期实际开窗的帧数会差半格。
    ///
    ///单位分工：编辑器口径的那几个字段（duration/windowMin/startFrom/openMax/…）= **秒**；
    ///本工程补的字段（fadeInFrames/fadeOutFrames/priority/ruleIndex/attacks[]/rootMotionTween）= **逻辑帧**。
    ///同一个资产里两种单位并存，只靠字段名与 Tooltip 区分，所以换算只准有这一处。
    ///
    ///两条盒轨读的是不相交的两半：攻击轨只出 tag/窗/priority/ruleIndex（`ruleIndex` 是 `attacks[]` 的 phase 号，
    ///不是下标），受击轨只出 tag/窗/priority/临时窗点亮/两个动作改写——这个分界就是它们分成两个类型的原因。
    ///</summary>
    public static class ActionAssetConvert
    {
        ///<summary>一个逻辑帧的秒数。唯一主人是 `LogicClock.Tick`，与 `ActionBoxBake.StepSec` 同一个数</summary>
        public const float Tick = LogicClock.Tick;

        ///<summary>秒 → 逻辑帧：与 ActionDurationDumper 同一条舍入口径</summary>
        public static int FramesOf(float seconds){
            return Mathf.RoundToInt(seconds / Tick);
        }

        ///<summary>逻辑帧 → 秒</summary>
        public static float SecOf(int frames){
            return frames * Tick;
        }

        //──────────── 输入名 ⇄ KeyMap ────────────

        ///<summary>
        ///表里的 `commands[].keySequence` 存的是 KeyMap 枚举（本工程是"角色相对的前后 + 屏幕纵深的上下"），
        ///而资产的 `inputSequence` 是字符串，词表是 `ActionKeyBits`（屏幕八向 + Button1..8）。
        ///两边靠这张表对接，**换算无损、名字只是标签**：`Forward→"Right"` 描述的是"朝右站着按前"那一格，
        ///角色一反手（`InputToCommand.inversed`）它实际走向屏幕左侧——前/后按谁的方向换算是宿主的事，
        ///这里就是宿主的答案。窗口下拉里看到 Right 而表里存的是 Forward，不是配错了。
        ///</summary>
        private static readonly KeyMap[] MappedKeys = {
            KeyMap.Forward, KeyMap.Backward, KeyMap.Up, KeyMap.Duck,
            KeyMap.UpForward, KeyMap.UpBackward, KeyMap.DuckForward, KeyMap.DuckBackward,
            KeyMap.Punch, KeyMap.Kick,
        };
        private static readonly string[] MappedNames = {
            "Right", "Left", "Up", "Down",
            "UpRight", "UpLeft", "DownRight", "DownLeft",
            "Button1", "Button2",
        };

        public static bool TryKeyName(KeyMap key, out string name){
            for (int i = 0; i < MappedKeys.Length; i++){
                if (MappedKeys[i] == key){ name = MappedNames[i]; return true; }
            }
            name = null;
            return false;
        }

        public static bool TryKeyMap(string name, out KeyMap key){
            for (int i = 0; i < MappedNames.Length; i++){
                if (string.Compare(MappedNames[i], name, System.StringComparison.OrdinalIgnoreCase) == 0){
                    key = MappedKeys[i];
                    return true;
                }
            }
            key = KeyMap.NoDirection;
            return false;
        }

        //──────────── 资产 → 运行期表结构 ────────────

        ///<summary>
        ///逐字段生成 `ActionInfo`。**不碰 `[NonSerialized]` 的烘焙字段**（`tagMask`、`rootMotionParam`）——
        ///那两个仍由 `GameData.Load` 负责，本函数只是把表搬进逻辑层的形状。
        ///数组顺序照资产的轨道顺序出，与原来 JSON 的行内顺序一致。
        ///</summary>
        public static ActionInfo ToInfo(ActionAsset a){
            var info = new ActionInfo();
            info.id = a.id;
            info.durationFrames = FramesOf(a.duration);
            info.animKey = a.animKey;
            info.catalog = a.catalog;
            info.priority = a.priority;
            info.autoNextActionId = a.autoNextActionId;
            info.keepPlayingAnim = a.keepPlayingAnim;
            info.autoTerminate = a.autoTerminate;
            info.flip = a.flip;
            info.rootMotionTween = a.rootMotionTween;

            CancelTagAsset[] cancel = a.cancelTags ?? new CancelTagAsset[0];
            info.cancelTag = new CancelTag[cancel.Length];
            for (int i = 0; i < cancel.Length; i++){
                info.cancelTag[i] = new CancelTag{
                    tag = cancel[i].tag,
                    startFromFrame = FramesOf(cancel[i].startFrom),
                    fadeInFrames = cancel[i].fadeInFrames,
                    priority = cancel[i].priority,
                };
            }

            BeCancelledTagAsset[] be = a.beCancelledTags ?? new BeCancelledTagAsset[0];
            info.beCancelledTag = new BeCancelledTag[be.Length];
            for (int i = 0; i < be.Length; i++){
                info.beCancelledTag[i] = new BeCancelledTag{
                    cancelWindowFrames = Range(be[i].windowMin, be[i].windowMax),
                    cancelTag = be[i].cancelTag ?? new string[0],
                    fadeOutFrames = be[i].fadeOutFrames,
                    priority = be[i].priority,
                };
            }

            TempBeCancelledTagAsset[] temp = a.tempBeCancelledTags ?? new TempBeCancelledTagAsset[0];
            info.tempBeCancelledTag = new TempBeCancelledTag[temp.Length];
            for (int i = 0; i < temp.Length; i++){
                info.tempBeCancelledTag[i] = new TempBeCancelledTag{
                    id = temp[i].id,
                    lengthFrames = FramesOf(temp[i].duration),
                    cancelTag = temp[i].cancelTag ?? new string[0],
                    fadeOutFrames = temp[i].fadeOutFrames,
                    priority = temp[i].priority,
                };
            }

            MoveInputAcceptanceAsset[] acc = a.inputAcceptance ?? new MoveInputAcceptanceAsset[0];
            info.inputAcceptance = new MoveInputAcceptance[acc.Length];
            for (int i = 0; i < acc.Length; i++){
                info.inputAcceptance[i] = new MoveInputAcceptance{
                    range = Range(acc[i].windowMin, acc[i].windowMax),
                    rate = acc[i].rate,
                };
            }

            //commands：本工程每招至多一条命令（表里就是这么长的），所以资产的单条 inputSequence 足够承载
            string[] seq = a.inputSequence ?? new string[0];
            if (seq.Length == 0){
                info.commands = new ActionCommand[0];
            }else{
                var keys = new KeyMap[seq.Length];
                for (int i = 0; i < seq.Length; i++) keys[i] = TryKeyMap(seq[i], out KeyMap k) ? k : KeyMap.NoDirection;
                info.commands = new ActionCommand[]{
                    new ActionCommand{ keySequence = keys, validInFrames = FramesOf(a.inputValidSec) }
                };
            }

            //attackPhase 由攻击轨按序导出：本工程每条 phase 恒为 1 tag × 1 段窗（转换期已断言）
            AttackBoxTrackAsset[] atk = a.attackBoxes ?? new AttackBoxTrackAsset[0];
            info.attackPhase = new AttackBoxTurnOnInfo[atk.Length];
            for (int i = 0; i < atk.Length; i++){
                info.attackPhase[i] = new AttackBoxTurnOnInfo{
                    inFrames = new FrameRange[]{ Range(atk[i].openMin, atk[i].openMax) },
                    tag = new string[]{ atk[i].tag },
                    attackPhase = atk[i].ruleIndex,
                    priority = atk[i].priority,
                };
            }

            HitBoxTrackAsset[] hit = a.hitBoxes ?? new HitBoxTrackAsset[0];
            info.defensePhase = new BeHitBoxTurnOnInfo[hit.Length];
            for (int i = 0; i < hit.Length; i++){
                info.defensePhase[i] = new BeHitBoxTurnOnInfo{
                    inFrames = new FrameRange[]{ Range(hit[i].openMin, hit[i].openMax) },
                    tag = new string[]{ hit[i].tag },
                    priority = hit[i].priority,
                    tempBeCancelledTagTurnOn = hit[i].tempBeCancelledTagTurnOn ?? new string[0],
                    attackerActionChange = hit[i].attackerActionChange,
                    selfActionChange = hit[i].selfActionChange,
                };
            }

            //attacks 直接就是本工程的 AttackInfo（int 帧口径），数组引用原样递出去，不复制
            info.attacks = a.attacks ?? new AttackInfo[0];
            return info;
        }

        ///<summary>FrameRange 用字段直构，**不走那个会 Clamp 到时长里的构造函数**——真源换算不该悄悄改数</summary>
        private static FrameRange Range(float minSec, float maxSec){
            return new FrameRange{ min = FramesOf(minSec), max = FramesOf(maxSec) };
        }
    }
}
