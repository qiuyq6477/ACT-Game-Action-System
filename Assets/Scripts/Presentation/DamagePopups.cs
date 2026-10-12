using UnityEngine;

/// <summary>
/// 伤害飘字：打出一记就在人头上顶一个数字，飘一会儿自己收回。
///
/// 解决的问题：1.9 做完数值结算之后，玩家其实看不见自己造成了多少伤害——
/// 血量条只说"他还剩多少"，不说"这一下狠不狠"。源工程根本没有这套（连 `damage popup` 都没有），
/// 它是商用清版动作的标配，而这一条在 `Doc/plan/beatemup-port.md` 里从 1.9 顺延到现在。
///
/// 【为什么是 `TextMesh` 而不是 uGUI 的 Text】：uGUI 每改一次文字就要重建一次 canvas，
/// 而命中那一拍已经在顿挫、在换动作、在摆盒子；世界空间的 `TextMesh` 就是一个普通网格，
/// 与角色走同一个排序，还不用往场景里塞第二个 Canvas。
///
/// 【为什么数字是预烤的一张表】：`number.ToString()` 每调用一次分配一个字符串，
/// 而"每记命中分配一次"正是 `Doc/issue/hit-event-heap-alloc.md` 刚关掉的那类问题。
/// 伤害是有限范围的东西，0..99 加上一个「99+」一共 101 条字符串，开机烤一次就够。
///
/// 【池为什么按容量循环而不是无限长】：与特效池同一条理由——超出容量就回头复用最早那一格，
/// 于是同屏最多 12 个数字，永远不会有"群战时飘字把对象池撑爆"这件事。
/// </summary>
public class DamagePopups : MonoBehaviour
{
    /// <summary>同屏最多几个数字</summary>
    public const int Capacity = 12;

    /// <summary>一个数字活多少拍（0.7 秒）。够看清，又不会挡着下一波</summary>
    public const int LifeFrames = 42;

    /// <summary>预烤到几。超过就归到「99+」</summary>
    public const int MaxNumber = 99;

    /// <summary>飘字从头顶多高的地方开始，以及一共往上飘多少米</summary>
    public const float StartHeight = 1.25f;
    public const float RiseHeight = 0.7f;

    private static DamagePopups _instance;
    private static string[] _numberText;

    private TextMesh[] _texts;
    private long[] _returnFrame;
    private Vector3[] _origin;
    private int _cursor;

    /// <summary>字体有没有拿到。拿不到就整套飘字不参与（而不是每记命中报一次错）</summary>
    private bool _usable;

    public static void Bind()
    {
        _numberText = BuildNumberTable();
    }

    /// <summary>由 `GameMain` 在开机时挂好；用例也可以直接挂一个再调 `Show`</summary>
    public void Init()
    {
        _instance = this;
        if (_numberText == null) Bind();

        _texts = new TextMesh[Capacity];
        _returnFrame = new long[Capacity];
        _origin = new Vector3[Capacity];

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
        {
            Debug.LogError("[飘字] 拿不到内建字体，伤害飘字不启用");
            return;
        }
        for (int i = 0; i < Capacity; i++)
        {
            //挂在接收者下面而不是留着漂浮：收工的时候场景里不会散落一堆没爹的文字
            GameObject go = new GameObject("DamagePopup" + i);
            go.transform.SetParent(transform, false);
            var mr = go.AddComponent<MeshRenderer>();
            TextMesh tm = go.AddComponent<TextMesh>();
            tm.font = font;
            tm.fontSize = 48;
            tm.characterSize = 0.045f;         //世界空间里的字：这个大小在 4 米外的相机前大约是屏幕十几个像素高
            tm.anchor = TextAnchor.UpperCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(1f, 0.92f, 0.62f);
            //字体自带的材质是"跟随光照"的那一类，光不够的时候字会发暗；
            //这里不去换材质——一换就把内建字体材质实例化了一份，而这条路径每记命中都在跑
            go.SetActive(false);
            _texts[i] = tm;
        }
        _usable = true;
    }

    /// <summary>
    /// 在 `at` 头顶冒一个数字。没挂过组件（比如没有 GameMain 的用例场景）就安静跳过
    /// </summary>
    public static void Show(Vector3 at, float damage)
    {
        //0 伤害的一记不冒字：格挡走的正是这条，而一个空串也要占一个池位、也要亮一拍
        if (damage <= 0f) return;
        if (_instance == null || !_instance._usable) return;
        _instance.Spawn(at, damage);
    }

    private void Spawn(Vector3 at, float damage)
    {
        int idx = _cursor;
        _cursor = idx + 1 < Capacity ? idx + 1 : 0;

        TextMesh tm = _texts[idx];
        tm.text = _numberText[NumberIndex(damage)];
        //横向抖一点点，连击时同一列数字不会把自己叠成一团糊
        float jitter = (Dice.Next(100) - 50) * 0.006f;
        _origin[idx] = new Vector3(at.x + jitter, at.y + StartHeight, at.z);
        tm.transform.position = _origin[idx];
        tm.gameObject.SetActive(true);
        _returnFrame[idx] = LogicClock.Frame + LifeFrames;
    }

    /// <summary>每逻辑帧一拍，由 `GameMain` 的表现回收拍调用：往上飘 + 到点收回</summary>
    public void Tick()
    {
        if (!_usable) return;
        for (int i = 0; i < Capacity; i++)
        {
            long due = _returnFrame[i];
            if (due == 0) continue;
            if (LogicClock.Frame >= due)
            {
                _texts[i].gameObject.SetActive(false);
                _returnFrame[i] = 0;
                continue;
            }
            float k = 1f - (float)(due - LogicClock.Frame) / LifeFrames;
            _texts[i].transform.position = _origin[i] + new Vector3(0f, RiseHeight * k, 0f);
        }
    }

    /// <summary>这个数字进哪一格文本。0 与负数（不该发生）都归到最外面那一格</summary>
    public static int NumberIndex(float damage)
    {
        if (damage <= 0f) return 0;
        int d = (int)(damage + 0.5f);
        return d > MaxNumber ? MaxNumber + 1 : d;
    }

    /// <summary>
    /// 烤出 0..99 与一条「99+」。运行期只是拿下标取现成的字符串，一次都不分配。
    /// 下标 0 同时也是"这一记没掉血"（格挡、霸体硬抗）那一格，所以留空串而不是「0」——
    /// 挡住的时候不该冒一个 0 出来，那是在告诉玩家"你的防御白做了"
    /// </summary>
    private static string[] BuildNumberTable()
    {
        string[] table = new string[MaxNumber + 2];
        table[0] = string.Empty;
        for (int i = 1; i <= MaxNumber; i++) table[i] = i.ToString();
        table[MaxNumber + 1] = "99+";
        return table;
    }

    //================================ 给用例看的两个洞 ====================================

    /// <summary>此刻亮着的数字有几个</summary>
    public int ActiveCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < Capacity; i++) if (_returnFrame[i] != 0) n++;
            return n;
        }
    }

    /// <summary>第 slot 格现在显示什么（没启用时返回 null）</summary>
    public string TextAt(int slot)
    {
        return _usable ? _texts[slot].text : null;
    }
}
