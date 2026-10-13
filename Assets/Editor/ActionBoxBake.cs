using System.Collections.Generic;
using CombatFramework;
using UnityEditor;
using UnityEngine;

namespace CombatFrameworkEditor
{
    ///<summary>
    ///动作盒烘培的算法本体：把挂在骨骼上的 `UnitActionBox` 沿一条动画逐帧采成浮点位姿，
    ///填进动作资产**已建好的**盒轨道的 `keys`。窗口入口在 `ActionEditorWindow` 的走带栏
    ///（caster prefab + animKey 反查的 clip + 当前资产就是全部输入）；算法单独成一个类，
    ///是因为"两次烘必须位级一致"这条只能靠直接调用来断言，而编辑器窗口不是能被测试驱动的东西。
    ///
    ///烘培**只管几何**，它不是轨道的主人：轨道的建/删、开启窗、命中规则都在 Action 时间轴窗口里
    ///编辑，烘培对它们一个字段都不碰——按轨上的 tag 找同名挂点，采出的键整组替换进那条轨的 keys。
    ///资产侧改过窗/规则、挂点没动 ⇒ 重烘原样保留（旧设计"重烘盖掉资产侧手改"的那类事故从此不存在）。
    ///
    ///三条口径决定运行期能不能信这份数据：
    ///- **采样步长 = 一个逻辑帧**（`LogicClock.Tick` = 1/60 秒）。步长就是保真度地板：短于它的窗会整体消失，
    ///  所以动作时长与键时刻都量化到帧格（不是 0.05s 那种"看着整齐"的数）。
    ///- **单位局部系 = prefab 根节点的局部系**（脚在原点、朝向按 prefab 自己的正前方）。运行期把这个
    ///  系按单位的朝向旋回世界，所以烘的时候根节点摆成 identity 即可，prefab 内部怎么歪都一并烘进局部坐标。
    ///- **轴归一化、缩放乘进半边长**：Unity 的 Transform 只表达"旋转 + 逐轴缩放"，矩阵的三列本就两两
    ///  正交，拆成"单位轴 × 长度"是精确拆分而非近似。`Obb` 的判据要正交单位基（球盒最近点靠它），
    ///  所以缩放不进轴里；乘进半长后运行期仍只有乘、加、绝对值——零三角函数、零开方、零归一化。
    ///
    ///键数由"动得够多才记一帧"决定（`KeepPosEps`/`KeepAxisEps`）：直刺那种轨迹压成一两键，
    ///挥砍留 4~8 键，最后一帧强制留（否则窗尾那一帧没有形状可插）。
    ///</summary>
    public static class ActionBoxBake
    {
        ///<summary>
        ///采样步长 = 一个逻辑帧，也是量化粒度：`LogicClock.Tick`（1/60 秒）。
        ///窗、键、吸附全用这同一个数，别处再写一个秒常数就会漂窗。
        ///</summary>
        public static readonly float StepSec = LogicClock.Tick;

        private const float KeepPosEps = 0.02f;    ///<summary>相邻两帧盒心位移的记账阈值（米）</summary>
        private const float KeepAxisEps = 0.02f;   ///<summary>相邻两帧轴向差的记账阈值（1−cosθ 量级）</summary>

        ///<summary>
        ///写盘前的网格吸附：位置/半长落毫米网格（3 位小数）、轴与时刻落 0.1 毫米网格（4 位小数）。
        ///低于网格的位只是采样的浮点噪声，留着会让资产文本尾数抖动、重烘 diff 满屏；
        ///吸附后两次烘位级一致（`ActionBoxBakeTests.Bake_TwoRuns_AreBitIdentical` 把守）。轴吸附后偏离单位长至多 ~0.01%，
        ///远低于 2cm 的位移记账阈值，`Obb` 的正交单位基契约不受影响。
        ///</summary>
        public static float Snap(float v, int decimals){
            float scale = Mathf.Pow(10f, decimals);
            return Mathf.Round(v * scale) / scale;
        }

