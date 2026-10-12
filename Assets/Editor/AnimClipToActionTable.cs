using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 解决的问题：BeatEmUp 把帧数据全写在 .anim 曲线里（判定盒的开关、位置、大小，外加动画事件），
/// 而本工程的真源是 `Assets/Resources/GameData/Action.json`（见 `Doc/decision/0001`）。
/// 手工把 41 条 clip 一条条拖进时间轴数帧数一定会数错，而且错了没有任何东西会发现——
/// 表现就是「这一拳差一帧打不着」，肉眼看不出来。这个工具把曲线直接翻成整数逻辑帧，
/// 并把可疑的地方逐条点出来。
///
/// 【输出的是草稿，不是成品表】：它写 `Doc/plan/beatemup-anim-dump.md`。
/// 源内容里缺着表要的另一半（伤害、取消标签、指令、受击阶段）——那些是玩法决定，得由人填；
/// 工具只负责「从 clip 里能确定下来的数」。
///
/// 帧号对账（本工程 1-based 逻辑帧，clip 是 60fps 的 0-based）：
/// 本工程的第 n 拍覆盖动作时间的 [(n-1)/60, n/60)，所以 clip 的第 f 帧 = 本工程的第 f+1 拍；
/// 而表里的窗口是「这一段被跨过就算数」（生效帧 = min..max+1），于是
/// min = 抬起那一帧 + 1，max = 落下那一帧 − 1，
/// 这样生效集合正好等于原片子里盒子开着的那几帧。
/// 这套换算必须写在代码旁边，因为 1.7 往表里填数的时候没人能独立验它——差一帧就是 16.7 毫秒的手感。
///
/// 【为什么读 .anim 的文本而不是走 AnimationUtility】：盒子的位置与缩放是 typed 曲线
/// （m_PositionCurves / m_ScaleCurves，一个块直接给 xyz），而 Unity 6 的 AnimationUtility
/// 只把浮点曲线（m_FloatCurves，这里是 m_IsActive / m_Enabled）暴露在 GetCurveBindings 里，
/// transform 那几组没有公开的读法。更麻烦的是 m_EditorCurves 里还有一份编辑器专用的副本，
/// 混着读会把同一条曲线数两遍。按文本读一次覆盖全部四种块，
/// 而且读的就是美术留下的原始数据，不经过任何重新解释。
///
/// 跑法：菜单 Tools → ACT → 导出 BeatEmUp 帧数据草稿。
/// 前置是源工程的 Animation 与 Sprites 连 .meta 一起导进来（guid 不变，曲线里的精灵引用才不会断）。
/// </summary>
public static class AnimClipToActionTable
{
    /// <summary>源 clip 所在目录</summary>
    private const string ClipFolder = "Assets/Arts/BeatEmUp/Animation";

    /// <summary>草稿落在 Doc/：生成物也进版本库，1.7 对拍和以后重跑做对比都靠它</summary>
    private const string DumpPath = "Doc/plan/beatemup-anim-dump.md";

    /// <summary>
    /// 源工程的预制体在另一个工程里，本工程的 AssetDatabase 没有它，
    /// 所以这里只把它当文本读三个数（真导进来反而会带上一堆缺失脚本引用的警告）。
    /// 【路径必须从 Application.dataPath 推】：编辑器进程的当前目录是用户主目录，
    /// 相对路径会解到 /Users/xxx/BeatEmUp 那种根本不存在的地方上，而且失败只是安静地留空
    /// </summary>
    private const string SourcePrefabRelative =
        "BeatEmUp/Assets/Osarion/BeatEmUpTemplate2D/Prefabs/Player.prefab";

    private static string SourcePrefabPath
    {
        get
        {
            //Assets 往上两级就是两个工程的共同父目录
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../"));
            return Path.Combine(root, SourcePrefabRelative);
        }
    }

    private const string HitBoxPath = "HitBox";

    /// <summary>
    /// 源工程用 SpriteRenderer.bounds 判相交，再单独用一条 z 距离 0.5 米卡纵深；
    /// 换成本工程的 3D 盒子时厚度就取这个 0.5，而不是拍一个「看着顺眼」的数
    /// </summary>
    private const float DepthThickness = 0.5f;

