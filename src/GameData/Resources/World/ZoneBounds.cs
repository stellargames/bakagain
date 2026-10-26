namespace GameData.Resources.World;

/// <remarks><b>Deliberately unread</b>: these four words are the original's world-viewport rect (zone_subsystem_init), which the port's IWorldViewport hard-codes; reading them only matters to a modder changing the viewport (TASK-350).</remarks>
public class ZoneBounds : IResource {
    public ZoneBounds(string id) { Id = id; }
    public ushort XOffset { get; set; }
    public ushort YOffset { get; set; }
    public ushort Width { get; set; }
    public ushort Height { get; set; }
    public ResourceType Type => ResourceType.DAT;
    public string Id { get; }
}
