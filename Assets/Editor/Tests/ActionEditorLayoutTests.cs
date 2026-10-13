using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using CombatFramework;
using CombatFrameworkEditor;

namespace CombatFramework.Tests
{
    ///<summary>
    ///动作编辑窗口的**几何与读数**测试。窗口本身要人眼判（CLI 抓不到编辑器窗口），
    ///但"画得对不对"里可判的部分不该因此悬着：lane 的行布局、秒↔像素的映射、
    ///盒轨道那条 lane 的标签与"一个位姿都没烘到就标红"的判据，全在这里钉住。
    ///`LayoutLanes` 同时被画带底色、摆条、命中测试、算整图高度四处读，
    ///它一漂移就是"条画得出来却点不中"——那种 bug 用眼睛看最难看出来。
    ///</summary>
    public class ActionEditorLayoutTests
    {
        //一个和真实窗口同量级的图区：宽 900、高按 6 条 lane 排
        private const float FullW = 900f;
        private static readonly Rect Full = new Rect(0f, 0f, FullW, 400f);

        ///<summary>窗口把图区从"整幅矩形往右挪一个名字栏"处开始，测试跟着它算</summary>
        private static Rect GraphRect(){
            return new Rect(Full.x + ActionEditorWindow.NameGutter, Full.y,
                Full.width - ActionEditorWindow.NameGutter, Full.height);
        }

        private static AttackBoxTrackAsset Track(string tag, float openMin, float openMax, int keys){
            AttackBoxTrackAsset t = new AttackBoxTrackAsset();
            t.tag = tag; t.openMin = openMin; t.openMax = openMax;
            t.keys = new ActionBoxKeyAsset[keys];
            return t;
        }

        private static HitBoxTrackAsset HitTrack(string tag, float openMin, float openMax, int keys){
            HitBoxTrackAsset t = new HitBoxTrackAsset();
            t.tag = tag; t.openMin = openMin; t.openMax = openMax;
            t.keys = new ActionBoxKeyAsset[keys];
            return t;
        }

        //──────────── lane 行布局 ────────────

        [Test]
        public void LayoutLanes_EmptyLaneStillOccupiesARow(){
            //取消出 1 条、取消入 1 条、临时入 1 条、移动接受度 0 条、攻击盒 1 条、受击盒 1 条
            ActionEditorWindow.LaneBand[] bands = ActionEditorWindow.LayoutLanes(new[] { 1, 1, 1, 0, 1, 1 });
            Assert.AreEqual(6, bands.Length);
            for (int i = 0; i < 6; i++){
                Assert.AreEqual(i, bands[i].rowStart, "第 " + i + " 条 lane 的起始行");
                Assert.AreEqual(1, bands[i].rows, "空 lane 也必须占一行，否则它的名字和图区对不上");
            }
            Assert.AreEqual(6, ActionEditorWindow.TotalRows(bands));
        }

        [Test]
        public void LayoutLanes_MultiBarLanesStackAndKeepContiguous(){
            ActionEditorWindow.LaneBand[] bands = ActionEditorWindow.LayoutLanes(new[] { 3, 0, 2, 1, 1, 1 });
            Assert.AreEqual(0, bands[0].rowStart);
            Assert.AreEqual(3, bands[1].rowStart, "三条 bar 的 lane 之后，下一条要往后挪三行");
            Assert.AreEqual(4, bands[2].rowStart);
            Assert.AreEqual(9, ActionEditorWindow.TotalRows(bands), "3+1(空 lane)+2+1+1+1");
            for (int i = 1; i < bands.Length; i++)
                Assert.AreEqual(bands[i - 1].rowStart + bands[i - 1].rows, bands[i].rowStart,
                    "lane 之间不许留空行也不许重叠");
        }

        [Test]
        public void LayoutLanes_RowBandsTileTheGraphAndNothingOverlaps(){
            int[] counts = { 2, 1, 1, 0, 3, 1 };
            ActionEditorWindow.LaneBand[] bands = ActionEditorWindow.LayoutLanes(counts);
            Rect graph = GraphRect();
            List<Rect> rows = new List<Rect>();
            for (int lane = 0; lane < bands.Length; lane++)
                for (int i = 0; i < bands[lane].rows; i++)
                    rows.Add(TimelineGraphGUI.Row(graph, bands[lane].rowStart + i));

            Assert.AreEqual(ActionEditorWindow.TotalRows(bands), rows.Count);
            float rowH = TimelineGraphGUI.RowH;
            for (int i = 0; i < rows.Count; i++){
                Assert.AreEqual(rowH, rows[i].height, 0.001f, "第 " + i + " 行高度");
                if (i > 0){
                    Assert.AreEqual(rows[i - 1].yMax, rows[i].y, 0.001f, "行与行首尾相接：既不叠也不留缝");
                }
            }
            //整张图的高 = 标尺带 + 所有行；最后一行的底边必须正好落在高-6 之内（窗口留了 6px 内边距）
            Assert.LessOrEqual(rows[rows.Count - 1].yMax,
                graph.y + TimelineGraphGUI.RulerBand + ActionEditorWindow.TotalRows(bands) * rowH + 6f);
        }

