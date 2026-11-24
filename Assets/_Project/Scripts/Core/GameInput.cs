using UnityEngine;
using UnityEngine.InputSystem;

namespace MaidCafe.Core
{
    /// <summary>
    /// All gameplay input actions, built in code so no asset wiring is required.
    /// Keyboard/mouse and gamepad are both supported.
    /// </summary>
    public static class GameInput
    {
        public static InputAction Move { get; private set; }
        public static InputAction LookMouse { get; private set; }
        public static InputAction LookStick { get; private set; }
        public static InputAction Sprint { get; private set; }
        public static InputAction Crouch { get; private set; }
        public static InputAction Interact { get; private set; }
        public static InputAction Phone { get; private set; }
        public static InputAction HoldBreath { get; private set; }
        public static InputAction Pause { get; private set; }

        private static InputActionMap _gameplay;
        private static InputActionMap _system;

        public static bool GameplayEnabled => _gameplay != null && _gameplay.enabled;

        public static void EnsureInitialized()
        {
            if (_gameplay != null) return;

            _gameplay = new InputActionMap("Gameplay");

            Move = _gameplay.AddAction("Move", InputActionType.Value);
            Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");
            Move.AddBinding("<Gamepad>/leftStick");

            LookMouse = _gameplay.AddAction("LookMouse", InputActionType.Value, "<Mouse>/delta");
            LookStick = _gameplay.AddAction("LookStick", InputActionType.Value, "<Gamepad>/rightStick");

            Sprint = _gameplay.AddAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");
            Sprint.AddBinding("<Gamepad>/leftStickPress");

            Crouch = _gameplay.AddAction("Crouch", InputActionType.Button, "<Keyboard>/leftCtrl");
            Crouch.AddBinding("<Keyboard>/c");
            Crouch.AddBinding("<Gamepad>/buttonEast");

            Interact = _gameplay.AddAction("Interact", InputActionType.Button, "<Keyboard>/e");
            Interact.AddBinding("<Gamepad>/buttonSouth");

            Phone = _gameplay.AddAction("Phone", InputActionType.Button, "<Mouse>/rightButton");
            Phone.AddBinding("<Keyboard>/f");
            Phone.AddBinding("<Gamepad>/leftTrigger");

            HoldBreath = _gameplay.AddAction("HoldBreath", InputActionType.Button, "<Keyboard>/space");
            HoldBreath.AddBinding("<Gamepad>/rightShoulder");

            // Pause lives in its own map so it keeps working while gameplay input is disabled.
            _system = new InputActionMap("System");
            Pause = _system.AddAction("Pause", InputActionType.Button, "<Keyboard>/escape");
            Pause.AddBinding("<Gamepad>/start");

            _gameplay.Enable();
            _system.Enable();
        }

        /// <summary>Enables or disables movement/look/interaction (pause menus, cutscenes, death).</summary>
        public static void SetGameplayEnabled(bool enabled)
        {
            EnsureInitialized();
            if (enabled) _gameplay.Enable();
            else _gameplay.Disable();
        }

        public static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _gameplay?.Dispose();
            _system?.Dispose();
            _gameplay = null;
            _system = null;
        }
    }
}
