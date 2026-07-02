# ACT 动作系统重构：阶段 1 实施计划（中央时钟驱动与 Playables 解耦）

您的建议非常准确。如果每个角色独立维护累加器，在低帧率卡顿补帧时，不同角色的逻辑 Tick 会出现先后交错或不同步，从而导致碰撞和时序混乱。

为了实现**彻底的逻辑与渲染分离**，本实施计划已更新为：引入全局中央时钟 **`TickManager`**，由其统一管理逻辑帧的累加、补帧追赶以及驱动所有角色的输入、动作与物理更新。

---

## 📋 实施任务分解

本阶段分为 4 个子任务，我们将依次进行：

### 任务 1.1：创建全局中央时钟 `TickManager`
* **目的**：提供统一的定频时钟源，确保所有实体以完全相同的步调、严格的顺序推进逻辑。
* **实现逻辑**：
  1. 创建 `TickManager` 单例组件。
  2. 声明 `const float TICK_RATE = 1f / 60f;`。
  3. 在 `Update()` 内通过逻辑时间累加器进行 Catch-up（追赶补帧）计算。
  4. **严格的顺序更新流水线**：在每一个逻辑 Tick 内，依次执行：
     * **第一步：采集输入**：驱动所有角色读取输入指令（`command.Tick()`）。
     * **第二步：更新动作**：驱动所有角色的动作控制器（`action.Tick()`），推进帧数百分比，判定 Cancel 切换。
     * **第三步：逻辑移动**：计算所有角色的重力、位移并应用逻辑移动。
     * **第四步：碰撞判定**：在全局统一的该逻辑帧时刻，执行碰撞分发（`DealWithAttacks()`）。
     * **第五步：表现层采样**：驱动所有角色的 `PlayableAnimationPlayer` 对齐动画骨骼。

### 任务 1.2：创建 `PlayableAnimationPlayer` 组件
* **目的**：建立一个完全受代码支配的动画播放器，接管动画混合与采样。
* **实现逻辑**：
  1. 挂载到挂有 `Animator` 的 GameObject 上。
  2. 在 `Awake` 中，从 `Animator.runtimeAnimatorController` 中提取并缓存所有关联的 `AnimationClip`，免去重新导表或修改现有 Prefab 资源的麻烦。
  3. 构建由 `PlayableGraph` $\rightarrow$ `AnimationMixerPlayable` $\rightarrow$ `AnimationPlayableOutput` 组成的 Playable 拓扑结构。
  4. 暴露 `PlayClip(string clipName, float transitionDuration, float startPercentage)`，通过混合器控制两个动画之间的淡入淡出（CrossFade）融合。
  5. 暴露 `EvaluateAnimation(float percentage, float dt)`，在每个逻辑 Tick 结束时被动调用，强制更新骨骼姿态。

### 任务 1.3：改造 `ActionController` 移除非确定性逻辑
* **目的**：移除原有的 `Update` 和变长 DeltaTime 计算，全部接入由 `TickManager` 驱动的 `LogicTick` 接口。
* **实现逻辑**：
  1. 废除 `ActionController.Update()` 及其自带的 `anim.speed = Freezing ? 0 : 1;` 妥协代码。
  2. 声明接口 `public void LogicTick(float tickDelta)`。
  3. 若角色处于顿帧状态（`_freezing > 0`），则**仅扣减 `_freezing` 倒计时**，跳过动作百分比 `_pec` 的累加，以此在 Tick 结束时实现确定性的顿帧。
  4. 将所有的 `anim.CrossFade(...)` 替换为调用 `PlayableAnimationPlayer.PlayClip(...)`。

### 任务 1.4：改造主流程（`GameMain` 与 `CharacterObj`）
* **目的**：将物理移动与攻击碰撞检测移出 `Update`，归入逻辑 Tick 驱动链条中。
* **实现逻辑**：
  1. 将 `GameMain.Update()` 中的 `ThisTickMove()` 移动计算与 `DealWithAttacks()` 碰撞结算彻底改写，由 `TickManager` 的逻辑 Tick 按帧序列统一驱动。

---

## 🛠️ 新增与改动类设计

### 1. 全局中央时钟 `TickManager.cs`
创建在 `Assets/Scripts/Methods/` 或 `Assets/Scripts/` 目录下：

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

public class TickManager : MonoBehaviour
{
    public static TickManager Instance { get; private set; }

    public const float TICK_RATE = 1f / 60f; // 60Hz 逻辑时钟
    private float _accumulator = 0f;

    // 集中管理场景中的所有角色
    [SerializeField] private GameMain gameMain;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Update()
    {
        _accumulator += Time.deltaTime;
        int iterations = 0;

        while (_accumulator >= TICK_RATE)
        {
            _accumulator -= TICK_RATE;
            ExecuteGlobalLogicTick(TICK_RATE);
            iterations++;
            
            if (iterations > 10) // 保护机制，防止极低帧率下无限循环
            {
                _accumulator = 0f;
                break;
            }
        }
    }