        //──────────── 秒 ↔ 像素 ────────────

        [Test]
        public void BarRect_RealPunchWindow_LandsWhereTheSecondsSay(){
            //真资产 MeleePunch：duration 0.42s，攻击盒窗 [0.06, 0.20)
            Rect graph = GraphRect();
            TimelineGraphGUI.Span span = TimelineGraphGUI.MakeSpan(graph, 0.42f);
            Rect row = TimelineGraphGUI.Row(graph, 4);            //攻击盒那条 lane
            Rect box = TimelineGraphGUI.BarRect(span, row, 0.06f, 0.20f, 3f, TimelineGraphGUI.RowH - 6f);

            Assert.AreEqual(span.Origin + 0.06f / 0.42f * span.Width, box.x + 3f, 0.01f, "条头");
            Assert.AreEqual(span.Origin + 0.20f / 0.42f * span.Width, box.xMax - 3f, 0.01f, "条尾");
            Assert.AreEqual(row.y, box.y, 0.001f, "条不能骑到隔壁行上");
            Assert.AreEqual(TimelineGraphGUI.RowH - 6f, box.height, 0.001f);
        }

        [Test]
        public void BarRect_NeverPaintsIntoTheNameGutter(){
            Rect graph = GraphRect();
            TimelineGraphGUI.Span span = TimelineGraphGUI.MakeSpan(graph, 0.42f);
            Rect row = TimelineGraphGUI.Row(graph, 0);
            //t=0 是最坏情况：条头再往左扩 pad 就顶到图区左边界
            Rect atZero = TimelineGraphGUI.BarRect(span, row, 0f, 0.1f, 3f, 16f);
            Assert.GreaterOrEqual(atZero.xMin, Full.x + ActionEditorWindow.NameGutter,
                "名字栏是 lane 标签的地方，条压进去就两败俱伤");

            //右端同理：越界值先被 TimeToX 夹住，所以配错的窗（openMax > duration）画不出图区外
            Rect overflow = TimelineGraphGUI.BarRect(span, row, 0.4f, 9f, 3f, 16f);
            Assert.LessOrEqual(overflow.xMax, span.Origin + span.Width + 3f, "越界的窗夹在右边界（pad 之外）");
        }

        [Test]
        public void BarRect_ZeroLengthBarIsStillClickable(){
            Rect graph = GraphRect();
            TimelineGraphGUI.Span span = TimelineGraphGUI.MakeSpan(graph, 0.42f);
            Rect row = TimelineGraphGUI.Row(graph, 0);
            Rect dot = TimelineGraphGUI.BarRect(span, row, 0.2f, 0.2f, 3f, 16f);
            Assert.AreEqual(6f, dot.width, 0.001f, "start==end 靠 pad 撑出看得见、点得中的宽度");

            Vector2 onTheDot = new Vector2(dot.x + dot.width * 0.5f, row.y + TimelineGraphGUI.RowH * 0.5f);
            Assert.IsTrue(TimelineGraphGUI.HitBar(onTheDot, span, row, 0.2f, 0.2f), "命中圈比条宽一圈");
            Assert.IsFalse(TimelineGraphGUI.HitBar(new Vector2(onTheDot.x, row.y + TimelineGraphGUI.RowH),
                span, row, 0.2f, 0.2f), "隔壁行不算命中");
        }

        [Test]
        public void Span_DurationBelowFloorIsClampedNotDividedByZero(){
            Rect graph = GraphRect();
            TimelineGraphGUI.Span span = TimelineGraphGUI.MakeSpan(graph, 0f);
            Assert.AreEqual(TimelineGraphGUI.MinDuration, span.Duration, 0.0001f);
            float x = span.TimeToX(0.02f);
            Assert.IsTrue(x >= span.Origin && x <= span.Origin + span.Width, "时长 0 的资产也不能算出 NaN/越界");
            Assert.IsFalse(float.IsNaN(span.XToTime(graph.x)));
        }

        //──────────── 盒轨道那条 lane 的读数 ────────────

