namespace BakAgain.Tests.Editor.World {
    using BakAgain.Graphics;
    using BakAgain.ResourceManagement;
    using BakAgain.Tests.TestSupport;
    using BakAgain.World;
    using GameData.Resources.Config;
    using GameData.Resources.Dialog;
    using GameData.Resources.Layout;
    using GameData.Resources.Menu;
    using NUnit.Framework;
    using ResourceExtraction;
    using System.Linq;

    /// <summary>
    /// TASK-61: the world-viewport rect <c>(65, 66, 1470, 606)</c> canonical exists in THREE
    /// independent places that all trace back to the same VGA source <c>(13, 11, 294, 101)</c>,
    /// but were never actually fenced against each other:
    ///
    /// <list type="number">
    ///   <item><see cref="WorldViewport.CanonicalRect"/> — the 3D render window. No longer a
    ///     typed-in constant: it is read from <b>START.DAT</b>, whose four int16
    ///     <c>LoadSTART.DAT</c> (@0x41620) puts into the descriptor <c>setupRenderView</c>
    ///     (@0x23d18) clips to. That makes this copy's source the real one, and the fence below
    ///     checks the parse rather than a literal.</item>
    ///   <item><see cref="DialogStyleTable"/> row 2's <see cref="DialogStyle.DefaultArea"/> — the
    ///     default dialog panel box (<c>dialogTypeData</c> @ 0x3a831 in KRONDOR.EXE).</item>
    ///   <item><c>REQ_MAIN.DAT</c>'s <c>ClickArea</c> menu entry with <c>ActionId</c> 192 — the
    ///     world-viewport hit region, loaded here through the real archive resource path (the same
    ///     <c>IResourceProvider.GetResource&lt;UserInterface&gt;</c> call <c>BakResourceProvider</c>
    ///     makes in production), not by reading <c>generated/REQ/REQ_MAIN.json</c> off disk.</item>
    /// </list>
    ///
    /// <para><b>This is deliberately three tests of one fact, not a shared constant.</b> The
    /// viewport and the default dialog box have two different meanings that merely coincide today
    /// — the original authored them separately too (REQ_MAIN.DAT's hit-region bytes vs the EXE's
    /// dialogTypeData table bytes are two unrelated locations in KRONDOR.EXE/KRONDOR.001). Do
    /// **not** "simplify" this by making one of the three read from another, or by introducing a
    /// shared <c>readonly</c> rect all three consumers reference: a mod that moves the world
    /// viewport must not silently move every default dialog box along with it, and vice versa.
    /// The correct response to any assertion below going red is to open the other two copies and
    /// update them BY HAND to match, then re-run this fixture — not to collapse the three sources
    /// into one.</para>
    /// </summary>
    public class ViewportRectFencedAcrossThreeCopiesTests {
        // Read this before "fixing" a failure here. Deliberately duplicated into every failure
        // message (not factored down to a single call site elsewhere) so it survives being read
        // in a CI log with no access to this file.
        private const string WhyTheseAreSeparateCopies =
            "This is NOT a shared constant that drifted -- WorldViewport.CanonicalRect, " +
            "DialogStyleTable row 2's DefaultArea, and REQ_MAIN's ActionId-192 ClickArea are " +
            "THREE INDEPENDENTLY-AUTHORED copies of the same real-world rect (VGA (13,11,294,101); " +
            "REQ_MAIN.DAT hit-region bytes vs KRONDOR.EXE dialogTypeData @0x3a831 are two unrelated " +
            "locations in the original binary/archive) that happen to coincide today because the " +
            "world viewport and the default dialog panel occupy the same screen region by design, " +
            "not because they are the same value. If this assertion just went red, exactly ONE of " +
            "the three copies was edited alone (WorldViewport.cs / DialogStyleTable.cs row 2 / " +
            "REQ_MAIN.DAT). The fix is to open the OTHER TWO and update them BY HAND to match -- " +
            "do NOT collapse the three into a single shared rect/constant. A mod that moves the " +
            "world viewport must not silently move every default dialog box, and a mod that " +
            "resizes the default dialog box must not silently move the 3D viewport -- collapsing " +
            "them would make both true.";

        [Test]
        [RequiresShippedGameData]
        public void CanonicalRect_MatchesDialogStyleTableRow2DefaultArea_ComponentForComponent() {
            Area viewport = new WorldViewport().CanonicalRect;
            DialogStyle row2 = DialogStyleTable.CreateShipped().Get(2);
            LayoutHint area = row2.DefaultArea;

            // "Component-for-component, units included": a LayoutLength is a (Value, Unit) pair,
            // so comparing only Value would pass even if row 2 were retyped from Px to Percent
            // (which would silently break for any Frame other than exactly 1600x1200 wide/tall).
            Assert.AreEqual(LayoutLengthUnit.Px, area.Left.Unit,
                "DialogStyleTable row 2 DefaultArea.Left must stay Px, not Percent. " + WhyTheseAreSeparateCopies);
            Assert.AreEqual(LayoutLengthUnit.Px, area.Top.Unit,
                "DialogStyleTable row 2 DefaultArea.Top must stay Px, not Percent. " + WhyTheseAreSeparateCopies);
            Assert.AreEqual(LayoutLengthUnit.Px, area.Width.Unit,
                "DialogStyleTable row 2 DefaultArea.Width must stay Px, not Percent. " + WhyTheseAreSeparateCopies);
            Assert.AreEqual(LayoutLengthUnit.Px, area.Height.Unit,
                "DialogStyleTable row 2 DefaultArea.Height must stay Px, not Percent. " + WhyTheseAreSeparateCopies);

            Assert.AreEqual(viewport.X, area.Left.Value,
                "WorldViewport.CanonicalRect.X vs DialogStyleTable row 2 DefaultArea.Left. " + WhyTheseAreSeparateCopies);
            Assert.AreEqual(viewport.Y, area.Top.Value,
                "WorldViewport.CanonicalRect.Y vs DialogStyleTable row 2 DefaultArea.Top. " + WhyTheseAreSeparateCopies);
            Assert.AreEqual(viewport.Width, area.Width.Value,
                "WorldViewport.CanonicalRect.Width vs DialogStyleTable row 2 DefaultArea.Width. " + WhyTheseAreSeparateCopies);
            Assert.AreEqual(viewport.Height, area.Height.Value,
                "WorldViewport.CanonicalRect.Height vs DialogStyleTable row 2 DefaultArea.Height. " + WhyTheseAreSeparateCopies);
        }

