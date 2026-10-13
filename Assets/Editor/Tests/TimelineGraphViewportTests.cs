using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CombatFramework;
using CombatFrameworkEditor;

namespace CombatFramework.Tests
{
    ///<summary>
    ///时间轴公共件的**视口与走带**测试。窗口本身要人眼判（CLI 抓不到编辑器窗口），
    ///但缩放/平移的换算、播放头的步进、预览盒采样这三件是纯函数——"缩放后鼠标下的时刻不变"
    ///"铺满视图的映射不许漂"这类判据拍不到就必须钉住，否则一改就是"看得见却点不中"的近亲。
    ///视口输入（滚轮/中键）用合成 Event 直灌 `HandleViewInput`，拖拽落点走 `ApplyBarDrag` 纯函数。
    ///</summary>
    public class TimelineGraphViewportTests
    {
        private const float FullW = 900f;
        private static readonly Rect Graph = new Rect(0f, 0f, FullW, 400f);

        private static float WidthOf(Rect rect){
            return rect.width - TimelineGraphGUI.LeftPad - TimelineGraphGUI.RightPad;
        }

        ///<summary>把合成事件灌进视口泵（GUI 外 Event.current 赋值不生效，走事件参数重载）</summary>
        private static bool PumpView(Event e, float dur, ref float timeStart, ref float zoom, ref bool panDrag){
            return TimelineGraphGUI.HandleViewInput(e, Graph, dur, ref timeStart, ref zoom, ref panDrag);
        }

        //──────────── 铺满视图与旧映射一致 ────────────

        [Test]
        public void FitSpan_IsTheLegacyMapping(){
            float dur = 0.42f;
            TimelineGraphGUI.Span span = TimelineGraphGUI.MakeSpan(Graph, dur);
            float width = WidthOf(Graph);

            Assert.AreEqual(0f, span.TimeStart, 1e-6f, "铺满视图左端就是 0");
            //用乘法形式钉"Pps=宽/时长"：除法式在 delta 1e-4 下会被 float/double 求值域差翻转（实测差 1.1e-4）
            Assert.AreEqual(width, span.Pps * dur, 0.01f, "铺满视图：像素/秒 × 时长 = 图区宽");
            Assert.AreEqual(span.Origin, span.TimeToX(0f), 0.001f, "t=0 落在图区左边界");
            Assert.AreEqual(span.Origin + width, span.TimeToX(dur), 0.001f, "t=duration 落在右边界");
            Assert.AreEqual(width, span.SecondsToWidth(dur), 0.001f);

            //旧口径的往返：XToTime(TimeToX(t)) == t
            float mid = span.TimeToX(0.17f);
            Assert.AreEqual(0.17f, span.XToTime(mid), 0.001f);
        }

        //──────────── 缩放视图的映射 ────────────

        [Test]
        public void ZoomedSpan_MapsTimeByPpsFromViewStart(){
            float dur = 0.42f;
            float width = WidthOf(Graph);
            float fitPps = width / dur;
            TimelineGraphGUI.Span span = TimelineGraphGUI.MakeSpan(Graph, dur, 0.1f, fitPps * 2f);

            Assert.AreEqual(span.Origin, span.TimeToX(span.TimeStart), 0.001f, "视图起点画在图区左边界");
            Assert.AreEqual(0.1f, span.XToTime(span.Origin), 0.001f);
            float t = 0.25f;
            Assert.AreEqual(t, span.XToTime(span.TimeToX(t)), 0.001f, "视区内往返闭合");
            //delta 放到 0.01：几千像素/秒的量级上 float ulp 就有 ~5e-4，内联算术经 Mono 求值还会带 double 域噪声
            Assert.AreEqual(fitPps * 2f, span.Pps, 0.01f);
            Assert.AreEqual(span.Pps, span.SecondsToWidth(1f), 0.001f, "1 秒的像素=Pps");
        }

