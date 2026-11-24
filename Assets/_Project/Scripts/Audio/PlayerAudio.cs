using MaidCafe.Core;
using MaidCafe.Player;
using UnityEngine;

namespace MaidCafe.Audio
{
    /// <summary>
    /// Sounds of the player's own body. They are always heard directly (no occlusion) but still pick up the traced
    /// room reverb.
    /// - Footsteps per gait (crouch / walk / sprint) and floor surface (wood or tile, from the floor's material).
    /// - Cloth rustle when crouching down or standing up.
    /// - Breathing: a scared inhale/exhale loop while hidden, a sharp breath in when holding it, a breath out when
    ///   letting go, the gasp when it runs out (see <see cref="BreathController"/>); fast breathing that grows louder
    ///   as stamina drains while sprinting, and panting once exhausted until stamina comes back.
    /// None of these are heard by the maids: their hearing uses <see cref="NoiseSystem"/> and the breathing rule in
    /// MaidAI, so the sounds only tell the player what the body is doing.
    /// </summary>
    public sealed class PlayerAudio : MonoBehaviour
    {
        private const float HideBreathVolume = 0.32f;
        private const float RunBreathMinVolume = 0.12f;
        private const float RunBreathMaxVolume = 0.5f;
        private const float ExhaustedVolume = 0.62f;
        private const float BreathFadePerSecond = 1.2f;

        /// <summary>Fatigue (1 - stamina) at which the sprint breathing becomes audible.</summary>
        private const float RunBreathFromFatigue = 0.3f;

        /// <summary>Floor materials (MaterialLibrary names, see FloorBuilder.Palette) that sound like tiles or concrete.</summary>
        private static readonly string[] HardFloors = { "Floor_Dining", "Floor_Kitchen", "Floor_Restroom", "Floor_Storage", "CarFloor" };

        private enum BreathMode
        {
            None,
            Hiding,
            Running,
            Exhausted
        }

        private PlayerContext _player;
        private PlayerMotor _motor;
        private BreathController _breath;
        private RaytracedSource _feet;
        private RaytracedSource _mouth;
        private RaytracedSource _breathLoop;

        private BreathMode _loopMode;
        private float _loopVolume;
        private bool _wasHolding;
        private bool _wasCrouching;
        private bool _gaspedThisFrame;
        private float _fatigueHold;
        private Collider _lastFloor;
        private bool _lastFloorHard;

        private void Awake()
        {
            _player = GetComponent<PlayerContext>();
            _motor = GetComponent<PlayerMotor>();
            _breath = GetComponent<BreathController>();
            _feet = RaytracedSource.Create(transform, "Footsteps", new Vector3(0f, 0.05f, 0f), 20f, occlude: false);
            _mouth = RaytracedSource.Create(transform, "Breath", new Vector3(0f, 1.5f, 0.1f), 12f, occlude: false);
            _breathLoop = RaytracedSource.Create(transform, "BreathLoop", new Vector3(0f, 1.5f, 0.1f), 12f, occlude: false);
        }

        private void OnEnable()
        {
            if (_motor != null) _motor.Footstep += OnFootstep;
            if (_breath != null) _breath.Gasped += OnGasp;
        }

        private void OnDisable()
        {
            if (_motor != null) _motor.Footstep -= OnFootstep;
            if (_breath != null) _breath.Gasped -= OnGasp;
            _breathLoop.Stop();
            _loopMode = BreathMode.None;
            _loopVolume = 0f;
        }

        private void Update()
        {
            if (_player == null || _motor == null) return;
            UpdateCrouch();
            UpdateHeldBreath();
            UpdateBreathLoop(Time.deltaTime);
            _gaspedThisFrame = false;
        }

        // ------------------------------------------------------------------ footsteps

        /// <param name="noiseRadius">The step's noise radius: ~1.5 crouching, ~5 walking, ~13 sprinting.</param>
        private void OnFootstep(float noiseRadius)
        {
            bool hard = FloorIsHard();
            Sfx id;
            float volume;
            if (_motor.IsCrouching)
            {
                id = Sfx.FootstepCrouch;
                volume = 0.2f;
            }
            else if (_motor.IsSprinting)
            {
                id = hard ? Sfx.FootstepRunTile : Sfx.FootstepRunWood;
                volume = 0.55f;
            }
            else
            {
                id = hard ? Sfx.FootstepWalkTile : Sfx.FootstepWalkWood;
                volume = 0.34f;
            }
            _feet.Source.pitch = Random.Range(0.95f, 1.05f);
            _feet.PlayOneShot(SoundBank.Get(id), volume * Random.Range(0.88f, 1f));
        }

