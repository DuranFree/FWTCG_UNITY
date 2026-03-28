using UnityEngine;

namespace FWTCG.UI
{
    /// <summary>
    /// 挂在 SafeArea RectTransform 上，在 Start 时将其锚点调整为 Screen.safeArea，
    /// 使所有子元素自动避开刘海/圆角/底部条等系统遮挡区域。
    /// 必须是 Canvas 的直接子节点（Canvas Space Overlay）。
    /// </summary>
    public class SafeAreaFitter : MonoBehaviour
    {
        private void Start()
        {
            Apply();
        }

        private void Apply()
        {
            var rt         = GetComponent<RectTransform>();
            var safeArea   = Screen.safeArea;
            var screenSize = new Vector2(Screen.width, Screen.height);

            // 防止在编辑器或初始化时 Screen.width/height 为 0
            if (screenSize.x <= 0 || screenSize.y <= 0) return;

            rt.anchorMin = safeArea.position / screenSize;
            rt.anchorMax = (safeArea.position + safeArea.size) / screenSize;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
