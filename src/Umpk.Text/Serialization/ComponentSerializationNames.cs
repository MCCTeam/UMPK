namespace Umpk.Text.Serialization;

internal static class ComponentSerializationNames
{
    internal static string ClickActionName(ClickEventAction action) => action switch
    {
        ClickEventAction.OpenUrl => "open_url",
        ClickEventAction.OpenFile => "open_file",
        ClickEventAction.RunCommand => "run_command",
        ClickEventAction.SuggestCommand => "suggest_command",
        ClickEventAction.ChangePage => "change_page",
        ClickEventAction.CopyToClipboard => "copy_to_clipboard",
        ClickEventAction.ShowDialog => "show_dialog",
        ClickEventAction.Custom => "custom",
        _ => throw new ComponentFormatException($"Unknown click action {action}"),
    };

    internal static string SourceKey(NbtDataSource source) => source switch
    {
        NbtDataSource.Block => "block",
        NbtDataSource.Entity => "entity",
        NbtDataSource.Storage => "storage",
        _ => throw new ComponentFormatException($"Unknown NBT data source {source}"),
    };
}
