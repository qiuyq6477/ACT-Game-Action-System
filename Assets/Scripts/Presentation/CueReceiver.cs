using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 帧事件的接收者：把表里的 cues 变成声音、特效和镜头震动。
///
/// 【解决的问题】：切片 1.5 把帧事件搬进表之后，派发口 `CueDispatch.Handler` 一直空着——
/// 表里 23 条 cue 按拍求值了却没人听。这一片就是那个"听的人"。
///
/// 【为什么全是池，一个 Instantiate 都不留】：源工程每响一次就 `new GameObject + AudioSource`、
/// 每个特效 `Instantiate + Destroy(go, 3f)`，照搬进来直接撞本工程的 0 分配红线
/// （热路径每逻辑帧一格不许漏，见 `Assets/Scripts/AGENTS.md`）。所以：
/// 音效只有一台常驻 `AudioSource`，叠着放靠 `PlayOneShot`，它不需要"空闲的槽"；
/// 特效按名字建池，池里的实例开机就摆好，放一次只是 SetActive + Play，收回只是 SetActive(false)。
///
/// 【为什么预热扫表而不是用到才载】：用到才载会把一次磁盘 IO 塞进打击那一拍，
/// 手感上就是"第三拳卡了一下"。表是全的（`GameData.AllActions()`），
/// 于是"哪些名字会被响到"是一个能在开机时读完的集合，读它一次比在战斗里补十次要便宜。
/// 表里写了个素材库里没有的名字，就在预热那一条日志里报，运行期安静跳过——
/// 这与 `ForceMoveMethod.Validate` 同一个立场：错在数据上要在开机说清楚，不许每帧刷。
///
/// 【生命周期按逻辑帧而不是秒】：特效收回的时机是 `LogicClock.Frame + lifeFrames`，
/// 于是渲染帧率换了（`targetFrameRate` 30/120 那两档验收）它照样在同样的拍数之后消失
/// </summary>
public class CueReceiver : MonoBehaviour
{
    /// <summary>每种特效池里备几个：同屏人数上限 × 每人同时最多一个烟，6 够打，超了就从同一个槽复用</summary>
    public const int EffectCapacity = 6;

    /// <summary>镜头震动的长度与幅度，抄源工程 CameraShake 的 duration=.3f / intensity=.15f，换算成 18 拍</summary>
    public const int ShakeFrames = 18;
    public const float ShakeIntensity = 0.15f;

    private const float DefaultEffectLifeFrames = 45f;

    /// <summary>素材放在 Resources 底下的这两格里，名字就是表里的 param（源工程也是这么按名字取的）</summary>
    public const string AudioFolder = "BeatEmUp/Audio/";
    public const string EffectFolder = "BeatEmUp/Effects/";

    private AudioSource _audio;

    /// <summary>音效名 → 载好的 clip。预热时填满，运行期只查不载</summary>
    private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();

    private readonly Dictionary<string, EffectPool> _poolIndex = new Dictionary<string, EffectPool>();

    /// <summary>Tick 要按顺序过一遍所有的池，字典的 Values 迭代器不保证顺序也不保证不分配，所以另存一份列表</summary>
    private readonly List<EffectPool> _pools = new List<EffectPool>();

    /// <summary>脚步音的几种变体，随机取一个——同一种脚步连踩三下听起来像一台坏掉的缝纫机</summary>
    private AudioClip[] _footsteps;

    private int _shakeFramesLeft;
    private int _shakeFramesTotal;

    /// <summary>相机这一拍该抬多少。只动纵向，横向永远是跟随玩家的那一个值（源工程也只在 y 上抖）</summary>
    public float ShakeOffsetY { get; private set; }

    /// <summary>
    /// 初始化。由挂它的人显式叫一次（`GameMain.Awake` 里 AddComponent 之后紧接着调）。
    /// 【为什么不是 Awake】：EditMode 用例里 AddComponent 不会触发 Awake——本工程的角色也一样，
    /// 它们的盒子绑定都是显式 `BindBoxes()`。写成 Awake 的话这一族用例根本没法测它
    /// </summary>
    public void Init()
    {
        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        //2D：清版动作的"远近"由纵深车道与相机透视负责，声音不再衰减一遍才不会被听得忽大忽小
        _audio.spatialBlend = 0f;
        Warm();
    }

