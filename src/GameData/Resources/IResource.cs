namespace GameData.Resources;

public interface IResource {
    /// <summary>
    /// No code reads this. It is kept because it is serialized next to <see cref="Id"/> and tells a
    /// person reading or authoring the JSON what kind of resource the file is (owner, 2026-11-05).
    /// </summary>
    ResourceType Type { get; }
    string Id { get; }
}