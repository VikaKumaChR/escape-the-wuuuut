using UnityEngine;

namespace DepthLab
{
    [AddComponentMenu("DepthLab/Cyberpunk Hologram")]
    [DisallowMultipleComponent]
    public class DepthLabCyberpunkHologram : UnityEngine.UI.MaskableGraphic
    {
        private const int NodeCount = 180;
        private const int ConnectionCount = 239;
        private const int ContourCount = 88;
        private const int VertexBudget = 5000;
        private const int DiscSides = 6;
        private const int RingSegments = 48;
        private const int ArcSegments = 20;
        private const double RefreshInterval = 0.05;
        private static readonly Vector2 DesignSize = new Vector2(650f, 740f);
        private static readonly Color32 Cyan = new Color32(0x00, 0xE5, 0xFF, 255);
        private static readonly Color32 Blue = new Color32(0x16, 0x77, 0xFF, 255);
        private static readonly Color32 Purple = new Color32(0xA8, 0x55, 0xF7, 255);
        private static readonly Color32 Magenta = new Color32(0xFF, 0x35, 0xB8, 255);

        private struct Node
        {
            public Vector2 Point;
            public float Radius;
            public float Phase;
            public byte Palette;
        }

        private struct Connection
        {
            public int From;
            public int To;
        }

        private struct Contour
        {
            public Vector2 From;
            public Vector2 To;
            public byte Palette;
        }

        private Node[] nodes;
        private Connection[] connections;
        private Contour[] contours;
        private Vector2[] discDirections;
        private Vector2[] ringDirections;
        private double nextRefresh;
        private float animationTime;
        private Rect drawingRect;
        private Vector2 drawingScale;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
            EnsureGeometry();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            raycastTarget = false;
            EnsureGeometry();
            SampleAnimation(Time.unscaledTimeAsDouble);
            SetVerticesDirty();
        }

#if UNITY_EDITOR
        protected override void Reset()
        {
            base.Reset();
            raycastTarget = false;
        }
#endif

        private void Update()
        {
            if (!isActiveAndEnabled)
                return;

            double now = Time.unscaledTimeAsDouble;
            if (now < nextRefresh)
                return;

            SampleAnimation(now);
            SetVerticesDirty();
        }

        private void SampleAnimation(double now)
        {
            // A bounded phase preserves float precision during long-running installations.
            animationTime = (float)(now % 1200.0);
            nextRefresh = now + RefreshInterval;
        }

