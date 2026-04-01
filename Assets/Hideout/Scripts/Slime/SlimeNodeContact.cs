using UnityEngine;

namespace Hideout.Slime
{
    /// <summary>
    /// Attached to each perimeter node. Detects corner trapping via
    /// diverging contact normals and applies an escape impulse.
    /// </summary>
    public class SlimeNodeContact : MonoBehaviour
    {
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
            if (count < 2) return;

            Vector2 n0 = _contacts[0].normal;
            Vector2 n1 = _contacts[1].normal;

            // Two normals diverging = wedged in a corner
            if (Vector2.Dot(n0, n1) < 0.3f)
            {
                Vector2 escape  = (n0 + n1).normalized;
                Vector2 toCenter = (_body.CenterPosition - (Vector2)transform.position).normalized;
                Vector2 dir     = (escape + toCenter * 0.5f).normalized;
                _rb.AddForce(dir * _escapeForce, ForceMode2D.Impulse);
            }
        }
    }
}