    /// <summary>
    /// 扫一遍全表，把会被响到的名字都载好、把特效的池都建出来
    /// </summary>
    private void Warm()
    {
        foreach (ActionInfo info in GameData.AllActions())
        {
            ActionCue[] cues = info.cues;
            if (cues == null) continue;
            for (int i = 0; i < cues.Length; i++)
            {
                if (cues[i].kind == CueKind.Sfx) LoadClip(cues[i].param);
                else if (cues[i].kind == CueKind.Effect) WarmPool(cues[i].param);
            }
        }

        //脚步 cue 在表里不带名字（源工程的 Footstep 也没参数），变体就固定从水泥地那一套里抽
        _footsteps = new[]
        {
            LoadClip("FootstepConcrete1"),
            LoadClip("FootstepConcrete2"),
            LoadClip("FootstepConcrete3"),
        };
    }

    /// <summary>
    /// 派发口。签名里的 owner 是"这一拍响的这条 cue 属于哪个角色"，特效要摆在他脚边，没有它就不知道该摆在哪
    /// </summary>
    public void OnCue(CharacterObj owner, ActionInfo info, ActionCue cue)
    {
        switch (cue.kind)
        {
            case CueKind.Sfx:
                PlayClip(cue.param);
                break;
            case CueKind.Footstep:
                PlayFootstep();
                break;
            case CueKind.Effect:
                SpawnEffect(owner, cue.param);
                break;
            case CueKind.CameraShake:
                _shakeFramesLeft = ShakeFrames;
                _shakeFramesTotal = ShakeFrames;
                break;
            //CueKind.Force 与 CueKind.Projectile 不在这一片：前者要先把"表里带一个 MoveInfo"这件事定了，
            //后者跟着弹射物那一片一起做。这里什么都不做，也不在运行期刷日志——
            //策划在表里多填一条没人听的 cue，不该变成每帧一条 Error
        }
    }

    /// <summary>每逻辑帧一次：到期收回的特效 + 镜头震动的衰减。由 GameMain 的最后一拍调用</summary>
    public void Tick()
    {
        for (int p = 0; p < _pools.Count; p++)
        {
            EffectPool pool = _pools[p];
            for (int i = 0; i < pool.returnFrame.Length; i++)
            {
                //0 是"这个槽空着"。LogicClock.Frame 从 0 开始，所以第一拍里空槽与刚放的槽都可能是 0，
                //多判一次 IsActive 就稳了：空槽本来就关着，再关一次不花什么
                if (pool.returnFrame[i] != 0 && LogicClock.Frame >= pool.returnFrame[i])
                {
                    pool.items[i].SetActive(false);
                    pool.returnFrame[i] = 0;
                }
            }
        }

        if (_shakeFramesLeft > 0)
        {
            _shakeFramesLeft--;
            float k = (float)_shakeFramesLeft / _shakeFramesTotal;
            //左右交替而不是正弦：一帧一偏移，抖出来的就是那种"顿一下"的硬拍子
            ShakeOffsetY = k * ShakeIntensity * ((_shakeFramesLeft & 1) == 0 ? 1f : -1f);
        }
        else ShakeOffsetY = 0f;
    }

    private void PlayClip(string name)
    {
        AudioClip clip;
        if (name == null || !_clips.TryGetValue(name, out clip) || clip == null) return;
        _audio.PlayOneShot(clip);
    }

    private void PlayFootstep()
    {
        if (_footsteps == null || _footsteps.Length == 0) return;
        //抽号走 Dice 而不是 UnityEngine.Random：逻辑层的随机只准从可复现的那一个源来（切片 1.6）
        AudioClip clip = _footsteps[Dice.Next(_footsteps.Length)];
        if (clip != null) _audio.PlayOneShot(clip);
    }

    private void SpawnEffect(CharacterObj owner, string name)
    {
        EffectPool pool;
        if (name == null || !_poolIndex.TryGetValue(name, out pool)) return;
        Vector3 at = owner ? owner.transform.position : Vector3.zero;

        int idx = pool.cursor;
        pool.cursor = idx + 1 < pool.items.Length ? idx + 1 : 0;
        GameObject go = pool.items[idx];
        go.transform.position = at;
        go.SetActive(true);
        ParticleSystem[] systems = pool.systems[idx];
        for (int i = 0; i < systems.Length; i++)
        {
            //先 Stop+Clear 再 Play：池里的上一个烟还有半截没放完的话，不冲掉就会看到两团叠在一起
            systems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            systems[i].Play(true);
        }
        pool.returnFrame[idx] = LogicClock.Frame + pool.lifeFrames;
    }

