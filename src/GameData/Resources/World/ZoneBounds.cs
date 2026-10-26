namespace GameData.Resources.World;

/// <remarks><b>Deliberately unread</b>: these four words are the travel view's rect, read into
/// g_world_widget->viewport (ZONE.C:50-54), where the 3D view is cleared and set up and the party
/// arrow centred (WORLDHIT.C:89-97, 208). The port's IWorldViewport takes START.DAT's rect instead,
/// which the original reads into g_active_window, the combat arena's view (BOOT.C:77-81; its tile
/// projection and cursor pick, PROJECT.C:40-76). Both
/// ship (13,11,294,101), so nothing differs until a mod changes one of them (TASK-350).</remarks>
public class ZoneBounds : IResource {
    public ZoneBounds(string id) { Id = id; }
    public ushort XOffset { get; set; }
    public ushort YOffset { get; set; }
    public ushort Width { get; set; }
    public ushort Height { get; set; }
    public ResourceType Type => ResourceType.DAT;
    public string Id { get; }
}
