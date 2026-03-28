using UnityEngine;

namespace FWTCG.UI
{
    /// <summary>
    /// P33 — 手牌卡片"可打出"旋转光弧。
    /// 挂在 HandCard 子物体 CanPlayGlow GO 上，通过 Update 持续 Z 轴旋转。
    /// Image.Type.Filled + Radial360 + fillAmount ≈ 0.22f = 彗星弧效果。
    /// 随卡片 Refresh 销毁重建，无需手动停止。
    /// </summary>
    public class CanPlayGlow : MonoBehaviour
    {
        private const float DegreesPerSecond = 120f; // 360° / 3s 线性

        private void Update()
        {
            transform.Rotate(0f, 0f, DegreesPerSecond * Time.deltaTime);
        }
    }
}
