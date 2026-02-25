using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using BMS.Overlay.ViewModels;

namespace BMS.Overlay.Views
{
    public partial class BmsTabView : Page
    {
        public BmsTabView()
        {
            InitializeComponent();
            DataContextChanged += BmsTabView_DataContextChanged;
        }

        private void BmsTabView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is MainViewModel oldVm)
                oldVm.PropertyChanged -= ViewModel_PropertyChanged;

            if (e.NewValue is MainViewModel newVm)
            {
                newVm.PropertyChanged += ViewModel_PropertyChanged;
                LoadContent(newVm.CurrentOrder?.Content);
            }
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentOrder))
            {
                var vm = DataContext as MainViewModel;
                Dispatcher.Invoke(() => LoadContent(vm?.CurrentOrder?.Content));
            }
        }

        private void LoadContent(string? xamlContent)
        {
            try
            {
                if (string.IsNullOrEmpty(xamlContent))
                {
                    OrderContent.Document = new FlowDocument(
                        new Paragraph(new Run("Select a faction from BMS Options to view orders"))
                        { Foreground = Brushes.Gray });
                    SetDocumentStyle(OrderContent.Document);
                    return;
                }

                // Try to parse as XAML FlowDocument content
                try
                {
                    var doc = new FlowDocument();
                    var range = new TextRange(doc.ContentStart, doc.ContentEnd);
                    using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xamlContent));
                    range.Load(stream, DataFormats.Xaml);
                    SetDocumentStyle(doc);
                    OrderContent.Document = doc;
                }
                catch
                {
                    // Fallback: treat as plain text
                    var doc = new FlowDocument(new Paragraph(new Run(xamlContent)));
                    SetDocumentStyle(doc);
                    OrderContent.Document = doc;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading content: {ex.Message}");
                var doc = new FlowDocument(new Paragraph(new Run(xamlContent ?? "Error loading content")));
                SetDocumentStyle(doc);
                OrderContent.Document = doc;
            }
        }

        private static void SetDocumentStyle(FlowDocument doc)
        {
            doc.Foreground = Brushes.White;
            doc.Background = Brushes.Transparent;
            doc.PagePadding = new Thickness(10);
            doc.FontSize = 12;
            doc.FontFamily = new FontFamily("Segoe UI");
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as MainViewModel;
            LoadContent(vm?.CurrentOrder?.Content);
        }
    }
}
