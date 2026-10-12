using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;


/// <summary>
/// 将输入翻译成命令
/// </summary>
public class InputToCommand : MonoBehaviour
{
    /// <summary>
    /// 保持一个按键的记录多少个逻辑帧，太早的就释放掉了（72帧 = 1.2秒）
    /// </summary>
    private const int RecordKeepFrames = 72;

    /// <summary>
    /// 是否接受手柄输入，这个当然可以不是bool，而是接受几号手柄输入
    /// 这个demo里就用bool区分接受还是不接受
    /// </summary>
    public bool controller;
    
    /// <summary>
    /// 当前的按键记录
    /// </summary>
    private List<KeyInputRecord> _input = new List<KeyInputRecord>();
    //最新的输入，如果是检查0的话，就会检查这个
    private List<KeyInputRecord> _newInputs = new List<KeyInputRecord>();

    /// <summary>
    /// 是否是反向的，正向的代表面向右侧
    /// </summary>
    [HideInInspector] public bool inversed = false;

    /// <summary>
    /// 非方向输入得有个cd，不然频率太高影响连招手感
    /// 如果没有这个，非常容易出现“全自动连续攻击”，如果攻击派生动作的按键一样的话
    /// 6个逻辑帧 = 0.09秒
    /// </summary>
    private const int NonDirectionInputCooldownFrames = 6;

    private int _nonDirectCooldownFrames = 0;

    //本渲染帧poll到的按键状态，逻辑帧共用它（见下面的采样与推进为什么要拆开）
    //Block、Jump 与 Grab 用 KeyCode 而不是 Input.GetButton：虚拟按钮要在 ProjectSettings/InputManager 里配一条轴，
    //而这份配置得编辑器启动时重读才生效。Step 2 换新版 Input System 时它们会和其他键一起进 action map
    private bool _sPunch, _sKick, _sUp, _sDuck, _sBack, _sForward, _sBlock, _sJump, _sGrab;

    /// <summary>
    /// Unity的按键状态每渲染帧只poll得到一次，所以采样必须在逻辑时钟之外做。
    /// 30FPS下一个快照会被两个逻辑帧共用，120FPS下有些逻辑帧看到的还是上一个快照——
    /// 这是引擎的上限，改成什么逻辑帧率都救不回来
    /// </summary>
    public void SampleRenderFrame()
    {
        if (!controller) return;

        _sPunch = Input.GetButton("Punch");
        _sKick = Input.GetButton("Kick");
        _sBlock = Input.GetKey(KeyCode.LeftShift);
        _sJump = Input.GetKey(KeyCode.Space);
        _sGrab = Input.GetKey(KeyCode.E);

        float xInput = Input.GetAxis("Horizontal");
        float yInput = Input.GetAxis("Vertical");
        float deadArea = 0.2f;
        bool xHasInput = Mathf.Abs(xInput) >= deadArea;
        bool yHasInput = Mathf.Abs(yInput) >= deadArea;

        //上下左右4个方向，每帧都走这里，所以不能是数组（原来的 new[] 就是每帧一次分配）
        //竖轴在这个游戏里是【纵深车道】，不是上段/下段（HD-2D 的约定，见 Doc/decision/0002）：
        //屏幕上方 = 远离镜头。值仍然叫 Up/Duck——它们就是小键盘的 8/2（屏幕纵向），
        //而且 GetKeyInput 本来就不镜像这两个，正是纵深需要的性质。
        //表里用 ↓↘→ 这类竖轴指令的招式（比如 TwinKick）因此照常可搓，语义变成"从近侧车道绕过来"
        _sUp = yHasInput && yInput > 0;
        _sDuck = yHasInput && yInput < 0;
        _sBack = xHasInput && xInput < 0;
        _sForward = xHasInput && xInput > 0;
    }

