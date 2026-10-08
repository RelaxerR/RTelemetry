namespace RTelemetry.Input;

public enum InputElementKind
{
    Other, Button, ImageButton, CheckBox, Switch, RadioButton, Slider, Stepper,
    Picker, DatePicker, TimePicker, Entry, Editor, CollectionView, ListView
}

/// <summary>Framework-independent interaction taxonomy. Availability is resolved by hit testing.</summary>
public static class InteractionRules
{
    public static bool IsInteractive(InputElementKind kind, bool hasTapGesture = false,
        bool isMarked = false, bool selectionEnabled = false, bool itemHit = false) =>
        isMarked || hasTapGesture || kind switch
        {
            InputElementKind.Button or InputElementKind.ImageButton or InputElementKind.CheckBox or
            InputElementKind.Switch or InputElementKind.RadioButton or InputElementKind.Slider or
            InputElementKind.Stepper or InputElementKind.Picker or InputElementKind.DatePicker or
            InputElementKind.TimePicker or InputElementKind.Entry or InputElementKind.Editor => true,
            InputElementKind.CollectionView or InputElementKind.ListView => selectionEnabled && itemHit,
            _ => false,
        };
}
