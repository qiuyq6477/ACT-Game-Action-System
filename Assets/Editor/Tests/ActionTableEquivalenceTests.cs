using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using CombatFramework;

///<summary>
///动作资产与 JSON 快照之间的**常驻**闸门：`Assets/Resources/GameData/ActionTable.asset`（运行期真源）
///导出的 `Assets/Tables/Snapshot/Action.json`，必须与资产**逐字段相等**——包括数组顺序
///（顺序是抢动作平手的 tie-break，不是排版）。**改了资产忘了导出快照，这里就红**；快照文件没了也红。
///
///它抓不到什么也要说清：快照本来就是资产导出来的，所以这几条比的是"文件跟不跟上真源"，
///不是"表填得对不对"。后者归 `DurationFramesSurviveTheSecondToFrameRoundTrip`（时长那一格）
///和菜单 `Tools/ACT/打印动作时长`（拿 Animator 里的实际长度对照）。
///
///比对逻辑不在这里重写——菜单 `Tools/ACT/动作表资产 ⇄ JSON 对拍（不写表）` 跑的是同一个
///`ActionTableConverter.Roundtrip`，两边必须同进退。
///</summary>
public class ActionTableEquivalenceTests
{
    private ActionInfo[] jsonRows;
    private string rawText;
    private ActionTableAsset table;
    private List<string> issues;

    [SetUp]
    public void SetUp(){
        jsonRows = ActionTableConverter.LoadJsonRows(out rawText);
        if (jsonRows == null){
            Assert.Fail("找不到快照 " + ActionTableConverter.TablePath
                + "：跑菜单 Tools/ACT/动作表资产 → 导出 JSON 快照（快照就是缺着，这正是本闸门要红的情况）");
        }
        table = AssetDatabase.LoadAssetAtPath<ActionTableAsset>(ActionTableConverter.ContainerPath);
        Assert.That(table, Is.Not.Null, "还没有 " + ActionTableConverter.ContainerPath
            + "，先跑菜单 Tools/ACT/动作表 JSON → 资产");
        issues = ActionTableConverter.Roundtrip(table, jsonRows, rawText);
    }

    [Test]
    public void SnapshotMatchesAssets(){
        Assert.That(issues, Is.Empty,
            "快照与资产不一致（改完资产忘了跑「导出 JSON 快照」？）：\n" + string.Join("\n", issues.ToArray()));
    }

    ///<summary>
    ///数组顺序就是抢动作平手时的 tie-break（`ActionController` 用严格 `>`，平手取靠前的一条），
    ///所以"内容全对但顺序换了"同样是回归，不能只比集合。
    ///</summary>
    [Test]
    public void ContainerOrderIsTableRowOrder(){
        Assert.That(table.actions.Length, Is.EqualTo(jsonRows.Length));
        for (int i = 0; i < jsonRows.Length; i++){
            Assert.That(table.actions[i], Is.Not.Null, "容器第 " + i + " 格是空引用");
            Assert.That(table.actions[i].id, Is.EqualTo(jsonRows[i].id), "第 " + i + " 行的顺序变了");
        }
    }

    ///<summary>
    ///被归一的只允许是 `rootMotionTween` 的那两个字段（JsonUtility 读不到键时给 null，资产吐回 JSON 只会写 ""/[]）。
    ///快照是从资产导出的，正常状态下一条都不会有；一旦冒出别的字段，那就是"一边 null、一边有值"，
    ///先在这里红，而不是被静默放行——这条断言防的是归一规则变成一个能吞掉数据的洞。
    ///</summary>
    [Test]
    public void OnlyTheKnownNullShapesWereNormalized(){
        foreach (string n in ActionTableConverter.Normalized){
            Assert.That(n, Does.Contain(".rootMotionTween.method").Or.Contain(".rootMotionTween.param"),
                "出现了一个不在已知清单里的 null↔空值 归一，得先搞清是不是丢了数据：" + n);
        }
    }

    ///<summary>`说明` 一行是裸字符串、一行是字符串数组，进不了逐字段比对，只比多重集</summary>
    [Test]
    public void CommentsSurviveAsMultiset(){
        List<string> commentIssues = ActionTableConverter.CommentDiff(table, rawText);
        Assert.That(commentIssues, Is.Empty, string.Join("\n", commentIssues.ToArray()));
    }

    ///<summary>
    ///时长必须还是运行期那条唯一真源：资产里的秒换算回整数帧之后，要和表里填的数一模一样。
    ///（Roundtrip 已经逐字段比过，这一条是把"为什么比它"写在名字里，顺便钉住换算函数本身没被人改口径）
    ///</summary>
    [Test]
    public void DurationFramesSurviveTheSecondToFrameRoundTrip(){
        for (int i = 0; i < table.actions.Length; i++){
            int fromAsset = ActionAssetConvert.FramesOf(table.actions[i].duration);
            Assert.That(fromAsset, Is.EqualTo(jsonRows[i].durationFrames),
                jsonRows[i].id + "：资产 " + table.actions[i].duration + "s 换算回来是 " + fromAsset
                + " 帧，表里是 " + jsonRows[i].durationFrames + " 帧");
        }
    }
}
