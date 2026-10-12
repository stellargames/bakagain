namespace BakAgain.Book {
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// One face of a turning page: a strip of quads from the spine to the outer edge, in screen pixels
    /// on a full-screen overlay. <see cref="BookPageCurl"/> fills <see cref="Columns"/> each frame.
    /// </summary>
    public sealed class CurlSheet : MaskableGraphic {
        /// <summary>Per column, spine to outer edge: screen x, the column's bottom and top y, texture u, shade.</summary>
        public readonly System.Collections.Generic.List<(float X, float Bottom, float Top, float U, float Shade, bool Shown)> Columns = new();

        private Texture _texture;

        /// <summary>What the face shows; changing it rebinds the face (a plain field would keep the old one).</summary>
        public Texture Texture {
            get => _texture;
            set {
                if (_texture != value) {
                    _texture = value;
                    SetMaterialDirty();
                }
            }
        }
        public float V0;
        public float V1 = 1;

        /// <summary>Drawn black, its shade as the opacity: a shadow, not a face.</summary>
        public bool Shadow;

        public override Texture mainTexture => _texture != null ? _texture : s_WhiteTexture;

        protected override void OnPopulateMesh(VertexHelper vh) {
            vh.Clear();
            for (int i = 1; i < Columns.Count; i++) {
                var a = Columns[i - 1];
                var b = Columns[i];
                if (!b.Shown) {
                    continue;
                }
                int start = vh.currentVertCount;
                vh.AddVert(new Vector3(a.X, a.Bottom), Shade(a.Shade), new Vector4(a.U, V0));
                vh.AddVert(new Vector3(a.X, a.Top), Shade(a.Shade), new Vector4(a.U, V1));
                vh.AddVert(new Vector3(b.X, b.Top), Shade(b.Shade), new Vector4(b.U, V1));
                vh.AddVert(new Vector3(b.X, b.Bottom), Shade(b.Shade), new Vector4(b.U, V0));
                vh.AddTriangle(start, start + 1, start + 2);
                vh.AddTriangle(start, start + 2, start + 3);
            }
        }

        private Color32 Shade(float shade) {
            byte v = (byte)(Mathf.Clamp01(shade) * 255);
            return Shadow ? new Color32(0, 0, 0, v) : new Color32(v, v, v, (byte)(color.a * 255));
        }
    }
}
