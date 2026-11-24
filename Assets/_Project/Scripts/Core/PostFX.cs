using MaidCafe.AI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MaidCafe.Core
{
    /// <summary>
    /// Global post-processing built in code: a base grade (brightness setting, vignette, light grain,
    /// bloom) and a "chase" layer (red vignette + chromatic aberration pulse while a maid is chasing).
    /// </summary>
    public class PostFX : MonoBehaviour
    {
        private ColorAdjustments _baseColor;
        private Volume _chase;
        private float _chaseWeight;

        private void Awake()
        {
            var baseVolume = CreateVolume("BaseGrade", 0f, out var baseProfile);
            baseVolume.weight = 1f;
            var tone = baseProfile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral);
            _baseColor = baseProfile.Add<ColorAdjustments>(true);
            _baseColor.postExposure.Override(GameSettings.Brightness);
            _baseColor.contrast.Override(10f);
            _baseColor.saturation.Override(-12f);
            var vignette = baseProfile.Add<Vignette>(true);
            vignette.intensity.Override(0.34f);
            vignette.smoothness.Override(0.45f);
            var grain = baseProfile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Thin2);
            grain.intensity.Override(0.3f);
            var bloom = baseProfile.Add<Bloom>(true);
            bloom.intensity.Override(0.5f);
            bloom.threshold.Override(1f);

            _chase = CreateVolume("ChaseFX", 20f, out var chaseProfile);
            _chase.weight = 0f;
            var chaseVignette = chaseProfile.Add<Vignette>(true);
            chaseVignette.color.Override(new Color(0.45f, 0f, 0.03f));
            chaseVignette.intensity.Override(0.5f);
            chaseVignette.smoothness.Override(0.6f);
            var aberration = chaseProfile.Add<ChromaticAberration>(true);
            aberration.intensity.Override(0.45f);
        }

        private Volume CreateVolume(string name, float priority, out VolumeProfile profile)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = priority;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = name + " (runtime)";
            volume.sharedProfile = profile;
            return volume;
        }

        private void Update()
        {
            _baseColor.postExposure.value = GameSettings.Brightness;

            float target = MaidAI.AnyChasing ? 0.75f + 0.25f * Mathf.Sin(Time.time * 7f) : 0f;
            _chaseWeight = Mathf.MoveTowards(_chaseWeight, target, Time.deltaTime * 2.5f);
            _chase.weight = _chaseWeight;
        }
    }
}
