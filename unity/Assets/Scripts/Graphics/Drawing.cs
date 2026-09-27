namespace BakAgain.Graphics {
    using BakAgain.Core;
    using BakAgain.CutScenes;
    using BakAgain.CutScenes.Extensions;
    using BakAgain.ResourceManagement.Models;
    using BakAgain.Utility;
    using GameData.Resources.Animation;
    using System;
    using UnityEngine;
    using UnityEngine.Rendering;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    public class Drawing {
        private static readonly int DstBlend = Shader.PropertyToID("_DstBlend");
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger<Drawing>();

        public static void CopyToArea(Texture source, Texture destination, IArea area) {
            Graphics.CopyTexture(
                source,
                0,
                0,
                0,
                0,
                Math.Min(area.Width, destination.width),
                Math.Min(area.Height, destination.height),
                destination,
                0,
                0,
                area.X,
                area.InverseY(destination.height));
        }

        public static void CopyArea(Texture source, Texture destination, IArea area) {
            int inverseY = area.InverseY(destination.height);
            Graphics.CopyTexture(
                source,
                0,
                0,
                area.X,
                inverseY,
                area.Width,
                area.Height,
                destination,
                0,
                0,
                area.X,
                inverseY);
        }

        public static void DrawBorder(IArea area, int paletteIndex, CutsceneState cutsceneState) {
            Texture2D singlePixel = GetScratchPixel();
            Material transparentMaterial = GetTransparentMaterial();

            // The area is in CANONICAL (1600x1200) coordinates, not the original 320x200 — the
            // extractors scale VGA coords on the way out, so a shipped TTM FillArea reads
            // X=70 Width=1455. ScaleFromOriginal is canvas/CANONICAL despite its name, so this
            // converts canonical -> buffer. Reading either as 320x200 makes the arithmetic look
            // broken when it is not.
            DrawingUtils.BorderRects border = DrawingUtils.GetBorderRects(area, cutsceneState.ScaleFromOriginal);

            RenderTexture indexedTarget = cutsceneState.CurrentIndexedBuffer;

            // Draw the indexed border
            RenderTexture.active = indexedTarget;

            singlePixel.SetPixel(0, 0, new Color(paletteIndex / 255f, 0, 0, 1));
            singlePixel.Apply();

            // Set it as the main texture
            cutsceneState.PassThroughMaterial.SetTexture(MainTex, singlePixel);

            GL.PushMatrix();
            GL.LoadPixelMatrix(0, indexedTarget.width, indexedTarget.height, 0);

            // Draw four sides
            Graphics.DrawTexture(border.Top, singlePixel, cutsceneState.PassThroughMaterial);
            Graphics.DrawTexture(border.Bottom, singlePixel, cutsceneState.PassThroughMaterial);
            Graphics.DrawTexture(border.Left, singlePixel, cutsceneState.PassThroughMaterial);
            Graphics.DrawTexture(border.Right, singlePixel, cutsceneState.PassThroughMaterial);

            GL.PopMatrix();

            RenderTexture directTarget = cutsceneState.CurrentDirectBuffer;

            // Create a transparent hole in the direct buffer, so that when it is drawn after the indexed buffer, it doesn't overwrite the border
            RenderTexture.active = directTarget;

            // Reuse the scratch pixel as a transparent pixel
            singlePixel.SetPixel(0, 0, Color.clear);
            singlePixel.Apply();
            transparentMaterial.mainTexture = singlePixel;

            GL.PushMatrix();
            GL.LoadPixelMatrix(0, directTarget.width, directTarget.height, 0);

            // Draw four transparent sides
            Graphics.DrawTexture(border.Top, singlePixel, transparentMaterial);
            Graphics.DrawTexture(border.Bottom, singlePixel, transparentMaterial);
            Graphics.DrawTexture(border.Left, singlePixel, transparentMaterial);
            Graphics.DrawTexture(border.Right, singlePixel, transparentMaterial);

            GL.PopMatrix();

            RenderTexture.active = null;
        }

        // Cached 1x1 scratch pixel and transparent material for DrawBorder. These are
        // pure render scratch (rewritten every call), so they are kept alive between
        // calls instead of allocating + destroying per border. HideAndDontSave keeps
        // them out of saves and prevents destruction on scene load; the null-check via
        // the implicit bool operator transparently re-creates them after a domain reload.
        private static Texture2D s_scratchPixel;
        private static Material s_transparentMaterial;

        private static Texture2D GetScratchPixel() {
            if (!s_scratchPixel) {
                s_scratchPixel = new Texture2D(1, 1, TextureFormat.RGBA32, false, true) {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
            return s_scratchPixel;
        }

        private static Material GetTransparentMaterial() {
            if (!s_transparentMaterial) {
                // *** Custom/ClearToTransparent, NOT Unlit/Transparent. *** This material's job is to
                // CLEAR the destination, and an alpha-blended shader cannot: drawing a fully
                // transparent pixel through SrcAlpha OneMinusSrcAlpha contributes nothing and leaves
                // the destination untouched. DrawBorder used it to punch a hole and punched none, so
                // an indexed border was hidden wherever the direct buffer already had something
                // opaque under it (TASK-204). Blend Zero Zero stores zero, which is the hole.
                s_transparentMaterial = new Material(Shader.Find("Custom/ClearToTransparent")) {
                    hideFlags = HideFlags.HideAndDontSave
                };
                s_transparentMaterial.SetInt(DstBlend, (int)BlendMode.Zero); // Force transparency
            }
            return s_transparentMaterial;
        }

        public static void DrawImage(IndexedTexture image, int x, int y, Orientation orientation, Vector2 resize, CutsceneState cutsceneState) {
            RenderTexture destination = image.IsIndexed ? cutsceneState.CurrentIndexedBuffer : cutsceneState.CurrentDirectBuffer;

            Rect sourceRect, destinationRect;

            if (image.IsIndexed) {
                (sourceRect, destinationRect) = GetRectsOriginal(image, x, y, orientation, resize, cutsceneState);
            } else {
                (sourceRect, destinationRect) = GetRectsModded(image, x, y, orientation, resize, cutsceneState);
            }

            Logger.LogDebug("Draw image {ImageName}:\n- image: (x:{X}, y:{Y}, width:{Width}, height:{Height})\n- clipArea: {ClipArea}\n- sourceRect: {SourceRect}\n- destRect: {DestRect}\n- resize: {Resize}", image.GetRenderTexture().name, x, y, image.Width, image.Height, cutsceneState.ClipArea, sourceRect, destinationRect, resize);

            // If the destination rect is completely outside the clip area, skip drawing
            if (destinationRect.width <= 0 || destinationRect.height <= 0) {
                Logger.LogWarning("Skipping image because it is outside the clip area");

                return;
            }

            if (image.IsIndexed) {
                // Draw a hole in the direct render texture
                RenderTexture.active = cutsceneState.CurrentDirectBuffer;
                GL.PushMatrix();
                GL.LoadPixelMatrix(0, destination.width, destination.height, 0);
                try {
                    Graphics.DrawTexture(destinationRect, image.IndexTexture, sourceRect, 0, 0, 0, 0, cutsceneState.CutOutMaterial);
                } finally {
                    GL.PopMatrix();
                    RenderTexture.active = null;
                }
            }

            // Draw the image to the destination render texture
            RenderTexture.active = destination;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, destination.width, destination.height, 0);
            try {
                Graphics.DrawTexture(destinationRect, image.GetRenderTexture(), sourceRect, 0, 0, 0, 0, cutsceneState.PassThroughMaterial);
            } finally {
                GL.PopMatrix();
                RenderTexture.active = null;
            }
        }

        public static void DrawImageWithSkew(IndexedTexture image, int x, int y, Orientation orientation, Vector2 resize, float topSkew, float bottomSkew, CutsceneState cutsceneState) {
            RenderTexture destination = image.IsIndexed ? cutsceneState.CurrentIndexedBuffer : cutsceneState.CurrentDirectBuffer;

            Rect sourceRect, destinationRect;

            if (image.IsIndexed) {
                (sourceRect, destinationRect) = GetRectsOriginal(image, x, y, orientation, resize, cutsceneState);
            } else {
                (sourceRect, destinationRect) = GetRectsModded(image, x, y, orientation, resize, cutsceneState);
            }

            Logger.LogDebug(
                "Draw skewed image {ImageName}:\n- image: (x:{X}, y:{Y}, width:{Width}, height:{Height})\n- clipArea: {ClipArea}\n- sourceRect: {SourceRect}\n- destRect: {DestRect}\n- resize: {Resize}\n- topSkew: {TopSkew}, bottomSkew: {BottomSkew}",
                image.GetRenderTexture().name,
                x,
                y,
                image.Width,
                image.Height,
                cutsceneState.ClipArea,
                sourceRect,
                destinationRect,
                resize,
                topSkew,
                bottomSkew);

            // If the destination rect is completely outside the clip area, skip drawing
            if (destinationRect.width <= 0 || destinationRect.height <= 0) {
                Logger.LogWarning("Skipping image because it is outside the clip area");

                return;
            }

            // Define the skewed vertices
            var topLeft = new Vector3(destinationRect.xMin, destinationRect.yMin, 0);
            var topRight = new Vector3(destinationRect.xMax, destinationRect.yMin + topSkew, 0);
            var bottomRight = new Vector3(destinationRect.xMax, destinationRect.yMax - bottomSkew, 0);
            var bottomLeft = new Vector3(destinationRect.xMin, destinationRect.yMax, 0);

            if (image.IsIndexed) {
                // Draw a hole in the direct render texture
                RenderTexture.active = cutsceneState.CurrentDirectBuffer;
                GL.PushMatrix();
                GL.LoadPixelMatrix(0, cutsceneState.CurrentDirectBuffer.width, cutsceneState.CurrentDirectBuffer.height, 0);
                try {
                    cutsceneState.CutOutMaterial.SetTexture(MainTex, image.IndexTexture);
                    cutsceneState.CutOutMaterial.SetPass(0);

                    GL.Begin(GL.QUADS);
                    GL.TexCoord2(sourceRect.xMin, sourceRect.yMax);
                    GL.Vertex(topLeft);
                    GL.TexCoord2(sourceRect.xMax, sourceRect.yMax);
                    GL.Vertex(topRight);
                    GL.TexCoord2(sourceRect.xMax, sourceRect.yMin);
                    GL.Vertex(bottomRight);
                    GL.TexCoord2(sourceRect.xMin, sourceRect.yMin);
                    GL.Vertex(bottomLeft);
                    GL.End();
                } finally {
                    GL.PopMatrix();
                    RenderTexture.active = null;
                }
            }

            // Draw the image to the destination render texture
            RenderTexture.active = destination;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, destination.width, destination.height, 0);
            try {
                cutsceneState.PassThroughMaterial.SetTexture(MainTex, image.GetRenderTexture());
                cutsceneState.PassThroughMaterial.SetPass(0);

                GL.Begin(GL.QUADS);
                GL.TexCoord2(sourceRect.xMin, sourceRect.yMax);
                GL.Vertex(topLeft);
                GL.TexCoord2(sourceRect.xMax, sourceRect.yMax);
                GL.Vertex(topRight);
                GL.TexCoord2(sourceRect.xMax, sourceRect.yMin);
                GL.Vertex(bottomRight);
                GL.TexCoord2(sourceRect.xMin, sourceRect.yMin);
                GL.Vertex(bottomLeft);
                GL.End();
            } finally {
                GL.PopMatrix();
                RenderTexture.active = null;
            }
        }

        private static (Rect sourceRect, Rect destinationRect) GetRectsOriginal(IndexedTexture image, int x, int y, Orientation orientation, Vector2 resize, CutsceneState cutsceneState)
        {
            // For indexed images, work in canonical 1600×1200 space
            float scaledWidth = image.CanonicalWidth * resize.x;
            float scaledHeight = image.CanonicalHeight * resize.y;

            var (sourceRect, destinationRect) = DrawingUtils.CalculateRects(x, y, scaledWidth, scaledHeight, cutsceneState.ClipArea, orientation);

            // Scale the original destination rect to the current resolution
            return (sourceRect, destinationRect.Scale(cutsceneState.ScaleFromOriginal));
        }

        private static (Rect sourceRect, Rect destinationRect) GetRectsModded(IndexedTexture image, int x, int y, Orientation orientation, Vector2 resize, CutsceneState cutsceneState)
        {
            // For modded images, convert to current resolution space first
            float scaledWidth = cutsceneState.CurrentDirectBuffer.width * image.Scale.x * resize.x;
            float scaledHeight = cutsceneState.CurrentDirectBuffer.height * image.Scale.y * resize.y;
            Rect scaledClipArea = cutsceneState.ClipArea.Scale(cutsceneState.ScaleFromOriginal);

            return DrawingUtils.CalculateRects(
                x * cutsceneState.ScaleFromOriginal.x,
                y * cutsceneState.ScaleFromOriginal.y,
                scaledWidth,
                scaledHeight,
                scaledClipArea,
                orientation);
        }

        public static void FillArea(IArea area, int colorIndex, CutsceneState state, bool isIndexed = false) {
            Area scaledArea = area.Scale<Area>(state.ScaleFromOriginal);
            var boxTexture = new Texture2D(scaledArea.Width, scaledArea.Height, TextureFormat.RGBA32, false, true) {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            if (isIndexed) {
                FillAreaIndexed(scaledArea, colorIndex, state, boxTexture);
            } else {
                FillAreaDirect(scaledArea, colorIndex, state, boxTexture);
            }

            UnityObjectUtil.Destroy(boxTexture);
        }

        private static void FillAreaDirect(IArea area, int colorIndex, CutsceneState state, Texture2D boxTexture) {
            // For direct mode, we use the color from the palette
            Color color = state.CurrentPalette[colorIndex];
            FillTexture(boxTexture, color);
            CopyToArea(boxTexture, state.CurrentDirectBuffer, area);
        }

        private static void FillAreaIndexed(IArea area, int colorIndex, CutsceneState state, Texture2D boxTexture) {
            // For indexed mode, we just set the index value (in red channel)
            var indexColor = new Color(colorIndex / 255f, 0, 0, 1);
            FillTexture(boxTexture, indexColor);
            CopyToArea(boxTexture, state.CurrentIndexedBuffer, area);

            // Add transparent pixels to direct buffer
            var transparentTexture = new Texture2D(area.Width, area.Height) {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            FillTexture(transparentTexture, Color.clear);
            CopyToArea(transparentTexture, state.CurrentDirectBuffer, area);
            UnityObjectUtil.Destroy(transparentTexture);
        }

        private static void FillTexture(Texture2D texture, Color color) {
            var pixels = new Color[texture.width * texture.height];
            for (int i = 0; i < pixels.Length; i++) {
                pixels[i] = color;
            }
            texture.SetPixels(pixels);
            texture.Apply();
        }
    }
}