using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace osuautodeafen.cs.Tooltips;

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

    /// <summary>
    ///     Determines if pointer is over the element, and it is not occluded by another control
    /// </summary>
    /// <remarks>
    ///     It should be fairly obvious but this mainly pertains to settings panel and the straingraph because they can overlap
    ///     But this can be used by any tooltip anywhere thankfully :D
    ///     (as to why this isn't built into avalonia already i dont have a clue in hell)
    /// </remarks>
    public static bool IsPointerOverElement(Control element, Point pointerInWindow)
    {
        if (element.GetVisualRoot() is not Window window)
            return false;

        if (window.InputHitTest(pointerInWindow) is not Control hit)
            return false;

        Control? current = hit;
        while (current != null)
        {
            if (current == element)
                return true;
            current = current.Parent as Control;
        }

        return false;
    }

    /// <summary>
    ///     Gets the pointer position relative to the window instead of the control
    /// </summary>
    /// <param name="control"></param>
    /// <param name="e"></param>
    /// <returns></returns>
    public static Point GetWindowRelativePointer(Control control, PointerEventArgs e)
    {
        if (control.GetVisualRoot() is not Window window) return new Point();
        Point controlPoint = e.GetPosition(control);
        PixelPoint screenPoint = control.PointToScreen(controlPoint);
        return new Point(screenPoint.X - window.Position.X, screenPoint.Y - window.Position.Y);
    }
}