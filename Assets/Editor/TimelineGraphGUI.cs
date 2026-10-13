using UnityEditor;
using UnityEngine;

namespace CombatFrameworkEditor
{
    ///<summary>
    ///时间轴的公共绘制件：秒↔像素的换算（含缩放/平移视口）、自适应刻度、吸附、按 id 着色、条状命中、
    ///走带与播放头。这里只管"怎么摆怎么点、怎么缩放怎么走带"——动作窗口画的是区间条（窗与段），
    ///至于画什么、点了改哪个字段，都由窗口自己决定。
    ///
    ///视口口径：zoom 是**相对铺满**的倍率（1=整段时长铺满图区宽，可缩到 1/ZoomOutMax 倍铺满或放大到
    ///MaxPps），timeStart 是视图左端的时刻；这两个都是编辑器状态（不落盘），换算与夹取全部
    ///收口在这里。铺满视图（MakeSpan 两参重载）的映射由 `ActionEditorLayoutTests` 钉着，不许漂。
    ///</summary>
    public static class TimelineGraphGUI
    {
        public const float RowH = 22f;           ///<summary>一行（一个节点 / 一条 lane）的像素高</summary>
        public const float LeftPad = 8f;         ///<summary>图区左边距</summary>
        public const float RightPad = 20f;       ///<summary>图区右边距（给行末的文字标签留位）</summary>
        public const float RulerBand = 30f;      ///<summary>顶部标尺带高度，第一行从它下面开始</summary>
        public const float Snap = 0.05f;         ///<summary>Timeline 拖拽吸附粒度（秒）；Action 用一逻辑帧，不在这</summary>
        public const float MinDuration = 0.05f;  ///<summary>可绘制的最小时长（除零保护，也是拖拽下限）</summary>
        public const float TickTargetPx = 80f;   ///<summary>标尺刻度的目标间距（像素）</summary>
        public const float MaxPps = 5000f;       ///<summary>缩放上限（像素/秒）：一逻辑帧 100px，够逐帧对齐</summary>
        public const float ZoomStep = 1.15f;     ///<summary>滚轮一档的缩放倍率</summary>
        public const float ZoomOutMax = 4f;      ///<summary>缩放下限（相对铺满的倍数）：最小可缩到铺满的 1/4，动作钉在视图左侧</summary>
        private const float PanPx = 40f;         ///<summary>滚轮平移一档对应的像素</summary>

        public static readonly Color PlayheadColor = new Color(1f, 0.25f, 0.25f, 0.9f);

        ///<summary>
        ///横轴映射：TimeStart 起的视图 ↔ 图区像素。TimeToX 仍把 t 夹进 [0, Duration]——
        ///配错的窗（越出动长）画不出右边界外；越出**视图**的部分靠 ClipTo 裁。
        ///</summary>
        public struct Span
        {
            public float Origin;    ///<summary>TimeStart 时刻处的像素 x</summary>
            public float Width;     ///<summary>视图像素宽（不含左右留白）</summary>
            public float Duration;  ///<summary>有效时长（已夹 MinDuration）</summary>
            public float Pps;       ///<summary>缩放：像素/秒（铺满时 = Width/Duration）</summary>
            public float TimeStart; ///<summary>视图左端的时刻（铺满时 = 0）</summary>

            ///<summary>视图能装下的秒数</summary>
            public float ViewDuration { get { return Width / Pps; } }

            ///<summary>秒 → 像素（越界先夹：窗外的值不会画到时长边界外）</summary>
            public float TimeToX(float t){
                return Origin + (Mathf.Clamp(t, 0f, Duration) - TimeStart) * Pps;
            }

            ///<summary>像素 → 秒（不夹：拖出边界时由调用方决定怎么收）</summary>
            public float XToTime(float x){
                return TimeStart + (x - Origin) / Pps;
            }

            ///<summary>时长 → 像素宽度</summary>
            public float SecondsToWidth(float seconds){
                return seconds * Pps;
            }
        }

