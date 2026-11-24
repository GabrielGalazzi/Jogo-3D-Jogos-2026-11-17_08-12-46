using UnityEngine;
using UnityEngine.AI;

namespace MaidCafe.AI
{
    /// <summary>
    /// Drives the maid's Animator (Resources/Maids/MaidAnimator) from the NavMeshAgent and the AI state,
    /// and adds the porcelain-doll head twitches on top while she searches.
    /// Without an imported model it animates the greybox body procedurally (bob, lean, head twitch).
    /// </summary>
    public class MaidAnimator : MonoBehaviour
    {
        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int AnimSpeedId = Animator.StringToHash("AnimSpeed");
        private static readonly int ChasingId = Animator.StringToHash("Chasing");
        private static readonly int SearchingId = Animator.StringToHash("Searching");
        private static readonly int CatchId = Animator.StringToHash("Catch");
        private static readonly int LocomotionId = Animator.StringToHash("Locomotion");

        // Ground speed (m/s) at which the authored clips play at 1x without foot sliding (see MaidAnimations.fbx).
        private const float WalkClipSpeed = 1.1f;
        private const float RunClipSpeed = 3.94f;

        private NavMeshAgent _agent;
        private MaidAI _ai;
        private Transform _model;
        private Transform _head;
        private Animator _animator;
        private float _phase;
        private MaidState _lastState;
        private float _twitchTimer;
        private Vector3 _twitch;
        private Vector3 _twitchTarget;

        /// <summary>Uses an imported humanoid model instead of the greybox body.</summary>
        public void Bind(Animator animator, Transform head)
        {
            _animator = animator;
            _head = head;
        }

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _ai = GetComponent<MaidAI>();
            _model = transform.Find("Model");
            if (_model != null && _head == null) _head = _model.Find("Head");
        }

        private void Update()
        {
            float speed = _agent != null && _agent.enabled ? _agent.velocity.magnitude : 0f;
            var state = _ai != null ? _ai.State : MaidState.Patrol;
            bool chasing = state == MaidState.Chase;
            bool searching = state == MaidState.Search || state == MaidState.Investigate;

            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                _animator.SetFloat(SpeedId, state == MaidState.Catching ? 0f : speed, 0.12f, Time.deltaTime);
                _animator.SetFloat(AnimSpeedId, PlaybackSpeed(speed));
                _animator.SetBool(ChasingId, chasing);
                _animator.SetBool(SearchingId, searching);
                if (state == MaidState.Catching && _lastState != MaidState.Catching) _animator.SetTrigger(CatchId);
                if (state != MaidState.Catching && _lastState == MaidState.Catching)
                {
                    _animator.ResetTrigger(CatchId);
                    _animator.Play(LocomotionId, 0, 0f); // respawned/relocated after a catch
                }
                _lastState = state;
                return;
            }
            _lastState = state;
            if (_model == null) return;

            // Greybox fallback.
            _phase += Time.deltaTime * (2f + speed * 2.2f);
            float bob = speed > 0.1f ? Mathf.Abs(Mathf.Sin(_phase)) * 0.05f * Mathf.Min(speed, 3f) : 0f;
            float lean = Mathf.Clamp(speed * (chasing ? 4f : 2f), 0f, 14f);
            _model.localPosition = new Vector3(0f, bob, 0f);
            _model.localRotation = Quaternion.Euler(lean, 0f, Mathf.Sin(_phase * 0.5f) * (chasing ? 4f : 1.5f));

            if (_head != null)
            {
                float twitch = searching && Mathf.Repeat(_phase, 6f) < 0.25f ? 35f : 0f;
                _head.localRotation = Quaternion.Euler(0f, Mathf.Sin(_phase * 0.3f) * 25f * (searching ? 1f : 0.2f) + twitch, chasing ? 12f : 0f);
            }
        }

        private void LateUpdate()
        {
            if (_animator == null || _head == null || _ai == null) return;

            // Sudden doll-like head snaps while searching or investigating (applied on top of the animation).
            bool searching = _ai.State == MaidState.Search || _ai.State == MaidState.Investigate;
            _twitchTimer -= Time.deltaTime;
            if (_twitchTimer <= 0f)
            {
                _twitchTimer = Random.Range(0.6f, 2.2f);
                _twitchTarget = searching && Random.value < 0.6f
                    ? new Vector3(Random.Range(-12f, 12f), Random.Range(-35f, 35f), Random.Range(-28f, 28f))
                    : Vector3.zero;
            }
            _twitch = Vector3.Lerp(_twitch, _twitchTarget, 1f - Mathf.Exp(-Time.deltaTime * 22f));
            if (_twitch.sqrMagnitude > 0.01f)
            {
                Transform body = transform;
                _head.rotation = Quaternion.AngleAxis(_twitch.y, body.up)
                               * Quaternion.AngleAxis(_twitch.z, body.forward)
                               * Quaternion.AngleAxis(_twitch.x, body.right)
                               * _head.rotation;
            }
        }

        private static float PlaybackSpeed(float speed)
        {
            if (speed < 0.15f) return 1f;
            if (speed <= 2.6f) return Mathf.Clamp(speed / WalkClipSpeed, 0.7f, 1.8f);
            return Mathf.Clamp(speed / RunClipSpeed, 0.85f, 1.5f);
        }
    }
}
