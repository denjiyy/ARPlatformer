using UnityEngine;
using UnityEngine.EventSystems;

namespace ARPlatformer
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class TouchJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [SerializeField] private RectTransform handle;
        [SerializeField] private float movementRange = 72f;

        public Vector2 Value { get; private set; }

        public void Configure(RectTransform handleTransform, float maxRange)
        {
            handle = handleTransform;
            movementRange = Mathf.Max(1f, maxRange);
            ResetHandle();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            UpdateHandle(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            UpdateHandle(eventData);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            ResetHandle();
        }

        private void OnDisable()
        {
            ResetHandle();
        }

        private void UpdateHandle(PointerEventData eventData)
        {
            if (handle == null)
                return;

            var rectTransform = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rectTransform,
                    eventData.position,
                    eventData.pressEventCamera,
                    out var localPoint))
            {
                return;
            }

            localPoint = Vector2.ClampMagnitude(localPoint, movementRange);
            Value = localPoint / movementRange;
            handle.anchoredPosition = localPoint;
        }

        private void ResetHandle()
        {
            Value = Vector2.zero;

            if (handle != null)
                handle.anchoredPosition = Vector2.zero;
        }
    }
}