        ///<summary>
        ///烘一遍并把 keys 写进资产里已建好的轨道（不 SaveAsset——落盘是调用方的事，测试要能在内存里看结果）。
        ///返回 false=中止（原因逐条落在 <paramref name="lines"/> 里）；中止时资产一个字段都不动。
        ///个别轨填不上（tag 没有挂点、骨骼没被动画驱动）不中止整次：报表逐条说明，其余照填。
        ///</summary>
        public static bool Run(GameObject prefab, AnimationClip clip, ActionAsset target,
                               bool setDurationToClip, List<string> lines){
            if (prefab == null || clip == null || target == null){
                lines.Add("Aborted: prefab / clip / target action asset — one is missing");
                return false;
            }
            if (clip.length <= 0f){
                lines.Add("Aborted: clip length <= 0 (nothing to sample)");
                return false;
            }
            int frames = Mathf.CeilToInt(clip.length / StepSec);
            //时长向上取整到帧格：动作时长必须落在帧格上（窗边界才数得清第几帧开），
            //而末键恰好停在这个时长上——向下取整会让最后一键越出时长，构建期当场拒收。
            float duration = frames * StepSec;
            lines.Add("clip '" + clip.name + "' is " + clip.length.ToString("0.###") + "s long ⇒ sampling " + (frames + 1)
                + " frames (including frame 0); action duration set to " + duration.ToString("0.###") + "s");

            GameObject go = Object.Instantiate(prefab);
            go.name = prefab.name + "(bake)";
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            try{
                //盒一律从克隆体上取：采样跟的是它的 transform，prefab 那份只是同一张图纸
                List<UnitActionBox> boxes = CollectBoxes(go, lines);
                if (boxes.Count == 0){
                    lines.Add("Aborted: no usable UnitActionBox (no boxes at all; nothing to sample from)");
                    return false;
                }
                Animator animator = go.GetComponentInChildren<Animator>();
                if (animator == null){
                    lines.Add("Aborted: no Animator in the prefab (no bones to pose)");
                    return false;
                }

                //采样与写回分两段：先把每条轨的新键组算全，再一次性写——报表里"哪条填了/哪条没填"
                //说的是同一个最终状态，不会出现"报表说好着、资产里是半截"
                List<Akku> acc = new List<Akku>();
                for (int i = 0; i < boxes.Count; i++) acc.Add(new Akku(boxes[i]));
                //用编辑期的动画采样 API（AnimationMode），不是 Animator.Play/Update：后者在 EditMode 不评估人形曲线，
                //跑起来位姿一动不动，烘出来的整条轨都是第 0 帧——`ActionBoxBakeTests.Bake_PunchingHandTravels_AndGuardHandBarelyMoves` 拦的就是这个
                AnimationMode.StartAnimationMode();
                try{
                    for (int f = 0; f <= frames; f++){
                        float t = f * StepSec;
                        //时长取到帧格后末帧可能已越过 clip 末尾，那一帧要的是收势位姿
                        AnimationMode.BeginSampling();
                        AnimationMode.SampleAnimationClip(go, clip, Mathf.Min(t, clip.length));
                        AnimationMode.EndSampling();
                        Matrix4x4 rootToLocal = go.transform.worldToLocalMatrix;
                        for (int i = 0; i < acc.Count; i++){
                            //盒心偏移乘在最后：offset 是挂点自己局部系里的位移（本工程受击胶囊的 m_Center {y:1}
                            //就是这个口径），只挪 GetColumn(3)，三个轴不受影响
                            Matrix4x4 m = rootToLocal * acc[i].box.transform.localToWorldMatrix
                                * Matrix4x4.Translate(acc[i].box.offset);
                            acc[i].Offer(t, m.GetColumn(3), m.GetColumn(0), m.GetColumn(1), m.GetColumn(2), f == frames);
                        }
                    }
                }finally{
                    AnimationMode.StopAnimationMode();
                }

                int filled = 0;
                ApplyDuration(target, duration, setDurationToClip, lines);
                filled += FillTracks(target.attackBoxes, acc, target.duration, "ATK", lines);
                filled += FillTracks(target.hitBoxes, acc, target.duration, "HIT", lines);
                ReportUnusedMounts(acc, lines);
                lines.Add("Filled keys into " + filled + " track(s) of '" + target.id + "'");
                return true;
            }finally{
                Object.DestroyImmediate(go);
            }
        }

        //──────────── 采样 ────────────

