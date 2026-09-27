namespace BakAgain.Book {
    using BakAgain.Graphics;
    using BakAgain.UI;
    using System.Collections.Generic;
    using System.Linq;
    using GameData.Resources.Book;
    using TMPro;
    using UnityEngine;
    using UnityEngine.UI;

    public class BookView : MonoBehaviour, IBookView {
        [SerializeField]
        private Canvas bookCanvas;

        [SerializeField]
        private RawImage backgroundImage;

        [SerializeField]
        private CanvasScaler canvasScaler;

        [SerializeField]
        private RectTransform contentRoot;

        private readonly List<GameObject> _pageObjects = new();

        // Background's square-pixel size (BOOK.SCX extracts to 1280x960 = 4:3). Drives the page
        // aspect and the book-pixel -> screen scaling. Modded backgrounds set their own size.
        private float _backgroundWidth = Canonical.BookWidth;
        private float _backgroundHeight = Canonical.BookHeight;

        // BOK layout coordinates, the word-wrap math (BakFontData advances), and the background /
        // book images all live in the canonical 1280x960 book space (the original 640x350 EGA frame
        // scaled x2 horizontal / x96-over-35 vertical by the extractor — square pixels at 4:3).
        // At render time the page is pillar/letter-boxed to that aspect inside the canvas, and these
        // constants map canonical book px into the resulting on-screen area (independent of
        // screen/canvas size). Canonical is square-pixel, so sx == sy when the background is 4:3.
        private const float BookSpaceWidth = Canonical.BookWidth;
        private const float BookSpaceHeight = Canonical.BookHeight;

        // BOOK.FNT glyph box height in canonical book px (15 EGA px x2 — the horizontal factor,
        // matching the x2-scaled advances the wrapper measures with, so rendered line widths track
        // the wrap decisions).
        private const float BookFontEmHeight = 30f;

        // The original book is 640x350 EGA; the extractor square-pixel-corrects it to 1280x960 — a
        // horizontal factor of x2 (1280/640) but a taller vertical factor of x960/350. The font is
        // sized on the horizontal factor (so widths/line-breaks match the wrap), so to reproduce the
        // original's non-square pixels each glyph must be stretched vertically by the ratio of the two
        // factors (= (960/350)/(1280/640) ≈ 1.371). Applied as a localScale.y on each text line.
        private const float EgaBookWidth = 640f;
        private const float EgaBookHeight = 350f;
        private const float FontVerticalStretch =
            (BookSpaceHeight * EgaBookWidth) / (BookSpaceWidth * EgaBookHeight);

        public void SetBackground(Sprite background) {
            backgroundImage.texture = background.texture;
            _backgroundWidth = background.texture.width;
            _backgroundHeight = background.texture.height;
        }

        // Pillar/letter-boxes the page to the background's aspect inside the canvas and sizes both the
        // background and the content area to that centered rect, so the content overlays the background
        // exactly. The canvas is ConstantPhysicalSize (its local size tracks the screen and its
        // referenceResolution is ignored), so fitting here is what keeps the page at the correct aspect
        // and makes book-pixel positioning independent of screen/canvas size.
        // Returns the on-screen page size and the texture->screen scale used to size book images.
        private (float width, float height, float displayScale) FitPageToCanvas() {
            var canvasRect = ((RectTransform)bookCanvas.transform).rect;
            float canvasWidth = canvasRect.width;
            float canvasHeight = canvasRect.height;
            float aspect = _backgroundWidth / _backgroundHeight;

            float width, height;
            if (canvasWidth / canvasHeight > aspect) {
                height = canvasHeight;
                width = canvasHeight * aspect;
            } else {
                width = canvasWidth;
                height = canvasWidth / aspect;
            }

            CenterRect(backgroundImage.rectTransform, width, height);
            CenterRect(contentRoot, width, height);

            return (width, height, width / _backgroundWidth);
        }

        private static void CenterRect(RectTransform rt, float width, float height) {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = Vector2.zero;
        }

        public (int paragraphIndex, int lineOffset) ShowPage(Page page, Sprite[] bookSprites, Color[] palette,
                            IReadOnlyList<Paragraph> allParagraphs, int startParagraph, int startLineOffset = 0) {
            ClearPage();

            // Pillar/letter-box the page and size the content area to overlay the background exactly.
            (float pageWidth, float pageHeight, float displayScale) = FitPageToCanvas();

            // Map canonical book-px (1280x960) coordinates into the on-screen page area.
            float sx = pageWidth / BookSpaceWidth;
            float sy = pageHeight / BookSpaceHeight;

            // Render images. Sprites are already EGA-corrected (square pixels at the background's
            // texture scale), so their native size times displayScale gives the correct on-screen
            // size; positions use the same book->screen scaling as the text.
            foreach (BookImage bookImage in page.Images) {
                if (bookImage.ImageNumber < 0 || bookImage.ImageNumber >= bookSprites.Length)
                    continue;

                var imgGo = new GameObject($"BookImage_{bookImage.ImageNumber}");
                imgGo.transform.SetParent(contentRoot, false);

                var img = imgGo.AddComponent<Image>();
                img.sprite = bookSprites[bookImage.ImageNumber];

                var rt = imgGo.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.up;
                rt.anchorMax = Vector2.up;
                rt.pivot = Vector2.up;
                float imgWidth = img.sprite.rect.width * displayScale;
                float imgHeight = img.sprite.rect.height * displayScale;
                rt.sizeDelta = new Vector2(imgWidth, imgHeight);

                // Apply mirroring — adjust position to keep the image in the same bounding box
                float posX = bookImage.X * sx;
                float posY = -bookImage.Y * sy;
                var scale = rt.localScale;
                if ((bookImage.Mirroring & Mirroring.Horizontal) != 0) {
                    scale.x = -1;
                    posX += imgWidth;
                }
                if ((bookImage.Mirroring & Mirroring.Vertical) != 0) {
                    scale.y = -1;
                    posY -= imgHeight;
                }
                rt.anchoredPosition = new Vector2(posX, posY);
                rt.localScale = scale;

                _pageObjects.Add(imgGo);
            }

            // Create a clipping container for the page text area
            var clipGo = new GameObject("PageClip");
            clipGo.transform.SetParent(contentRoot, false);
            var clipRt = clipGo.AddComponent<RectTransform>();
            clipRt.anchorMin = Vector2.up;
            clipRt.anchorMax = Vector2.up;
            clipRt.pivot = Vector2.up;
            clipRt.anchoredPosition = new Vector2(page.XOffset * sx, -page.YOffset * sy);
            clipRt.sizeDelta = new Vector2(page.Width * sx, page.Height * sy);
            clipGo.AddComponent<RectMask2D>();
            _pageObjects.Add(clipGo);

            // Render paragraphs — flow from allParagraphs[startParagraph] until page is full
            // From RE: paragraph.Width = right margin, paragraph.WordSpacing = inter-paragraph Y gap
            // Reserved area fields: X=leftX, Y=topY, Width=rightX, Height=bottomY (screen coords!)
            float cursorY = 0;
            int paragraphIndex = startParagraph;
            int currentLineOffset = startLineOffset;

            // Render page number (from RE: bok_drawPageNumber at 0x4d3fb). Defined as a local function
            // so it runs at every exit point — the page number must appear even when the page fills up
            // and the loop returns early to continue on the next page.
            // Uses Roman numerals, black, font index 1. Odd pages right-aligned, even pages left-aligned,
            // at book-space Y = pageHeight - 52 canonical px (the original's 19 EGA px x96/35).
            void RenderPageNumber() {
                if (!page.ShowPageNumber)
                    return;

                var numGo = new GameObject("PageNumber");
                numGo.transform.SetParent(contentRoot, false);

                var tmp = numGo.AddComponent<TextMeshProUGUI>();
                // The DISPLAY number, not the page's index in the file: booktext_draw_page_number
                // prints page->wDisplayNumber (BOOKTEXT.C:404), so C92's first page is 456, not 3.
                tmp.text = GameData.Resources.Book.BookPageNumeral.For(page.PageDisplayNumber);
                tmp.fontSize = BookFontEmHeight * sx;
                tmp.color = Color.black;
                tmp.textWrappingMode = TextWrappingModes.NoWrap;
                tmp.overflowMode = TextOverflowModes.Overflow;
                if (GameFonts.Book != null)
                    tmp.font = GameFonts.Book; // Original game uses selectBokFont(1): BOOK.FNT

                bool isOddPage = (page.PageDisplayNumber % 2) != 0;
                tmp.alignment = isOddPage
                    ? TextAlignmentOptions.BottomRight
                    : TextAlignmentOptions.BottomLeft;

                var rt = numGo.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.up;
                rt.anchorMax = Vector2.up;

                float pageNumY = -(BookSpaceHeight - 52f) * sy; // 19 EGA px ×96/35

                if (isOddPage) {
                    rt.pivot = new Vector2(1f, 0f);
                    // 2 EGA px right inset ×2 canonical.
                    rt.anchoredPosition = new Vector2((page.XOffset + page.Width - 4) * sx, pageNumY);
                } else {
                    rt.pivot = new Vector2(0f, 0f);
                    rt.anchoredPosition = new Vector2(page.XOffset * sx, pageNumY);
                }
                rt.sizeDelta = new Vector2(400 * sx, 82 * sy); // 200×30 EGA px scaled

                _pageObjects.Add(numGo);
            }

            while (paragraphIndex < allParagraphs.Count) {
                var paragraph = allParagraphs[paragraphIndex];
                bool isContinuation = (currentLineOffset > 0);

                // From RE (sub_ovr148_51F): y += wordSpacing + yOffset before each paragraph
                // Skip inter-paragraph spacing for continuations (already applied on previous page)
                float nextCursorY = isContinuation
                    ? cursorY
                    : cursorY + paragraph.InterParagraphSpacing + paragraph.YOffset;

                // Determine font index for this paragraph
                int fontIndex = 0;
                if (paragraph.TextSegments.Count > 0)
                    fontIndex = paragraph.TextSegments[0].Font;

                // Compute base left offset (reserved areas handled per-line at render time)
                float leftOffset = paragraph.XOffset;
                float rightMargin = paragraph.Width;

                // Pre-wrap text using original game's word-wrap algorithm (sub_ovr148_51F)
                // This ensures line breaks match the DOS original exactly.
                int baseWidth = (int)(page.Width - leftOffset - rightMargin);
                int firstLineWidth = baseWidth - paragraph.StartIndent;

                string wrappedText;
                if (page.ReservedAreas.Count > 0) {
                    // Use per-line width calculation for pages with reserved areas
                    wrappedText = BakTextWrapper.WrapParagraphWithReservedAreas(
                        paragraph.TextSegments, page, paragraph,
                        (int)(page.YOffset + nextCursorY), fontIndex);
                } else {
                    wrappedText = BakTextWrapper.WrapParagraph(
                        paragraph.TextSegments, baseWidth, firstLineWidth, fontIndex);
                }

                // Split wrapped text into lines
                var allLines = wrappedText.Split('\n');
                int totalLines = allLines.Length;

                // For continuations, take only lines from currentLineOffset onward
                int startLine = isContinuation ? currentLineOffset : 0;
                int remainingLines = totalLines - startLine;

                // Check how many lines fit in the remaining page space
                float availableSpace = page.Height - nextCursorY;
                int fittingLines;

                if (availableSpace < paragraph.LineSpacing) {
                    // Not enough space for even one line
                    bool isFirstContentOnPage = (paragraphIndex == startParagraph && !isContinuation)
                                             || (paragraphIndex == startParagraph && isContinuation && cursorY == 0);
                    if (isFirstContentOnPage) {
                        fittingLines = 1; // Force at least one line
                    } else {
                        // Stop — continue on next page
                        RenderPageNumber();
                        return (paragraphIndex, startLine);
                    }
                } else {
                    fittingLines = Mathf.FloorToInt(availableSpace / paragraph.LineSpacing);
                }

                bool paragraphSplit = fittingLines < remainingLines;
                int linesToShow = paragraphSplit ? fittingLines : remainingLines;
                float paragraphHeight = linesToShow * paragraph.LineSpacing;

                bool isJustify = paragraph.Alignment == GameData.Resources.Book.TextAlignment.Justify;

                // Render each wrapped line as its own object. Per-line lets us:
                //  - justify EVERY line except the paragraph's final one (ragged left), as the DOS
                //    original does — TMP's TopJustified can't, because our pre-wrapped '\n' breaks make
                //    every line look like a paragraph's final line, so nothing gets justified;
                //  - position each line at an exact LineSpacing interval (no font-line-height slack); and
                //  - apply the vertical glyph stretch (see FontVerticalStretch) per line.
                for (int li = 0; li < linesToShow; li++) {
                    int absoluteLine = startLine + li;
                    float lineY = nextCursorY + li * paragraph.LineSpacing;
                    float lineScreenY = page.YOffset + lineY;
                    float lineLeft = ComputeLineLeftOffset(page, leftOffset, lineScreenY, paragraph.LineSpacing);
                    float lineWidth = page.Width - lineLeft - rightMargin;

                    var paraGo = new GameObject("Paragraph");
                    paraGo.transform.SetParent(clipRt, false);

                    var tmp = paraGo.AddComponent<TextMeshProUGUI>();

                    var rt = paraGo.GetComponent<RectTransform>();
                    rt.anchorMin = Vector2.up;
                    rt.anchorMax = Vector2.up;
                    rt.pivot = Vector2.up;
                    rt.anchoredPosition = new Vector2(lineLeft * sx, -lineY * sy);
                    rt.sizeDelta = new Vector2(lineWidth * sx, paragraph.LineSpacing * sy);
                    // The book is square-pixel-corrected with a taller vertical factor than horizontal;
                    // the font is sized on the horizontal factor (widths/line-breaks match), so stretch
                    // it vertically to restore the original's non-square glyph aspect.
                    rt.localScale = new Vector3(1f, FontVerticalStretch, 1f);

                    // Font selection
                    if (GameFonts.Book != null) {
                        tmp.font = GameFonts.Book;
                    }

                    // Justify all lines except the paragraph's actual final line (left-ragged). When the
                    // paragraph splits across pages, its final line is on a later page, so every line
                    // shown here is justified.
                    bool isParagraphFinalLine = !paragraphSplit && (li == linesToShow - 1);
                    if (isJustify) {
                        tmp.textWrappingMode = TextWrappingModes.Normal;
                        tmp.overflowMode = TextOverflowModes.Overflow;
                        tmp.alignment = isParagraphFinalLine
                            ? TextAlignmentOptions.TopLeft
                            : TextAlignmentOptions.TopFlush;
                    } else {
                        tmp.textWrappingMode = TextWrappingModes.NoWrap;
                        tmp.overflowMode = TextOverflowModes.Overflow;
                        tmp.alignment = MapAlignment(paragraph.Alignment);
                    }
                    tmp.fontSize = BookFontEmHeight * sx;
                    tmp.characterSpacing = 0;
                    tmp.wordSpacing = 0;
                    tmp.lineSpacing = 0;
                    tmp.margin = Vector4.zero;

                    // Build rich text for this line with per-segment formatting
                    var richText = new System.Text.StringBuilder();

                    // First-line indent: only the actual first line, not continuations, and not when
                    // the line is displaced by a reserved area
                    bool isFirstLine = (absoluteLine == 0) && !isContinuation;
                    bool displacedByReservedArea = lineLeft > leftOffset + 0.5f;
                    if (isFirstLine && !displacedByReservedArea && paragraph.StartIndent > 0)
                        richText.Append($"<space={paragraph.StartIndent * sx}px>");

                    int charOffset = GetCharOffsetAtLine(wrappedText, absoluteLine, paragraph.TextSegments);
                    AppendRichText(richText, allLines[absoluteLine], paragraph.TextSegments, palette, charOffset);

                    tmp.richText = true;
                    tmp.text = richText.ToString();

                    _pageObjects.Add(paraGo);
                }

                cursorY = nextCursorY + paragraphHeight;

                if (paragraphSplit) {
                    // Paragraph was split — next page continues from this paragraph at the split point
                    RenderPageNumber();
                    return (paragraphIndex, startLine + linesToShow);
                }

                paragraphIndex++;
                currentLineOffset = 0; // Reset line offset for subsequent paragraphs
            }

            RenderPageNumber();

            return (paragraphIndex, 0);
        }

        public void Show() => bookCanvas.enabled = true;
        public void Hide() {
            ClearPage();
            bookCanvas.enabled = false;
        }

        public Cysharp.Threading.Tasks.UniTask ShowAsync() {
            Show();
            return Cysharp.Threading.Tasks.UniTask.CompletedTask;
        }

        public Cysharp.Threading.Tasks.UniTask HideAsync() {
            Hide();
            return Cysharp.Threading.Tasks.UniTask.CompletedTask;
        }

        /// <summary>
        /// Computes the effective left offset for a text line at the given screen Y position.
        /// Lines overlapping a reserved area are pushed right; lines below it use the base offset.
        /// </summary>
        private static float ComputeLineLeftOffset(
            Page page, float baseLeftOffset, float lineScreenY, float lineHeight
        ) {
            float leftOffset = baseLeftOffset;
            foreach (var ra in page.ReservedAreas) {
                if (lineScreenY < ra.Y2 && lineScreenY + lineHeight > ra.Y) {
                    float newLeft = (ra.X2 + 1) - page.XOffset;
                    if (newLeft > leftOffset)
                        leftOffset = newLeft;
                }
            }
            return leftOffset;
        }

        private void ClearPage() {
            foreach (var go in _pageObjects)
                Destroy(go);
            _pageObjects.Clear();
        }

        /// <summary>
        /// Returns the character offset in the original (unwrapped) text corresponding to the
        /// start of line number <paramref name="lineNumber"/> in the wrapped text.
        /// Accounts for \n insertions and stripped leading spaces.
        /// </summary>
        private static int GetCharOffsetAtLine(
            string wrappedText, int lineNumber, IReadOnlyList<TextSegment> segments
        ) {
            int totalChars = 0;
            foreach (var seg in segments)
                totalChars += seg.Text.Length;

            int currentLine = 0;
            int origIdx = 0;

            for (int i = 0; i < wrappedText.Length && currentLine < lineNumber; i++) {
                char ch = wrappedText[i];
                if (ch == '\n') {
                    currentLine++;
                    // Skip stripped leading spaces in original text (same logic as AppendRichText)
                    while (origIdx < totalChars && GetOrigChar(segments, origIdx) == ' ')
                        origIdx++;
                } else {
                    origIdx++;
                }
            }
            return origIdx;
        }

        /// <summary>
        /// Builds rich text from pre-wrapped text, applying per-segment formatting.
        /// Maps characters in the wrapped text (which has \n inserted and leading spaces removed)
        /// back to their source segments for correct color/style tags.
        /// </summary>
        private static void AppendRichText(
            System.Text.StringBuilder richText,
            string wrappedText,
            IReadOnlyList<TextSegment> segments,
            Color[] palette,
            int origCharOffset = 0
        ) {
            // Build a map: original char index → segment index
            int totalChars = 0;
            foreach (var seg in segments)
                totalChars += seg.Text.Length;

            var segMap = new int[totalChars];
            int ci = 0;
            for (int s = 0; s < segments.Count; s++) {
                for (int c = 0; c < segments[s].Text.Length; c++)
                    segMap[ci++] = s;
            }

            // Walk the wrapped text, tracking position in the original text.
            // The wrapper inserts \n and strips leading spaces on continuation lines,
            // so we advance the original index only for non-\n characters.
            // origCharOffset allows starting from a later position (for paragraph continuations).
            int origIdx = origCharOffset;
            int prevSeg = -1;

            foreach (char ch in wrappedText) {
                if (ch == '\n') {
                    if (prevSeg >= 0) {
                        CloseSegmentTags(richText, segments[prevSeg]);
                        prevSeg = -1;
                    }
                    richText.Append('\n');

                    // The wrapper stripped leading spaces — advance origIdx past them
                    while (origIdx < totalChars && GetOrigChar(segments, origIdx) == ' ')
                        origIdx++;
                    continue;
                }

                int seg = origIdx < segMap.Length ? segMap[origIdx] : 0;
                if (seg != prevSeg) {
                    if (prevSeg >= 0) CloseSegmentTags(richText, segments[prevSeg]);
                    OpenSegmentTags(richText, segments[seg], palette);
                    prevSeg = seg;
                }

                richText.Append(ch);
                origIdx++;
            }

            if (prevSeg >= 0) CloseSegmentTags(richText, segments[prevSeg]);
        }

        private static char GetOrigChar(IReadOnlyList<TextSegment> segments, int index) {
            foreach (var seg in segments) {
                if (index < seg.Text.Length)
                    return seg.Text[index];
                index -= seg.Text.Length;
            }
            return '\0';
        }

        private static void OpenSegmentTags(
            System.Text.StringBuilder sb, TextSegment segment, Color[] palette
        ) {
            Color color = segment.Color < palette.Length
                ? palette[segment.Color]
                : Color.white;
            string hex = ColorUtility.ToHtmlStringRGB(color);
            sb.Append($"<color=#{hex}>");

            if ((segment.FontStyle & GameData.Resources.Book.FontStyle.Bold) != 0)
                sb.Append("<b>");
            if ((segment.FontStyle & GameData.Resources.Book.FontStyle.Italic) != 0)
                sb.Append("<i>");
            if ((segment.FontStyle & GameData.Resources.Book.FontStyle.Underlined) != 0)
                sb.Append("<u>");
        }

        private static void CloseSegmentTags(System.Text.StringBuilder sb, TextSegment segment) {
            if ((segment.FontStyle & GameData.Resources.Book.FontStyle.Underlined) != 0)
                sb.Append("</u>");
            if ((segment.FontStyle & GameData.Resources.Book.FontStyle.Italic) != 0)
                sb.Append("</i>");
            if ((segment.FontStyle & GameData.Resources.Book.FontStyle.Bold) != 0)
                sb.Append("</b>");
            sb.Append("</color>");
        }

        private static TextAlignmentOptions MapAlignment(GameData.Resources.Book.TextAlignment alignment) {
            return alignment switch {
                GameData.Resources.Book.TextAlignment.Left => TextAlignmentOptions.TopLeft,
                GameData.Resources.Book.TextAlignment.Right => TextAlignmentOptions.TopRight,
                GameData.Resources.Book.TextAlignment.Center => TextAlignmentOptions.Top,
                GameData.Resources.Book.TextAlignment.Justify => TextAlignmentOptions.TopJustified,
                _ => TextAlignmentOptions.TopLeft
            };
        }
    }
}
