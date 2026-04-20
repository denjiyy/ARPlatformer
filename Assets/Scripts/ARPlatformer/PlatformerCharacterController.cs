using System;
using UnityEngine;

namespace ARPlatformer
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlatformerCharacterController : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 1.8f;
        [SerializeField] private float jumpHeight = 0.48f;
        [SerializeField] private float gravity = -14f;
        [SerializeField] private float fallGravityMultiplier = 1.65f;
        [SerializeField] private float terminalVelocity = -20f;
        [SerializeField] private float groundedVelocity = -0.6f;
        [SerializeField] private float groundAcceleration = 12f;
        [SerializeField] private float groundDeceleration = 16f;
        [SerializeField] private float airAcceleration = 5.5f;
        [SerializeField] private float airDeceleration = 3.5f;
        [SerializeField] private float turnSpeed = 14f;
        [SerializeField] private float coyoteTime = 0.12f;
        [SerializeField] private float jumpBufferTime = 0.14f;
        [SerializeField] private float fallRespawnDistance = 0.5f;

        private CharacterController _characterController;
        private Transform _cameraTransform;
        private Vector2 _moveInput;
        private Vector3 _planarVelocity;
        private float _verticalVelocity;
        private float _lastGroundedTime = float.NegativeInfinity;
        private float _lastJumpRequestTime = float.NegativeInfinity;
        private Vector3 _checkpointPosition;
        private Vector3 _checkpointForward = Vector3.forward;
        private float _respawnFloorY;
        private float _lastRespawnTime;
        private bool _movementEnabled = true;
        private bool _respawnRequested;
        private bool _isGrounded;
        private float _fallStartHeight = float.PositiveInfinity;
        private float _fallStartTime = float.NegativeInfinity;
        private float _spawnTime = float.NegativeInfinity;

        public event Action RespawnRequested;
        public event Action Jumped;

        public float NormalizedMoveSpeed { get; private set; }
        public bool IsGrounded => _characterController != null && _characterController.isGrounded;
        public float VerticalVelocity => _verticalVelocity;

        public void Configure(Transform cameraTransform, float movementSpeed, float characterJumpHeight, float characterGravity)
        {
            _cameraTransform = cameraTransform;
            moveSpeed = movementSpeed;
            jumpHeight = characterJumpHeight;
            gravity = characterGravity;
        }

        public void SetMoveInput(Vector2 moveInput)
        {
            _moveInput = Vector2.ClampMagnitude(moveInput, 1f);
        }

        public void QueueJump()
        {
            _lastJumpRequestTime = Time.time;
        }

        public void SetCheckpoint(Vector3 checkpointPosition, Vector3 checkpointForward)
        {
            _checkpointPosition = checkpointPosition;
            _checkpointForward = checkpointForward.sqrMagnitude > 0.001f
                ? checkpointForward.normalized
                : transform.forward;
            _respawnFloorY = checkpointPosition.y - fallRespawnDistance;
        }

        public void SetMovementEnabled(bool movementEnabled)
        {
            _movementEnabled = movementEnabled;

            if (movementEnabled)
                return;

            _moveInput = Vector2.zero;
            _planarVelocity = Vector3.zero;
            _verticalVelocity = groundedVelocity;
            NormalizedMoveSpeed = 0f;
        }

        public void RespawnToCheckpoint()
        {
            _movementEnabled = true;
            _lastRespawnTime = Time.time;
            _spawnTime = Time.time;
            WarpTo(_checkpointPosition, _checkpointForward);
        }

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            SetCheckpoint(transform.position, transform.forward);
            // Initialize spawn time to now so initial spawn has grace period protection
            _spawnTime = Time.time;
        }

        private void FixedUpdate()
        {
            if (_characterController == null)
                return;

            if (!_movementEnabled)
                return;

            if (_cameraTransform == null && Camera.main != null)
                _cameraTransform = Camera.main.transform;

            var deltaTime = Time.fixedDeltaTime;
            var moveFrame = GetCameraRelativeMove();
            var isGrounded = IsActuallyGrounded();

            _isGrounded = isGrounded;
            if (isGrounded)
            {
                _lastGroundedTime = Time.time;
                _fallStartHeight = float.PositiveInfinity;
                _fallStartTime = float.NegativeInfinity;
            }
            else if (_fallStartTime < 0f)
            {
                _fallStartTime = Time.time;
                _fallStartHeight = transform.position.y;
            }

            UpdatePlanarVelocity(moveFrame, isGrounded, deltaTime);
            TryConsumeJump(isGrounded);
            ApplyGravity(isGrounded, deltaTime);

            var motion = _planarVelocity;
            motion.y = _verticalVelocity;
            var collisionFlags = _characterController.Move(motion * deltaTime);

            if ((collisionFlags & CollisionFlags.Below) != 0)
            {
                _lastGroundedTime = Time.time;
                if (_verticalVelocity < 0f)
                    _verticalVelocity = groundedVelocity;
            }

            RotateTowardMoveDirection(deltaTime);
            CheckRespawnBounds();
        }

        private Vector3 GetCameraRelativeMove()
        {
            var forward = Vector3.forward;
            var right = Vector3.right;

            if (_cameraTransform != null)
            {
                forward = _cameraTransform.forward;
                right = _cameraTransform.right;
                forward.y = 0f;
                right.y = 0f;

                if (forward.sqrMagnitude > 0.001f)
                    forward.Normalize();
                else
                    forward = Vector3.forward;

                if (right.sqrMagnitude > 0.001f)
                    right.Normalize();
                else
                    right = Vector3.right;
            }

            var moveFrame = forward * _moveInput.y + right * _moveInput.x;
            return moveFrame.sqrMagnitude > 1f ? moveFrame.normalized : moveFrame;
        }

        private void UpdatePlanarVelocity(Vector3 desiredDirection, bool isGrounded, float deltaTime)
        {
            var desiredVelocity = desiredDirection * moveSpeed;
            var currentAcceleration = isGrounded ? groundAcceleration : airAcceleration;
            var currentDeceleration = isGrounded ? groundDeceleration : airDeceleration;

            if (desiredDirection.sqrMagnitude > 0.0001f)
                _planarVelocity = Vector3.MoveTowards(_planarVelocity, desiredVelocity, currentAcceleration * deltaTime);
            else
                _planarVelocity = Vector3.MoveTowards(_planarVelocity, Vector3.zero, currentDeceleration * deltaTime);

            NormalizedMoveSpeed = moveSpeed > 0.001f
                ? Mathf.Clamp01(new Vector3(_planarVelocity.x, 0f, _planarVelocity.z).magnitude / moveSpeed)
                : 0f;
        }

        private void TryConsumeJump(bool isGrounded)
        {
            var hasBufferedJump = Time.time - _lastJumpRequestTime <= jumpBufferTime;
            var canUseCoyoteTime = Time.time - _lastGroundedTime <= coyoteTime;

            if (!hasBufferedJump || (!isGrounded && !canUseCoyoteTime))
                return;

            _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            _lastJumpRequestTime = float.NegativeInfinity;
            _lastGroundedTime = float.NegativeInfinity;
            Jumped?.Invoke();
        }

        private void ApplyGravity(bool isGrounded, float deltaTime)
        {
            if (isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = groundedVelocity;

            var gravityMultiplier = _verticalVelocity < 0f ? fallGravityMultiplier : 1f;
            _verticalVelocity += gravity * gravityMultiplier * deltaTime;
            _verticalVelocity = Mathf.Max(_verticalVelocity, terminalVelocity);
        }

        private void RotateTowardMoveDirection(float deltaTime)
        {
            var planarDirection = new Vector3(_planarVelocity.x, 0f, _planarVelocity.z);
            if (planarDirection.sqrMagnitude < 0.0001f)
                return;

            var targetRotation = Quaternion.LookRotation(planarDirection.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, turnSpeed * deltaTime);
        }

        private void CheckRespawnBounds()
        {
            if (_respawnRequested || Time.time - _lastRespawnTime < 1f || _isGrounded)
                return;

            if (Time.time - _spawnTime < 0.5f)
                return;

            if (_fallStartTime < 0f)
                return;

            var fallDistance = _fallStartHeight - transform.position.y;
            if (fallDistance <= fallRespawnDistance)
                return;

            if (transform.position.y > _respawnFloorY)
                return;

            if (Time.time - _fallStartTime < 0.35f)
                return;

            _respawnRequested = true;
            RespawnRequested?.Invoke();
            _respawnRequested = false;
        }

        private bool IsActuallyGrounded()
        {
            if (_characterController == null)
                return false;
            return _characterController.isGrounded;
        }

        private void WarpTo(Vector3 worldPosition, Vector3 forward)
        {
            if (_characterController != null)
                _characterController.enabled = false;

            transform.SetPositionAndRotation(
                worldPosition,
                Quaternion.LookRotation(forward.sqrMagnitude > 0.001f ? forward : Vector3.forward, Vector3.up));

            _planarVelocity = Vector3.zero;
            _verticalVelocity = groundedVelocity;
            NormalizedMoveSpeed = 0f;
            _lastJumpRequestTime = float.NegativeInfinity;
            _lastGroundedTime = Time.time;

            if (_characterController != null)
                _characterController.enabled = true;
        }
    }
}
