using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace singalUI.Views;

public partial class HelpBubble : UserControl
{
    public static readonly StyledProperty<Control?> PlacementTargetProperty =
        AvaloniaProperty.Register<HelpBubble, Control?>(nameof(PlacementTarget));

    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<HelpBubble, bool>(nameof(IsOpen));

    public static readonly StyledProperty<string> HelpTextProperty =
        AvaloniaProperty.Register<HelpBubble, string>(nameof(HelpText), string.Empty);

    public static readonly StyledProperty<PlacementMode> PlacementProperty =
        AvaloniaProperty.Register<HelpBubble, PlacementMode>(nameof(Placement), PlacementMode.Bottom);

    public static readonly StyledProperty<double> BubbleMaxWidthProperty =
        AvaloniaProperty.Register<HelpBubble, double>(nameof(BubbleMaxWidth), 420);

    public static readonly StyledProperty<double> VerticalOffsetProperty =
        AvaloniaProperty.Register<HelpBubble, double>(nameof(VerticalOffset));

    public static readonly StyledProperty<double> HorizontalOffsetProperty =
        AvaloniaProperty.Register<HelpBubble, double>(nameof(HorizontalOffset));

    public Control? PlacementTarget
    {
        get => GetValue(PlacementTargetProperty);
        set => SetValue(PlacementTargetProperty, value);
    }

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public string HelpText
    {
        get => GetValue(HelpTextProperty);
        set => SetValue(HelpTextProperty, value);
    }

    public PlacementMode Placement
    {
        get => GetValue(PlacementProperty);
        set => SetValue(PlacementProperty, value);
    }

    public double BubbleMaxWidth
    {
        get => GetValue(BubbleMaxWidthProperty);
        set => SetValue(BubbleMaxWidthProperty, value);
    }

    public double VerticalOffset
    {
        get => GetValue(VerticalOffsetProperty);
        set => SetValue(VerticalOffsetProperty, value);
    }

    public double HorizontalOffset
    {
        get => GetValue(HorizontalOffsetProperty);
        set => SetValue(HorizontalOffsetProperty, value);
    }

    private Popup? _popup;
    private TextBlock? _bubbleText;
    private Border? _bubbleBorder;

    public HelpBubble()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _popup = this.FindControl<Popup>("PART_Popup");
        _bubbleText = this.FindControl<TextBlock>("BubbleText");
        _bubbleBorder = this.FindControl<Border>("BubbleBorder");
        ApplyTextAndSize();
        SyncPopup();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsOpenProperty ||
            change.Property == PlacementTargetProperty ||
            change.Property == PlacementProperty ||
            change.Property == VerticalOffsetProperty ||
            change.Property == HorizontalOffsetProperty)
        {
            SyncPopup();
        }
        else if (change.Property == HelpTextProperty ||
                 change.Property == BubbleMaxWidthProperty)
        {
            ApplyTextAndSize();
        }
    }

    private void ApplyTextAndSize()
    {
        if (_bubbleText != null)
            _bubbleText.Text = HelpText ?? string.Empty;
        if (_bubbleBorder != null)
            _bubbleBorder.MaxWidth = BubbleMaxWidth;
    }

    private void SyncPopup()
    {
        if (_popup == null)
            return;
        _popup.IsOpen = IsOpen;
        _popup.PlacementTarget = PlacementTarget;
        _popup.Placement = Placement;
        _popup.VerticalOffset = VerticalOffset;
        _popup.HorizontalOffset = HorizontalOffset;
    }
}
