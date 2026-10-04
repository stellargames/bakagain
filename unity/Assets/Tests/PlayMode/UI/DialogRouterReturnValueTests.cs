namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.Tests.TestSupport;
    using BakAgain.Core;
    using BakAgain.Core.Services;
    using BakAgain.UI;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Content;
    using GameData.Resources.Dialog;
    using GameData.Resources.Dialog.Actions;
    using GameData.Resources.Dialog.Branches;
    using GameData.Resources.GameState;
    using GameData.Resources.Location;
    using Microsoft.Extensions.Logging.Abstractions;
    using NUnit.Framework;
    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// A dialog whose only job is to answer — no text, no speaker, no menu — still answers.
    /// </summary>
    /// <remarks>
    /// <b>This is the whole GDS action-override mechanism.</b> <c>GdsSceneRules.OutcomeFor</c>
    /// translates -1/-2/-3/-4/-5 and leaves everything else alone, so a router that answered 0
    /// silently left every hotspot on its own action code. Twenty of the ninety-seven entries
    /// carrying <c>SetReturnValue</c> in the shipped DDX files are exactly this shape.
    ///
    /// <para>Measured at Northwarden on 2026-09-13: dialog 1500156 resolves to DIAL_Z15 offset
    /// 25857, whose actions are <c>SetVar 17</c> and <c>SetReturnValue(-4)</c>. The walk reached it
    /// and the executor held -4 while <c>ShowById</c> answered 0, so the Great Hall hotspot opened
    /// the hall instead of taking the sub-scene transition. TASK-485.</para>
    /// </remarks>
    public class DialogRouterReturnValueTests {
        private const int RouterId = 1500156;
        private const int RouterOffset = 25808;
        private const int LeafOffset = 25857;
        private const int GateFlag = 7633;
        private const int OfferAnswer = -2;
        private const int BusyAnswer = -5;

        private GameObject _go;
        private PanelSettings _panelSettings;

        [TearDown]
        public void TearDown() {
            if (_go != null) {
                Object.DestroyImmediate(_go);
            }
            if (_panelSettings != null) {
                Object.DestroyImmediate(_panelSettings);
            }
        }

        [UnityTest]
        public IEnumerator ATextLessRouterAnswersItsReturnValueRatherThanZero() {
            // The gate flag SET, so the third conditional fails; the chapter ones fail because
            // Var 7 reads 0 with no save hydrated, which is outside both 1..4 and 6..9. Only the
            // default is left, and it reaches the leaf.
            //
            // *** Var 7 CANNOT be set from a bare session, and the first version of this tried. ***
            // `SetGlobalValue(30007, ...)` routes to GameStateEventFields.Field.Chapter and writes
            // the property, while `GetGlobalValue(30007)` falls through to the parsed save — so the
            // "chapter 4" control read 0 and took the default anyway, passing for the wrong reason.
            var session = new GameSession();
            session.SetGlobalFlag(GateFlag, true);

            int answered = 0;
            yield return Show(session, a => answered = a);

            Assert.AreEqual(-4, answered,
                "the router's SetReturnValue is its answer, panel or no panel");
        }

        [UnityTest]
        public IEnumerator ARouterThatEndsBeforeTheLeafStillAnswersZero() {
            // The control: with the gate flag CLEAR the third conditional wins, the walk stops on
            // the router itself, and nothing set a return value — so 0 is right here. Without this
            // the test above would pass for an implementation that answered -4 unconditionally.
            var session = new GameSession();
            session.SetGlobalFlag(GateFlag, false);

            int answered = -1;
            yield return Show(session, a => answered = a);

            Assert.AreEqual(0, answered, "a walk that set no return value answers nothing");
        }

        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator ASkipWaitChainPlaysThroughWithNoClick() {
            // TASK-496: dialog_wait_for_acknowledge returns at once on 0x4000, so the inn's two room
            // lines play straight on. Without the flag this chain would sit on its first line waiting
            // for a click the test never sends, and ShowById would never complete.
            var session = new GameSession();

            int answered = -1;
            yield return Show(session, a => answered = a, SkipWaitChain());

            Assert.AreEqual(0, answered, "a chain with no pick and no SetReturnValue answers 0");
        }

        private IEnumerator Show(GameSession session, System.Action<int> report, Dialog dialog = null) {
            _go = new GameObject("DialogRouterUnderTest");
            var document = _go.AddComponent<UIDocument>();
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            document.panelSettings = _panelSettings;

            var executor = new DialogExecutor(
                NullLogger<DialogExecutor>.Instance, session, clock: null, provider: null);
            executor.UseResourceLoader(new OneDialogLoader(dialog ?? Router()));

            var manager = _go.AddComponent<DialogManager>();
            manager.Construct(cursorManager: null, stack: null, menuSound: null,
                gameSession: session, executor: executor, preferences: null);

            UniTask<int> showing = manager.ShowById(RouterId);
            // A text-less router completes at once, but a TEXT entry lays out, wipes open and settles
            // its pagination before it can skip the wait, which takes many frames. Bounded so a wait
            // that never ends still fails instead of hanging — in TIME, not frames: run alone, the
            // first text entry also pays the cold Addressables and font start, and 300 frames ran
            // out before it did (TASK-788). The full suite had always warmed it up first.
            float deadline = Time.realtimeSinceStartup + 10f;
            while (showing.Status == UniTaskStatus.Pending && Time.realtimeSinceStartup < deadline) {
                yield return null;
            }

            Assert.AreNotEqual(UniTaskStatus.Pending, showing.Status, "ShowById never completed");
            report(showing.GetAwaiter().GetResult());
        }

        /// <summary>DIAL_Z15's 25808 and 25857, rebuilt: three conditionals that end, one default.</summary>
        private static Dialog Router() {
            var router = new DialogEntry {
                Offset = RouterOffset,
                Id = RouterId,
                Actions = new List<DialogActionBase>(),
                Branches = new List<DialogBranchBase> {
                    new ConditionalBranch {
                        Condition = new VarCondition { Var = 7, Min = 1, Max = 4 }, TargetOffset = 0,
                    },
                    new ConditionalBranch {
                        Condition = new VarCondition { Var = 7, Min = 6, Max = 9 }, TargetOffset = 0,
                    },
                    new ConditionalBranch {
                        Condition = new FlagCondition { Flag = GateFlag, Set = false }, TargetOffset = 0,
                    },
                    new DefaultBranch { TargetOffset = LeafOffset },
                },
            };
            var leaf = new DialogEntry {
                Offset = LeafOffset,
                Actions = new List<DialogActionBase> { new SetReturnValueAction { Value = -4 } },
                Branches = new List<DialogBranchBase>(),
            };

            var dialog = new Dialog("dial_z15");
            dialog.Entries.Add(router);
            dialog.Entries.Add(leaf);
            // Same stamping DdxExtractor does, so the walker's key index resolves the default.
            foreach (DialogEntry e in dialog.Entries) {
                e.Key = ContentKey.ForBase("ddx:dial_z15", e.Offset);
                foreach (DialogBranchBase b in e.Branches) {
                    b.TargetKey = b.TargetOffset is int off && off != 0
                        ? ContentKey.ForBase("ddx:dial_z15", off)
                        : null;
                }
            }
            return dialog;
        }

        /// <summary>The inn's 16928 -> 17299 shape: two SkipWait lines, the first defaulting to the second.</summary>
        private static Dialog SkipWaitChain() {
            var first = new DialogEntry {
                Offset = 100, Id = RouterId, Text = "The room was cramped.",
                Flags = DialogEntryFlags.SkipWait,
                Actions = new List<DialogActionBase>(),
                Branches = new List<DialogBranchBase> { new DefaultBranch { TargetOffset = 200 } },
            };
            var second = new DialogEntry {
                Offset = 200, Text = "In moments, they were all fast asleep...",
                Flags = DialogEntryFlags.SkipWait,
                Actions = new List<DialogActionBase>(),
                Branches = new List<DialogBranchBase>(),
            };
            var dialog = new Dialog("dial_z13");
            dialog.Entries.Add(first);
            dialog.Entries.Add(second);
            foreach (DialogEntry e in dialog.Entries) {
                e.Key = ContentKey.ForBase("ddx:dial_z13", e.Offset);
                foreach (DialogBranchBase b in e.Branches) {
                    b.TargetKey = b.TargetOffset is int off && off != 0 ? ContentKey.ForBase("ddx:dial_z13", off) : null;
                }
            }
            return dialog;
        }

        /// <summary>
        /// A line whose continuation is a text-less OFFSET router reaches what the router chooses.
        /// </summary>
        /// <remarks>
        /// The Garrison's arrival narration (DIAL_Z19 2002) defaults to 3689, the router that asks
        /// "do they carry the ruby?", whose satisfied arm is Captain Belford offering to take it.
        /// Continuing a line followed only id-addressed targets, so the chain stopped on the router:
        /// it draws nothing, and NextLine refuses to continue from a text-less entry, which ended
        /// the conversation on the narration and left the quest unhandable. TASK-559.
        /// </remarks>
        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator AContinuationWalksThroughATextLessOffsetRouter() {
            var session = new GameSession();
            session.SetGlobalFlag(GateFlag, true);

            int answered = 0;
            yield return Show(session, a => answered = a, RouterContinuationChain());

            Assert.AreEqual(OfferAnswer, answered,
                "the continuation walked the router to the arm its condition chose");
        }

        /// <summary>
        /// The control: the same chain with the gate CLEAR takes the router's default arm.
        /// </summary>
        /// <remarks>
        /// Without it a walk that ignored the condition and always took the default would pass the
        /// test above for the wrong reason — the router is only walked correctly if BOTH arms land.
        /// </remarks>
        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator TheRoutersDefaultArmIsTakenWhenItsConditionFails() {
            var session = new GameSession();
            session.SetGlobalFlag(GateFlag, false);

            int answered = 0;
            yield return Show(session, a => answered = a, RouterContinuationChain());

            Assert.AreEqual(BusyAnswer, answered, "the gate is clear, so the default arm answers");
        }

        /// <summary>The Garrison's 2002 -> 3689 -> 3718 shape: a line, a text-less router, two arms.</summary>
        private static Dialog RouterContinuationChain() {
            var line = Entry(100, "The Garrison was impressive.",
                new DefaultBranch { TargetOffset = 200 });
            line.Id = RouterId;
            var router = new DialogEntry {
                Offset = 200,
                Actions = new List<DialogActionBase>(),
                Branches = new List<DialogBranchBase> {
                    new ConditionalBranch {
                        Condition = new FlagCondition { Flag = GateFlag, Set = true },
                        TargetOffset = 300,
                    },
                    new DefaultBranch { TargetOffset = 400 },
                },
            };
            DialogEntry offer = Entry(300, "Are you returning Makala's ruby?");
            offer.Actions.Add(new SetReturnValueAction { Value = OfferAnswer });
            DialogEntry busy = Entry(400, "The captain was busy.");
            busy.Actions.Add(new SetReturnValueAction { Value = BusyAnswer });

            var dialog = new Dialog("dial_z19");
            dialog.Entries.Add(line);
            dialog.Entries.Add(router);
            dialog.Entries.Add(offer);
            dialog.Entries.Add(busy);
            Stamp(dialog, "ddx:dial_z19");
            return dialog;
        }

        /// <summary>A SkipWait line, so the chain runs without waiting on a dismiss click.</summary>
        private static DialogEntry Entry(int offset, string text, params DialogBranchBase[] branches) =>
            new DialogEntry {
                Offset = offset,
                Text = text,
                Flags = DialogEntryFlags.SkipWait,
                Actions = new List<DialogActionBase>(),
                Branches = new List<DialogBranchBase>(branches),
            };

        /// <summary>The same key stamping DdxExtractor does, so the walker's index resolves targets.</summary>
        private static void Stamp(Dialog dialog, string file) {
            foreach (DialogEntry e in dialog.Entries) {
                e.Key = ContentKey.ForBase(file, e.Offset);
                foreach (DialogBranchBase b in e.Branches) {
                    b.TargetKey = b.TargetOffset is int off && off != 0
                        ? ContentKey.ForBase(file, off)
                        : null;
                }
            }
        }

        private sealed class OneDialogLoader : IDialogResourceLoader {
            private readonly Dialog _dialog;

            public OneDialogLoader(Dialog dialog) {
                _dialog = dialog;
            }

            public UniTask<Dialog> LoadDialogAsync(int dialogId) => UniTask.FromResult(_dialog);

            public UniTask<TeleportDestinationSet> GetTeleportDestinationsAsync() =>
                UniTask.FromResult(new TeleportDestinationSet("TELEPORT.DAT"));
        }
    }
}
