using UnityEngine;

namespace Hideout
{
    /// <summary>
    /// Owns the two global palette colors and pushes them to all shaders.
    /// Swap palette at any time — transition is automatic.
    /// Usage: PaletteManager.Instance.SetPalette(dark, light);
    /// </summary>
    public class PaletteManager : MonoBehaviour
    {
        public static PaletteManager Instance { get; private set; }

        [SerializeField] private Color colorA = new Color(0.1f, 0.08f, 0.08f);
        [SerializeField] private Color colorB = new Color(0.9f, 0.87f, 0.78f);
        [SerializeField] private float transitionSpeed = 2f;

        private Color _targetA;
        private Color _targetB;
        private Color _currentA;
        private Color _currentB;

        private static readonly int ShaderColorA = Shader.PropertyToID("_PaletteColorA");
        private static readonly int ShaderColorB = Shader.PropertyToID("_PaletteColorB");

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _currentA = colorA;
            _currentB = colorB;
            _targetA = colorA;
            _targetB = colorB;
            PushToShader();
        }

        private void Update()
        {
            _currentA = Color.Lerp(_currentA, _targetA, Time.deltaTime * transitionSpeed);
            _currentB = Color.Lerp(_currentB, _targetB, Time.deltaTime * transitionSpeed);
            PushToShader();
        }

        public void SetPalette(Color dark, Color light)
        {
            _targetA = dark;
            _targetB = light;
        }

        public void SetPaletteImmediate(Color dark, Color light)
        {
            _currentA = dark;
            _currentB = light;
            _targetA = dark;
            _targetB = light;
            PushToShader();
        }

        private void PushToShader()
        {
            Shader.SetGlobalColor(ShaderColorA, _currentA);
            Shader.SetGlobalColor(ShaderColorB, _currentB);
        }
        private void OnValidate()
        {
            _targetA = colorA;
            _targetB = colorB;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
