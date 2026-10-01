using UnityEngine;
using UnityEngine.InputSystem;

namespace Hideout.Slime
{
    /// <summary>
    /// Basic player input for the slime body.
    /// Arrow keys apply horizontal movement force and a jump impulse.
    /// Uses the New Input System (Keyboard.current).
    /// </summary>
    [RequireComponent(typeof(SlimeBody))]
    public class SlimeController : MonoBehaviour
    {
        [Header("Movement")]
        public float moveForce = 12f;

        [Header("Jump")]
        public float jumpForce      = 5f;
        public float jumpCoyoteTime = 0.1f;
        public float jumpBufferTime = 0.12f;

        private SlimeBody _body;
        private float     _coyoteTimer;
        private float     _jumpBufferTimer;
        private bool _jumpConsumed;
        private bool _wasSupported;

        private void Awake()
        {
            _body = GetComponent<SlimeBody>();
        }

        private void Update()
        {
            bool supported = _body.HasSupport;
            if (supported && !_wasSupported) _jumpConsumed = false;
            _wasSupported = supported;
            if (supported && !_jumpConsumed)
                _coyoteTimer = jumpCoyoteTime;
            else
                _coyoteTimer -= Time.deltaTime;

            // Buffer jump in Update so FixedUpdate never misses a press between steps
            _jumpBufferTimer = Mathf.Max(0f, _jumpBufferTimer - Time.deltaTime);
            var kb = Keyboard.current;
            if (kb != null && (kb.upArrowKey.wasPressedThisFrame ||
                kb.wKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
                _jumpBufferTimer = jumpBufferTime;
        }

        private void FixedUpdate()
        {
            var kb = Keyboard.current;
            if (kb == null) { _body.SetCrouching(false); return; }

            float horizontal = 0f;
            if (kb.leftArrowKey.isPressed || kb.aKey.isPressed) horizontal -= 1f;
            if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) horizontal += 1f;

            if (horizontal != 0f)
                _body.AddMovementForce(new Vector2(horizontal * moveForce, 0f));

            if (_jumpBufferTimer > 0f && _coyoteTimer > 0f)
            {
                _body.AddImpulse(Vector2.up * jumpForce);
                _coyoteTimer = 0f;
                _jumpBufferTimer = 0f;
                _jumpConsumed = true;
            }

            // Crouch: down arrow. SetCrouching handles force + stiffness each frame.
            _body.SetCrouching(kb.downArrowKey.isPressed || kb.sKey.isPressed);
        }

        public void ResetInputState()
        {
            _coyoteTimer = 0f;
            _jumpBufferTimer = 0f;
            _jumpConsumed = false;
            _wasSupported = false;
        }
    }
}
