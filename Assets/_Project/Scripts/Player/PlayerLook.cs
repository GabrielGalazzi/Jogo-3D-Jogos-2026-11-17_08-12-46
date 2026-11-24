using MaidCafe.Core;
using UnityEngine;

namespace MaidCafe.Player
{
    /// <summary>
    /// Mouse/gamepad look (yaw on the body, pitch on the head), FOV with sprint kick,
    /// head-bob, and optional look limits while hiding.
    /// </summary>
    public class PlayerLook : MonoBehaviour
    {
        [SerializeField] private Transform head;
        [SerializeField] private Camera cam;

        [Header("Look")]
        [SerializeField] private float mouseDegreesPerCount = 0.08f;
        [SerializeField] private float stickDegreesPerSecond = 170f;
        [SerializeField] private float minPitch = -85f;
        [SerializeField] private float maxPitch = 85f;

        [Header("FOV")]
        [SerializeField] private float sprintFovKick = 6f;
        [SerializeField] private float fovLerpSpeed = 6f;

        [Header("Head-bob")]
        [SerializeField] private float bobFrequency = 1.9f;
        [SerializeField] private float bobVertical = 0.045f;
        [SerializeField] private float bobHorizontal = 0.03f;

        private PlayerMotor _motor;
        private float _yaw;
        private float _pitch;
        private float _bobPhase;
        private Vector3 _cameraBaseLocal;

        private bool _limited;
        private float _limitCenterYaw;
        private float _limitYawRange;
        private float _limitMinPitch;
        private float _limitMaxPitch;

        public Camera Camera => cam;
        public float Yaw => _yaw;

        private void Awake()
        {
            _motor = GetComponent<PlayerMotor>();
            _yaw = transform.eulerAngles.y;
            if (cam != null) _cameraBaseLocal = cam.transform.localPosition;
        }

        public void SetReferences(Transform headTransform, Camera camera)
        {
            head = headTransform;
            cam = camera;
            _cameraBaseLocal = cam.transform.localPosition;
        }

        public void SetYaw(float yaw)
        {
            _yaw = yaw;
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        public void SetPitch(float pitch)
        {
            _pitch = pitch;
            if (head != null) head.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        /// <summary>
        /// Restricts the view around a centre yaw (used while hiding). A <paramref name="yawRange"/> of 180 or more
        /// leaves the yaw free (full turn) and only limits the pitch.
        /// </summary>
        public void SetLimits(float centerYaw, float yawRange, float pitchMin, float pitchMax)
        {
            _limited = true;
            _limitCenterYaw = centerYaw;
            _limitYawRange = yawRange;
            _limitMinPitch = pitchMin;
            _limitMaxPitch = pitchMax;
        }

        public void ClearLimits() => _limited = false;

        private void Update()
        {
            float dt = Time.deltaTime;

            if (GameInput.GameplayEnabled)
            {
                float sensitivity = GameSettings.MouseSensitivity;
                Vector2 delta = GameInput.LookMouse.ReadValue<Vector2>() * (mouseDegreesPerCount * sensitivity);
                delta += GameInput.LookStick.ReadValue<Vector2>() * (stickDegreesPerSecond * sensitivity * dt);
                if (GameSettings.InvertY) delta.y = -delta.y;

                _yaw += delta.x;
                _pitch -= delta.y;
            }

            if (_limited)
            {
                if (_limitYawRange < 180f)
                {
                    float offset = Mathf.Clamp(Mathf.DeltaAngle(_limitCenterYaw, _yaw), -_limitYawRange, _limitYawRange);
                    _yaw = _limitCenterYaw + offset;
                }
                _pitch = Mathf.Clamp(_pitch, _limitMinPitch, _limitMaxPitch);
            }
            else
            {
                _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
            }

            _yaw = Mathf.Repeat(_yaw, 360f);
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            if (head != null) head.localRotation = Quaternion.Euler(_pitch, 0f, 0f);

            UpdateFov(dt);
            UpdateBob(dt);
        }

        private void UpdateFov(float dt)
        {
            if (cam == null) return;
            float target = GameSettings.FieldOfView + (_motor != null && _motor.IsSprinting ? sprintFovKick : 0f);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, target, 1f - Mathf.Exp(-fovLerpSpeed * dt));
        }

        private void UpdateBob(float dt)
        {
            if (cam == null || _motor == null) return;
            float speed = _motor.HorizontalSpeed;
            float amount = Mathf.Clamp01(speed / Mathf.Max(0.01f, _motor.SprintSpeed));
            Vector3 offset = Vector3.zero;
            if (speed > 0.2f)
            {
                _bobPhase += dt * bobFrequency * Mathf.PI * 2f * Mathf.Lerp(0.6f, 1.3f, amount);
                offset.y = Mathf.Abs(Mathf.Sin(_bobPhase)) * bobVertical * (0.4f + amount);
                offset.x = Mathf.Cos(_bobPhase) * bobHorizontal * (0.4f + amount);
            }
            Vector3 target = _cameraBaseLocal + offset;
            cam.transform.localPosition = Vector3.Lerp(cam.transform.localPosition, target, 1f - Mathf.Exp(-12f * dt));
        }
    }
}
