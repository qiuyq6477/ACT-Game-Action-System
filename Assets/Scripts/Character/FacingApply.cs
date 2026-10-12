using UnityEngine;

/// <summary>
/// 角色面向在表现层怎么落地。
///
/// 逻辑层的真源只有 `InputToCommand.inversed` 这一个布尔——输入镜像（GetKeyInput）、
/// 位移符号（NatureMove）、受力方向翻转（GetForceDirection）三处全都只用它。
/// 而"朝左朝右具体变成什么变换"属于渲染后端，原来这一行写在 ActionController 里，
/// 等于是把 3D 模型的假设钉死在了逻辑层：
/// 3D 模型绕 Y 转 90°/270° 就是朝右/朝左，可一张面向镜头的精灵四边形绕 Y 转 90° 会侧对镜头变成一条线，
/// 只能靠 x 轴取反来镜像。所以把它挪到这里，由每个角色的 prefab 选一种落地方式。
/// </summary>
public class FacingApply : MonoBehaviour
{
    public enum Backend
    {
        /// <summary>
        /// 3D 骨骼模型：朝右 = 绕 Y 转 90°，朝左 = 270°（本工程改造前的唯一做法）
        /// </summary>
        ModelYaw,
        /// <summary>
        /// 贴在 3D 平面上的像素精灵：靠 localScale.x 取反镜像。
        /// 翻 x 而不是用 SpriteRenderer.flipX，是因为判定盒、武器挂点都是子节点——
        /// flipX 只翻贴图不翻子节点，出招的盒子会留在错误的一侧
        /// </summary>
        SpriteScaleX
    }

    [Tooltip("这个角色的面向怎么落地。3D 模型选 ModelYaw，精灵选 SpriteScaleX")]
    public Backend backend = Backend.ModelYaw;

    /// <summary>
    /// 把逻辑的面向写进 Transform。只在换动作那一拍被调用（跟改造前的时机逐字相同），
    /// 挂在哪个节点就翻哪个节点连同它的子树，所以要翻的部分请放在同一个父节点下
    /// </summary>
    /// <param name="inversed">true = 朝左（屏幕空间的反手）</param>
    public void Apply(bool inversed)
    {
        if (backend == Backend.ModelYaw)
        {
            transform.eulerAngles = new Vector3(0, inversed ? 270f : 90f, 0);
            return;
        }

        //保留原有缩放的大小，只定 x 的符号：美术给精灵节点做过整体缩放时不能把它抹平
        Vector3 scale = transform.localScale;
        scale.x = inversed ? -Mathf.Abs(scale.x) : Mathf.Abs(scale.x);
        transform.localScale = scale;
    }
}
