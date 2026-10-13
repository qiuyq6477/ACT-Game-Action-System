using UnityEngine;

namespace CombatFramework
{
    ///<summary>
    ///动作资产 → 编辑期浮点模型（`ActionModel`）的构建器，只有一个入口 `ToModel(ActionAsset)`。
    ///谁在用：`ActionEditorWindow.RefreshPreviewModel`——Scene 视图要画判定盒线框，得先有一份
    ///按秒取样的浮点模型；它不落盘、不进热路径，改坏了只影响预览。
    ///
    ///运行期的表构建在 `Assets/Scripts/Character/GameData.Load`（走 `ActionAssetConvert.ToInfo`，
    ///秒→帧在那里换算成 int 帧号），与这里无关：两边读同一份资产，但一个给编辑期看、一个给运行期跑。
    ///
    ///这里拒的都是"填了就一定是错的"那一类：空 id、非正时长、负半边长、空 tag、越出动长的窗与关键帧。
    ///还没接的那条同样是等判定盒进运行期（`Doc/plan/beatemup-port.md` 的 A/F）再补：
    ///输入窗超出输入环长度（`InputToCommand.RecordKeepFrames`）。
    ///</summary>
    public static class CombatDataBuilder
    {
        public static ActionModel ToModel(ActionAsset a){
            AssertRef(a, a != null, "ActionAsset");
            if (string.IsNullOrEmpty(a.id)){
                throw new System.InvalidOperationException(
                    "动作资产 '" + a.name + "' 未填 id——平手全序按 id 定序，空 id 谁都撞");
            }
            if (a.duration <= 0f){
                throw new System.InvalidOperationException(
                    "动作 '" + a.id + "' 的 duration=" + a.duration + "（动作钟走不完的动作会卡在收尾态，"
                    + "挂在动作结束上的复位永远不落）");
            }
            ActionModel m = new ActionModel();
            m.id = a.id;
            m.animKey = a.animKey ?? "";
            m.catalog = a.catalog ?? "";
            m.duration = a.duration;
            m.priority = a.priority;
            m.autoNextActionId = a.autoNextActionId ?? "";
            m.keepPlayingAnim = a.keepPlayingAnim;
            m.inputSequence = ToKeySequence(a);
            m.inputValidSec = a.inputValidSec;

            CancelTagAsset[] cancelSrc = a.cancelTags ?? new CancelTagAsset[0];
            m.cancelTags = new ActionCancelTag[cancelSrc.Length];
            for (int i = 0; i < cancelSrc.Length; i++){
                CancelTagAsset t = cancelSrc[i];
                AssertRef(t, t != null, "CancelTagAsset");
                m.cancelTags[i] = new ActionCancelTag{
                    tag = t.tag ?? "",
                    startFrom = t.startFrom,
                    priority = t.priority
                };
            }

            BeCancelledTagAsset[] beSrc = a.beCancelledTags ?? new BeCancelledTagAsset[0];
            m.beCancelledTags = new ActionBeCancelledTag[beSrc.Length];
            for (int i = 0; i < beSrc.Length; i++){
                BeCancelledTagAsset t = beSrc[i];
                AssertRef(t, t != null, "BeCancelledTagAsset");
                CheckWindow(a.id, "被取消段" + i, t.windowMin, t.windowMax, a.duration);
                m.beCancelledTags[i] = new ActionBeCancelledTag{
                    windowMin = t.windowMin,
                    windowMax = t.windowMax,
                    cancelTag = Normalize(t.cancelTag),
                    priority = t.priority
                };
            }

            TempBeCancelledTagAsset[] tempSrc = a.tempBeCancelledTags ?? new TempBeCancelledTagAsset[0];
            m.tempBeCancelledTags = new ActionTempBeCancelledTag[tempSrc.Length];
            for (int i = 0; i < tempSrc.Length; i++){
                TempBeCancelledTagAsset t = tempSrc[i];
                AssertRef(t, t != null, "TempBeCancelledTagAsset");
                if (string.IsNullOrEmpty(t.id)){
                    throw new System.InvalidOperationException(
                        "动作 '" + a.id + "' 的第" + i + "个临时窗模板没填 id（点亮时按 id 查表，空 id 没人能解析）");
                }
                if (t.duration <= 0f){
                    throw new System.InvalidOperationException(
                        "动作 '" + a.id + "' 的临时窗 '" + t.id + "' duration=" + t.duration + "（点不开等于没这个窗）");
                }
                m.tempBeCancelledTags[i] = new ActionTempBeCancelledTag{
                    id = t.id,
                    duration = t.duration,
                    cancelTag = Normalize(t.cancelTag),
                    priority = t.priority
                };
            }

            MoveInputAcceptanceAsset[] accSrc = a.inputAcceptance ?? new MoveInputAcceptanceAsset[0];
            m.inputAcceptance = new ActionMoveAcceptance[accSrc.Length];
            for (int i = 0; i < accSrc.Length; i++){
                MoveInputAcceptanceAsset t = accSrc[i];
                AssertRef(t, t != null, "MoveInputAcceptanceAsset");
                CheckWindow(a.id, "移动接受度段" + i, t.windowMin, t.windowMax, a.duration);
                m.inputAcceptance[i] = new ActionMoveAcceptance{
                    windowMin = t.windowMin,
                    windowMax = t.windowMax,
                    rate = t.rate
                };
            }
            m.attackBoxes = ToBoxTracks(a.id, a.attackBoxes, a.duration, "攻击盒");
            m.hitBoxes = ToBoxTracks(a.id, a.hitBoxes, a.duration, "受击盒");
            return m;
        }

