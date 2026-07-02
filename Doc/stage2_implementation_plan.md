# ACT 动作系统重构：阶段 2 实施计划（全面转为“帧”整型）

本计划详述了**阶段 2**的重构设计、接口改动与实施步骤。本阶段的目标是消除浮点数百分比（`_pec`）的时间表示，在逻辑层以 60Hz 逻辑帧（**`int Frame`**）进行严格的精确匹配，彻底解决因浮点插值产生的跳帧与精度误差。

---

## 📋 实施任务分解

本阶段分为 4 个子任务，我们将依次进行：

### 任务 2.1：定义序列化数据（`ActionData`）与逻辑运行数据（`ActionInfo`）
* **目的**：解耦磁盘数据结构（JSON）与逻辑层运行数据，保持对现有 `Action.json` 的兼容，同时允许在运行时动态转换。
* **实现逻辑**：
  1. 将现有的 [ActionInfo.cs](file:///C:/project/ACT-Game-Action-System/Assets/Scripts/Character/Action/ActionInfo.cs) 重命名为 `ActionData.cs`（及其子结构如 `CancelTagData`，`BeCancelledTagData` 等），使其专用于 JSON 序列化加载。
  2. 重新定义 `ActionInfo.cs` 及其子结构，全部采用 `int` 类型（例如：`startFrame`，`endFrame`，`fadeOutFrames`，`fadeInFrames`），代表第几帧，使其在逻辑计算中只发生整数比对。

### 任务 2.2：实现基于角色动画长度的“时钟帧率转换”
* **目的**：在角色初始化时，将加载的百分比配置转换成当前角色具体的帧数值。由于不同角色可重写 Clip 长度，故该转换在角色初始化时进行。
* **实现逻辑**：
  1. 在 `ActionController.SetAllActions` 中，传入 `List<ActionData>`，并逐个转换为 `ActionInfo`。
  2. **帧数换算公式**：
     $$\text{MaxFrames} = \text{Mathf.RoundToInt}(\text{ClipLength} \times 60\text{f})$$
     $$\text{TargetFrame} = \text{Mathf.RoundToInt}(\text{Percentage} \times \text{MaxFrames})$$
  3. 例如，一个 1.5 秒的动作，共有 $1.5 \times 60 = 90$ 帧。配置中 `min = 0.2` 被转换为第 $0.2 \times 90 = 18$ 帧。

### 任务 2.3：重构 `ActionController` 逻辑帧循环
* **目的**：使 `ActionController` 彻底运行在帧的概念上。
* **实现逻辑**：
  1. 声明字段：`private int _curFrame` (当前动作进行到第几帧), `private int _lastFrame` (上一 Tick 所在帧), `private int _maxFrames` (当前动作总帧数)。
  2. 在 `LogicTick(float delta)` 中：
     * **硬直状态**：若 `Freezing`，只扣减硬直时间，`_curFrame` 不递增，且 `_lastFrame = _curFrame`。
     * **正常状态**：`_lastFrame = _curFrame;`，然后 `_curFrame++`。
     * 计算百分比 `float pec = (float)_curFrame / _maxFrames` 并传递给 `playablePlayer.SetPec(pec)`。
  3. 修改区间判定方法：
     * 碰撞盒开启判定：
       ```csharp
       // 判断当前帧是否落入配置的帧区间内
       if (_curFrame >= range.startFrame && _curFrame <= range.endFrame)
       ```
     * Cancel 条件判定：
       ```csharp
       // 判断当前动作被取消的帧区间是否与上一帧到这一帧的跨度有交集
       if (bcTag.startFrame <= _curFrame && bcTag.endFrame >= _lastFrame)
       ```

### 任务 2.4：适配 Temp 命中派生与输入判定
* **目的**：重构在命中或防御触发时，临时开启的 CancelTag（从百分比改写为以帧为长度单位的计数）。
* **实现逻辑**：
  1. 重写 `AddTempBeCancelledTag`：
     ```csharp
     // 命中时，开启从当前帧开始，持续持续配置帧数（durationFrames）的临时 Cancel 区间
     int startFrame = _curFrame;
     int endFrame = _curFrame + tempTag.durationFrames;
     ```

---

## 🛠️ 数据结构重构对照表

### 1. 新建 `ActionData.cs` (原 ActionInfo 兼容 JSON)
```csharp
[System.Serializable]
public struct ActionData
{
    public string id;
    public string animKey;
    public string catalog;
    public CancelTagData[] cancelTag;
    public BeCancelledTagData[] beCancelledTag;
    public TempBeCancelledTagData[] tempBeCancelledTag;
    public ActionCommand[] commands; // 原有 command 不变
    public MoveInputAcceptanceData[] inputAcceptance;
    public string autoNextActionId;
    public bool keepPlayingAnim;
    public bool autoTerminate;
    public AttackInfo[] attacks;
    public AttackBoxTurnOnInfoData[] attackPhase;
    public BeHitBoxTurnOnInfoData[] defensePhase;
    public ScriptMethodInfo rootMotionTween;
    public int priority;
    public bool flip;
}
```

### 2. 逻辑运行层 `ActionInfo.cs` (整型帧结构)
```csharp
public struct ActionInfo
{
    public string id;
    public string animKey;
    public string catalog;
    
    public int maxFrames; // 当前动作最大帧数 (ClipLength * 60)
    
    public CancelTag[] cancelTag;
    public BeCancelledTag[] beCancelledTag;
    public TempBeCancelledTag[] tempBeCancelledTag;
    public ActionCommand[] commands;
    
    public MoveInputAcceptance[] inputAcceptance;
    
    public string autoNextActionId;
    public bool keepPlayingAnim;
    public bool autoTerminate;
    
    public AttackInfo[] attacks;
    public AttackBoxTurnOnInfo[] attackPhase;
    public BeHitBoxTurnOnInfo[] defensePhase;
    
    public ScriptMethodInfo rootMotionTween;
    public int priority;
    public bool flip;
}
```

---

## 📈 阶段 2 核心转换逻辑与接口

在 `ActionController.cs` 中增加核心转换方法：

```csharp
public ActionInfo ConvertToRuntimeInfo(ActionData data)
{
    float clipLength = playablePlayer.GetClipLength(data.animKey);
    int maxFrames = Mathf.RoundToInt(clipLength * 60f);

    ActionInfo info = new ActionInfo();
    info.id = data.id;
    info.animKey = data.animKey;
    info.catalog = data.catalog;
    info.maxFrames = maxFrames;
    info.commands = data.commands;
    info.autoNextActionId = data.autoNextActionId;
    info.keepPlayingAnim = data.keepPlayingAnim;
    info.autoTerminate = data.autoTerminate;
    info.attacks = data.attacks;
    info.rootMotionTween = data.rootMotionTween;
    info.priority = data.priority;
    info.flip = data.flip;

    // 1. 转换被取消的 BeCancelledTag 范围
    info.beCancelledTag = new BeCancelledTag[data.beCancelledTag.Length];
    for (int i = 0; i < data.beCancelledTag.Length; i++)
    {
        var raw = data.beCancelledTag[i];
        info.beCancelledTag[i] = new BeCancelledTag
        {
            startFrame = Mathf.RoundToInt(raw.min * maxFrames),
            endFrame = Mathf.RoundToInt(raw.max * maxFrames),
            cancelTag = raw.cancelTag,
            fadeOutFrames = Mathf.RoundToInt(raw.fadeOutPercentage * maxFrames),
            priority = raw.priority
        };
    }

    // 2. 转换 CancelTag 范围（需要查找目标 Action 的动画长度）
    info.cancelTag = new CancelTag[data.cancelTag.Length];
    for (int i = 0; i < data.cancelTag.Length; i++)
    {
        var raw = data.cancelTag[i];
        // 查找目标动作的配置以计算它的帧数
        float targetClipLength = 0f;
        ActionData targetData = GameData.GetActionData(raw.targetActionId);
        if (targetData.id != null)
        {
            targetClipLength = playablePlayer.GetClipLength(targetData.animKey);
        }
        int targetMaxFrames = Mathf.RoundToInt(targetClipLength * 60f);

        info.cancelTag[i] = new CancelTag
        {
            tag = raw.tag,
            startFromFrame = Mathf.RoundToInt(raw.startFromPercentage * targetMaxFrames),
            fadeInFrames = Mathf.RoundToInt(raw.fadeInPercentage * targetMaxFrames),
            priority = raw.priority
        };
    }

    // 3. 转换攻击盒/受击盒的帧数区间
    // ... 对 attackPhase 和 defensePhase 进行类似的区间乘法换算 ...

    return info;
}
```

---

## 🔍 测试与验证方案
1. **动画状态跳转无缝性测试**：
   * 连招测试中，查看从一招 Cancel 到下一招时，画面混合（Crossfade 帧数换算）是否依然能保持预想的平滑度。
2. **边缘帧判定测试**：
   * 校验当动作处于最后一帧（如第 89 帧，总长 90 帧）时，可取消判定与自动衔接动作（`autoNextActionId`）切换是否在第 90 帧准确咬合，没有任何浮点溢出延迟。
