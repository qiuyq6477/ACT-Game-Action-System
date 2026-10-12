using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 解决的问题：表里的 animKey 必须和 Animator 的**状态名**同名，不然 CrossFade 一直是静默失败
/// （屏幕上的姿势停在上一招，而逻辑已经在下一招了，这种事肉眼看不出来）。
/// 源工程那张 Player.controller 的状态名是 Idle / Run / Punch1 这种短名，
/// 而导出工具产出的 animKey 是 clip 名（PlayerIdle / PlayerRun / …），两边对不上；
/// 更麻烦的是那张控制器是给"Animator 自己推进"用的，框架这里是每逻辑帧手动步进一次并自己决定切哪个状态，
/// 状态之间那些自动转换全是多余的，还会跟帧时钟抢方向盘。
/// 所以这张控制器不手工摆、也不从源工程继承，而是由表生成：表里每一行 → 一个同名状态，转换一条都不建。
/// 重导动画或加了新行之后重跑一次就行——它是派生数据，不是手工资产。
///
/// 跑法：菜单 Tools → ACT → 生成 BeatEmUp 玩家的 AnimatorController，
/// 完事再跑 Tools → ACT → 打印动作时长，那边应当一条"找不到状态"都没有。
/// </summary>
public static class BeatEmUpAnimatorControllerBuilder
{
    private const string TablePath = "Assets/Resources/GameData/Action.json";
    private const string ClipFolder = "Assets/Arts/BeatEmUp/Animation";
    private const string OutFolder = "Assets/Arts/BeatEmUp/Animator";
    private const string OutPath = OutFolder + "/BeatEmUpPlayer.controller";
    private const string LayerName = "Base Layer";

    [MenuItem("Tools/ACT/生成 BeatEmUp 玩家的 AnimatorController")]
    public static void Build()
    {
        TextAsset table = AssetDatabase.LoadAssetAtPath<TextAsset>(TablePath);
        if (!table)
        {
            Debug.LogError("找不到 " + TablePath + "：animKey 的清单就是从这里来的");
            return;
        }

        //同一个 animKey 可能被好几行动作共用（以后换皮时会有），按表里出现的顺序去重
        List<string> keys = new List<string>();
        foreach (ActionInfo info in JsonUtility.FromJson<ActionInfoContainer>(table.text).data)
        {
            if (string.IsNullOrEmpty(info.animKey) || keys.Contains(info.animKey)) continue;
            keys.Add(info.animKey);
        }

        AnimatorController controller = LoadOrCreateController();
        if (!controller) return;
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        ClearStates(machine);

        StringBuilder report = new StringBuilder();
        int made = 0;
        int skipped = 0;
        foreach (string key in keys)
        {
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipFolder + "/" + key + ".anim");
            if (!clip)
            {
                //demo 那 13 行走的是 3D 骨骼的控制器，这里的 clip 目录里当然没有它们，不算错
                skipped++;
                continue;
            }

            AnimatorState state = machine.AddState(key);
            state.motion = clip;
            //状态之间一条转换都不建：切哪个状态、从第几拍开始，全由 ActionController 按逻辑帧决定。
            //源工程那张控制器也是平的（31 个状态、0 条转换），它只把状态名写成了短名（Idle 而不是 PlayerIdle），
            //而这里的规矩是"animKey == 状态名 == clip 名"，三者同名才对得上（打印动作时长就是按这个查的）
            made++;
        }

        //默认状态只在 Animator 自己起手时用到（框架每拍都 CrossFade），给表里第一个建出来的状态就对了
        machine.defaultState = machine.states.Length > 0 ? machine.states[0].state : null;

        EditorUtility.SetDirty(controller);
        EditorUtility.SetDirty(machine);
        AssetDatabase.SaveAssets();

        report.Append("控制器 ").Append(OutPath).Append("：建了 ").Append(made).Append(" 个状态，跳过 ")
              .Append(skipped).Append(" 个在本目录没有同名 clip 的 animKey（demo 的 3D 骨骼行走另一张控制器）\n");

        //【必须重新读盘数一遍】：状态机是控制器的子资产，用 new AnimatorController() 现攒出来的那份
        //AddLayer 出来的层只有一个 m_StateMachine: {fileID: 0}——AddState 全加在内存里，
        //日志报"建了 21 个状态"而盘上是个空壳，Play 里表现为姿势永远停在 prefab 那一帧。
        //这个错只有读盘能发现，所以它是本工具的验收，不是可选的收尾打印
        AnimatorController onDisk = AssetDatabase.LoadAssetAtPath<AnimatorController>(OutPath);
        ChildAnimatorState[] saved = onDisk != null && onDisk.layers.Length > 0
            ? onDisk.layers[0].stateMachine.states
            : new ChildAnimatorState[0];
        if (saved.Length != made)
        {
            report.Append("✗ 读回来只有 ").Append(saved.Length).Append(" 个状态，和刚建的 ").Append(made)
                  .Append(" 个对不上：控制器是空壳，CrossFade 会静默失败\n");
            Debug.LogError(report.ToString());
            return;
        }

        foreach (ChildAnimatorState child in saved)
            report.Append("  ").Append(child.state.name).Append(" ← ").Append(child.state.motion.name)
                  .Append("  ").Append((child.state.motion as AnimationClip).length.ToString("0.0000")).Append(" 秒\n");
        Debug.Log(report.ToString());
    }

    /// <summary>
    /// 有就复用、没有就建。复用的目的是保住 .controller 的 guid——prefab 上的 Animator 引用的是它，
    /// 删了重吹会让引用断掉
    /// </summary>
    private static AnimatorController LoadOrCreateController()
    {
        AnimatorController existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(OutPath);
        if (existing) return existing;

        if (!AssetDatabase.IsValidFolder(OutFolder))
        {
            string parentFolder = OutFolder.Replace('\\', '/').TrimEnd('/');
            int lastSlash = parentFolder.LastIndexOf('/');
            parentFolder = parentFolder.Substring(0, lastSlash);
            if (!AssetDatabase.IsValidFolder(parentFolder))
            {
                Debug.LogError("找不到 " + parentFolder + "，控制器没法放进去");
                return null;
            }
            AssetDatabase.CreateFolder(parentFolder,
                OutFolder.Replace('\\', '/').Substring(lastSlash + 1));
        }

        //这一句自己会建出"一层 + 一个状态机"两个子资产并挂好引用；
        //换成 new AnimatorController() + AddLayer + CreateAsset 的话状态机是空的（见 Build 里那段验收）
        AnimatorController created = AnimatorController.CreateAnimatorControllerAtPath(OutPath);
        if (created.layers.Length > 0) created.layers[0].name = LayerName;
        return created;
    }

    private static void ClearStates(AnimatorStateMachine machine)
    {
        //states 是数组，从后往前删才不会因为边删边变而漏掉
        for (int i = machine.states.Length - 1; i >= 0; i--) machine.RemoveState(machine.states[i].state);
    }
}
