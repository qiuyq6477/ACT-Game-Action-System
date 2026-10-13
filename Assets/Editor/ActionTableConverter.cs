using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using CombatFramework;

///<summary>
///动作表在 `ActionAsset` 资产与 JSON 之间的三个工具。
///
///运行期读的是 `ActionTableAsset`（`GameData.Load`），
///`Assets/Tables/Snapshot/Action.json` 是**从资产导出的只读快照**——给人看、给 git 读 diff、
///改坏了能对照上一版还原（它逮过一次误拖）。它不参与运行期，也不放在 `Resources/` 下。
///
///三个菜单：
///- `动作表资产 → 导出 JSON 快照`：日常用这个，导完立刻自比一次。
///- `动作表资产 ⇄ JSON 对拍（不写表）`：**快照落后于资产**时报红，防止改了资产忘了导出。
///- `动作表 JSON → 资产（并逐字段对拍）`：灾备入口——只剩快照时用它重建资产。
///   它会按快照覆写资产，所以只在"快照才是对的"那种情况下跑。
///
///对拍的口径（三条，都在下面实现）：
///① **结构相等**：两侧都归一成 `ActionInfo[]`，用反射逐字段深比（跳过 [NonSerialized] 的烘焙字段），
///   数组长度与顺序都必须一致——顺序是抢动作平手的 tie-break，不是排版。
///② **走一遍文本**：资产 → ToInfo → `JsonUtility.ToJson` → `FromJson` → 再比，
///   这样枚举落成 int、null 数组变空数组这类序列化意外也会被抓到，而不是等运行期。
///③ **`说明` 只比多重集**：快照里它一行是字符串、一行是字符串数组，`ActionInfo` 没这个字段
///   （JsonUtility 会直接忽略），所以无法参与①；见 Doc/plan/action-editor.md 的已知限制。
///</summary>
public static class ActionTableConverter
{
    ///<summary>下面三个常量与两个方法 public：常驻对拍用例必须和菜单用**同一把尺**，不许各写一份</summary>
    ///<remarks>
    ///表在 `Assets/Tables/Snapshot/`，不在 `Resources/` 下：运行期读的是 `ActionTableAsset`，
    ///这个 JSON 只是给人看的快照，留在 Resources 里会被打进包体却没人读。
    ///</remarks>
    public const string TablePath = "Assets/Tables/Snapshot/Action.json";
    private const string ActionDir = "Assets/Tables/Actions";
    public const string ContainerPath = "Assets/Resources/GameData/ActionTable.asset";

    [MenuItem("Tools/ACT/动作表资产 → 导出 JSON 快照（运行期不读它）")]
    public static void ExportSnapshot(){
        ActionTableAsset table = AssetDatabase.LoadAssetAtPath<ActionTableAsset>(ContainerPath);
        if (table == null || table.actions == null || table.actions.Length == 0){
            Debug.LogError("找不到 " + ContainerPath);
            return;
        }
        var rows = new ActionInfo[table.actions.Length];
        for (int i = 0; i < table.actions.Length; i++) rows[i] = ActionAssetConvert.ToInfo(table.actions[i]);

        // JsonUtility 不认 `说明`（ActionInfo 里没这个字段），所以先把行吐出来，再按资产里的值插回去
        string json = JsonUtility.ToJson(new ActionInfoContainer{ data = rows }, true);
        int injected = 0;
        for (int i = 0; i < table.actions.Length; i++){
            ActionAsset a = table.actions[i];
            if (a == null || a.说明 == null || a.说明.Length == 0) continue;
            json = InsertComment(json, a.id, a.说明);
            injected++;
        }

        EnsureFolder(System.IO.Path.GetDirectoryName(TablePath).Replace('\\', '/'));
        // 带 UTF-8 BOM 写回，与这份表一直的编码一致（少了 BOM，别人用工具打开会看到乱码）
        var bytes = new System.Collections.Generic.List<byte>{ 0xEF, 0xBB, 0xBF };
        bytes.AddRange(System.Text.Encoding.UTF8.GetBytes(json));
        System.IO.File.WriteAllBytes(TablePath, bytes.ToArray());
        AssetDatabase.ImportAsset(TablePath);
        AssetDatabase.Refresh();

        // 导完立刻自比一次：快照与资产对不上就是发射器写错了，别让下一轮对拍才发现
        string raw;
        ActionInfo[] again = LoadJsonRows(out raw);
        var issues = again != null ? Roundtrip(table, again, raw) : new List<string>{ "快照解析不回来" };
        var sb = new System.Text.StringBuilder();
        sb.Append("\n──── 导出动作表快照 ────\n").Append(TablePath)
          .Append("：").Append(rows.Length).Append(" 行，`说明` 回填 ").Append(injected).Append(" 条\n");
        sb.Append(issues.Count == 0
            ? "✓ 快照与资产逐字段相等\n（注：快照由发射器排版，键序与缩进与最初手写的那份不同——"
              + "第一次导出会看到整文件重排，之后就稳定了）"
            : "✗ 快照回读与资产不一致：\n  " + string.Join("\n  ", issues.ToArray()));
        Debug.Log(sb.ToString());
    }

