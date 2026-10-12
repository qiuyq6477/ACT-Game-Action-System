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
                    //击退与格挡推开的曲线名同理：认不出来不会报错，只会"打中人一动不动"，
                    //而这张表（ForceMoveMethod）和 rootMotionTween 那张（RootMotionMethod）撞过名
                    if (info.attacks != null)
                        for (int i = 0; i < info.attacks.Length; i++)
                        {
                            ForceMoveMethod.Validate(info.attacks[i].pushPower.tweenMethod,
                                                     info.id + " 的第 " + i + " 记 pushPower");
                            ForceMoveMethod.Validate(info.attacks[i].blockPush.tweenMethod,
                                                     info.id + " 的第 " + i + " 记 blockPush");
                        }
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

    /// <summary>
    /// 按 id 取出"某个角色只会这些"的那一份表。返回的是**新建的 List**，
    /// 所以每个角色的子集互不影响；ids 留空则给回共享的全表引用（demo 里那两个角色就是这样）
    /// 【只在初始化期调用】：这里分配一次，运行期不再碰
    /// </summary>
    /// <param name="ids">这个角色会的动作 id，顺序不重要</param>
    public static List<ActionInfo> ActionsFor(string[] ids)
    {
        if (ids == null || ids.Length == 0) return _cachedAllActions;
        List<ActionInfo> subset = new List<ActionInfo>(ids.Length);
        for (int i = 0; i < ids.Length; i++)
        {
            if (!Actions.TryGetValue(ids[i], out ActionInfo info))
            {
                Debug.LogError("动作表里没有 id 为 \"" + ids[i] + "\" 的行：这个角色的招式表要核对一下，" +
                               "现在这条被跳过了，它会既搓不出来也没有动画");
                continue;
            }
            subset.Add(info);
        }
        return subset;
    }
}