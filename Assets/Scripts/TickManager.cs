using UnityEngine;

/// <summary>
/// 全局中央逻辑时钟，统一管理逻辑 Tick，确保多角色时序同步与确定性
/// </summary>
public class TickManager : MonoBehaviour
{
    public static TickManager Instance { get; private set; }

    public const float TICK_RATE = 1f / 60f; // 60Hz 逻辑 Tick
    private float _accumulator = 0f;

    private GameMain _gameMain;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        _gameMain = FindObjectOfType<GameMain>();
        if (_gameMain == null)
        {
            Debug.LogError("[TickManager] GameMain not found in the scene!");
        }
    }

    void Update()
    {
        if (_gameMain == null) return;

        _accumulator += Time.deltaTime;
        int iterations = 0;

        while (_accumulator >= TICK_RATE)
        {
            _accumulator -= TICK_RATE;
            ExecuteGlobalLogicTick(TICK_RATE);
            iterations++;
            
            if (iterations > 10) // 极端防卡死保护，避免陷入死循环
            {
                _accumulator = 0f;
                break;
            }
        }
    }

    private void ExecuteGlobalLogicTick(float dt)
    {
        // 1. 采集并更新所有实体的输入缓存
        if (_gameMain.player != null && _gameMain.player.input != null)
        {
            _gameMain.player.input.InputTick(dt);
        }
        if (_gameMain.enemy != null)
        {
            foreach (var enemy in _gameMain.enemy)
            {
                if (enemy != null && enemy.input != null)
                {
                    enemy.input.InputTick(dt);
                }
            }
        }

        // 2. 更新所有实体的动作状态机与 Cancel 条件判定
        if (_gameMain.player != null && _gameMain.player.action != null)
        {
            _gameMain.player.action.LogicTick(dt);
        }
        if (_gameMain.enemy != null)
        {
            foreach (var enemy in _gameMain.enemy)
            {
                if (enemy != null && enemy.action != null)
                {
                    enemy.action.LogicTick(dt);
                }
            }
        }

        // 3. 更新所有角色个体的生命周期/命中记录等逻辑
        if (_gameMain.player != null)
        {
            _gameMain.player.CharacterLogicTick(dt);
        }
        if (_gameMain.enemy != null)
        {
            foreach (var enemy in _gameMain.enemy)
            {
                if (enemy != null)
                {
                    enemy.CharacterLogicTick(dt);
                }
            }
        }

        // 4. 执行逻辑位移计算
        _gameMain.TickMovements(dt);

        // 5. 判定全局碰撞检测与伤害/受击分发
        _gameMain.DealWithAttacksInTick();

        // 6. 表现层骨骼更新与动画融合采样
        if (_gameMain.player != null && _gameMain.player.action != null && _gameMain.player.action.playablePlayer != null)
        {
            _gameMain.player.action.playablePlayer.EvaluateAnimation(dt);
        }
        if (_gameMain.enemy != null)
        {
            foreach (var enemy in _gameMain.enemy)
            {
                if (enemy != null && enemy.action != null && enemy.action.playablePlayer != null)
                {
                    enemy.action.playablePlayer.EvaluateAnimation(dt);
                }
            }
        }
    }
}
