namespace BakAgain.UI.InGame {
    using System;
    using BakAgain.Core;
    using BakAgain.ResourceManagement;
    using Cysharp.Threading.Tasks;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// Draws the party portrait "heads" (HEADS.BMX) into the REQ_MAIN party-member slots.
    /// Faithful to the original addHeads (KRONDOR.EXE @ 0x166a7), but positions/sizes are
    /// inherited from the REQ click-area elements (hotspot_2/3/4) — no VGA constants here.
    /// The head id for an active slot is the character roster index (ActivePartyIndices[slot]).
    ///
    /// Plain C# (mirrors CompassView). Event-driven: re-renders on GameSession
    /// .PartyCompositionChanged, so it does no per-frame work. Reusable — the future
    /// inventory screen (which also has party-member click areas) can attach the same view.
    /// </summary>
    public sealed class PartyHeadsView {
        // REQ_MAIN party-member ActionIds are 2,3,4 (three slots) -> elements "hotspot_{id}".
        /// <inheritdoc cref="GameData.Resources.Character.ActiveParty.Slots"/>
        private const int SlotCount = GameData.Resources.Character.ActiveParty.Slots;

        /// <summary>REQ_MAIN's first party click area. Other screens put the portraits in the SAME
        /// PLACE under DIFFERENT action ids (REQ_CAST uses 128..130), so this is a default, not a
        /// constant.</summary>
        public const int DefaultFirstActionId = 2;

        private readonly int _firstActionId;
        private const string HeadsBmx = "HEADS.BMX";

        private readonly GameSession _session;
        private readonly IResourceProviderService _resources;
        private readonly int _placeholderHeadId;
        private readonly VisualElement[] _heads = new VisualElement[SlotCount];
        private bool _subscribed;
        private int _generation;

        public PartyHeadsView(GameSession session, IResourceProviderService resources,
            int placeholderHeadId = -1, int firstActionId = DefaultFirstActionId) {
            _firstActionId = firstActionId;
            _session = session;
            _resources = resources;
            _placeholderHeadId = placeholderHeadId;
        }

        /// <summary>Resolve a slot to its HEADS.BMX sub-index: the active member's roster id,
        /// or <paramref name="placeholderId"/> for empty/out-of-range slots (-1 = hide).</summary>
        public static int ResolveHeadId(int slot, int activeCount, byte[] indices, int placeholderId) {
            if (slot < 0 || slot >= activeCount || indices == null || slot >= indices.Length) {
                return placeholderId;
            }
            return indices[slot];
        }

        /// <summary>Create one head child per party-member click area and subscribe to roster
        /// changes. Call after the REQ panel is built. Does not load sprites — call
        /// <see cref="RenderAsync"/> for that.</summary>
        public void Attach(VisualElement panelRoot) {
            if (panelRoot == null) {
                return;
            }
            for (int i = 0; i < SlotCount; i++) {
                VisualElement hotspot = panelRoot.Q(name: $"hotspot_{_firstActionId + i}");
                if (hotspot == null) {
                    Debug.LogWarning($"PartyHeadsView: REQ hotspot_{_firstActionId + i} not found; slot {i} skipped.");
                    continue;
                }
                var head = new VisualElement {
                    name = $"partyhead_{i}",
                    pickingMode = PickingMode.Ignore, // clicks pass through to the hotspot
                    style = {
                        position = Position.Absolute,
                        left = 0, top = 0, right = 0, bottom = 0, // fill the click area (REQ pos+size)
                        display = DisplayStyle.None,
                    },
                };
                hotspot.Add(head);
                _heads[i] = head;
            }
            if (!_subscribed) {
                _session.PartyCompositionChanged += OnPartyCompositionChanged;
                _subscribed = true;
            }
        }

        /// <summary>(Re)load and apply the head sprite for every slot. Async loads are
        /// generation-guarded so a superseded render or a Dispose() drops stale results.</summary>
        public async UniTask RenderAsync() {
            int gen = ++_generation;
            for (int i = 0; i < SlotCount; i++) {
                VisualElement head = _heads[i];
                if (head == null) {
                    continue;
                }
                int id = ResolveHeadId(i, _session.ActivePartyCount, _session.ActivePartyIndices, _placeholderHeadId);
                if (id < 0) {
                    head.style.display = DisplayStyle.None;
                    continue;
                }
                Sprite sprite = await _resources.LoadAssetAsync<Sprite>($"{HeadsBmx}#{id}", this);
                if (gen != _generation) {
                    return; // superseded or disposed while awaiting
                }
                if (sprite == null) {
                    head.style.display = DisplayStyle.None;
                    Debug.LogWarning($"PartyHeadsView: {HeadsBmx}#{id} not found; slot {i} hidden.");
                    continue;
                }
                head.style.display = _hidden ? DisplayStyle.None : DisplayStyle.Flex;
                head.style.backgroundImage = Background.FromSprite(sprite);
                head.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                head.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left);
                head.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top);
            }
        }

        private void OnPartyCompositionChanged() => RenderAsync().Forget();

        /// <summary>
        /// Show or hide all three portraits.
        /// </summary>
        /// <remarks>
        /// <b>A fight hides them.</b> COMBAT.DAT and SHOOT.DAT carry no portrait elements — only the
        /// hidden character-screen zone sits in that region — so the arena draws one head of its
        /// own (the acting character's) and none of the travel HUD's three. Leaving ours up put two
        /// of them behind the arena's parchment and the third beside a panel naming somebody else.
        ///
        /// <para>Hiding rather than removing: the REQ hotspots underneath are the same elements the
        /// travel screen rebuilds against, and the fight ends by showing these again.</para>
        ///
        /// <para><b>Only a CHANGE does anything.</b> <c>InGameScreen.Update</c> calls this every
        /// frame, and a show used to re-render unconditionally: three HEADS.BMX loads a frame whose
        /// handles the provider keeps until <see cref="Dispose"/> — 574,101 on one view in a session
        /// (TASK-576). The first show needs no render of its own: the owner renders on attach.</para>
        /// </remarks>
        public void SetVisible(bool visible) {
            if (_hidden == !visible) {
                return;
            }
            _hidden = !visible;
            for (var i = 0; i < SlotCount; i++) {
                if (_heads[i] == null) {
                    continue;
                }
                if (_hidden) {
                    _heads[i].style.display = DisplayStyle.None;
                }
            }
            if (visible) {
                RenderAsync().Forget();
            }
        }

        // Set while a fight is on: RenderAsync must not put the heads back when the roster changes
        // mid-fight (a summon does exactly that).
        private bool _hidden;

        /// <summary>Unsubscribe, remove head children, release loaded sprites.</summary>
        public void Dispose() {
            _generation++; // invalidate any in-flight RenderAsync
            if (_subscribed) {
                _session.PartyCompositionChanged -= OnPartyCompositionChanged;
                _subscribed = false;
            }
            for (int i = 0; i < SlotCount; i++) {
                _heads[i]?.RemoveFromHierarchy();
                _heads[i] = null;
            }
            _resources.ReleaseAssets(this);
        }
    }
}
