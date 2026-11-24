using MaidCafe.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace MaidCafe.Dev
{
    /// <summary>
    /// First-person viewer for the showcase scene. Walk mode uses the same capsule as the game's player (so it bumps
    /// into the maids' body colliders); fly mode (F) has no collision and moves freely, including above and below
    /// the models (the floor is one-sided, so it is see-through from below).
    /// Mouse look, WASD, Shift faster, Space jump / fly up, Ctrl or C fly down, Esc frees the mouse.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class ShowcaseViewer : MonoBehaviour
    {
        private const float EyeHeight = 1.63f;
        private const float WalkSpeed = 2.4f, WalkFastSpeed = 4.5f;
        private const float FlySpeed = 2.5f, FlyFastSpeed = 7f;
        private const float Gravity = -18f, JumpSpeed = 4.5f;
        private const float MouseDegreesPerCount = 0.08f;

        private CharacterController _controller;
        private Transform _head;
        private Vector3 _spawn;
        private float _spawnYaw;
        private float _yaw, _pitch, _verticalSpeed;

        public bool Flying { get; private set; }
        public Camera Camera { get; private set; }

        public static ShowcaseViewer Create(Vector3 position, float yaw, Color background)
        {
            var go = new GameObject("Viewer");
            go.SetActive(false);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            // Same capsule as PlayerMotor, so collisions feel like the game.
            var controller = go.AddComponent<CharacterController>();
            controller.height = 1.75f;
            controller.center = new Vector3(0f, 0.875f, 0f);
            controller.radius = 0.35f;
            controller.stepOffset = 0.3f;
            controller.slopeLimit = 45f;
            controller.skinWidth = 0.03f;

            var head = new GameObject("Head").transform;
            head.SetParent(go.transform, false);
            head.localPosition = new Vector3(0f, EyeHeight, 0f);

            var camGo = new GameObject("ViewerCamera") { tag = "MainCamera" };
            camGo.transform.SetParent(head, false);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 150f;
            cam.fieldOfView = GameSettings.FieldOfView;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            cam.GetUniversalAdditionalCameraData().antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            camGo.AddComponent<AudioListener>();

            var viewer = go.AddComponent<ShowcaseViewer>();
            viewer._controller = controller;
            viewer._head = head;
            viewer._spawn = position;
            viewer._spawnYaw = yaw;
            viewer._yaw = yaw;
            viewer.Camera = cam;
            go.SetActive(true);
            return viewer;
        }

        private void Start() => SetCursorLocked(true);

        private void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null) return;
            float dt = Time.deltaTime;

            if (kb.escapeKey.wasPressedThisFrame) SetCursorLocked(Cursor.lockState != CursorLockMode.Locked);
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked) SetCursorLocked(true);

            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 delta = mouse.delta.ReadValue() * (MouseDegreesPerCount * GameSettings.MouseSensitivity);
                if (GameSettings.InvertY) delta.y = -delta.y;
                _yaw += delta.x;
                _pitch = Mathf.Clamp(_pitch - delta.y, -89f, 89f);
            }
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            _head.localRotation = Quaternion.Euler(_pitch, 0f, 0f);

            if (kb.fKey.wasPressedThisFrame) SetFlying(!Flying);

            var input = new Vector2(
                (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f),
                (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f));
            bool fast = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;

            if (Flying)
            {
                // Fly where you look; Space / Ctrl (or C) straight up / down.
                float vertical = (kb.spaceKey.isPressed ? 1f : 0f) - (kb.leftCtrlKey.isPressed || kb.cKey.isPressed ? 1f : 0f);
                Transform view = Camera.transform;
                Vector3 move = view.forward * input.y + view.right * input.x + Vector3.up * vertical;
                transform.position += Vector3.ClampMagnitude(move, 1f) * ((fast ? FlyFastSpeed : FlySpeed) * dt);
                return;
            }

            Vector3 walk = Vector3.ClampMagnitude(transform.forward * input.y + transform.right * input.x, 1f) * (fast ? WalkFastSpeed : WalkSpeed);
            if (_controller.isGrounded)
            {
                _verticalSpeed = -1f;
                if (kb.spaceKey.wasPressedThisFrame) _verticalSpeed = JumpSpeed;
            }
            else
            {
                _verticalSpeed += Gravity * dt;
            }
            _controller.Move((walk + Vector3.up * _verticalSpeed) * dt);

            if (transform.position.y < -10f) Teleport(_spawn, _spawnYaw);
        }

        private void SetFlying(bool flying)
        {
            Flying = flying;
            _controller.enabled = !flying;
            _verticalSpeed = 0f;
            if (!flying && transform.position.y < 0f)
            {
                // Back to walking from below the floor: stand on it at the same spot.
                transform.position = new Vector3(transform.position.x, 0.05f, transform.position.z);
            }
        }

        private void Teleport(Vector3 position, float yaw)
        {
            bool wasEnabled = _controller.enabled;
            _controller.enabled = false;
            transform.position = position;
            _controller.enabled = wasEnabled;
            _yaw = yaw;
            _pitch = 0f;
            _verticalSpeed = 0f;
        }

        private static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
