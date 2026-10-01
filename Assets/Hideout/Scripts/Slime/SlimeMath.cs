using UnityEngine;

namespace Hideout.Slime
{
    public static class SlimeMath
    {
        // The perimeter is initialized in counterclockwise order.
        public static Vector2 OutwardNormal(Vector2 edge) =>
            new Vector2(edge.y, -edge.x).normalized;
    }
}
