using UnityEngine;

namespace CombatFramework
{
    ///<summary>
    ///定向包围盒（OBB）：中心 + 三个正交基向量 + 三半边长。
    ///基向量在编辑期就烘成浮点（骨骼旋转不参与运行期计算），取用时只做点积、绝对值与比较，
    ///零归一化、零开方。刻意用"三基向量 + 三半长"而不是矩阵/四元数：烘出来就是最终形状，
    ///不存在"再归一化一次"的口径漂移。
    ///
    ///约定（烘培侧必须保证）：
    ///①axisX/axisY/axisZ 两两正交且为单位长——`ToWorldObb` 直接按它们旋盒、线框按 axis × half 拉角，
    ///  不等长时线框会虚胖，投影半宽也就不再等于 half；
    ///②半边长非负；负半长是配表事故，交给构建期校验报，这里不做钳制。
    ///</summary>
    public readonly struct Obb
    {
        public readonly Vector3 center;
        public readonly Vector3 axisX;
        public readonly Vector3 axisY;
        public readonly Vector3 axisZ;
        public readonly float halfX;
        public readonly float halfY;
        public readonly float halfZ;

        public Obb(Vector3 center, Vector3 axisX, Vector3 axisY, Vector3 axisZ,
                   float halfX, float halfY, float halfZ){
            this.center = center;
            this.axisX = axisX;
            this.axisY = axisY;
            this.axisZ = axisZ;
            this.halfX = halfX;
            this.halfY = halfY;
            this.halfZ = halfZ;
        }

        ///<summary>未使用：轴对齐盒（世界轴基 + 三半边长），给不随动画转的盒和测试构造用。
        ///<param name="center">盒中心（逻辑位，含 y）</param>
        ///<param name="half">三半边长（米）</param>
        ///</summary>
        public static Obb AxisAligned(Vector3 center, Vector3 half){
            return new Obb(center, Vector3.right, Vector3.up, Vector3.forward, half.x, half.y, half.z);
        }

        ///<summary>未使用：沿分离轴 L 的投影半宽 = |axisX·L|·halfX + |axisY·L|·halfY + |axisZ·L|·halfZ。
        ///L 不必是单位向量：两侧半宽与 L 长度同比放大，分离判据 |d·L| &gt; ra + rb 两边同次缩放，结论不变。
        ///L 为零向量时半宽为 0，判据退化成 0 &gt; 0 = 假（无需 epsilon）。画线框用不到它，相交测试才用。</summary>
        public float RadiusOn(Vector3 L){
            return Mathf.Abs(Vector3.Dot(axisX, L)) * halfX
                 + Mathf.Abs(Vector3.Dot(axisY, L)) * halfY
                 + Mathf.Abs(Vector3.Dot(axisZ, L)) * halfZ;
        }

        public override string ToString(){
            return "Obb(" + center + ", half=" + halfX + "/" + halfY + "/" + halfZ + ")";
        }
    }
}