        ///<summary>克隆体侧的预检：不合格的直接报出来并剔除，不进入采样（省得烘完才发现是空轨道）</summary>
        private static List<UnitActionBox> CollectBoxes(GameObject root, List<string> lines){
            List<UnitActionBox> boxes = new List<UnitActionBox>(root.GetComponentsInChildren<UnitActionBox>(true));
            for (int i = boxes.Count - 1; i >= 0; i--){
                UnitActionBox b = boxes[i];
                if (string.IsNullOrEmpty(b.trackTag)){
                    lines.Add("Removed " + b.gameObject.name + ": empty tag (tracks match mounts by tag)");
                    boxes.RemoveAt(i);
                }else if (b.size.x <= 0f || b.size.y <= 0f || b.size.z <= 0f){
                    lines.Add("Removed " + b.gameObject.name + " (" + b.trackTag + "): all three sides must be > 0, got " + b.size);
                    boxes.RemoveAt(i);
                }
            }
            return boxes;
        }

        //──────────── 写回 ────────────

        ///<summary>
        ///把采样结果按 tag 对号入座填进一组已有轨道。只写 keys：窗、命中规则、轨道增删都是
        ///资产侧（Action 时间轴窗口）的真相，烘培一概不碰。返回填上的轨数。
        ///</summary>
        private static int FillTracks(ActionBoxTrackBase[] tracks, List<Akku> acc, float duration,
                                      string kind, List<string> lines){
            if (tracks == null) return 0;
            int filled = 0;
            for (int ti = 0; ti < tracks.Length; ti++){
                ActionBoxTrackBase track = tracks[ti];
                if (track == null) continue;
                Akku source = null;
                for (int i = 0; i < acc.Count; i++){
                    if (acc[i].box.trackTag == track.tag){ source = acc[i]; break; }
                }
                if (source == null){
                    lines.Add(CombatValidator.WarnPrefix + "track[" + ti + "] '" + track.tag + "' (" + kind
                        + "): no UnitActionBox tagged '" + track.tag + "' on this prefab — keys left unchanged");
                    continue;
                }
                source.referenced = true;   //tag 配上了就算"被引用"，哪怕位姿没采成（那是另一条报表行的事）
                if (source.degenerate){
                    lines.Add("Skipped " + kind + " track[" + ti + "] '" + track.tag + "': zero scale in the bone chain"
                        + " collapses the box to zero volume (nothing could ever be hit) — keys left unchanged");
                    continue;
                }
                if (source.keys.Count == 0){
                    lines.Add("Skipped " + kind + " track[" + ti + "] '" + track.tag + "': not a single pose sampled"
                        + " (does the clip drive this bone at all?) — keys left unchanged");
                    continue;
                }
                track.keys = source.keys.ToArray();
                filled++;
                if (track.openMax > duration + StepSec * 0.5f){
                    lines.Add(CombatValidator.WarnPrefix + "track[" + ti + "] '" + track.tag + "' (" + kind
                        + "): open window ends at " + track.openMax + "s but the action duration is " + duration.ToString("0.###")
                        + "s — the build will reject a window past the action duration");
                }
                float lastT = source.keys[source.keys.Count - 1].t;
                if (lastT > duration + StepSec * 0.5f){
                    lines.Add(CombatValidator.WarnPrefix + "track[" + ti + "] '" + track.tag + "' (" + kind
                        + "): keys reach " + lastT.ToString("0.###") + "s but the action duration is " + duration.ToString("0.###")
                        + "s — the build will reject keys past the action duration (uncheck 'set duration to clip' only with a matching duration)");
                }
                Vector3 last = track.keys[track.keys.Length - 1].center;
                lines.Add("  " + kind + " track[" + ti + "] '" + track.tag + "': " + track.keys.Length + " keys, window "
                    + track.openMin + "~" + track.openMax + "s, last key center ("
                    + last.x.ToString("0.##") + ", " + last.y.ToString("0.##") + ", " + last.z.ToString("0.##") + ")  "
                    + VerticalSpan(track));
            }
            return filled;
        }

        ///<summary>没被任何轨引用的挂点逐个点名：要么是忘了建轨，要么是挂点该删了</summary>
        private static void ReportUnusedMounts(List<Akku> acc, List<string> lines){
            for (int i = 0; i < acc.Count; i++){
                if (acc[i].referenced) continue;
                lines.Add("Unused mount '" + acc[i].box.trackTag + "' (" + acc[i].box.gameObject.name
                    + "): no track in this action references it (create a track with this tag in the action timeline window)");
            }
        }

