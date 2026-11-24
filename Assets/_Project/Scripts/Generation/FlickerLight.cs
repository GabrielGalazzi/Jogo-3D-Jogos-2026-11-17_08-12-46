using UnityEngine;

namespace MaidCafe.Generation
{
    /// <summary>Makes a light flicker irregularly (failing fluorescent / bulb).</summary>
    [RequireComponent(typeof(Light))]
    public class FlickerLight : MonoBehaviour
    {
        [SerializeField] private float minOnTime = 0.05f;
        [SerializeField] private float maxOnTime = 2.5f;
        [SerializeField] private float minOffTime = 0.03f;
        [SerializeField] private float maxOffTime = 0.35f;
        [SerializeField] private Renderer lampRenderer;

        private Light _light;
        private float _baseIntensity;
        private float _timer;
        private bool _on = true;
        private MaterialPropertyBlock _block;
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private Color _emission;

        public void SetLamp(Renderer lamp, Color emission)
        {
            lampRenderer = lamp;
            _emission = emission;
        }

        private void Awake()
        {
            _light = GetComponent<Light>();
            _baseIntensity = _light.intensity;
            _block = new MaterialPropertyBlock();
            _timer = Random.Range(minOnTime, maxOnTime);
        }

        private void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;

            _on = !_on;
            _timer = _on ? Random.Range(minOnTime, maxOnTime) : Random.Range(minOffTime, maxOffTime);
            _light.intensity = _on ? _baseIntensity * Random.Range(0.7f, 1.1f) : 0f;

            if (lampRenderer != null)
            {
                _block.SetColor(EmissionId, _on ? _emission : Color.black);
                lampRenderer.SetPropertyBlock(_block);
            }
        }
    }
}
