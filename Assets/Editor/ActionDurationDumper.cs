using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 解决的问题：Phase 2 第3步把表里所有"该动作的百分之多少"换成了绝对逻辑帧，
/// 于是"表里的 durationFrames 和 clip 实际长度对不对得上"变成了一件事关手感、
/// 又完全没有运行期兜底的隐式约定——美术重导一次动画就漂一次。
/// FBX 的 .meta 里只有 firstFrame/lastFrame、没有现成的秒数，(last-first)/fps 还是 (last-first+1)/fps
/// 差一帧就会让每一个判定窗口偏两帧——所以数必须由 Unity 自己报。
/// 顺带查空窗口（min &gt; max）和越界窗口：这两种在运行期都是盒子静默不开。
/// 跑法：菜单 Tools → ACT → 打印动作时长，改完动画或改完表都跑一遍，看有没有 ✗ 和【】。
/// </summary>
public static class ActionDurationDumper
{
    private const string TablePath = "Assets/Resources/GameData/Action.json";
    /// <summary>
    /// 角色 prefab 放的地方。时长校对要读的是 prefab 上那张控制器，所以这里只扫这个目录，
    /// 不去碰美术目录里那些与招式表无关的控制器
    /// </summary>
    private const string PrefabFolder = "Assets/Resources";

