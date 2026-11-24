using System;
using MaidCafe.Core;
using UnityEngine;

namespace MaidCafe.Player
{
    /// <summary>
    /// First-person CharacterController movement: walk, sprint with stamina, crouch with ceiling check (sprinting
    /// from a crouch stands up and runs at once), gravity, footstep noise. Tuned to avoid snagging on corners and
    /// door frames.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMotor : MonoBehaviour
    {
        [Header("Speeds (m/s)")]
        [SerializeField] private float walkSpeed = 2.8f;
        [SerializeField] private float sprintSpeed = 5.4f;
        [SerializeField] private float crouchSpeed = 1.5f;
        [SerializeField] private float acceleration = 14f;
        [SerializeField] private float deceleration = 18f;
        [SerializeField] private float gravity = -20f;

        [Header("Stamina (seconds of sprint)")]
        [SerializeField] private float maxStamina = 5f;
        [SerializeField] private float staminaRegenPerSecond = 0.8f;
        [SerializeField] private float staminaRegenDelay = 1.2f;
        [SerializeField] private float exhaustedThreshold = 1.5f;

        [Header("Heights (m)")]
        [SerializeField] private float standHeight = 1.75f;
        [SerializeField] private float crouchHeight = 1.0f;
        [SerializeField] private float eyeOffsetFromTop = 0.12f;
        [SerializeField] private float heightLerpSpeed = 10f;

        [Header("Noise radius (m)")]
        [SerializeField] private float crouchNoise = 1.5f;
        [SerializeField] private float walkNoise = 5f;
        [SerializeField] private float sprintNoise = 13f;

        [Header("References")]
        [SerializeField] private Transform head;

        private CharacterController _controller;
        private PlayerContext _player;
        private Vector3 _horizontalVelocity;
        private float _verticalVelocity;
        private float _stamina;
        private float _staminaRegenTimer;
        private bool _exhausted;
        private bool _crouchToggled;
        private bool _forcedCrouch;
        private bool _crouchBlockedUntilRelease;
        private float _stepDistance;

        public bool IsSprinting { get; private set; }
        public bool IsCrouching { get; private set; }
        public float HorizontalSpeed => _horizontalVelocity.magnitude;
        public float Stamina01 => _stamina / maxStamina;

        /// <summary>True from the moment stamina runs out until it has recovered enough to sprint again.</summary>
        public bool IsExhausted => _exhausted;
        public float CurrentHeight => _controller != null ? _controller.height : standHeight;
        public float EyeHeight => EyeHeightOverride ?? CurrentHeight - eyeOffsetFromTop;

        /// <summary>Forces the camera height (e.g. lying under a table). Null = derived from the capsule.</summary>
        public float? EyeHeightOverride { get; set; }

        /// <summary>
        /// While true the player cannot crouch (inside a locker). A crouch key still held when this is cleared is
        /// ignored until it is released, so the player never leaves the locker crouched.
        /// </summary>
        public bool KeepStanding { get; set; }
        public float SprintSpeed => sprintSpeed;

        /// <summary>Noise radius (m) of each footstep at the current gait: the value sent to <see cref="NoiseSystem"/>.</summary>
        public float StepNoiseRadius => IsSprinting ? sprintNoise : IsCrouching ? crouchNoise : walkNoise;

        /// <summary>
        /// Footstep noise the player is making right now: <see cref="StepNoiseRadius"/> while moving on the ground (the
        /// same conditions under which footsteps are emitted), 0 when standing still, airborne or not in control.
        /// </summary>
        public float MovementNoise =>
            _controller != null && _controller.enabled && _controller.isGrounded && HorizontalSpeed >= FootstepMinSpeed
                ? StepNoiseRadius
                : 0f;

        private const float FootstepMinSpeed = 0.3f;

        /// <summary>Raised on every footstep with the noise radius that step produced.</summary>
        public event Action<float> Footstep;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _player = GetComponent<PlayerContext>();
            _stamina = maxStamina;
            ConfigureController();
        }

        private void ConfigureController()
        {
            _controller.height = standHeight;
            _controller.center = new Vector3(0f, standHeight * 0.5f, 0f);
            _controller.radius = 0.35f;
            _controller.stepOffset = 0.3f;
            _controller.slopeLimit = 45f;
            _controller.skinWidth = 0.03f;
            _controller.minMoveDistance = 0f;
        }

        public void SetHead(Transform headTransform) => head = headTransform;

        public void ResetVelocity()
        {
            _horizontalVelocity = Vector3.zero;
            _verticalVelocity = 0f;
        }

        public void ForceCrouch(bool crouch)
        {
            _forcedCrouch = crouch;
            if (!crouch) _crouchToggled = false;
        }

