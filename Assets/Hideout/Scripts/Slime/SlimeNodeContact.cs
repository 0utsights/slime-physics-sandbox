using UnityEngine;

namespace Hideout.Slime
{
    /// <summary>
    /// Attached to each perimeter node.
    /// Tracks grounded state and contact normals.
    /// Applies corner escape impulse when wedged.
    /// Exposes IsGrounded to SlimeBody for damping switching.
    /// </summary>
    public class SlimeNodeContact : MonoBehaviour
    {
        public bool IsGrounded { get; private set; }
        public Vector2 ContactNormal { get; private set; }

        private SlimeBody _body;
        private Rigidbody2D _rb;
        private float _escapeForce;

        private static readonly ContactPoint2D[] _contacts = new ContactPoint2D[4];

        public void Init(SlimeBody body, float escapeForce)
        {
            _body = body;
            _escapeForce = escapeForce;
            _rb = GetComponent<Rigidbody2D>();
        }

        private void OnCollisionStay2D(Collision2D collision)
        {
            int count = collision.GetContacts(_contacts);
            IsGrounded = true;

            if (count >= 1)
                ContactNormal = _contacts[0].normal;

            // Corner escape: two diverging normals = wedged
            if (count >= 2)
            {
                Vector2 n0 = _contacts[0].normal;
                Vector2 n1 = _contacts[1].normal;
                if (Vector2.Dot(n0, n1) < 0.3f)
                {
                    Vector2 escape   = (n0 + n1).normalized;
                    Vector2 toCenter = (_body.CenterPosition - (Vector2)transform.position).normalized;
                    Vector2 dir      = (escape + toCenter * 0.5f).normalized;
                    _rb.AddForce(dir * _escapeForce, ForceMode2D.Impulse);
                }
            }
        }

        private void OnCollisionExit2D(Collision2D collision)
        {
            IsGrounded = false;
            ContactNormal = Vector2.zero;
        }
    }
}