    ///<summary>把 `说明` 插在这一行 `"id"` 之后；单条还原成裸字符串，多条还原成数组（与表的两种原始形态一致）</summary>
    private static string InsertComment(string json, string id, string[] comment){
        string anchor = "\"id\": \"" + id + "\"";
        int at = json.IndexOf(anchor, System.StringComparison.Ordinal);
        if (at < 0) return json;
        var sb = new System.Text.StringBuilder();
        if (comment.Length == 1){
            sb.Append("\"").Append(Escape(comment[0])).Append("\"");
        }else{
            sb.Append("[");
            for (int i = 0; i < comment.Length; i++){
                if (i > 0) sb.Append(", ");
                sb.Append("\"").Append(Escape(comment[i])).Append("\"");
            }
            sb.Append("]");
        }
        int insertAt = at + anchor.Length;
        return json.Substring(0, insertAt) + ",\n            \"说明\": " + sb + json.Substring(insertAt);
    }

    private static string Escape(string s){
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    [MenuItem("Tools/ACT/动作表 JSON → 资产（并逐字段对拍）")]
    public static void Migrate(){
        var rows = LoadJsonRows(out string rawText);
        if (rows == null) return;
        EnsureFolder(ActionDir);

        var created = new List<ActionAsset>();
        var problems = new List<string>();
        for (int i = 0; i < rows.Length; i++){
            ActionAsset a = BuildAsset(rows[i], ExtractComment(rawText, rows[i].id), problems);
            created.Add(a);
        }
        if (problems.Count > 0){
            foreach (string p in problems) Debug.LogError("重建中止：" + p);
            Debug.LogError("中止：资产一个都没写。上面每一条都是「表里有一种形状资产表达不了」，"
                + "要么扩资产、要么改表，不许静默丢数据。");
            return;
        }

        // 逐招落盘（已存在就原地更新，GUID 不变——prefab 与别的资产可能引用着它）
        var inContainer = new ActionAsset[created.Count];
        for (int i = 0; i < created.Count; i++){
            string path = ActionDir + "/" + created[i].id + ".asset";
            ActionAsset existing = AssetDatabase.LoadAssetAtPath<ActionAsset>(path);
            if (existing == null){
                AssetDatabase.CreateAsset(created[i], path);
            }else{
                EditorUtility.CopySerialized(created[i], existing);
                EditorUtility.SetDirty(existing);
            }
            inContainer[i] = AssetDatabase.LoadAssetAtPath<ActionAsset>(path);
        }

        ActionTableAsset table = AssetDatabase.LoadAssetAtPath<ActionTableAsset>(ContainerPath);
        bool newTable = table == null;
        if (newTable) table = ScriptableObject.CreateInstance<ActionTableAsset>();
        table.actions = inContainer;
        EditorUtility.SetDirty(table);
        if (newTable) AssetDatabase.CreateAsset(table, ContainerPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(Report(Roundtrip(table, rows, rawText),
            "已写 " + inContainer.Length + " 个动作资产 + 容器 " + ContainerPath
            + (newTable ? "（新建）" : "（更新）") + "。Action.json 原样未动。"));
    }

    [MenuItem("Tools/ACT/动作表资产 ⇄ JSON 对拍（不写表）")]
    public static void VerifyOnly(){
        var rows = LoadJsonRows(out string rawText);
        if (rows == null) return;
        ActionTableAsset table = AssetDatabase.LoadAssetAtPath<ActionTableAsset>(ContainerPath);
        if (table == null || table.actions == null || table.actions.Length == 0){
            Debug.LogError("找不到 " + ContainerPath + "，先跑「动作表 JSON → 资产」");
            return;
        }
        Debug.Log(Report(Roundtrip(table, rows, rawText), "只读对拍：" + table.actions.Length + " 个资产 vs " + TablePath));
    }

    //──────────── 对拍 ────────────

    ///<summary>资产 → 表结构 → JSON 文本 → 再解析回来，与原表逐字段比。返回红绿清单。public：常驻用例跑同一条</summary>
    public static List<string> Roundtrip(ActionTableAsset table, ActionInfo[] jsonRows, string rawText){
        var issues = new List<string>();
        Normalized.Clear();
        if (table.actions.Length != jsonRows.Length){
            issues.Add("行数不符：表 " + jsonRows.Length + " 行，资产 " + table.actions.Length + " 个");
            return issues;
        }
        var fromAsset = new ActionInfo[table.actions.Length];
        for (int i = 0; i < table.actions.Length; i++){
            ActionAsset a = table.actions[i];
            if (a == null){
                issues.Add("容器第 " + i + " 格是空的（引用丢了）");
                fromAsset[i] = default;
                continue;
            }
            fromAsset[i] = ActionAssetConvert.ToInfo(a);
        }

        // 走一遍 JSON 文本，把序列化的意外也算进对拍（枚举落成 int、null 数组变空数组这类）
        var wrapper = new ActionInfoContainer{ data = fromAsset };
        string emitted = JsonUtility.ToJson(wrapper);
        ActionInfo[] reparsed = JsonUtility.FromJson<ActionInfoContainer>(emitted).data;
        if (reparsed == null || reparsed.Length != jsonRows.Length){
            issues.Add("资产生成的 JSON 解析不回来（" + (reparsed == null ? "null" : reparsed.Length + " 行")
                + "），说明 ToJson 的形状和表本身已经不是同一套");
            return issues;
        }

        for (int i = 0; i < jsonRows.Length; i++){
            string where = "第 " + i + " 行 " + jsonRows[i].id;
            if (reparsed[i].id != jsonRows[i].id){
                issues.Add(where + "：顺序错了，资产第 " + i + " 格是 '" + reparsed[i].id
                    + "'。数组顺序=平手 tie-break，不是排版问题。");
                continue;
            }
            DiffFields(jsonRows[i], reparsed[i], where, issues, Normalized);
        }
        foreach (string s in CommentDiff(table, rawText)) issues.Add(s);
        return issues;
    }

    ///<summary>上一轮对拍里被归一的空值形状差异，逐条留名（报告里要点名出来）</summary>
    public static readonly List<string> Normalized = new List<string>();

    private static string Report(List<string> issues, string header){
        var sb = new System.Text.StringBuilder();
        sb.Append("\n──── 动作表对拍（" ).Append(header).Append("）────\n");
        if (issues.Count == 0){
            sb.Append("✓ 逐字段相等：").Append("全部对上，可以进入下一步（迁 GameData.Load 并删 JSON）\n");
        }else{
            sb.Append("✗ 有 ").Append(issues.Count).Append(" 处不一致，**不许删 Action.json**：\n");
            for (int i = 0; i < issues.Count; i++) sb.Append("  ").Append(i + 1).Append(") ").Append(issues[i]).Append("\n");
        }
        if (Normalized.Count > 0){
            sb.Append("△ ").Append(Normalized.Count).Append(" 处 null↔空值 被归一（JsonUtility 的空值形状差异，非数据差异）：\n");
            for (int i = 0; i < Normalized.Count && i < 4; i++) sb.Append("  · ").Append(Normalized[i]).Append("\n");
            if (Normalized.Count > 4) sb.Append("  · …其余 ").Append(Normalized.Count - 4).Append(" 条同类\n");
        }
        return sb.ToString();
    }

    ///<summary>
    ///反射逐字段深比。跳过 [NonSerialized]（那两个是 GameData.Load 的烘焙产物，不在表里）。
    ///
    ///`normalized` 收的是"JsonUtility 的空值形状差异"：表里没写这个键，解析出来是 null；
    ///资产转回 JSON 时 JsonUtility 只会吐 ""/[]，永远不会吐 null。两边语义相同
    ///（`GameData.Load` 把空 method 当"这一招没有位移"），所以判等，但**必须逐条点名**——
    ///静默放行等于把比对器变成一个会说谎的东西。
    ///</summary>
    public static void DiffFields<T>(T expected, T actual, string path, List<string> issues, List<string> normalized = null){
        foreach (FieldInfo f in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance)){
            if (f.IsDefined(typeof(System.NonSerializedAttribute), false)) continue;
            CompareValue(f.GetValue(expected), f.GetValue(actual), path + "." + f.Name, issues, normalized);
        }
    }

    private static void CompareValue(object exp, object act, string path, List<string> issues, List<string> normalized){
        // 空值形状：null / "" / 空数组 在本表语义里都是"这个字段没填"。
        // 只有**一侧真的是 null** 才算被归一——两边都是空数组的话本来就该走下面的相等比较，
        // 别把它报成"表里缺键"（数组实例不同引用必然不等，早期版本就是这么误报了 106 条）。
        if (exp == null || act == null){
            object other = exp == null ? act : exp;
            if (other == null) return;
            if (IsEmptyShape(other)){
                if (normalized != null){
                    normalized.Add(path + "：一边是 null（JsonUtility 读表时缺键就给 null，吐 JSON 时只会吐 "
                        + (other is string ? "空串" : "空数组") + "），语义相同");
                }
                return;
            }
            issues.Add(path + "：一边是 null，另一边是 " + other + " —— 这不是空值形状差异，是真丢了数据");
            return;
        }
        if (exp is System.Array ea && act is System.Array aa){
            if (ea.Length != aa.Length){
                issues.Add(path + "：数组长度 " + ea.Length + " vs " + aa.Length);
                return;
            }
            for (int i = 0; i < ea.Length; i++) CompareValue(ea.GetValue(i), aa.GetValue(i), path + "[" + i + "]", issues, normalized);
            return;
        }
        if (exp.GetType().IsValueType && !exp.GetType().IsEnum && IsNumericish(exp)){
            float x = System.Convert.ToSingle(exp), y = System.Convert.ToSingle(act);
            if (!Mathf.Approximately(x, y)) issues.Add(path + "：" + exp + " vs " + act);
            return;
        }
        if (exp is string || exp.GetType().IsEnum || exp.GetType().IsPrimitive){
            if (!exp.Equals(act)) issues.Add(path + "：" + exp + " vs " + act);
            return;
        }
        // 嵌套结构体/类：递归
        foreach (FieldInfo f in exp.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)){
            if (f.IsDefined(typeof(System.NonSerializedAttribute), false)) continue;
            CompareValue(f.GetValue(exp), f.GetValue(act), path + "." + f.Name, issues, normalized);
        }
    }

