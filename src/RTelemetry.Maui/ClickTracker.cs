using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls;
using RTelemetry.Client;
using RTelemetry.Contracts;

namespace RTelemetry.Maui;

/// <summary>
/// Автоматически пишет <c>ui.click</c> на каждое нажатие <see cref="Button"/>, <see cref="ImageButton"/>
/// и <see cref="TapGestureRecognizer"/> во всём дереве приложения, включая страницы, добавленные позже.
/// </summary>
/// <remarks>
/// Свойства события: <c>element</c> (AutomationId → x:Name → тип), <c>element_type</c>, <c>page</c>,
/// <c>kind</c> (<c>button</c>/<c>tap</c>), для тапа — <c>x</c>/<c>y</c> в долях страницы.
/// Текст элементов никогда не пишется: на кнопках выбора он совпадает с текстом истории.
/// Свои контролы (например, на GraphicsView) сообщают о нажатии через <see cref="TrackClick"/>.
/// </remarks>
public sealed class ClickTracker
{
    private static readonly object Marker = new();

    private readonly ITelemetryClient _client;
    private readonly ConditionalWeakTable<Element, object> _hooked = new();

    public ClickTracker(ITelemetryClient client)
    {
        _client = client;
    }

    /// <summary>Подписывается на всё дерево приложения. Вызывать один раз, например в конструкторе <c>App</c>.</summary>
    public void Attach(Application application)
    {
        application.DescendantAdded += OnDescendantAdded;

        foreach (var window in application.Windows)
        {
            HookTree(window);
        }
    }

    public void Detach(Application application) => application.DescendantAdded -= OnDescendantAdded;

    /// <summary>Ручной клик для контролов, которые трекер не видит сам.</summary>
    public void TrackClick(Element element, string kind = "custom", IReadOnlyDictionary<string, object?>? extra = null)
    {
        var props = new Dictionary<string, object?>
        {
            ["element"] = Describe(element),
            ["element_type"] = element.GetType().Name,
            ["page"] = FindPage(element)?.GetType().Name,
            ["kind"] = kind,
        };

        if (extra is not null)
        {
            foreach (var (key, value) in extra)
            {
                props[key] = value;
            }
        }

        _client.Track(TelemetryEventNames.UiClick, props);
    }

    private void OnDescendantAdded(object? sender, ElementEventArgs e) => HookTree(e.Element);

    private void HookTree(Element root)
    {
        Hook(root);
        if (root is IVisualTreeElement visual)
        {
            foreach (var child in visual.GetVisualTreeDescendants())
            {
                if (child is Element element)
                {
                    Hook(element);
                }
            }
        }
    }

    private void Hook(Element element)
    {
        if (_hooked.TryGetValue(element, out _))
        {
            return;
        }

        _hooked.Add(element, Marker);

        switch (element)
        {
            case Button button:
                button.Clicked += OnButtonClicked;
                break;
            case ImageButton imageButton:
                imageButton.Clicked += OnButtonClicked;
                break;
        }

        if (element is View view)
        {
            foreach (var tap in view.GestureRecognizers.OfType<TapGestureRecognizer>())
            {
                tap.Tapped += (_, args) => OnTapped(view, args);
            }
        }
    }

    private void OnButtonClicked(object? sender, EventArgs e)
    {
        if (sender is Element element)
        {
            TrackClick(element, "button");
        }
    }

    private void OnTapped(View view, TappedEventArgs args)
    {
        var page = FindPage(view);
        Dictionary<string, object?>? extra = null;

        if (page is not null && page.Width > 0 && page.Height > 0 && args.GetPosition(page) is { } point)
        {
            extra = new Dictionary<string, object?>
            {
                ["x"] = Math.Round(point.X / page.Width, 4),
                ["y"] = Math.Round(point.Y / page.Height, 4),
            };
        }

        TrackClick(view, "tap", extra);
    }

    private static string Describe(Element element)
    {
        if (!string.IsNullOrEmpty(element.AutomationId)) return element.AutomationId;
        if (!string.IsNullOrEmpty(element.StyleId)) return element.StyleId;
        return element.GetType().Name;
    }

    private static Page? FindPage(Element? element)
    {
        while (element is not null and not Page)
        {
            element = element.Parent;
        }

        return element as Page;
    }
}
