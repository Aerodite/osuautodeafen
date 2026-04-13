namespace osuautodeafen.Tooltips;

public abstract class Tooltips
{
    public enum TooltipState
    {
        Hidden,
        Showing,
        Hiding,
        Appearing
    }

    public enum TooltipType
    {
        None,
        Section,
        Deafen,
        Time,
        Information
    }
}