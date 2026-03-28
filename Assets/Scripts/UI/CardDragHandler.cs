using System;
using UnityEngine.EventSystems;
using UnityEngine;

namespace FWTCG.UI
{
    /// <summary>
    /// 挂在手牌按钮上，将拖拽事件转发给 GameUI。
    /// </summary>
    public class CardDragHandler : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public int CardUid;
        public Action<int, Vector2> OnBeginDragCb;
        public Action<Vector2>      OnDragCb;
        public Action<int, Vector2> OnEndDragCb;

        public void OnBeginDrag(PointerEventData e) => OnBeginDragCb?.Invoke(CardUid, e.position);
        public void OnDrag(PointerEventData e)      => OnDragCb?.Invoke(e.position);
        public void OnEndDrag(PointerEventData e)   => OnEndDragCb?.Invoke(CardUid, e.position);
    }
}
