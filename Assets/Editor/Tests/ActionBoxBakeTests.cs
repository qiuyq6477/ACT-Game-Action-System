using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using CombatFramework;
using CombatFrameworkEditor;

namespace CombatFramework.Tests
{
    ///<summary>
    ///判定盒烘培的两条常驻用例，钉的都是"不报错但会骗人"那两件：
    ///
    ///① 同一份输入烘两次必须位级一致。不一致就是 keys 里留着没吸附到网格的浮点噪声，
    ///   症状是重烘一次 diff 满屏、资产文本尾数抖动，于是谁也不敢信资产里那份轨迹。
    ///② EditMode 下 `AnimationMode` 真的在驱动本工程这具人形骨骼。不驱动的症状是每个盒的位姿
    ///   都停在同一帧——线框照样画得出来、盒却一动不动，比直接抛错难发现得多
    ///   （`Animator.Play`/`Update` 在 EditMode 就不评估人形曲线，这正是烘培不走它的原因）。
    ///
    ///两条都只在内存里的临时 `ActionAsset` 上烘：不碰 `Assets/Tables/Actions/` 那 15 份，也不写快照。
    ///</summary>
    public class ActionBoxBakeTests
    {
        private const string PrefabPath = "Assets/Resources/Player.prefab";

        ///<summary>被测的动作：Punch1 的攻击轨是 LeftPunch（出拳手），RightPunch 是这一招里收着的那只手</summary>
        private const string ActionId = "Punch1";
        private const string PunchTag = "LeftPunch";
        private const string GuardTag = "RightPunch";

        ///<summary>受击侧的挂点 tag：两条 lane 都要烘到，不然拆成两个子类之后受击那条没人验过</summary>
        private const string HurtTag = "Hurt";

        ///<summary>出拳手的盒心在这招里至少要走这么多米（一拳的行程是几十厘米量级，3cm 只用来区分"动了"和"没动"）</summary>
        private const float MinPunchTravel = 0.03f;

        private readonly List<ActionAsset> scratch = new List<ActionAsset>();

        [TearDown]
        public void TearDown(){
            for (int i = 0; i < scratch.Count; i++) if (scratch[i] != null) Object.DestroyImmediate(scratch[i]);
            scratch.Clear();
        }

        private static GameObject LoadPrefab(){
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, "找不到 " + PrefabPath + "，烘培没有采样对象");
            return prefab;
        }

        ///<summary>
        ///按 animatorKey 找到要播的那条 clip：先按 clip 名（`animationClips` 已含 OverrideController 的覆盖结果），
        ///没有再按控制器里的状态名走 layers/states——状态名与 clip 名可以不同名，只查一边会在改控制器时莫名红掉。
        ///</summary>
        private static AnimationClip FindClip(GameObject prefab, string name){
            Animator animator = prefab.GetComponentInChildren<Animator>();
            Assert.That(animator, Is.Not.Null, PrefabPath + " 上没有 Animator");
            RuntimeAnimatorController rc = animator.runtimeAnimatorController;
            Assert.That(rc, Is.Not.Null, "Animator 没挂控制器，取不到 clip");
            AnimationClip[] clips = rc.animationClips;
            for (int i = 0; i < clips.Length; i++){
                if (clips[i] != null && clips[i].name == name) return clips[i];
            }
            var controller = rc as UnityEditor.Animations.AnimatorController;
            if (controller == null || controller.layers == null) return null;
            for (int l = 0; l < controller.layers.Length; l++){
                var machine = controller.layers[l].stateMachine;
                if (machine == null || machine.states == null) continue;
                for (int s = 0; s < machine.states.Length; s++){
                    var state = machine.states[s].state;
                    if (state == null || state.name != name) continue;
                    AnimationClip clip = state.motion as AnimationClip;
                    if (clip != null) return clip;
                    var tree = state.motion as UnityEditor.Animations.BlendTree;
                    if (tree == null || tree.children == null) continue;
                    for (int c = 0; c < tree.children.Length; c++){
                        clip = tree.children[c].motion as AnimationClip;
                        if (clip != null) return clip;
                    }
                }
            }
            return null;
        }

        ///<summary>临时资产：两条攻击轨 + 一条受击轨，只填 tag 和一整段窗，keys 归烘培填</summary>
        private ActionAsset NewScratchAsset(){
            ActionAsset a = ScriptableObject.CreateInstance<ActionAsset>();
            a.name = "BakeScratch";
            a.id = ActionId;
            a.animKey = ActionId;
            a.duration = 1f;
            a.attackBoxes = new[]{ NewTrack(PunchTag), NewTrack(GuardTag) };
            a.hitBoxes = new[]{ NewHitTrack(HurtTag) };
            scratch.Add(a);
            return a;
        }

        private static AttackBoxTrackAsset NewTrack(string tag){
            AttackBoxTrackAsset t = new AttackBoxTrackAsset();
            t.tag = tag;
            t.openMin = 0f;
            t.openMax = 1f;
            return t;
        }

        private static HitBoxTrackAsset NewHitTrack(string tag){
            HitBoxTrackAsset t = new HitBoxTrackAsset();
            t.tag = tag;
            t.openMin = 0f;
            t.openMax = 1f;
            return t;
        }

        private static string Report(List<string> lines){
            return lines == null || lines.Count == 0 ? "(烘培没有输出)" : "\n" + string.Join("\n", lines.ToArray());
        }