        /// <summary>True when the floor under the player is tiled or concrete (checked by its material, cached per collider).</summary>
        private bool FloorIsHard()
        {
            if (!Physics.Raycast(transform.position + Vector3.up * 0.3f, Vector3.down, out var hit, 1f,
                    Layers.SolidExceptMaids, QueryTriggerInteraction.Ignore))
            {
                return false;
            }
            if (hit.collider == _lastFloor) return _lastFloorHard;
            _lastFloor = hit.collider;
            var floorRenderer = hit.collider.GetComponent<Renderer>();
            string material = floorRenderer != null && floorRenderer.sharedMaterial != null ? floorRenderer.sharedMaterial.name : "";
            _lastFloorHard = false;
            foreach (string prefix in HardFloors)
            {
                if (material.StartsWith(prefix))
                {
                    _lastFloorHard = true;
                    break;
                }
            }
            return _lastFloorHard;
        }

        private void UpdateCrouch()
        {
            bool crouching = _motor.IsCrouching;
            if (crouching != _wasCrouching && !_player.IsHiding) _mouth.PlayOneShot(SoundBank.Get(Sfx.CrouchCloth), 0.28f);
            _wasCrouching = crouching;
        }

        // ------------------------------------------------------------------ breathing

        private void UpdateHeldBreath()
        {
            bool holding = _player.IsHoldingBreath;
            if (holding && !_wasHolding)
            {
                _mouth.PlayOneShot(SoundBank.Get(Sfx.BreathHoldStart), 0.42f);
            }
            else if (!holding && _wasHolding && _player.IsHiding && !_gaspedThisFrame)
            {
                _mouth.PlayOneShot(SoundBank.Get(Sfx.BreathRelease), 0.36f);
            }
            _wasHolding = holding;
        }

        private void OnGasp()
        {
            _gaspedThisFrame = true;
            _mouth.PlayOneShot(SoundBank.Get(Sfx.Gasp), 0.8f);
        }

        /// <summary>
        /// One looping breath at a time. The wanted loop fades in; a different one first fades the current loop out.
        /// Holding the breath silences the hiding loop at once (the breath-in one-shot covers the cut).
        /// </summary>
        private void UpdateBreathLoop(float dt)
        {
            float fatigue = 1f - _motor.Stamina01;
            // Keep the sprint breathing going for a moment after the player stops running.
            if (_motor.IsSprinting && _motor.HorizontalSpeed > 0.5f) _fatigueHold = 1.5f;
            else _fatigueHold = Mathf.Max(0f, _fatigueHold - dt);

            BreathMode want = BreathMode.None;
            float wantVolume = 0f;
            if (_player.IsHiding)
            {
                if (!_player.IsHoldingBreath)
                {
                    want = BreathMode.Hiding;
                    wantVolume = HideBreathVolume;
                }
            }
            else if (_motor.IsExhausted)
            {
                want = BreathMode.Exhausted;
                wantVolume = ExhaustedVolume;
            }
            else if (_fatigueHold > 0f && fatigue > RunBreathFromFatigue)
            {
                want = BreathMode.Running;
                float k = Mathf.InverseLerp(RunBreathFromFatigue, 1f, fatigue);
                wantVolume = Mathf.Lerp(RunBreathMinVolume, RunBreathMaxVolume, k);
            }

            if (_player.IsHoldingBreath && _loopMode == BreathMode.Hiding) _loopVolume = 0f;

            if (want != _loopMode)
            {
                // Fade the current loop out first; switch when it is silent.
                _loopVolume = Mathf.MoveTowards(_loopVolume, 0f, BreathFadePerSecond * 2f * dt);
                if (_loopVolume <= 0f)
                {
                    _breathLoop.Stop();
                    _loopMode = want;
                    var clip = SoundBank.Get(LoopSfx(want));
                    if (want != BreathMode.None && clip != null)
                    {
                        _breathLoop.Play(clip, 0f, loop: true, startTime: Random.Range(0f, clip.length));
                    }
                }
            }
            else
            {
                _loopVolume = Mathf.MoveTowards(_loopVolume, wantVolume, BreathFadePerSecond * dt);
            }
            _breathLoop.BaseVolume = _loopVolume;
        }

        private static Sfx LoopSfx(BreathMode mode)
        {
            switch (mode)
            {
                case BreathMode.Hiding: return Sfx.BreathHideLoop;
                case BreathMode.Exhausted: return Sfx.BreathExhaustedLoop;
                default: return Sfx.BreathRunLoop;
            }
        }
    }
}
