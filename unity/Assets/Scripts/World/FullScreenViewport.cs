namespace BakAgain.World {
    using System;
    using BakAgain.Graphics;
    using UnityEngine;

    /// <summary>
    /// The travel viewport as Enhanced full-screen sees it: the whole window while
    /// <see cref="Active"/>, the faithful rect otherwise. Handed to BOTH the world view and the
    /// picker so a click always maps through the box the world was actually drawn in.
    /// </summary>
    /// <remarks>Lens values (canonical rect, focal length, aspect) stay the original's — the FOV rule
    /// in WorldProjection.CoverVerticalFovDegrees widens from them.</remarks>
    public sealed class FullScreenViewport : IWorldViewport {
        private readonly IWorldViewport _inner;
        private readonly Func<bool> _active;
        private readonly Func<Vector2> _windowSize;

        public FullScreenViewport(IWorldViewport inner, Func<bool> active, Func<Vector2> windowSize) {
            _inner = inner;
            _active = active;
            _windowSize = windowSize;
        }

        public bool Active => _active();
        public Area CanonicalRect => _inner.CanonicalRect;
        public float ViewportAspect => _inner.ViewportAspect;
        public int FocalLength => _inner.FocalLength;

        public Rect ToScreenRect(Rect stageScreenRect) {
            if (!Active) {
                return _inner.ToScreenRect(stageScreenRect);
            }
            Vector2 s = _windowSize();
            return new Rect(0, 0, s.x, s.y);
        }

        public Vector2Int RenderTextureSize(Rect stageScreenRect) {
            if (!Active) {
                return _inner.RenderTextureSize(stageScreenRect);
            }
            Vector2 s = _windowSize();
            return new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(s.x)), Mathf.Max(1, Mathf.RoundToInt(s.y)));
        }
    }
}