    ///<summary>null / 空串 / 空数组——三者在这张表里是同一件事："没填"</summary>
    private static bool IsEmptyShape(object v){
        if (v == null) return true;
        if (v is string s) return s.Length == 0;
        if (v is System.Array a) return a.Length == 0;
        return false;
    }

    private static bool IsNumericish(object v){
        return v is int || v is float || v is long || v is double || v is short || v is byte;
    }

    //──────────── 说明（策划注释）────────────

    ///<summary>
    ///原表里 `说明` 有两种形态：`HurtFromForward` 是字符串数组、`TurnBack` 是裸字符串，
    ///而 `ActionInfo` 没这个字段（JsonUtility 遇到多余键会直接忽略），所以它进不了逐字段比对，
    ///只能单独比一次**多重集**（只比值集合与个数，不比排版形态）。public：常驻用例要用同一把尺。
    ///</summary>
    public static List<string> CommentDiff(ActionTableAsset table, string rawText){
        var issues = new List<string>();
        var want = new List<string>();
        foreach (string id in IdsOf(table)){
            string c = ExtractComment(rawText, id);
            if (c != null) want.Add(id + "=" + c);
        }
        var got = new List<string>();
        for (int i = 0; i < table.actions.Length; i++){
            ActionAsset a = table.actions[i];
            if (a == null || a.说明 == null || a.说明.Length == 0) continue;
            got.Add(a.id + "=" + string.Join("␟", a.说明));
        }
        want.Sort(); got.Sort();
        if (want.Count != got.Count) issues.Add("说明：条数 " + want.Count + " vs " + got.Count);
        else for (int i = 0; i < want.Count; i++) if (want[i] != got[i]) issues.Add("说明 不等：" + want[i] + " vs " + got[i]);
        return issues;
    }

