namespace BakAgain.EditorTools.UI {
    using BakAgain.UI;
    using GameData.Resources.Dialog;
    using GameData.Resources.Layout;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// Renders dialog panels straight into the Game view so the flowed body text can be LOOKED at
    /// — line pitch, break points, the paragraph indent, the speaker title — without driving a
    /// whole play session to a dialog.
    ///
    /// <para>An editor tool, not a test: <see cref="BakAgain.Tests.PlayMode.UI"/> already pins the
    /// geometry numerically. This exists because "the numbers are right" and "it looks like the
    /// original" are different claims, and only one of them can be screenshotted.</para>
    /// </summary>
    internal static class DialogFlowPreview {
        private const string HostName = "DialogFlowPreview";

        [MenuItem("Tools/BaK/Dialog/Preview flowed dialog text")]
        internal static void Show() {
            Hide();

            // DontSave: the preview is scaffolding, and a scene that quietly gained a UIDocument
            // full of dialog panels would be a nasty diff to find later.
            var host = new GameObject(HostName) { hideFlags = HideFlags.DontSave };
            var document = host.AddComponent<UIDocument>();
            document.panelSettings =
                AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Settings/UI/PanelSettings.asset");
            document.sortingOrder = 900;
            VisualElement root = document.rootVisualElement;

            Color[] palette = Palette();

            // The bordered info box the main menu shows — the shipped row-2 rect, with the
            // tab-indented paragraph opening it.
            Add(root, 65f, 40f, 1470f, 606f, BoxedStyle(),
                new DialogEntry {
                    Text = "\tLeft clicking on a member of your party will show you that person's "
                        + "inventory and statistics. Right clicking will bring up more options for "
                        + "that character, including the ability to give an item to another member "
                        + "of the party.",
                    DialogType = DialogType.Normal,
                }, palette);

            // A titled panel with a centred speaker line above centred body text.
            Add(root, 65f, 690f, 1470f, 480f, BoxedStyle(),
                new DialogEntry {
                    Text = "#Gorath#±Into ±a ±Dark ±Night\n\nThe moredhel warrior says nothing for a "
                        + "long moment... Then he turns away, and the silence between you grows "
                        + "colder than the northern wind.",
                    DialogType = DialogType.Normal,
                    Flags = DialogEntryFlags.CenterText,
                }, palette);
        }

        [MenuItem("Tools/BaK/Dialog/Hide flowed-dialog preview")]
        internal static void Hide() {
            GameObject existing = GameObject.Find(HostName);
            if (existing != null) {
                Object.DestroyImmediate(existing);
            }
        }

        private static void Add(VisualElement root, float left, float top, float width, float height,
            DialogStyle style, DialogEntry entry, Color[] palette) {
            VisualElement panel = DialogPanelBuilder.BuildPanel(entry, style, new DialogLayout(), palette);
            panel.style.left = left;
            panel.style.top = top;
            panel.style.width = width;
            panel.style.height = height;
            root.Add(panel);
        }

        private static DialogStyle BoxedStyle() => new DialogStyle {
            FillPenColor = 0x01,
            BorderPenColor = 0x01,
            ShadowPenColor = 0x04,
            BodyTextPenColor = 0x00,
            TextShadowPenSource = 0x00,
            DefaultArea = LayoutHint.PxRect(65f, 66f, 1470f, 606f),
            TextPadLeft = 50f,
            TextPadRight = 50f,
        };

        // Stand-in for OPTIONS.PAL: only the pens the dialog chrome and text actually reach have
        // to be right for the preview to be readable.
        private static Color[] Palette() {
            var palette = new Color[256];
            for (int i = 0; i < palette.Length; i++) {
                palette[i] = Color.magenta;
            }
            palette[0x00] = Color.black;
            palette[0x01] = new Color(0.29f, 0.18f, 0.08f);   // dark brown
            palette[0x04] = new Color(0.86f, 0.70f, 0.31f);   // gold bevel
            palette[0x05] = new Color(0.93f, 0.83f, 0.55f);   // cream highlight
            palette[0x0A] = new Color(1.00f, 0.94f, 0.72f);   // bright cream
            palette[0x0B] = new Color(0.52f, 0.35f, 0.16f);
            palette[0x0F] = new Color(0.98f, 0.98f, 0.90f);
            return palette;
        }
    }
}
