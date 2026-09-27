namespace BakAgain.UI {
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Dialog;
    using System.Threading;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// Show modal DDX dialogs. Backed by a persistent <c>UIDocument</c> overlay
    /// that lives outside any state-controlled GameObject — so callers can fire
    /// dialogs from any state without worrying about which UI prefab is active.
    /// <para>
    /// Every show call takes only a <see cref="DialogEntry"/>. Environmental
    /// state (palette) is held by the manager via
    /// <see cref="SetActivePalette"/>; the rendering mode (narrative dismiss vs
    /// fire-and-forget) is picked by the caller via the method it invokes.
    /// </para>
    /// </summary>
    public interface IDialogManager {
        /// <summary>
        /// Render <paramref name="entry"/> and await dismissal on mouse / key.
        /// The narrative path — branches are not surfaced as buttons; the engine
        /// would silently evaluate them to pick a continuation. Used by the
        /// in-game / menu / chapter-intro callers and the cutscene's
        /// wait-for-input frames.
        /// </summary>
        UniTask ShowEntry(DialogEntry entry, CancellationToken cancellationToken = default);

        /// <summary>
        /// Render a resolved <see cref="DialogPlay"/> — the entry plus the text-variable slots the
        /// branch walk accumulated for it. Prefer this over the bare-entry overload whenever the
        /// caller resolved through <c>DialogExecutor</c>: the bare overload has to seed its own
        /// slots, which drops the writes made by the text-less router entries the walk passed
        /// through (57 shipped entries do exactly that).
        /// </summary>
        UniTask ShowEntry(DialogPlay play, CancellationToken cancellationToken = default);

        /// <summary>
        /// Render <paramref name="entry"/> and return as soon as the panel is
        /// on-screen. The panel stays up until <see cref="ClearDialog"/> (or a
        /// subsequent show call) disposes it. Used by the cutscene
        /// narrative-auto-advance path which drives its own pacing.
        /// </summary>
        UniTask DisplayEntry(DialogEntry entry, CancellationToken cancellationToken = default);

        /// <summary>Non-blocking render of a resolved play. See <see cref="ShowEntry(DialogPlay,
        /// CancellationToken)"/> for why a play beats a bare entry.</summary>
        UniTask DisplayEntry(DialogPlay play, CancellationToken cancellationToken = default);

        /// <summary>
        /// Build a dialog-styled box — <paramref name="entry"/>'s chrome, at <paramref name="entry"/>'s
        /// area — as a child of <paramref name="host"/>, and return it for the caller to draw into.
        /// No text is rendered.
        ///
        /// <para>For a screen that needs the real parchment behind content it lays out itself. It
        /// must be built into the caller's own visual tree rather than shown as a dialog: the dialog
        /// overlay is a separate <c>UIDocument</c> on a higher <c>sortingOrder</c>, so a panel shown
        /// through it covers anything the calling screen draws. Style, area and palette resolution
        /// stay here, where they already live.</para>
        ///
        /// <para>Returns null when the entry can't be styled; the caller keeps its own fallback.</para>
        /// </summary>
        UniTask<VisualElement> BuildStyledBoxAsync(DialogEntry entry, VisualElement host);

        /// <summary>
        /// Resolve a dialog id to the play the walk lands on, without showing anything.
        /// </summary>
        /// <returns>The resolved play, or null when the id yields nothing to show.</returns>
        /// <remarks>
        /// For a caller that must inspect the entry before choosing how to present it. An
        /// interactive location does exactly that: the original picks between drawing the
        /// description into the scene and opening the dialog window <i>per entry</i>, on its type and
        /// whether it branches — see <c>GdsSceneInteraction.ExamineStyleFor</c>. That choice is the
        /// location's, not this manager's, so it needs the entry first.
        /// </remarks>
        UniTask<DialogPlay> ResolveById(int id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Hide any current panel and reset overlay state. The cutscene
        /// "clear dialog" frame command (DialogCommand 0/255) calls this between
        /// narrative beats.
        /// </summary>
        void ClearDialog();

        /// <summary>
        /// Let presses through the panel a <see cref="DisplayEntry(DialogPlay, CancellationToken)"/>
        /// left up, to whatever screen is underneath. For a caller that ends the view on a press
        /// anywhere, as the original's item description does (INVINSP.C:423-425).
        /// </summary>
        void LetClicksThroughPanel();

        /// <summary>
        /// Set the palette dialog text and chrome are resolved against (each
        /// text/chrome pen is a palette index in the original engine). Cutscene
        /// frames call this whenever their active palette changes; when never
        /// set, the manager falls back to <c>OPTIONS.PAL</c>.
        /// </summary>
        void SetActivePalette(Color[] palette);

        /// <summary>
        /// Convenience: load the DDX file matching <paramref name="id"/>, find
        /// the entry, and call <see cref="ShowEntry"/>.
        /// </summary>
        /// <summary>
        /// Shows a dialog and answers what it RETURNED — <c>ExecuteDialog</c>'s own result.
        /// </summary>
        /// <returns>
        /// The value a <c>SetReturnValue</c> action set, or 0 when the dialog simply ran out of
        /// lines. Most callers ignore it; a location hotspot does not — see
        /// <c>GdsSceneRules.OutcomeFor</c>, whose five-entry table maps it onto the scene action
        /// that actually runs.
        /// </returns>
        UniTask<int> ShowById(int id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Confirm/choice path: load the entry, render one button per branch
        /// (labelled from <c>KEYWORD.DAT</c> via the branch's keyword index),
        /// await selection, and return <c>true</c> when the first branch (Yes)
        /// was chosen. For the preferences-style Yes/No confirm dialogs whose
        /// <c>ConditionalBranch</c> carries a <c>FlagCondition</c> whose
        /// <c>Flag</c> doubles as a keyword index.
        /// </summary>
        UniTask<bool> ShowConfirmById(int id, CancellationToken cancellationToken = default);

        /// <summary>
        /// The same choice path, but answering <b>which</b> branch was taken rather than only
        /// whether it was the first — the flag its <c>FlagCondition</c> carries, or -1 when nothing
        /// could be resolved.
        /// </summary>
        /// <remarks>
        /// Needed wherever a dialog offers more than yes/no. The shop's buy offer is the case in
        /// hand: 260 accept, 262 haggle, 261 decline, and collapsing it to a bool makes haggling
        /// indistinguishable from walking away.
        /// </remarks>
        UniTask<int> ShowChoiceById(int id, CancellationToken cancellationToken = default);

        /// <summary>
        /// The same choice path, answering with the chosen branch's <b>position</b> — which is what
        /// the original's <c>dialog_Show</c> returns.
        /// </summary>
        /// <returns>The 0-based branch index, or -1 when nothing could be resolved.</returns>
        /// <remarks>
        /// <b>The position is the primitive; the flag is the convenience.</b>
        /// <c>ShowDialogChoiceMenu</c> @0x4b54c returns the index of the option clicked, and
        /// <c>ExecuteDialog</c> hands that straight back when no <c>SetReturnValue</c> action fires —
        /// which is the common case. Rules ported against the original's return value therefore
        /// speak in positions, and converting to the branch's flag first (see
        /// <see cref="ShowChoiceById"/>) makes them unrecognisable: the temple's service menu treats
        /// 1 as "heal" where the flag for that option is 272.
        /// </remarks>
        UniTask<int> ShowChoiceIndexById(int id, CancellationToken cancellationToken = default);
    }
}
