using System.Collections.Generic;
using UnityEngine;
using CombatFramework;

namespace CombatFrameworkEditor
{
    ///<summary>
    ///单动作校验：入口是时间轴窗口的"Validate"按钮，判据都冲着"字符串 tag 拼错不报编译错、
    ///症状是手感玄学"那一类去——取消关系全是 tag，跨动作的引用只有查全库才判得了。
    ///
    ///`WarnPrefix` 与 `ValidateActionAsset` 这两个签名被窗口钉死（`DrawValidation` 调校验，
    ///`DrawValidation` 与 `DrawBakeReport` 都按前缀分警告/错误样式），`ActionBoxBake` 的报表行也用它，
    ///改名字或改前缀等于把这两个区打回"所有行都是错误"。
    ///
    ///`CheckAttackRules` 管攻击轨 → `attacks[]` 那根指针：phase 配错不报编译错也不报运行期错，
    ///症状是"打中了但没伤害"，只有查表判据拦得住。
    ///还没接的那条判据要等判定盒进运行期（`Doc/plan/beatemup-port.md` 的 A/F）才有意义：
    ///输入窗超出输入环长度（`InputToCommand.RecordKeepFrames`，72 帧）。
    ///</summary>
    public static class CombatValidator
    {
        public const string WarnPrefix = "Warning: ";

        ///<summary>窗边界的量化粒度：同一个帧格，取 `ActionBoxBake.StepSec`，不再自己算一份</summary>
        private static readonly float FrameSec = ActionBoxBake.StepSec;

        ///<summary>
        ///单个动作的校验（动作时间轴窗口的入口）：本招自己 + 与全库的交叉引用。
        ///交叉引用要看到别的动作才判得了，而取消关系全是字符串 tag——拼错不报编译错，
        ///症状是"这一招永远接不上"，没人会当 bug 报，只会当成手感玄学。
        ///</summary>
        public static List<string> ValidateActionAsset(ActionAsset a, List<ActionAsset> library){
            List<string> issues = new List<string>();
            ValidateAction(a, a != null ? a.name : "<null>", issues);
            ValidateActionLinks(a, library, issues);
            return issues;
        }

        private static void ValidateAction(ActionAsset a, string path, List<string> issues){
            if (a == null){
                issues.Add(path + ": ActionAsset is null");
                return;
            }
            if (string.IsNullOrEmpty(a.id)) issues.Add(path + ": id is empty (ties break by id, so an empty id collides with everything)");
            if (a.duration <= 0f){
                issues.Add(path + ": duration<=0 (the body clock never finishes; the action sticks in its ending state)");
                return;
            }
            FrameIssue(path + ".duration", a.duration, issues, "duration");

            if (a.cancelTags != null){
                for (int i = 0; i < a.cancelTags.Length; i++){
                    CancelTagAsset t = a.cancelTags[i];
                    string p = path + ".cancelTags[" + i + "]";
                    if (t == null){
                        issues.Add(p + ": entry is null");
                        continue;
                    }
                    if (t.startFrom < 0f) issues.Add(p + ": startFrom is negative (take-over point before the action starts)");
                    else if (t.startFrom > a.duration) issues.Add(p + ": startFrom " + t.startFrom + " exceeds duration " + a.duration + " (you are already in the ending state when switched in)");
                    else FrameIssue(p + ".startFrom", t.startFrom, issues, "take-over point");
                    if (string.IsNullOrEmpty(t.tag)) issues.Add(p + ": empty tag (this cancel path can never be matched)");
                }
            }

            if (a.beCancelledTags != null){
                for (int i = 0; i < a.beCancelledTags.Length; i++){
                    BeCancelledTagAsset t = a.beCancelledTags[i];
                    string p = path + ".beCancelledTags[" + i + "]";
                    if (t == null){
                        issues.Add(p + ": entry is null");
                        continue;
                    }
                    CheckActionWindow(p, t.windowMin, t.windowMax, a.duration, "be-cancelled window", issues);
                    if (t.cancelTag == null || t.cancelTag.Length == 0){
                        issues.Add(p + ": no cancelTag configured (nothing can open this window)");
                        continue;
                    }
                    CheckNoEmptyTag(t.cancelTag, p + ".cancelTag", issues);
                }
            }

            if (a.tempBeCancelledTags != null){
                for (int i = 0; i < a.tempBeCancelledTags.Length; i++){
                    TempBeCancelledTagAsset t = a.tempBeCancelledTags[i];
                    string p = path + ".tempBeCancelledTags[" + i + "]";
                    if (t == null){
                        issues.Add(p + ": entry is null");
                        continue;
                    }
                    if (string.IsNullOrEmpty(t.id)) issues.Add(p + ": empty template id (nothing can light it up by name)");
                    if (t.duration <= 0f) issues.Add(p + ": duration<=0 (a window that cannot stay open is no window)");
                    else FrameIssue(p + ".duration", t.duration, issues, "temp window lifetime");
                    CheckNoEmptyTag(t.cancelTag, p + ".cancelTag", issues);
                }
            }

            if (a.inputAcceptance != null){
                for (int i = 0; i < a.inputAcceptance.Length; i++){
                    MoveInputAcceptanceAsset t = a.inputAcceptance[i];
                    string p = path + ".inputAcceptance[" + i + "]";
                    if (t == null){
                        issues.Add(p + ": entry is null");
                        continue;
                    }
                    CheckActionWindow(p, t.windowMin, t.windowMax, a.duration, "move-acceptance segment", issues);
                    if (t.rate < 0f) issues.Add(p + ": rate " + t.rate + " is negative (0 = feet nailed down, 1 = walk as usual; there is no negative walking)");
                    for (int j = i + 1; j < a.inputAcceptance.Length; j++){
                        MoveInputAcceptanceAsset o = a.inputAcceptance[j];
                        if (o == null) continue;
                        if (t.windowMin < o.windowMax && o.windowMin < t.windowMax){
                            issues.Add(WarnPrefix + p + " overlaps inputAcceptance[" + j + "] (runtime takes the minimum, so this is not a bug — but two overlapping segments is a table mistake)");
                        }
                    }
                }
            }

            CheckTracks(a, a.attackBoxes, path + ".attackBoxes", a.duration, "attack", issues);
            CheckTracks(a, a.hitBoxes, path + ".hitBoxes", a.duration, "hit", issues);
            CheckAttackRules(a, path, issues);
        }