        [Test]
        public void EntryEdge_BoxLanesDragTheirWindowEdges(){
            //盒轨的窗可拖：两头必须各接 openMin/openMax，拖拽/吸附的那条公共路径才接得上。
            //两条 lane 各用自己那个子类验——窗字段在基类上，属性路径还能解析才说明拆分没动到形状
            ActionAsset a = ScriptableObject.CreateInstance<ActionAsset>();
            a.attackBoxes = new[]{ Track("R_Fist", 0.06f, 0.20f, 3) };
            a.hitBoxes = new[]{ HitTrack("Body", 0f, 0.42f, 1) };
            SerializedObject so = new SerializedObject(a);
            SerializedProperty atk = so.FindProperty("attackBoxes").GetArrayElementAtIndex(0);
            SerializedProperty hit = so.FindProperty("hitBoxes").GetArrayElementAtIndex(0);
            Assert.AreEqual("openMin", ActionEditorWindow.EntryEdge(atk, ActionEditorWindow.LaneAtk, true).name,
                "条头=openMin");
            Assert.AreEqual("openMax", ActionEditorWindow.EntryEdge(atk, ActionEditorWindow.LaneAtk, false).name,
                "条尾=openMax");
            Assert.AreEqual("openMin", ActionEditorWindow.EntryEdge(hit, ActionEditorWindow.LaneHit, true).name);
            Assert.AreEqual("openMax", ActionEditorWindow.EntryEdge(hit, ActionEditorWindow.LaneHit, false).name);
        }

        [Test]
        public void BoxLaneRows_LabelCarriesTagIndexAndKeyCount(){
            List<ActionEditorWindow.BoxRow> rows = ActionEditorWindow.BoxLaneRows(new[]{
                Track("R_Fist", 0.06f, 0.20f, 22),
                Track("Blade", 0.30f, 0.42f, 5),
            });
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("R_Fist#0  22 keys", rows[0].label);
            Assert.AreEqual("Blade#1  5 keys", rows[1].label);
            Assert.AreEqual(0.06f, rows[0].start, 0.0001f, "窗直接读资产，不吸附不取整");
            Assert.AreEqual(0.20f, rows[0].end, 0.0001f);
            Assert.IsFalse(rows[0].bad);
            Assert.IsFalse(rows[1].bad);
        }

        [Test]
        public void BoxLaneRows_TrackWithNoBakedPoseIsFlaggedRed(){
            List<ActionEditorWindow.BoxRow> rows = ActionEditorWindow.BoxLaneRows(new[]{
                Track("R_Fist", 0.06f, 0.20f, 22),
                Track("L_Fist", 0.06f, 0.20f, 0),
            });
            Assert.IsFalse(rows[0].bad);
            Assert.IsTrue(rows[1].bad, "开着窗却一个位姿都没烘到=这条轨永远判不到东西，必须标出来");
        }

        [Test]
        public void BoxLaneRows_NullArraysYieldNoRows(){
            Assert.AreEqual(0, ActionEditorWindow.BoxLaneRows(null).Count, "没烘过盒的动作：lane 是空的，不是报错");
            AttackBoxTrackAsset nullKeys = Track("X", 0f, 0f, 3);
            nullKeys.keys = null;
            List<ActionEditorWindow.BoxRow> rows = ActionEditorWindow.BoxLaneRows(new[]{ nullKeys });
            Assert.AreEqual(1, rows.Count);
            Assert.IsTrue(rows[0].bad, "keys 为 null 与 keys 长度为 0 同一条判据");
            Assert.AreEqual("X#0  0 keys", rows[0].label, "null 数组读数收口成 0，不让标签上出现空串");
        }

        [Test]
        public void BoxLaneRows_SameTagSameColorAcrossLanes(){
            //同一条轨在攻击 lane 与受击 lane 里出现时同色：眼睛靠这个把两侧对上号
            List<ActionEditorWindow.BoxRow> a = ActionEditorWindow.BoxLaneRows(new[]{ Track("R_Fist", 0f, 1f, 3) });
            List<ActionEditorWindow.BoxRow> b = ActionEditorWindow.BoxLaneRows(new[]{ Track("R_Fist", 0f, 1f, 7) });
            Color ca = a[0].color, cb = b[0].color;
            Assert.AreEqual(ca.r, cb.r, 0.0001f);
            Assert.AreEqual(ca.g, cb.g, 0.0001f);
            Assert.AreEqual(ca.b, cb.b, 0.0001f);
            List<ActionEditorWindow.BoxRow> other = ActionEditorWindow.BoxLaneRows(new[]{ Track("Body", 0f, 1f, 3) });
            Assert.AreNotEqual(ca.ToString(), other[0].color.ToString(), "不同 tag 撞色只是难看，同 tag 不同色才是骗人");
        }
    }
}