        [Test]
        public void CanonicalRect_MatchesReqMainActionId192ClickArea_LoadedThroughTheRealResourcePath() {
            // Needs the shipped KRONDOR.001 archive; ignores (not fails) on a bare checkout,
            // matching every other test that goes through BakResourceLocator/GeneralResourceProvider.
            ShippedGameData.RequireOrIgnore();

            Area viewport = new WorldViewport().CanonicalRect;

            // The real resource path: the same IResourceProvider.GetResource<T> call
            // BakResourceProvider/BakResourceLocator make in production -- reads REQ_MAIN.DAT out
            // of KRONDOR.001 via UserInterfaceExtractor + CanonicalSpace.Apply, NOT a read of the
            // already-generated generated/REQ/REQ_MAIN.json off disk.
            IResourceProvider provider = ResourceProviderFactory.CreateResourceProvider(BakResourceSettings.GamePath);
            UserInterface reqMain = provider.GetResource<UserInterface>("REQ_MAIN.DAT");

            UiElement clickArea = reqMain.MenuEntries.Single(entry => entry.ActionId == 192);

            Assert.AreEqual(viewport.X, clickArea.XPosition,
                "WorldViewport.CanonicalRect.X vs REQ_MAIN ActionId-192 ClickArea.XPosition. " + WhyTheseAreSeparateCopies);
            Assert.AreEqual(viewport.Y, clickArea.YPosition,
                "WorldViewport.CanonicalRect.Y vs REQ_MAIN ActionId-192 ClickArea.YPosition. " + WhyTheseAreSeparateCopies);
            Assert.AreEqual(viewport.Width, clickArea.Width,
                "WorldViewport.CanonicalRect.Width vs REQ_MAIN ActionId-192 ClickArea.Width. " + WhyTheseAreSeparateCopies);
            Assert.AreEqual(viewport.Height, clickArea.Height,
                "WorldViewport.CanonicalRect.Height vs REQ_MAIN ActionId-192 ClickArea.Height. " + WhyTheseAreSeparateCopies);
        }

        [Test]
        public void CanonicalRect_IsTheRectStartDatActuallyCarries_NotATypedInLiteral() {
            ShippedGameData.RequireOrIgnore();

            Area viewport = new WorldViewport().CanonicalRect;

            // Deliberately re-read here rather than trusting the same call inside WorldViewport:
            // this asserts the PARSE, so a field-order slip in StartDataExtractor (the file has no
            // header or terminator, so order is all there is) moves the viewport and is caught,
            // instead of both sides moving together and agreeing on a wrong rect.
            IResourceProvider provider =
                ResourceProviderFactory.CreateResourceProvider(BakResourceSettings.GamePath);
            StartData start = provider.GetResource<StartData>("START.DAT");

            Assert.AreEqual(start.ViewportX, viewport.X,
                "WorldViewport.CanonicalRect.X vs START.DAT. " + WhyTheseAreSeparateCopies);
            Assert.AreEqual(start.ViewportY, viewport.Y,
                "WorldViewport.CanonicalRect.Y vs START.DAT. " + WhyTheseAreSeparateCopies);
            Assert.AreEqual(start.ViewportWidth, viewport.Width,
                "WorldViewport.CanonicalRect.Width vs START.DAT. " + WhyTheseAreSeparateCopies);
            Assert.AreEqual(start.ViewportHeight, viewport.Height,
                "WorldViewport.CanonicalRect.Height vs START.DAT. " + WhyTheseAreSeparateCopies);

            // The values the constants held before this became data-driven. Pinned so the switch is
            // provably a no-op on what reaches the screen; if a future build's START.DAT differs,
            // this is the assertion that says so out loud rather than silently moving the view.
            Assert.AreEqual(65, viewport.X, "the shipped viewport moved. " + WhyTheseAreSeparateCopies);
            Assert.AreEqual(66, viewport.Y, "the shipped viewport moved. " + WhyTheseAreSeparateCopies);
            Assert.AreEqual(1470, viewport.Width, "the shipped viewport moved. " + WhyTheseAreSeparateCopies);
            Assert.AreEqual(606, viewport.Height, "the shipped viewport moved. " + WhyTheseAreSeparateCopies);
        }
    }
}
