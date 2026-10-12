using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 屏幕上的一条状态条（玩家血量），完全由代码搭出来。
///
/// 解决的问题：清版动作总得让人看见"我还剩多少血"。而本工程改不了也不手写 `.unity`
/// ——手写 Canvas 的 RectTransform 那一串数字是最容易写错又最难发现的东西（编辑期看不到），
/// 所以 UI 也走代码：开机搭一次，运行期只改一个数。
///
/// 【为什么是 `Image.Type.Filled` 而不是缩放一条白边】：源工程那条血量子物体 `localScale.x = hp/max`，
/// 会把图片本身拉扁。Filled 只裁不拉伸，而且 `fillAmount` 是一个 float，比改 scale 便宜。
///
/// 【为什么只在数值变了才写】：`Image.fillAmount` 会弄脏 canvas（触发一次 rebuild）。
/// 每渲染帧无脑写一次就等于每秒几十次白白重建。
/// </summary>
public class HudBar : MonoBehaviour
{
    private const float MarginX = 24f;
    private const float MarginY = -24f;

    private Image _fill;
    private float _last = -1f;

    /// <summary>条子此刻多长（0~1）。调试与用例读这个，别去猜"填充是哪个子物体"的结构</summary>
    public float Value => _fill != null ? _fill.fillAmount : 0f;

    /// <summary>
    /// 在屏幕某个角搭一条。`corner` 用 UI 的锚点写法：(0,1) = 左上、(1,1) = 右上。
    /// 只在开机调用，所以这里出现的一切分配都不在热路径上
    /// </summary>
    public static HudBar Build(string name, Vector2 corner, Vector2 size, Color fillColor)
    {
        Canvas canvas = EnsureCanvas();
        if (canvas == null)
        {
            Debug.LogWarning("[HUD] 场景里找不到 Canvas，也建不出来一个——血量条不显示");
            return null;
        }

        GameObject root = new GameObject(name, typeof(RectTransform));
        root.transform.SetParent(canvas.transform, false);
        RectTransform rt = (RectTransform)root.transform;
        rt.anchorMin = corner;
        rt.anchorMax = corner;
        rt.pivot = corner;
        rt.sizeDelta = size;
        //锚点在角上，所以往内的偏移就是那个角的正负号乘出去
        rt.anchoredPosition = new Vector2(corner.x < 0.5f ? MarginX : -MarginX, corner.y < 0.5f ? MarginY : -MarginY);

        var background = root.AddComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.55f);
        background.raycastTarget = false;      //血条不吃点击，别挡住以后的菜单

        GameObject fillGo = new GameObject("Fill", typeof(RectTransform));
        fillGo.transform.SetParent(root.transform, false);
        RectTransform frt = (RectTransform)fillGo.transform;
        frt.anchorMin = Vector2.zero;
        frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(2f, 2f);   //让底色露出两像素当边框
        frt.offsetMax = new Vector2(-2f, -2f);

        var fill = fillGo.AddComponent<Image>();
        fill.color = fillColor;
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = 0;   //0 = 从左往右（这个枚举值是 int，Unity 的 Filled 就吃一个序号）
        fill.fillAmount = 1f;
        fill.raycastTarget = false;

        HudBar bar = root.AddComponent<HudBar>();
        bar._fill = fill;
        return bar;
    }

    /// <summary>场景里已经有 Canvas（demo 带一个调试用的）就用它，没有就搭一个最朴素的屏幕空间画布</summary>
    private static Canvas EnsureCanvas()
    {
        Canvas existing = Object.FindFirstObjectByType<Canvas>();
        if (existing != null) return existing;

        GameObject go = new GameObject("HudCanvas");
        Canvas canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        go.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    /// <summary>0 = 空条、1 = 满条。数值没变就一个字都不写</summary>
    public void SetValue(float k)
    {
        k = Mathf.Clamp01(k);
        if (Mathf.Abs(k - _last) < 0.001f) return;
        _last = k;
        _fill.fillAmount = k;
    }

    /// <summary>
    /// 这个人该显示多满。【没挂 CombatStats 就是满格】：与 `CombatPipeline` 同一个立场——
    /// 不装数值组件就等于不参与数值，条子不该因此变成空的或者消失
    /// </summary>
    public static float FillFor(CombatStats stats)
    {
        if (stats == null || !stats.HasHp) return 1f;
        return Mathf.Clamp01(stats.Hp / stats.maxHp);
    }
}
