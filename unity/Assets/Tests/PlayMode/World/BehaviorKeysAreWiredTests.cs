namespace BakAgain.Tests.PlayMode.World {
    using BakAgain.World.Interaction;
    using GameData.Resources.World;
    using NUnit.Framework;
    using ResourceExtraction;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;

    /// <summary>
    /// The profile table's behaviour keys and the handlers' own keys must agree.
    ///
    /// <para><b>A mismatch is SILENT.</b> <c>WorldInteractionController</c> dispatches by string:
    /// an entity whose behaviour no handler claims falls to a <c>Debug.Log</c> — "no handler
    /// registered" — and the click does nothing. So a typo between a row's key and a handler's
    /// <c>Behavior</c> property makes an object inert with no error, no warning and a green suite.
    /// Six keys exist today ("container", "door", "building", "traversal", "grave", "catapult",
    /// "rift"), three of them added in one day.</para>
    ///
    /// <para><b>The handler set is found by REFLECTION, not listed here.</b> Writing the list out
    /// would make this test agree with itself: a handler added to the production list and forgotten
    /// here would pass. Reflection over the assembly cannot be forgotten.</para>
    /// </summary>
    public class BehaviorKeysAreWiredTests {
        /// <summary>Every concrete handler's <c>Behavior</c>, built with null collaborators —
        /// the property is a constant, so nothing is dereferenced.</summary>
        private static HashSet<string> HandlerKeys() {
            var keys = new HashSet<string>();
            foreach (Type type in typeof(IWorldInteractionHandler).Assembly.GetTypes()) {
                if (type.IsAbstract || type.IsInterface
                    || !typeof(IWorldInteractionHandler).IsAssignableFrom(type)) {
                    continue;
                }
                ConstructorInfo ctor = type.GetConstructors()
                    .OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
                if (ctor == null) {
                    continue;
                }
                object[] args = ctor.GetParameters()
                    .Select(p => p.ParameterType.IsValueType
                        ? Activator.CreateInstance(p.ParameterType)
                        : null)
                    .ToArray();
                var handler = (IWorldInteractionHandler)ctor.Invoke(args);
                keys.Add(handler.Behavior);
            }
            return keys;
        }

        /// <summary>Every profiled entity type's key is claimed by a handler.</summary>
        [Test]
        public void EveryProfiledTypeHasAHandlerForItsKey() {
            HashSet<string> handlers = HandlerKeys();
            Assert.IsNotEmpty(handlers, "reflection found no handlers — the fixture is broken, "
                + "not the wiring");

            var unclaimed = new List<string>();
            foreach (WorldEntityType type in Enum.GetValues(typeof(WorldEntityType))) {
                if (InteractionProfileTable.TryGet(type, out string behavior, out _)
                    && !handlers.Contains(behavior)) {
                    unclaimed.Add($"{type} -> '{behavior}'");
                }
            }

            CollectionAssert.IsEmpty(unclaimed,
                "these types have a profile row whose behaviour key no handler claims, so clicking "
                + "one logs 'no handler registered' and does nothing: "
                + string.Join(", ", unclaimed));
        }

        /// <summary>And the other direction: no handler answers to a key nothing yields.</summary>
        /// <remarks>
        /// Catches the typo from the handler's side — a <c>Behavior</c> of "riftmachine" against a
        /// row that says "rift" fails here rather than shipping an object that cannot be clicked.
        /// </remarks>
        [Test]
        public void NoHandlerAnswersToAKeyTheTableNeverYields() {
            var yielded = new HashSet<string>();
            foreach (WorldEntityType type in Enum.GetValues(typeof(WorldEntityType))) {
                if (InteractionProfileTable.TryGet(type, out string behavior, out _)) {
                    yielded.Add(behavior);
                }
            }

            List<string> orphans = HandlerKeys().Where(k => !yielded.Contains(k)).ToList();

            CollectionAssert.IsEmpty(orphans,
                "these handlers answer to a behaviour key no entity type produces, so they can "
                + "never be reached: " + string.Join(", ", orphans));
        }
    }
}
