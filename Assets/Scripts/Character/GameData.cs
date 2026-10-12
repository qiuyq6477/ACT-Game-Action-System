using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 游戏的静态数据，这里有一些是凑效果的
/// </summary>
public static class GameData
{
    private static bool _loaded = false; 
    public static Dictionary<string, ActionInfo> Actions = new Dictionary<string, ActionInfo>();
    private static List<ActionInfo> _cachedAllActions = new List<ActionInfo>();
    
    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        Actions.Clear();
        _cachedAllActions.Clear();

        TextAsset ta = Resources.Load<TextAsset>("GameData/Action");
        if (ta)
        {
            ActionInfoContainer aic = JsonUtility.FromJson<ActionInfoContainer>(ta.text);
            foreach (ActionInfo info in aic.data)
            {
                if (!string.IsNullOrEmpty(info.id))
                {
                    Actions[info.id] = info;
                    _cachedAllActions.Add(info);
                }
            }
        }
    }

    public static List<ActionInfo> AllActions()
    {
        return _cachedAllActions;
    }
}