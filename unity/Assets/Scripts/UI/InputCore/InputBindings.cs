namespace BakAgain.UI.InputCore {
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.InputSystem;

    /// <summary>One remappable binding: an action's binding at <see cref="Index"/> (a composite's part
    /// for the walking keys).</summary>
    public sealed class RebindSlot {
        public RebindSlot(string id, string group, string labelKey, InputAction action, int index,
            string letter = null, bool second = false, string section = null, bool mouseButton = false) {
            Id = id;
            Group = group;
            LabelKey = labelKey;
            Action = action;
            Index = index;
            Letter = letter;
            Second = second;
            Section = section;
            MouseButton = mouseButton;
            DefaultPath = action.bindings[index].path;
        }

        /// <summary>The slot's PlayerPrefs name, after <see cref="InputBindings.PrefsPrefix"/>.</summary>
        public string Id { get; }

        /// <summary>The keys that are live together (<see cref="InputBindings.Walk"/>, <see cref="InputBindings.Combat"/>,
        /// <see cref="InputBindings.Menus"/>): a key swaps only with a slot in its own group.</summary>
        public string Group { get; }

        /// <summary>The UiTemplates key of the slot's caption.</summary>
        public string LabelKey { get; }

        /// <summary>A hotkey's letter or digit, for its caption; null otherwise.</summary>
        public string Letter { get; }

        /// <summary>The action's second key (the arrows beside WASD, keypad Enter beside Enter).</summary>
        public bool Second { get; }

        /// <summary>The page heading it is listed under when not its group's own (the hotkeys, the modifiers).</summary>
        public string Section { get; }

        /// <summary>Click or inspect: only a mouse button can take it (UI Toolkit needs a pointer's button).</summary>
        public bool MouseButton { get; }

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

        /// <summary>The slot groups: travel, the fight, and every menu and hotkey.</summary>
        public const string Walk = "walk", Combat = "combat", Menus = "menus";

        /// <summary>The slot's saved binding, or null while it keeps its default.</summary>
        public static string Saved(string id) {
            string saved = PlayerPrefs.GetString(PrefsPrefix + id, null);
            return string.IsNullOrEmpty(saved) ? null : saved;
        }

        /// <summary>Raised after a load, a change or a reset, for what mirrors a binding elsewhere
        /// (<see cref="UiPointerBindings"/>).</summary>
        public static event System.Action Changed;

        /// <summary>Applies every saved binding. A slot with nothing saved keeps its default.</summary>
        public static void Load(IEnumerable<RebindSlot> slots) {
            foreach (RebindSlot slot in slots) {
                string saved = Saved(slot.Id);
                if (saved != null) {
                    slot.Action.ApplyBindingOverride(slot.Index, saved);
                }
            }
            Changed?.Invoke();
        }

        /// <summary>Binds <paramref name="slot"/> to <paramref name="path"/> and saves it; a slot that
        /// held the path takes this slot's old one.</summary>
        public static void Set(IReadOnlyList<RebindSlot> slots, RebindSlot slot, string path) {
            string old = slot.Path;
            foreach (RebindSlot other in slots) {
                if (other != slot && other.Group == slot.Group && SameKey(other.Path, path)) {
                    Apply(other, old);
                }
            }
            Apply(slot, path);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>Back to the defaults; the saved bindings are forgotten.</summary>
        public static void Reset(IEnumerable<RebindSlot> slots) {
            foreach (RebindSlot slot in slots) {
                slot.Action.RemoveBindingOverride(slot.Index);
                PlayerPrefs.DeleteKey(PrefsPrefix + slot.Id);
            }
            PlayerPrefs.Save();
            Changed?.Invoke();
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

        // The same key, however it is written: a hotkey's "#(m)" (the key that prints M) is "<Keyboard>/m" on
        // a US layout, and a rebind always records the second form.
        private static bool SameKey(string a, string b) {
            if (string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
            InputControl ca = InputSystem.FindControl(a);
            return ca != null && ca == InputSystem.FindControl(b);
        }
    }
}
