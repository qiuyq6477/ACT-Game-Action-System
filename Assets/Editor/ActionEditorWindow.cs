using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CombatFramework;

namespace CombatFrameworkEditor
{
    ///<summary>
    ///动作编辑窗口：一条**身体时钟**轴 + 六条区间 lane（Cancel Out / Cancel In / Temp Cancel In / Move Accept / Attack Boxes / Hit Boxes）。
    ///横轴是"这具身体的动作钟走到第几秒"，配的是**区间**（窗、段、接手点），而且要看到对方的动作名。
    ///
    ///两条 lane 的读数口径不同，画出来刻意区分：
    ///- Cancel Out：条从 `startFrom` 画到动作末尾——前面那段是被跳过的（切进来的人不吃自己的前摇），
    ///  条头那个点就是"从第几秒接手"，标签列出这个 tag 在全库能接下的动作。
    ///- Cancel In：条就是 [windowMin, windowMax) 这段窗，标签列出**谁**能在这段里把本动作交出去、
    ///  以及对方从它自己的第几秒接手。
    ///- Temp Cancel In：`duration` 与动作进度无关（点亮后独立存活若干逻辑帧），所以条画在 [0, duration) 上，
    ///  读的是长度不是位置。
    ///- Move Accept：按 rate 着色（0=红=定住脚，1=绿=照常走）；重叠段取小，画成红框——运行期不报错，
    ///  但两段能重叠基本就是配表事故。
    ///- Attack Boxes / Hit Boxes：**这一招用盒的实例表**，生命周期归本窗口——"+"建空轨、选中可删、
    ///  窗可拖（吸附一逻辑帧）、命中规则在明细面板配；`tag` 指向 prefab 上的 `UnitActionBox` 挂点，
    ///  跟骨骼的 `keys` 由烘培按 tag 填入（键数标签标红=还没填）。同一 tag 可以建多条轨，各开各的窗。
    ///
    ///视口与预览（公共件都在 `TimelineGraphGUI`，禁再抄）：
    ///- 滚轮=以鼠标为锚缩放、Shift+滚轮/中键拖拽=平移；块拖拽整块平移、拖两端改边界（吸附一逻辑帧）。
    ///- 走带（重置/上一帧/播放/下一帧/到尾）驱动一条播放头；播放头时刻开着的窗在条上提亮。
    ///- Caster 预览（编辑器演示用，不落盘）：给一个带 Animator 的视觉预制体 + 一条 clip，
    ///  播放头走到哪就 `AnimationMode.SampleAnimationClip` 到哪（Edit Mode 下唯一能评估人形曲线的采样口，
    ///  与 `ActionBoxBake` 同一条路）；同时用 `ActionBoxSampler` 在播放头时刻采样烘好的盒，Scene 视图画线框
    ///  （攻击=红、受击=蓝）——"窗与判定盒跟动画对不对得上"当场看得见。
    ///
    ///拖拽吸附粒度 = 一个逻辑帧（不是 Timeline 的 0.05s）：窗边界落在两帧中间时"第几帧开窗"要靠
    ///累加误差决定，那正是对拍最难查的一类分叉，所以在编辑期就把它吸到帧上。
    ///`clipLength` 服务百分比模式的换算与显示，不落盘、不进运行时模型。
    ///</summary>
    public class ActionEditorWindow : EditorWindow
    {
        private const int LaneOut = 0;
        private const int LaneIn = 1;
        private const int LaneTemp = 2;
        private const int LaneMove = 3;
        ///<summary>盒轨两条 lane 的 id（public：EntryEdge 的"哪条 lane 接哪个字段"判据被包内测钉住）</summary>
        public const int LaneAtk = 4;
        public const int LaneHit = 5;
        private const int LaneCount = 6;
        private static readonly string[] LaneLabel = { "Cancel Out", "Cancel In", "Temp Cancel In", "Move Accept", "Attack Boxes", "Hit Boxes" };

        public const float NameGutter = 96f;  ///<summary>lane 名占的左栏宽度（图区从它之后开始）</summary>
        private const float EdgeGrab = 10f;    ///<summary>距条头/条尾多少像素内算抓住那一头（也是光标反馈的热区）</summary>
        private const float BodyPad = 3f;      ///<summary>画出来的条向左右各扩的像素</summary>

        ///<summary>拖拽吸附粒度=一个逻辑帧。数本身只有一个主人：`ActionBoxBake.StepSec`（采样步长、
        ///量化粒度、吸附粒度是同一条帧格，各定一份迟早会漂出两套"什么叫做对齐"）</summary>
        private static readonly float FrameSec = ActionBoxBake.StepSec;

        private static readonly Color AttackBoxColor = new Color(1f, 0.35f, 0.3f, 0.95f);
        private static readonly Color HitBoxColor = new Color(0.35f, 0.6f, 1f, 0.95f);

        private ActionAsset asset;
        private SerializedObject so;
        private readonly List<ActionAsset> library = new List<ActionAsset>();
        private List<string> bakeReport;   //最近一次烘培的逐行报表（本窗口内置烘培的输出区）

        private float clipLength;        private bool percentMode;
        ///<summary>
        ///单位开关：本工程的动作表以逻辑帧为真源（`LogicClock.Tick` = 1/60 秒一格，见 `Assets/Scripts/AGENTS.md`
        ///的"时长一律 int 逻辑帧"），所以**默认按帧显示**；切到秒是给对动画时长的人用的。
        ///两者都只是显示外壳——`ActionAsset` 里落盘的永远是秒（见 `ActionAsset` 的类注释），
        ///帧↔秒的唯一换算点是本文件的 `FrameSec`（= `ActionBoxBake.StepSec` = `LogicClock.Tick`）。
        ///唯一不受这个开关管的是标尺刻度：`TimelineGraphGUI.DrawRuler` 的步长取"整齐的"1/2/5×10ⁿ，
        ///天生按秒走，换成帧格要动被视口用例钉着的公共换算，收益不值（记在 `Doc/plan/action-editor.md` 的"已知限制"）。
        ///</summary>
        private bool frameMode = true;
        private int selLane = -1;
        private int selIndex = -1;
        private Vector2 scroll;
        private List<string> issues;

        //拖拽状态
        private bool dragging;
        private int dragLane, dragIndex, dragPart;   //part: 0=整体 1=条头 2=条尾
        private float dragGrabSec;

        //悬停反馈（边热区高亮 + 左右箭头光标）
        private bool hoverValid;
        private Bar hoverBar;
        private int hoverRow = -1, hoverPart;

        //视口状态（编辑器自有，不落盘）：缩放存"相对铺满"的倍率（可小于 1，下限 1/ZoomOutMax），
        //窗口宽度变化时视图跟着铺满比例走
        private float viewStart;
        private float zoom = 1f;
        private bool panDrag;
        private Rect lastGraphRect;   //上一帧的图区矩形：滚轮要赶在外层 ScrollView 前接走（见 OnGUI）

        //Caster 预览（编辑器演示用，不落盘——与 clipLength 同类的窗口字段）。
        //clip 不给手动选：它必须就是运行期这一招会播的动画，手选错一条，预览与烘好的判定盒就对不上。
        //所以只从 animKey 反查 Animator 控制器（运行期 `anim.CrossFade(animKey)` 用的就是这个名字），查到哪条播哪条。
        private GameObject casterPrefab;
        private GameObject casterInstance;
        private AnimationClip previewClip;
        private string lastResolveKey;
        private float playhead;
        private float lastSampledPlayhead = -1f;   //上次实采的播放头：判断"顺向小步"还是"跳变需重放"（人形采样有状态）
        private bool previewPlaying;
        private double lastFrameTime;
        private ActionModel previewModel;
        private bool previewModelValid;
        private bool animationModeOwned;

        ///<summary>一条可看可拖的区间条（head/tail 为 null 表示那一头不属于本条目、不可拖）</summary>
        private struct Bar
        {
            public int lane, index;
            public SerializedProperty head, tail;
            public float start, end;
            public string label;
            public Color color;
            public bool bad;
        }

        ///<summary>一条 lane 在整张图里占的行区间</summary>
        public struct LaneBand
        {
            public int rowStart;
            public int rows;
        }

        ///<summary>盒轨道一条 lane 上的一根条（读的是资产，不碰 SerializedObject）</summary>
        public struct BoxRow
        {
            public string label;
            public float start, end;
            public Color color;
            public bool bad;
        }

        ///<summary>播放头时刻开着的盒（SceneView 线框与包测共用的读数）</summary>
        public struct BoxPreview
        {
            public ActionBoxPose pose;
            public bool attack;
        }

        [MenuItem("Combat Framework/Action Timeline", priority = 2001)]
        public static ActionEditorWindow OpenDefault(){
            var w = GetWindow<ActionEditorWindow>("Action Timeline");
            w.minSize = new Vector2(720, 460);
            w.Show();
            return w;
        }

        public static void Open(ActionAsset asset){
            var w = OpenDefault();
            w.Load(asset);
        }

        private void Load(ActionAsset a){
            asset = a;
            so = a != null ? new SerializedObject(a) : null;
            selLane = selIndex = -1;
            issues = null;
            bakeReport = null;
            viewStart = 0f;
            zoom = 1f;
            previewPlaying = false;
            playhead = 0f;
            lastSampledPlayhead = -1f;   //新实例/新资产：采样历史作废，下次采样从 0 重放
            RefreshLibrary();
            Repaint();
        }

        private void OnEnable(){
            if (asset != null) Load(asset);
            EditorApplication.update += OnEditorUpdate;
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            //自愈：域重载后窗口字段里的 caster 引用还在（Object 可序列化）而场景实例可能已丢——重建并重新采样
            if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode
                && casterPrefab != null && casterInstance == null){
                EnsureCaster();
                ResolvePreviewClip();
                SampleCaster();
            }
        }

