using System;
using MaidCafe.Interaction;
using UnityEngine;

namespace MaidCafe.Player
{
    /// <summary>
    /// Facade over the player components and the shared player state (hiding, locks, safe position).
    /// Other systems talk to the player only through this class.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class PlayerContext : MonoBehaviour
    {
        public static PlayerContext Instance { get; private set; }

        public CharacterController Controller { get; private set; }
        public PlayerMotor Motor { get; private set; }
        public PlayerLook Look { get; private set; }
        public Interactor Interactor { get; private set; }
        public PhoneController Phone { get; private set; }
        public BreathController Breath { get; private set; }
        public Camera Camera { get; private set; }

        public HidingSpot CurrentHidingSpot { get; private set; }
        public bool IsHiding => CurrentHidingSpot != null;
        public bool IsHoldingBreath { get; set; }

        /// <summary>Boxes of sweets carried (gifts for the watcher maid). Reset on every new floor.</summary>
        public int Gifts { get; set; }

        /// <summary>When true, the motor ignores input (hiding, cutscenes, death).</summary>
        public bool MovementLocked { get; set; }

        /// <summary>Last position where the player was grounded on walkable floor; used by the anti-softlock watchdog.</summary>
        public Vector3 LastSafePosition { get; set; }

        public event Action<HidingSpot> HidingChanged;

        public Vector3 EyePosition => Camera != null ? Camera.transform.position : transform.position + Vector3.up * 1.6f;

        private void Awake()
        {
            Instance = this;
            Controller = GetComponent<CharacterController>();
            Motor = GetComponent<PlayerMotor>();
            Look = GetComponent<PlayerLook>();
            Interactor = GetComponent<Interactor>();
            Phone = GetComponent<PhoneController>();
            Breath = GetComponent<BreathController>();
            Camera = GetComponentInChildren<Camera>();
            LastSafePosition = transform.position;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void SetHidingSpot(HidingSpot spot)
        {
            CurrentHidingSpot = spot;
            MovementLocked = spot != null;
            if (spot == null) IsHoldingBreath = false;
            HidingChanged?.Invoke(spot);
        }

        /// <summary>Moves the player safely (CharacterController must be disabled while teleporting).</summary>
        public void Teleport(Vector3 position, float yaw)
        {
            bool wasEnabled = Controller.enabled;
            Controller.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            Controller.enabled = wasEnabled;
            Look.SetYaw(yaw);
            Look.SetPitch(0f);
            Motor.ResetVelocity();
            LastSafePosition = position;
        }

        /// <summary>Resets transient state after a respawn or floor load.</summary>
        public void ResetState()
        {
            if (CurrentHidingSpot != null) CurrentHidingSpot.ReleaseOccupant();
            CurrentHidingSpot = null;
            MovementLocked = false;
            IsHoldingBreath = false;
            Controller.enabled = true;
            Look.ClearLimits();
            Motor.ResetVelocity();
            Motor.ForceCrouch(false);
            Motor.KeepStanding = false;
            Motor.EyeHeightOverride = null;
        }
    }
}