    private static IEnumerable<string> IdsOf(ActionTableAsset table){
        for (int i = 0; i < table.actions.Length; i++)
            if (table.actions[i] != null) yield return table.actions[i].id;
    }

    ///<summary>
    ///从原文里抠出某一行的 `说明`。不用 JsonUtility：它按 `ActionInfo` 的字段走，多出来的键直接丢。
    ///做法是从这一行的 `"id"` 往后扫到同深度的下一个 `"`id"`，在这段里找 `说明`，
    ///值是裸字符串还是数组都吃下来，返回归一后的 "a␟b" 形式（数组）或原串。
    ///</summary>
    private static string ExtractComment(string json, string id){
        int at = json.IndexOf("\"id\":\"" + id + "\"", System.StringComparison.Ordinal);
        if (at < 0) at = json.IndexOf("\"id\": \"" + id + "\"", System.StringComparison.Ordinal);
        if (at < 0) return null;
        int next = at + 1;
        int found = json.IndexOf("\"id\"", next, System.StringComparison.Ordinal);
        int end = found < 0 ? json.Length : found;
        int key = json.IndexOf("说明", next, System.StringComparison.Ordinal);
        if (key < 0 || key > end) return null;
        int colon = json.IndexOf(':', key);
        int i = colon + 1;
        while (i < json.Length && (json[i] == ' ' || json[i] == '\t' || json[i] == '\n' || json[i] == '\r')) i++;
        if (i < json.Length && json[i] == '['){
            int close = json.IndexOf(']', i);
            string body = json.Substring(i + 1, close - i - 1);
            var parts = new List<string>();
            foreach (string raw in body.Split(',')){
                string s = raw.Trim();
                parts.Add(s.Trim('"'));
            }
            return string.Join("␟", parts.ToArray());
        }
        int q1 = json.IndexOf('"', i);
        int q2 = json.IndexOf('"', q1 + 1);
        return json.Substring(q1 + 1, q2 - q1 - 1);
    }

