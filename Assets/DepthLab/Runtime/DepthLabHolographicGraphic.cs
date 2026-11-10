using UnityEngine;

namespace DepthLab
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class DepthLabHolographicGraphic : UnityEngine.UI.MaskableGraphic
    {
        public enum Illustration { Panel, Disparity, Parallax, Illusion, Lighting }
        public Illustration illustration;
        public Color glassColor = new Color(.018f, .046f, .09f, .90f);

        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            if (illustration == Illustration.Panel)
            {
                DrawPanel(mesh, rect);
                return;
            }

            float size = Mathf.Min(rect.width, rect.height);
            Vector2 origin = rect.center - Vector2.one * size * .5f;
            Color dim = color;
            dim.a *= .3f;
            Line(mesh, origin, size, .04f, .02f, .28f, .02f, dim);
            Line(mesh, origin, size, .02f, .04f, .02f, .28f, dim);
            Line(mesh, origin, size, .72f, .98f, .96f, .98f, dim);
            Line(mesh, origin, size, .98f, .72f, .98f, .96f, dim);
            switch (illustration)
            {
                case Illustration.Disparity:
                    Circle(mesh, origin, size, new Vector2(.27f, .30f), .13f, color);
                    Circle(mesh, origin, size, new Vector2(.73f, .30f), .13f, color);
                    Circle(mesh, origin, size, new Vector2(.50f, .72f), .09f, color);
                    Line(mesh, origin, size, .27f, .43f, .50f, .63f, dim);
                    Line(mesh, origin, size, .73f, .43f, .50f, .63f, dim);
                    Line(mesh, origin, size, .40f, .30f, .60f, .30f, color);
                    break;
                case Illustration.Parallax:
                    for (int i = 0; i < 3; i++)
                    {
                        float x = .22f + i * .28f, h = .60f - i * .13f;
                        Line(mesh, origin, size, x, .20f, x, .25f + h, color);
                        Line(mesh, origin, size, x - .13f, .40f, x, .25f + h, color);
                        Line(mesh, origin, size, x + .13f, .40f, x, .25f + h, color);
                        Line(mesh, origin, size, x - .13f, .40f, x + .13f, .40f, dim);
                    }
                    Line(mesh, origin, size, .12f, .14f, .85f, .14f, color);
                    Line(mesh, origin, size, .76f, .08f, .85f, .14f, color);
                    break;
                case Illustration.Illusion:
                    Line(mesh, origin, size, .13f, .12f, .43f, .89f, dim);
                    Line(mesh, origin, size, .87f, .12f, .57f, .89f, dim);
                    for (int i = 0; i < 2; i++)
                    {
                        float y = .32f + i * .33f, d = i == 0 ? -.1f : .1f;
                        Line(mesh, origin, size, .28f, y, .72f, y, color);
                        Line(mesh, origin, size, .28f, y, .28f + d, y + .09f, color);
                        Line(mesh, origin, size, .28f, y, .28f + d, y - .09f, color);
                        Line(mesh, origin, size, .72f, y, .72f - d, y + .09f, color);
                        Line(mesh, origin, size, .72f, y, .72f - d, y - .09f, color);
                    }
                    break;
                case Illustration.Lighting:
                    Circle(mesh, origin, size, new Vector2(.57f, .40f), .25f, color);
                    Circle(mesh, origin, size, new Vector2(.22f, .78f), .07f, color);
                    Line(mesh, origin, size, .30f, .70f, .43f, .57f, dim);
                    Line(mesh, origin, size, .37f, .40f, .57f, .20f, dim);
                    Line(mesh, origin, size, .38f, .51f, .67f, .22f, dim);
                    Line(mesh, origin, size, .46f, .59f, .75f, .30f, dim);
                    Line(mesh, origin, size, .25f, .10f, .87f, .10f, color);
                    break;
            }
        }

        private void DrawPanel(UnityEngine.UI.VertexHelper mesh, Rect r)
        {
            float cut = Mathf.Min(32, Mathf.Min(r.width, r.height) * .1f);
            Vector2[] corners = {
                new Vector2(r.xMin + cut, r.yMin), new Vector2(r.xMax, r.yMin),
                new Vector2(r.xMax, r.yMax - cut), new Vector2(r.xMax - cut, r.yMax),
                new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMin + cut)
            };
            foreach (Vector2 p in corners) mesh.AddVert(p, glassColor, Vector2.zero);
            for (int i = 1; i < corners.Length - 1; i++) mesh.AddTriangle(0, i, i + 1);
            Color edge = color;
            edge.a *= .45f;
            for (int i = 0; i < corners.Length; i++)
                Stroke(mesh, corners[i], corners[(i + 1) % corners.Length], 1.5f, edge);
            Color grid = color;
            grid.a *= .035f;
            for (float x = r.xMin + 48; x < r.xMax - 24; x += 64)
                Stroke(mesh, new Vector2(x, r.yMin + 24), new Vector2(x, r.yMax - 24), .7f, grid);
            for (float y = r.yMin + 48; y < r.yMax - 24; y += 64)
                Stroke(mesh, new Vector2(r.xMin + 24, y), new Vector2(r.xMax - 24, y), .7f, grid);
            Stroke(mesh, new Vector2(r.xMin + 32, r.yMax), new Vector2(r.xMin + 180, r.yMax), 3, color);
            Stroke(mesh, new Vector2(r.xMax - 180, r.yMin), new Vector2(r.xMax - 32, r.yMin), 3, color);
        }

        private static void Line(UnityEngine.UI.VertexHelper mesh, Vector2 origin, float size,
            float x1, float y1, float x2, float y2, Color tint)
        {
            Stroke(mesh, origin + new Vector2(x1, y1) * size,
                origin + new Vector2(x2, y2) * size, Mathf.Max(1.5f, size * .023f), tint);
        }

        private static void Circle(UnityEngine.UI.VertexHelper mesh, Vector2 origin, float size,
            Vector2 center, float radius, Color tint)
        {
            for (int i = 0; i < 32; i++)
            {
                float a = i * Mathf.PI / 16, b = (i + 1) * Mathf.PI / 16;
                Stroke(mesh, origin + (center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius) * size,
                    origin + (center + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius) * size,
                    Mathf.Max(1.5f, size * .023f), tint);
            }
        }

        private static void Stroke(UnityEngine.UI.VertexHelper mesh, Vector2 a, Vector2 b, float width, Color tint)
        {
            Vector2 delta = b - a;
            Vector2 normal = new Vector2(-delta.y, delta.x).normalized * (width * .5f);
            int start = mesh.currentVertCount;
            mesh.AddVert(a - normal, tint, Vector2.zero);
            mesh.AddVert(a + normal, tint, Vector2.zero);
            mesh.AddVert(b + normal, tint, Vector2.zero);
            mesh.AddVert(b - normal, tint, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
