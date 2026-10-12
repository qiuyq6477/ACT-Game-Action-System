using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 取消条件函数表：表里那条 `CancelTag` 想挤掉当前动作，除了标签对得上、指令搓得对，
/// 还得多过一道"这时候允许吗"的门禁。
///
/// 解决的问题：本工程的派生只看「窗口开着 + 标签相交 + 指令成立」，这三条全跟"我在什么状态"无关。
/// BeatEmUp 那种"空中不许接地面招""只有对手真躺在地上了才能追击"就配不出来——
/// 硬要配的话只能在代码里写 if，招式表就漏了洞（同样的门禁每条招各写一遍，改一处忘四处）。
/// 所以照 `RootMotionMethod` / `ForceMoveMethod` / `PickActionMethod` 的样子再开一张函数表，
/// 表里填函数名和参数，判定逻辑留在代码里但只有一个入口。
///
/// 【参数为什么留字符串、不在 GameData.Load 里烘焙成数值】：AGENTS.md 那条规矩针对的是每帧都跑的路径，
/// 而这个函数只在"一次派生真的差点要成立"的时候才被问一次（标签相交 + 指令已经匹配上了），
/// 一局里总共问不了几百次。字符串比对不分配，数字用 TryParse 也不分配。
/// 真正的代价在另一头：烘焙要先把所有关键字注册一遍，而关键字唯一的消费者就是这两个函数。
/// 所以 `Validate` 只在读表期检查参数写没写对，不改写它。
/// </summary>
public static class CancelConditionMethod
{
    /// <summary>
    /// 表里没配半径时用的默认值。取 1.2 米是 BeatEmUp 那只 HitBox 打得到人的距离量级
    /// （盒子世界宽度 0.79 米，加上双方受击盒半径 0.3 米），不是拍一个看着顺眼的数
    /// </summary>
    private const float DefaultNearbyRange = 1.2f;

    public static Dictionary<string, Func<CharacterObj, string[], bool>> Methods =
        new Dictionary<string, Func<CharacterObj, string[], bool>>
        {
            {"StateIs", EvalStateIs},
            {"NearbyUnitInAction", EvalNearbyUnit},
        };

    /// <summary>
    /// 门禁求值。表里没配条件就是没有条件——现有 13 条动作一条都不用改，跟改造前逐帧等价
    /// </summary>
    /// <param name="info">表里 CancelTag.cancelCondition 那一格</param>
    /// <param name="self">想挤掉别人的那个动作的主人</param>
    /// <returns></returns>
    public static bool Allows(ScriptMethodInfo info, CharacterObj self)
    {
        if (string.IsNullOrEmpty(info.method)) return true;
        if (!Methods.TryGetValue(info.method, out Func<CharacterObj, string[], bool> eval))
        {
            //认不出来只能否掉：默认允许的话，函数名打错一个字母就等于悄悄把门禁拆了，
            //而"这招在空中也能接地面招"这种手感问题没人会怀疑到表里
            Debug.LogError("找不到取消条件函数 \"" + info.method + "\"：这次派生按不成立处理，" +
                           "新增函数请在 Methods/CancelConditionMethod.cs 的表里注册");
            return false;
        }

        return eval(self, info.param);
    }

    /// <summary>
    /// 读表期检查：参数写错要在开机就喊，不能等到玩家搓出那招的时候再抛异常
    /// （`float.Parse` 遇到 "1,2" 这种手滑写法是直接抛的，而那正好是在战斗中间）
    /// </summary>
    /// <param name="info">表里那一格</param>
    /// <param name="ownerId">哪条动作的哪个 tag，只为了报错时说得清</param>
    public static void Validate(ScriptMethodInfo info, string ownerId)
    {
        if (string.IsNullOrEmpty(info.method)) return;
        if (!Methods.ContainsKey(info.method))
        {
            Debug.LogError(ownerId + " 的取消条件写了 \"" + info.method + "\"，函数表里没有这一条");
            return;
        }

        string[] param = info.param ?? new string[0];
        switch (info.method)
        {
            case "StateIs":
                if (param.Length == 0)
                    Debug.LogError(ownerId + " 的 StateIs 没写参数：要 Grounded / Airborne / Any 中的一个");
                else if (param[0] != "Grounded" && param[0] != "Airborne" && param[0] != "Any")
                    Debug.LogError(ownerId + " 的 StateIs 参数是 \"" + param[0] + "\"，只认 Grounded / Airborne / Any");
                break;
            case "NearbyUnitInAction":
                if (param.Length == 0)
                    Debug.LogError(ownerId + " 的 NearbyUnitInAction 没写动作 id：它查的就是" +
                                   "\"谁的当前动作等于这个 id\"，比如追击倒地敌人就写 KnockdownGrounded");
                if (param.Length > 1)
                {
                    //TryParse 的 out 参数在 && 短路边上不算"已赋值"，所以把它单独拿出来判
                    bool isNumber = float.TryParse(param[1],
                        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
                        out float range);
                    if (!isNumber)
                        Debug.LogError(ownerId + " 的 NearbyUnitInAction 半径 \"" + param[1] + "\" 不是个数");
                    else if (range <= 0f)
                        Debug.LogError(ownerId + " 的 NearbyUnitInAction 半径是 " + range +
                                       "，非正数意味着谁都算在近处之外");
                }
                break;
        }
        //新增一个函数就要在这里补一条 case：认参数是这两个函数唯一的"写错了会怎样"的口径
    }

    /// <summary>
    /// 地面/空中门禁。横版动作游戏里最常见的门禁："空中按拳只能出空中技"就靠它
    /// </summary>
    private static bool EvalStateIs(CharacterObj self, string[] param)
    {
        string want = param == null || param.Length == 0 ? "" : param[0];
        switch (want)
        {
            case "Any": return true;
            case "Grounded": return self.State == MoveState.Grounded;
            case "Airborne": return self.State == MoveState.Airborne;
        }

        //读表期已经喊过一次，运行期再遇到（比如运行中改了表）也只能否掉
        Debug.LogError("StateIs 的参数 \"" + want + "\" 不认识：按不成立处理");
        return false;
    }

    /// <summary>
    /// "近处有没有一个正处于某个动作的角色"。BeatEmUp 的倒地追击就写
    /// {method:"NearbyUnitInAction", param:["KnockdownGrounded","1.2"]}
    /// 【距离为什么只算 x 和 z】：HD-2D 里角色不会飘在不同高度上打架，y 归地面探测管；
    /// 用它做距离会让"同一个车道前后站"被算成很远
    /// </summary>
    private static bool EvalNearbyUnit(CharacterObj self, string[] param)
    {
        string actionId = param == null || param.Length == 0 ? "" : param[0];
        if (actionId.Length == 0) return false;

        float range = DefaultNearbyRange;
        if (param.Length > 1 && float.TryParse(param[1],
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
                out float parsed) && parsed > 0f) range = parsed;

        return CharacterObj.AnyOtherInAction(self, actionId, range);
    }
}