    //──────────── 表结构 → 资产（正向）────────────

    ///<summary>
    ///一行表 → 一个资产。**只处理能无损表达的**：任何"一种形状装不进另一种形状"的情况一律记进
    /// <paramref name="problems"/> 让整次重建中止，绝不静默丢数据（丢一格窗口，症状是"这一拳有时打不到"）。
    ///</summary>
    private static ActionAsset BuildAsset(ActionInfo info, string comment, List<string> problems){
        var a = ScriptableObject.CreateInstance<ActionAsset>();
        a.name = info.id;
        a.id = info.id;
        a.animKey = info.animKey;
        a.catalog = info.catalog;
        a.duration = ActionAssetConvert.SecOf(info.durationFrames);
        a.priority = info.priority;
        a.autoNextActionId = info.autoNextActionId;
        a.keepPlayingAnim = info.keepPlayingAnim;
        a.autoTerminate = info.autoTerminate;
        a.flip = info.flip;
        a.rootMotionTween = info.rootMotionTween;
        a.attacks = info.attacks ?? new AttackInfo[0];
        a.说明 = SplitComment(comment);

        CancelTag[] ct = info.cancelTag ?? new CancelTag[0];
        a.cancelTags = new CancelTagAsset[ct.Length];
        for (int i = 0; i < ct.Length; i++){
            a.cancelTags[i] = new CancelTagAsset{
                tag = ct[i].tag,
                startFrom = ActionAssetConvert.SecOf(ct[i].startFromFrame),
                priority = ct[i].priority,
                fadeInFrames = ct[i].fadeInFrames,
            };
        }

        BeCancelledTag[] bt = info.beCancelledTag ?? new BeCancelledTag[0];
        a.beCancelledTags = new BeCancelledTagAsset[bt.Length];
        for (int i = 0; i < bt.Length; i++){
            a.beCancelledTags[i] = new BeCancelledTagAsset{
                windowMin = ActionAssetConvert.SecOf(bt[i].cancelWindowFrames.min),
                windowMax = ActionAssetConvert.SecOf(bt[i].cancelWindowFrames.max),
                cancelTag = bt[i].cancelTag ?? new string[0],
                priority = bt[i].priority,
                fadeOutFrames = bt[i].fadeOutFrames,
            };
        }

        TempBeCancelledTag[] tt = info.tempBeCancelledTag ?? new TempBeCancelledTag[0];
        a.tempBeCancelledTags = new TempBeCancelledTagAsset[tt.Length];
        for (int i = 0; i < tt.Length; i++){
            a.tempBeCancelledTags[i] = new TempBeCancelledTagAsset{
                id = tt[i].id,
                duration = ActionAssetConvert.SecOf(tt[i].lengthFrames),
                cancelTag = tt[i].cancelTag ?? new string[0],
                priority = tt[i].priority,
                fadeOutFrames = tt[i].fadeOutFrames,
            };
        }

        MoveInputAcceptance[] ma = info.inputAcceptance ?? new MoveInputAcceptance[0];
        a.inputAcceptance = new MoveInputAcceptanceAsset[ma.Length];
        for (int i = 0; i < ma.Length; i++){
            a.inputAcceptance[i] = new MoveInputAcceptanceAsset{
                windowMin = ActionAssetConvert.SecOf(ma[i].range.min),
                windowMax = ActionAssetConvert.SecOf(ma[i].range.max),
                rate = ma[i].rate,
            };
        }

        // 搓招：本工程每招至多一条命令，所以资产的单条 inputSequence 够装；多于一条就必须停下来问
        ActionCommand[] cmd = info.commands ?? new ActionCommand[0];
        if (cmd.Length > 1){
            problems.Add(info.id + "：有 " + cmd.Length + " 条 commands，资产的单条 inputSequence 装不下");
        }else if (cmd.Length == 1){
            KeyMap[] keys = cmd[0].keySequence ?? new KeyMap[0];
            if (keys.Length == 0){
                problems.Add(info.id + "：commands[0].keySequence 是空的，转成空 inputSequence 会让"
                    + " validInFrames 变成一个没有意义的数");
            }else{
                var names = new string[keys.Length];
                for (int i = 0; i < keys.Length; i++){
                    if (ActionAssetConvert.TryKeyName(keys[i], out string n)) names[i] = n;
                    else{
                        problems.Add(info.id + "：keySequence[" + i + "]=" + keys[i]
                            + " 不在对接表里（KeyMap↔ActionKeyBits 词表没这一项，见 ActionAssetConvert.TryKeyName）");
                        names[i] = keys[i].ToString();
                    }
                }
                a.inputSequence = names;
                a.inputValidSec = ActionAssetConvert.SecOf(cmd[0].validInFrames);
            }
        }

        TrackFrom(a, info.attackPhase, "攻击", info.id, problems);
        TrackFrom(a, info.defensePhase, "受击", info.id, problems);
        PreserveEditOnly(a);
        return a;
    }

