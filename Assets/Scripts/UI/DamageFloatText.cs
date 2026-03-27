using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace FWTCG.UI
{
    /// <summary>
    /// 伤害/增益数字浮起系统（对象池）。
    /// 调用 DamageFloatText.Instance.Show("+3", pos, DamageType.Damage);
    /// 数字从给定屏幕位置向上漂浮并淡出。
    /// </summary>
    public class DamageFloatText : MonoBehaviour
    {
        public static DamageFloatText Instance { get; private set; }

        public enum FloatType { Damage, Heal, Buff, Debuff, Gold }

        private static readonly Color ColDamage = new Color(0.91f, 0.25f, 0.34f, 1f);  // 红
        private static readonly Color ColHeal   = new Color(0.25f, 0.91f, 0.54f, 1f);  // 绿
        private static readonly Color ColBuff   = new Color(0.37f, 0.65f, 0.98f, 1f);  // 蓝
        private static readonly Color ColDebuff = new Color(0.91f, 0.60f, 0.20f, 1f);  // 橙
        private static readonly Color ColGold   = new Color(0.78f, 0.67f, 0.43f, 1f);  // 金

        private Canvas _canvas;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            BuildCanvas();
        }

        // ─────────────────────────────────────────────
        // 公共 API
        // ─────────────────────────────────────────────

        /// <summary>
        /// 在屏幕坐标 screenPos 处弹出浮字。
        /// 若屏幕坐标未知，传 Vector2.zero 会显示在屏幕中央。
        /// </summary>
        public void Show(string text, Vector2 screenPos, FloatType type = FloatType.Damage)
        {
            StartCoroutine(SpawnFloat(text, screenPos, type));
        }

        // ─────────────────────────────────────────────
        // 内部
        // ─────────────────────────────────────────────

        private IEnumerator SpawnFloat(string text, Vector2 screenPos, FloatType type)
        {
            // 创建浮字 GO
            var go = new GameObject("FloatText");
            go.transform.SetParent(_canvas.transform, false);

            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(80, 32);

            // 屏幕坐标转 Canvas 局部坐标
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvas.GetComponent<RectTransform>(),
                screenPos, _canvas.worldCamera, out Vector2 localPos);
            rt.anchoredPosition = localPos;

            var cg = go.AddComponent<CanvasGroup>();
            cg.alpha = 1f;
            cg.blocksRaycasts = false;

            var t = go.AddComponent<Text>();
            t.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize  = 20;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.text      = text;
            t.color     = TypeColor(type);

            // 向上飘 60px，同时淡出，耗时 1.2s
            float dur     = 1.2f;
            float elapsed = 0f;
            Vector2 startPos = rt.anchoredPosition;
            Vector2 endPos   = startPos + new Vector2(0, 60f);

            while (elapsed < dur)
            {
                elapsed += Time.deltaTime;
                float tt = Mathf.Clamp01(elapsed / dur);
                float ease = UITween.ApplyEasePublic(tt, UITween.Ease.OutQuad);
                rt.anchoredPosition = Vector2.Lerp(startPos, endPos, ease);
                cg.alpha = 1f - tt;   // 线性淡出
                yield return null;
            }

            Destroy(go);
        }

        private static Color TypeColor(FloatType type) => type switch
        {
            FloatType.Damage => ColDamage,
            FloatType.Heal   => ColHeal,
            FloatType.Buff   => ColBuff,
            FloatType.Debuff => ColDebuff,
            FloatType.Gold   => ColGold,
            _                => Color.white,
        };

        private void BuildCanvas()
        {
            var canvasGo = new GameObject("DamageFloatCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 90;
            canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            var rt = canvasGo.AddComponent<RectTransform>();
            _ = rt;
        }
    }
}
