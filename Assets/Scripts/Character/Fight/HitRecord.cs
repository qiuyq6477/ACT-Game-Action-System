/// <summary>
/// 命中信息记录：一个动作对同一个目标、同一个 phase 还能不能再打一次。
/// 【为什么是 struct】：以前它是 class，每记命中都 `new` 一个塞进 List，于是"打中人"那一拍
/// 带着一次堆分配（切片 1.3 把命中搬进逻辑帧之后 GcProbe 记出来的，见 `Doc/issue/hit-event-heap-alloc.md`）。
/// 现在列表的容量在角色构造时一次给足，元素是结构体，`Add` 与改值都不分配。
/// 【结构体放进 List 的坑】：`HitRecords[i].Tick()` 改的是一份拷贝，编译器不报错、改了等于没改，
/// 所以所有倒数都必须走"取出来 → 改 → 写回去"三步（见 `CharacterObj.TickHitRecords` 与 `AddHitRecord`）。
/// </summary>
public struct HitRecord
{
    /// <summary>
    /// 打中的目标是谁
    /// </summary>
    public int UniqueId;
    /// <summary>
    /// 这是第几段伤害
    /// </summary>
    public int Phase;
    /// <summary>
    /// 还能打中他几次
    /// </summary>
    public int CanHitTimes;
    /// <summary>
    /// 冷却多少个逻辑帧以后才能继续打他
    /// </summary>
    public int CooldownFrames;

    public HitRecord(CharacterObj cha, int phase, int canHitTimes, int cooldownFrames)
    {
        UniqueId = cha.gameObject.GetInstanceID();
        Phase = phase;
        CanHitTimes = canHitTimes;
        CooldownFrames = cooldownFrames;
    }

    public void Tick()
    {
        if (CooldownFrames > 0)
            CooldownFrames--;
    }
}