    [MenuItem("Tools/ACT/打印动作时长（填 durationFrames 用）")]
    public static void Dump()
    {
        //状态长度来自**每个角色 prefab 自己那张控制器**：现在场上有两套骨架
        //（3D 骨骼的 Player.prefab 和 HD-2D 精灵的 BeatEmUpPlayer.prefab），
        //只盯着一张的话，另一家的 animKey 全会被误报成"找不到状态"
        Dictionary<string, float> seconds = new Dictionary<string, float>();
        foreach (string path in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder }))
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(path);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            AnimatorController controller = ControllerOf(prefab, assetPath);
            if (controller) CollectInto(controller, seconds);
        }
        if (seconds.Count == 0)
        {
            Debug.LogError(PrefabFolder + " 下面一个带 AnimatorController 的 prefab 都没有：状态长度量不到");
            return;
        }

        TextAsset table = AssetDatabase.LoadAssetAtPath<TextAsset>(TablePath);
        if (!table)
        {
            Debug.LogError("找不到 " + TablePath);
            return;
        }

        ActionInfoContainer container = JsonUtility.FromJson<ActionInfoContainer>(table.text);

        StringBuilder sb = new StringBuilder();
        sb.Append("动作时长（逻辑帧 = 秒 x 60，一个逻辑帧 ").Append(LogicClock.Tick.ToString("0.0000000")).Append(" 秒）\n");
        List<string> paste = new List<string>();
        foreach (ActionInfo info in container.data)
        {
            if (string.IsNullOrEmpty(info.id)) continue;

            float sec;
            if (!seconds.TryGetValue(info.animKey, out sec))
            {
                sb.Append("【找不到状态】 ").Append(info.id).Append(" 的 animKey = \"").Append(info.animKey)
                  .Append("\" 在控制器里没有同名状态，CrossFade 一直是静默失败的\n");
                continue;
            }

            float raw = sec / LogicClock.Tick;
            int frames = Mathf.RoundToInt(raw);
            string note = Mathf.Abs(raw - frames) > 0.02f
                ? "  ⚠不是 1/60 秒的整数倍（30帧或60帧authored的都会是），取整会丢精度"
                : "";
            //表里的durationFrames是运行期唯一的时长真源，美术重导clip之后必须跟着改，差一帧所有窗口就全偏
            string drift = info.durationFrames == frames
                ? ""
                : "  ✗表里填的是 " + info.durationFrames + "，和实际的 " + frames + " 对不上，改表";
            sb.Append(info.id).Append(" | animKey ").Append(info.animKey)
              .Append(" | ").Append(sec.ToString("0.000000")).Append(" 秒 | ")
              .Append(frames).Append(" 帧").Append(note).Append(drift).Append("\n");
            paste.Add("\"" + info.id + "\": " + frames);
            CheckWindows(info, frames, sb);
        }

        sb.Append("\n可以直接抄进表里的：\n").Append(string.Join("\n", paste));
        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// 窗口取整有可能把窄窗口压成空区间（min > max），或者整段落在动作之外，
    /// 这两种在运行期都是静默不开盒，所以这里必须点名
    /// </summary>
    private static void CheckWindows(ActionInfo info, int durationFrames, StringBuilder sb)
    {
        if (info.attackPhase != null)
            foreach (AttackBoxTurnOnInfo phase in info.attackPhase)
                Scan(info.id, "attackPhase", phase.inFrames, durationFrames, sb);
        if (info.defensePhase != null)
            foreach (BeHitBoxTurnOnInfo phase in info.defensePhase)
                Scan(info.id, "defensePhase", phase.inFrames, durationFrames, sb);
        if (info.beCancelledTag != null)
            foreach (BeCancelledTag tag in info.beCancelledTag)
                ScanOne(info.id, "beCancelledTag", tag.cancelWindowFrames, durationFrames, sb);
        if (info.inputAcceptance != null)
            foreach (MoveInputAcceptance acceptance in info.inputAcceptance)
                ScanOne(info.id, "inputAcceptance", acceptance.range, durationFrames, sb);
    }

    private static void Scan(string actionId, string where, FrameRange[] ranges, int durationFrames, StringBuilder sb)
    {
        if (ranges == null) return;
        foreach (FrameRange range in ranges)
            ScanOne(actionId, where, range, durationFrames, sb);
    }

    private static void ScanOne(string actionId, string where, FrameRange range, int durationFrames, StringBuilder sb)
    {
        if (range.min > range.max)
            sb.Append("【空窗口】 ").Append(actionId).Append(" 的 ").Append(where)
              .Append(" 是 [").Append(range.min).Append(", ").Append(range.max)
              .Append("]，min 已经大于 max，这个盒子永远不会开\n");
        else if (range.min >= durationFrames)
            sb.Append("【越界】 ").Append(actionId).Append(" 的 ").Append(where)
              .Append(" 从第 ").Append(range.min).Append(" 帧开，但这个动作只有 ").Append(durationFrames).Append(" 帧\n");
    }

    /// <summary>
    /// 这个 prefab 根节点上的 Animator 挂的那张控制器。挂的不是单个 AnimatorController（比如 AnimatorOverrideController）
    /// 就量不了，宁可喊出来也不要悄悄少一家
    /// </summary>
    private static AnimatorController ControllerOf(GameObject prefab, string assetPath)
    {
        if (!prefab) return null;
        Animator animator = prefab.GetComponent<Animator>();
        if (!animator) return null;
        if (!animator.runtimeAnimatorController)
        {
            Debug.LogWarning(assetPath + " 的 Animator 没挂控制器，这个角色的 animKey 校对不到");
            return null;
        }
        AnimatorController controller = animator.runtimeAnimatorController as AnimatorController;
        if (!controller)
            Debug.LogError(assetPath + " 挂的不止一个控制器或者不是 AnimatorController，量不到状态长度");
        return controller;
    }

    /// <summary>
    /// 状态名 → 秒。注意这里的键是Animator状态名（表里的animKey就是它），不是clip名，两者可以不同名
    /// </summary>
    private static void CollectInto(AnimatorController controller, Dictionary<string, float> seconds)
    {
        foreach (AnimatorControllerLayer layer in controller.layers)
            Collect(layer.stateMachine, seconds, controller.name);
    }

    private static void Collect(AnimatorStateMachine stateMachine, Dictionary<string, float> seconds, string fromController)
    {
        foreach (ChildAnimatorState child in stateMachine.states)
        {
            AnimatorState state = child.state;
            AnimationClip clip = state.motion as AnimationClip;
            if (!clip)
            {
                Debug.LogWarning("状态 " + state.name + " 的 motion 不是单个AnimationClip（可能是BlendTree），" +
                                 "长度量不出来，需要手工数");
                continue;
            }

            if (seconds.ContainsKey(state.name))
                Debug.LogWarning("状态名 " + state.name + " 在 " + fromController + " 与其他控制器的状态重名，" +
                                 "表里的 animKey 会随机撞到其中一个，先改名");

            //状态上的speed会把播放时长缩放，所以要除掉；这工程里现在全是1
            seconds[state.name] = state.speed == 0f ? clip.length : clip.length / state.speed;
        }

        foreach (ChildAnimatorStateMachine sub in stateMachine.stateMachines)
            Collect(sub.stateMachine, seconds, fromController);
    }
}
