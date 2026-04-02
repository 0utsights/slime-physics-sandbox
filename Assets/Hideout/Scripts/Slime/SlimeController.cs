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

        private SlimeBody _body;
        private float     _coyoteTimer;
        private bool      _jumpBuffered;

        private void Awake()
        {
            _body = GetComponent<SlimeBody>();
        }

        private void Update()
        {
            if (_body.GroundedRatio > 0.1f)
                _coyoteTimer = jumpCoyoteTime;
            else
                _coyoteTimer -= Time.deltaTime;

            // Buffer jump in Update so FixedUpdate never misses a press between steps
            if (Keyboard.current.upArrowKey.wasPressedThisFrame)
                _jumpBuffered = true;
        }

        private void FixedUpdate()
        {
            var kb = Keyboard.current;

            float horizontal = 0f;
            if (kb.leftArrowKey.isPressed)  horizontal = -1f;
            if (kb.rightArrowKey.isPressed) horizontal =  1f;

            if (horizontal != 0f)
                _body.AddMovementForce(new Vector2(horizontal * moveForce, 0f));

            if (_jumpBuffered && _coyoteTimer > 0f)
            {
                _body.AddImpulse(Vector2.up * jumpForce);
                _coyoteTimer = 0f;
            }

            _jumpBuffered = false;

            // Crouch: down arrow. SetCrouching handles force + stiffness each frame.
            _body.SetCrouching(kb.downArrowKey.isPressed);
        }
    }
}
