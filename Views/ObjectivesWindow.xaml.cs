using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BMS.Overlay.ViewModels;
using BMS.Shared.Models;

namespace BMS.Overlay.Views;

public partial class ObjectivesWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _isRobloxVisible = false;

    public ObjectivesWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;

        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.CurrentOrder)
                               or nameof(MainViewModel.CurrentObjectives))
            {
                Dispatcher.Invoke(() => { RebuildObjectives(); UpdateVisibility(); });
            }
        };
    }

    public void SetRobloxVisible(bool visible)
    {
        _isRobloxVisible = visible;
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        var hasObjectives = _viewModel.CurrentObjectives.Count > 0;
        Opacity = (_isRobloxVisible && hasObjectives) ? 1.0 : 0.0;
    }

    /// <summary>
    /// Position the window based on user preference (TopLeft or TopRight).
    /// Call this whenever position setting or overlay top changes.
    /// </summary>
    public void ApplyPosition(string position, double overlayTop, double overlayWidth)
    {
        var screen = SystemParameters.PrimaryScreenWidth;
        Width = overlayWidth;

        Top = Math.Round(SystemParameters.PrimaryScreenHeight * 0.05);
        Left = position == "TopRight"
            ? screen - Width
            : 0;
    }

    public void RebuildObjectives()
    {
        ObjectivesStack.Children.Clear();

        var objectives = _viewModel.CurrentObjectives;
        if (objectives.Count == 0)
        {
            UpdateVisibility();
            return;
        }

        foreach (var obj in objectives.OrderBy(o => o.Index))
            ObjectivesStack.Children.Add(BuildObjectiveRow(obj));

        // Adjust window height: header ~30 + per item ~26 + padding
        Height = Math.Min(30 + objectives.Count * 26 + 16, 400);
        UpdateVisibility();
    }

    private UIElement BuildObjectiveRow(MissionObjective objective)
    {
        var row = new DockPanel { Margin = new Thickness(2, 1, 2, 1) };

        var cb = new CheckBox
        {
            IsChecked = objective.IsChecked,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };

        var textBlock = new TextBlock
        {
            Text = $"{objective.Index + 1}. {objective.Text}",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };

        ApplyObjectiveStyle(textBlock, objective.IsChecked);

        cb.Checked += async (s, e) =>
        {
            ApplyObjectiveStyle(textBlock, true);
            await _viewModel.ToggleObjectiveAsync(objective.Id);
        };
        cb.Unchecked += async (s, e) =>
        {
            ApplyObjectiveStyle(textBlock, false);
            await _viewModel.ToggleObjectiveAsync(objective.Id);
        };

        row.Children.Add(cb);
        row.Children.Add(textBlock);
        return row;
    }

    private static void ApplyObjectiveStyle(TextBlock tb, bool isChecked)
    {
        if (isChecked)
        {
            tb.Foreground = new SolidColorBrush(Color.FromRgb(0x6A, 0x6A, 0x6A));
            tb.TextDecorations = TextDecorations.Strikethrough;
        }
        else
        {
            tb.Foreground = new SolidColorBrush(Colors.White);
            tb.TextDecorations = null;
        }
    }
}
