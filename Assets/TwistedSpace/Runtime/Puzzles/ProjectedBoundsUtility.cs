using UnityEngine;

namespace TwistedSpace.Puzzles
{
    /// <summary>
    /// Allocation-free, approximate screen-space geometry, NOT exact silhouettes or occlusion.
    /// Every corner of every renderer's world AABB is projected using the mono center eye.
    /// Rotated/concave meshes therefore have conservative, sometimes loose screen rectangles.
    /// </summary>
    public static class ProjectedBoundsUtility
    {
        public static bool TryProject(Camera camera, Renderer[] renderers,
            float minimumViewportSize, out Rect viewportBounds)
        {
            viewportBounds = default;
            if (!camera || renderers == null || renderers.Length == 0)
                return false;

            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            float nearPlane = camera.nearClipPlane + 0.0001f;

            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Renderer targetRenderer = renderers[rendererIndex];
                if (!targetRenderer || !targetRenderer.enabled || targetRenderer.forceRenderingOff ||
                    !targetRenderer.gameObject.activeInHierarchy ||
                    (camera.cullingMask & (1 << targetRenderer.gameObject.layer)) == 0)
                    return false;

                Bounds bounds = targetRenderer.bounds;
                Vector3 center = bounds.center;
                Vector3 extents = bounds.extents;
                for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++)
                {
                    Vector3 corner = center + new Vector3(
                        (cornerIndex & 1) == 0 ? -extents.x : extents.x,
                        (cornerIndex & 2) == 0 ? -extents.y : extents.y,
                        (cornerIndex & 4) == 0 ? -extents.z : extents.z);
                    Vector3 point = camera.WorldToViewportPoint(corner, Camera.MonoOrStereoscopicEye.Mono);

                    // Reject rather than clamp: offscreen slivers or a near-plane crossing
                    // must not masquerade as a perfect overlap. Full AABB visibility is required.
                    if (!IsFinite(point.x) || !IsFinite(point.y) || !IsFinite(point.z) ||
                        point.z <= nearPlane || point.z >= camera.farClipPlane ||
                        point.x < 0f || point.x > 1f || point.y < 0f || point.y > 1f)
                        return false;

                    minX = Mathf.Min(minX, point.x);
                    minY = Mathf.Min(minY, point.y);
                    maxX = Mathf.Max(maxX, point.x);
                    maxY = Mathf.Max(maxY, point.y);
                }
            }

            float minimumSize = Mathf.Max(0.00001f, minimumViewportSize);
            if (maxX - minX < minimumSize || maxY - minY < minimumSize)
                return false;

            viewportBounds = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return true;
        }

        /// <summary>
        /// Common intersection / enclosing rectangle of ALL targets. The enclosing rectangle
        /// is an upper bound on their union, so this score never exceeds true multi-way IoU.
        /// It is intentionally conservative, permutation invariant and NOT an anchor average.
        /// A single misplaced target reduces (or eliminates) the common intersection.
        /// </summary>
        public static float ConservativeOverlap(Rect[] rectangles)
        {
            if (rectangles == null || rectangles.Length < 2)
                return 0f;

            float intersectionMinX = float.NegativeInfinity;
            float intersectionMinY = float.NegativeInfinity;
            float intersectionMaxX = float.PositiveInfinity;
            float intersectionMaxY = float.PositiveInfinity;
            float unionMinX = float.PositiveInfinity;
            float unionMinY = float.PositiveInfinity;
            float unionMaxX = float.NegativeInfinity;
            float unionMaxY = float.NegativeInfinity;

            for (int i = 0; i < rectangles.Length; i++)
            {
                Rect rectangle = rectangles[i];
                if (!IsFinite(rectangle.xMin) || !IsFinite(rectangle.yMin) ||
                    !IsFinite(rectangle.xMax) || !IsFinite(rectangle.yMax) ||
                    rectangle.width <= 0f || rectangle.height <= 0f)
                    return 0f;
                intersectionMinX = Mathf.Max(intersectionMinX, rectangle.xMin);
                intersectionMinY = Mathf.Max(intersectionMinY, rectangle.yMin);
                intersectionMaxX = Mathf.Min(intersectionMaxX, rectangle.xMax);
                intersectionMaxY = Mathf.Min(intersectionMaxY, rectangle.yMax);
                unionMinX = Mathf.Min(unionMinX, rectangle.xMin);
                unionMinY = Mathf.Min(unionMinY, rectangle.yMin);
                unionMaxX = Mathf.Max(unionMaxX, rectangle.xMax);
                unionMaxY = Mathf.Max(unionMaxY, rectangle.yMax);
            }

            float intersection = Mathf.Max(0f, intersectionMaxX - intersectionMinX) *
                Mathf.Max(0f, intersectionMaxY - intersectionMinY);
            float enclosingArea = (unionMaxX - unionMinX) * (unionMaxY - unionMinY);
            return IsFinite(enclosingArea) && enclosingArea > 0f
                ? Mathf.Clamp01(intersection / enclosingArea) : 0f;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
