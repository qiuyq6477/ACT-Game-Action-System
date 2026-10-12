
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 根据Catalog选择技能的函数
/// key是Catalog
/// value是(ActionInfo[] of candidates, ForceDirection)=>ActionInfo
/// 执行value获得一个结果
/// 候选数组由 GameData.ActionsOfCatalog 的倒排索引给出，是只读的静态数据，别去改它的长度或元素
/// </summary>
public static class PickActionMethod
{
    public static Dictionary<string, Func<ActionInfo[], ForceDirection, ActionInfo>> Methods =
        new Dictionary<string, Func<ActionInfo[], ForceDirection, ActionInfo>>()
        {
            //受伤
            {
               "Hurt",
               (candidates, dir) =>
               {
                   foreach (ActionInfo info in candidates)
                   {
                       if (
                           (info.id == "HurtFromForward" && dir == ForceDirection.Forward) ||
                           (info.id == "HurtFromBackward" && dir == ForceDirection.Backward)
                       ) return info;
                   }
                   return new ActionInfo();
               }
            },
            //受伤
            {
                "Beaten",
                (candidates, dir) =>
                {
                    foreach (ActionInfo info in candidates)
                    {
                        if (
                            (info.id == "BeatenFromForward" && dir == ForceDirection.Forward) ||
                            (info.id == "BeatenFromBackward" && dir == ForceDirection.Backward)
                        ) return info;
                    }
                    return new ActionInfo();
                }
            },
        };
}