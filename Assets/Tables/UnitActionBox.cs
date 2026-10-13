using UnityEngine;

namespace CombatFramework
{
    ///<summary>
    ///动作盒挂点：**只定义一个盒子**（名字 + 形状），不参与逻辑帧、不进快照、不知道自己被哪一招用。
    ///把它挂在角色 prefab 的某根骨骼（或其子节点）下，摆成一个正好包住该部位的盒子；
    ///`Editor/ActionBoxBake.cs` 按动作逐帧读它的位姿、换算成单位局部系，填进**动作资产里已建好的**盒轨道。
    ///
    ///"这个盒在这招里何时算数、打中怎么结算"都在动作资产那条轨道上（Action 时间轴窗口里建轨、拖窗、
    ///配命中规则）；这里没有那些字段——挂点是几何的真相源，时序与规则的真相源是轨道，两份各管各的。
    ///同一 prefab 上可以挂任意多个盒（几只拳头、几块受击部位），同一招里同一个盒也可以被多条轨道引用。
    ///
    ///`trackTag` 是挂点与轨道的对账键（烘培按它找挂点；运行期命中事件把它原样带到宿主，
    ///部位倍率/肉质那些内容规则都在宿主侧查它，框架只负责送）。
    ///</summary>
    public class UnitActionBox : MonoBehaviour{
        [Tooltip("轨道标识（攻击盒建议按挂点骨骼命名，受击盒按部位 Head/Chest/Arm…）。\n"
            + "不叫 tag 是因为 Component.tag 是 GameObject 标签，同名会遮蔽它")]
        public string trackTag = "";

        [Tooltip("三边长（米，沿本节点的局部轴）。节点自身的缩放会在烘培时乘进半边长（三个基向量保持单位长），"
            + "所以想整体放大就调节点的 scale，想改单边就改这里")]
        public Vector3 size = new Vector3(0.3f, 0.3f, 0.3f);

        [Tooltip("盒心相对本节点的偏移（米，沿本节点的局部轴）。受击盒挂在根节点下时靠它把盒顶到腰胸高度"
            + "（等价于原来那颗 m_Center {y:1} 的胶囊），不填烘出来会贴地")]
        public Vector3 offset = Vector3.zero;

        private void OnDrawGizmosSelected(){
            Gizmos.matrix = transform.localToWorldMatrix * Matrix4x4.Translate(offset);
            Gizmos.color = new Color(1f, 0.55f, 0.25f, 0.9f);
            Gizmos.DrawWireCube(Vector3.zero, size);
            Gizmos.color = new Color(1f, 0.55f, 0.25f, 0.18f);
            Gizmos.DrawCube(Vector3.zero, size);
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
