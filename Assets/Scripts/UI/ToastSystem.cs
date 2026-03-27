using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FWTCG.UI
{
    /// <summary>
    /// 浮动 Toast 通知系统。
    /// 单例 MonoBehaviour；调用 ToastSystem.Instance.Show("消息") 显示通知。
    /// 每条 Toast 从屏幕下方滑入，停留后淡出，多条 Toast 自动垂直堆叠。
    ///
    /// 自建 Canvas 方案：Awake 时检查是否已有 ToastSystem Canvas，不重复创建。
    /// </summary>
    public class ToastSystem : MonoBehaviour
    {
        public static ToastSystem Instance { get; private set; }

        private Transform         _container;     // 垂直堆叠容器（VerticalLayoutGroup）
        private readonly Queue<string> _queue = new();
        private bool _showing;

        // 主题颜色
        private static readonly Color BgColor   = new Color(0.05f, 0.05f, 0.1f, 0.92f);
        private static readonly Color TextColor = new Color(0.78f, 0.67f, 0.43f, 1f);   // 金色 #c8aa6e

        // ─────────────────────────────────────────────
        // 生命周期
        // ─────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            BuildContainer();
        }

        // ─────────────────────────────────────────────
        // 公共 API
        // ─────────────────────────────────────────────

        /// <summary>显示一条浮动通知（线程安全队列）。</summary>
        public void Show(string message, float duration = 2.2f)
        {
            _queue.Enqueue(message);
            if (!_showing)
                StartCoroutine(DrainQueue(duration));
        }

        // ─────────────────────────────────────────────
        // 内部
        // ─────────────────────────────────────────────

        private IEnumerator DrainQueue(float duration)
        {
            _showing = true;
            while (_queue.Count > 0)
            {
                string msg = _queue.Dequeue();
                yield return StartCoroutine(ShowOne(msg, duration));
            }
            _showing = false;
        }

        private IEnumerator ShowOne(string msg, float duration)
        {
            // 创建 Toast GO
            var go = new GameObject("Toast");
            go.transform.SetParent(_container, false);

            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(360, 36);

            var img = go.AddComponent<Image>();
            img.color = BgColor;

            var cg = go.AddComponent<CanvasGroup>();
            cg.alpha = 0f;

            // 添加文字
            var textGo = new GameObject("Msg");
            textGo.transform.SetParent(go.transform, false);
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(8, 0);
            textRt.offsetMax = new Vector2(-8, 0);
            var t = textGo.AddComponent<Text>();
            t.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize  = 13;
            t.color     = TextColor;
            t.alignment = TextAnchor.MiddleCenter;
            t.text      = msg;

            // 入场：淡入 + 上滑
            float inDur = 0.35f;
            float elapsed = 0f;
            Vector2 startPos = rt.anchoredPosition - new Vector2(0, 16f);
            Vector2 endPos   = rt.anchoredPosition;
            while (elapsed < inDur)
            {
                elapsed += Time.deltaTime;
                float tt = UITween.ApplyEasePublic(Mathf.Clamp01(elapsed / inDur), UITween.Ease.OutQuad);
                cg.alpha = tt;
                rt.anchoredPosition = Vector2.Lerp(startPos, endPos, tt);
                yield return null;
            }
            cg.alpha = 1f;

            // 停留
            yield return new WaitForSeconds(duration);

            // 淡出
            float outDur = 0.25f;
            elapsed = 0f;
            while (elapsed < outDur)
            {
                elapsed += Time.deltaTime;
                cg.alpha = 1f - Mathf.Clamp01(elapsed / outDur);
                yield return null;
            }

            Destroy(go);
        }

        private void BuildContainer()
        {
            // 在现有的 Canvas 上附加，或自建
            var canvasGo = new GameObject("ToastCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;    // 始终置顶
            canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            var containerGo = new GameObject("ToastContainer");
            containerGo.transform.SetParent(canvas.transform, false);

            var rt = containerGo.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot     = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0, 80f);
            rt.sizeDelta = new Vector2(400, 0);

            var layout = containerGo.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth  = true;
            layout.childControlHeight = false;
            layout.spacing = 4f;
            layout.childAlignment = TextAnchor.UpperCenter;

            var fitter = containerGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _container = containerGo.transform;
        }
    }
}
