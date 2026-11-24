using MaidCafe.Core;
using UnityEngine;

namespace MaidCafe.Player
{
    /// <summary>
    /// Hold breath while hiding (Space / RB). Limited breath; running out causes a loud gasp that
    /// nearby maids can hear. Searching maids next to your hiding spot hear you if you are not holding it.
    /// </summary>
    public class BreathController : MonoBehaviour
    {
        [SerializeField] private float holdSeconds = 6f;
        [SerializeField] private float recoverSeconds = 4f;
        [SerializeField] private float gaspNoise = 7f;
        [SerializeField] private float gaspLockout = 2f;

        private PlayerContext _player;
        private float _lockout;

        public float Breath01 { get; private set; } = 1f;

        /// <summary>Raised when the player runs out of breath and gasps.</summary>
        public event System.Action Gasped;

        private void Awake()
        {
            _player = GetComponent<PlayerContext>();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _lockout = Mathf.Max(0f, _lockout - dt);

            bool wants = _player.IsHiding && GameInput.GameplayEnabled && GameInput.HoldBreath.IsPressed();
            bool holding = wants && _lockout <= 0f && Breath01 > 0f;
            _player.IsHoldingBreath = holding;

            if (holding)
            {
                Breath01 = Mathf.Max(0f, Breath01 - dt / holdSeconds);
                if (Breath01 <= 0f)
                {
                    _lockout = gaspLockout;
                    _player.IsHoldingBreath = false;
                    NoiseSystem.Emit(transform.position, gaspNoise, NoiseType.Interaction);
                    Gasped?.Invoke();
                }
            }
            else
            {
                Breath01 = Mathf.Min(1f, Breath01 + dt / recoverSeconds);
            }
        }
    }
}
