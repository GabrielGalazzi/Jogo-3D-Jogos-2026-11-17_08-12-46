using MaidCafe.Core;
using MaidCafe.Player;
using UnityEngine;

namespace MaidCafe.Interaction
{
    /// <summary>
    /// Finds the interactable under the crosshair and handles press / hold-to-use.
    /// While hiding, the current hiding spot is always the target (so the player can leave).
    /// </summary>
    public class Interactor : MonoBehaviour
    {
        [SerializeField] private float range = 2.3f;
        [SerializeField] private float probeRadius = 0.08f;
        [SerializeField] private LayerMask mask = ~0;

        private PlayerContext _player;
        private bool _waitForRelease;

        public IInteractable Current { get; private set; }
        public float HoldProgress { get; private set; }

        private void Awake()
        {
            _player = GetComponent<PlayerContext>();
        }

        private void Update()
        {
            if (!GameInput.GameplayEnabled || _player.Camera == null)
            {
                Clear();
                return;
            }

            IInteractable target = _player.IsHiding ? _player.CurrentHidingSpot : Probe();
            if (target != null && !target.CanInteract(_player)) target = null;

            if (!ReferenceEquals(target, Current))
            {
                Current = target;
                HoldProgress = 0f;
            }

            if (_waitForRelease)
            {
                if (!GameInput.Interact.IsPressed()) _waitForRelease = false;
                return;
            }

            if (Current == null)
            {
                HoldProgress = 0f;
                return;
            }

            if (Current.HoldDuration <= 0f)
            {
                if (GameInput.Interact.WasPressedThisFrame()) Use();
                return;
            }

            if (GameInput.Interact.IsPressed())
            {
                HoldProgress += Time.deltaTime / Current.HoldDuration;
                if (HoldProgress >= 1f) Use();
            }
            else
            {
                HoldProgress = 0f;
            }
        }

        private void Use()
        {
            var target = Current;
            HoldProgress = 0f;
            _waitForRelease = true;
            target.Interact(_player);
        }

        private IInteractable Probe()
        {
            Transform eye = _player.Camera.transform;
            if (Physics.SphereCast(eye.position, probeRadius, eye.forward, out RaycastHit hit, range, mask, QueryTriggerInteraction.Collide))
            {
                return hit.collider.GetComponentInParent<IInteractable>();
            }
            return null;
        }

        private void Clear()
        {
            Current = null;
            HoldProgress = 0f;
        }
    }
}
