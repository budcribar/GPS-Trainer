using System.Text.Json;

namespace GpsTrainer.Web.Services;

/// <summary>Where a floating editor dialog sits, in viewport pixels.</summary>
public sealed class DialogSpot
{
    public double Left { get; set; }
    public double Top { get; set; }
}

/// <summary>Measured dialog and viewport, from the browser.</summary>
public sealed class DialogSize
{
    public double Width { get; set; }
    public double Height { get; set; }
    public double ViewportWidth { get; set; }
    public double ViewportHeight { get; set; }
    public List<DialogRect> Blocked { get; set; } = [];
}

/// <summary>A control the help dialog must not cover.</summary>
public sealed class DialogRect
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

/// <summary>
/// Keeps the course and help dialogs on screen. Help opens at the 2013 spot
/// (left 800, top 545), beside the map. A saved spot that would sit off-screen
/// or on Zoom To, Undo, or the profile is replaced with that spot. A drag is clamped.
/// </summary>
public static class EditorDialogLayout
{
    public const double Margin = 8;
    public const double HelpDefaultLeft = 800;
    public const double HelpDefaultTop = 545;
    public const string CourseKey = "gpstrainer.courseDialog";
    public const string HelpKey = "gpstrainer.helpDialog";

    public static DialogSpot Default(bool course, double viewportWidth, double viewportHeight, double dialogWidth, double dialogHeight)
    {
        if (course)
            return new DialogSpot { Left = viewportWidth - dialogWidth - Margin, Top = Margin };
        return new DialogSpot { Left = HelpDefaultLeft, Top = HelpDefaultTop };
    }

    public static bool IsOffScreen(DialogSpot spot, double viewportWidth, double viewportHeight, double dialogWidth, double dialogHeight)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0)
            return true;
        if (spot.Left < 0 || spot.Top < 0)
            return true;
        if (spot.Left >= viewportWidth)
            return true;
        var fitsWidth = dialogWidth + Margin * 2 <= viewportWidth;
        if (fitsWidth && spot.Left + dialogWidth > viewportWidth)
            return true;
        var fitsHeight = dialogHeight + Margin * 2 <= viewportHeight;
        if (fitsHeight && spot.Top + dialogHeight > viewportHeight)
            return true;
        return false;
    }

    public static DialogSpot Clamp(DialogSpot spot, double viewportWidth, double viewportHeight, double dialogWidth, double dialogHeight)
    {
        double left;
        double top;
        if (dialogWidth + Margin * 2 > viewportWidth)
            left = Margin;
        else
            left = Math.Min(Math.Max(spot.Left, Margin), viewportWidth - dialogWidth - Margin);

        if (dialogHeight + Margin * 2 > viewportHeight)
            top = Margin;
        else
            top = Math.Min(Math.Max(spot.Top, Margin), viewportHeight - dialogHeight - Margin);

        return new DialogSpot { Left = left, Top = top };
    }

    public static bool Hits(DialogSpot spot, double width, double height, IReadOnlyList<DialogRect>? blocked)
    {
        if (blocked is null || width <= 0 || height <= 0)
            return false;
        foreach (var box in blocked)
        {
            if (box.Width <= 0 || box.Height <= 0)
                continue;
            var clear = spot.Left + width <= box.Left
                || box.Left + box.Width <= spot.Left
                || spot.Top + height <= box.Top
                || box.Top + box.Height <= spot.Top;
            if (!clear)
                return true;
        }
        return false;
    }

    public static DialogSpot Place(
        DialogSpot? saved,
        bool course,
        double viewportWidth,
        double viewportHeight,
        double dialogWidth,
        double dialogHeight,
        IReadOnlyList<DialogRect>? blocked = null)
    {
        var fallback = Clamp(Default(course, viewportWidth, viewportHeight, dialogWidth, dialogHeight), viewportWidth, viewportHeight, dialogWidth, dialogHeight);
        if (!course)
            fallback = ClearOf(fallback, dialogWidth, dialogHeight, viewportWidth, viewportHeight, blocked);

        if (saved is not null
            && !IsOffScreen(saved, viewportWidth, viewportHeight, dialogWidth, dialogHeight)
            && (course || !Hits(saved, dialogWidth, dialogHeight, blocked)))
        {
            var kept = Clamp(saved, viewportWidth, viewportHeight, dialogWidth, dialogHeight);
            if (course || !Hits(kept, dialogWidth, dialogHeight, blocked))
                return kept;
        }
        return fallback;
    }

    private static DialogSpot ClearOf(DialogSpot preferred, double width, double height, double viewportWidth, double viewportHeight, IReadOnlyList<DialogRect>? blocked)
    {
        var clamped = Clamp(preferred, viewportWidth, viewportHeight, width, height);
        if (!Hits(clamped, width, height, blocked))
            return clamped;

        DialogSpot? best = null;
        var bestDistance = double.MaxValue;
        void Consider(double left, double top)
        {
            var spot = Clamp(new DialogSpot { Left = left, Top = top }, viewportWidth, viewportHeight, width, height);
            if (Hits(spot, width, height, blocked))
                return;
            var distance = Math.Abs(spot.Left - HelpDefaultLeft) + Math.Abs(spot.Top - HelpDefaultTop);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = spot;
            }
        }

        if (blocked is not null)
        {
            foreach (var box in blocked)
            {
                Consider(preferred.Left, box.Top - height - Margin);
                Consider(preferred.Left, box.Top + box.Height + Margin);
                Consider(box.Left + box.Width + Margin, preferred.Top);
                Consider(box.Left - width - Margin, preferred.Top);
            }
        }
        for (var top = Margin; top < viewportHeight; top += 48)
        {
            for (var left = Margin; left < viewportWidth; left += 48)
                Consider(left, top);
        }
        return best ?? clamped;
    }

    public static string Serialize(double left, double top) =>
        JsonSerializer.Serialize(new DialogSpot { Left = left, Top = top }, SessionSnapshot.Json);

    public static bool TryParse(string? json, out DialogSpot spot)
    {
        spot = new DialogSpot();
        if (string.IsNullOrWhiteSpace(json))
            return false;
        try
        {
            var parsed = JsonSerializer.Deserialize<DialogSpot>(json, SessionSnapshot.Json);
            if (parsed is null)
                return false;
            spot = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