    /// <summary>
    /// 把本渲染帧采到的状态登记成一个逻辑帧的输入记录
    /// 值得注意的是：
    /// 好的输入是会判断up和down的，这样才能得出是tap还是holding
    /// 但是操作并不是这个demo主要表现的内容，所以我就偷了个懒
    /// 其实也不是没有动作游戏采取这种“Turbo输入”的
    /// 没有holding和tap，用这种"Turbo输入"的结果就是一些手感会变差，比如移动，还有蓄力，但不是做不了，得凑
    /// </summary>
    public void Tick()
    {
        long gcProbe = GcProbe.Begin(GcProbe.Region.InputUpdate);
        //开始去掉已经过期的操作记录
        int index = 0;
        while (index < _input.Count)
            //等于的时候还是保留一下，其实无所谓，就那么1帧的差别
            if (LogicClock.Frame - _input[index].Frame > RecordKeepFrames) _input.RemoveAt(index);
            else index++;
        
        //最新输入刷新了
        _newInputs.Clear();

        if (controller)
        {
            bool noKey = true;

            //Block 每拍都登记，而且不吃下面那个非方向键的 6 拍节流。
            //长按要问的是"这一拍还按着吗"，节流会让按住期间每隔 6 拍才留一条记录，
            //而按住判定的窗口只有一拍（见 IsKeyHeld），必然被读成松手——格挡就会一卡一卡地失效
            if (_sBlock)
            {
                AddInput(KeyMap.Block);
                //按着格挡不算"什么都没按"：否则 NoInput 那种"站着不动"的派生会被骗出来
                noKey = false;
            }

            //加入新的输入
            if (_nonDirectCooldownFrames <= 0)
            {
                if (_sPunch)
                {
                    AddInput(KeyMap.Punch);
                    noKey = false;
                }
                if (_sKick)
                {
                    AddInput(KeyMap.Kick);
                    noKey = false;
                }
                if (_sJump)
                {
                    AddInput(KeyMap.Jump);
                    noKey = false;
                }
                if (_sGrab)
                {
                    AddInput(KeyMap.Grab);
                    noKey = false;
                }

                if (!noKey) _nonDirectCooldownFrames = NonDirectionInputCooldownFrames;
            }
            else
            {
                _nonDirectCooldownFrames--;
            }

            bool up = _sUp, duck = _sDuck, back = _sBack, forward = _sForward;
            bool noDir = !up && !duck && !back && !forward;
        
            //这里不能else，因为可能同一帧有8个方向的输入的，但是unity和ue的input都自作聪明的屏蔽了……
            //尽管他们有问题，但我们不能有问题
            if (up) AddInput(KeyMap.Up);
            if (duck) AddInput(KeyMap.Duck);
            if (back) AddInput(KeyMap.Backward);
            if (forward) AddInput(KeyMap.Forward);
            if (up && back) AddInput(KeyMap.UpBackward);
            if (duck && back) AddInput(KeyMap.DuckBackward);
            if (up && forward) AddInput(KeyMap.UpForward);
            if (duck && forward) AddInput(KeyMap.DuckForward);
        
            //最后看是否要加入没有操作的操作
            if (noDir)
                AddInput(noKey ? KeyMap.NoInput : KeyMap.NoDirection);
        }
        GcProbe.End(GcProbe.Region.InputUpdate, gcProbe);
    }

    /// <summary>
    /// 把一个按键登记在"当前逻辑帧"上。
    /// 键盘采样走这里，测试和以后的AI、回放也走这里——逻辑层只认"第几帧按了什么"，不认输入设备
    /// </summary>
    /// <param name="key">按下的键</param>
    public void AddInput(KeyMap key)
    {
        KeyInputRecord kir = new KeyInputRecord(key, LogicClock.Frame);
        _input.Add(kir);
        _newInputs.Add(kir);
    }

    /// <summary>
    /// 指定的操作是否存在于记录中
    /// </summary>
    /// <param name="action"></param>
    /// <returns></returns>
    public bool ActionOccur(ActionCommand action)
    {
        //这里的顺序非常重要，所以一定要for而不能foreach，尽管C#尽可能保证了顺序执行，但谁知道呢？
        //回看窗口最少也是一个逻辑帧，跟表里填0是同一个意思
        long lastFrame = LogicClock.Frame - Mathf.Max(action.validInFrames, 1);
        for (int i = 0; i < action.keySequence.Length; i++)
        {
            int idx = 0;
            bool found = false;
            //之所以这里不能记录上一次的j，把上一次的j作为j的起点，是因为同一帧会有若干输入
            //他们的排序是不稳定的，但是他们的先后顺序应该都是相等的，比如同一帧输入了前a，那么它既可以是前→a，也可以是a→前。
            //所以我们宁可牺牲一些性能，也要追求精准
            for (int j = 0; j < _input.Count; j++)
            {
                if (_input[j].Frame >= lastFrame && GetKeyInput(_input[j].Key) == action.keySequence[i])
                {
                    found = true;
                    lastFrame = _input[j].Frame;
                    break;
                }
            }
            if (found) continue;
            //特殊处理最后一个，最后一个可以检查_newInput里面获取
            //登记输入的时候同一个记录既进_input也进_newInputs，所以这一段其实永远不会改变结论，
            //留着只是因为它是老代码；要清理的话得连IsKeyHeld里那一段一起清
            if (i == action.keySequence.Length - 1)
            {
                for (int j = 0; j < _newInputs.Count; j++)
                {
                    if (GetKeyInput(_newInputs[j].Key) == action.keySequence[i])
                    {
                        found = true;
                        lastFrame = _newInputs[j].Frame;
                        break;
                    }
                }
            }
            if (found) continue;
            //有一个输入没找到，那自然就结束了
            return false;   
        }

        return true;    //肯定找到了才会到这里
    }

