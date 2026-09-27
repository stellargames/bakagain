namespace BakAgain.World {
    using UnityEngine;

    /// <summary>
    /// Shows one frame of a flip-book entity — the runtime half of
    /// <c>renderShapeDispatcher</c>'s <c>face = flagByte % frameCount</c>.
    /// </summary>
    /// <remarks>
    /// <b>A flagged mesh's extra faces are positions, not extra geometry.</b> They all reach the
    /// mesh as sub-meshes so one can be picked between; this picks it, by lighting that sub-mesh's
    /// material and nulling the others. A door's eight are its swing.
    ///
    /// <para>Unflagged sub-meshes are never touched — in the original a mesh that opts out with
    /// 0xFF is drawn unconditionally, and here that is the door's frame and hinges, which must not
    /// blink while the panel moves.</para>
    ///
    /// <para><b>Everything it needs is serialized, and the renderer is looked up rather than
    /// held.</b> World entities are built once as a template and Instantiated per placement, and a
    /// clone keeps only serialized state — a plain field would arrive null and a cached
    /// <see cref="MeshRenderer"/> reference would still point at the TEMPLATE's renderer, so every
    /// door would drive the same one.</para>
    /// </remarks>
    public sealed class WorldMeshFrames : MonoBehaviour {
        /// <summary>Marks a sub-mesh that is always drawn, whatever frame is showing.</summary>
        public const int AlwaysLit = -1;

        [SerializeField] private Material[] _materials;
        [SerializeField] private int[] _frameOfSubMesh;
        [SerializeField] private int _frame;

        /// <summary>The frame currently showing.</summary>
        public int Frame => _frame;

        /// <summary>How many frames this entity has — one past the highest frame ordinal.</summary>
        public int FrameCount {
            get {
                if (_frameOfSubMesh == null) {
                    return 0;
                }
                var highest = 0;
                foreach (int f in _frameOfSubMesh) {
                    if (f > highest) {
                        highest = f;
                    }
                }

                return highest + 1;
            }
        }

        /// <summary>
        /// Adopt a built renderer's materials. <paramref name="frameOfSubMesh"/> carries
        /// <see cref="AlwaysLit"/> for unflagged sub-meshes and the frame ordinal for the rest.
        /// </summary>
        internal void Initialise(Material[] materials, int[] frameOfSubMesh) {
            _materials = materials;
            _frameOfSubMesh = frameOfSubMesh;
            _frame = 0;
        }

        /// <summary>
        /// Take another entity's frame map — used when cloning a template.
        /// </summary>
        /// <remarks>
        /// <b>Copied explicitly rather than left to Instantiate.</b> The model loader hands out a
        /// cached template that is built asynchronously, so a placement can clone it while it is
        /// still being filled in; the clone then carries empty arrays and silently never animates.
        /// Copying after the clone depends on the template being ready THEN, which it is by the
        /// time a placement is positioned.
        /// </remarks>
        internal void CopyFrom(WorldMeshFrames source) {
            if (source == null || source._frameOfSubMesh == null || source._frameOfSubMesh.Length == 0) {
                return;
            }
            _materials = (Material[])source._materials?.Clone();
            _frameOfSubMesh = (int[])source._frameOfSubMesh.Clone();
            SetFrame(source._frame);
        }

        /// <summary>
        /// Show <paramref name="frame"/>, wrapping the way the original's modulo does.
        /// </summary>
        public void SetFrame(int frame) {
            var renderer = GetComponent<MeshRenderer>();
            int count = FrameCount;
            if (renderer == null || _materials == null || count <= 0) {
                return;
            }

            int wanted = ((frame % count) + count) % count;
            _frame = wanted;

            Material hidden = HiddenMaterial;
            var live = new Material[_materials.Length];
            for (var i = 0; i < _materials.Length; i++) {
                live[i] = _frameOfSubMesh[i] == AlwaysLit || _frameOfSubMesh[i] == wanted
                    ? _materials[i]
                    : hidden;
            }
            renderer.sharedMaterials = live;
        }

        /// <summary>
        /// The material an inactive frame wears — one that draws nothing.
        /// </summary>
        /// <remarks>
        /// <b>NOT null, which is what this used to assign.</b> A null entry in
        /// <see cref="Renderer.sharedMaterials"/> does not hide a sub-mesh: Unity draws it with the
        /// magenta error material, so the mechanism meant to hide a frame DISPLAYED it. gate2's rift
        /// showed a magenta blob between its posts and the combat grid marker painted a magenta
        /// border around its live frame, both from this line.
        ///
        /// <para>Shared and built once — every flip-book entity in the world hides frames with the
        /// same material, and it is created rather than serialized so a template cloned before it
        /// finished building still gets it.</para>
        /// </remarks>
        private static Material HiddenMaterial {
            get {
                if (_hidden != null) {
                    return _hidden;
                }
                Shader shader = Shader.Find(HiddenShaderName);
                if (shader == null) {
                    // Better a visible fault than a silent one: without the shader there is no way
                    // to hide a frame, and null would put the magenta straight back.
                    Debug.LogError($"WorldMeshFrames: shader '{HiddenShaderName}' not found; "
                        + "inactive flip-book frames cannot be hidden.");
                    return null;
                }
                _hidden = new Material(shader) {
                    name = "BakHiddenFrame",
                    hideFlags = HideFlags.HideAndDontSave,
                };
                return _hidden;
            }
        }

        private const string HiddenShaderName = "BakAgain/HiddenFrame";
        private static Material _hidden;
    }
}
