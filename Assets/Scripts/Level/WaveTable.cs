using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 波次表：一关有哪几波、每波都有谁、站在哪。
///
/// 解决的问题：敌兵原来是**摆在场景里**的（`GameMain.enemy` 那一排 Inspector 引用），
/// 于是"加一关""改一波"要动 `.unity`，而 .unity 是最容易改坏、又最难 diff 的那种文件。
/// 这张表把它变成数据：加一关 = 加一段 JSON。
///
/// 数值抄源工程：`03_Level1` 是三波、2/3/3 只，出生横向 9.65 / 18.96 / 30.6。
/// 这里改成"每只各给一个 x 与 z"，因为本工程多了纵深车道（z），
/// 而源工程那一关所有人 y 都是 0——同一排挤在一起在本工程会被同轨推挤推开，不如一开始就分车道。
/// </summary>
[Serializable]
public class SpawnRow
{
    /// <summary>Resources 底下的预制体名（与 cue 的 param 同一个取法）</summary>
    public string prefab;

    public float x;
    public float z;
}

[Serializable]
public class WaveRow
{
    public SpawnRow[] enemies;
}

[Serializable]
public class LevelRow
{
    public string id;

    /// <summary>一波全灭之后隔几拍开下一波。源工程是"下一波立刻 SetActive"，这里留一点喘气的拍子</summary>
    public int framesBetweenWavesFrames = 45;

    public WaveRow[] waves;
}

[Serializable]
internal class LevelContainer
{
    public LevelRow[] data;
}

/// <summary>
/// 波次表的读表入口。与 `GameData` 同一条规矩：**只在开机读一次**，
/// 运行期（尤其是逻辑帧里）一次 IO 都不许做——生成一波人用的那个 prefab 也得是提前载好的
/// </summary>
public static class WaveTable
{
    private static readonly Dictionary<string, LevelRow> _levels = new Dictionary<string, LevelRow>();
    private static readonly List<string> _order = new List<string>();
    private static bool _loaded;

    public static bool Loaded => _loaded;

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        _levels.Clear();
        _order.Clear();

        TextAsset ta = Resources.Load<TextAsset>("GameData/Wave");
        if (!ta)
        {
            Debug.Log("[波次表] 没有 Resources/GameData/Wave.json，波次这一套不参与（老场景照原样跑）");
            return;
        }

        LevelContainer container = JsonUtility.FromJson<LevelContainer>(ta.text);
        if (container == null || container.data == null) return;
        foreach (LevelRow row in container.data)
        {
            if (row == null || string.IsNullOrEmpty(row.id) || row.waves == null) continue;
            if (_levels.ContainsKey(row.id))
            {
                Debug.LogError("[波次表] 关卡 id 重复：「" + row.id + "」，后一条被忽略");
                continue;
            }
            _levels[row.id] = row;
            _order.Add(row.id);
            Validate(row);
        }
    }

    /// <summary>
    /// 开机就把表里的每一个引用查一遍：prefab 名字取不到东西、波是空的，
    /// 都在这里喊而不是等战斗中间"这一波没出人"（与 `ForceMoveMethod.Validate` 同一个立场）
    /// </summary>
    private static void Validate(LevelRow row)
    {
        for (int w = 0; w < row.waves.Length; w++)
        {
            WaveRow wave = row.waves[w];
            if (wave == null || wave.enemies == null || wave.enemies.Length == 0)
            {
                Debug.LogError("[波次表] 「" + row.id + "」第 " + (w + 1) + " 波是空的：这一波开不出人，波次也推不动");
                continue;
            }
            for (int i = 0; i < wave.enemies.Length; i++)
            {
                SpawnRow spawn = wave.enemies[i];
                if (spawn == null || string.IsNullOrEmpty(spawn.prefab))
                {
                    Debug.LogError("[波次表] 「" + row.id + "」第 " + (w + 1) + " 波第 " + (i + 1) + " 行没写 prefab");
                    continue;
                }
                GameObject prefab = Resources.Load<GameObject>(spawn.prefab);
                if (prefab == null)
                    Debug.LogError("[波次表] 「" + row.id + "」要生成的预制体 「" + spawn.prefab + "」在 Resources 里找不到");
                else if (prefab.GetComponent<CombatStats>() == null)
                    Debug.LogError("[波次表] 「" + spawn.prefab + "」没挂 CombatStats：他不会死，"
                                   + "于是这一波永远清不完、关卡推不下去");
            }
        }
    }

    public static LevelRow Get(string id)
    {
        LevelRow row;
        return _levels.TryGetValue(id, out row) ? row : null;
    }

    /// <summary>表里第一关。没有表就返回 null，调用方退回"场景里摆好的那几只"</summary>
    public static LevelRow First()
    {
        return _order.Count == 0 ? null : _levels[_order[0]];
    }

    public static int Count => _order.Count;
}