        ///<summary>
        ///盒轨道资产 → 模型：位姿本来就是浮点，这里只做形状与校验（半边长已在烘培时算好）。
        ///拒的几条都指向同一类事故——烘出来的东西与这招的时长不匹配（clip 换了、duration 手改过），
        ///症状是"这一段永远判不到"或"动作结束前盒还悬在半路"，全是静默的。
        ///</summary>
        private static ActionBoxTrack[] ToBoxTracks(string actionId, ActionBoxTrackBase[] src, float duration, string what){
            if (src == null || src.Length == 0) return new ActionBoxTrack[0];
            ActionBoxTrack[] dst = new ActionBoxTrack[src.Length];
            for (int i = 0; i < src.Length; i++){
                ActionBoxTrackBase t = src[i];
                AssertRef(t, t != null, "ActionBoxTrackBase");
                string p = "动作 '" + actionId + "' 的" + what + "轨[" + i + "]";
                if (string.IsNullOrEmpty(t.tag)) throw new System.InvalidOperationException(p + " 没填 tag（命中事件靠它认部位，空 tag 到了宿主也是一团糊）");
                CheckWindow(actionId, what + "轨 '" + t.tag + "' 开启窗", t.openMin, t.openMax, duration);
                if (t.keys == null || t.keys.Length == 0){
                    Debug.LogWarning(p + "（tag '" + t.tag + "'）一个位姿都没烘到——开着窗但没有形状，这条轨永远判不到东西（重烘一次）");
                    dst[i] = new ActionBoxTrack{
                        tag = t.tag, openMin = t.openMin, openMax = t.openMax,
                        keys = new ActionBoxKey[0]
                    };
                    continue;
                }
                ActionBoxKey[] keys = new ActionBoxKey[t.keys.Length];
                for (int k = 0; k < keys.Length; k++){
                    ActionBoxKeyAsset s = t.keys[k];
                    AssertRef(s, s != null, "ActionBoxKeyAsset");
                    if (s.t < 0f || s.t > duration){
                        throw new System.InvalidOperationException(
                            p + " 第" + k + "个关键帧 t=" + s.t + " 越出动长 " + duration + "（clip 与 duration 不匹配，重烘或改时长）");
                    }
                    keys[k] = new ActionBoxKey{
                        t = s.t, center = s.center,
                        axisX = s.axisX, axisY = s.axisY, axisZ = s.axisZ,
                        half = Half(p, s.half)
                    };
                }
                dst[i] = new ActionBoxTrack{
                    tag = t.tag, openMin = t.openMin, openMax = t.openMax,
                    keys = keys
                };
            }
            return dst;
        }

        ///<summary>半边长三分量必须都为正（零或负的盒谁都盖不住，`Obb` 的正交单位基 + 半长口径也不成立）</summary>
        private static Vector3 Half(string context, Vector3 half){
            if (half.x <= 0f || half.y <= 0f || half.z <= 0f){
                throw new System.InvalidOperationException(context + " 的半边长必须都大于0，收到 " + half);
            }
            return half;
        }

        private static void CheckWindow(string actionId, string what, float min, float max, float duration){
            if (min < 0f || max <= min){
                throw new System.InvalidOperationException(
                    "动作 '" + actionId + "' 的" + what + " 区间非法：[" + min + "," + max + "]");
            }
            if (max > duration){
                throw new System.InvalidOperationException(
                    "动作 '" + actionId + "' 的" + what + " 越出动长：窗尾 " + max + " > duration " + duration
                    + "（身体时钟走不到的窗等于没开）");
            }
        }

        ///<summary>
        ///搓招序列：位名 → 位掩码（词表见 ActionKeyBits）。名字写错必须抛而不是跳过——
        ///跳过后这条序列少了那一拍，症状是"这招莫名更容易出"，比"出不来"难查得多。
        ///</summary>
        private static int[] ToKeySequence(ActionAsset a){
            string[] src = a.inputSequence ?? new string[0];
            int[] dst = new int[src.Length];
            for (int i = 0; i < src.Length; i++){
                int bit;
                if (string.IsNullOrEmpty(src[i]) || ActionKeyBits.TryBit(src[i], out bit) == false){
                    throw new System.InvalidOperationException(
                        "动作 '" + a.id + "' 的搓招序列第" + i + "项 '" + (src[i] ?? "")
                        + "' 不在输入位词表里（可选：Up/…/UpLeft 八个方向 + Button1..Button8）");
                }
                dst[i] = bit;
            }
            if (dst.Length == 0 && a.inputValidSec > 0f){
                Debug.LogWarning(
                    "[动作] '" + a.id + "' 配了 inputValidSec=" + a.inputValidSec
                    + " 但搓招序列为空——空招对输入这条路是关着的，这个数没有意义");
            }
            return dst;
        }

        private static string[] Normalize(string[] src){
            if (src == null) return new string[0];
            string[] dst = new string[src.Length];
            for (int i = 0; i < src.Length; i++) dst[i] = src[i] ?? "";
            return dst;
        }

        public static void AssertRef(object asset, bool ok, string kindName){
            if (ok == false){
                throw new System.InvalidOperationException(kindName + "为空，无法构建Model");
            }
        }
    }
}