        ///<summary>
        ///盒轨道：开窗、位姿、越界三件事。位姿是烘培写进去的，所以这里也是"clip 换了但没重烘"
        ///唯一的落地点——症状是这一段永远判不到，而图上那条色带看着完全正常。
        ///</summary>
        private static void CheckTracks(ActionAsset a, ActionBoxTrackBase[] tracks, string path, float duration, string kind, List<string> issues){
            if (tracks == null) return;
            for (int i = 0; i < tracks.Length; i++){
                ActionBoxTrackBase t = tracks[i];
                string p = path + "[" + i + "]";
                if (t == null){
                    issues.Add(p + ": entry is null");
                    continue;
                }
                if (string.IsNullOrEmpty(t.tag)) issues.Add(p + ": empty tag (tracks match mounts by tag; an empty one matches nothing and bakes no keys)");
                CheckActionWindow(p, t.openMin, t.openMax, duration, kind + " box open window", issues);
                if (t.keys == null || t.keys.Length == 0){
                    issues.Add(WarnPrefix + p + " (tag '" + t.tag + "'): no pose baked yet — the window is open but the box has no shape (bake it)");
                    continue;
                }
                for (int k = 0; k < t.keys.Length; k++){
                    ActionBoxKeyAsset s = t.keys[k];
                    if (s == null){
                        issues.Add(p + ".keys[" + k + "]: entry is null");
                        continue;
                    }
                    if (s.t < 0f || s.t > duration){
                        issues.Add(p + ".keys[" + k + "]: t=" + s.t + " is outside the action duration " + duration + " (the clip changed without a re-bake, or duration changed without one)");
                    }
                }
            }
        }