    ///<summary>
    ///窗 → 轨道。本工程每条 phase 恒为 1 tag × 1 段窗（实测 15 行皆如此），
    ///出现第二条就说明表的形状超过了轨道能表达的，中止而不是"取第一条"。
    ///</summary>
    private static void TrackFrom(ActionAsset a, AttackBoxTurnOnInfo[] phases, string kind, string id, List<string> problems){
        if (phases == null) return;
        var tracks = new List<AttackBoxTrackAsset>(phases.Length);
        for (int i = 0; i < phases.Length; i++){
            AttackBoxTurnOnInfo p = phases[i];
            if (p.tag == null || p.tag.Length != 1) problems.Add(id + "：" + kind + "phase[" + i + "] 的 tag 有 "
                + (p.tag == null ? 0 : p.tag.Length) + " 个，轨道只能带一个 tag");
            if (p.inFrames == null || p.inFrames.Length != 1) problems.Add(id + "：" + kind + "phase[" + i
                + "] 的 inFrames 有 " + (p.inFrames == null ? 0 : p.inFrames.Length) + " 段，一条轨道只能开一扇窗");
            tracks.Add(new AttackBoxTrackAsset{
                tag = (p.tag != null && p.tag.Length > 0) ? p.tag[0] : "",
                openMin = ActionAssetConvert.SecOf(p.inFrames != null && p.inFrames.Length > 0 ? p.inFrames[0].min : 0),
                openMax = ActionAssetConvert.SecOf(p.inFrames != null && p.inFrames.Length > 0 ? p.inFrames[0].max : 0),
                priority = p.priority,
                ruleIndex = p.attackPhase,
            });
        }
        a.attackBoxes = tracks.ToArray();
    }

