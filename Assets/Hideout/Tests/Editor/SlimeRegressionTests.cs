using NUnit.Framework;
using UnityEngine;
using Hideout.Slime;

namespace Hideout.Tests
{
    public class SlimeRegressionTests
    {
        [TestCase(6)]
        [TestCase(12)]
        [TestCase(24)]
        public void CounterclockwisePerimeterNormalsPointOutward(int count)
        {
            for (int i = 0; i < count; i++)
            {
                float a = 2f * Mathf.PI * i / count;
                float b = 2f * Mathf.PI * (i + 1) / count;
                Vector2 from = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 to = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
                Vector2 normal = SlimeMath.OutwardNormal(to - from);
                Assert.That(Vector2.Dot(normal, (from + to) * 0.5f), Is.GreaterThan(0f));
            }
        }

        [Test]
        public void ImmediatePalettePreservesBothRequestedColors()
        {
            Color oldA = Shader.GetGlobalColor("_PaletteColorA");
            Color oldB = Shader.GetGlobalColor("_PaletteColorB");
            var obj = new GameObject("Palette test");
            try
            {
                var palette = obj.AddComponent<PaletteManager>();
                palette.SetPaletteImmediate(Color.black, Color.white);
                Assert.That(Shader.GetGlobalColor("_PaletteColorA"), Is.EqualTo(Color.black));
                Assert.That(Shader.GetGlobalColor("_PaletteColorB"), Is.EqualTo(Color.white));
            }
            finally
            {
                Object.DestroyImmediate(obj);
                Shader.SetGlobalColor("_PaletteColorA", oldA);
                Shader.SetGlobalColor("_PaletteColorB", oldB);
            }
        }
    }
}
