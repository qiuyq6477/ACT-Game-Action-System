using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CombatFrameworkEditor
{
    ///<summary>
    ///窗口用的下拉绘制公共件，只有一个方法：`IdDropdown`——词表由调用方给：`DrawHeader` 的输入序列用
    ///`ActionKeyBits.Names`，取消 tag 用全库已用的 tag，受击轨点亮的临时窗用全库的模板 id。
    ///窗口里填的都是纯 ID 字符串，没有按 schema 驱动的参数表单那一层。
    ///</summary>
    public static class CombatAssetGUI
    {
        private const string UnregisteredSuffix = "  (unregistered)";

        ///<summary>回调ID下拉：选项=注册表内已注册ID（含当前未注册值保留），空=留空</summary>
        public static void IdDropdown(SerializedProperty idProp, string label, IEnumerable<string> registeredIds){
            string cur = idProp.stringValue;
            List<string> options = new List<string>{ "" };
            options.AddRange(registeredIds.Where(x => !string.IsNullOrEmpty(x)).OrderBy(x => x));
            if (!string.IsNullOrEmpty(cur) && !options.Contains(cur)) options.Add(cur + UnregisteredSuffix);
            string curOption = string.IsNullOrEmpty(cur) ? "" : (options.Contains(cur) ? cur : cur + UnregisteredSuffix);
            int idx = Mathf.Max(0, options.IndexOf(curOption));
            string[] display = options.Select(o =>
                string.IsNullOrEmpty(o) ? "(none)" : o).ToArray();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(label);
            int newIdx = EditorGUILayout.Popup(idx, display);
            EditorGUILayout.EndHorizontal();
            if (newIdx != idx){
                string v = options[newIdx];
                idProp.stringValue = v.EndsWith(UnregisteredSuffix) ? v.Substring(0, v.Length - UnregisteredSuffix.Length) : v;
            }
        }
    }
}