        [Test]
        public void ZoomedSpan_NeverShowsOutsideTheDuration(){
            //视图起点被夹：起点+视图时长不许越过 duration（视图比时长宽时只许 0）
            float dur = 0.42f;
            float width = WidthOf(Graph);
            float fitPps = width / dur;
            float pps = fitPps * 4f;   //视图只装得下 dur/4
            TimelineGraphGUI.Span span = TimelineGraphGUI.MakeSpan(Graph, dur, 0.4f, pps);
            Assert.LessOrEqual(span.TimeStart + span.ViewDuration, dur + 1e-4f, "视图右端不越过时长");
            Assert.GreaterOrEqual(span.TimeStart, 0f);

            TimelineGraphGUI.Span wide = TimelineGraphGUI.MakeSpan(Graph, dur, 0.3f, fitPps);
            Assert.AreEqual(0f, wide.TimeStart, 1e-6f, "铺满倍率下没有平移余量");
        }

        [Test]
        public void ZoomedSpan_BelowFit_PinsStartAtZero_AndLetsTheRightSideRunEmpty(){
            //缩到铺满以下（1/ZoomOutMax）：视图装不下整段时长是本意——起点钉在 0、动作贴左侧，
            //右侧留空，时长右边界线照画。此时"视图右端不越时长"的不变式不再成立，也不该成立
            float dur = 0.42f;
            float width = WidthOf(Graph);
            float fitPps = width / dur;
            TimelineGraphGUI.Span span = TimelineGraphGUI.MakeSpan(Graph, dur, 0.4f, fitPps / TimelineGraphGUI.ZoomOutMax);
            Assert.AreEqual(0f, span.TimeStart, 1e-6f, "铺满以下没有平移余量，起点钉 0");
            Assert.AreEqual(fitPps / TimelineGraphGUI.ZoomOutMax, span.Pps, 0.01f, "pps 停在下限");
            Assert.Greater(span.TimeStart + span.ViewDuration, dur, "视图比时长宽=右侧留空");
            Assert.AreEqual(span.Origin, span.TimeToX(0f), 0.001f, "动作开头仍画在图区左边界");
        }

        //──────────── 缩放夹取与锚点 ────────────

        [Test]
        public void ClampPps_FloorIsFitOverZoomOutMax_AndRespectsTheCap(){
            float dur = 0.42f;
            float width = WidthOf(Graph);
            float fit = width / dur;
            float floor = fit / TimelineGraphGUI.ZoomOutMax;
            Assert.AreEqual(floor, TimelineGraphGUI.ClampPps(fit * 0.1f, width, dur), 1e-4f, "最小缩到铺满的 1/ZoomOutMax");
            Assert.AreEqual(floor, TimelineGraphGUI.ClampPps(floor * 0.1f, width, dur), 1e-4f, "触底不击穿");
            Assert.AreEqual(fit, TimelineGraphGUI.ClampPps(fit, width, dur), 1e-4f);
            Assert.AreEqual(TimelineGraphGUI.MaxPps, TimelineGraphGUI.ClampPps(TimelineGraphGUI.MaxPps * 10f, width, dur), 1e-4f);

            //极短时长铺满就可能超过 MaxPps：上限抬到铺满，但下限照样是铺满的 1/ZoomOutMax
            float tinyFit = width / TimelineGraphGUI.MinDuration;
            Assert.AreEqual(tinyFit * 0.5f, TimelineGraphGUI.ClampPps(tinyFit * 0.5f, width, TimelineGraphGUI.MinDuration), 1e-4f);
            Assert.AreEqual(tinyFit / TimelineGraphGUI.ZoomOutMax, TimelineGraphGUI.ClampPps(tinyFit * 0.01f, width, TimelineGraphGUI.MinDuration), 1e-4f);
        }

        [Test]
        public void ZoomedTimeStart_KeepsTheTimeUnderTheAnchorAtTheSamePixel(){
            float dur = 0.42f;
            float width = WidthOf(Graph);
            float fit = width / dur;
            float origin = Graph.x + TimelineGraphGUI.LeftPad;
            float anchorX = origin + width * 0.3f;
            float anchorTime = 0.3f * dur;

            float newPps = fit * 2f;
            float newStart = TimelineGraphGUI.ZoomedTimeStart(0f, fit, newPps, anchorX, origin);
            TimelineGraphGUI.Span span = TimelineGraphGUI.MakeSpan(Graph, dur, newStart, newPps);
            Assert.AreEqual(anchorTime, span.XToTime(anchorX), 0.001f, "缩放前后锚点下的时刻必须落在同一像素");
        }

        //──────────── 视口输入（合成事件） ────────────