    private AudioClip LoadClip(string name)
    {
        AudioClip clip;
        if (_clips.TryGetValue(name, out clip)) return clip;
        clip = Resources.Load<AudioClip>(AudioFolder + name);
        if (clip == null) Debug.LogWarning("[帧事件] 表里要响的音效 「" + name + "」在 " + AudioFolder + " 里找不到");
        _clips[name] = clip;
        return clip;
    }

    private void WarmPool(string name)
    {
        EffectPool exists;
        if (_poolIndex.TryGetValue(name, out exists)) return;

        GameObject prefab = Resources.Load<GameObject>(EffectFolder + name);
        if (prefab == null)
        {
            Debug.LogWarning("[帧事件] 表里要放的特效 「" + name + "」在 " + EffectFolder + " 里找不到");
            return;
        }

        EffectPool pool = new EffectPool();
        pool.items = new GameObject[EffectCapacity];
        pool.systems = new ParticleSystem[EffectCapacity][];
        pool.returnFrame = new long[EffectCapacity];
        pool.lifeFrames = Mathf.Max(1, Mathf.CeilToInt(LifeSeconds(prefab) * 60f));
        for (int i = 0; i < EffectCapacity; i++)
        {
            //挂在接收者下面而不是留着漂浮：收工的时候场景里不会散落一堆没爹的烟
            GameObject go = Instantiate(prefab, transform);
            go.name = name + i;
            go.SetActive(false);
            pool.items[i] = go;
            pool.systems[i] = go.GetComponentsInChildren<ParticleSystem>(true);
        }
        _poolIndex[name] = pool;
        _pools.Add(pool);
    }

    /// <summary>
    /// 一条特效活多久：拿它自己那个粒子系统的主时长（含延迟）算，读不到就退回默认值。
    /// 不问美术要一个"多少拍"的字段，是因为这个数本来就写在片子里，抄一遍就会有两个真源
    /// </summary>
    private static float LifeSeconds(GameObject prefab)
    {
        ParticleSystem ps = prefab.GetComponentInChildren<ParticleSystem>();
        if (ps == null) return DefaultEffectLifeFrames / 60f;
        ParticleSystem.MainModule main = ps.main;
        return main.duration + main.startDelayMultiplier;
    }

    //================================ 给用例看的三个洞 ====================================
    //只问"池里现在什么样"，不把 EffectPool 这个类型漏出去：用例改的是行为，不是字段布局

    /// <summary>这个名的池里有几个正亮着。名字没有池（素材缺失）就是 0</summary>
    public int ActiveEffectCount(string name)
    {
        EffectPool pool;
        if (!_poolIndex.TryGetValue(name, out pool)) return 0;
        int n = 0;
        for (int i = 0; i < pool.returnFrame.Length; i++) if (pool.returnFrame[i] != 0) n++;
        return n;
    }

    /// <summary>这个名的池里第 slot 个实例（预热时就摆好，一直在那儿，只是开关不同）</summary>
    public GameObject PooledInstance(string name, int slot)
    {
        EffectPool pool;
        if (!_poolIndex.TryGetValue(name, out pool)) return null;
        return pool.items[slot];
    }

    /// <summary>池里备好的实例总数，也就是 `EffectCapacity`；没有这个池返回 0</summary>
    public int PoolCapacity(string name)
    {
        EffectPool pool;
        return _poolIndex.TryGetValue(name, out pool) ? pool.items.Length : 0;
    }

    /// <summary>这个池里的实例亮多少拍之后收回去（从片子自己的粒子时长换算来）</summary>
    public int EffectLifeFrames(string name)
    {
        EffectPool pool;
        return _poolIndex.TryGetValue(name, out pool) ? pool.lifeFrames : -1;
    }

    /// <summary>预热有没有把这个名字的音效载到。表里写了个库里没有的名字，靠这条钉住</summary>
    public bool HasClip(string name)
    {
        AudioClip clip;
        return _clips.TryGetValue(name, out clip) && clip != null;
    }

    /// <summary>一个名字的特效池：备好的实例、每个实例还剩几拍该收回、以及它们身上的粒子系统</summary>
    private class EffectPool
    {
        public GameObject[] items;
        public ParticleSystem[][] systems;
        public long[] returnFrame;
        public int lifeFrames;
        public int cursor;
    }
}
