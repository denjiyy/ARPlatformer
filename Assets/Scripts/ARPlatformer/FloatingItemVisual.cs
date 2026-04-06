using UnityEngine;

namespace ARPlatformer
{
    public sealed class FloatingItemVisual : MonoBehaviour
    {
        [SerializeField] private float spinSpeed = 90f;
        [SerializeField] private float bobHeight = 0.05f;
        [SerializeField] private float bobSpeed = 2.4f;

        private Vector3 _baseLocalPosition;
        private float _phaseOffset;

        public void Configure(float configuredSpinSpeed, float configuredBobHeight, float configuredBobSpeed)
        {
            spinSpeed = configuredSpinSpeed;
            bobHeight = configuredBobHeight;
            bobSpeed = configuredBobSpeed;
        }

        private void Awake()
        {
            _baseLocalPosition = transform.localPosition;
            _phaseOffset = Random.Range(0f, Mathf.PI * 2f);
        }

        private void Update()
        {
            var bobOffset = Mathf.Sin(Time.time * bobSpeed + _phaseOffset) * bobHeight;
            transform.localPosition = _baseLocalPosition + Vector3.up * bobOffset;
            transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.Self);
        }
    }
}
