using System;
using MaidCafe.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MaidCafe.Player
{
    /// <summary>
    /// The player's phone: toggles night vision (post-processing + an invisible IR fill light),
    /// drains battery while on, flickers when low. Batteries are picked up in the level.
    /// </summary>
    public class PhoneController : MonoBehaviour
    {
        [SerializeField] private float secondsPerFullCharge = 110f;
        [SerializeField] private float lowBatteryThreshold = 0.15f;
        [SerializeField] private float blendSpeed = 6f;
        [SerializeField] private float irIntensity = 0.7f;
        [SerializeField] private Transform phoneModel;

        private Volume _volume;
        private Light _irLight;
        private float _flickerTimer;
        private float _flickerValue = 1f;
        private Vector3 _phoneLoweredPos = new Vector3(0.16f, -0.45f, 0.3f);
        private Vector3 _phoneRaisedPos = new Vector3(0.2f, -0.19f, 0.34f);

        public float Battery01 { get; private set; } = 1f;
        public bool NightVisionOn { get; private set; }
        public bool IsLowBattery => Battery01 <= lowBatteryThreshold;

        public event Action<bool> NightVisionChanged;
        public event Action BatteryDepleted;

        private void Start()
        {
            var cam = GetComponentInChildren<Camera>();
            BuildVolume();
            BuildIrLight(cam != null ? cam.transform : transform);
        }

        public void SetPhoneModel(Transform model) => phoneModel = model;

        public void AddBattery(float amount)
        {
            Battery01 = Mathf.Clamp01(Battery01 + amount);
        }

        public void ResetBattery(float value = 1f)
        {
            Battery01 = Mathf.Clamp01(value);
            SetNightVision(false);
        }

        public void SetNightVision(bool on)
        {
            if (on && Battery01 <= 0f) on = false;
            if (on == NightVisionOn) return;
            NightVisionOn = on;
            NightVisionChanged?.Invoke(on);
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            if (GameInput.GameplayEnabled && GameInput.Phone.WasPressedThisFrame())
            {
                SetNightVision(!NightVisionOn);
            }

            if (NightVisionOn)
            {
                Battery01 = Mathf.Max(0f, Battery01 - dt / secondsPerFullCharge);
                if (Battery01 <= 0f)
                {
                    SetNightVision(false);
                    BatteryDepleted?.Invoke();
                }
            }

            // Low battery flicker.
            float target = NightVisionOn ? 1f : 0f;
            if (NightVisionOn && IsLowBattery)
            {
                _flickerTimer -= dt;
                if (_flickerTimer <= 0f)
                {
                    _flickerTimer = UnityEngine.Random.Range(0.05f, 0.4f);
                    _flickerValue = UnityEngine.Random.value < 0.25f ? 0.25f : 1f;
                }
                target *= _flickerValue;
            }

            if (_volume != null) _volume.weight = Mathf.MoveTowards(_volume.weight, target, blendSpeed * dt);
            if (_irLight != null)
            {
                _irLight.enabled = _volume != null && _volume.weight > 0.01f;
                _irLight.intensity = irIntensity * (_volume != null ? _volume.weight : 0f);
            }

            if (phoneModel != null)
            {
                Vector3 goal = NightVisionOn ? _phoneRaisedPos : _phoneLoweredPos;
                phoneModel.localPosition = Vector3.Lerp(phoneModel.localPosition, goal, 1f - Mathf.Exp(-10f * dt));
            }
        }

        private void BuildVolume()
        {
            var go = new GameObject("NightVisionVolume");
            go.transform.SetParent(transform, false);
            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 50f;
            _volume.weight = 0f;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "NightVisionProfile (runtime)";

            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(1.3f);
            color.contrast.Override(18f);

            // Luminance routed mostly into green = classic monochrome night vision.
            var mixer = profile.Add<ChannelMixer>(true);
            mixer.redOutRedIn.Override(8f);
            mixer.redOutGreenIn.Override(16f);
            mixer.redOutBlueIn.Override(3f);
            mixer.greenOutRedIn.Override(30f);
            mixer.greenOutGreenIn.Override(59f);
            mixer.greenOutBlueIn.Override(11f);
            mixer.blueOutRedIn.Override(9f);
            mixer.blueOutGreenIn.Override(18f);
            mixer.blueOutBlueIn.Override(4f);

            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Medium4);
            grain.intensity.Override(0.75f);
            grain.response.Override(0.6f);

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.5f);
            vignette.smoothness.Override(0.5f);
            vignette.color.Override(Color.black);

            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.6f);
            bloom.threshold.Override(0.9f);

            _volume.sharedProfile = profile;
        }

        private void BuildIrLight(Transform parent)
        {
            var go = new GameObject("NightVisionFill");
            go.transform.SetParent(parent, false);
            // In front of the held phone so the phone body is not blown out by the fill light.
            go.transform.localPosition = new Vector3(0f, 0f, 0.6f);
            _irLight = go.AddComponent<Light>();
            _irLight.type = LightType.Point;
            _irLight.range = 13f;
            _irLight.color = new Color(0.85f, 1f, 0.85f);
            _irLight.shadows = LightShadows.None;
            _irLight.intensity = 0f;
            _irLight.enabled = false;
        }
    }
}