        /// <summary>
        /// Stands the player up at once and keeps them standing (<see cref="KeepStanding"/>): clears a toggled or
        /// forced crouch and restores the standing capsule. The camera rises to the standing eye height through the
        /// usual height smoothing. Used when entering a locker, which always has room to stand.
        /// </summary>
        public void StandUpNow()
        {
            KeepStanding = true;
            _forcedCrouch = false;
            _crouchToggled = false;
            _crouchBlockedUntilRelease = true;
            IsCrouching = false;
            _controller.height = standHeight;
            _controller.center = new Vector3(0f, standHeight * 0.5f, 0f);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            bool locked = _player != null && _player.MovementLocked;
            bool inputEnabled = GameInput.GameplayEnabled && !locked;

            Vector2 input = inputEnabled && _controller.enabled ? GameInput.Move.ReadValue<Vector2>() : Vector2.zero;
            input = Vector2.ClampMagnitude(input, 1f);

            // Sprint pressed while moving forward, with stamina to run. From a crouch it stands the player up first.
            bool sprintRequested = inputEnabled && _controller.enabled && GameInput.Sprint.IsPressed() && input.y > 0.1f
                                   && !_exhausted && _stamina > 0f;

            UpdateCrouch(inputEnabled, sprintRequested, dt);

            if (!_controller.enabled)
            {
                // Controller is off while hiding or being moved by a script: drop the last velocity,
                // otherwise HorizontalSpeed keeps its walking/sprinting value and the head-bob keeps playing.
                // Stamina still recovers (e.g. catching your breath inside a locker).
                IsSprinting = false;
                ResetVelocity();
                _stepDistance = 0f;
                UpdateStamina(dt);
                return;
            }

            IsSprinting = sprintRequested && !IsCrouching;
            UpdateStamina(dt);

            float targetSpeed = IsCrouching ? crouchSpeed : IsSprinting ? sprintSpeed : walkSpeed;
            Vector3 wish = (transform.right * input.x + transform.forward * input.y) * targetSpeed;
            float rate = wish.sqrMagnitude > _horizontalVelocity.sqrMagnitude ? acceleration : deceleration;
            _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, wish, rate * targetSpeed * dt);

            if (_controller.isGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
            else _verticalVelocity += gravity * dt;

            Vector3 before = transform.position;
            _controller.Move((_horizontalVelocity + Vector3.up * _verticalVelocity) * dt);

            // Kill velocity into walls so the player does not "stick" when sliding along them.
            Vector3 moved = transform.position - before;
            moved.y = 0f;
            if (dt > 0f && _horizontalVelocity.sqrMagnitude > 0.01f)
            {
                Vector3 actual = moved / dt;
                if (actual.sqrMagnitude < _horizontalVelocity.sqrMagnitude) _horizontalVelocity = actual;
            }

            if (_controller.isGrounded) _player.LastSafePosition = transform.position;

            UpdateFootsteps(moved.magnitude);
        }

        private void UpdateStamina(float dt)
        {
            if (IsSprinting && HorizontalSpeed > 0.5f)
            {
                _stamina = Mathf.Max(0f, _stamina - dt);
                _staminaRegenTimer = staminaRegenDelay;
                if (_stamina <= 0f) _exhausted = true;
            }
            else
            {
                _staminaRegenTimer -= dt;
                if (_staminaRegenTimer <= 0f) _stamina = Mathf.Min(maxStamina, _stamina + staminaRegenPerSecond * dt);
                if (_exhausted && _stamina >= exhaustedThreshold) _exhausted = false;
            }
        }

        /// <param name="sprintRequested">
        /// Sprinting from a crouch stands the player up in the same frame (when there is room above), so the run starts
        /// at once. The crouch stays off until the crouch key is pressed again (hold mode: released and pressed again).
        /// </param>
        private void UpdateCrouch(bool inputEnabled, bool sprintRequested, float dt)
        {
            // A disabled action reads as released anyway; the check also keeps this safe before input is set up.
            bool crouchHeld = GameInput.GameplayEnabled && GameInput.Crouch.IsPressed();
            if (_crouchBlockedUntilRelease && !crouchHeld && !KeepStanding) _crouchBlockedUntilRelease = false;

            bool wantCrouch;
            if (KeepStanding)
            {
                _crouchToggled = false;
                wantCrouch = false;
            }
            else if (_forcedCrouch)
            {
                wantCrouch = true;
            }
            else if (GameSettings.CrouchToggle)
            {
                if (inputEnabled && !_crouchBlockedUntilRelease && GameInput.Crouch.WasPressedThisFrame())
                {
                    _crouchToggled = !_crouchToggled;
                }
                wantCrouch = _crouchToggled;
            }
            else
            {
                wantCrouch = inputEnabled && !_crouchBlockedUntilRelease && crouchHeld;
            }

            if (wantCrouch && sprintRequested && !_forcedCrouch && !KeepStanding && CanStandUp())
            {
                _crouchToggled = false;
                if (crouchHeld) _crouchBlockedUntilRelease = true;
                wantCrouch = false;
            }

            // Can only stand up when there is room above (a locker always has room).
            if (!wantCrouch && IsCrouching && !KeepStanding && !CanStandUp())
            {
                wantCrouch = true;
            }
            IsCrouching = wantCrouch;

            float target = IsCrouching ? crouchHeight : standHeight;
            float height = Mathf.MoveTowards(_controller.height, target, heightLerpSpeed * dt);
            if (!Mathf.Approximately(height, _controller.height))
            {
                _controller.height = height;
                _controller.center = new Vector3(0f, height * 0.5f, 0f);
            }
            if (head != null)
            {
                Vector3 local = head.localPosition;
                local.y = Mathf.Lerp(local.y, EyeHeight, 1f - Mathf.Exp(-heightLerpSpeed * dt));
                head.localPosition = local;
            }
        }

        private bool CanStandUp()
        {
            float radius = _controller.radius * 0.95f;
            Vector3 bottom = transform.position + Vector3.up * (radius + 0.05f);
            float distance = standHeight - radius * 2f - 0.05f;
            return !Physics.SphereCast(bottom, radius, Vector3.up, out _, distance, ~0, QueryTriggerInteraction.Ignore);
        }

        private void UpdateFootsteps(float movedDistance)
        {
            if (!_controller.isGrounded || HorizontalSpeed < FootstepMinSpeed) return;
            float stride = IsSprinting ? 2.0f : IsCrouching ? 1.0f : 1.6f;
            _stepDistance += movedDistance;
            if (_stepDistance < stride) return;
            _stepDistance = 0f;

            float radius = StepNoiseRadius;
            NoiseSystem.Emit(transform.position, radius, NoiseType.Footstep);
            Footstep?.Invoke(radius);
        }
    }
}