        private static bool ApplyDuration(ActionAsset target, float duration, bool setDurationToClip, List<string> lines){
            if (setDurationToClip){
                if (!Mathf.Approximately(target.duration, duration)){
                    target.duration = duration;
                    lines.Add("Action duration " + target.duration + "s (clip length rounded up to the frame grid)");
                    return true;
                }
                return false;
            }
            if (Mathf.Abs(target.duration - duration) > StepSec){
                lines.Add(CombatValidator.WarnPrefix + "action duration " + target.duration + "s differs from this clip's frame-grid length "
                    + duration.ToString("0.###") + "s by more than one frame — the body clock and the animation will drift apart; windows are authored against the action duration");
            }
            return false;
        }

        ///<summary>
        ///这条轨在全部键上**沿世界 Y 盖到多高**。挂点的父链带着 bind 旋转时，盒的三个轴会跟着父链转：
        ///"1.7 米高"能烘成"1.83 米深"（第一份真受击盒就是这么躺下的——它只盖住 y∈[0.62,1.43]，
        ///于是 0.5 米高的平射子弹从那人腿底下穿过去，而几何上这一切完全合法、不报任何错）。
        ///这个数不进判定，只把"竖直方向到底吃了多少"摆到作者眼前。
        ///</summary>
        private static string VerticalSpan(ActionBoxTrackBase t){
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (ActionBoxKeyAsset k in t.keys){
                float v = Mathf.Abs(k.axisX.y) * k.half.x + Mathf.Abs(k.axisY.y) * k.half.y + Mathf.Abs(k.axisZ.y) * k.half.z;
                lo = Mathf.Min(lo, k.center.y - v);
                hi = Mathf.Max(hi, k.center.y + v);
            }
            return "vertical y in [" + lo.ToString("0.00") + ", " + hi.ToString("0.00") + "]";
        }

        ///<summary>
        ///一个挂载盒的攒键过程：与上一记的键比，动得够多才再记一帧（force 用于末帧）。
        ///存进去的轴已归一化、累积缩放乘进半边长。
        ///</summary>
        private class Akku
        {
            public readonly UnitActionBox box;
            public readonly List<ActionBoxKeyAsset> keys = new List<ActionBoxKeyAsset>();
            public bool degenerate;
            public bool referenced;   //有轨按 tag 认领了这个挂点（Unused mount 报表只点名没人认领的）
            private readonly Vector3 half;
            private Vector3 center, ax, ay, az;
            private bool has;

            public Akku(UnitActionBox box){
                this.box = box;
                half = box.size * 0.5f;
            }

            public void Offer(float t, Vector3 c, Vector3 x, Vector3 y, Vector3 z, bool force){
                float lx = x.magnitude, ly = y.magnitude, lz = z.magnitude;
                if (lx <= 0f || ly <= 0f || lz <= 0f){
                    degenerate = true;
                    return;
                }
                //先归一化再吸附：落网格的值同时用于"动没动"的判定与写盘，重烘的资产文本才位级一致
                t = Snap(t, 4);
                c = SnapVec(c, 3);
                x = SnapVec(x / lx, 4); y = SnapVec(y / ly, 4); z = SnapVec(z / lz, 4);
                Vector3 scaledHalf = new Vector3(Snap(half.x * lx, 3), Snap(half.y * ly, 3), Snap(half.z * lz, 3));
                if (has == false || force || Moved(c, x, y, z)){
                    keys.Add(new ActionBoxKeyAsset{
                        t = t, center = c,
                        axisX = x, axisY = y, axisZ = z,
                        half = scaledHalf
                    });
                }
                center = c; ax = x; ay = y; az = z;
                has = true;
            }

            private static Vector3 SnapVec(Vector3 v, int decimals){
                return new Vector3(Snap(v.x, decimals), Snap(v.y, decimals), Snap(v.z, decimals));
            }

            private bool Moved(Vector3 c, Vector3 x, Vector3 y, Vector3 z){
                if (Vector3.Distance(c, center) > KeepPosEps) return true;
                return AxisMoved(x, ax) || AxisMoved(y, ay) || AxisMoved(z, az);
            }

            private static bool AxisMoved(Vector3 a, Vector3 b){
                return (a - b).sqrMagnitude > KeepAxisEps * KeepAxisEps;
            }
        }
    }
}
