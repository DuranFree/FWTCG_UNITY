using UnityEngine;
using UnityEngine.UI;

namespace FWTCG.UI
{
    /// <summary>
    /// P38 — uGUI Canvas 粒子系统（40 粒子：20 主粒子 + 10 萤火虫 + 10 符文字形）。
    /// 挂在 Background 面板上；Awake 预分配全部 GO，Update 每帧更新位置 + alpha。
    ///
    /// 粒子物理（P38）：
    ///   • 重力：-8 px/s²（极弱，粒子保持漂浮感）
    ///   • 风力：6 px/s 正弦波（周期约 12.6s）
    ///   • 边界：主粒子超出底/顶边界时循环重置；萤火虫/符文字形边界弹射（速度衰减 0.6）
    ///
    /// 星座连线（uGUI 需继承 MaskableGraphic）列入 tech-debt，本 Phase 跳过。
    /// </summary>
    public class CanvasParticles : MonoBehaviour
    {
        // ── 粒子数量 ──────────────────────────────────
        private const int MainCount    = 20;
        private const int FireflyCount = 10;
        private const int RuneCount    = 10;
        private const int Total        = MainCount + FireflyCount + RuneCount;

        // ── Canvas 半尺寸（CanvasScaler 参考分辨率 1280×720）──
        private const float HalfW = 640f;
        private const float HalfH = 360f;

        // ── 粒子物理常量 ──────────────────────────────
        private const float Gravity = -8f;   // px/s²
        private const float WindAmp =  6f;   // px/s（正弦幅值）
        private const float WindFreq = 0.5f; // 正弦角频率（rad/s）

        // ── 符文字符池 ─────────────────────────────────
        private static readonly string[] RuneEmojis =
            { "🔥", "✨", "🌿", "💥", "🌀", "⚜️", "🔥", "✨", "🌿", "💥" };

        // ── 粒子类型常量 ──────────────────────────────
        private const int KindMain    = 0;
        private const int KindFirefly = 1;
        private const int KindRune    = 2;

        // ── 并行数组（避免对象开销）──────────────────
        private readonly RectTransform[] _rts       = new RectTransform[Total];
        private readonly Graphic[]       _gfx       = new Graphic[Total];
        private readonly Vector2[]       _vel       = new Vector2[Total];
        private readonly float[]         _phase     = new float[Total];
        private readonly float[]         _period    = new float[Total];
        private readonly float[]         _alphaBase = new float[Total];
        private readonly float[]         _alphaAmp  = new float[Total];
        private readonly int[]           _kind      = new int[Total];
        private readonly Color[]         _colBase   = new Color[Total];

        private float         _time;
        private System.Random _rng;

        // ── 主题颜色 ──────────────────────────────────
        private static readonly Color C_White   = new Color(1f,    1f,    1f);
        private static readonly Color C_Cyan    = new Color(0.04f, 0.78f, 0.73f);
        private static readonly Color C_Gold    = new Color(0.78f, 0.67f, 0.43f);
        private static readonly Color C_Firefly = new Color(0.62f, 0.92f, 0.35f);

        // ─────────────────────────────────────────────
        // 初始化
        // ─────────────────────────────────────────────

        private void Awake()
        {
            _rng = new System.Random();

            int idx = 0;

            // 主粒子：4-6px 白/青/金点，匀速向上漂移 + 轻重力 + 风力
            Color[] mainPalette = { C_White, C_Cyan, C_Gold };
            for (int i = 0; i < MainCount; i++, idx++)
            {
                InitImage(idx, $"CPMain{i}", 5f, mainPalette[i % 3]);
                _kind[idx]      = KindMain;
                _vel[idx]       = new Vector2(RandF(-5f, 5f), RandF(20f, 50f));
                _alphaBase[idx] = 0.07f;
                _alphaAmp[idx]  = 0.05f;
                _period[idx]    = RandF(1.5f, 3.5f);
            }

            // 萤火虫：6×6px 黄绿色，Sin 曲线漂移 + 弱重力 + alpha 脉冲
            for (int i = 0; i < FireflyCount; i++, idx++)
            {
                InitImage(idx, $"CPFirefly{i}", 6f, C_Firefly);
                _kind[idx]      = KindFirefly;
                _vel[idx]       = new Vector2(RandF(-8f, 8f), RandF(-5f, 5f));
                _alphaBase[idx] = 0.08f;
                _alphaAmp[idx]  = 0.07f;
                _period[idx]    = RandF(1.2f, 2.2f);
            }

            // 符文字形：Text 11px，极慢漂移 + 仅风力 + 低 alpha
            for (int i = 0; i < RuneCount; i++, idx++)
            {
                InitRune(idx, i);
                _kind[idx]      = KindRune;
                _vel[idx]       = new Vector2(RandF(-6f, 6f), RandF(-4f, 4f));
                _alphaBase[idx] = 0.04f;
                _alphaAmp[idx]  = 0.03f;
                _period[idx]    = RandF(2.5f, 5f);
            }
        }

