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
    /// 保持一个按键的存在时间最多这么多秒，太早的就释放掉了
    /// </summary>
    private const float RecordKeepTime = 1.2f;

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
    /// 现在的时间戳
    /// </summary>
    private double _timeStamp = 0;

    /// <summary>
    /// 非方向输入得有个cd，不然频率太高影响连招手感
    /// 如果没有这个，非常容易出现“全自动连续攻击”，如果攻击派生动作的按键一样的话
    /// </summary>
    private const float NonDirectionInputCooldown = 0.09f;

    private float _nonDirectCooldown = 0;
    
    /// <summary>
    /// 值得注意的是：
    /// 好的输入是会判断up和down的，这样才能得出是tap还是holding
    /// 但是操作并不是这个demo主要表现的内容，所以我就偷了个懒
    /// 其实也不是没有动作游戏采取这种“Turbo输入”的
    /// 没有holding和tap，用这种"Turbo输入"的结果就是一些手感会变差，比如移动，还有蓄力，但不是做不了，得凑
    /// </summary>
    private void Update()
    {
        long gcProbe = GcProbe.Begin(GcProbe.Region.InputUpdate);
        float dt = Time.deltaTime;
        //开始去掉已经过期的操作记录
        int index = 0;
        while (index < _input.Count)
            //等于的时候还是保留一下，其实无所谓，就那么1帧的差别
            if (_timeStamp - _input[index].TimeStamp > RecordKeepTime) _input.RemoveAt(index);
            else index++;
        
        //最新输入刷新了
        _newInputs.Clear();

        if (controller)
        {
            bool noKey = true;
            //加入新的输入
            if (_nonDirectCooldown <= 0)
            {
                if (Input.GetButton("Punch"))
                {
                    AddInput(KeyMap.Punch);
                    noKey = false;
                }
                if (Input.GetButton("Kick"))
                {
                    AddInput(KeyMap.Kick);
                    noKey = false;
                }

                if (!noKey) _nonDirectCooldown = NonDirectionInputCooldown;
            }
            else
            {
                _nonDirectCooldown -= dt;
            }

            float xInput = Input.GetAxis("Horizontal");
            float yInput = Input.GetAxis("Vertical");
            float deadArea = 0.2f;
            bool xHasInput = Mathf.Abs(xInput) >= deadArea;
            bool yHasInput = Mathf.Abs(yInput) >= deadArea;
            bool noDir = !xHasInput && !yHasInput;
        
            //上下左右4个方向，每帧都走这里，所以不能是数组（原来的 new[] 就是每帧一次分配）
            bool up = false, duck = false, back = false, forward = false;
            if (xHasInput)
                if (xInput > 0) forward = true;
                else back = true;

            if (yHasInput)
                if (yInput > 0) up = true;
                else duck = true;

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
        
        //计数器
        _timeStamp += Time.deltaTime;
        GcProbe.End(GcProbe.Region.InputUpdate, gcProbe);
    }

    private void AddInput(KeyMap key)
    {
        KeyInputRecord kir = new KeyInputRecord(key, _timeStamp);
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
        double lastStamp = _timeStamp - Mathf.Max(action.validInSec, Time.deltaTime);   //最小就是上1帧
        for (int i = 0; i < action.keySequence.Length; i++)
        {
            int idx = 0;
            bool found = false;
            //之所以这里不能记录上一次的j，把上一次的j作为j的起点，是因为同一帧会有若干输入
            //他们的排序是不稳定的，但是他们的先后顺序应该都是相等的，比如同一帧输入了前a，那么它既可以是前→a，也可以是a→前。
            //所以我们宁可牺牲一些性能，也要追求精准
            for (int j = 0; j < _input.Count; j++)
            {
                if (_input[j].TimeStamp >= lastStamp && GetKeyInput(_input[j].Key) == action.keySequence[i])
                {
                    found = true;
                    lastStamp = _input[j].TimeStamp;
                    break;
                }
            }
            if (found) continue;
            //特殊处理最后一个，最后一个可以检查_newInput里面获取
            //这是一个策划配表和Update之间的妥协，如果是帧作为单位就不会有问题，但是update……
            //策划作为地球人，可不知道delta是多少，每台电脑的delta都不一样，所以只能做这个补丁
            //当然也可以做成如果检查时间都不符合的情况下，所有的指令都看new，着看你需要咋样的手感了
            //我这里就用最后一个键可以访问new的
            if (i == action.keySequence.Length - 1)
            {
                for (int j = 0; j < _newInputs.Count; j++)
                {
                    if (GetKeyInput(_newInputs[j].Key) == action.keySequence[i])
                    {
                        found = true;
                        lastStamp = _newInputs[j].TimeStamp;
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
        double lastStamp = _timeStamp - Time.deltaTime; //和ActionOccur一样，最短只看上一帧
        //输入记录是按时间戳追加的，所以倒着找，一旦早于窗口就可以直接收工
        for (int i = _input.Count - 1; i >= 0; i--)
        {
            if (_input[i].TimeStamp < lastStamp) break;
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
            if (IsDirectionInput(list[readIndex].Key))
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
    /// 按下的时间记录
    /// </summary>
    public double TimeStamp;

    /// <summary>
    /// 按下的键
    /// </summary>
    public KeyMap Key;

    public KeyInputRecord(KeyMap key, double timeStamp)
    {
        Key = key;
        TimeStamp = timeStamp;
    }
}
