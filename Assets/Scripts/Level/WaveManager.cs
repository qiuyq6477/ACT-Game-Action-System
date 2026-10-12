using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 波次推进：按 `WaveTable` 一批一批地生成敌兵，一波全灭之后隔几拍开下一波。
///
/// 解决的问题：源工程的 `WaveManager` 把每一波的敌兵**预摆在场景里**、
/// 靠 `wave.SetActive(true)` 当"生成"，靠订阅 `HealthSystem.onUnitDeath` 数人数。
/// 本工程不改场景（加一关只动一份 JSON），也没有那个静态事件总线，
/// 所以这里自己持有"这一波有谁"，每逻辑帧看一次他们死光了没有。
///
/// 【为什么"生成"是一个委托而不是自己 Instantiate】：把一个角色真正放上场要做事不止一处——
/// 认判定盒、给他自己那一份动作子集、挂决策器、进 `GameMain` 那张全体列表。
/// 这些全是 `GameMain` 的知识，波次管理器不该知道；它只知道"给我一个名字和一个坐标，场上就多一个人"。
/// 用例因此可以塞一个假的进来，把"第几波什么时候开"这件事单独钉死，而不必先搭一个真场景。
/// </summary>
public class WaveManager
{
    /// <summary>由 `GameMain` 注入：按名字与坐标放一个人上场，返回他。没注入就整套管都不管</summary>
    public Func<string, Vector3, CharacterObj> spawn;

    /// <summary>推哪一关。null 就是不推（没有波次表的老场景）</summary>
    public LevelRow level;

    /// <summary>已经开到第几波（1 起）。0 = 一场还没开</summary>
    public int WaveIndex { get; private set; }

    /// <summary>最后一波也清完了。给以后的 HUD 与关卡结算看</summary>
    public bool LevelCleared { get; private set; }

    /// <summary>场上还有几个这一波的人没死。给 HUD、也给"配额/难度"那类以后的判断用</summary>
    public int AliveInWave
    {
        get
        {
            int n = 0;
            for (int i = 0; i < _current.Count; i++)
                if (_current[i] != null && !_current[i].IsDead) n++;
            return n;
        }
    }

    /// <summary>这一波的人。清了之后会被清空，所以"上一波的人数"随时可查</summary>
    private readonly List<CharacterObj> _current = new List<CharacterObj>(8);

    private bool _scheduled;
    private long _readyFrame;

    /// <summary>
    /// 开场第一波。由 `GameMain` 在 Awake 里叫一次——
    /// 于是"什么时候有第一个人上场"是一件明说出来的事，而不是某个 `Start()` 的时机
    /// </summary>
    public void StartFirstWave()
    {
        if (spawn == null || level == null || level.waves == null) return;
        OpenWave();
    }

    /// <summary>每逻辑帧一拍，由 `GameMain.TickAll` 在输入拍之前调用（新生成的人这一拍就能走完剩下的链）</summary>
    public void Tick(long frame)
    {
        if (LevelCleared || spawn == null || level == null || level.waves == null) return;

        if (AliveInWave > 0) return;              //这一波还有人站着，什么都不做
        if (WaveIndex >= level.waves.Length)      //最后一波也清了
        {
            LevelCleared = true;
            return;
        }

        //刚清完的那一拍先排期，别在同一拍就开下一波：源工程是"立刻 SetActive 下一波"，
        //于是上一波最后一个人才倒下、下一波第一人已经贴脸，这里给它一个喘气的间隔
        if (!_scheduled)
        {
            _scheduled = true;
            _readyFrame = frame + Mathf.Max(0, level.framesBetweenWavesFrames);
            return;
        }
        if (frame < _readyFrame) return;

        _scheduled = false;
        OpenWave();
    }

    private void OpenWave()
    {
        WaveRow wave = level.waves[WaveIndex];
        _current.Clear();
        for (int i = 0; i < wave.enemies.Length; i++)
        {
            SpawnRow row = wave.enemies[i];
            CharacterObj unit = spawn(row.prefab, new Vector3(row.x, 0f, row.z));
            if (unit != null) _current.Add(unit);
        }
        WaveIndex++;
    }
}