        ///<summary>铺满视图：整段时长映射到图区宽（缩放=1、起点=0；与旧版逐位一致）</summary>
        public static Span MakeSpan(Rect rect, float rawDuration){
            float duration = Mathf.Max(MinDuration, rawDuration);
            float width = Mathf.Max(1f, rect.width - LeftPad - RightPad);
            return MakeSpan(rect, rawDuration, 0f, width / duration);
        }

        ///<summary>缩放视图：调用方给的 timeStart/pps 会在里面夹取（下限=铺满的 1/ZoomOutMax，上限=MaxPps）</summary>
        public static Span MakeSpan(Rect rect, float rawDuration, float timeStart, float pps){
            float duration = Mathf.Max(MinDuration, rawDuration);
            float width = Mathf.Max(1f, rect.width - LeftPad - RightPad);
            pps = ClampPps(pps, width, duration);
            timeStart = ClampTimeStart(timeStart, width, pps, duration);
            return new Span{
                Origin = rect.x + LeftPad, Width = width, Duration = duration,
                Pps = pps, TimeStart = timeStart
            };
        }

        ///<summary>像素/秒的可行域：下限=铺满的 1/ZoomOutMax（再小就缩不下去，动作钉在视图左侧），上限=max(MaxPps, 铺满)（极短时长铺满就可能超上限，不回压）</summary>
        public static float ClampPps(float pps, float viewWidth, float duration){
            float dur = Mathf.Max(MinDuration, duration);
            float fit = viewWidth / dur;
            return Mathf.Clamp(pps, fit / ZoomOutMax, Mathf.Max(MaxPps, fit));
        }

        ///<summary>视图起点的可行域：[0, Duration−视图时长]（视图比时长还宽时只许 0）</summary>
        public static float ClampTimeStart(float timeStart, float viewWidth, float pps, float duration){
            float dur = Mathf.Max(MinDuration, duration);
            float maxStart = Mathf.Max(0f, dur - viewWidth / pps);
            return Mathf.Clamp(timeStart, 0f, maxStart);
        }

        ///<summary>锚点缩放：锚像素处的时间在缩放前后必须落在同一像素上，反解出新的视图起点</summary>
        public static float ZoomedTimeStart(float timeStart, float pps, float newPps, float anchorX, float originX){
            float anchorTime = timeStart + (anchorX - originX) / pps;
            return anchorTime - (anchorX - originX) / newPps;
        }

        ///<summary>
        ///视口输入（两窗口共用）：滚轮=以鼠标为锚缩放（可缩到铺满的 1/ZoomOutMax），Shift+滚轮 /
        ///触摸板横扫 / 中键拖拽=平移。zoom 存"相对铺满"的倍率，窗口宽度变化时视图跟着铺满比例走。
        ///返回 true=事件已消费，调用方不再处理。
        ///</summary>
        public static bool HandleViewInput(Rect graphRect, float duration, ref float timeStart, ref float zoom, ref bool panDrag){
            Event e = Event.current;
            if (e == null) return false;   //GUI 外（测试/eval 直调）Event.current 拿不到，别炸
            return HandleViewInput(e, graphRect, duration, ref timeStart, ref zoom, ref panDrag);
        }

        ///<summary>事件参数重载：测试用合成 Event 直灌（GUI 外 Event.current 赋值不生效，见包内视口输入测试）</summary>
        public static bool HandleViewInput(Event e, Rect graphRect, float duration, ref float timeStart, ref float zoom, ref bool panDrag){
            float origin = graphRect.x + LeftPad;
            float width = Mathf.Max(1f, graphRect.width - LeftPad - RightPad);
            float dur = Mathf.Max(MinDuration, duration);
            float pps = width / dur * zoom;

            if (e.type == EventType.MouseDrag && panDrag){
                timeStart = ClampTimeStart(timeStart - e.delta.x / pps, width, pps, dur);
                e.Use();
                return true;
            }
            if (e.type == EventType.MouseUp && panDrag){
                panDrag = false;
                e.Use();
                return true;
            }
            if (e.type == EventType.ScrollWheel && graphRect.Contains(e.mousePosition)){
                bool panIntent = e.shift || Mathf.Abs(e.delta.x) > Mathf.Abs(e.delta.y);
                if (panIntent){
                    timeStart = ClampTimeStart(timeStart + (e.delta.x + e.delta.y) * PanPx / pps, width, pps, dur);
                }else{
                    float next = ClampPps(pps * (e.delta.y < 0f ? ZoomStep : 1f / ZoomStep), width, dur);
                    timeStart = ClampTimeStart(ZoomedTimeStart(timeStart, pps, next, e.mousePosition.x, origin), width, next, dur);
                    zoom = next / (width / dur);
                }
                e.Use();
                return true;
            }
            if (e.type == EventType.MouseDown && e.button == 2 && graphRect.Contains(e.mousePosition)){
                panDrag = true;
                e.Use();
                return true;
            }
            return false;
        }

