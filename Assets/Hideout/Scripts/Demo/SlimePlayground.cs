using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Hideout.Slime;

namespace Hideout.Demo
{
    /// <summary>A small runtime playground for the existing SampleScene.</summary>
    public sealed class SlimePlayground : MonoBehaviour
    {
        private static readonly Color Ink = new Color(0.055f, 0.11f, 0.095f);
        private static readonly Color Paper = new Color(0.93f, 0.91f, 0.82f);
        private static readonly Vector2 Spawn = new Vector2(-9f, 0f);
        private SlimeBody _slime;
        private Camera _camera;
        private Sprite _square;
        private Texture2D _texture;
        private GameObject _leftEye, _rightEye;
        private float _startedAt;
        private float _finishTime;
        private bool _finished;
        private float _previousStep;
        private int _previousVelocityIterations, _previousPositionIterations;
        private GUIStyle _title, _body, _small;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (SceneManager.GetActiveScene().name != "SampleScene" ||
                Object.FindFirstObjectByType<SlimePlayground>() != null) return;
            new GameObject("Slime Playground").AddComponent<SlimePlayground>();
        }

        private void Start()
        {
            _slime = Object.FindFirstObjectByType<SlimeBody>();
            _camera = Camera.main;
            if (_slime == null || _camera == null) { enabled = false; return; }
            _previousStep = Time.fixedDeltaTime;
            _previousVelocityIterations = Physics2D.velocityIterations;
            _previousPositionIterations = Physics2D.positionIterations;
            Time.fixedDeltaTime = 0.01f;
            Physics2D.velocityIterations = 16;
            Physics2D.positionIterations = 8;

            // Keep the original scene and physics system intact in source.
            foreach (var sprite in Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
                if (sprite.gameObject.name == "Square" || sprite.gameObject.name == "Square (1)")
                    sprite.gameObject.SetActive(false);

            _camera.transform.position = new Vector3(0f, 3f, -10f);
            _camera.orthographic = true;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.black;
            PaletteManager.Instance?.SetPaletteImmediate(Ink, Paper);

            _texture = new Texture2D(1, 1);
            _texture.SetPixel(0, 0, Color.white);
            _texture.Apply();
            _square = Sprite.Create(_texture, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);

            Block("Floor", new Vector2(0, -1.5f), new Vector2(24, 1));
            Block("Landing", new Vector2(-5, -0.3f), new Vector2(2.5f, 1.4f));
            Block("Slope", new Vector2(-2.7f, -0.6f), new Vector2(2.5f, 0.35f), -18f);
            Block("Low passage", new Vector2(0.5f, 0.05f), new Vector2(2.5f, 0.5f));
            Block("Step", new Vector2(4.6f, -0.4f), new Vector2(2f, 1.2f));
            Block("Finish platform", new Vector2(8, 0f), new Vector2(2.5f, 2f));
            Block("Left boundary", new Vector2(-12, 1), new Vector2(0.5f, 5));
            Block("Right boundary", new Vector2(12, 1), new Vector2(0.5f, 5));
            Block("Finish post", new Vector2(8.8f, 1.7f), new Vector2(0.06f, 1.4f), solid: false);
            Block("Finish flag", new Vector2(9.05f, 2.25f), new Vector2(0.5f, 0.35f), solid: false);
            _leftEye = Block("Left eye", Spawn, new Vector2(0.045f, 0.075f), solid: false);
            _rightEye = Block("Right eye", Spawn, new Vector2(0.045f, 0.075f), solid: false);
            foreach (var eye in new[] { _leftEye, _rightEye })
            {
                var renderer = eye.GetComponent<SpriteRenderer>();
                renderer.color = Color.black;
                renderer.sortingOrder = 2;
            }
            ResetRun();
        }

        private GameObject Block(string label, Vector2 position, Vector2 size, float rotation = 0f, bool solid = true)
        {
            var obj = new GameObject(label);
            obj.transform.SetParent(transform);
            obj.transform.position = position;
            obj.transform.localScale = new Vector3(size.x, size.y, 1);
            obj.transform.rotation = Quaternion.Euler(0, 0, rotation);
            var renderer = obj.AddComponent<SpriteRenderer>();
            renderer.sprite = _square;
            renderer.color = Color.white;
            renderer.sortingOrder = -1;
            if (solid) obj.AddComponent<BoxCollider2D>();
            return obj;
        }

        private void LateUpdate()
        {
            if (_slime == null || _leftEye == null) return;
            Vector2 center = _slime.CenterPosition;
            float look = Mathf.Clamp(_slime.Velocity.x * 0.01f, -0.03f, 0.03f);
            _leftEye.transform.position = new Vector3(center.x - 0.12f + look, center.y + 0.1f, -0.1f);
            _rightEye.transform.position = new Vector3(center.x + 0.12f + look, center.y + 0.1f, -0.1f);
        }

        private void Update()
        {
            if (_slime == null || _camera == null) return;
            _camera.orthographicSize = Mathf.Max(6f, 12.5f / Mathf.Max(0.1f, _camera.aspect));
            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame) ResetRun();
            Vector2 position = _slime.CenterPosition;
            if (position.y < -5f || float.IsNaN(position.x) || float.IsNaN(position.y) ||
                float.IsInfinity(position.x) || float.IsInfinity(position.y)) ResetRun();
            if (!_finished && position.x > 7.2f && position.x < 9.5f &&
                position.y > 1.15f && position.y < 3f && _slime.HasSupport)
            {
                _finished = true;
                _finishTime = Time.time - _startedAt;
            }
        }

        private void ResetRun()
        {
            _slime.ResetPose(Spawn);
            _startedAt = Time.time;
            _finished = false;
        }

        private void OnGUI()
        {
            if (_slime == null || _camera == null) return;
            if (_title == null)
            {
                _title = new GUIStyle(GUI.skin.label) { fontSize = 38, fontStyle = FontStyle.Bold };
                _body = new GUIStyle(GUI.skin.label) { fontSize = 18 };
                _small = new GUIStyle(GUI.skin.label) { fontSize = 13 };
                _title.normal.textColor = _body.normal.textColor = _small.normal.textColor = Paper;
            }
            Matrix4x4 previous = GUI.matrix;
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1280 * scale) / 2,
                (Screen.height - 720 * scale) / 2, 0), Quaternion.identity, Vector3.one * scale);
            GUI.Label(new Rect(44, 26, 900, 28), "HIDEOUT  /  UNITY PHYSICS PLAYGROUND", _small);
            GUI.Label(new Rect(42, 58, 900, 58), "Small body. Big possibilities.", _title);
            GUI.Label(new Rect(44, 121, 960, 32), "Jump, land, and squeeze through a world in two colors.", _body);
            GUI.Label(new Rect(44, 655, 1040, 34), "A / D  MOVE     SPACE  JUMP     S  SQUEEZE     R  RESET", _body);
            float seconds = _finished ? _finishTime : Time.time - _startedAt;
            GUI.Label(new Rect(1080, 34, 160, 32), seconds.ToString("0.0") + " s", _body);
            if (_finished)
            {
                GUI.Label(new Rect(44, 182, 1000, 40), "Made it! Explore some more, or press R for another run.", _body);
            }
            GUI.matrix = previous;
        }

        private void OnDestroy()
        {
            if (_previousStep > 0f)
            {
                Time.fixedDeltaTime = _previousStep;
                Physics2D.velocityIterations = _previousVelocityIterations;
                Physics2D.positionIterations = _previousPositionIterations;
            }
            if (_square != null) Destroy(_square);
            if (_texture != null) Destroy(_texture);
        }
    }
}
