using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace FWTCG.UI
{
    /// <summary>
    /// 轻量 Coroutine 补间工具 — 替代 DOTween 基础功能。
    /// 所有方法均为静态，由外部 MonoBehaviour 启动协程。
    ///
    /// 使用示例：
    ///   StartCoroutine(UITween.FadeIn(canvasGroup, 0.4f));
    ///   StartCoroutine(UITween.MoveY(rt, 60f, 0.6f, Ease.OutQuad));
    /// </summary>
    public static class UITween
    {
        // ─────────────────────────────────────────────
        // 缓动函数
        // ─────────────────────────────────────────────

        public enum Ease { Linear, OutQuad, InQuad, OutBack, InOutQuad }

        /// <summary>供外部直接调用缓动函数（ToastSystem 等）。</summary>
        public static float ApplyEasePublic(float t, Ease ease) => ApplyEase(t, ease);

        private static float ApplyEase(float t, Ease ease) => ease switch
        {
            Ease.OutQuad   => 1f - (1f - t) * (1f - t),
            Ease.InQuad    => t * t,
            Ease.OutBack   => 1f + 2.70158f * Mathf.Pow(t - 1f, 3f) + 1.70158f * Mathf.Pow(t - 1f, 2f),
            Ease.InOutQuad => t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f,
            _              => t,
        };

        // ─────────────────────────────────────────────
        // Canvas Group Fade
        // ─────────────────────────────────────────────

        public static IEnumerator FadeIn(CanvasGroup cg, float duration, Ease ease = Ease.OutQuad)
        {
            cg.alpha = 0f;
            yield return Lerp01(duration, ease, t => cg.alpha = t);
        }

        public static IEnumerator FadeOut(CanvasGroup cg, float duration, Ease ease = Ease.OutQuad,
            Action onComplete = null)
        {
            float start = cg.alpha;
            yield return Lerp01(duration, ease, t => cg.alpha = Mathf.Lerp(start, 0f, t));
            onComplete?.Invoke();
        }

        // ─────────────────────────────────────────────
        // Image Color
        // ─────────────────────────────────────────────

        public static IEnumerator TintTo(Image img, Color target, float duration, Ease ease = Ease.OutQuad)
        {
            Color start = img.color;
            yield return Lerp01(duration, ease, t => img.color = Color.Lerp(start, target, t));
        }

        public static IEnumerator PulseColor(Image img, Color flash, float duration)
        {
            Color orig = img.color;
            yield return TintTo(img, flash, duration * 0.3f, Ease.OutQuad);
            yield return TintTo(img, orig,  duration * 0.7f, Ease.OutQuad);
        }

        /// <summary>Text 版积分脉冲：快速闪入目标色，再缓慢回原色。</summary>
        public static IEnumerator PulseColor(Text txt, Color flash, float duration)
        {
            Color orig = txt.color;
            float elapsed = 0f;
            // 闪入
            float inDur = duration * 0.3f;
            while (elapsed < inDur)
            {
                elapsed += Time.deltaTime;
                txt.color = Color.Lerp(orig, flash, ApplyEase(Mathf.Clamp01(elapsed / inDur), Ease.OutQuad));
                yield return null;
            }
            txt.color = flash;
            elapsed = 0f;
            // 淡回
            float outDur = duration * 0.7f;
            while (elapsed < outDur)
            {
                elapsed += Time.deltaTime;
                txt.color = Color.Lerp(flash, orig, ApplyEase(Mathf.Clamp01(elapsed / outDur), Ease.OutQuad));
                yield return null;
            }
            txt.color = orig;
        }

        // ─────────────────────────────────────────────
        // RectTransform Move
        // ─────────────────────────────────────────────

        /// <summary>从当前位置向上偏移 deltaY（世界空间 Y），动画时长 duration。</summary>
        public static IEnumerator MoveY(RectTransform rt, float deltaY, float duration,
            Ease ease = Ease.OutQuad)
        {
            Vector2 start = rt.anchoredPosition;
            Vector2 end   = start + new Vector2(0, deltaY);
            yield return Lerp01(duration, ease, t => rt.anchoredPosition = Vector2.Lerp(start, end, t));
        }

        /// <summary>从 startY 到 endY 的绝对 anchoredPosition.y。</summary>
        public static IEnumerator MoveToY(RectTransform rt, float targetY, float duration,
            Ease ease = Ease.OutQuad)
        {
            Vector2 start = rt.anchoredPosition;
            Vector2 end   = new Vector2(start.x, targetY);
            yield return Lerp01(duration, ease, t => rt.anchoredPosition = Vector2.Lerp(start, end, t));
        }

        // ─────────────────────────────────────────────
        // Scale
        // ─────────────────────────────────────────────

        public static IEnumerator ScaleTo(RectTransform rt, Vector3 target, float duration,
            Ease ease = Ease.OutBack)
        {
            Vector3 start = rt.localScale;
            yield return Lerp01(duration, ease, t => rt.localScale = Vector3.Lerp(start, target, t));
        }

        public static IEnumerator PopIn(RectTransform rt, float duration = 0.35f)
        {
            rt.localScale = Vector3.zero;
            yield return ScaleTo(rt, Vector3.one, duration, Ease.OutBack);
        }

        // ─────────────────────────────────────────────
        // Shake（全屏震动）
        // ─────────────────────────────────────────────

        public static IEnumerator Shake(RectTransform rt, float strength, float duration,
            int vibrato = 10)
        {
            Vector2 orig = rt.anchoredPosition;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                float decay = 1f - (elapsed / duration);
                Vector2 offset = UnityEngine.Random.insideUnitCircle * strength * decay;
                rt.anchoredPosition = orig + offset;
                elapsed += Time.deltaTime * vibrato;
                yield return null;
            }
            rt.anchoredPosition = orig;
        }

        // ─────────────────────────────────────────────
        // 内部工具
        // ─────────────────────────────────────────────

        private static IEnumerator Lerp01(float duration, Ease ease, Action<float> setter)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                setter(ApplyEase(Mathf.Clamp01(elapsed / duration), ease));
                yield return null;
            }
            setter(1f);
        }
    }
}