        // ─────────────────────────────────────────────
        // 每帧更新
        // ─────────────────────────────────────────────

        private void Update()
        {
            _time += Time.deltaTime;
            float wind = WindAmp * Mathf.Sin(_time * WindFreq);
            float dt   = Time.deltaTime;

            for (int i = 0; i < Total; i++)
            {
                if (_rts[i] == null) continue;

                // ── 粒子物理 ──
                switch (_kind[i])
                {
                    case KindMain:
                        _vel[i].y += Gravity * dt;
                        _vel[i].x += wind * dt * 0.4f;
                        break;
                    case KindFirefly:
                        _vel[i].y += Gravity * 0.3f * dt;
                        _vel[i].x += wind * dt * 0.2f;
                        // 正弦横向摆动
                        _vel[i].x += Mathf.Sin(_time * 1.2f + _phase[i]) * 3f * dt;
                        break;
                    case KindRune:
                        // 仅风力（极轻）
                        _vel[i].x += wind * dt * 0.15f;
                        break;
                }

                // 速度上限
                float maxSpd = _kind[i] == KindRune ? 15f : 65f;
                float spd    = _vel[i].magnitude;
                if (spd > maxSpd) _vel[i] *= maxSpd / spd;

                // ── 位移 ──
                Vector2 pos = _rts[i].anchoredPosition + _vel[i] * dt;

                // ── 边界处理 ──
                if (_kind[i] == KindMain)
                {
                    // 超出底部 → 循环到顶
                    if (pos.y < -HalfH - 30f)
                    {
                        pos = new Vector2(RandF(-HalfW, HalfW), HalfH + 10f);
                        _vel[i].y = RandF(20f, 50f);
                    }
                    // 超出顶部 → 循环到底
                    if (pos.y > HalfH + 30f)
                        pos = new Vector2(RandF(-HalfW, HalfW), -HalfH - 10f);
                    // 横向宽松反弹
                    if (pos.x < -HalfW - 20f || pos.x > HalfW + 20f)
                    {
                        _vel[i].x *= -0.6f;
                        pos.x      = Mathf.Clamp(pos.x, -HalfW, HalfW);
                    }
                }
                else
                {
                    // 萤火虫 / 符文：硬边界弹射
                    if (pos.x < -HalfW || pos.x > HalfW)
                    {
                        _vel[i].x *= -0.6f;
                        pos.x      = Mathf.Clamp(pos.x, -HalfW, HalfW);
                    }
                    if (pos.y < -HalfH || pos.y > HalfH)
                    {
                        _vel[i].y *= -0.6f;
                        pos.y      = Mathf.Clamp(pos.y, -HalfH, HalfH);
                    }
                }

                _rts[i].anchoredPosition = pos;

                // ── Alpha 脉冲 ──
                float a = _alphaBase[i]
                        + _alphaAmp[i] * (Mathf.Sin(_time / _period[i] + _phase[i]) * 0.5f + 0.5f);
                Color c = _colBase[i];
                _gfx[i].color = new Color(c.r, c.g, c.b, a);
            }
        }

        // ─────────────────────────────────────────────
        // 工厂辅助
        // ─────────────────────────────────────────────

        private void InitImage(int i, string goName, float size, Color col)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = Vector2.one * 0.5f;
            rt.pivot     = Vector2.one * 0.5f;
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = RandPos();
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;

            _rts[i]      = rt;
            _gfx[i]      = img;
            _colBase[i]  = col;
            _phase[i]    = RandF(0f, Mathf.PI * 2f);
            img.color    = new Color(col.r, col.g, col.b, _alphaBase[i]);
        }

        private void InitRune(int i, int runeIdx)
        {
            var go = new GameObject($"CPRune{runeIdx}");
            go.transform.SetParent(transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = Vector2.one * 0.5f;
            rt.pivot     = Vector2.one * 0.5f;
            rt.sizeDelta = new Vector2(18f, 18f);
            rt.anchoredPosition = RandPos();
            var txt = go.AddComponent<Text>();
            txt.text          = RuneEmojis[runeIdx];
            txt.fontSize      = 11;
            txt.alignment     = TextAnchor.MiddleCenter;
            txt.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.raycastTarget = false;

            _rts[i]     = rt;
            _gfx[i]     = txt;
            _colBase[i] = Color.white;
            _phase[i]   = RandF(0f, Mathf.PI * 2f);
            txt.color   = new Color(1f, 1f, 1f, 0.04f);
        }

        private Vector2 RandPos()
            => new Vector2(RandF(-HalfW, HalfW), RandF(-HalfH, HalfH));

        private float RandF(float min, float max)
            => (float)(_rng.NextDouble() * (max - min) + min);
    }
}
