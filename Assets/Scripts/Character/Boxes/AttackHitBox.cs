using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 攻击盒
/// 正确的做法：每个动作帧每个角色可能都会有若干攻击盒。
/// 但是受限于Unity和UE之类的对于游戏的理解非常外行，所以给的框架不能很好的实现动作游戏
/// 于是我们只能凑效果，就是在角色身上先绑定了攻击盒，然后在合适的时候开启或者关闭他们
/// TODO: 攻击盒与动画解耦
/// </summary>
public class AttackHitBox : MonoBehaviour
{
    /// <summary>
    /// 标签，可以被当做一种分类方式
    /// 比如我们开启一些攻击盒的时候可以byTag，只要tags里面有指定的tag就算是符合条件了。
    /// </summary>
    [Tooltip("盒子的tag")] public string[] tags;

    /// <summary>
    /// 这个框的主人
    /// </summary>
    [HideInInspector] public CharacterObj master;

    /// <summary>
    /// 伤害倍率，这里的伤害倍率x动作的伤害倍率x角色的攻击力=伤害力，可以这么设计的，标准动作游戏里很常见。
    /// 【关键1】
    /// 伤害倍率本身只是一个例子，大多游戏其实是不做的，早期的MH有大剑头部、身体和剑柄伤害有差异
    /// 靠的就是不同的伤害倍率，后来觉得没意义就干掉了。
    /// 【关键2】
    /// 除了伤害倍率之外，还可以有很多其他属性，根据游戏的具体需要去添加，这里只是一个范例。
    /// 比如我们有些攻击某些部位打中人才能令目标着火，着火比如是因为武器附魔了，那么那几个攻击盒算是“武器”呢？
    /// 就可以在这里追加属性去实现，比如bool isEnchantment代表是否是一个附魔有效的攻击盒
    /// </summary>
    [Tooltip("伤害倍率"), Range(0, float.MaxValue)]public float damageRate = 1;

    /// <summary>
    /// 当同一帧有多个攻击盒命中了同一个角色的受击盒时，我们算是命中了，这点没错
    /// 但是有一个问题，到底应该算哪个攻击盒打中了对手呢？总得有个结论的，所以用Priority来进行冒泡
    /// </summary>
    [Tooltip("基础的优先级")] public int basePriority;
    /// <summary>
    /// 临时的优先级，由当前开启自己的那个 AttackBoxTurnOnInfo.priority 给出，每帧重算
    /// </summary>
    public int TempPriority { get; private set; }
    /// <summary>
    /// 攻击盒当前的优先级
    /// </summary>
    public int Priority => TempPriority + basePriority;
    
    /// <summary>
    /// 当前是否开启了
    /// </summary>
    public bool Active { get; private set; }

    /// <summary>
    /// tags烘焙出来的位掩码，开关和临时优先级都用它判定，不再比对字符串
    /// </summary>
    public int TagMask { get; private set; }

    /// <summary>
    /// 把Inspector上填的tags翻译成位掩码。
    /// 由CharacterObj.BindBoxes顺带调用（那里本来就要遍历子组件认主人），
    /// 一定要在任何Update之前完成，否则盒子会以为自己的标签谁都不是
    /// </summary>
    public void BakeTagMask()
    {
        TagMask = BoxTagRegistry.MaskOf(tags);
    }

    /// <summary>
    /// 自己那个碰撞体，由 `CharacterObj.BindBoxes` 缓存（那里本来就要遍历子组件认主人）。
    /// 采样要拿它做形状查询，每帧GetComponent一次既慢又可能分配
    /// </summary>
    private Collider _collider;

    /// <summary>
    /// 缓冲区满过一次就只喊一声：溢出说明场上同层受击盒比容量还多，
    /// 但每拍都刷一行日志会把真正的问题淹掉
    /// </summary>
    private static bool _bufferOverflowLogged;

    public void BindCollider(Collider col)
    {
        _collider = col;
    }

    /// <summary>
    /// 刷新一个逻辑帧的开启状态，由GameMain的时钟在ActionController之后、命中结算之前调用
    /// </summary>
    public void Tick()
    {
        Active = master && master.ShouldAttackBoxActive(TagMask);
        TempPriority = master ? master.GetAttackBoxTempPriority(TagMask) : 0;
    }