        private static ActionBoxKeyAsset[] KeysOf(ActionBoxTrackBase[] tracks, string tag){
            for (int i = 0; i < tracks.Length; i++){
                if (tracks[i] != null && tracks[i].tag == tag) return tracks[i].keys;
            }
            Assert.Fail("临时资产里没有 tag=" + tag + " 这条轨");
            return null;
        }

        ///<summary>键序列里盒心的位移跨度（三轴各取 max−min，再取最大的那一轴）</summary>
        private static float TravelSpan(ActionBoxKeyAsset[] keys){
            if (keys == null || keys.Length < 2) return 0f;
            Vector3 min = keys[0].center, max = keys[0].center;
            for (int i = 1; i < keys.Length; i++){
                min = Vector3.Min(min, keys[i].center);
                max = Vector3.Max(max, keys[i].center);
            }
            Vector3 d = max - min;
            return Mathf.Max(d.x, Mathf.Max(d.y, d.z));
        }

        private static bool Bake(GameObject prefab, AnimationClip clip, ActionAsset target, List<string> lines){
            return ActionBoxBake.Run(prefab, clip, target, true, lines);
        }

        [Test]
        public void Bake_TwoRuns_AreBitIdentical(){
            GameObject prefab = LoadPrefab();
            AnimationClip clip = FindClip(prefab, ActionId);
            Assert.That(clip, Is.Not.Null, "控制器里找不到 '" + ActionId + "' 这条 clip，烘培用例没有输入可比");

            ActionAsset first = NewScratchAsset();
            ActionAsset second = NewScratchAsset();
            List<string> l1 = new List<string>();
            List<string> l2 = new List<string>();
            Assert.IsTrue(Bake(prefab, clip, first, l1), "第一次烘就失败：" + Report(l1));
            Assert.IsTrue(Bake(prefab, clip, second, l2), "第二次烘就失败：" + Report(l2));

            ActionBoxKeyAsset[] keys = KeysOf(first.attackBoxes, PunchTag);
            Assert.That(keys, Is.Not.Null, "临时资产里没有 " + PunchTag + " 这条轨的 keys");
            Assert.That(keys.Length, Is.GreaterThan(0),
                "一条位姿都没烘到——那这条比的是两份空数据，绿得没有意义：" + Report(l1));
            Assert.That(KeysOf(first.hitBoxes, HurtTag).Length, Is.GreaterThan(0),
                "受击轨一个位姿都没烘到：两条 lane 各自烘的是子类上的字段，只验攻击那条等于受击那条没人管");
            string json = JsonUtility.ToJson(first);
            //轨拆成子类之后，位姿挂在基类字段上——先确认序列化器真的把它写进了文本，
            //否则下面这条"两份 JSON 相等"比的是两份空壳，怎么漂都绿
            Assert.IsTrue(json.Contains("\"keys\":[{"), "JsonUtility 没把 keys 写进 JSON，这条对比没有内容可比的：" + json);
            Assert.AreEqual(json, JsonUtility.ToJson(second),
                "同一份输入两次烘出来不一样：keys 里留着没吸附到网格的浮点噪声，"
                + "重烘一次 diff 就满屏（`ActionBoxBake.Snap` 的三条网格必须都盖上）");
        }

        [Test]
        public void Bake_PunchingHandTravels_AndGuardHandBarelyMoves(){
            GameObject prefab = LoadPrefab();
            AnimationClip clip = FindClip(prefab, ActionId);
            Assert.That(clip, Is.Not.Null, "控制器里找不到 '" + ActionId + "' 这条 clip");

            ActionAsset target = NewScratchAsset();
            List<string> lines = new List<string>();
            Assert.IsTrue(Bake(prefab, clip, target, lines), "烘培失败：" + Report(lines));

            ActionBoxKeyAsset[] punch = KeysOf(target.attackBoxes, PunchTag);
            Assert.That(punch, Is.Not.Null, "临时资产里没有 " + PunchTag + " 这条轨的 keys");
            Assert.That(punch.Length, Is.GreaterThanOrEqualTo(2),
                "出拳手只烘出 " + punch.Length + " 个位姿：EditMode 没在驱动骨骼，"
                + "整条轨停在同一帧（烘培只认 '动了 2cm 才记一帧'，一个键都不加就是它压根没动）" + Report(lines));

            float punchSpan = TravelSpan(punch);
            float guardSpan = TravelSpan(KeysOf(target.attackBoxes, GuardTag));
            Assert.That(punchSpan, Is.GreaterThanOrEqualTo(MinPunchTravel),
                PunchTag + " 的盒心在 " + clip.name + " 里只走了 " + punchSpan.ToString("0.###") + " 米——"
                + "位姿没跟上动画，线框会画出一只钉在原地的拳头");
            //护臂那只手到底走多少取决于这招怎么设计，这里只断言它不比出拳手走得远：
            //真按"几乎不动"的阈值钉死，改一版动画就得跟着改测试，那是把设计写进了判据
            Assert.That(guardSpan, Is.LessThanOrEqualTo(punchSpan),
                "收着的那只手（" + GuardTag + "）走出 " + guardSpan.ToString("0.###") + " 米，比出拳手还远，"
                + "采样给的不是这一招的姿势");
        }
    }
}