        [Test]
        public void HandleViewInput_MiddleButtonPressDragRelease_PansTheView(){
            float timeStart = 0f, zoom = 2f;
            bool panDrag = false;

            Assert.IsTrue(PumpView(new Event{
                type = EventType.MouseDown, button = 2, mousePosition = new Vector2(450f, 200f)
            }, 0.42f, ref timeStart, ref zoom, ref panDrag), "中键按下被视口泵消费（条逻辑不得再看见它）");
            Assert.IsTrue(panDrag, "中键按下进入平移态");

            //delta.x=-20：timeStart -= delta/pps ⇒ 向右平移 20px 的量
            Assert.IsTrue(PumpView(new Event{
                type = EventType.MouseDrag, button = 2, delta = new Vector2(-20f, 0f)
            }, 0.42f, ref timeStart, ref zoom, ref panDrag));
            float pps = WidthOf(Graph) / 0.42f * 2f;
            Assert.AreEqual(20f / pps, timeStart, 1e-6f, "拖动量换算成秒进 timeStart");
            Assert.IsTrue(panDrag, "拖动中平移态保持");

            Assert.IsTrue(PumpView(new Event{ type = EventType.MouseUp, button = 2 }, 0.42f, ref timeStart, ref zoom, ref panDrag));
            Assert.IsFalse(panDrag, "抬起退出平移态");
        }

        [Test]
        public void HandleViewInput_WheelZoomsBothWays_AndBottomsOutAtFitOverZoomOutMax(){
            float timeStart = 0f, zoom = 1f;
            bool panDrag = false;

            Assert.IsTrue(PumpView(new Event{
                type = EventType.ScrollWheel, mousePosition = new Vector2(450f, 200f), delta = new Vector2(0f, -3f)
            }, 0.42f, ref timeStart, ref zoom, ref panDrag), "图区内滚轮被消费");
            Assert.Greater(zoom, 1f, "滚轮上=放大（相对铺满的倍率变大）");

            //往下滚 40 档：1.15⁴⁰ 远超 4 倍，触底后必须稳稳停在 1/ZoomOutMax，不许击穿也不许回弹
            for (int i = 0; i < 40; i++){
                PumpView(new Event{
                    type = EventType.ScrollWheel, mousePosition = new Vector2(450f, 200f), delta = new Vector2(0f, 3f)
                }, 0.42f, ref timeStart, ref zoom, ref panDrag);
            }
            Assert.AreEqual(1f / TimelineGraphGUI.ZoomOutMax, zoom, 1e-4f, "缩小下限=铺满的 1/ZoomOutMax");
            Assert.AreEqual(0f, timeStart, 1e-6f, "铺满以下起点钉 0");
        }

        [Test]
        public void HandleViewInput_WheelOutsideGraph_AndShiftWheelPan(){
            float timeStart = 0f, zoom = 2f;   //先放大：铺满倍率下没有平移余量，timeStart 推不动
            bool panDrag = false;

            Assert.IsFalse(PumpView(new Event{
                type = EventType.ScrollWheel, mousePosition = new Vector2(450f, 3800f), delta = new Vector2(0f, 3f)
            }, 0.42f, ref timeStart, ref zoom, ref panDrag), "图区外的滚轮不归视口泵管（留给外层 ScrollView）");
            Assert.AreEqual(2f, zoom, 1e-6f, "图区外的滚轮不归视口泵管，缩放不动");

            //Shift+滚轮=平移意图：不缩放，按像素换算挪 timeStart
            Assert.IsTrue(PumpView(new Event{
                type = EventType.ScrollWheel, mousePosition = new Vector2(450f, 200f),
                delta = new Vector2(0f, 3f), shift = true
            }, 0.42f, ref timeStart, ref zoom, ref panDrag));
            Assert.AreEqual(2f, zoom, 1e-6f, "平移意图不改缩放");
            Assert.Greater(timeStart, 0f, "Shift+滚轮把视图往右推");
        }

        //──────────── 走带 ────────────

        [Test]
        public void AdvancePlayhead_WrapsAtTheEnd_AndFreezesWhenStopped(){
            Assert.AreEqual(0.05f, TimelineGraphGUI.AdvancePlayhead(0.03f, true, 0.02f, 0.42f), 1e-6f);
            Assert.AreEqual(0f, TimelineGraphGUI.AdvancePlayhead(0.41f, true, 0.02f, 0.42f), 1e-6f, "越过时长循环回 0");
            Assert.AreEqual(0.41f, TimelineGraphGUI.AdvancePlayhead(0.41f, false, 0.02f, 0.42f), 1e-6f, "暂停时不动");
        }

