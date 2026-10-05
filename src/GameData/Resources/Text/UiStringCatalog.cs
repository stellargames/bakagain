namespace GameData.Resources.Text;

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

/// <summary>
/// Player-visible strings lifted out of KRONDOR.EXE. The real catalog will use keys formatted as
/// <c>base:uistring:&lt;group&gt;.&lt;name&gt;</c>, but Get/TryGet are key-format-agnostic and will work with any string key.
/// Shipped as an embedded resource rather than read from the executable at runtime — see
/// docs/superpowers/specs/2026-08-07-exe-ui-string-catalog-design.md.
/// </summary>
public sealed class UiStringCatalog {
    public const string ResourceId = "uistrings.json";

    private readonly Dictionary<string, string> _entries;

    private UiStringCatalog(Dictionary<string, string> entries, string locale = "en") {
        _entries = entries;
        Locale = locale;
    }

    /// <summary>The language the entries are in — what a template's plural rules follow (TASK-776).</summary>
    public string Locale { get; }

    public IReadOnlyDictionary<string, string> Entries => _entries;

    /// <summary>The text for a key, or empty when absent. Never the key itself: a raw key on
    /// screen is the failure this catalog exists to remove.</summary>
    public string Get(string key) => _entries.TryGetValue(key, out string v) ? v : "";

    public bool TryGet(string key, out string value) => _entries.TryGetValue(key, out value);

    /// <summary>A catalog of exactly these entries.</summary>
    public static UiStringCatalog From(IEnumerable<KeyValuePair<string, string>> entries) {
        var copy = new Dictionary<string, string>();
        foreach (KeyValuePair<string, string> kv in entries) {
            copy[kv.Key] = kv.Value;
        }
        return new UiStringCatalog(copy);
    }

    /// <summary>Parses a flat key/value JSON document into a catalog.</summary>
    /// <exception cref="JsonException">The input is not well-formed JSON. Deliberately not
    /// caught here: a parse function that swallows malformed input would silently ship a
    /// half-empty UI instead of telling anyone. Callers loading a mod-supplied file (as opposed
    /// to the trusted embedded resource) must catch this and fall back to
    /// <see cref="Embedded"/> — see <c>UiStringLoader</c>.</exception>
    public static UiStringCatalog FromJson(string json) {
        var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
            ?? new Dictionary<string, string>();
        return new UiStringCatalog(parsed);
    }

    /// <summary>Per-entry override, later-source-wins — the rule <c>ContentRegistry.Merge</c>
    /// uses, so a translation can replace part of the catalog without restating all of it.</summary>
    public UiStringCatalog Merge(UiStringCatalog? over) {
        var merged = new Dictionary<string, string>(_entries);
        if (over != null) {
            foreach (KeyValuePair<string, string> kv in over._entries) {
                merged[kv.Key] = kv.Value;
            }
        }
        return new UiStringCatalog(merged, Locale);
    }

    /// <summary>This catalog with every entry <paramref name="pack"/> translates replaced (TASK-773);
    /// untranslated entries keep the original English.</summary>
    public UiStringCatalog TranslatedBy(LanguagePack pack) {
        var translated = new Dictionary<string, string>(_entries);
        foreach (string key in _entries.Keys) {
            if (pack.TryTranslate(key, _entries[key], out string text)) {
                translated[key] = text;
            }
        }
        return new UiStringCatalog(translated, string.IsNullOrEmpty(pack.Locale) ? Locale : pack.Locale);
    }

    private static UiStringCatalog? _embedded;

    /// <summary>The catalog compiled into this assembly. One copy, nothing to hand-sync.</summary>
    public static UiStringCatalog Embedded {
        get {
            if (_embedded == null) {
                Assembly asm = typeof(UiStringCatalog).Assembly;
                string? name = null;
                foreach (string candidate in asm.GetManifestResourceNames()) {
                    if (candidate.EndsWith(ResourceId, StringComparison.Ordinal)) {
                        name = candidate;
                    }
                }
                if (name == null) {
                    _embedded = new UiStringCatalog(new Dictionary<string, string>());
                } else {
                    using Stream s = asm.GetManifestResourceStream(name);
                    using var r = new StreamReader(s);
                    _embedded = FromJson(r.ReadToEnd());
                    // The port's templates, composed from the EXE's own pieces (TASK-776), so a
                    // pack translates them and the POT lists them like any other entry.
                    foreach (KeyValuePair<string, string> template in UiTemplates.EnglishFor(_embedded)) {
                        _embedded._entries[template.Key] = template.Value;
                    }
                }
            }
            return _embedded;
        }
    }
}