    /// <summary>
    /// 表里 tag 的占位名。源片子只有一只盒子、不分拳脚部位，
    /// 1.7 落地时再按本工程的习惯拆成 LeftPunch / RightKick 那些
    /// </summary>
    private const string DraftTag = "BeatHit";

    /// <summary>这四段是曲线的真身；m_EditorCurves 与 m_EulerEditorCurves 是编辑器副本，读它们会把每条曲线数两遍</summary>
    private static readonly HashSet<string> CurveSections = new HashSet<string>
    {
        "m_PositionCurves", "m_ScaleCurves", "m_FloatCurves", "m_PPtrCurves"
    };

    private class Span
    {
        public int onFrame;       //clip 的 0-based：盒子从这一帧开始是开的
        public int offFrame;      //这一帧已经关了；int.MaxValue 表示直到片尾还开着
    }

    private class Cue
    {
        public int frame;         //已经换算成本工程的 1-based
        public string text;
    }

    /// <summary>.anim 里一个曲线块：曲线挂在哪、控哪个属性、关键帧是什么</summary>
    private class Block
    {
        public string section;
        public string path;
        public string attribute;
        public readonly List<Frame3> keys = new List<Frame3>();
    }

    /// <summary>一个关键帧：帧号 + 值（标量曲线的值只放在 x 上）</summary>
    private struct Frame3
    {
        public int frame;
        public Vector3 value;
    }

    private class Clip
    {
        public string name;
        public int durationFrames;
        public float exactFrames;     //Unity 实际加载出来的长度换算成帧，跟整数对不上就是导入设置动了它
        public int sampleRate;
        public readonly List<Span> windows = new List<Span>();
        public readonly List<Span> enabledWindows = new List<Span>();
        public readonly List<int> trackFrames = new List<int>();
        public readonly List<Vector3> positions = new List<Vector3>();
        public readonly List<Vector3> scales = new List<Vector3>();
        public readonly List<Cue> cues = new List<Cue>();
        public readonly List<string> suspects = new List<string>();
        public readonly List<string> notes = new List<string>();
        public int spriteKeys;
        public int activeCurveBlocks;
    }

    [MenuItem("Tools/ACT/导出 BeatEmUp 帧数据草稿（.anim → 表）")]
    public static void Export()
    {
        string[] guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { ClipFolder });
        if (guids.Length == 0)
        {
            Debug.LogError(ClipFolder + " 里一条 AnimationClip 都没有——先把源工程的 Animation 连 .meta 拷进来");
            return;
        }

