using UnityEngine;

namespace Hideout.Rendering
{
    /// <summary>
    /// Attach to Main Camera.
    /// Snaps every pixel on screen to either PaletteColorA or PaletteColorB
    /// based on luminance. Affects everything — sprites, tilemaps, UI, particles.
    /// Colors are driven globally by PaletteManager.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [ExecuteInEditMode]
    public class OneBitCamera : MonoBehaviour
    {
        private Material _material;

        private void OnEnable()
        {
            Shader shader = Shader.Find("Hideout/OneBit");
            if (shader != null)
                _material = new Material(shader);
            else
                Debug.LogError("[OneBitCamera] Could not find shader Hideout/OneBit");
        }

        private void OnRenderImage(RenderTexture src, RenderTexture dest)
        {
            if (_material == null) { Graphics.Blit(src, dest); return; }
            Graphics.Blit(src, dest, _material);
        }

        private void OnDisable()
        {
            if (_material != null)
                DestroyImmediate(_material);
        }
    }
}