        [Test]
        public void StepPlayhead_SnapsFirst_ThenSteps_AndClamps(){
            float dur = 0.42f;
            //输入避开"正好落在两格中间"的半值：0.05/0.02=2.5 那种点会被 float 噪声与取偶舍入翻转方向
            Assert.AreEqual(0.12f, TimelineGraphGUI.StepPlayhead(0.137f, 0.02f, -1, dur), 1e-4f, "上一档先吸到帧格再走");
            Assert.AreEqual(0.16f, TimelineGraphGUI.StepPlayhead(0.137f, 0.02f, 1, dur), 1e-4f);
            Assert.AreEqual(0f, TimelineGraphGUI.StepPlayhead(0.005f, 0.02f, -1, dur), 1e-4f, "头前不许走出界");
            Assert.AreEqual(dur, TimelineGraphGUI.StepPlayhead(0.415f, 0.02f, 1, dur), 1e-4f, "尾后不许走出界");
        }

        //──────────── Caster 预览的盒采样 ────────────

        private static ActionBoxTrack Track(string tag, float openMin, float openMax, params ActionBoxKey[] keys){
            return new ActionBoxTrack{
                tag = tag, openMin = openMin, openMax = openMax,
                keys = keys
            };
        }

        private static ActionBoxKey Key(float t, float cz){
            return new ActionBoxKey{
                t = t,
                center = new Vector3(0f, 0f, cz),
                axisX = Vector3.right, axisY = Vector3.up, axisZ = Vector3.forward,
                half = new Vector3(0.3f, 0.3f, 0.3f)
            };
        }

        [Test]
        public void SamplePreviewBoxes_OnlyOpenWindowsComeBack_WithTheirLane(){
            ActionModel model = new ActionModel{
                attackBoxes = new[]{ Track("R_Fist", 0.06f, 0.20f, Key(0.06f, 0.4f), Key(0.20f, 0.8f)) },
                hitBoxes = new[]{ Track("Body", 0f, 0.42f, Key(0f, 0f)) }
            };

            //窗口未开：攻击盒不出现，受击盒在
            List<ActionEditorWindow.BoxPreview> atStart = ActionEditorWindow.SamplePreviewBoxes(model, 0.02f);
            Assert.AreEqual(1, atStart.Count);
            Assert.IsFalse(atStart[0].attack);
            Assert.AreEqual("Body", atStart[0].pose.tag);

            //窗口正开：攻击盒出现且标攻击
            List<ActionEditorWindow.BoxPreview> mid = ActionEditorWindow.SamplePreviewBoxes(model, 0.12f);
            Assert.AreEqual(2, mid.Count);
            Assert.IsTrue(mid.Exists(p => p.attack && p.pose.tag == "R_Fist"));
            Assert.IsTrue(mid.Exists(p => p.attack == false && p.pose.tag == "Body"));

            //窗尾之后（半开 [min,max)）：攻击盒退场
            List<ActionEditorWindow.BoxPreview> after = ActionEditorWindow.SamplePreviewBoxes(model, 0.24f);
            Assert.AreEqual(1, after.Count);
            Assert.IsFalse(after[0].attack);
        }

        [Test]
        public void SamplePreviewBoxes_InterpolatesBetweenKeys(){
            ActionModel model = new ActionModel{
                attackBoxes = new[]{ Track("R_Fist", 0f, 0.42f, Key(0f, 0.4f), Key(0.4f, 0.8f)) },
                hitBoxes = null
            };
            List<ActionEditorWindow.BoxPreview> open = ActionEditorWindow.SamplePreviewBoxes(model, 0.2f);
            Assert.AreEqual(1, open.Count);
            Assert.AreEqual(0.6f, open[0].pose.center.z, 1e-4f, "两键正中间=线性插值中点");
        }

        [Test]
        public void SamplePreviewBoxes_ToleratesEmptyModel(){
            Assert.AreEqual(0, ActionEditorWindow.SamplePreviewBoxes(new ActionModel(), 0.1f).Count);
        }

