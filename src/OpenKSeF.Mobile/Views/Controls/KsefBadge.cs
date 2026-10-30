using Microsoft.Maui.Controls.Shapes;
using OpenKSeF.Mobile.ViewModels;

namespace OpenKSeF.Mobile.Views.Controls;

/// <summary>Pill badge for a KSeF invoice status or a KSeF environment (design-system §4); set one of Status or Environment.</summary>
public class KsefBadge : Border
{
    public static readonly BindableProperty StatusProperty =
        BindableProperty.Create(nameof(Status), typeof(string), typeof(KsefBadge), propertyChanged: (b, _, _) => ((KsefBadge)b).Refresh());

    public static readonly BindableProperty EnvironmentProperty =
        BindableProperty.Create(nameof(Environment), typeof(string), typeof(KsefBadge), propertyChanged: (b, _, _) => ((KsefBadge)b).Refresh());

    public static readonly BindableProperty ShortProperty =
        BindableProperty.Create(nameof(Short), typeof(bool), typeof(KsefBadge), false, propertyChanged: (b, _, _) => ((KsefBadge)b).Refresh());

    private readonly Label _label = new() { FontSize = 12, FontAttributes = FontAttributes.Bold, VerticalTextAlignment = TextAlignment.Center };

    public KsefBadge()
    {
        StrokeThickness = 0;
        StrokeShape = new RoundRectangle { CornerRadius = 12 };
        Padding = new Thickness(10, 3);
        HorizontalOptions = LayoutOptions.Start;
        VerticalOptions = LayoutOptions.Center;
        Content = _label;
        Refresh();
    }

    public string? Status
    {
        get => (string?)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public string? Environment
    {
        get => (string?)GetValue(EnvironmentProperty);
        set => SetValue(EnvironmentProperty, value);
    }

    public bool Short
    {
        get => (bool)GetValue(ShortProperty);
        set => SetValue(ShortProperty, value);
    }

    private void Refresh()
    {
        string text, tone;
        if (Environment is not null)
            (text, tone) = (IssuingFormat.EnvironmentLabel(Environment), IssuingFormat.EnvironmentTone(Environment));
        else
            (text, tone) = (StatusIcon(Status) + IssuingFormat.StatusLabel(Status, Short), IssuingFormat.StatusTone(Status));

        _label.Text = text;
        IsVisible = text.Length > 0;
        var (fg, bg) = tone == "Production" ? ("UiOnError", "UiError") : ($"Ui{tone}", $"Ui{tone}Bg");
        _label.SetAppThemeColor(Label.TextColorProperty, Color(fg), Color(fg + "Dark"));
        this.SetAppThemeColor(BackgroundColorProperty, Color(bg), Color(bg + "Dark"));
    }

    private static string StatusIcon(string? status) => status switch
    {
        "Draft" => "✎ ",
        "Sending" or "Approved" => "⟳ ",
        "Accepted" => "✓ ",
        "Rejected" => "✕ ",
        _ => string.Empty
    };

    private static Color Color(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color c ? c : Colors.Gray;
}
