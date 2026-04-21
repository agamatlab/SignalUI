using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using singalUI.ViewModels;
using System;

namespace singalUI.Views
{
    public partial class CollapsiblePanelView : UserControl
    {
        public static readonly StyledProperty<string> TitleProperty =
            AvaloniaProperty.Register<CollapsiblePanelView, string>(nameof(Title), "");

        public static readonly StyledProperty<string> IconPathProperty =
            AvaloniaProperty.Register<CollapsiblePanelView, string>(nameof(IconPath), "");

        public static readonly StyledProperty<bool> IsExpandedProperty =
            AvaloniaProperty.Register<CollapsiblePanelView, bool>(nameof(IsExpanded), true);

        public new static readonly StyledProperty<object?> ContentProperty =
            AvaloniaProperty.Register<CollapsiblePanelView, object?>(nameof(Content), null);

        public string Title
        {
            get => GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public string IconPath
        {
            get => GetValue(IconPathProperty);
            set => SetValue(IconPathProperty, value);
        }

        public bool IsExpanded
        {
            get => GetValue(IsExpandedProperty);
            set => SetValue(IsExpandedProperty, value);
        }

        public new object? Content
        {
            get => GetValue(ContentProperty);
            set => SetValue(ContentProperty, value);
        }

        public CollapsiblePanelView()
        {
            InitializeComponent();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            // Sync from DataContext properties when using the old pattern
            if (change.Property.Name == nameof(DataContext))
            {
                if (DataContext is CollapsiblePanelViewModel vm)
                {
                    // Only set if the direct properties haven't been explicitly set
                    if (string.IsNullOrEmpty(Title) || Title == "")
                        Title = vm.Title;
                    if (string.IsNullOrEmpty(IconPath) || IconPath == "")
                        IconPath = vm.IconPath;
                    if (Content == null)
                        Content = vm.Content;
                    // Don't override IsExpanded as it may be set in XAML
                }
            }
        }

        private void OnToggleClick(object? sender, RoutedEventArgs e)
        {
            IsExpanded = !IsExpanded;
        }
    }
}