    private void ExecuteGlobalLogicTick(float dt)
    {
        // 确保所有玩家和敌人的逻辑步骤以极其严格的顺序依次进行，避免时序产生的Bug
        
        // 1. 采集并更新所有实体的输入缓存
        gameMain.player.input.InputTick(dt);
        foreach (var enemy in gameMain.enemy)
        {
            enemy.input.InputTick(dt);
        }

        // 2. 更新所有实体的动作状态机与 Cancel 条件判定
        gameMain.player.action.LogicTick(dt);
        foreach (var enemy in gameMain.enemy)
        {
            enemy.action.LogicTick(dt);
        }

        // 3. 执行逻辑位移计算（结合重力与阻挡）
        gameMain.TickMovements(dt);

        // 4. 判定全局碰撞检测与伤害/受击分发
        gameMain.DealWithAttacksInTick();

        // 5. 表现层骨骼更新与动画融合采样
        gameMain.player.action.playablePlayer.EvaluateAnimation(dt);
        foreach (var enemy in gameMain.enemy)
        {
            enemy.action.playablePlayer.EvaluateAnimation(dt);
        }
    }
}
```

### 2. 精准动画控制组件 `PlayableAnimationPlayer.cs`
挂载到每一个 `CharacterObj` 上，替换 Animator Controller：

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

[RequireComponent(typeof(Animator))]
public class PlayableAnimationPlayer : MonoBehaviour
{
    private PlayableGraph _graph;
    private AnimationMixerPlayable _mixer;
    private Dictionary<string, AnimationClip> _clipCache = new Dictionary<string, AnimationClip>();
    
    private AnimationClipPlayable _currentPlayable;
    private AnimationClipPlayable _transitionPlayable;
    
    private float _transitionDuration = 0f;
    private float _transitionTimer = 0f;
    private bool _isTransitioning = false;
    private float _currentPec = 0f;

    public void Initialize()
    {
        Animator animator = GetComponent<Animator>();
        
        if (animator.runtimeAnimatorController != null)
        {
            foreach (var clip in animator.runtimeAnimatorController.animationClips)
            {
                if (!_clipCache.ContainsKey(clip.name))
                    _clipCache.Add(clip.name, clip);
            }
        }

        _graph = PlayableGraph.Create("PlayablePlayer_" + gameObject.name);
        _mixer = AnimationMixerPlayable.Create(_graph, 2);
        
        var output = AnimationPlayableOutput.Create(_graph, "AnimationOutput", animator);
        output.SetSourcePlayable(_mixer);
        
        _graph.Play();
    }

    public void PlayClip(string clipName, float transitionDuration, float startPercentage)
    {
        if (!_clipCache.TryGetValue(clipName, out AnimationClip newClip))
        {
            Debug.LogError($"[Playable] Animation clip not found: {clipName}");
            return;
        }

        if (transitionDuration <= 0f)
        {
            _isTransitioning = false;
            SetupMainPlayable(newClip, startPercentage);
        }
        else
        {
            _transitionDuration = transitionDuration;
            _transitionTimer = 0f;
            _isTransitioning = true;
            SetupTransitionPlayables(newClip, startPercentage);
        }
    }

    private void SetupMainPlayable(AnimationClip clip, float startPercentage)
    {
        if (_currentPlayable.IsValid()) _currentPlayable.Destroy();
        
        _currentPlayable = AnimationClipPlayable.Create(_graph, clip);
        _currentPlayable.SetSpeed(0); // 完全手动 Evaluate 控制进度
        _currentPlayable.SetTime(clip.length * startPercentage);

        _graph.Connect(_currentPlayable, 0, _mixer, 0);
        _mixer.SetInputWeight(0, 1.0f);
        _mixer.SetInputWeight(1, 0.0f);
    }

    private void SetupTransitionPlayables(AnimationClip nextClip, float startPercentage)
    {
        if (_transitionPlayable.IsValid()) _transitionPlayable.Destroy();
        _transitionPlayable = _currentPlayable;

        _currentPlayable = AnimationClipPlayable.Create(_graph, nextClip);
        _currentPlayable.SetSpeed(0);
        _currentPlayable.SetTime(nextClip.length * startPercentage);

        _graph.Disconnect(_mixer, 0);
        _graph.Disconnect(_mixer, 1);

        _graph.Connect(_transitionPlayable, 0, _mixer, 0);
        _graph.Connect(_currentPlayable, 0, _mixer, 1);

        _mixer.SetInputWeight(0, 1.0f);
        _mixer.SetInputWeight(1, 0.0f);
    }

    public void SetPec(float pec)
    {
        _currentPec = pec;
    }

    // 由 TickManager 在逻辑 Tick 的表现采样阶段（第 5 步）统一调用
    public void EvaluateAnimation(float tickDelta)
    {
        if (_isTransitioning)
        {
            _transitionTimer += tickDelta;
            float weight = Mathf.Clamp01(_transitionTimer / _transitionDuration);
            
            _mixer.SetInputWeight(0, 1.0f - weight);
            _mixer.SetInputWeight(1, weight);

            if (weight >= 1.0f)
            {
                _isTransitioning = false;
                if (_transitionPlayable.IsValid()) _transitionPlayable.Destroy();
            }
        }

        if (_currentPlayable.IsValid())
        {
            AnimationClip clip = _currentPlayable.GetAnimationClip();
            _currentPlayable.SetTime(clip.length * _currentPec);
        }

        _graph.Evaluate();
    }

    void OnDestroy()
    {
        if (_graph.IsValid()) _graph.Destroy();
    }
}
```

---

## 🔍 测试与验证方案
1. **多角色时序同步测试**：
   * 将玩家与敌人的位置和攻击指令固定输入（如一前一后相向攻击）。
   * 开启 `TickManager` 并在同一 Tick 内抓取数据，验证受击和反击是否始终发生在精确的逻辑 Tick 里，没有任何先后帧漂移。
2. **变频降帧测试**：
   * 利用限帧工具限速到 5 FPS，观察补帧期间双方角色是否仍然能同时完成位移和受击反馈。
