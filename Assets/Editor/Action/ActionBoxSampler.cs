using System.Collections.Generic;
using UnityEngine;

namespace CombatFramework
{
    ///<summary>
    ///一条盒轨道在某一时刻的位姿（**单位局部系**：相对 prefab 根、按朝向归零）。
    ///要去世界判定就调 <see cref="ToWorldObb"/>。
    ///</summary>
    public readonly struct ActionBoxPose
    {
        public readonly string tag;
        public readonly Vector3 center;
        public readonly Vector3 axisX;
        public readonly Vector3 axisY;
        public readonly Vector3 axisZ;
        public readonly Vector3 half;

        public ActionBoxPose(ActionBoxTrack track, Vector3 center, Vector3 axisX, Vector3 axisY, Vector3 axisZ, Vector3 half){
            this.tag = track.tag;
            this.center = center;
            this.axisX = axisX;
            this.axisY = axisY;
            this.axisZ = axisZ;
            this.half = half;
        }

        public ActionBoxPose(ActionBoxTrack track, ActionBoxKey key)
            : this(track, key.center, key.axisX, key.axisY, key.axisZ, key.half){
        }

        ///<summary>
        ///旋到世界：绕 Y 转朝向（0°=+Z、90°=+X），再平移到单位逻辑位。
        ///这条工程只有水平朝向——面向的真源是 `InputToCommand.inversed`，落地交给 `FacingApply`
        ///（3D 模型绕 Y 转 90/270、精灵翻 localScale.x），判定盒没有需要烘的俯仰，所以这里不接 pitch。
        ///与 `Quaternion.Euler(0, degree, 0)` 逐位相同，由 `ToWorldObb_MatchesQuaternionEuler` 钉住：
        ///线框画的是它、身体摆的也是它，两者必须指同一个地方。
        ///</summary>
        public Obb ToWorldObb(Vector3 unitPos, float degree){
            float yaw = degree * Mathf.Deg2Rad;
            float sy = Mathf.Sin(yaw);
            float cy = Mathf.Cos(yaw);
            return new Obb(RotateY(center, sy, cy) + unitPos,
                           RotateY(axisX, sy, cy),
                           RotateY(axisY, sy, cy),
                           RotateY(axisZ, sy, cy),
                           half.x, half.y, half.z);
        }

        public override string ToString(){
            return "BoxPose(" + tag + " @ " + center + " half=" + half + ")";
        }

        //绕 Y 转：x' = x·c + z·s，z' = z·c − x·s（与 Unity 的 Quaternion.Euler(0,d,0)*v 同向）
        private static Vector3 RotateY(Vector3 v, float s, float c){
            return new Vector3(v.x * c + v.z * s, v.y, v.z * c - v.x * s);
        }
    }

    ///<summary>
    ///盒轨道求值：算出"这一招的这一刻，这个盒占的是哪一块"。
    ///
    ///数据是编辑期逐逻辑帧烘的（`Editor/ActionBoxBake.cs`，入口在动作时间轴窗口），这里只做三件事：
    ///① 判窗（复用 <see cref="ActionRules.WindowOpen"/>，全仓库只有一条窗规则）；
    ///② 在键表里**区间扫**出包围当前时刻的那一对键；
    ///③ 两键之间线性插值。
    ///
    ///两个刻意的口径：
    ///- **时刻落在键表之外就夹到端点**，不判"没有盒"。首键是这招起手时的真实位姿，身体时钟从 0
    ///  起步时本就在那儿；键表短于动作时长属烘培覆盖不足，夹住比凭空消失好查。
    ///- **插值后的三轴不再严格单位长**（两单位向量的线性插值短于 1，误差是夹角的平方量级）。
    ///  归一化要开方，而逐帧逐盒做它等于把成本从编辑期搬进热路径；烘培步长是一个逻辑帧，
    ///  相邻键夹角极小，所以不补。两端算的是同一串浮点乘加（同序、无并行归约），确定性不受影响。
    ///</summary>
    public static class ActionBoxSampler
    {
        ///<summary>
        ///单条轨：窗开着且有形状才谈位姿。
        ///<param name="was">本帧起点的动作钟时刻（与 now 相同=瞬时判据）</param>
        ///<param name="now">本帧终点的动作钟时刻（位姿按它取）</param>
        ///</summary>
        public static bool TrySample(ActionBoxTrack track, float was, float now, out ActionBoxPose pose){
            pose = default(ActionBoxPose);
            if (IsOpen(track, was, now) == false) return false;
            pose = At(track, now);
            return true;
        }

        ///<summary>
        ///时刻是否落在这一条轨的开启窗内（**不算位姿**的轻量判据，给"先筛再插"的调用方省一次扫描）。
        ///没有键的轨一律判不开——开着窗但没有形状，等于永远判不到东西。
        ///</summary>
        public static bool IsOpen(ActionBoxTrack track, float was, float now){
            if (track.keys == null || track.keys.Length == 0) return false;
            return ActionRules.WindowOpen(track.openMin, track.openMax, was, now);
        }

        ///<summary>
        ///这一条轨在 <paramref name="now"/> 的位姿（**不判窗**：调用方已经知道它开着，或者就是要看窗外形状）。
        ///</summary>
        public static ActionBoxPose At(ActionBoxTrack track, float now){
            ActionBoxKey[] keys = track.keys;
            if (keys == null || keys.Length == 0) return default(ActionBoxPose);
            if (now <= keys[0].t) return new ActionBoxPose(track, keys[0]);
            int last = keys.Length - 1;
            if (now >= keys[last].t) return new ActionBoxPose(track, keys[last]);

            //区间扫：键表按 t 升序（烘培保证），k = 最后一支 keys[k].t <= now 的键。
            //键数是个位数到十几，线性扫即可——二分省不下什么，还得为"now 恰好等于末键"多写一个分支。
            int k = 0;
            for (int i = 1; i < last; i++){
                if (keys[i].t > now) break;
                k = i;
            }
            ActionBoxKey a = keys[k];
            ActionBoxKey b = keys[k + 1];
            float span = b.t - a.t;
            float u = span > 0f ? (now - a.t) / span : 0f;
            return new ActionBoxPose(track,
                Vector3.Lerp(a.center, b.center, u),
                Vector3.Lerp(a.axisX, b.axisX, u),
                Vector3.Lerp(a.axisY, b.axisY, u),
                Vector3.Lerp(a.axisZ, b.axisZ, u),
                Vector3.Lerp(a.half, b.half, u));
        }

        ///<summary>
        ///一组轨：把这一刻开着的都收进 <paramref name="into"/>（表序=资产的轨道序，两端同序）。
        ///返回值=收了几条。窗口自己走 `TrySample`（`CollectOpenBoxes`），这条目前没人调；
        ///将来做盒对盒相交测试时，它就是把"这一刻开着的盒"一次捞干净的那只手。
        ///</summary>
        public static int SampleAll(ActionBoxTrack[] tracks, float was, float now, List<ActionBoxPose> into){
            if (tracks == null || into == null) return 0;
            int n = 0;
            for (int i = 0; i < tracks.Length; i++){
                ActionBoxPose pose;
                if (TrySample(tracks[i], was, now, out pose) == false) continue;
                into.Add(pose);
                n++;
            }
            return n;
        }
    }
}