        ///<summary>
        ///盒的朝向换算必须和 Unity 自己的 `Quaternion.Euler` 指同一个地方：线框画的是它、身体摆的也是它。
        ///绕 Y 的手性是 x' = x·c + z·s（正角把 +Z 转向 +X）；写成减号时 90°/270° 两档正好对调，
        ///症状是"线框贴在身后"，窗口里没人会当 bug 报，所以这条只能由测试钉住。
        ///</summary>
        [Test]
        public void ToWorldObb_MatchesQuaternionEuler(){
            ActionBoxTrack track = Track("R_Fist", 0f, 1f, Key(0f, 0.4f));
            ActionBoxPose pose = ActionBoxSampler.At(track, 0f);
            Vector3 unitPos = new Vector3(1.5f, 0.25f, -0.5f);
            float[] degrees = { 0f, 90f, 137f, 270f };
            for (int i = 0; i < degrees.Length; i++){
                Obb obb = pose.ToWorldObb(unitPos, degrees[i]);
                Quaternion q = Quaternion.Euler(0f, degrees[i], 0f);
                string at = "朝向 " + degrees[i] + "：";
                Same(q * pose.center + unitPos, obb.center, at + "盒心");
                Same(q * pose.axisX, obb.axisX, at + "axisX");
                Same(q * pose.axisY, obb.axisY, at + "axisY");
                Same(q * pose.axisZ, obb.axisZ, at + "axisZ");
            }
        }

        ///<summary>逐分量比（浮点三角函数的零头在 1e-6 量级，给 1e-4 容差；Vector3.Equals 是精确比较，不能用它）</summary>
        private static void Same(Vector3 expect, Vector3 actual, string what){
            const float eps = 1e-4f;
            Assert.AreEqual(expect.x, actual.x, eps, what + " x");
            Assert.AreEqual(expect.y, actual.y, eps, what + " y");
            Assert.AreEqual(expect.z, actual.z, eps, what + " z");
        }

        //──────────── 条的抓取判据（窄条主体优先） ────────────

        [Test]
        public void PickDragPart_WideBar_EdgesGrabEdges_MiddleDragsWhole(){
            const float grab = 10f;
            //宽条 [100, 300]：两头 ±grab 抓边，中间拖整条
            Assert.AreEqual(1, ActionEditorWindow.PickDragPart(100f, 300f, true, true, 105f, grab), "条头热区");
            Assert.AreEqual(2, ActionEditorWindow.PickDragPart(100f, 300f, true, true, 295f, grab), "条尾热区");
            Assert.AreEqual(0, ActionEditorWindow.PickDragPart(100f, 300f, true, true, 200f, grab), "条身=整条平移");
        }

        [Test]
        public void PickDragPart_NarrowBar_BodyWinsTheMiddle(){
            const float grab = 10f;
            //窄条 [100, 115]（宽 15 < grab×3）：边缘热区已互相盖住条身——中段 50% 必须还给"整条平移"，
            //否则铺满视图下的短窗点哪都是"拖边"、整条永远挪不动
            Assert.AreEqual(1, ActionEditorWindow.PickDragPart(100f, 115f, true, true, 102f, grab), "左 1/4 归条头");
            Assert.AreEqual(0, ActionEditorWindow.PickDragPart(100f, 115f, true, true, 107.5f, grab), "中段=整条平移");
            Assert.AreEqual(2, ActionEditorWindow.PickDragPart(100f, 115f, true, true, 113.5f, grab), "右 1/4 归条尾");
        }

        [Test]
        public void PickDragPart_SingleSideBars_AlwaysDragTheirOnlyEnd(){
            const float grab = 10f;
            //取消出（只有 startFrom）：条身平移无意义，点哪都拖条头
            Assert.AreEqual(1, ActionEditorWindow.PickDragPart(100f, 300f, true, false, 105f, grab));
            Assert.AreEqual(1, ActionEditorWindow.PickDragPart(100f, 300f, true, false, 290f, grab));
            //临时入（只有 duration）：条身 pinned 在 0，点哪都拖条尾（=长度）
            Assert.AreEqual(2, ActionEditorWindow.PickDragPart(100f, 300f, false, true, 105f, grab));
            Assert.AreEqual(2, ActionEditorWindow.PickDragPart(100f, 300f, false, true, 290f, grab));
            //两头都无（防御分支，当前六条 lane 至少给一头）：0 = 拖拽分支空操作
            Assert.AreEqual(0, ActionEditorWindow.PickDragPart(100f, 300f, false, false, 200f, grab));
        }