    /// <summary>
    /// 某个键在当前这一帧是否处于按住状态
    /// 给"移动意图"这类单键判定用的极速路径：ActionOccur 要为多键搓招保序，
    /// 每次都得带着数组走一遍子序列匹配，而按住判定只需要看这一帧有没有该键的记录
    /// </summary>
    /// <param name="key">要看的键（已经是大脑里的方向，不受正反手影响）</param>
    /// <returns></returns>
    public bool IsKeyHeld(KeyMap key)
    {
        long lastFrame = LogicClock.Frame - 1;    //和ActionOccur一样，最短只看上一帧
        //输入记录是按逻辑帧追加的，所以倒着找，一旦早于窗口就可以直接收工
        for (int i = _input.Count - 1; i >= 0; i--)
        {
            if (_input[i].Frame < lastFrame) break;
            if (GetKeyInput(_input[i].Key) == key) return true;
        }

        //和ActionOccur对最后一个键的处理保持一致：本帧刚按下、还没来得及进历史记录的也算
        for (int i = 0; i < _newInputs.Count; i++)
            if (GetKeyInput(_newInputs[i].Key) == key) return true;

        return false;
    }

    /// <summary>
    /// 根据origin和inversed得出实际上输入是什么
    /// 我们毕竟有左右方向正反手搓招的
    /// </summary>
    /// <param name="origin"></param>
    /// <returns></returns>
    private KeyMap GetKeyInput(KeyMap origin)
    {
        if (!inversed) return origin;
        switch (origin)
        {
            case KeyMap.Backward: return KeyMap.Forward;
            case KeyMap.Forward: return KeyMap.Backward;
            case KeyMap.DuckBackward: return KeyMap.DuckForward;
            case KeyMap.DuckForward: return KeyMap.DuckBackward;
            case KeyMap.UpBackward: return KeyMap.UpForward;
            case KeyMap.UpForward: return KeyMap.UpBackward;
        }

        return origin;
    }

    /// <summary>
    /// 获得输入列表所组成的文字
    /// </summary>
    /// <returns></returns>
    public string InputText()
    {
        string res = "输入：\n";
        Dictionary<KeyMap, int> pCount = new Dictionary<KeyMap, int>
        {
            {KeyMap.Punch, 0},
            {KeyMap.Kick, 0},
            {KeyMap.Up, 0},
            {KeyMap.UpForward, 0},
            {KeyMap.Forward, 0},
            {KeyMap.DuckForward, 0},
            {KeyMap.Duck, 0},
            {KeyMap.DuckBackward, 0},
            {KeyMap.Backward, 0},
            {KeyMap.UpBackward, 0},
        };
        for (int i = 0; i < _input.Count; i++)
            if (pCount.ContainsKey(_input[i].Key)) pCount[_input[i].Key] += 1;
        
        foreach (KeyValuePair<KeyMap,int> pair in pCount)
        {
            string keyText = "";
            switch (pair.Key)
            {
                case KeyMap.Punch: keyText = "【拳】"; break;
                case KeyMap.Kick: keyText = "【脚】"; break;
                case KeyMap.Backward: keyText = "【←】"; break;
                case KeyMap.UpBackward: keyText = "【↖】"; break;
                case KeyMap.Up: keyText = "【↑】"; break;
                case KeyMap.UpForward: keyText = "【↗】"; break;
                case KeyMap.Forward: keyText = "【→】"; break;
                case KeyMap.DuckForward: keyText = "【↘】"; break;
                case KeyMap.Duck: keyText = "【↓】"; break;
                case KeyMap.DuckBackward: keyText = "【↙】"; break;
            }

            bool hasNew = false;
            foreach (KeyInputRecord record in _newInputs)
            {
                if (record.Key == pair.Key)
                {
                    hasNew = true;
                    break;
                }
            }

            res += keyText + " x " + pair.Value + " 次" + (hasNew ? "【新输入】":"") +  "\n";
        }

        return res;
    }

