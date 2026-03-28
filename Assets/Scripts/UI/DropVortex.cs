using UnityEngine;
using UnityEngine.UI;

namespace FWTCG.UI
{
    /// <summary>
    /// P35 — 拖拽放置区漩涡。
    /// 挂在放置区中央的幽灵 GO 上（父节点为 rootCanvas）；Awake 创建 2 层弧环：
    ///   • 直径 80px，速度 60°/s，alpha 0.15
    ///   • 直径 120px，速度 90°/s，alpha 0.12
    /// 颜色青色（#0ac8b9），比 VortexRings 更快更亮。
    /// 由 BeginCardDrag 创建，EndCardDrag 销毁。
    /// </summary>
    public class DropVortex : MonoBehaviour
    {
        // (直径, 角速度 °/s, fillAmount, alpha)
        private static readonly (float d, float spd, float fill, float alpha)[] RingDefs =
        {
            ( 80f, 60f, 0.55f, 0.15f),
            (120f, 90f, 0.50f, 0.12f),
        };

        private readonly RectTransform[] _rings = new RectTransform[2];

        private void Awake()
        {
            for (int i = 0; i < RingDefs.Length; i++)
            {
                var (d, _, fill, alpha) = RingDefs[i];
                var go  = new GameObject($"DVRing{i}");
                go.transform.SetParent(transform, false);
                var rt  = go.AddComponent<RectTransform>();
                rt.anchorMin        = new Vector2(0.5f, 0.5f);
                rt.anchorMax        = new Vector2(0.5f, 0.5f);
                rt.pivot            = new Vector2(0.5f, 0.5f);
                rt.sizeDelta        = new Vector2(d, d);
                rt.anchoredPosition = Vector2.zero;
                var img = go.AddComponent<Image>();
                img.type            = Image.Type.Filled;
                img.fillMethod      = Image.FillMethod.Radial360;
                img.fillAmount      = fill;
                img.color           = new Color(0.04f, 0.78f, 0.73f, alpha);
                img.raycastTarget   = false;
                _rings[i]           = rt;
            }
        }

        private void Update()
        {
            for (int i = 0; i < _rings.Length; i++)
                if (_rings[i] != null)
                    _rings[i].Rotate(0f, 0f, RingDefs[i].spd * Time.deltaTime);
        }
    }
}
