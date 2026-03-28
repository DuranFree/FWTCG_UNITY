using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FWTCG.UI
{
    /// <summary>
    /// P30 — 手牌悬停缩放：鼠标悬停时 Scale 1→1.07（0.1s），离开时 1.07→1（0.12s）。
    /// 挂在手牌 row GameObject 上即可生效。
    /// </summary>
    public class HoverScale : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private RectTransform _rt;
        private Coroutine     _current;

        private void Awake() => _rt = GetComponent<RectTransform>();

        public void OnPointerEnter(PointerEventData _)
        {
            if (_current != null) StopCoroutine(_current);
            _current = StartCoroutine(UITween.ScaleTo(_rt, new Vector3(1.07f, 1.07f, 1f),
                0.10f, UITween.Ease.OutQuad));
        }

        public void OnPointerExit(PointerEventData _)
        {
            if (_current != null) StopCoroutine(_current);
            _current = StartCoroutine(UITween.ScaleTo(_rt, Vector3.one,
                0.12f, UITween.Ease.OutQuad));
        }
    }
}