        ///<summary>
        ///攻击轨与 `attacks[]` 之间的指针。判据都冲着"命中静默地不结算"那一类去：
        ///`ToInfo` 把轨上的 ruleIndex 原样写进 attackPhase，运行期 `ActionController.IndexOfAttack`
        ///按 phase 线性查第一条，查不到**不报错**——盒照样判定成立，只是拿默认的 AttackInfo 结算
        ///（0 伤害、0 硬直、0 推力），谁都不会当成 bug 报。
        ///
        ///它必须独立成一条循环，不能并进 `CheckTracks`：那里在 keys 为空时就 continue，
        ///而"没烘位姿"和"指针配错"是两件事，前者不该把后者整段吞掉。
        ///</summary>
        private static void CheckAttackRules(ActionAsset a, string path, List<string> issues){
            AttackBoxTrackAsset[] tracks = a.attackBoxes;
            int trackCount = tracks != null ? tracks.Length : 0;
            if (trackCount == 0) return;
            AttackInfo[] rules = a.attacks;
            if (rules == null || rules.Length == 0){
                issues.Add(path + ".attackBoxes: " + trackCount + " attack track(s) but attacks[] is empty"
                    + " (every hit settles as nothing)");
                return;
            }
            for (int i = 0; i < trackCount; i++){
                AttackBoxTrackAsset t = tracks[i];
                if (t == null) continue;
                if (IndexOfPhase(rules, t.ruleIndex) < 0){
                    issues.Add(path + ".attackBoxes[" + i + "] (tag '" + t.tag + "'): ruleIndex " + t.ruleIndex
                        + " matches no attacks[].phase (a hit that resolves no rule still registers, with 0 damage and 0 stun)");
                }
            }
            for (int i = 0; i < rules.Length; i++){
                int first = IndexOfPhase(rules, rules[i].phase);
                if (first != i){
                    issues.Add(path + ".attacks[" + i + "]: phase " + rules[i].phase + " is also carried by attacks["
                        + first + "] (IndexOfAttack stops at the first match, so this entry can never be reached)");
                }
                bool referenced = false;
                for (int k = 0; k < trackCount; k++){
                    if (tracks[k] != null && tracks[k].ruleIndex == rules[i].phase) referenced = true;
                }
                if (referenced == false){
                    issues.Add(WarnPrefix + path + ".attacks[" + i + "]: no attack track points at phase " + rules[i].phase
                        + " (this action can never land it)");
                }
            }
        }

        ///<summary>按 phase 取第一条——与运行期 `ActionController.IndexOfAttack` 同一个算法，连"重复取首条"这个行为一起</summary>
        private static int IndexOfPhase(AttackInfo[] rules, int phase){
            for (int i = 0; i < rules.Length; i++) if (rules[i].phase == phase) return i;
            return -1;
        }

        ///<summary>
        ///边界不在逻辑帧上时只报警：这是"策划数的帧数与运行期实际开窗的那一帧差半格"——
        ///一次带非帧接手点的切换会把后续所有窗整体平移半帧，最难对上的就是它。
        ///</summary>
        private static void FrameIssue(string path, float sec, List<string> issues, string what){
            if (sec <= 0f) return;
            float frames = sec / FrameSec;
            float nearest = Mathf.Round(frames);
            if (Mathf.Abs(frames - nearest) < 0.02f) return;
            issues.Add(WarnPrefix + path + ": " + what + " " + sec + "s is not on a logic frame (" + frames.ToString("0.###")
                + " frames); align to " + (nearest * FrameSec).ToString("0.###") + "s");
        }

        private static void CheckActionWindow(string path, float min, float max, float duration, string what, List<string> issues){
            FrameIssue(path + ".windowMin", min, issues, what + " start");
            FrameIssue(path + ".windowMax", max, issues, what + " end");
            if (max <= min){
                issues.Add(path + ": " + what + " is empty [" + min + "," + max + ") (an empty window never opens; runtime does not complain, it just never hits)");
            }else if (max > duration){
                issues.Add(path + ": " + what + " ends at " + max + " but duration is " + duration + " (the body clock never gets there)");
            }
        }

        private static void CheckNoEmptyTag(string[] tags, string path, List<string> issues){
            if (tags == null) return;
            for (int i = 0; i < tags.Length; i++){
                if (string.IsNullOrEmpty(tags[i])) issues.Add(path + "[" + i + "]: empty tag (matching takes the first pair; an empty entry is skipped outright)");
            }
        }

        ///<summary>
        ///与全库的交叉引用只留一条：**autoNext 的目标必须存在**（切到不存在的动作=这一招播完就断，
        ///而它只有看到全库才判得了）。cancel 关系本身成环是合法的（连段就是互消），故不报。
        ///</summary>
        private static void ValidateActionLinks(ActionAsset a, List<ActionAsset> library, List<string> issues){
            if (a == null) return;
            string path = a.name;
            if (string.IsNullOrEmpty(a.autoNextActionId) == false && FindActionIn(a, library, a.autoNextActionId) == null){
                issues.Add(path + ": autoNextActionId '" + a.autoNextActionId + "' not found in the library (finishing will switch to a nonexistent action)");
            }
        }

        ///<summary>
        ///在全库里找一个动作 id，**把自己也算进去**（自指 autoNext 是合法配法，找得到才轮到那条报警去说破它）。
        ///</summary>
        private static ActionAsset FindActionIn(ActionAsset self, List<ActionAsset> library, string id){
            if (string.IsNullOrEmpty(id)) return null;
            if (self != null && self.id == id) return self;
            if (library == null) return null;
            for (int i = 0; i < library.Count; i++){
                if (library[i] != null && library[i].id == id) return library[i];
            }
            return null;
        }
    }
}