        ///<summary>标尺：约 80px 一格，步长取"整齐"的 1/2/5×10ⁿ；缩放后只画视区内的刻度</summary>
        public static void DrawRuler(Rect rect, Span span){
            float rawStep = TickTargetPx / span.Pps;
            float step = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(rawStep)));
            if (rawStep / step >= 5f) step *= 5f;
            else if (rawStep / step >= 2f) step *= 2f;
            var labelStyle = new GUIStyle(EditorStyles.miniLabel){ alignment = TextAnchor.MiddleCenter };
            float viewEnd = Mathf.Min(span.Duration, span.TimeStart + span.ViewDuration);
            for (float t = Mathf.Max(0f, Mathf.Ceil(span.TimeStart / step) * step); t <= viewEnd + 0.0001f; t += step){
                float x = span.TimeToX(t);
                if (x < rect.x - 1f || x > rect.xMax + 1f) continue;
                EditorGUI.DrawRect(new Rect(x, rect.y, 1, rect.height), new Color(0, 0, 0, 0.15f));
                GUI.Label(new Rect(x - 20, rect.y + 2, 40, 14), t.ToString("0.##"), labelStyle);
            }
        }

        ///<summary>时长右边界（"越过这条线就是配错了"的那根线；平移出视图就不画）</summary>
        public static void DrawBoundary(Rect rect, Span span){
            float x = span.TimeToX(span.Duration);
            if (x < rect.x || x > rect.xMax) return;
            EditorGUI.DrawRect(new Rect(x, rect.y + 14, 1, rect.height - 20), new Color(0, 0, 0, 0.4f));
        }

        ///<summary>与可视区求交：缩放/平移后块可能画出图区（左侧压到名字栏、右侧出窗），画之前裁掉出界部分</summary>
        public static Rect ClipTo(Rect r, Rect clip){
            float xMin = Mathf.Max(r.xMin, clip.xMin);
            float xMax = Mathf.Min(r.xMax, clip.xMax);
            if (xMax <= xMin) return new Rect(0f, 0f, 0f, 0f);
            return new Rect(xMin, r.y, xMax - xMin, r.height);
        }

        ///<summary>第 index 行（一个节点 / 一条 lane）的整行矩形</summary>
        public static Rect Row(Rect rect, int index){
            return new Rect(rect.x, rect.y + RulerBand + index * RowH, rect.width, RowH);
        }

        ///<summary>
        ///[start, end]（秒）在这一行里画出的条。`pad` 向左右各扩若干像素——
        ///点状块（start==end）就是靠它撑出看得见、点得中的宽度。
        ///</summary>
        public static Rect BarRect(Span span, Rect row, float start, float end, float pad, float height){
            float x0 = span.TimeToX(start), x1 = span.TimeToX(end);
            return new Rect(Mathf.Min(x0, x1) - pad, row.y, Mathf.Abs(x1 - x0) + pad * 2f, height);
        }

        ///<summary>条状命中：比画出来的块宽一圈、高一截，免得要点得像素级精确</summary>
        public static bool HitBar(Vector2 mouse, Span span, Rect row, float start, float end){
            return BarRect(span, row, start, end, 10f, RowH - 2f).Contains(mouse);
        }

        ///<summary>吸附到 step 的整数倍</summary>
        public static float SnapTo(float t, float step){
            return Mathf.Round(t / step) * step;
        }

        ///<summary>
        ///播放头逐帧推进（走带的"播放"一档用）：到尾循环回 0。dt 由调用方显式给
        ///（EditorApplication.timeSinceStartup 的差），这里不做任何时钟直读。
        ///</summary>
        public static float AdvancePlayhead(float time, bool playing, float dt, float duration){
            if (playing == false) return time;
            float next = time + dt;
            return next > duration ? 0f : next;
        }

        ///<summary>播放头上/下一档：先吸到步长格上再走 N 步，夹进 [0, duration]</summary>
        public static float StepPlayhead(float time, float step, int frames, float duration){
            return Mathf.Clamp(SnapTo(time, step) + frames * step, 0f, duration);
        }

        ///<summary>
        ///走带控件：跳到头 / 上一档 / 播放·暂停 / 下一档 / 跳到尾，右侧跟调用方格式化的读数。
        ///返回 true=播放头或播放态被这排按钮改过（调用方据此采样预览、重置推进时钟）。
        ///</summary>
        public static bool DrawTransport(ref float playhead, ref bool playing, float step, float duration, string readout){
            bool changed = false;
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("|<", GUILayout.Width(30))){ playhead = 0f; playing = false; changed = true; }
            if (GUILayout.Button("<", GUILayout.Width(24))){ playhead = StepPlayhead(playhead, step, -1, duration); changed = true; }
            if (GUILayout.Button(playing ? "Pause" : "Play", GUILayout.Width(50))){ playing = !playing; changed = true; }
            if (GUILayout.Button(">", GUILayout.Width(24))){ playhead = StepPlayhead(playhead, step, 1, duration); changed = true; }
            if (GUILayout.Button(">|", GUILayout.Width(30))){ playhead = duration; playing = false; changed = true; }
            GUILayout.Space(10);
            EditorGUILayout.LabelField(readout, EditorStyles.miniLabel, GUILayout.MinWidth(70));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            return changed;
        }

        ///<summary>标尺带按下/拖动=刷播放头。返回 true 时 time 带出该处时刻（已夹 [0, Duration]）</summary>
        public static bool RulerScrub(Rect rect, Span span, out float time){
            Event e = Event.current;
            time = 0f;
            if (e.type != EventType.MouseDown && e.type != EventType.MouseDrag) return false;
            if (e.mousePosition.y < rect.y || e.mousePosition.y > rect.y + RulerBand) return false;
            if (e.mousePosition.x < rect.x || e.mousePosition.x > rect.xMax) return false;
            time = Mathf.Clamp(span.XToTime(e.mousePosition.x), 0f, span.Duration);
            return true;
        }

        ///<summary>播放头：贯穿整个图区的竖线</summary>
        public static void DrawPlayhead(Rect rect, Span span, float time){
            float x = span.TimeToX(time);
            if (x < rect.x || x > rect.xMax) return;
            EditorGUI.DrawRect(new Rect(x - 1f, rect.y, 2f, rect.height), PlayheadColor);
        }

        ///<summary>Play模式禁改的提示条（返回 true=正在 Play）</summary>
        public static bool DrawPlayModeWarning(){
            if (Application.isPlaying == false) return false;
            EditorGUILayout.HelpBox("Editing disabled in Play mode (runtime works on a registration snapshot; edit after exiting Play)", MessageType.Warning);
            return true;
        }

        ///<summary>按字符串哈希出一个稳定的颜色（同一个 id 在两个窗口里同色）</summary>
        public static Color ColorFromId(string id){
            if (string.IsNullOrEmpty(id)) return new Color(0.5f, 0.5f, 0.5f, 0.6f);
            int h = 17;
            foreach (char c in id) h = h * 31 + c;
            float r = ((h >> 16) & 0xFF) / 255f;
            float g = ((h >> 8) & 0xFF) / 255f;
            float b = (h & 0xFF) / 255f;
            return new Color(r * 0.5f + 0.4f, g * 0.5f + 0.4f, b * 0.5f + 0.4f, 0.9f);
        }
    }
}
