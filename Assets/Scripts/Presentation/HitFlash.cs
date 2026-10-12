using UnityEngine;

/// <summary>
/// 受击白闪：打中那一拍把这个人身上所有渲染器提亮，几拍之后退回原样。
///
/// 解决的问题：ACT 的"打到了"有三条通道同时说话——顿一下（硬直）、摆出受击姿势、以及**亮一下**。
/// 前两条在切片 1.9 之前就有，第三条一直缺，于是打中人只"顿"，不"炸"。
/// 源工程用的是换材质（`sr.material = Resources.Load("HitFlashMat")` 挂一会儿再换回去），
/// 那在 Unity 里是**每次都在实例化一份材质**，落在命中那一拍就是分配；
/// 这里改用 `MaterialPropertyBlock`：不新建材质、不弄脏共享材质球，也不需要美术再给一个白闪 shader。
///
/// 【时长为什么是拍而不是秒】：本工程的时长一律是整型逻辑帧。6 拍 = 0.1 秒，
/// 正是源工程 `hitFlashDuration` 在 `Enemy.prefab` 里填的那个数。
/// </summary>
public class HitFlash : MonoBehaviour
{
    /// <summary>一次闪多少拍。源工程敌兵 0.1 秒 = 6 拍</summary>
    public const int DefaultFrames = 6;

    private Renderer[] _renderers;
    private MaterialPropertyBlock _block;
    private int _left;
    private int _total;

    /// <summary>
    /// 认一遍身上的渲染器。灰盒是 MeshRenderer、精灵是 SpriteRenderer，两者都是 Renderer，
    /// 而 `_Color` 这两个用的 shader 都认识——所以一条路同时管住两种角色
    /// </summary>
    public void Init()
    {
        if (_renderers != null) return;
        _renderers = GetComponentsInChildren<Renderer>(true);
        _block = new MaterialPropertyBlock();
    }

    /// <summary>开始闪。已经在闪的时候重复打只把剩余拍数重置满，不叠加（跟硬直同一条规矩）</summary>
    public void Begin(int frames)
    {
        if (_renderers == null) Init();
        if (_renderers.Length == 0) return;
        _left = frames;
        _total = frames;
        Light();
    }

    /// <summary>每逻辑帧一拍，由 `GameMain` 的表现回收拍调用</summary>
    public void Tick()
    {
        if (_left <= 0) return;
        _left--;
        if (_left <= 0) { Clear(); return; }
        Light();
    }

    /// <summary>
    /// 把当前该亮的颜色写进每一只渲染器。闪一整段最多 6 拍，所以"每拍都写"比"只在第一次写"
    /// 更费一点点，但换来的是不需要记"上次写了什么"——而渲染器原本的颜色我们根本拿不到
    /// （`Renderer.material.color` 会实例化材质，正是这一片要避免的那次分配）
    /// </summary>
    private void Light()
    {
        float k = _total > 0 ? (float)_left / _total : 0f;
        //乘色：1 = 原样，越大越往白里冲。精灵用的 Sprites/Default 的 _Color 是乘法，
        //所以这里是"大于 1 的白"而不是"混一点白"，混白在深色贴图上根本看不出来
        Color c = new Color(1f + 1.6f * k, 1f + 1.1f * k, 1f + 0.6f * k, 1f);
        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer r = _renderers[i];
            if (r == null) continue;
            r.GetPropertyBlock(_block);
            _block.SetColor("_Color", c);
            r.SetPropertyBlock(_block);
        }
    }

    private void Clear()
    {
        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer r = _renderers[i];
            if (r == null) continue;
            r.SetPropertyBlock(null);       //整块清掉，比写回一个"原来的颜色"可靠：原来是什么色我们并不知道
        }
    }

    /// <summary>此刻还剩几拍在闪（用例与调试看这个）</summary>
    public int FramesLeft => _left;
}
