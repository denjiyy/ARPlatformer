using UnityEngine;

namespace ARPlatformer
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlatformerCharacterController))]
    public sealed class PlatformerCharacterAnimator : MonoBehaviour
    {
        [SerializeField] private string blendParameter = "Blend";
        [SerializeField] private string jumpTrigger = "Jump";
        [SerializeField] private float blendDamping = 8f;

        private PlatformerCharacterController _characterController;
        private Animator _animator;
        private int _blendParameterHash = -1;
        private int _jumpTriggerHash = -1;
        private float _currentBlend;

        private void Awake()
        {
            _characterController = GetComponent<PlatformerCharacterController>();
            RefreshAnimator();
        }

        private void OnEnable()
        {
            if (_characterController == null)
                _characterController = GetComponent<PlatformerCharacterController>();

            if (_characterController != null)
                _characterController.Jumped += HandleJumped;
        }

        private void OnDisable()
        {
            if (_characterController != null)
                _characterController.Jumped -= HandleJumped;
        }

        private void Update()
        {
            if (_animator == null || _characterController == null || _blendParameterHash == -1)
                return;

            var interpolation = blendDamping <= 0f
                ? 1f
                : 1f - Mathf.Exp(-blendDamping * Time.deltaTime);

            _currentBlend = Mathf.Lerp(_currentBlend, _characterController.NormalizedMoveSpeed, interpolation);
            _animator.SetFloat(_blendParameterHash, _currentBlend);
        }

        public void RefreshAnimator()
        {
            _animator = GetComponentInChildren<Animator>(true);
            _blendParameterHash = string.IsNullOrWhiteSpace(blendParameter)
                ? -1
                : Animator.StringToHash(blendParameter);
            _jumpTriggerHash = string.IsNullOrWhiteSpace(jumpTrigger)
                ? -1
                : Animator.StringToHash(jumpTrigger);

            if (_animator == null)
                return;

            _animator.applyRootMotion = false;
        }

        private void HandleJumped()
        {
            if (_animator == null || _jumpTriggerHash == -1)
                return;

            _animator.SetTrigger(_jumpTriggerHash);
        }
    }
}
