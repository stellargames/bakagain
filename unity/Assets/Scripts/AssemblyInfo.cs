using System.Runtime.CompilerServices;

// Exposes `internal` test seams (e.g. GameSession.SetZoneContainersForTest) to the Tests
// assembly (Assets/Tests/Tests.asmdef), which is a separate assembly from BakAgain and
// would otherwise have no visibility into internals.
[assembly: InternalsVisibleTo("Tests")]

// Exposes internals to the editor-only scripts under Assets/Editor. There is no Editor asmdef, so
// those compile into Unity's implicit Assembly-CSharp-Editor, which auto-references BakAgain but
// sees only its public surface.
[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]
