// Lets the Unity assembly declare C# 9 records (init accessors): netstandard2.1 lacks the marker type,
// the same shim GameData and ResourceExtraction carry. First user: the Enhanced combat HUD's models.
namespace System.Runtime.CompilerServices {
    internal static class IsExternalInit { }
}