    /// <summary>
    /// 拿本盒【当前这一逻辑帧】的位置和形状，主动去物理世界查一遍受击盒，查到的交给主人记账。
    /// 【为什么不再是OnTriggerEnter】：物理回调的频率是物理步（本工程Fixed Timestep 0.02，即50Hz），
    /// 而盒子开在哪几拍是60Hz的逻辑帧说了算的，两者互相滑移——只开一两拍的窄窗口可能静默打空，
    /// 反过来也可能在盒子已经关掉的当帧多报一次；位移一大（击退、冲刺）盒子还会"穿过"而不是"重叠过"目标。
    /// 换成主动采样之后，"这一拳打到谁"和"盒子开在哪几拍"用的是同一个整型帧时钟，跨帧率完全确定
    /// </summary>
    /// <param name="buffer">GameMain预分配的常驻缓冲区，绝不扩容绝不new</param>
    public void SampleTouches(Collider[] buffer)
    {
        //关着的盒子不查：这就是"窄窗口"能被精确控制的原理
        if (!Active || !master || !_collider) return;

        int found = Overlap(buffer);
        for (int i = 0; i < found; i++)
        {
            //查询返回的是Collider，要拿回受击盒只能查BeHitBox那张表（GetComponent的分配行为随版本变）
            if (BeHitBox.TryGet(buffer[i], out BeHitBox bhb) && bhb.master != master)
                master.OnAttackBoxHit(this, bhb);
        }

        //NonAlloc在装满时返回的就是容量，多出来的相交被无声丢掉，所以必须喊一声
        if (found == buffer.Length && !_bufferOverflowLogged)
        {
            _bufferOverflowLogged = true;
            Debug.LogError("命中采样缓冲区溢出（容量" + buffer.Length +
                           "）：这一拍可能有受击盒被漏掉，请调大GameMain._boxBuffer，注意它是一次性分配的常驻内存");
        }
    }

    /// <summary>
    /// 按形状分派一次overlap查询，返回写进buffer的条数。
    /// 三种形状是工程里用得上的全部：受击盒是Capsule，BeatEmUp的精灵盒子导出来是Box，
    /// 本工程的攻击盒是Sphere。查不到形状就是配表事故，返回0而不是抛异常——
    /// 一个盒子的形状写错不该让整局跑不下去
    /// </summary>
    private int Overlap(Collider[] buffer)
    {
        Transform t = _collider.transform;
        Vector3 scale = AbsLossyScale(t);

        SphereCollider sphere = _collider as SphereCollider;
        if (sphere)
            return Physics.OverlapSphereNonAlloc(t.TransformPoint(sphere.center),
                sphere.radius * Mathf.Max(scale.x, scale.y, scale.z), buffer, BeHitBox.QueryMask);

        BoxCollider box = _collider as BoxCollider;
        if (box)
            return Physics.OverlapBoxNonAlloc(t.TransformPoint(box.center),
                Vector3.Scale(box.size * 0.5f, scale), buffer, t.rotation, BeHitBox.QueryMask);

        CapsuleCollider capsule = _collider as CapsuleCollider;
        if (capsule)
        {
            //胶囊的半径归另外两根轴、高度归direction那根轴，这是它唯一的几何拆法
            int axis = capsule.direction;
            float along = scale[axis];
            float across = Mathf.Max(scale[(axis + 1) % 3], scale[(axis + 2) % 3]);
            float radius = capsule.radius * across;
            //PhysX要的是两端球心，而端头那两个半球各占一个radius，所以直线段是"高度减两个半径"
            float halfSpan = Mathf.Max(0f, capsule.height * along * 0.5f - radius);
            Vector3 center = t.TransformPoint(capsule.center);
            Vector3 span = t.TransformVector(AxisLocal(axis) * halfSpan);
            return Physics.OverlapCapsuleNonAlloc(center - span, center + span, radius, buffer, BeHitBox.QueryMask);
        }

        return 0;
    }

    /// <summary>
    /// 各轴取绝对值：FacingApply的SpriteScaleX后端把localScale.x翻成负数来镜像精灵，
    /// 而物理形状只认大小不认朝向，负号必须抹掉，否则查出来的盒子会翻到角色背后去
    /// </summary>
    private static Vector3 AbsLossyScale(Transform t)
    {
        Vector3 s = t.lossyScale;
        return new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
    }

    private static Vector3 AxisLocal(int axis)
    {
        return axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
    }
}
