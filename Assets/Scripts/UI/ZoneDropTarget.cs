using UnityEngine;

namespace FWTCG.UI
{
    /// <summary>
    /// 挂在可放置区域的按钮上，记录该区域的 zone 字符串（"base"/"0"/"1"）。
    /// EndDrag 时通过 EventSystem.RaycastAll 查找此组件来确定落点。
    /// </summary>
    public class ZoneDropTarget : MonoBehaviour
    {
        public string ZoneId;
    }
}
