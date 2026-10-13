using UnityEngine;

namespace CombatFramework
{
    ///<summary>
    ///动作表容器：**数组顺序本身就是数据**——抢动作平手时取靠前的一条（`ActionController` 用严格 `>`），
    ///所以这个数组的顺序必须与原来 `Action.json` 的行序逐位一致，重排等于改手感。
    ///
    ///它必须是与 `ActionAsset` 不同的类型：`ActionEditorWindow.RefreshLibrary` 用
    ///`AssetDatabase.FindAssets("t:ActionAsset")` 建"全库"（取消关系要看到别的动作才判得了），
    ///容器同型就会把自己也当成一条动作收进库里。
    ///</summary>
    [CreateAssetMenu(fileName = "ActionTable", menuName = "Combat Framework/Action Table", order = 28)]
    public class ActionTableAsset : ScriptableObject
    {
        [Tooltip("整张动作表。顺序=平手 tie-break，新增动作一律追加末尾")]
        public ActionAsset[] actions = new ActionAsset[0];
    }
}