        //──────────── 拖拽落点（跟手偏移） ────────────

        [Test]
        public void ApplyBarDrag_WholeBarTranslates_KeepingWidthAndGrabOffset(){
            //双端条 [0.1, 0.2] 抓中段（抓在 0.15，偏移 0.05），光标到 0.30 ⇒ 锚点 0.25：整条平移到 [0.25, 0.35]
            float h = 0.1f, t = 0.2f;
            ActionEditorWindow.ApplyBarDrag(0, true, true, 0.25f, ref h, ref t, 0.42f);
            Assert.AreEqual(0.25f, h, 1e-4f);
            Assert.AreEqual(0.35f, t, 1e-4f, "宽度保持不变");
        }

        [Test]
        public void ApplyBarDrag_WholeBarClampsAtBothEnds(){
            float h = 0.1f, t = 0.2f;
            ActionEditorWindow.ApplyBarDrag(0, true, true, 0.9f, ref h, ref t, 0.42f);
            Assert.AreEqual(0.32f, h, 1e-4f, "左端夹在 dur−width，右端不出时长");
            Assert.AreEqual(0.42f, t, 1e-4f);

            float h2 = 0.1f, t2 = 0.2f;
            ActionEditorWindow.ApplyBarDrag(0, true, true, -0.5f, ref h2, ref t2, 0.42f);
            Assert.AreEqual(0f, h2, 1e-4f, "左端不许出 0");
            Assert.AreEqual(0.1f, t2, 1e-4f);
        }

        [Test]
        public void ApplyBarDrag_EdgeFollowsTheGrabAnchor_NotCrossingItsNeighbour(){
            //抓条头（偏移已在锚点里）：头跟到锚点，对边不动；越过对边时被拦住
            float h = 0.1f, t = 0.2f;
            ActionEditorWindow.ApplyBarDrag(1, true, true, 0.16f, ref h, ref t, 0.42f);
            Assert.AreEqual(0.16f, h, 1e-4f, "头跟到锚点（抓取偏移在 MouseDown 记账）");
            Assert.AreEqual(0.2f, t, 1e-4f, "对边不动");
            ActionEditorWindow.ApplyBarDrag(1, true, true, 0.9f, ref h, ref t, 0.42f);
            Assert.AreEqual(0.2f, h, 1e-4f, "头不许越过尾");

            float h2 = 0.1f, t2 = 0.2f;
            ActionEditorWindow.ApplyBarDrag(2, true, true, -0.1f, ref h2, ref t2, 0.42f);
            Assert.AreEqual(0.1f, t2, 1e-4f, "尾不许越过头，也不许出 0");
        }

        [Test]
        public void ApplyBarDrag_SingleSidedBars_FollowWithTheirOnlyEdge(){
            //取消出：只有 startFrom 自由（尾=动作末尾，数据语义钉死）
            float h = 0.1f, t = 9.9f;
            ActionEditorWindow.ApplyBarDrag(1, true, false, 0.3f, ref h, ref t, 0.42f);
            Assert.AreEqual(0.3f, h, 1e-4f, "取消出的头跟到锚点");
            Assert.AreEqual(9.9f, t, 1e-4f, "不可动的一头原样返回");

            //临时入：只有 duration 自由（头=动作起点，钉在 0）
            float h2 = 9.9f, t2 = 0.2f;
            ActionEditorWindow.ApplyBarDrag(2, false, true, 0.35f, ref h2, ref t2, 0.42f);
            Assert.AreEqual(0.35f, t2, 1e-4f, "临时入的尾跟到锚点");
            Assert.AreEqual(9.9f, h2, 1e-4f);

            //出界夹取：单边条的自由边也夹在 [0, dur]
            float h3 = 0.1f, t3 = 9.9f;
            ActionEditorWindow.ApplyBarDrag(1, true, false, 0.9f, ref h3, ref t3, 0.42f);
            Assert.AreEqual(0.42f, h3, 1e-4f, "头夹在时长处");
        }
    }
}