        private void EnsureGeometry()
        {
            if (nodes != null)
                return;

            nodes = new Node[NodeCount];
            connections = new Connection[ConnectionCount];
            contours = new Contour[ContourCount];
            discDirections = new Vector2[DiscSides];
            ringDirections = new Vector2[RingSegments];
            for (int i = 0; i < DiscSides; i++)
            {
                float angle = i * Mathf.PI * 2f / DiscSides;
                discDirections[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }
            for (int i = 0; i < RingSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / RingSegments;
                ringDirections[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }

            int edge = 0;
            int contour = 0;
            for (int hemisphere = 0; hemisphere < 2; hemisphere++)
            {
                float side = hemisphere == 0 ? -1f : 1f;
                int start = hemisphere * 70;
                for (int row = 0; row < 7; row++)
                {
                    float y = 340f + row * 51f;
                    float latitude = (y - 505f) / 183f;
                    float width = 242f * Mathf.Sqrt(1f - latitude * latitude);
                    for (int column = 0; column < 10; column++)
                    {
                        int index = start + row * 10 + column;
                        float lane = column / 9f;
                        float fold = Mathf.Sin(column * 0.9f + row * 1.35f + hemisphere * 0.6f);
                        Vector2 point = new Vector2(side * (18f + width * lane),
                            y + fold * 6f * Mathf.Sin(lane * Mathf.PI));
                        SetNode(index, point, column == 0 ? (byte)2 : (byte)((column + row) % 3));
                        if (column > 0)
                            Connect(ref edge, index - 1, index);
                        if (row > 0 && column % 2 == 0)
                            Connect(ref edge, index - 10, index);
                    }
                }

                for (int i = 0; i < 32; i++)
                {
                    contours[contour++] = new Contour
                    {
                        From = HemisphereOutline(side, i * Mathf.PI / 32f),
                        To = HemisphereOutline(side, (i + 1) * Mathf.PI / 32f),
                        Palette = (byte)hemisphere
                    };
                }
                for (int i = 0; i < 12; i++)
                {
                    contours[contour++] = new Contour
                    {
                        From = FissurePoint(side, i / 12f),
                        To = FissurePoint(side, (i + 1) / 12f),
                        Palette = 2
                    };
                }
            }

            for (int i = 0; i < 16; i++)
            {
                Vector2 point = new Vector2(Mathf.Sin(i * 0.6f) * 7f, 304f - i * 16.4f);
                SetNode(140 + i, point, (byte)(i % 2 == 0 ? 0 : 2));
                if (i > 0)
                    Connect(ref edge, 139 + i, 140 + i);
            }
            Connect(ref edge, 0, 140);
            Connect(ref edge, 70, 140);

            for (int branch = 0; branch < 12; branch++)
            {
                float side = branch % 2 == 0 ? -1f : 1f;
                int level = branch / 2;
                int root = 144 + level * 2;
                int first = 156 + branch * 2;
                Vector2 origin = nodes[root].Point;
                SetNode(first, new Vector2(side * (46f + level * 3f), origin.y - 12f), 1);
                SetNode(first + 1, new Vector2(side * (126f - level * 5f), origin.y - 35f), 3);
                Connect(ref edge, root, first);
                Connect(ref edge, first, first + 1);
                // Short collaterals join adjacent levels, never cross the spinal midline.
                if (level > 0)
                    Connect(ref edge, first - 4, first);
                else
                    Connect(ref edge, 140, first);
            }
        }

        private void SetNode(int index, Vector2 point, byte palette)
        {
            // Index hashing provides repeatable variation without touching Random's global state.
            uint hash = unchecked((uint)(index + 1) * 2654435761u);
            nodes[index] = new Node
            {
                Point = point,
                Radius = 1.7f + (hash & 255u) / 255f * 1.4f,
                Phase = ((hash >> 8) & 1023u) / 1023f * Mathf.PI * 2f,
                Palette = palette
            };
        }

        private void Connect(ref int edge, int from, int to)
        {
            connections[edge++] = new Connection { From = from, To = to };
        }

        private static Vector2 HemisphereOutline(float side, float angle)
        {
            float lobe = 1f + 0.018f * Mathf.Sin(angle * 7f);
            return new Vector2(side * (16f + 242f * Mathf.Sin(angle) * lobe),
                505f + 183f * Mathf.Cos(angle));
        }

        private static Vector2 FissurePoint(float side, float t)
        {
            return new Vector2(side * (14f + 3f * Mathf.Sin(t * Mathf.PI * 5f)),
                322f + t * 366f);
        }

        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
        {
            vh.Clear();
            EnsureGeometry();
            drawingRect = GetPixelAdjustedRect();
            if (drawingRect.width <= 0f || drawingRect.height <= 0f)
                return;

            drawingScale = new Vector2(drawingRect.width / DesignSize.x,
                drawingRect.height / DesignSize.y);
            for (int i = 0; i < contours.Length; i++)
            {
                Contour outline = contours[i];
                AddLine(vh, outline.From, outline.To, 1.15f, Tint(outline.Palette, 0.48f));
            }
            for (int i = 0; i < connections.Length; i++)
            {
                Connection link = connections[i];
                Node from = nodes[link.From];
                Node to = nodes[link.To];
                float pulse = 0.5f + 0.5f * Mathf.Sin(animationTime * 1.7f + from.Phase);
                AddLine(vh, from.Point, to.Point, 0.85f,
                    Tint(from.Palette, 0.16f + pulse * 0.21f));
            }

            DrawScanRing(vh, new Vector2(0f, 505f), new Vector2(286f, 135f), 0);
            DrawScanRing(vh, new Vector2(0f, 475f), new Vector2(272f, 93f), 1);
            DrawScanRing(vh, new Vector2(0f, 179f), new Vector2(153f, 58f), 2);

            for (int i = 0; i < nodes.Length; i++)
            {
                Node node = nodes[i];
                float pulse = 0.5f + 0.5f * Mathf.Sin(animationTime * 2.2f + node.Phase);
                if (i % 5 == 0)
                    AddDisc(vh, node.Point, node.Radius * (2f + pulse * 0.35f),
                        Tint(node.Palette, 0.08f + pulse * 0.06f));
                AddDisc(vh, node.Point, node.Radius, Tint(node.Palette, 0.5f + pulse * 0.45f));
            }
        }

        private void DrawScanRing(UnityEngine.UI.VertexHelper vh, Vector2 center,
            Vector2 radius, int ring)
        {
            center.y += Mathf.Sin(animationTime * Mathf.PI * 2f / 12f + ring * 1.8f) * 19f;
            for (int i = 0; i < RingSegments; i++)
            {
                Vector2 a = center + Vector2.Scale(ringDirections[i], radius);
                Vector2 b = center + Vector2.Scale(ringDirections[(i + 1) % RingSegments], radius);
                AddLine(vh, a, b, 0.8f, Tint((byte)ring, 0.12f));
            }

            float start = animationTime * Mathf.PI * 2f / (ring == 1 ? -10f : 15f) + ring * 2.1f;
            const float sweep = Mathf.PI * 0.62f;
            Vector2 previous = center + new Vector2(Mathf.Cos(start) * radius.x,
                Mathf.Sin(start) * radius.y);
            for (int i = 1; i <= ArcSegments; i++)
            {
                float angle = start + sweep * i / ArcSegments;
                Vector2 point = center + new Vector2(Mathf.Cos(angle) * radius.x,
                    Mathf.Sin(angle) * radius.y);
                AddLine(vh, previous, point, 1.8f, Tint((byte)(ring == 2 ? 3 : 0),
                    0.2f + 0.55f * i / ArcSegments));
                previous = point;
            }
        }

        private Color32 Tint(byte palette, float alpha)
        {
            Color value;
            switch (palette)
            {
                case 1: value = Blue; break;
                case 2: value = Purple; break;
                case 3: value = Magenta; break;
                default: value = Cyan; break;
            }
            Color tint = color;
            value.r *= tint.r;
            value.g *= tint.g;
            value.b *= tint.b;
            value.a = Mathf.Clamp01(alpha * tint.a);
            return value;
        }

        private Vector3 ToLocal(Vector2 point)
        {
            // Rect minima encode any pivot; authored x coordinates are centered on the brain.
            return new Vector3(drawingRect.xMin + (point.x + DesignSize.x * 0.5f) * drawingScale.x,
                drawingRect.yMin + point.y * drawingScale.y, 0f);
        }

        private void AddLine(UnityEngine.UI.VertexHelper vh, Vector2 from, Vector2 to,
            float width, Color32 tint)
        {
            if (vh.currentVertCount + 4 > VertexBudget)
                return;
            Vector2 direction = to - from;
            float length = direction.magnitude;
            if (length < 0.001f)
                return;
            Vector2 normal = new Vector2(-direction.y, direction.x) * (width * 0.5f / length);
            int start = vh.currentVertCount;
            vh.AddVert(ToLocal(from - normal), tint, Vector2.zero);
            vh.AddVert(ToLocal(from + normal), tint, Vector2.zero);
            vh.AddVert(ToLocal(to + normal), tint, Vector2.zero);
            vh.AddVert(ToLocal(to - normal), tint, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }

        private void AddDisc(UnityEngine.UI.VertexHelper vh, Vector2 center, float radius,
            Color32 tint)
        {
            if (vh.currentVertCount + DiscSides + 1 > VertexBudget)
                return;
            int start = vh.currentVertCount;
            vh.AddVert(ToLocal(center), tint, Vector2.zero);
            for (int i = 0; i < DiscSides; i++)
                vh.AddVert(ToLocal(center + discDirections[i] * radius), tint, Vector2.zero);
            for (int i = 0; i < DiscSides; i++)
                vh.AddTriangle(start, start + 1 + (i + 1) % DiscSides, start + 1 + i);
        }
    }
}
