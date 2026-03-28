using UnityEngine;
using UnityEngine.UI;

namespace FWTCG.UI
{
    /// <summary>
    /// P34 — 背景漩涡旋转光环。
    /// 挂在 Background 面板上；Awake 创建：
    ///   • 3 个弧形 Image（直径 400/600/800px，转速 8/10/12s 一圈，青/金交替）
    ///   • 6 个符文 emoji Text 沿 310px 轨道公转（18°/s）
    /// 所有 alpha 极低（0.04-0.06），作为全屏环境装饰。
    /// </summary>
    public class VortexRings : MonoBehaviour
    {
        // (直径, 角速度 °/s, fillAmount)
        private static readonly (float d, float spd, float fill)[] RingDefs =
        {
            (400f, 45f, 0.60f),   // 360/8  = 45°/s
            (600f, 36f, 0.55f),   // 360/10 = 36°/s
            (800f, 30f, 0.50f),   // 360/12 = 30°/s
        };

        private static readonly string[] RuneEmojis =
            { "🔥", "✨", "🌿", "💥", "🌀", "⚜️" };

        private readonly RectTransform[] _rings    = new RectTransform[3];
        private readonly RectTransform[] _orbitals = new RectTransform[6];
        private float _orbitalAngle;

        private const float OrbitRadius  = 310f;
        private const float OrbitalSpeed = 18f;  // °/s

        private void Awake()
        {
            // ── 3 层旋转弧环 ──
            for (int i = 0; i < RingDefs.Length; i++)
            {
                var (d, _, fill) = RingDefs[i];
                var go  = new GameObject($"VRing{i}");
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
                img.color           = i % 2 == 0
                    ? new Color(0.04f, 0.78f, 0.73f, 0.05f)  // 青色
                    : new Color(0.78f, 0.67f, 0.43f, 0.04f); // 金色
                img.raycastTarget   = false;
                _rings[i]           = rt;
            }

            // ── 6 个轨道符文 ──
            for (int i = 0; i < RuneEmojis.Length; i++)
            {
                var go  = new GameObject($"OrbRune{i}");
                go.transform.SetParent(transform, false);
                var rt  = go.AddComponent<RectTransform>();
                rt.anchorMin        = new Vector2(0.5f, 0.5f);
                rt.anchorMax        = new Vector2(0.5f, 0.5f);
                rt.pivot            = new Vector2(0.5f, 0.5f);
                rt.sizeDelta        = new Vector2(22f, 22f);
                var txt = go.AddComponent<Text>();
                txt.text            = RuneEmojis[i];
                txt.font            = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                txt.fontSize        = 11;
                txt.alignment       = TextAnchor.MiddleCenter;
                txt.color           = new Color(1f, 1f, 1f, 0.06f);
                txt.raycastTarget   = false;
                _orbitals[i]        = rt;
            }

            PlaceOrbitals();
        }

        private void Update()
        {
            for (int i = 0; i < _rings.Length; i++)
                if (_rings[i] != null)
                    _rings[i].Rotate(0f, 0f, RingDefs[i].spd * Time.deltaTime);

            _orbitalAngle = (_orbitalAngle + OrbitalSpeed * Time.deltaTime) % 360f;
            PlaceOrbitals();
        }

        private void PlaceOrbitals()
        {
            int n = _orbitals.Length;
            for (int i = 0; i < n; i++)
            {
                if (_orbitals[i] == null) continue;
                float a = (_orbitalAngle + i * (360f / n)) * Mathf.Deg2Rad;
                _orbitals[i].anchoredPosition = new Vector2(
                    OrbitRadius * Mathf.Cos(a),
                    OrbitRadius * Mathf.Sin(a));
            }
        }
    }
}
