namespace BakAgain.Core.Services {
    using System;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Config;

    /// <summary>
    /// Holds the player's committed <see cref="Preferences"/> (the equivalent of
    /// the original game's <c>pConfiguration</c> / KRONDOR.CFG) and persists them
    /// to Unity storage. The Preferences screen edits a working copy
    /// (<c>Current.Clone()</c>) and commits it via <see cref="Apply"/> on OK,
    /// mirroring dialog_Preferences' copy-on-entry / apply-on-OK behaviour.
    /// </summary>
    public interface IPreferencesService {
        /// <summary>The committed preferences. Read on demand; subscribe to
        /// <see cref="Changed"/> rather than caching the values.</summary>
        Preferences Current { get; }

        /// <summary>Raised whenever <see cref="Current"/> changes (initial load,
        /// Apply). Effect consumers (audio, detail level, text speed) hook here.</summary>
        event Action Changed;

        /// <summary>Loads preferences from Unity storage, seeding from the shipped
        /// defaults (DEFAULT.DAT) on first run. Idempotent.</summary>
        UniTask EnsureLoadedAsync();

        /// <summary>Returns a fresh copy of the shipped default preferences
        /// (DEFAULT.DAT) — used by the "Defaults" button.</summary>
        UniTask<Preferences> GetDefaultsAsync();

        /// <summary>Commits <paramref name="working"/> as the new current
        /// preferences, persists them, and raises <see cref="Changed"/>.</summary>
        void Apply(Preferences working);
    }
}
