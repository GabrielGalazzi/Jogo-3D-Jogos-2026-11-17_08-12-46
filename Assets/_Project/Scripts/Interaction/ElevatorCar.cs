using System;
using MaidCafe.Audio;
using MaidCafe.Core;
using MaidCafe.Player;
using UnityEngine;

namespace MaidCafe.Interaction
{
    public enum ElevatorMode
    {
        /// <summary>The car the player arrives in. Closes for good once the player leaves it.</summary>
        Arrival,

        /// <summary>Locked until the floor objective is complete; boarding it ends the floor.</summary>
        Exit,

        /// <summary>Exit of the last floor; boarding it wins the game.</summary>
        FinalExit
    }

    /// <summary>
    /// Elevator car built against the outer wall. Doors slide along the room-side face of the wall.
    /// Player presence is polled against the car's interior box (robust with CharacterController).
    /// </summary>
    public class ElevatorCar : MonoBehaviour, IInteractable
    {
        [SerializeField] private ElevatorMode mode;
        [SerializeField] private Transform leftDoor;
        [SerializeField] private Transform rightDoor;
        [SerializeField] private float closedOffset = 0.4f;
        [SerializeField] private float openOffset = 1.2f;
        [SerializeField] private float doorSpeed = 1.1f;
        [SerializeField] private Vector3 interiorCenter;
        [SerializeField] private Vector3 interiorSize;
        [SerializeField] private Transform spawnPoint;
        [SerializeField] private Light carLight;

        private float _open;
        private bool _wantOpen;
        private bool _doorsMoving;
        private bool _locked = true;
        private bool _boarding;
        private bool _arrivalStarted;
        private bool _boardedRaised;
        private float _leftTimer;

        public ElevatorMode Mode => mode;
        public bool Locked => _locked;
        public bool PlayerInside { get; private set; }
        public Transform SpawnPoint => spawnPoint;
        public bool DoorsClosed => _open <= 0.001f;

        /// <summary>Raised once when the player is inside an unlocked exit car and the doors have fully closed.</summary>
        public event Action PlayerBoarded;

        /// <summary>Optional objective hook: when it returns true, interacting runs <see cref="OverrideUse"/> instead of the locked hint.</summary>
        public Func<bool> OverrideReady { get; set; }
        public Action OverrideUse { get; set; }
        public string OverridePromptKey { get; set; }
        public float OverrideHold { get; set; } = 1f;

        private bool OverrideActive => OverrideReady != null && OverrideReady();

        public string PromptKey => OverrideActive ? OverridePromptKey : "interact.elevatorLocked";
        public string NameKey => mode == ElevatorMode.FinalExit ? "object.exit" : "object.elevator";
        public float HoldDuration => OverrideActive ? OverrideHold : 0f;

        public void Configure(ElevatorMode elevatorMode, Transform left, Transform right, float closed, float open,
            Vector3 localInteriorCenter, Vector3 localInteriorSize, Transform spawn, Light light)
        {
            carLight = light;
            mode = elevatorMode;
            leftDoor = left;
            rightDoor = right;
            closedOffset = closed;
            openOffset = open;
            interiorCenter = localInteriorCenter;
            interiorSize = localInteriorSize;
            spawnPoint = spawn;
            ApplyDoors();
        }

        public bool CanInteract(PlayerContext player) => mode != ElevatorMode.Arrival && _locked;

        public void Interact(PlayerContext player)
        {
            if (OverrideActive)
            {
                OverrideUse?.Invoke();
                return;
            }
            GameEvents.ShowHint(mode == ElevatorMode.FinalExit ? "hint.exitLocked" : "hint.elevatorLocked");
        }

        /// <summary>Arrival car: open the doors so the player can step out.</summary>
        public void OpenForArrival()
        {
            _arrivalStarted = true;
            _leftTimer = 0f;
            _wantOpen = true;
        }

        /// <summary>Exit car: objective complete.</summary>
        public void Unlock()
        {
            if (!_locked) return;
            _locked = false;
            _wantOpen = true;
            NoiseSystem.Emit(transform.position, 10f, NoiseType.Alarm, fromPlayer: false);
            GameAudio.PlayAt(Sfx.ElevatorChime, transform.position + transform.forward * 0.4f + Vector3.up * 2.4f, 0.9f, 35f, 0f);
        }

        /// <summary>Instantly closes the doors (used when resetting a floor).</summary>
        public void ForceClosed()
        {
            _wantOpen = false;
            _open = 0f;
            ApplyDoors();
        }

        private void Update()
        {
            var player = PlayerContext.Instance;
            PlayerInside = player != null && IsInside(player.transform.position);

            switch (mode)
            {
                case ElevatorMode.Arrival:
                    UpdateArrival();
                    break;
                default:
                    UpdateExit();
                    break;
            }

            float target = _wantOpen ? 1f : 0f;
            bool moving = Mathf.Abs(_open - target) > 0.001f;
            if (moving && !_doorsMoving)
            {
                GameAudio.PlayAt(target > _open ? Sfx.ElevatorDoorOpen : Sfx.ElevatorDoorClose,
                    transform.position + transform.forward * 0.3f + Vector3.up * 1.2f, 0.6f, 20f);
            }
            _doorsMoving = moving;
            _open = Mathf.MoveTowards(_open, target, doorSpeed * Time.deltaTime);
            ApplyDoors();
            // No shadows on the car light, so keep it off while the doors are shut to avoid light leaking through them.
            if (carLight != null) carLight.enabled = _open > 0.02f || PlayerInside;

            if (_boarding && DoorsClosed && PlayerInside && !_boardedRaised)
            {
                _boardedRaised = true;
                PlayerBoarded?.Invoke();
            }
        }

        private void UpdateArrival()
        {
            if (!_arrivalStarted) return;

            if (PlayerInside)
            {
                // Anti-softlock: whenever the player is inside (arrival, respawn, safe-point teleport),
                // the doors open so they can always step out.
                _leftTimer = 0f;
                _wantOpen = true;
                return;
            }

            if (!_wantOpen) return;
            _leftTimer += Time.deltaTime;
            if (_leftTimer > 1.5f)
            {
                // The player stepped out: the doors shut behind them, so the car cannot be used as a refuge.
                _wantOpen = false;
            }
        }

        private void UpdateExit()
        {
            if (_locked || _boardedRaised) return;
            if (PlayerInside && !_boarding)
            {
                _boarding = true;
                _wantOpen = false;
            }
            else if (!PlayerInside && _boarding)
            {
                // Player stepped back out while the doors were closing.
                _boarding = false;
                _wantOpen = true;
            }
        }

        private bool IsInside(Vector3 worldPosition)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition) - interiorCenter;
            Vector3 half = interiorSize * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        private void ApplyDoors()
        {
            float offset = Mathf.Lerp(closedOffset, openOffset, Mathf.SmoothStep(0f, 1f, _open));
            if (leftDoor != null)
            {
                var p = leftDoor.localPosition;
                leftDoor.localPosition = new Vector3(-offset, p.y, p.z);
            }
            if (rightDoor != null)
            {
                var p = rightDoor.localPosition;
                rightDoor.localPosition = new Vector3(offset, p.y, p.z);
            }
        }
    }
}