    /// <summary>
    /// 删除所有的非方向输入，在变化动作的时候删除
    /// 采用原址双指针原地过滤，避免频繁调用 RemoveAt 导致的内部数组大量内存拷贝与时间退化。
    /// </summary>
    public void CleanNonDirectionInputs()
    {
        FilterInPlace(_input);
        FilterInPlace(_newInputs);
    }

    private static void FilterInPlace(List<KeyInputRecord> list)
    {
        int writeIndex = 0;
        for (int readIndex = 0; readIndex < list.Count; readIndex++)
        {
            if (KeepAcrossActionChange(list[readIndex].Key))
            {
                if (writeIndex != readIndex)
                {
                    list[writeIndex] = list[readIndex];
                }
                writeIndex++;
            }
        }
        if (writeIndex < list.Count)
        {
            list.RemoveRange(writeIndex, list.Count - writeIndex);
        }
    }

    /// <summary>
    /// 换动作时哪些按键的历史要留下。方向键必须留（搓招是看一段序列的，清空就等于把连招打断），
    /// Block 也必须留：它表达的是"此刻正按着"这个持续状态，
    /// 换动作就清会让长按格挡在派生的那一刻断掉，表现是"挡到一半自己泄了"
    /// 【为什么不干脆说 Block 是方向键】：那是句假话——它不参与 3×3 的方向语义，
    /// 也不该被 noDir 那类判定算进去；留历史是它的登记规则，不是它的分类
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    private static bool KeepAcrossActionChange(KeyMap key)
    {
        return IsDirectionInput(key) || key == KeyMap.Block;
    }

    /// <summary>
    /// 一个输入是否是方向输入
    /// switch 语句，底层编译为跳转表 (Jump Table)，实现 0 GC 堆分配与 O(1) 极速匹配。
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    public static bool IsDirectionInput(KeyMap key)
    {
        switch (key)
        {
            case KeyMap.Backward:
            case KeyMap.UpBackward:
            case KeyMap.Up:
            case KeyMap.UpForward:
            case KeyMap.Forward:
            case KeyMap.DuckForward:
            case KeyMap.Duck:
            case KeyMap.DuckBackward:
            case KeyMap.NoDirection:
                return true;
            default:
                return false;
        }
    }
}

/// <summary>
/// 定义的有效按键对应的命令
/// </summary>
[Serializable]
public enum KeyMap
{
    Punch = 100,
    Kick = 101,
    /// <summary>
    /// 格挡键。它按"按住"用（每拍登记、不吃节流、换动作不清历史），
    /// 所以登记规则跟方向键同一类，但它不是方向——GetKeyInput 不镜像它
    /// </summary>
    Block = 102,
    /// <summary>
    /// 跳跃键。和拳脚一样是"按一下就登记"的非方向输入，因此吃那个 6 拍节流；
    /// 空中重复按不会二段跳——招式表里起跳那行的门禁是 StateIs Grounded
    /// </summary>
    Jump = 103,
    /// <summary>
    /// 抓取/投掷键（源工程就是同一个 Grab 键：没举人时试着抓，举着人时把人摔出去）。
    /// 和拳脚跳跃一样吃那个 6 拍节流。两种含义分开靠表里的门禁 HoldingUnit，不靠这个键
    /// </summary>
    Grab = 104,
    Backward = 4,
    UpBackward = 7,
    Up = 8,
    UpForward = 9,
    Forward = 6,
    DuckForward = 3,
    Duck = 2,
    DuckBackward = 1,
    NoDirection = 0,    //没有输入方向
    NoInput = -1        //没有输入任何按钮
}

/// <summary>
/// 按键记录，放在序列中的
/// </summary>
public struct KeyInputRecord
{
    /// <summary>
    /// 这个输入登记在哪个逻辑帧
    /// </summary>
    public long Frame;

    /// <summary>
    /// 按下的键
    /// </summary>
    public KeyMap Key;

    public KeyInputRecord(KeyMap key, long frame)
    {
        Key = key;
        Frame = frame;
    }
}
