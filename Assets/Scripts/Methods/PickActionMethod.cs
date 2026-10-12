
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 根据Catalog选择技能的函数
/// key是Catalog
/// value是(ActionInfo[] of candidates, ForceDirection)=>ActionInfo
/// 执行value获得一个结果
/// 候选数组由 ActionController.ActionsOfCatalog 的倒排索引给出（建在这个角色自己那份动作表上），
/// 是只读的静态数据，别去改它的长度或元素
/// 【认不出方向时的落点必须是 candidates[0]，不能是空的 ActionInfo】：这两支函数按 id 认"正面/背面"，
/// 而一个角色的分类下完全可能只有一行（移植家的受击就只有 PlayerHit 一行，方向无从区分）。
/// 那时候返回空 ActionInfo 等于静默不换动作——挨了一拳站在原地不动，是 Play Mode 里最难查的一类错，
/// 而它离真正的填表错误（分类名写错）只差一个字符
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
                   //这一家里没有分正反手的受击行，就用它仅有的那一行（见类注释）
                   return candidates.Length > 0 ? candidates[0] : new ActionInfo();
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
                    //这一家里没有分正反手的受击行，就用它仅有的那一行（见类注释）
                   return candidates.Length > 0 ? candidates[0] : new ActionInfo();
                }
            },
        };
}