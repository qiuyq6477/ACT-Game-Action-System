using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 游戏的静态数据，从 JSON 载入原始数据 ActionData
/// </summary>
public static class GameData
{
    private static bool _loaded = false; 
    public static Dictionary<string, ActionData> Actions = new Dictionary<string, ActionData>();

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        Actions.Clear();

        TextAsset ta = Resources.Load<TextAsset>("GameData/Action");
        if (ta)
        {
            ActionDataContainer adc = JsonUtility.FromJson<ActionDataContainer>(ta.text);
            foreach (ActionData data in adc.data)
            {
                if (data.id != "") Actions.Add(data.id, data);
            }
        }
    }

    public static List<ActionData> AllActionDatas()
    {
        List<ActionData> res = new List<ActionData>();
        foreach (KeyValuePair<string, ActionData> pair in Actions)
        {
            res.Add(pair.Value);
        }

        return res;
    }

    public static ActionData GetActionData(string id)
    {
        if (Actions.TryGetValue(id, out ActionData data))
        {
            return data;
        }
        return default;
    }
}