        private void OnDisable(){
            EditorApplication.update -= OnEditorUpdate;
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            previewPlaying = false;
            DestroyCaster();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state){
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredPlayMode){
                previewPlaying = false;
                DestroyCaster();
            }
        }

        ///<summary>走带的"播放"一档：真 dt 从 timeSinceStartup 的差里来，到尾循环；顺手把 caster 采到新时刻</summary>
        private void OnEditorUpdate(){
            if (previewPlaying == false) return;
            if (EditorApplication.isPlaying){
                previewPlaying = false;
                return;
            }
            double now = EditorApplication.timeSinceStartup;
            float dt = lastFrameTime > 0.0 ? (float)(now - lastFrameTime) : 0f;
            lastFrameTime = now;
            if (dt <= 0f) return;
            float dur = Mathf.Max(TimelineGraphGUI.MinDuration, asset != null ? asset.duration : 0f);
            playhead = TimelineGraphGUI.AdvancePlayhead(playhead, true, dt, dur);
            SampleCaster();
            Repaint();
        }

        ///<summary>
        ///全库=工程里所有 ActionAsset（编辑期口径）。不读运行时注册表：那只存在于 Play 中，
        ///而且注册会覆盖同 id，编辑期要看到的是"有哪几份、写了什么"。
        ///</summary>
        private void RefreshLibrary(){
            library.Clear();
            foreach (string guid in AssetDatabase.FindAssets("t:ActionAsset")){
                ActionAsset found = AssetDatabase.LoadAssetAtPath<ActionAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (found != null && found != asset) library.Add(found);
            }
        }

        private void OnGUI(){
            bool playing = TimelineGraphGUI.DrawPlayModeWarning();

            EditorGUILayout.BeginHorizontal();
            ActionAsset picked = (ActionAsset)EditorGUILayout.ObjectField("Action Asset", asset, typeof(ActionAsset), false);
            if (picked != asset) Load(picked);
            if (asset != null && GUILayout.Button("Refresh Library", GUILayout.Width(110))) RefreshLibrary();
            if (asset != null && GUILayout.Button("Ping in Inspector", GUILayout.Width(110))){
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }
            EditorGUILayout.EndHorizontal();

            if (asset == null){
                EditorGUILayout.HelpBox("Select an Action asset first (right-click in the Project window → Create/Combat Framework/Action to create one)", MessageType.Info);
                return;
            }
            if (so == null) Load(asset);   //自愈：域重载后 SerializedObject 是半初始化状态

            so.Update();
            //外层 ScrollView 内容超高时会自己吃掉滚轮，图区的缩放/平移就永远收不到——
            //赶在它前面用上一帧的图区矩形把滚轮/中键接走（首帧矩形为空自动跳过；事件已 Used 时下面的调用是 no-op）
            TimelineGraphGUI.HandleViewInput(lastGraphRect, Mathf.Max(TimelineGraphGUI.MinDuration, asset.duration),
                ref viewStart, ref zoom, ref panDrag);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawTransportBar();
            DrawHeader(playing);
            DrawLaneToolbar(playing);
            DrawGraph(playing);
            DrawDetail(playing);
            DrawValidation(playing);
            DrawBakeReport();
            EditorGUILayout.EndScrollView();
            if (!playing) so.ApplyModifiedProperties();
            EnsureCaster();
            bool clipChanged = ResolvePreviewClip();
            RefreshPreviewModel();
            if (clipChanged) SampleCaster();
        }

        //──────────── 走带 + Caster 预览 ────────────

        ///<summary>
        ///走带与预览输入区。caster/clip 都是编辑器演示字段：caster 赋值即建实例（清空即销毁），
        ///clip 兼任百分比模式的换算基准。改动播放头的路径全部收口到 <see cref="SeekTo"/>，那里负责采样。
        ///</summary>
        private void DrawTransportBar(){
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            float dur = Mathf.Max(TimelineGraphGUI.MinDuration, asset.duration);
            //读数只显示当前选中的那一种单位：同一时刻并排两个数就会出现两份"这个窗有多长"
            string readout = frameMode
                ? "f" + Mathf.RoundToInt(playhead / FrameSec)
                : playhead.ToString("0.###") + "s";
            if (TimelineGraphGUI.DrawTransport(ref playhead, ref previewPlaying, FrameSec, dur, readout)){
                lastFrameTime = 0.0;
                SeekTo(playhead);
                Repaint();
            }

            EditorGUILayout.BeginHorizontal();
            GameObject pickedCaster = (GameObject)EditorGUILayout.ObjectField("Caster (preview)", casterPrefab, typeof(GameObject), false);
            if (pickedCaster != casterPrefab){
                DestroyCaster();
                casterPrefab = pickedCaster;
                lastResolveKey = null;
                lastSampledPlayhead = -1f;
                EnsureCaster();
            }
            if (previewClip != null){
                EditorGUILayout.LabelField("Clip: " + previewClip.name + " (from animKey '" + (asset != null ? asset.animKey : "") + "')", EditorStyles.miniLabel);
            }else{
                EditorGUILayout.LabelField(PreviewClipHint(), EditorStyles.miniLabel);
            }
            GUILayout.FlexibleSpace();
            //视图+走带复位：播放头回 0 停播、缩放/平移回到"整招铺满"。视图状态是本窗口自有字段，
            //所以按钮在这而不是共享走带件里（TimelineGraphGUI 禁长本窗口的私货）
            if (GUILayout.Button("Reset View", GUILayout.Width(80))){
                SeekTo(0f);
                previewPlaying = false;
                lastFrameTime = 0.0;
                viewStart = 0f;
                zoom = 1f;
                Repaint();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            frameMode = EditorGUILayout.ToggleLeft(new GUIContent("Frame Units (1 逻辑帧 = LogicClock.Tick)",
                "开：所有时间字段与走带读数按逻辑帧显示。关：按秒显示。\n"
                + "两种都只是显示外壳，落盘的永远是秒；标尺刻度不受这个开关影响，恒为秒。"), frameMode, GUILayout.MinWidth(110));
            clipLength = EditorGUILayout.FloatField("clipLength (s)", clipLength, GUILayout.MinWidth(100));
            percentMode = EditorGUILayout.Toggle("Percent Mode", percentMode);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying || casterPrefab == null || previewClip == null)){
                if (GUILayout.Button("Bake Keys from Caster Mounts", GUILayout.Width(190))) BakeNow();
            }
            EditorGUILayout.LabelField(BakeHint(), EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        ///<summary>
        ///本窗口内置的烘培（输入就用眼前这三样：caster prefab + animKey 反查出的 clip + 当前资产）。
        ///只往已有轨填 keys，窗/规则/轨增删一概不碰；setDurationToClip 恒 true——帧格化时长是仓库契约。
        ///先 Apply 冲掉待写编辑（烘培直接写资产字段，两份真相不能叠加），烘完刷新预览让新键立刻可见。
        ///</summary>
        private void BakeNow(){
            so.ApplyModifiedProperties();
            List<string> lines = new List<string>();
            if (ActionBoxBake.Run(casterPrefab, previewClip, asset, true, lines)){
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
            }
            bakeReport = lines;
            RefreshPreviewModel();
            SampleCaster();
            Repaint();
        }

        ///<summary>烘培按钮旁的状态行：三个输入哪个没就绪，说清楚缺哪件</summary>
        private string BakeHint(){
            if (EditorApplication.isPlaying) return "editing disabled in play mode";
            if (casterPrefab == null) return "assign a caster prefab — its UnitActionBox mounts are the bake source";
            if (previewClip == null) return "clip not resolved (see the hint above) — bake samples the action's own clip";
            return "fills pose keys into this action's existing tracks; windows and hit rules are untouched";
        }

        ///<summary>预览 clip 解析不出时的原因行（只影响动画采样，盒预览与走带照常）</summary>
        private string PreviewClipHint(){
            if (casterPrefab == null) return "assign a caster prefab to preview";
            string animKey = asset != null ? asset.animKey : "";
            if (string.IsNullOrEmpty(animKey)) return "action has no animKey — nothing to play";
            return "clip for animKey '" + animKey + "' not found in the Animator controller (clip 名或状态名)";
        }

        ///<summary>animKey → 动画 clip 的编辑器反查：运行期就是 `anim.CrossFade(animKey)`（`ActionController`），
        ///预览要采样才需要拿到 clip 本体，所以按同一个名字去 Animator 控制器里找——先 clip 名（含
        ///OverrideController 的覆盖结果），没有再按状态名找。以 (caster, asset, animKey) 为缓存键，不变不重查。
        ///返回 true = 解析到了 clip（`previewClip != null`，调用方据此采样）。</summary>
        private bool ResolvePreviewClip(){
            string animKey = asset != null ? asset.animKey : "";
            string key = (casterPrefab != null ? casterPrefab.GetInstanceID().ToString() : "0")
                + "|" + (asset != null ? asset.GetInstanceID().ToString() : "0") + "|" + animKey;
            if (key == lastResolveKey) return false;
            lastResolveKey = key;
            previewClip = null;
            if (casterPrefab == null || string.IsNullOrEmpty(animKey)) return false;

            previewClip = FindControllerClip(casterPrefab, animKey);
            if (previewClip != null) clipLength = previewClip.length;
            return previewClip != null;
        }

        ///<summary>控制器里找动画：先按 clip 名（animationClips 已含 OverrideController 的覆盖结果），
        ///没有再按状态名走 layers/states（`Animator.Play` 认的是状态名，两者可能不同名）</summary>
        private static AnimationClip FindControllerClip(GameObject prefab, string animName){
            Animator animator = prefab.GetComponentInChildren<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null) return null;
            AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
            for (int i = 0; i < clips.Length; i++){
                if (clips[i] != null && clips[i].name == animName) return clips[i];
            }
            var ac = animator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
            if (ac == null || ac.layers == null) return null;
            for (int l = 0; l < ac.layers.Length; l++){
                UnityEditor.Animations.AnimatorControllerLayer layer = ac.layers[l];
                if (layer.stateMachine == null || layer.stateMachine.states == null) continue;
                for (int s = 0; s < layer.stateMachine.states.Length; s++){
                    if (layer.stateMachine.states[s].state == null
                        || layer.stateMachine.states[s].state.name != animName) continue;
                    Motion m = layer.stateMachine.states[s].state.motion;
                    var clip = m as AnimationClip;
                    if (clip != null) return clip;
                    var tree = m as UnityEditor.Animations.BlendTree;
                    if (tree == null || tree.children == null) continue;
                    for (int c = 0; c < tree.children.Length; c++){
                        clip = tree.children[c].motion as AnimationClip;
                        if (clip != null) return clip;
                    }
                }
            }
            return null;
        }

        ///<summary>改播放头的唯一入口：夹进时长后把 caster 采样到该时刻</summary>
        private void SeekTo(float t){
            playhead = Mathf.Clamp(t, 0f, Mathf.Max(TimelineGraphGUI.MinDuration, asset != null ? asset.duration : 0f));
            SampleCaster();
        }

        ///<summary>caster 实例跟着字段走：有 prefab 没实例就建（原点、identity）， prefab 清空/被删就销毁</summary>
        private void EnsureCaster(){
            if (casterPrefab == null){
                if (casterInstance != null) DestroyCaster();
                return;
            }
            if (casterInstance != null) return;
            CreateCasterInstance();
            SampleCaster();
        }

        ///<summary>只负责建实例（不采样）：跳拍重建路径也走这里，避免 EnsureCaster→SampleCaster→EnsureCaster 的递归</summary>
        private void CreateCasterInstance(){
            casterInstance = PrefabUtility.InstantiatePrefab(casterPrefab) as GameObject;
            if (casterInstance == null) casterInstance = Object.Instantiate(casterPrefab);   //非资产来源兜底
            casterInstance.name = casterPrefab.name + " (ActionPreview)";
            casterInstance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        }

        private void DestroyCaster(){
            if (casterInstance != null){
                Object.DestroyImmediate(casterInstance);
                casterInstance = null;
            }
            if (animationModeOwned && AnimationMode.InAnimationMode()){
                AnimationMode.StopAnimationMode();
            }
            animationModeOwned = false;
            SceneView.RepaintAll();
        }

        ///<summary>
        ///把 clip 采到播放头时刻。用编辑期的动画采样 API，不是 Animator.Play/Update：
        ///后者在 EditMode 不评估人形曲线（`ActionBoxBake` 那条注释与测试就是为拦这个写的）。
        ///
        ///人形采样是**有状态**的：根运动的积分依赖采样历史，跳拍直接采会得到错误姿态（身体转到一个
        ///莫名的朝向，盒线框就此脱离拳头）。所以播放头跳变（拖标尺/点步进/切资产）时从 0 按帧距重放
        ///到目标拍——复刻连续播放会经历的采样历史；顺向小步（播放中）直接采当前拍。keys 是根相对
        ///坐标、对根的朝向不变，只要采样历史与烘培一致，身体和盒线框就永远在同一帧系里。
        ///</summary>
        private void SampleCaster(){
            if (casterInstance == null || previewClip == null) return;
            Animator animator = casterInstance.GetComponentInChildren<Animator>();
            if (animator == null) return;
            if (!AnimationMode.InAnimationMode()){
                AnimationMode.StartAnimationMode();
                animationModeOwned = true;
            }
            float target = Mathf.Clamp(playhead, 0f, previewClip.length);
            bool jumped = target < lastSampledPlayhead - FrameSec * 0.5f
                || target > lastSampledPlayhead + FrameSec * 1.5f;
            if (jumped){
                //人形采样状态跟着实例走（根运动积分按历史累积），跳拍重放救不回一个脏实例——
                //重建一个新实例再从 0 顺序采，复刻烘培的采样条件（新鲜实例 + 逐帧顺序），姿态才与 keys 同源
                DestroyCaster();
                CreateCasterInstance();
                animator = casterInstance.GetComponentInChildren<Animator>();
                if (!AnimationMode.InAnimationMode()){      //DestroyCaster 把动画模式一并停了，重放前得再开
                    AnimationMode.StartAnimationMode();
                    animationModeOwned = true;
                }
                int steps = Mathf.RoundToInt(target / FrameSec);
                for (int i = 0; i <= steps; i++){
                    SampleClipAt(animator, Mathf.Min(i * FrameSec, previewClip.length));
                }
                lastSampledPlayhead = target;
                SceneView.RepaintAll();
                return;
            }
            SampleClipAt(animator, target);
            lastSampledPlayhead = target;
            SceneView.RepaintAll();
        }

        private void SampleClipAt(Animator animator, float t){
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(animator.gameObject, previewClip, t);
            AnimationMode.EndSampling();
        }

        ///<summary>盒采样要的运行时模型：从资产现转一份（不完整资产转不出来时盒预览退场，其余照常）</summary>
        private void RefreshPreviewModel(){
            previewModelValid = false;
            if (asset == null) return;
            try{
                previewModel = CombatDataBuilder.ToModel(asset);
                previewModelValid = true;
            }catch (System.Exception){
                //资产还没配齐（没 id / 时长非正等）：盒预览退场，时间轴与动画预览照常
            }
        }

        ///<summary>播放头时刻开着的盒（窗与位姿全走 `ActionBoxSampler` 纯函数）。public static：SceneView 里
        ///画得对不对有客观判据（开窗与位姿），抽成出口才能被包内测钉住（同 `LayoutLanes` 的理由）。</summary>
        public static List<BoxPreview> SamplePreviewBoxes(ActionModel model, float time){
            List<BoxPreview> open = new List<BoxPreview>();
            CollectOpenBoxes(model.attackBoxes, time, true, open);
            CollectOpenBoxes(model.hitBoxes, time, false, open);
            return open;
        }

        private static void CollectOpenBoxes(ActionBoxTrack[] tracks, float t, bool attack, List<BoxPreview> into){
            if (tracks == null) return;
            for (int i = 0; i < tracks.Length; i++){
                ActionBoxPose pose;
                if (ActionBoxSampler.TrySample(tracks[i], t, t, out pose)){
                    into.Add(new BoxPreview{ pose = pose, attack = attack });
                }
            }
        }

        ///<summary>Scene 视图的判定盒线框：caster 摆在原点、朝向 0°，位姿是单位局部系，直接旋回世界即可</summary>
        private void OnSceneGUI(SceneView sceneView){
            if (casterInstance == null || previewModelValid == false || Application.isPlaying) return;
            List<BoxPreview> open = SamplePreviewBoxes(previewModel, playhead);
            //盒画在 caster 根的当前帧里（人形采样的根运动会挪动/转动根）：keys 是根相对坐标，
            //跟身体用同一个根帧，盒线框才贴得住拳头——根被积分到哪里，盒就跟到哪里
            Vector3 rootPos = casterInstance.transform.position;
            float rootDeg = casterInstance.transform.eulerAngles.y;
            for (int i = 0; i < open.Count; i++){
                DrawWireObb(open[i].pose, open[i].attack ? AttackBoxColor : HitBoxColor, rootPos, rootDeg);
            }
        }

        private static void DrawWireObb(ActionBoxPose pose, Color color, Vector3 rootPos, float rootDeg){
            //烘出来的位姿是"朝向归零"的单位局部系，这里只补根节点那一个水平转角
            Obb obb = pose.ToWorldObb(rootPos, rootDeg);
            Vector3 c = obb.center;
            Vector3 ax = obb.axisX * obb.halfX;
            Vector3 ay = obb.axisY * obb.halfY;
            Vector3 az = obb.axisZ * obb.halfZ;
            Vector3 p000 = c - ax - ay - az, p100 = c + ax - ay - az, p110 = c + ax + ay - az, p010 = c - ax + ay - az;
            Vector3 p001 = c - ax - ay + az, p101 = c + ax - ay + az, p111 = c + ax + ay + az, p011 = c - ax + ay + az;
            Handles.color = color;
            Handles.DrawAAPolyLine(2f, p000, p100, p110, p010, p000);
            Handles.DrawAAPolyLine(2f, p001, p101, p111, p011, p001);
            Handles.DrawAAPolyLine(2f, p000, p001);
            Handles.DrawAAPolyLine(2f, p100, p101);
            Handles.DrawAAPolyLine(2f, p110, p111);
            Handles.DrawAAPolyLine(2f, p010, p011);
            Handles.Label(c + ay, pose.tag, EditorStyles.miniLabel);
        }

        //──────────── 头部：基础字段 + 输入 + 百分比模式 ────────────

        private void DrawHeader(bool playing){
            using (new EditorGUI.DisabledScope(playing)){
                EditorGUILayout.PropertyField(so.FindProperty("id"));

                EditorGUILayout.BeginHorizontal();
                //时长走 TimeField：表头与 lane 明细必须同一个单位口径，否则会出现"表头 0.2、窗写 12"这种分裂读数
                TimeField(so.FindProperty("duration"), "Duration");
                EditorGUILayout.PropertyField(so.FindProperty("priority"));
                EditorGUILayout.PropertyField(so.FindProperty("keepPlayingAnim"));
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.PropertyField(so.FindProperty("animKey"));
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(so.FindProperty("catalog"));
                EditorGUILayout.PropertyField(so.FindProperty("autoNextActionId"));
                EditorGUILayout.EndHorizontal();

                TimeField(so.FindProperty("inputValidSec"), "Input window valid for");
                SerializedProperty seq = so.FindProperty("inputSequence");
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Input Sequence: " + DescribeSequence(seq));
                if (GUILayout.Button("+ Beat", GUILayout.Width(56))) seq.arraySize++;
                if (seq.arraySize > 0 && GUILayout.Button("- Last", GUILayout.Width(56))) seq.DeleteArrayElementAtIndex(seq.arraySize - 1);
                EditorGUILayout.EndHorizontal();
                using (new EditorGUI.IndentLevelScope()){
                    for (int i = 0; i < seq.arraySize; i++){
                        CombatAssetGUI.IdDropdown(seq.GetArrayElementAtIndex(i), "Beat " + (i + 1), ActionKeyBits.Names);
                    }
                }

                if (percentMode && clipLength <= 0f){
                    EditorGUILayout.HelpBox("Percent mode needs a clipLength > 0 (it fills automatically once the caster resolves the action's clip; or type it in manually)", MessageType.Warning);
                }

                string next = so.FindProperty("autoNextActionId").stringValue;
                if (!string.IsNullOrEmpty(next) && FindAction(next) == null){
                    EditorGUILayout.HelpBox("autoNextActionId '" + next + "' not found in the library — finishing this action will switch to a nonexistent action", MessageType.Warning);
                }
            }
        }

        private static string DescribeSequence(SerializedProperty seq){
            if (seq.arraySize == 0) return "(empty = unreachable by input; only intent entries can start it)";
            List<string> names = new List<string>();
            for (int i = 0; i < seq.arraySize; i++) names.Add(seq.GetArrayElementAtIndex(i).stringValue);
            return string.Join(" → ", names);
        }

        //──────────── lane 增删 ────────────

        private void DrawLaneToolbar(bool playing){
            using (new EditorGUI.DisabledScope(playing)){
                EditorGUILayout.BeginHorizontal();
                for (int lane = 0; lane < LaneCount; lane++){
                    SerializedProperty arr = LaneArray(lane);
                    if (GUILayout.Button("+ " + LaneLabel[lane] + " (" + arr.arraySize + ")", GUILayout.MinWidth(96))){
                        AddEntry(lane, arr);
                        selLane = lane;
                        selIndex = arr.arraySize - 1;
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private SerializedProperty LaneArray(int lane){
            switch (lane){
                case LaneOut: return so.FindProperty("cancelTags");
                case LaneIn: return so.FindProperty("beCancelledTags");
                case LaneTemp: return so.FindProperty("tempBeCancelledTags");
                case LaneAtk: return so.FindProperty("attackBoxes");
                case LaneHit: return so.FindProperty("hitBoxes");
                default: return so.FindProperty("inputAcceptance");
            }
        }

        ///<summary>新条目的默认值。public static：判据全是数据形状，不该要求先点得到 GUI。</summary>
        public static void AddEntry(int lane, SerializedProperty arr){
            arr.arraySize++;
            SerializedProperty e = arr.GetArrayElementAtIndex(arr.arraySize - 1);
            switch (lane){
                case LaneOut:
                    e.FindPropertyRelative("tag").stringValue = "";
                    e.FindPropertyRelative("startFrom").floatValue = 0f;
                    break;
                case LaneIn:
                    e.FindPropertyRelative("windowMin").floatValue = 0f;
                    e.FindPropertyRelative("windowMax").floatValue = 0.1f;
                    break;
                case LaneTemp:
                    e.FindPropertyRelative("id").stringValue = "";
                    e.FindPropertyRelative("duration").floatValue = 0.1f;
                    break;
                case LaneAtk:
                    //空轨：tag 留空等作者填（明细面板），窗给一小段作起点，keys 等烘培或静态键
                    e.FindPropertyRelative("tag").stringValue = "";
                    e.FindPropertyRelative("openMin").floatValue = 0f;
                    e.FindPropertyRelative("openMax").floatValue = 0.1f;
                    //ruleIndex 停在 -1 是最坏默认值：命中查不到规则，打中就是 0 伤害 0 硬直，而且到处都不报错
                    //（`CharacterObj.CanAttackTargetNow` 照样放行）。所以先指到本招的第一条 attacks 规则。
                    e.FindPropertyRelative("ruleIndex").intValue = FirstAttackRule(arr);
                    break;
                case LaneHit:
                    e.FindPropertyRelative("tag").stringValue = "";
                    e.FindPropertyRelative("openMin").floatValue = 0f;
                    e.FindPropertyRelative("openMax").floatValue = 0.1f;
                    break;
                default:
                    e.FindPropertyRelative("windowMin").floatValue = 0f;
                    e.FindPropertyRelative("windowMax").floatValue = 0.1f;
                    e.FindPropertyRelative("rate").floatValue = 0f;
                    break;
            }
        }

        ///<summary>
        ///本招 `attacks[]` 第一条规则的 **phase 号**（不是下标）。一条规则都没有才回 -1，
        ///那种组合（有攻击轨、没有命中规则）由 `CombatValidator.CheckAttackRules` 报错，不在这里猜。
        ///</summary>
        private static int FirstAttackRule(SerializedProperty attackBoxes){
            ActionAsset a = attackBoxes.serializedObject.targetObject as ActionAsset;
            AttackInfo[] rules = a != null ? a.attacks : null;
            if (rules == null || rules.Length == 0) return -1;
            return rules[0].phase;
        }

        //──────────── 图 ────────────

        private void DrawGraph(bool playing){
            List<Bar>[] bars = CollectBars();
            int[] counts = new int[LaneCount];
            for (int lane = 0; lane < LaneCount; lane++) counts[lane] = bars[lane].Count;
            LaneBand[] layout = LayoutLanes(counts);
            int rowCount = TotalRows(layout);

            float height = TimelineGraphGUI.RulerBand + rowCount * TimelineGraphGUI.RowH + 6f;
            Rect full = GUILayoutUtility.GetRect(10, 10000, height, height);
            GUI.Box(full, "", EditorStyles.helpBox);
            Rect rect = new Rect(full.x + NameGutter, full.y, full.width - NameGutter, full.height);

            float dur = Mathf.Max(TimelineGraphGUI.MinDuration, so.FindProperty("duration").floatValue);
            lastGraphRect = rect;   //滚轮前置捕获用的图区矩形（OnGUI 在进 ScrollView 之前读它）
            TimelineGraphGUI.HandleViewInput(rect, dur, ref viewStart, ref zoom, ref panDrag);
            float fitPps = (rect.width - TimelineGraphGUI.LeftPad - TimelineGraphGUI.RightPad) / dur;
            TimelineGraphGUI.Span span = TimelineGraphGUI.MakeSpan(rect, dur, viewStart, fitPps * zoom);
            TimelineGraphGUI.DrawRuler(rect, span);

            GUIStyle nameStyle = new GUIStyle(EditorStyles.miniBoldLabel);
            for (int lane = 0; lane < LaneCount; lane++){
                LaneBand lb = layout[lane];
                Rect band = new Rect(full.x, rect.y + TimelineGraphGUI.RulerBand + lb.rowStart * TimelineGraphGUI.RowH,
                    full.width, lb.rows * TimelineGraphGUI.RowH);
                if (lane % 2 == 1) EditorGUI.DrawRect(band, new Color(0, 0, 0, 0.06f));
                GUI.Label(new Rect(full.x + 4, band.y, NameGutter - 8, TimelineGraphGUI.RowH), LaneLabel[lane], nameStyle);

                for (int i = 0; i < bars[lane].Count; i++){
                    Bar b = bars[lane][i];
                    Rect line = TimelineGraphGUI.Row(rect, lb.rowStart + b.index);
                    Rect boxRaw = TimelineGraphGUI.BarRect(span, line, b.start, b.end, BodyPad, TimelineGraphGUI.RowH - 6);
                    Rect box = TimelineGraphGUI.ClipTo(boxRaw, rect);
                    if (box.width <= 0f) continue;   //整根滚出视图
                    bool selected = b.lane == selLane && b.index == selIndex;
                    Color border = selected ? Color.yellow
                        : (b.bad ? new Color(1f, 0.35f, 0.35f, 0.95f) : new Color(0, 0, 0, 0.4f));
                    Rect borderRect = TimelineGraphGUI.ClipTo(new Rect(boxRaw.x - 1, boxRaw.y - 1, boxRaw.width + 2, boxRaw.height + 2), rect);
                    if (borderRect.width > 0f) EditorGUI.DrawRect(borderRect, border);
                    if (b.lane == LaneOut){
                        //取消出不是窗口：唯一可改的是接入点 startFrom（切进来时身体时钟落点），块尾=整招时长不归它管。
                        //被跳过的前摇 [0, 接入点] 压暗弱化、接入点画边界线——让"只有一条边界可拖"从画面上读出来
                        Rect skipped = TimelineGraphGUI.ClipTo(
                            TimelineGraphGUI.BarRect(span, line, 0f, b.start, 0f, TimelineGraphGUI.RowH - 6), rect);
                        if (skipped.width > 0.5f) EditorGUI.DrawRect(skipped, new Color(0f, 0f, 0f, 0.22f));
                        float bx = span.TimeToX(b.start);
                        if (bx >= rect.x && bx <= rect.xMax){
                            EditorGUI.DrawRect(new Rect(bx - 1f, box.y, 2f, box.height), border);
                            EditorGUI.DrawRect(new Rect(bx, box.y, 1f, box.height), b.color);
                        }
                        EditorGUI.DrawRect(box, new Color(b.color.r, b.color.g, b.color.b, 0.7f));
                    }else if (b.lane == LaneTemp){
                        //临时入也不是窗口：唯一可改的是 duration=点亮后存活多久（独立时钟；运行期窗在招里=全程）。
                        //画成左端锚点 + 半透明长度条 + 实色右端——右端就是那个可拖的"长度"，左端不是任何窗边
                        EditorGUI.DrawRect(box, new Color(b.color.r, b.color.g, b.color.b, 0.45f));
                        float ax = span.TimeToX(0f);
                        if (ax >= rect.x - 2f) EditorGUI.DrawRect(new Rect(ax - 1f, box.y, 2f, box.height), border);
                        float ex = Mathf.Min(span.TimeToX(b.end), rect.xMax - 1f);
                        EditorGUI.DrawRect(new Rect(ex - 1f, box.y, 2f, box.height), b.color);
                    }else{
                        EditorGUI.DrawRect(box, b.color);
                    }
                    //播放头正开着的窗：提亮一层，"这一帧哪些窗开着"一眼可见
                    if (playhead >= b.start && playhead < b.end){
                        EditorGUI.DrawRect(box, new Color(1f, 1f, 1f, 0.16f));
                    }
                    if (boxRaw.xMax < rect.xMax - 4f){
                        GUI.Label(new Rect(boxRaw.xMax + 4, box.y, full.xMax - boxRaw.xMax - 8, box.height), b.label, EditorStyles.miniLabel);
                    }
                }
            }
            TimelineGraphGUI.DrawBoundary(rect, span);
            TimelineGraphGUI.DrawPlayhead(rect, span, playhead);

            HandleGraphMouse(playing, full, rect, span, bars, layout);
        }

        ///<summary>
        ///lane 的行布局：**每条 lane 至少占一行**（空 lane 也要有名字和"这里什么都没有"的落点）。
        ///画带底色、摆条、命中测试、算整张图的高全读这一份——以前画的那条路自己数行、
        ///命中的那条路再数一遍，两处口径一漂移就变成"看得见却点不中"。
        ///</summary>
        public static LaneBand[] LayoutLanes(int[] laneCounts){
            LaneBand[] bands = new LaneBand[laneCounts.Length];
            int row = 0;
            for (int i = 0; i < laneCounts.Length; i++){
                int rows = Mathf.Max(1, laneCounts[i]);
                bands[i] = new LaneBand{ rowStart = row, rows = rows };
                row += rows;
            }
            return bands;
        }

        public static int TotalRows(LaneBand[] bands){
            int row = 0;
            for (int i = 0; i < bands.Length; i++) row += bands[i].rows;
            return row;
        }

        private List<Bar>[] CollectBars(){
            List<Bar>[] bars = new List<Bar>[LaneCount];
            for (int lane = 0; lane < LaneCount; lane++) bars[lane] = new List<Bar>();

            float dur = so.FindProperty("duration").floatValue;

            SerializedProperty outArr = LaneArray(LaneOut);
            for (int i = 0; i < outArr.arraySize; i++){
                SerializedProperty e = outArr.GetArrayElementAtIndex(i);
                SerializedProperty from = e.FindPropertyRelative("startFrom");
                string tag = e.FindPropertyRelative("tag").stringValue;
                bool bad = string.IsNullOrEmpty(tag) || !ProvidesTag(tag);
                bars[LaneOut].Add(new Bar{
                    lane = LaneOut, index = i, head = from, tail = null,
                    start = from.floatValue, end = dur,
                    label = tag + " → " + ProvidersOf(tag),
                    color = TimelineGraphGUI.ColorFromId(tag), bad = bad
                });
            }

            SerializedProperty inArr = LaneArray(LaneIn);
            for (int i = 0; i < inArr.arraySize; i++){
                SerializedProperty e = inArr.GetArrayElementAtIndex(i);
                SerializedProperty min = e.FindPropertyRelative("windowMin"), max = e.FindPropertyRelative("windowMax");
                string[] tags = StringArray(e.FindPropertyRelative("cancelTag"));
                string incoming = IncomingOf(tags);
                bars[LaneIn].Add(new Bar{
                    lane = LaneIn, index = i, head = min, tail = max,
                    start = min.floatValue, end = max.floatValue,
                    label = incoming,
                    color = TimelineGraphGUI.ColorFromId(tags.Length > 0 ? tags[0] : ""),
                    bad = incoming == null
                });
            }

            SerializedProperty tempArr = LaneArray(LaneTemp);
            for (int i = 0; i < tempArr.arraySize; i++){
                SerializedProperty e = tempArr.GetArrayElementAtIndex(i);
                SerializedProperty len = e.FindPropertyRelative("duration");
                string id = e.FindPropertyRelative("id").stringValue;
                string[] tags = StringArray(e.FindPropertyRelative("cancelTag"));
                bars[LaneTemp].Add(new Bar{
                    lane = LaneTemp, index = i, head = null, tail = len,
                    start = 0f, end = len.floatValue,
                    label = id + " lit " + len.floatValue + "s → " + JoinTags(tags),
                    color = TimelineGraphGUI.ColorFromId(id),
                    bad = string.IsNullOrEmpty(id) || !ProvidesTags(tags)
                });
            }

            SerializedProperty moveArr = LaneArray(LaneMove);
            bool[] overlapped = new bool[moveArr.arraySize];
            for (int i = 0; i < moveArr.arraySize; i++){
                SerializedProperty a = moveArr.GetArrayElementAtIndex(i);
                for (int j = i + 1; j < moveArr.arraySize; j++){
                    SerializedProperty b = moveArr.GetArrayElementAtIndex(j);
                    if (a.FindPropertyRelative("windowMin").floatValue < b.FindPropertyRelative("windowMax").floatValue
                        && b.FindPropertyRelative("windowMin").floatValue < a.FindPropertyRelative("windowMax").floatValue){
                        overlapped[i] = overlapped[j] = true;
                    }
                }
            }
            for (int i = 0; i < moveArr.arraySize; i++){
                SerializedProperty e = moveArr.GetArrayElementAtIndex(i);
                SerializedProperty min = e.FindPropertyRelative("windowMin"), max = e.FindPropertyRelative("windowMax");
                float rate = Mathf.Clamp01(e.FindPropertyRelative("rate").floatValue);
                bars[LaneMove].Add(new Bar{
                    lane = LaneMove, index = i, head = min, tail = max,
                    start = min.floatValue, end = max.floatValue,
                    label = "walk " + Mathf.RoundToInt(rate * 100f) + "%" + (overlapped[i] ? " (overlaps: min taken)" : ""),
                    color = Color.Lerp(new Color(1f, 0.35f, 0.35f, 0.85f), new Color(0.35f, 0.9f, 0.4f, 0.85f), rate),
                    bad = overlapped[i]
                });
            }
            AddBoxBars(bars, LaneAtk, so.FindProperty("attackBoxes"), asset.attackBoxes);
            AddBoxBars(bars, LaneHit, so.FindProperty("hitBoxes"), asset.hitBoxes);
            return bars;
        }

        ///<summary>
        ///盒轨道一条 lane 的读数：窗就是资产上那两个秒数，标签是 `tag#i  N keys`（i=轨下标，同 tag
        ///多轨时区分"哪一根"），**一根键都没烘到的轨标红**（开着窗却永远判不到东西）。
        ///读的是 `asset` 而不是 SerializedObject：烘培直接写资产字段，从序列化副本读会显示上一次烘的结果。
        ///</summary>
        public static List<BoxRow> BoxLaneRows(ActionBoxTrackBase[] tracks){
            List<BoxRow> rows = new List<BoxRow>();
            if (tracks == null) return rows;
            for (int i = 0; i < tracks.Length; i++){
                ActionBoxTrackBase t = tracks[i];
                int keys = t.keys != null ? t.keys.Length : 0;
                rows.Add(new BoxRow{
                    start = t.openMin, end = t.openMax,
                    label = t.tag + "#" + i + "  " + keys + " keys",
                    color = TimelineGraphGUI.ColorFromId(t.tag),
                    bad = keys == 0
                });
            }
            return rows;
        }

        ///<summary>
        ///盒轨道两条 lane：窗可拖（head/tail 接 openMin/openMax 的 SerializedProperty，与动作轨同一条
        ///拖拽/吸附路径），位姿不归这里改——keys 由烘培按 tag 填、静态盒在明细面板加单键。
        ///</summary>
        private void AddBoxBars(List<Bar>[] bars, int lane, SerializedProperty arr, ActionBoxTrackBase[] tracks){
            List<BoxRow> rows = BoxLaneRows(tracks);
            for (int i = 0; i < rows.Count && i < arr.arraySize; i++){
                BoxRow r = rows[i];
                SerializedProperty e = arr.GetArrayElementAtIndex(i);
                bars[lane].Add(new Bar{
                    lane = lane, index = i,
                    head = e.FindPropertyRelative("openMin"), tail = e.FindPropertyRelative("openMax"),
                    start = r.start, end = r.end, label = r.label, color = r.color, bad = r.bad
                });
            }
        }

        private void HandleGraphMouse(bool playing, Rect full, Rect rect, TimelineGraphGUI.Span span,
            List<Bar>[] bars, LaneBand[] layout){
            Event e = Event.current;
            if (playing){
                if (e.type == EventType.MouseDown && full.Contains(e.mousePosition)) e.Use();
                return;
            }
            float dur = so.FindProperty("duration").floatValue;

            //悬停态的绘制（Repaint 拍；状态更新在 UpdateHover）
            if (e.type == EventType.Repaint){
                DrawHoverFeedback(rect, span);
            }

            //视口输入（滚轮缩放/中键平移）优先：消费掉就不进块逻辑
            if (TimelineGraphGUI.HandleViewInput(rect, dur, ref viewStart, ref zoom, ref panDrag)){
                Repaint();
                return;
            }

            //标尺带按下/拖动=刷播放头
            float scrub;
            if (TimelineGraphGUI.RulerScrub(rect, span, out scrub)){
                e.Use();
                SeekTo(scrub);
                Repaint();
                return;
            }

            if (e.type == EventType.MouseDown && full.Contains(e.mousePosition)){
                Bar hit;
                int row;
                if (TryPick(e.mousePosition, rect, span, bars, layout, out hit, out row)){
                    selLane = hit.lane;
                    selIndex = hit.index;
                    dragging = true;
                    dragLane = hit.lane;
                    dragIndex = hit.index;
                    dragPart = EdgePartAt(span, hit, e.mousePosition);
                    float cursorT = span.XToTime(e.mousePosition.x);
                    if (dragPart == 0){
                        dragGrabSec = cursorT - hit.start;   //整条平移：锚在条起点
                    }else{
                        //边拖/单边条：锚在被抓的那一头上，拖动按位移跟手，边缘不会跳到点击处
                        dragGrabSec = cursorT - (dragPart == 1 ? hit.head.floatValue : hit.tail.floatValue);
                    }
                }else{
                    //点空白=取消选中（以前只能换选，永远去不掉）
                    selLane = -1;
                    selIndex = -1;
                }
                e.Use();
                Repaint();
            }else if (e.type == EventType.MouseUp){
                dragging = false;
            }else if (dragging && e.type == EventType.MouseDrag && selLane >= 0 && selIndex >= 0 && selIndex < LaneArray(selLane).arraySize){
                SerializedProperty entry = LaneArray(selLane).GetArrayElementAtIndex(selIndex);
                SerializedProperty head = EntryEdge(entry, selLane, true), tail = EntryEdge(entry, selLane, false);
                //锚点=光标时刻减抓取偏移再吸附到帧（落在 <see cref="ApplyBarDrag"/>）。`t` 吸了而 `dragGrabSec`
                //没吸的话，差出来的那个零头会原样落进 windowMin/windowMax——等于亲手把窗边界配到两帧中间
                float grabT = SnapToFrame(span.XToTime(e.mousePosition.x) - dragGrabSec, dur);
                float headV = head != null ? head.floatValue : 0f;
                float tailV = tail != null ? tail.floatValue : 0f;
                ApplyBarDrag(dragPart, head != null, tail != null, grabT, ref headV, ref tailV, dur);
                if (head != null && headV != head.floatValue) head.floatValue = headV;
                if (tail != null && tailV != tail.floatValue) tail.floatValue = tailV;
                e.Use();
                Repaint();
            }

            UpdateHover(e, full, rect, span, bars, layout);
        }

        ///<summary>悬停态的状态更新（MouseMove/MouseDrag 拍）：抓到哪条边记下来，绘制在 <see cref="DrawHoverFeedback"/>；
        ///拖拽中跟着被拖的边走</summary>
        private void UpdateHover(Event e, Rect full, Rect rect, TimelineGraphGUI.Span span, List<Bar>[] bars, LaneBand[] layout){
            if (e.type != EventType.MouseMove && e.type != EventType.MouseDrag) return;
            if (full.Contains(e.mousePosition) == false){
                if (hoverValid){ hoverValid = false; Repaint(); }
                return;
            }
            bool valid;
            Bar hit;
            int row, part = 0;
            if (dragging && selLane >= 0 && selIndex >= 0){
                valid = TryFindBar(selLane, selIndex, bars, layout, out hit, out row);
                part = dragPart;
            }else{
                valid = TryPick(e.mousePosition, rect, span, bars, layout, out hit, out row);
                if (valid) part = EdgePartAt(span, hit, e.mousePosition);
            }
            if (valid != hoverValid
                || (valid && (hit.lane != hoverBar.lane || hit.index != hoverBar.index || part != hoverPart))){
                hoverValid = valid;
                hoverBar = hit;
                hoverRow = row;
                hoverPart = part;
                Repaint();
            }
        }

        ///<summary>悬停态的绘制（Repaint 拍）：边热区亮出那条边 + 左右箭头光标</summary>
        private void DrawHoverFeedback(Rect rect, TimelineGraphGUI.Span span){
            if (hoverValid == false || hoverPart == 0 || hoverRow < 0) return;
            Rect line = TimelineGraphGUI.Row(rect, hoverRow);
            float x = hoverPart == 1
                ? TimelineGraphGUI.BarRect(span, line, hoverBar.start, hoverBar.start, 0f, 0f).x
                : TimelineGraphGUI.BarRect(span, line, hoverBar.end, hoverBar.end, 0f, 0f).x;
            if (x < rect.x - EdgeGrab || x > rect.xMax + EdgeGrab) return;
            EditorGUIUtility.AddCursorRect(new Rect(x - EdgeGrab, line.y, EdgeGrab * 2f, line.height), MouseCursor.ResizeHorizontal);
            EditorGUI.DrawRect(new Rect(x - 1f, line.y + 2f, 2f, line.height - 4f), new Color(1f, 1f, 1f, 0.65f));
        }

        ///<summary>条目的可拖边界（tail=false 那一头对 Cancel Out/Temp 不存在，返回 null）。
        ///public static：盒轨窗"可拖"是这次才有的行为，两头各接哪个字段有客观答案，包内测钉住（同 PickDragPart 的理由）。</summary>
        public static SerializedProperty EntryEdge(SerializedProperty entry, int lane, bool head){
            switch (lane){
                case LaneOut: return head ? entry.FindPropertyRelative("startFrom") : null;
                case LaneIn: return head ? entry.FindPropertyRelative("windowMin") : entry.FindPropertyRelative("windowMax");
                case LaneTemp: return head ? null : entry.FindPropertyRelative("duration");
                case LaneAtk: case LaneHit:
                    return head ? entry.FindPropertyRelative("openMin") : entry.FindPropertyRelative("openMax");
                default: return head ? entry.FindPropertyRelative("windowMin") : entry.FindPropertyRelative("windowMax");
            }
        }

        ///<summary>按下/悬停时判"抓的是哪一头"。主体优先的窄条规则见 <see cref="PickDragPart"/>。</summary>
        private static int EdgePartAt(TimelineGraphGUI.Span span, Bar hit, Vector2 mouse){
            float headX = TimelineGraphGUI.BarRect(span, Rect.zero, hit.start, hit.start, 0f, 0f).x;
            float tailX = TimelineGraphGUI.BarRect(span, Rect.zero, hit.end, hit.end, 0f, 0f).x;
            return PickDragPart(headX, tailX, hit.head != null, hit.tail != null, mouse.x, EdgeGrab);
        }

        ///<summary>
        ///条的抓取判据（public static：这条判据画在窗口里拍不到，抽成出口被包内测钉住，同 `LayoutLanes` 的理由）。
        ///- 宽条（两头都在且宽 ≥ edgeGrab×3）：±edgeGrab 抓边、中间拖整条。
        ///- **窄条主体优先**：短窗在铺满视图下只有十几像素，边缘热区会盖满条身——点哪都判成"拖边"、
        ///  整条挪不动（正是"点击块拖动变成边缘拖动"的原因）。窄条改成中段 50% 平移整条、两侧各 25% 归最近的边；
        ///  要精确改边可以放大时间轴（滚轮）或走明细面板的数值框。
        ///- 单边条（取消出/临时入）：条的唯自由度就是那一头，点哪都拖它。
        ///- 两头都无：0，拖拽分支拿不到可写字段就是空操作（防御分支，当前六条 lane 都给至少一头）。
        ///</summary>
        public static int PickDragPart(float headX, float tailX, bool hasHead, bool hasTail, float mouseX, float edgeGrab){
            if (hasHead == false && hasTail == false) return 0;
            if (hasHead && hasTail){
                float width = tailX - headX;
                if (width < edgeGrab * 3f){
                    float u = width > 0.5f ? (mouseX - headX) / width : 0.5f;
                    return u < 0.25f ? 1 : (u > 0.75f ? 2 : 0);
                }
            }
            int part = Mathf.Abs(mouseX - headX) <= edgeGrab && hasHead ? 1
                : (Mathf.Abs(mouseX - tailX) <= edgeGrab && hasTail ? 2 : 0);
            if (part == 0 && hasHead == false) part = 2;
            if (part == 0 && hasTail == false) part = 1;
            return part;
        }

        ///<summary>
        ///把一次拖拽落到条的两端（public static：判据要进包测，同 `PickDragPart` 的理由）。
        ///grabT 是"光标时刻 − 抓取偏移"吸附到帧后的锚点，各模式都按它推进——抓取时鼠标与边/块的相对
        ///位置在整个拖动中保持不变，边跟着手位移走而不是跳到点击处：
        ///- part 0（双端条抓中段）：整条平移，宽度不变，左端夹在 [0, dur−width]。
        ///- part 1/2：那一端跟到 grabT，夹在 [0, dur] 内且不越过对边。单边条（取消出=只有头、临时入=只有尾）
        ///  被 `PickDragPart` 恒判成自己唯一可动的端，点哪都拖那头——这是它们仅有的自由度
        ///  （取消出的尾=动作末尾、临时入的头=动作起点，数据语义钉死的，不是拖拽逻辑偷懒）。
        ///- 两头都不可拖：原样返回（防御分支，当前六条 lane 至少给一头）。
        ///</summary>
        public static void ApplyBarDrag(int part, bool hasHead, bool hasTail, float grabT,
                                        ref float curHead, ref float curTail, float dur){
            if (hasHead == false && hasTail == false) return;
            if (part == 0 && hasHead && hasTail){
                float width = curTail - curHead;
                curHead = Mathf.Clamp(grabT, 0f, Mathf.Max(0f, dur - width));
                curTail = curHead + width;
                return;
            }
            if (part == 1 && hasHead){
                curHead = Mathf.Clamp(grabT, 0f, hasTail ? curTail : dur);
            }else if (part == 2 && hasTail){
                curTail = Mathf.Clamp(grabT, hasHead ? curHead : 0f, dur);
            }
        }

        private bool TryPick(Vector2 mouse, Rect rect, TimelineGraphGUI.Span span, List<Bar>[] bars,
            LaneBand[] layout, out Bar hit, out int row){
            for (int lane = LaneCount - 1; lane >= 0; lane--){
                row = layout[lane].rowStart;
                for (int i = bars[lane].Count - 1; i >= 0; i--){
                    Bar b = bars[lane][i];
                    Rect line = TimelineGraphGUI.Row(rect, row + b.index);
                    if (TimelineGraphGUI.HitBar(mouse, span, line, b.start, b.end)){
                        hit = b;
                        return true;
                    }
                }
            }
            row = -1;
            hit = default(Bar);
            return false;
        }

        private static bool TryFindBar(int lane, int index, List<Bar>[] bars, LaneBand[] layout, out Bar hit, out int row){
            hit = default(Bar);
            row = -1;
            if (lane < 0 || lane >= LaneCount || layout == null || index < 0 || index >= bars[lane].Count) return false;
            hit = bars[lane][index];
            row = layout[lane].rowStart + index;
            return true;
        }

        ///<summary>只吸附、不夹上界（批量对齐用的是这个）</summary>
        private static float SnapToFrame(float t){
            return TimelineGraphGUI.SnapTo(t, FrameSec);
        }

        private static float SnapToFrame(float t, float dur){
            return Mathf.Clamp(SnapToFrame(t), 0f, dur);
        }

        //──────────── 明细面板 ────────────

        private void DrawDetail(bool playing){
            if (selLane < 0 || selIndex < 0 || selIndex >= LaneArray(selLane).arraySize){
                EditorGUILayout.HelpBox("Click a bar to select it; drag its edges or body to edit (snapped to one logic frame). "
                    + "Cancel Out / Temp Cancel In hold a single value each — dragging always moves that one boundary "
                    + "(landing time / alive duration); the dimmed or anchored side is not a window edge", MessageType.None);
                return;
            }
            SerializedProperty entry = LaneArray(selLane).GetArrayElementAtIndex(selIndex);
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(playing)){
                EditorGUILayout.LabelField(LaneLabel[selLane] + " [" + selIndex + "]", EditorStyles.boldLabel);
                switch (selLane){
                    case LaneOut:
                        CombatAssetGUI.IdDropdown(entry.FindPropertyRelative("tag"), "Cancel tag", AllTags());
                        TimeField(entry.FindPropertyRelative("startFrom"), "Take over at");
                        EditorGUILayout.PropertyField(entry.FindPropertyRelative("priority"), new GUIContent("Priority bonus"));
                        EditorGUILayout.LabelField("Can take down: " + ProvidersOf(entry.FindPropertyRelative("tag").stringValue));
                        break;
                    case LaneIn:
                        TimeField(entry.FindPropertyRelative("windowMin"), "Window start (incl.)");
                        TimeField(entry.FindPropertyRelative("windowMax"), "Window end (excl.)");
                        EditorGUILayout.PropertyField(entry.FindPropertyRelative("priority"), new GUIContent("Priority bonus"));
                        break;
                    case LaneTemp:
                        EditorGUILayout.PropertyField(entry.FindPropertyRelative("id"), new GUIContent("Template id"));
                        TimeField(entry.FindPropertyRelative("duration"), "Alive for");
                        EditorGUILayout.PropertyField(entry.FindPropertyRelative("priority"), new GUIContent("Priority bonus"));
                        break;
                    case LaneAtk:{
                        //攻击轨能填的就这四样：挂点 tag、窗、分数、指向哪条命中规则。结算全在那条 attacks 里。
                        //keys 归烘培（多键只读），静态盒走"Add static key"给的单键编辑
                        //（轴恒单位 identity，采样器夹到端点=常量位姿）
                        DrawBoxTrackHead(entry);
                        EditorGUILayout.PropertyField(entry.FindPropertyRelative("priority"), new GUIContent("Priority bonus"));
                        DrawRulePicker(entry.FindPropertyRelative("ruleIndex"));
                        DrawBoxKeys(entry);
                        break;
                    }
                    case LaneHit:{
                        //受击轨带的是"这扇窗被打通之后"：点亮哪些临时连招窗、双方各切进哪一招
                        DrawBoxTrackHead(entry);
                        EditorGUILayout.PropertyField(entry.FindPropertyRelative("priority"), new GUIContent("Priority bonus"));
                        DrawIdList(entry.FindPropertyRelative("tempBeCancelledTagTurnOn"), AllTempWindowIds(), "window");
                        EditorGUILayout.PropertyField(entry.FindPropertyRelative("attackerActionChange"),
                            new GUIContent("Attacker action change (who hit me)"), true);
                        EditorGUILayout.PropertyField(entry.FindPropertyRelative("selfActionChange"),
                            new GUIContent("Self action change"), true);
                        DrawBoxKeys(entry);
                        break;
                    }
                    default:
                        TimeField(entry.FindPropertyRelative("windowMin"), "Segment start (incl.)");
                        TimeField(entry.FindPropertyRelative("windowMax"), "Segment end (excl.)");
                        EditorGUILayout.PropertyField(entry.FindPropertyRelative("rate"), new GUIContent("Walk speed scale"));
                        break;
                }
                if (GUILayout.Button("Delete", GUILayout.Width(70))){
                    LaneArray(selLane).DeleteArrayElementAtIndex(selIndex);
                    //必须先 Apply：ExitGUI 会当场中止整轮 OnGUI，末尾那次 ApplyModifiedProperties
                    //到不了，下一帧 so.Update() 就把没落盘的删除冲回去——删了等于没删
                    so.ApplyModifiedProperties();
                    selIndex = -1;
                    GUIUtility.ExitGUI();
                    return;
                }

                if (selLane == LaneIn || selLane == LaneTemp){
                    DrawIdList(entry.FindPropertyRelative("cancelTag"), AllTags(), "tag");
                    if (selLane == LaneIn){
                        string incoming = IncomingOf(StringArray(entry.FindPropertyRelative("cancelTag")));
                        EditorGUILayout.LabelField("Who cancels this action here: "
                            + (incoming ?? "(nobody in the library carries these tags — this window is effectively closed)"));
                    }
                }
            }
        }

        ///<summary>两条盒轨共用的头：挂点 tag + 开窗区间（这两个字段在 `ActionBoxTrackBase` 上，lane 之间同形）</summary>
        private void DrawBoxTrackHead(SerializedProperty entry){
            SerializedProperty tag = entry.FindPropertyRelative("tag");
            EditorGUILayout.PropertyField(tag, new GUIContent("Track tag (UnitActionBox name)"));
            EditorGUILayout.LabelField("Mounts on caster: " + MountTagHint(tag.stringValue), EditorStyles.miniLabel);
            TimeField(entry.FindPropertyRelative("openMin"), "Window start (incl.)");
            TimeField(entry.FindPropertyRelative("openMax"), "Window end (excl.)");
        }

        ///<summary>
        ///攻击轨的结算指针：填的是 `attacks[]` 里那条的 **phase 号**，不是数组下标。
        ///这一格只能在窗口里防——填错不报编译错，运行期 `IndexOfAttack` 查不到就用默认值结算，
        ///症状是"打中了，没伤害也没硬直"，谁都会当成手感问题。
        ///</summary>
        private void DrawRulePicker(SerializedProperty ruleIndex){
            EditorGUILayout.PropertyField(ruleIndex, new GUIContent("Attack rule (attacks[].phase)"));
            ActionAsset a = so.targetObject as ActionAsset;
            AttackInfo[] rules = a != null ? a.attacks : null;
            if (rules == null || rules.Length == 0){
                EditorGUILayout.HelpBox("This action has no attacks[] entries: the window can open, but a hit settles nothing.",
                    MessageType.Error);
                return;
            }
            List<string> phases = new List<string>();
            for (int i = 0; i < rules.Length; i++) phases.Add(rules[i].phase.ToString());
            if (phases.Contains(ruleIndex.intValue.ToString()) == false){
                EditorGUILayout.HelpBox("No attacks[] entry carries phase " + ruleIndex.intValue
                    + " — this hit settles as nothing. Phases in this action: " + string.Join(", ", phases),
                    MessageType.Error);
            }
        }

        ///<summary>时间字段：帧模式落整帧、百分比模式与秒模式都只是换算外壳（落盘永远是秒）</summary>
        private void TimeField(SerializedProperty p, string label){
            if (frameMode){
                int frames = Mathf.RoundToInt(p.floatValue / FrameSec);
                int next = EditorGUILayout.IntField(label + " (帧)", frames);
                //写回时一律乘回帧长：这样"编辑出来的值必然落在帧格上"，拖拽吸附与手输是同一个口径
                if (next != frames) p.floatValue = next * FrameSec;
            }else if (percentMode && clipLength > 0f){
                float pct = p.floatValue / clipLength * 100f;
                float next = EditorGUILayout.FloatField(label + " (%)", pct);
                p.floatValue = next * clipLength / 100f;
            }else{
                EditorGUILayout.PropertyField(p, new GUIContent(label + " (s)"));
            }
        }

        ///<summary>
        ///tag 与烘培挂点的对账提示：caster 预览给到 prefab 时就能列出它身上的挂点 tag，
        ///填了没有的 tag 当场可见（烘培会警告"no UnitActionBox tagged …"，这里提前一步）。
        ///</summary>
        private string MountTagHint(string tag){
            if (casterPrefab == null) return "assign a caster prefab to list its UnitActionBox tags";
            List<string> names = new List<string>();
            foreach (UnitActionBox b in casterPrefab.GetComponentsInChildren<UnitActionBox>(true)){
                if (b != null && string.IsNullOrEmpty(b.trackTag) == false) names.Add(b.trackTag);
            }
            if (names.Count == 0) return "(this prefab has no UnitActionBox — bake has nothing to sample)";
            return string.Join(", ", names) + (names.Contains(tag) ? "" : "  — no mount tagged '" + tag + "'");
        }

        ///<summary>
        ///keys 区：空轨给"Add static key"（不跟骨骼的静态盒：中心/半边长手填，轴恒单位 identity——
        ///单键轨被采样器夹到端点，整段就是常量位姿）；单键轨可编辑 center/half；
        ///多键轨只读（那是烘培按 tag 采出来的骨骼轨迹，手改一帧=下一次重烘被覆盖）。
        ///</summary>
        private void DrawBoxKeys(SerializedProperty entry){
            SerializedProperty keys = entry.FindPropertyRelative("keys");
            int n = keys.arraySize;
            if (n == 0){
                EditorGUILayout.HelpBox("No keys: this track never hits anything yet. Bake to fill bone-following keys, or add a static key for a box that stays put relative to the character.", MessageType.None);
                if (GUILayout.Button("Add static key", GUILayout.Width(130))){
                    keys.arraySize = 1;
                    SerializedProperty k = keys.GetArrayElementAtIndex(0);
                    k.FindPropertyRelative("t").floatValue = 0f;
                    k.FindPropertyRelative("center").vector3Value = Vector3.zero;
                    k.FindPropertyRelative("axisX").vector3Value = Vector3.right;
                    k.FindPropertyRelative("axisY").vector3Value = Vector3.up;
                    k.FindPropertyRelative("axisZ").vector3Value = Vector3.forward;
                    k.FindPropertyRelative("half").vector3Value = new Vector3(0.15f, 0.15f, 0.15f);
                }
                return;
            }
            if (n == 1){
                EditorGUILayout.LabelField("Static key (axes stay unit identity)", EditorStyles.miniLabel);
                SerializedProperty k = keys.GetArrayElementAtIndex(0);
                EditorGUILayout.PropertyField(k.FindPropertyRelative("center"), new GUIContent("Center (root-local)"));
                EditorGUILayout.PropertyField(k.FindPropertyRelative("half"), new GUIContent("Half extents"));
                EditorGUILayout.LabelField("Remove it to go back to an empty track (or rebake for bone-following keys)", EditorStyles.miniLabel);
                if (GUILayout.Button("Remove static key", GUILayout.Width(130))){
                    keys.DeleteArrayElementAtIndex(0);
                }
                return;
            }
            EditorGUILayout.LabelField(n + " keys baked (bone-following — edit via the bake window, not by hand)", EditorStyles.miniLabel);
        }

        ///<summary>
        ///id 数组的一行编辑器。词表由调用方给：取消 tag 和临时窗模板 id 是两个集合——前者全库互查，
        ///后者是"挨打那一招"自己声明的模板名，共用一个下拉就是把人往错的方向引。
        ///</summary>
        private void DrawIdList(SerializedProperty arr, IEnumerable<string> candidates, string itemPrefix){
            EditorGUILayout.BeginHorizontal();
            int n = EditorGUILayout.IntField("Count", arr.arraySize, GUILayout.Width(150));
            if (n != arr.arraySize) arr.arraySize = Mathf.Max(0, n);
            if (arr.arraySize > 0 && GUILayout.Button("- Last", GUILayout.Width(60))) arr.DeleteArrayElementAtIndex(arr.arraySize - 1);
            if (GUILayout.Button("+ " + itemPrefix, GUILayout.Width(60))) arr.arraySize++;
            EditorGUILayout.EndHorizontal();
            using (new EditorGUI.IndentLevelScope()){
                for (int i = 0; i < arr.arraySize; i++){
                    CombatAssetGUI.IdDropdown(arr.GetArrayElementAtIndex(i), itemPrefix + "[" + i + "]", candidates);
                }
            }
        }

        //──────────── 全库交叉查询 ────────────

        private ActionAsset FindAction(string id){
            if (asset != null && asset.id == id) return asset;
            for (int i = 0; i < library.Count; i++){
                if (library[i].id == id) return library[i];
            }
            return null;
        }

        ///<summary>全库出现过的 tag（下拉选项：两头都收，新招也从这里挑）</summary>
        private IEnumerable<string> AllTags(){
            List<string> tags = new List<string>();
            for (int i = 0; i < library.Count; i++) CollectTags(library[i], tags);
            if (asset != null) CollectTags(asset, tags);
            tags.Sort();
            //去重=每个值只留第一格。这里原先写的是 `RemoveAll(t => IndexOf(t) != LastIndexOf(t))`，
            //那一句删掉的是"出现过不止一次的值"——一个 tag 被越多人用，它反而整个从下拉里消失
            //（出货数据里 "attack" 同时出现在拳的 cancelTags 与 idle 的 beCancelledTags ⇒ 正好被抹掉，
            //  而那正是这一格最该被选中的那个 tag）。已排序 ⇒ 相邻比较就够。
            for (int i = tags.Count - 1; i > 0; i--) if (tags[i] == tags[i - 1]) tags.RemoveAt(i);
            return tags;
        }

        ///<summary>
        ///全库的临时窗模板 id。受击轨点亮的是"挨打那一招"自己声明的模板，所以词表同样要跨动作收；
        ///它和 `AllTags()` 是两个集合（一边是 PunchCombo1 这种模板名，一边是 Punch2 这种取消 tag）。
        ///</summary>
        private List<string> AllTempWindowIds(){
            List<string> ids = new List<string>();
            for (int i = 0; i < library.Count; i++) CollectTempIds(library[i], ids);
            if (asset != null) CollectTempIds(asset, ids);
            ids.Sort();
            for (int i = ids.Count - 1; i > 0; i--) if (ids[i] == ids[i - 1]) ids.RemoveAt(i);
            return ids;
        }

        private static void CollectTempIds(ActionAsset a, List<string> into){
            if (a.tempBeCancelledTags == null) return;
            for (int i = 0; i < a.tempBeCancelledTags.Length; i++){
                if (a.tempBeCancelledTags[i] != null && string.IsNullOrEmpty(a.tempBeCancelledTags[i].id) == false){
                    into.Add(a.tempBeCancelledTags[i].id);
                }
            }
        }

        private static void CollectTags(ActionAsset a, List<string> into){
            if (a.cancelTags != null){
                for (int i = 0; i < a.cancelTags.Length; i++)
                    if (a.cancelTags[i] != null && string.IsNullOrEmpty(a.cancelTags[i].tag) == false) into.Add(a.cancelTags[i].tag);
            }
            CollectBeTags(a.beCancelledTags, into);
            if (a.tempBeCancelledTags != null){
                for (int i = 0; i < a.tempBeCancelledTags.Length; i++)
                    if (a.tempBeCancelledTags[i] != null) CollectList(a.tempBeCancelledTags[i].cancelTag, into);
            }
        }

        private static void CollectBeTags(BeCancelledTagAsset[] be, List<string> into){
            if (be == null) return;
            for (int i = 0; i < be.Length; i++) if (be[i] != null) CollectList(be[i].cancelTag, into);
        }

        private static void CollectList(string[] src, List<string> into){
            if (src == null) return;
            for (int i = 0; i < src.Length; i++) if (string.IsNullOrEmpty(src[i]) == false) into.Add(src[i]);
        }

        ///<summary>本动作某条 tag 能被谁接住=全库里"可被取消侧"列了这条 tag 的动作</summary>
        private bool ProvidesTag(string tag){
            return ProvidesTags(new[] { tag });
        }

        private bool ProvidesTags(string[] tags){
            if (tags.Length == 0) return false;
            for (int i = 0; i < library.Count; i++){
                if (Intersects(library[i], tags)) return true;
            }
            return asset != null && Intersects(asset, tags);
        }

        private static bool Intersects(ActionAsset a, string[] tags){
            if (a.beCancelledTags != null){
                for (int i = 0; i < a.beCancelledTags.Length; i++){
                    if (a.beCancelledTags[i] != null && Hits(a.beCancelledTags[i].cancelTag, tags)) return true;
                }
            }
            if (a.tempBeCancelledTags != null){
                for (int i = 0; i < a.tempBeCancelledTags.Length; i++){
                    if (a.tempBeCancelledTags[i] != null && Hits(a.tempBeCancelledTags[i].cancelTag, tags)) return true;
                }
            }
            return false;
        }

        private static bool Hits(string[] provided, string[] wanted){
            if (provided == null) return false;
            for (int i = 0; i < provided.Length; i++){
                for (int w = 0; w < wanted.Length; w++){
                    if (string.IsNullOrEmpty(wanted[w]) == false && provided[i] == wanted[w]) return true;
                }
            }
            return false;
        }

        private string ProvidersOf(string tag){
            if (string.IsNullOrEmpty(tag)) return "(no tag)";
            List<string> names = new List<string>();
            for (int i = 0; i < library.Count; i++){
                if (Intersects(library[i], new[] { tag })) names.Add(library[i].id);
            }
            if (asset != null && Intersects(asset, new[] { tag })) names.Add(asset.id + " (this action)");
            return names.Count == 0 ? "no taker in library" : string.Join(", ", names);
        }

        ///<summary>谁能在这段窗里把本动作交出去，以及对方从它自己的第几秒接手</summary>
        private string IncomingOf(string[] tags){
            List<string> names = new List<string>();
            for (int i = 0; i <= library.Count; i++){
                ActionAsset other = i < library.Count ? library[i] : asset;
                if (other == null || other.cancelTags == null) continue;
                for (int c = 0; c < other.cancelTags.Length; c++){
                    CancelTagAsset ct = other.cancelTags[c];
                    if (ct == null || string.IsNullOrEmpty(ct.tag)) continue;
                    if (Hits(tags, new[] { ct.tag }) == false) continue;
                    names.Add(other.id + "@" + ct.startFrom + "s");
                }
            }
            return names.Count == 0 ? null : string.Join(", ", names);
        }

        private static string[] StringArray(SerializedProperty arr){
            string[] dst = new string[arr.arraySize];
            for (int i = 0; i < arr.arraySize; i++) dst[i] = arr.GetArrayElementAtIndex(i).stringValue;
            return dst;
        }

        private static string JoinTags(string[] tags){
            return tags.Length == 0 ? "(no tags)" : string.Join(", ", tags);
        }

        //──────────── 校验 ────────────

        ///<summary>
        ///校验区。两个按钮都会写资产（一个提交待写入的编辑、一个整批改窗边界），所以和其余四段一样
        ///吃 Play 门——`OnGUI` 末尾那条"Play 中不 ApplyModifiedProperties"的规矩不该在这一格被绕过。
        ///读数（issues 列表）本身无害，但按钮和读数同区，拆开只会多一处判断。
        ///</summary>
        private void DrawValidation(bool playing){
            using (new EditorGUI.DisabledScope(playing)){
                EditorGUILayout.Space();
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Validate Action", GUILayout.Width(120))){
                    so.ApplyModifiedProperties();
                    issues = CombatValidator.ValidateActionAsset(asset, library);
                }
                if (GUILayout.Button("Align Windows to Frames", GUILayout.Width(160))){
                    QuantizeToFrames();
                }
                EditorGUILayout.EndHorizontal();
            }

            if (issues == null) return;
            if (issues.Count == 0){
                EditorGUILayout.HelpBox("Validation passed: no issues", MessageType.Info);
                return;
            }
            foreach (string issue in issues){
                EditorGUILayout.HelpBox(issue, issue.StartsWith(CombatValidator.WarnPrefix) ? MessageType.Warning : MessageType.Error);
            }
        }

        ///<summary>把本动作所有窗边界吸到帧上（`ActionBoxBake.StepSec` = `LogicClock.Tick` = 1/60 秒的整数倍）</summary>
        private void QuantizeToFrames(){
            Quantize(so.FindProperty("cancelTags"), "startFrom");
            Quantize(so.FindProperty("beCancelledTags"), "windowMin", "windowMax");
            Quantize(so.FindProperty("tempBeCancelledTags"), "duration");
            Quantize(so.FindProperty("inputAcceptance"), "windowMin", "windowMax");
            Quantize(so.FindProperty("attackBoxes"), "openMin", "openMax");
            Quantize(so.FindProperty("hitBoxes"), "openMin", "openMax");
            SerializedProperty dur = so.FindProperty("duration");
            dur.floatValue = SnapToFrame(dur.floatValue);
            so.ApplyModifiedProperties();
            issues = null;
        }

        private void Quantize(SerializedProperty arr, params string[] fields){
            for (int i = 0; i < arr.arraySize; i++){
                SerializedProperty e = arr.GetArrayElementAtIndex(i);
                for (int f = 0; f < fields.Length; f++){
                    SerializedProperty p = e.FindPropertyRelative(fields[f]);
                    p.floatValue = SnapToFrame(p.floatValue);
                }
            }
        }

        ///<summary>
        ///内置烘培的逐行报表（走带栏 "Bake Keys" 的输出区，切资产/重烘即刷新）。
        ///带 WarnPrefix 的行是警告样式，其余是普通说明——不复用校验区的样式，那里没前缀=Error，
        ///而烘培报表的普通行（"clip is …s long"）只是说明，不是错误。
        ///</summary>
        private void DrawBakeReport(){
            if (bakeReport == null || bakeReport.Count == 0) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Bake Report", EditorStyles.boldLabel);
            foreach (string line in bakeReport){
                EditorGUILayout.HelpBox(line,
                    line.StartsWith(CombatValidator.WarnPrefix) ? MessageType.Warning : MessageType.None);
            }
        }
    }
}
