using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// 使用 Playables API 代替 Animator Controller 播放动画
/// 完全由外部 LogicTick 手动驱动采样进度
/// </summary>
public class PlayableAnimationPlayer : MonoBehaviour
{
    [System.Serializable]
    public struct StateClipMapping
    {
        public string stateName;
        public AnimationClip clip;
    }

    [SerializeField] private List<StateClipMapping> serializedMappings = new List<StateClipMapping>();

    private PlayableGraph _graph;
    private AnimationMixerPlayable _mixer;
    private Dictionary<string, AnimationClip> _clipCache = new Dictionary<string, AnimationClip>();
    
    private AnimationClipPlayable _currentPlayable;
    private AnimationClipPlayable _transitionPlayable;
    
    private float _transitionDuration = 0f;
    private float _transitionTimer = 0f;
    private bool _isTransitioning = false;
    private float _currentPec = 0f;

    public void Initialize(Animator animator)
    {
        if (animator == null)
        {
            Debug.LogError($"[PlayableAnimationPlayer] Animator passed to Initialize is null on {gameObject.name}!");
            return;
        }

        _clipCache.Clear();

        // 1. 先从序列化好的映射关系中加载（保证在打包运行期也有效）
        foreach (var mapping in serializedMappings)
        {
            if (mapping.clip != null && !_clipCache.ContainsKey(mapping.stateName))
            {
                _clipCache.Add(mapping.stateName, mapping.clip);
                Debug.Log($"[PlayableAnimationPlayer] Loaded State Mapping: {mapping.stateName} -> {mapping.clip.name}");
            }
        }

        // 2. 如果在 Editor 环境，自动抽取最新的 Animator Controller 状态名称与 Clip 的映射关系
#if UNITY_EDITOR
        PopulateMappingsFromController(animator);
#endif

        // 3. 兜底策略：把未映射的原生 AnimationClip.name 也加入缓存
        if (animator.runtimeAnimatorController != null)
        {
            foreach (var clip in animator.runtimeAnimatorController.animationClips)
            {
                if (!_clipCache.ContainsKey(clip.name))
                {
                    _clipCache.Add(clip.name, clip);
                    Debug.Log($"[PlayableAnimationPlayer] Cached raw clip fallback: {clip.name}");
                }
            }
        }

        // 初始化 PlayableGraph 拓扑
        _graph = PlayableGraph.Create("PlayablePlayer_" + gameObject.name);
        _mixer = AnimationMixerPlayable.Create(_graph, 2);
        
        var output = AnimationPlayableOutput.Create(_graph, "AnimationOutput", animator);
        output.SetSourcePlayable(_mixer);
        
        _graph.Play();
    }

#if UNITY_EDITOR
    private void PopulateMappingsFromController(Animator animator)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;

        UnityEditor.Animations.AnimatorController controller = null;
        AnimatorOverrideController overrideController = animator.runtimeAnimatorController as AnimatorOverrideController;
        if (overrideController != null)
        {
            controller = overrideController.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
        }
        else
        {
            controller = animator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
        }

        if (controller == null) return;

        bool changed = false;
        foreach (var layer in controller.layers)
        {
            foreach (var stateInfo in layer.stateMachine.states)
            {
                var state = stateInfo.state;
                var clip = state.motion as AnimationClip;
                if (clip != null)
                {
                    AnimationClip targetClip = clip;
                    if (overrideController != null)
                    {
                        var overridden = overrideController[clip];
                        if (overridden != null) targetClip = overridden;
                    }

                    int index = serializedMappings.FindIndex(m => m.stateName == state.name);
                    if (index >= 0)
                    {
                        if (serializedMappings[index].clip != targetClip)
                        {
                            var mapping = serializedMappings[index];
                            mapping.clip = targetClip;
                            serializedMappings[index] = mapping;
                            changed = true;
                        }
                    }
                    else
                    {
                        serializedMappings.Add(new StateClipMapping { stateName = state.name, clip = targetClip });
                        changed = true;
                    }

                    if (!_clipCache.ContainsKey(state.name))
                    {
                        _clipCache.Add(state.name, targetClip);
                        Debug.Log($"[PlayableAnimationPlayer] Cached State Mapping: {state.name} -> {targetClip.name}");
                    }
                }
            }
        }

        if (changed)
        {
            UnityEditor.EditorUtility.SetDirty(this);
        }
    }
#endif

    public float GetClipLength(string clipName)
    {
        if (_clipCache.TryGetValue(clipName, out AnimationClip clip))
            return clip.length;
        return 0f;
    }

    public void PlayClip(string clipName, float transitionDuration, float startPercentage)
    {
        if (!_clipCache.TryGetValue(clipName, out AnimationClip newClip))
        {
            Debug.LogError($"[PlayableAnimationPlayer] Animation clip not found: {clipName}");
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

    // 由 TickManager 在逻辑 Tick 的表现采样阶段统一调用
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
                if (_transitionPlayable.IsValid())
                {
                    _transitionPlayable.Destroy();
                }
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
