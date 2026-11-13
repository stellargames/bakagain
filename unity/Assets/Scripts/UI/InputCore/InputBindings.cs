namespace BakAgain.UI.InputCore {
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.InputSystem;

    /// <summary>One remappable binding: an action's binding at <see cref="Index"/> (a composite's part
    /// for the walking keys).</summary>
    public sealed class RebindSlot {
        public RebindSlot(string id, string labelKey, InputAction action, int index) {
            Id = id;
            LabelKey = labelKey;
            Action = action;
            Index = index;
            DefaultPath = action.bindings[index].path;
        }

        /// <summary>The slot's PlayerPrefs name, after <see cref="InputBindings.PrefsPrefix"/>.</summary>
        public string Id { get; }

        /// <summary>The UiTemplates key of the slot's caption.</summary>
        public string LabelKey { get; }

        public InputAction Action { get; }
        public int Index { get; }
        public string DefaultPath { get; }
        public string Path => Action.bindings[Index].effectivePath;
    }

    /// <summary>
    /// The player's remapped keys (TASK-1080): saved per slot in PlayerPrefs, like the other options,
    /// and applied over the defaults when the input source starts. A key another slot holds swaps
    /// with it, so no two slots ever share one.
    /// </summary>
    public static class InputBindings {
        public const string PrefsPrefix = "input binding ";

        /// <summary>The slot's saved binding, or null while it keeps its default.</summary>
        public static string Saved(string id) {
            string saved = PlayerPrefs.GetString(PrefsPrefix + id, null);
            return string.IsNullOrEmpty(saved) ? null : saved;
        }

        /// <summary>Applies every saved binding. A slot with nothing saved keeps its default.</summary>
        public static void Load(IEnumerable<RebindSlot> slots) {
            foreach (RebindSlot slot in slots) {
                string saved = Saved(slot.Id);
                if (saved != null) {
                    slot.Action.ApplyBindingOverride(slot.Index, saved);
                }
            }
        }

        /// <summary>Binds <paramref name="slot"/> to <paramref name="path"/> and saves it; a slot that
        /// held the path takes this slot's old one.</summary>
        public static void Set(IReadOnlyList<RebindSlot> slots, RebindSlot slot, string path) {
            string old = slot.Path;
            foreach (RebindSlot other in slots) {
                if (other != slot && SamePath(other.Path, path)) {
                    Apply(other, old);
                }
            }
            Apply(slot, path);
            PlayerPrefs.Save();
        }

        /// <summary>Back to the defaults; the saved bindings are forgotten.</summary>
        public static void Reset(IEnumerable<RebindSlot> slots) {
            foreach (RebindSlot slot in slots) {
                slot.Action.RemoveBindingOverride(slot.Index);
                PlayerPrefs.DeleteKey(PrefsPrefix + slot.Id);
            }
            PlayerPrefs.Save();
        }

        private static void Apply(RebindSlot slot, string path) {
            if (path == slot.DefaultPath) {
                slot.Action.RemoveBindingOverride(slot.Index);
                PlayerPrefs.DeleteKey(PrefsPrefix + slot.Id);
            } else {
                slot.Action.ApplyBindingOverride(slot.Index, path);
                PlayerPrefs.SetString(PrefsPrefix + slot.Id, path);
            }
        }

        private static bool SamePath(string a, string b) => string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);
    }
}
