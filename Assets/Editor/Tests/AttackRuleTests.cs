using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using CombatFramework;
using CombatFrameworkEditor;

///<summary>
///攻击轨 → `attacks[]` 那根指针（`ruleIndex`）的三条常驻用例。这条指针是整张表里最容易静默的一格：
///填的不是下标而是 phase 号，查不到时运行期不抛不报错，命中照样成立，只是拿默认的 AttackInfo 结算
///（0 伤害、0 硬直、0 推力）——症状长得像"手感问题"，没人会当 bug 报。
///
///三条各看一层：①出货数据今天确实是好的（15 份资产全扫）；②窗口新建攻击轨落的默认值指着第一条规则
///（-1 是最坏的默认值）；③校验器在"这条轨还没烘位姿"时仍然报指针——`CheckTracks` 在 keys 为空处就
///`continue`，指针检查一旦并进那个循环就会被整段吞掉。
///</summary>
public class AttackRuleTests
{
    private static AttackInfo Rule(int phase){
        AttackInfo r = new AttackInfo();
        r.phase = phase;
        return r;
    }

    private static string Join(List<string> issues){
        return string.Join("\n", issues == null ? new string[0] : issues.ToArray());
    }

    ///<summary>真源容器里每条攻击轨都必须指到本招一条真实的命中规则</summary>
    [Test]
    public void EveryAttackTrackOfEveryShippedAssetResolvesToAPhase(){
        ActionTableAsset table = AssetDatabase.LoadAssetAtPath<ActionTableAsset>(ActionTableConverter.ContainerPath);
        Assert.That(table, Is.Not.Null, "还没有 " + ActionTableConverter.ContainerPath
            + "，先跑菜单 Tools/ACT/动作表资产 → 导出 JSON 快照那一侧的容器");
        int scanned = 0;
        for (int i = 0; i < table.actions.Length; i++){
            ActionAsset a = table.actions[i];
            Assert.That(a, Is.Not.Null, "容器第 " + i + " 格是空引用");
            AttackBoxTrackAsset[] tracks = a.attackBoxes;
            if (tracks == null) continue;
            for (int k = 0; k < tracks.Length; k++){
                AttackBoxTrackAsset t = tracks[k];
                if (t == null) continue;
                scanned++;
                Assert.That(HasPhase(a.attacks, t.ruleIndex), Is.True,
                    a.id + " 的攻击轨[" + k + "]（tag '" + t.tag + "'）ruleIndex=" + t.ruleIndex
                    + " 在 attacks[] 里没有同 phase 的条目：打中就是 0 伤害 0 硬直，运行期一声不响");
            }
        }
        Assert.That(scanned, Is.GreaterThan(0),
            "一条攻击轨都没扫到——容器空了，这条用例就不再看得住任何东西（先查 " + ActionTableConverter.ContainerPath + "）");
    }

    private static bool HasPhase(AttackInfo[] rules, int phase){
        if (rules == null) return false;
        for (int i = 0; i < rules.Length; i++) if (rules[i].phase == phase) return true;
        return false;
    }

    ///<summary>
    ///新建攻击轨的默认指针：指 `attacks[0].phase`，不是数组下标；本招一条规则都没有才留 -1。
    ///phase 0 与下标 0 在出货数据里恰好重合，所以这里故意把第一条规则填成 phase 3——
    ///写错成"下标"的那一版在这份数据上会给出 0，测试当场红。
    ///</summary>
    [Test]
    public void AddEntry_OnAttackTrackPointsAtTheFirstRulePhase(){
        ActionAsset a = ScriptableObject.CreateInstance<ActionAsset>();
        a.attacks = new AttackInfo[]{ Rule(3), Rule(0) };
        SerializedObject so = new SerializedObject(a);
        ActionEditorWindow.AddEntry(ActionEditorWindow.LaneAtk, so.FindProperty("attackBoxes"));
        Assert.AreEqual(3, so.FindProperty("attackBoxes").GetArrayElementAtIndex(0)
            .FindPropertyRelative("ruleIndex").intValue, "默认应当指 attacks[0].phase");

        ActionAsset bare = ScriptableObject.CreateInstance<ActionAsset>();
        SerializedObject bareSo = new SerializedObject(bare);
        ActionEditorWindow.AddEntry(ActionEditorWindow.LaneAtk, bareSo.FindProperty("attackBoxes"));
        Assert.AreEqual(-1, bareSo.FindProperty("attackBoxes").GetArrayElementAtIndex(0)
            .FindPropertyRelative("ruleIndex").intValue, "没有规则可指时才留 -1，由 Validate 报“attacks[] is empty”");
    }

    ///<summary>指针错 + 还没烘位姿：两条都得报出来，不能被 keys 那条 continue 吞掉</summary>
    [Test]
    public void ValidatorReportsBadRuleIndexEvenWhenKeysAreEmpty(){
        ActionAsset a = ScriptableObject.CreateInstance<ActionAsset>();
        a.id = "Probe";
        a.duration = 1f;
        a.attacks = new AttackInfo[]{ Rule(7) };
        AttackBoxTrackAsset t = new AttackBoxTrackAsset();
        t.tag = "Fist";
        t.openMin = 0f;
        t.openMax = 0.5f;
        t.ruleIndex = 99;
        t.keys = new ActionBoxKeyAsset[0];
        a.attackBoxes = new AttackBoxTrackAsset[]{ t };

        List<string> issues = CombatValidator.ValidateActionAsset(a, new List<ActionAsset>{ a });
        Assert.That(Join(issues), Does.Contain("ruleIndex 99"),
            "空 keys 把 ruleIndex 那条一起吞了（CheckAttackRules 必须独立于 CheckTracks 的 continue）：\n" + Join(issues));
    }
}