        BoxSpec box = ReadSourceBox();
        List<Clip> clips = new List<Clip>();
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AnimationClip loaded = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (loaded != null) clips.Add(Parse(path, loaded));
        }
        clips.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        StringBuilder sb = new StringBuilder();
        Header(sb, clips.Count, box);
        foreach (Clip c in clips) Section(sb, c);
        Summary(sb, clips);

        File.WriteAllText(DumpPath, sb.ToString());
        AssetDatabase.Refresh();
        Debug.Log("[帧数据草稿] " + clips.Count + " 条 clip 已写到 " + DumpPath + "\n" + SuspectList(clips));
    }

    //================================ 抽取 ====================================

    private static Clip Parse(string assetPath, AnimationClip loaded)
    {
        Clip c = new Clip
        {
            name = loaded.name,
            exactFrames = loaded.length / LogicClock.Tick,
            durationFrames = Mathf.RoundToInt(loaded.length / LogicClock.Tick)
        };

        string text = ReadText(assetPath);
        foreach (Block b in ReadBlocks(text))
        {
            if (b.section == "m_PPtrCurves" && b.attribute == "m_Sprite")
            {
                c.spriteKeys += b.keys.Count;
                continue;
            }
            if (b.path != HitBoxPath) continue;
            if (b.section == "m_FloatCurves" && b.attribute == "m_IsActive")
            {
                c.activeCurveBlocks++;
                AddSpans(c.windows, b);
            }
            else if (b.section == "m_FloatCurves" && b.attribute == "m_Enabled") AddSpans(c.enabledWindows, b);
            else if (b.section == "m_PositionCurves") AddTrack(c, b, true);
            else if (b.section == "m_ScaleCurves") AddTrack(c, b, false);
        }

        c.cues.AddRange(ReadEvents(text));
        c.sampleRate = SampleRate(text);

        //sampleRate 与片长要跟 Unity 给的对得上：
        //对不上说明导入设置或重序列化动过这条片子，那时长就不是数关键帧能信的那个时长了
        if (c.sampleRate != 60)
            c.suspects.Add("这条 clip 的 sampleRate 是 " + c.sampleRate + "，不是 60：所有帧号都要按这个比率重算");
        if (Mathf.Abs(c.exactFrames - c.durationFrames) > 0.02f)
            c.suspects.Add("片长离 1/60 秒有零头（" + c.exactFrames.ToString("0.###") + " 帧），" +
                           "durationFrames 是取整出来的，每个窗口都会跟着偏一点");

        Check(c);
        return c;
    }

    /// <summary>
    /// 按行扫 .anim，只收四种曲线块。块的边界是 "  - " 开头（位置/缩放写 "  - curve:"，
    /// 浮点写 "  - serializedVersion: 2"），块的结尾是缩进 4 的 "    path:"——
    /// 所有曲线块都以它收尾，所以拿它当「这块读完了」的信号最稳
    /// </summary>
    private static List<Block> ReadBlocks(string text)
    {
        List<Block> blocks = new List<Block>();
        Block cur = null;
        string section = null;
        float time = 0f;
        bool pendingTime = false;

        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith("  m_"))
            {
                if (cur != null) { blocks.Add(cur); cur = null; }
                section = Key(line);
                pendingTime = false;
                continue;
            }
            if (!CurveSections.Contains(section)) continue;

            if (line.StartsWith("  - "))
            {
                if (cur != null) blocks.Add(cur);
                cur = new Block { section = section };
                pendingTime = false;
                continue;
            }
            if (cur == null) continue;

            string key = Key(line);
            string val = Value(line);
            if (key == "attribute") cur.attribute = val;
            else if (key == "time")
            {
                float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out time);
                pendingTime = true;
            }
            else if (key == "value" && pendingTime)
            {
                cur.keys.Add(new Frame3 { frame = Frame(time), value = Vec(val) });
                pendingTime = false;
            }
            else if (key == "path")
            {
                cur.path = val;
                blocks.Add(cur);
                cur = null;
            }
        }
        return blocks;
    }

    private static List<Cue> ReadEvents(string text)
    {
        List<Cue> cues = new List<Cue>();
        int at = text.IndexOf("\n  m_Events:\n", System.StringComparison.Ordinal);
        if (at < 0) return cues;
        //从表头之后开始扫：连着把表头也喂进循环的话，第一行就撞上 "  m_" 的收尾条件直接退出
        at += "\n  m_Events:\n".Length;

        float time = 0f;
        string func = null, data = null;
        foreach (string raw in text.Substring(at).Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith("  - "))
            {
                if (func != null) cues.Add(MakeCue(time, func, data));
                func = null;
                data = null;
                float.TryParse(Value(line.Substring(2)), NumberStyles.Float, CultureInfo.InvariantCulture, out time);
                continue;
            }
            string key = Key(line);
            if (key == "functionName") func = Value(line);
            else if (key == "data") data = Value(line);
            else if (line.StartsWith("  m_")) break;
        }
        if (func != null) cues.Add(MakeCue(time, func, data));
        return cues;
    }

    private static Cue MakeCue(float time, string func, string data)
    {
        return new Cue
        {
            frame = Frame(time) + 1,       //同一套换算：clip 的第 f 帧 = 本工程的第 f+1 拍
            text = func + (string.IsNullOrEmpty(data) || data == "None" ? "" : "(" + data + ")")
        };
    }

    private static string Key(string line)
    {
        string s = line.TrimStart();
        if (s.StartsWith("- ")) s = s.Substring(2);
        int colon = s.IndexOf(':');
        return colon < 0 ? s : s.Substring(0, colon).Trim();
    }

    private static string Value(string line)
    {
        int colon = line.IndexOf(':');
        return colon < 0 ? "" : line.Substring(colon + 1).Trim();
    }

    private static Vector3 Vec(string val)
    {
        if (!val.StartsWith("{"))
        {
            float f;
            float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out f);
            return new Vector3(f, 0f, 0f);
        }
        return new Vector3(Num(val, "x:"), Num(val, "y:"), Num(val, "z:"));
    }

    private static float Num(string seg, string key)
    {
        int at = seg.IndexOf(key, System.StringComparison.Ordinal);
        if (at < 0) return 0f;
        int i = at + key.Length;
        while (i < seg.Length && (seg[i] == ' ' || seg[i] == '-')) i++;
        int start = i;
        while (i < seg.Length && (char.IsDigit(seg[i]) || seg[i] == '.')) i++;
        float v;
        return float.TryParse(seg.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out v)
            ? v : 0f;
    }

    private static int SampleRate(string text)
    {
        int at = text.IndexOf("m_SampleRate:", System.StringComparison.Ordinal);
        if (at < 0) return 0;
        string seg = text.Substring(at, 22);
        int i = seg.IndexOf(':') + 1;
        while (i < seg.Length && seg[i] == ' ') i++;
        int start = i;
        while (i < seg.Length && char.IsDigit(seg[i])) i++;
        int rate;
        return int.TryParse(seg.Substring(start, i - start), out rate) ? rate : 0;
    }

    /// <summary>
    /// Unity 写的 .anim 是 LF，但 .prefab 是 CRLF（编辑器版本与平台都会影响它）。
    /// 不统一行尾的话，按整行匹配的查找会一个都命中不了，而且失败只是安静地留空——
    /// 所以所有外部文本都从这里进
    /// </summary>
    private static string ReadText(string path)
    {
        return File.ReadAllText(path).Replace("\r\n", "\n");
    }

    private static int Frame(float seconds)
    {
        return Mathf.RoundToInt(seconds / LogicClock.Tick);
    }

    /// <summary>
    /// 0/1 阶梯曲线读成「从第几帧抬起、到第几帧落下」。
    /// 只认关键帧不认插值：这类曲线是 constant 的，取中间值会把台阶读糊
    /// </summary>
    private static void AddSpans(List<Span> into, Block b)
    {
        int open = int.MinValue;
        for (int i = 0; i < b.keys.Count; i++)
        {
            bool on = b.keys[i].value.x > 0.5f;
            if (on && open == int.MinValue) open = b.keys[i].frame;
            else if (!on && open != int.MinValue)
            {
                into.Add(new Span { onFrame = open, offFrame = b.keys[i].frame });
                open = int.MinValue;
            }
        }
        //末尾还开着：源工程一循环就把盒子留在亮着的状态，落下帧只能按片尾算
        if (open != int.MinValue) into.Add(new Span { onFrame = open, offFrame = int.MaxValue });
    }

    /// <summary>
    /// 位置与缩放并进同一套帧号，两个数组按下标一一对应，读草稿的人才对得上。
    /// 只动过其中一根时另一根在该帧留默认值——空着（0 或 1,1,1）比编一个数诚实，
    /// 切片 1.5 落地时按「没有关键帧就沿用上一条」处理
    /// </summary>
    private static void AddTrack(Clip c, Block b, bool isPosition)
    {
        for (int i = 0; i < b.keys.Count; i++)
        {
            int frame = b.keys[i].frame;
            int slot = c.trackFrames.IndexOf(frame);
            if (slot < 0)
            {
                c.trackFrames.Add(frame);
                c.positions.Add(Vector3.zero);
                c.scales.Add(Vector3.one);
                slot = c.trackFrames.Count - 1;
            }
            if (isPosition) c.positions[slot] = b.keys[i].value;
            else c.scales[slot] = b.keys[i].value;
        }
    }

    private static void Check(Clip c)
    {
        if (c.windows.Count == 0 && c.activeCurveBlocks == 0 && (c.trackFrames.Count > 0 || c.enabledWindows.Count > 0))
            c.suspects.Add("动了盒子却没有 m_IsActive 曲线：源片子里盒子是常驻开的（或者只靠 m_Enabled 控制）。" +
                           "表里不能凭空写窗口，得先定这条动作到底哪几帧判得到");
        if (c.windows.Count == 0 && c.activeCurveBlocks > 0)
            c.notes.Add("源片子里盒子全程关着（m_IsActive 一直是 0）：这条动作本来就打不着人（比如倒地中的起身前那几帧），" +
                        "表里就不给它 attackPhase，不是数据缺失");

        foreach (Span w in c.windows)
        {
            int min = w.onFrame + 1, max = w.offFrame - 1;
            if (max < min)
                c.suspects.Add("盒子只开一帧（第 " + min + " 拍）。表里的窗口是跨过就算数，" +
                               "写成 [" + min + "," + min + "] 会多活一拍——要么认了这一拍，要么这条本身要改玩法");
            if (w.onFrame >= c.durationFrames)
                c.suspects.Add("窗口从第 " + (w.onFrame + 1) + " 帧开，但片子只有 " + c.durationFrames + " 帧，永远跑不到");
            if (w.offFrame != int.MaxValue && w.offFrame > c.durationFrames)
                c.suspects.Add("窗口在第 " + w.offFrame + " 帧落下，已经超出片长 " + c.durationFrames + " 帧");
        }

        foreach (Span w in c.enabledWindows)
            if (w.offFrame != int.MaxValue && w.offFrame > c.durationFrames)
                c.suspects.Add("m_Enabled 在第 " + w.offFrame + " 帧落下，超出片长 " + c.durationFrames +
                               " 帧：盒子可能开着但看不见，或者反过来");

        foreach (Cue cue in c.cues)
            if (cue.frame > c.durationFrames)
                c.suspects.Add("cue " + cue.text + " 落在第 " + cue.frame + " 拍，已经超出片长 " +
                               c.durationFrames + " 拍，跑不到");

        foreach (int f in c.trackFrames)
            if (f + 1 > c.durationFrames)
                c.suspects.Add("盒子轨迹在第 " + (f + 1) + " 帧有关键帧，超出片长 " + c.durationFrames + " 帧");

        if (c.windows.Count > 1)
            c.suspects.Add("一条片子里有 " + c.windows.Count + " 段独立窗口：源工程只有一只盒子反复开关。" +
                           "本工程的表要么写成一个 inFrames 数组里几段、要么拆成几条 attackPhase，先定玩法");
    }

    //================================ 源盒子规格 ====================================

    private class BoxSpec
    {
        public Vector3 size;
        public Vector3 scale;
        public Vector3 position;
        public string spritePath;
        public bool found;
    }

    /// <summary>
    /// 读源工程 Player.prefab 里那只 HitBox 的常驻规格（局部位置、局部缩放、SpriteRenderer 的 size 与精灵 guid）。
    /// 只认 GameObject 块里列出的那几个组件 fileID，再顺着 id 取块——
    /// 按「名字附近最近的 Transform」猜会把上一个节点的缩放抄过来，而且抄错了完全看不出来
    /// </summary>
    private static BoxSpec ReadSourceBox()
    {
        BoxSpec s = new BoxSpec();
        string sourcePrefab = SourcePrefabPath;
        if (!File.Exists(sourcePrefab))
        {
            Debug.LogWarning("读不到源预制体 " + sourcePrefab +
                             "：草稿里盒子的世界尺寸那一栏留空（窗口、轨迹、cue 都不受影响）");
            return s;
        }

        string text = ReadText(sourcePrefab);
        int at = text.IndexOf("\n  m_Name: " + HitBoxPath + "\n", System.StringComparison.Ordinal);
        if (at < 0)
        {
            Debug.LogWarning("源预制体里没有名为 " + HitBoxPath + " 的子节点，盒子规格读不到");
            return s;
        }

        //GameObject 块从上一个 "--- !u!1 &" 开始；往回找到那里，才不会把上一个节点的组件列表一起扫进来
        int blockStart = text.LastIndexOf("\n--- ", at, System.StringComparison.Ordinal);
        string head = text.Substring(blockStart < 0 ? 0 : blockStart, at - blockStart);
        List<string> ids = new List<string>();
        foreach (string line in head.Split('\n'))
        {
            int m = line.IndexOf("component: {fileID: ", System.StringComparison.Ordinal);
            if (m < 0) continue;
            int end = line.IndexOf('}', m);
            ids.Add(line.Substring(m + 20, end - m - 20));
        }

        foreach (string id in ids)
        {
            string body = ComponentBlock(text, id);
            if (body.StartsWith("Transform:"))
            {
                s.position = Vec3(body, "m_LocalPosition", s.position);
                s.scale = Vec3(body, "m_LocalScale", s.scale);
                s.found = true;
            }
            else if (body.StartsWith("SpriteRenderer:"))
            {
                s.size = Vec3(body, "m_Size", s.size);
                //精灵的 guid 只从 m_Sprite 那一行往后找：块里第一个 guid: 是 m_Materials 的内置材质，
                //拿它去解析会得到 unity_builtin_extra，尺寸栏看着像真的其实是错的
                int sp = body.IndexOf("m_Sprite: ", System.StringComparison.Ordinal);
                int g = sp < 0 ? -1 : body.IndexOf("guid: ", sp, System.StringComparison.Ordinal);
                if (g >= 0)
                {
                    int end = body.IndexOf(',', g);
                    s.spritePath = AssetDatabase.GUIDToAssetPath(body.Substring(g + 6, end - g - 6));
                    if (string.IsNullOrEmpty(s.spritePath))
                        Debug.LogWarning("盒子的精灵 guid 在本工程解析不到——精灵没连 .meta 导进来");
                }
            }
        }

        if (!s.found) Debug.LogWarning("源预制体的 " + HitBoxPath + " 节点上没找到 Transform 块，尺寸栏留空");
        return s;
    }

    private static string ComponentBlock(string text, string fileID)
    {
        int at = text.IndexOf("&" + fileID + "\n", System.StringComparison.Ordinal);
        if (at < 0) return "";
        int nl = text.IndexOf('\n', at);
        int next = text.IndexOf("\n--- ", nl, System.StringComparison.Ordinal);
        return text.Substring(nl + 1, (next < 0 ? text.Length : next) - nl - 1);
    }

    /// <summary>
    /// 取 {x: …, y: …, z: …} 一整个花括号里的三个数。
    /// 【只在本行内取】：早先按"跳过 key: { 之后固定几个字符"来切，切歪一个字符就把
    /// 下一行 m_LocalScale 的 x 当成了 m_LocalPosition 的 x——数值看着正常，谁也不会发现
    /// </summary>
    private static Vector3 Vec3(string body, string key, Vector3 fallback)
    {
        int brace = body.IndexOf(key + ": {", System.StringComparison.Ordinal);
        if (brace < 0) return fallback;
        brace += key.Length + 2;
        int close = body.IndexOf('}', brace);
        if (close < 0) return fallback;
        string seg = body.Substring(brace, close - brace + 1);
        return new Vector3(Num(seg, "x:"), Num(seg, "y:"), Num(seg, "z:"));
    }

    //================================ 落地 ====================================

    private static void Header(StringBuilder sb, int count, BoxSpec box)
    {
        sb.Append("# BeatEmUp 帧数据草稿（由 .anim 曲线导出）\n\n");
        sb.Append("> 状态：参考资料 · 2026-10\n\n");
        sb.Append("本文件由菜单 `Tools → ACT → 导出 BeatEmUp 帧数据草稿（.anim → 表）` 生成，**别手改**——");
        sb.Append("改了 clip 就重跑。它把 `").Append(ClipFolder).Append("` 里 ").Append(count)
          .Append(" 条 clip 的判定盒窗口、盒子轨迹、动画事件翻成整数逻辑帧，")
          .Append("供 `beatemup-port.md` 的切片 1.7 往 `Assets/Resources/GameData/Action.json` 里填行。\n\n");
        sb.Append("## 帧号怎么对账\n\n");
        sb.Append("本工程的第 n 拍覆盖动作时间的 [(n-1)/60, n/60)，源 clip 是 60fps 的 0-based，");
        sb.Append("所以 **clip 的第 f 帧 = 本工程的第 f+1 拍**。表里的窗口是「这一段被跨过就算数」（生效帧 = min..max+1），");
        sb.Append("因此导出的时候 **min = 抬起那一帧 + 1，max = 落下那一帧 − 1**，");
        sb.Append("这样生效集合正好等于原片子里盒子开着的那几帧，一帧不多一帧不少。");
        sb.Append("盒子只开一帧的情况这条公式给不出来（窗口至少跨两拍），已在各条片子的【可疑】里点名。\n");
        sb.Append("\n这条换算是照着源工程的读法对出来的，不是猜的：它在 MonoBehaviour 的 Update 里读 " +
                  "`hitBox.gameObject.activeSelf`（`UnitActions.cs:344`），而 Animator 在同一帧的更早阶段求值曲线，" +
                  "所以 clip 的第 f 帧恰好在第 f 个渲染帧被读到；本工程 60Hz 下第 f 个渲染帧就是第 f+1 拍。\n\n");
        sb.Append("## 源工程的判定盒规格\n\n");
        sb.Append("源工程全场只有一只 `HitBox`，每招靠 clip 里的位置和缩放曲线把它挪到拳头或脚上——");
        sb.Append("这就是本工程要加「盒子轨迹」（切片 1.5）的原因。常驻规格：\n");
        if (box.found)
        {
            Vector3 world = new Vector3(box.size.x * box.scale.x, box.size.y * box.scale.y, DepthThickness);
            sb.Append("- 局部位置 ").Append(Fmt(box.position)).Append("，局部缩放 ").Append(Fmt(box.scale))
              .Append("，`SpriteRenderer.size` ").Append(Fmt(box.size))
              .Append("（精灵 ").Append(string.IsNullOrEmpty(box.spritePath) ? "未解析" : box.spritePath).Append("）\n");
            sb.Append("- 换成本工程的 3D 盒子：**size = ").Append(Fmt(world))
              .Append("**，中心放在上面那个局部位置；厚度 ").Append(DepthThickness)
              .Append(" 米来自源工程 `targetInZRange(0.5)` 那条纵深判定，不是随手取的\n");
        }
        else sb.Append("- 没读到（源预制体不在 `").Append(SourcePrefabRelative).Append("`），尺寸栏空着\n");
        sb.Append("\n");
    }

    private static void Section(StringBuilder sb, Clip c)
    {
        sb.Append("## ").Append(c.name).Append(" — ").Append(c.durationFrames).Append(" 帧");
        if (c.sampleRate != 60) sb.Append("（sampleRate ").Append(c.sampleRate).Append("）");
        if (c.spriteKeys > 0) sb.Append("，精灵 ").Append(c.spriteKeys).Append(" 张");
        sb.Append("\n\n");

        if (c.windows.Count == 0 && c.cues.Count == 0 && c.trackFrames.Count == 0 && c.enabledWindows.Count == 0)
        {
            sb.Append("纯表现片子：没有判定盒曲线也没有事件。表里就是一行只有 animKey 和 durationFrames 的动作。\n");
            Suspects(sb, c);
            return;
        }

        if (c.windows.Count > 0) sb.Append("- 判定盒窗口（表里的 inFrames）：").Append(Ranges(c.windows)).Append("\n");
        else sb.Append("- 判定盒窗口：没有 m_IsActive 曲线\n");

        if (c.enabledWindows.Count > 0)
            sb.Append("- 精灵可见窗口（m_Enabled，只影响看不看得见，别当判定窗口用）：")
              .Append(Ranges(c.enabledWindows)).Append("\n");

        if (c.trackFrames.Count > 0)
        {
            sb.Append("- 盒子轨迹（局部位置，本工程的拍号）：");
            for (int i = 0; i < c.trackFrames.Count; i++)
                sb.Append("第").Append(c.trackFrames[i] + 1).Append("拍 ").Append(Fmt(c.positions[i])).Append("  ");
            sb.Append("\n- 盒子缩放：");
            for (int i = 0; i < c.trackFrames.Count; i++)
                sb.Append("第").Append(c.trackFrames[i] + 1).Append("拍 ").Append(Fmt(c.scales[i])).Append("  ");
            sb.Append("（世界 size = 这个 × 精灵规格）\n");
        }

        if (c.cues.Count > 0)
        {
            List<string> outs = new List<string>();
            foreach (Cue cue in c.cues) outs.Add("第" + cue.frame + "拍 " + cue.text);
            sb.Append("- cue：").Append(string.Join(" · ", outs)).Append("\n");
            sb.Append("  派发都得走池化（切片 1.5）：源工程每个事件都 `new GameObject + AudioSource`，照搬就撞热路径 0 分配的红线\n");
        }

        if (c.windows.Count > 0)
        {
            sb.Append("- 可以直接抄进表里的片段（tag 是占位名 `").Append(DraftTag)
              .Append("`，1.7 要按部位拆开）：\n\n```\n\"attackPhase\": [{ \"inFrames\": [")
              .Append(PasteRanges(c)).Append(" ], \"tag\": [\"").Append(DraftTag)
              .Append("\"], \"attackPhase\": 0, \"priority\": 0 }]\n```\n");
        }

        Suspects(sb, c);
    }

    private static string Ranges(List<Span> spans)
    {
        List<string> outs = new List<string>();
        foreach (Span w in spans)
            outs.Add("[" + (w.onFrame + 1) + "," + (w.offFrame == int.MaxValue ? "片尾" : (w.offFrame - 1).ToString()) + "]");
        return string.Join(" ", outs);
    }

    private static string PasteRanges(Clip c)
    {
        List<string> outs = new List<string>();
        foreach (Span w in c.windows)
            outs.Add("{\"min\": " + (w.onFrame + 1) + ", \"max\": " +
                     (w.offFrame == int.MaxValue ? c.durationFrames - 1 : w.offFrame - 1) + "}");
        return string.Join(", ", outs);
    }

    private static void Suspects(StringBuilder sb, Clip c)
    {
        foreach (string n in c.notes) sb.Append("- 备注：").Append(n).Append("\n");
        foreach (string s in c.suspects) sb.Append("- **【可疑】** ").Append(s).Append("\n");
        sb.Append("\n");
    }

    private static void Summary(StringBuilder sb, List<Clip> clips)
    {
        int bad = 0, withWindow = 0, withCue = 0;
        foreach (Clip c in clips)
        {
            if (c.suspects.Count > 0) bad++;
            if (c.windows.Count > 0) withWindow++;
            if (c.cues.Count > 0) withCue++;
        }
        sb.Append("---\n\n");
        sb.Append("共 ").Append(clips.Count).Append(" 条：").Append(withWindow).Append(" 条带判定盒窗口，")
          .Append(withCue).Append(" 条带 cue，").Append(bad).Append(" 条有可疑值。\n\n");
        sb.Append("表里还缺的列（伤害、取消标签、指令、受击阶段）在源 clip 里根本没有，")
          .Append("属于玩法决定——由切片 1.7 对照 BeatEmUp 的 `AttackData` 与状态机填，本工具不猜。\n");
    }

    private static string SuspectList(List<Clip> clips)
    {
        StringBuilder sb = new StringBuilder();
        foreach (Clip c in clips)
            foreach (string s in c.suspects)
                sb.Append("  ").Append(c.name).Append("：").Append(s).Append("\n");
        return sb.Length == 0 ? "可疑值：0 条" : "可疑值清单：\n" + sb;
    }

    private static string Fmt(Vector3 v)
    {
        return "(" + v.x.ToString("0.####") + ", " + v.y.ToString("0.####") + ", " + v.z.ToString("0.####") + ")";
    }
}