    private static void TrackFrom(ActionAsset a, BeHitBoxTurnOnInfo[] phases, string kind, string id, List<string> problems){
        if (phases == null) return;
        var tracks = new List<HitBoxTrackAsset>(phases.Length);
        for (int i = 0; i < phases.Length; i++){
            BeHitBoxTurnOnInfo p = phases[i];
            if (p.tag == null || p.tag.Length != 1) problems.Add(id + "：" + kind + "phase[" + i + "] 的 tag 有 "
                + (p.tag == null ? 0 : p.tag.Length) + " 个，轨道只能带一个 tag");
            if (p.inFrames == null || p.inFrames.Length != 1) problems.Add(id + "：" + kind + "phase[" + i
                + "] 的 inFrames 有 " + (p.inFrames == null ? 0 : p.inFrames.Length) + " 段，一条轨道只能开一扇窗");
            tracks.Add(new HitBoxTrackAsset{
                tag = (p.tag != null && p.tag.Length > 0) ? p.tag[0] : "",
                openMin = ActionAssetConvert.SecOf(p.inFrames != null && p.inFrames.Length > 0 ? p.inFrames[0].min : 0),
                openMax = ActionAssetConvert.SecOf(p.inFrames != null && p.inFrames.Length > 0 ? p.inFrames[0].max : 0),
                priority = p.priority,
                tempBeCancelledTagTurnOn = p.tempBeCancelledTagTurnOn ?? new string[0],
                attackerActionChange = p.attackerActionChange,
                selfActionChange = p.selfActionChange,
            });
        }
        a.hitBoxes = tracks.ToArray();
    }

    ///<summary>
    ///已经烘好的 `keys` 不进快照，重建资产时按 tag 从旧轨搬回来——不搬就等于一次回写清掉美术的烘培结果。
    ///</summary>
    private static void PreserveEditOnly(ActionAsset fresh){
        ActionAsset old = AssetDatabase.LoadAssetAtPath<ActionAsset>(ActionDir + "/" + fresh.id + ".asset");
        if (old == null) return;
        CopyKeys(old.attackBoxes, fresh.attackBoxes);
        CopyKeys(old.hitBoxes, fresh.hitBoxes);
    }

    private static void CopyKeys(ActionBoxTrackBase[] from, ActionBoxTrackBase[] to){
        if (from == null || to == null) return;
        for (int i = 0; i < to.Length; i++){
            for (int j = 0; j < from.Length; j++){
                if (from[j] != null && from[j].tag == to[i].tag && from[j].keys != null && from[j].keys.Length > 0){
                    to[i].keys = from[j].keys;
                    break;
                }
            }
        }
    }

    private static string[] SplitComment(string comment){
        if (string.IsNullOrEmpty(comment)) return new string[0];
        return comment.Split('␟');
    }

    //──────────── 读表 ────────────

    public static ActionInfo[] LoadJsonRows(out string rawText){
        rawText = null;
        TextAsset ta = AssetDatabase.LoadAssetAtPath<TextAsset>(TablePath);
        if (ta == null){
            Debug.LogError("找不到 " + TablePath + "（已经迁过了？那只需要跑对拍菜单）");
            return null;
        }
        // 用 .text 而不是 Encoding.UTF8.GetString(.bytes)：表文件带 UTF-8 BOM，
        // 自己解码会把 BOM 留在串头，JsonUtility 当场报 "JSON parse error: Invalid value"；
        // TextAsset.text 已经解码并去掉 BOM，与运行期 GameData.Load 读的是同一条路。
        rawText = ta.text;
        var container = JsonUtility.FromJson<ActionInfoContainer>(rawText);
        if (container.data == null || container.data.Length == 0){
            Debug.LogError(TablePath + " 解析不出任何行");
            return null;
        }
        return container.data;
    }

    private static void EnsureFolder(string path){
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        string leaf = System.IO.Path.GetFileName(path);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
