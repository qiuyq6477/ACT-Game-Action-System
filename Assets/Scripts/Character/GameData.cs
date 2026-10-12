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
    private static Dictionary<string, ActionInfo[]> _catalogIndex = new Dictionary<string, ActionInfo[]>();
    private static readonly ActionInfo[] NoActions = new ActionInfo[0];
    
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
            foreach (ActionInfo source in aic.data)
            {
                //结构体的foreach变量不允许直接改成员（CS1654），拷一份出来改，改完的这份才进表
                ActionInfo info = source;
                if (!string.IsNullOrEmpty(info.id))
                {
                    //时长是整条帧号时钟的标尺，填漏了会一路除出无穷大，先在这里挡住并喊一声
                    if (info.durationFrames <= 0)
                    {
                        Debug.LogError("动作 " + info.id + " 的 durationFrames 没填（或填了0）：" +
                                       "跑菜单 Tools/ACT/打印动作时长 拿正确帧数填回 Action.json");
                        info.durationFrames = 1;
                    }
                    //把只有字符串的表数据烘焙成运行期直接可用的数值，这里分配一次，之后每帧都不再碰字符串
                    info.rootMotionParam = RootMotionMethod.Bake(info.rootMotionTween.method, info.rootMotionTween.param, info.durationFrames);
                    //盒子的tag烘焙成位掩码；阶段数组本身是引用类型，按下标改元素就改到了共享的那份表
                    if (info.attackPhase != null)
                        for (int i = 0; i < info.attackPhase.Length; i++)
                        {
                            info.attackPhase[i].tagMask = BoxTagRegistry.MaskOf(info.attackPhase[i].tag);
                            //轨迹同样在这里摊平成按帧号直取的数组：热路径只剩一次数组下标
                            if (AttackBoxKey.Bake(info.attackPhase[i].track, info.durationFrames,
                                    out Vector3[] pose, out Vector3[] size, info.id))
                            {
                                info.attackPhase[i].bakedPosition = pose;
                                info.attackPhase[i].bakedSize = size;
                            }
                        }
                    if (info.defensePhase != null)
                        for (int i = 0; i < info.defensePhase.Length; i++)
                            info.defensePhase[i].tagMask = BoxTagRegistry.MaskOf(info.defensePhase[i].tag);
                    //派生门禁的参数在开机就检查一遍：认不出函数名、半径写成"1,2"这种事
                    //要留到玩家搓出那招时才炸，而那时候是战斗中间
                    if (info.cancelTag != null)
                        for (int i = 0; i < info.cancelTag.Length; i++)
                            CancelConditionMethod.Validate(info.cancelTag[i].cancelCondition,
                                                           info.id + " 的第 " + i + " 条 cancelTag");
                    Actions[info.id] = info;
                    _cachedAllActions.Add(info);
                }
            }
        }

        BuildCatalogIndex();
    }

    public static List<ActionInfo> AllActions()
    {
        return _cachedAllActions;
    }

    /// <summary>
    /// 按分类取出这一类的动作，没有就给空数组，运行期不再产生任何临时对象
    /// 【前置条件】索引是对整张表建的，前提是所有角色共用同一份动作表
    /// （GameMain 把 AllActions() 返回的同一个 List 交给每个 ActionController）。
    /// 等 Phase 4 做出"每个角色只会一部分动作"，这个索引就得下移到 ActionController 里
    /// </summary>
    /// <param name="catalog">动作分类，即 ActionInfo.catalog</param>
    /// <returns></returns>
    public static ActionInfo[] ActionsOfCatalog(string catalog)
    {
        //配表漏填的 param 会是 null，而字典不接受 null 键，这里先挡掉
        if (string.IsNullOrEmpty(catalog)) return NoActions;
        return _catalogIndex.TryGetValue(catalog, out ActionInfo[] actions) ? actions : NoActions;
    }

    /// <summary>
    /// 分类倒排索引：以前每次受击/派生都要 new 一个 List 再全表扫一遍 catalog 字符串
    /// 现在只有初始化期分配，运行期只读
    /// </summary>
    private static void BuildCatalogIndex()
    {
        Dictionary<string, List<ActionInfo>> grouped = new Dictionary<string, List<ActionInfo>>();
        foreach (ActionInfo info in _cachedAllActions)
        {
            if (string.IsNullOrEmpty(info.catalog)) continue;
            if (!grouped.TryGetValue(info.catalog, out List<ActionInfo> group))
            {
                group = new List<ActionInfo>();
                grouped[info.catalog] = group;
            }
            group.Add(info);
        }

        _catalogIndex.Clear();
        foreach (KeyValuePair<string, List<ActionInfo>> kv in grouped)
        {
            //定长数组：运行期只能按下标取，长度也改不动
            _catalogIndex.Add(kv.Key, kv.Value.ToArray());
        }
    }
}