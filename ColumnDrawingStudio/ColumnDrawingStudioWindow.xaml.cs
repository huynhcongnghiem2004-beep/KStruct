using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Autodesk.Revit.DB;

namespace ColumnRebarPro.V3
{
    // Separate UI entry point; delegates actual drawing production to the existing V3/V4 engine.
    internal partial class ColumnDrawingStudioWindow : Window
    {
        private readonly Document _document;
        private readonly V3DeploymentControl _settings;
        internal DeploymentSettingsFile Settings { get; private set; }

        internal ColumnDrawingStudioWindow(Document document)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            InitializeComponent();
            _settings = new V3DeploymentControl();
            _settings.Initialize(document);
            _settings.PrepareForExistingRebar();
            SettingsHost.Content = _settings;

            _settings.AddHandler(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent,
                new SelectionChangedEventHandler((sender, args) => RefreshSummary()), true);
            _settings.AddHandler(System.Windows.Controls.Primitives.ToggleButton.CheckedEvent,
                new RoutedEventHandler((sender, args) => RefreshSummary()), true);
            _settings.AddHandler(System.Windows.Controls.Primitives.ToggleButton.UncheckedEvent,
                new RoutedEventHandler((sender, args) => RefreshSummary()), true);
            _settings.AddHandler(TextBox.TextChangedEvent,
                new TextChangedEventHandler((sender, args) => RefreshSummary()), true);
            Loaded += (sender, args) => RefreshSummary();
        }

        private void RefreshSummary()
        {
            if (_settings == null) return;
            LongSummary.Text = "Dim: " + _settings.SelectedChoiceLabel("LongDimension") +
                "  ·  Đai: " + _settings.SelectedChoiceLabel("LongStirrupTag") +
                "  ·  Hiện tag: " + (_settings.IsOptionChecked("LongShowTag") ? "Có" : "Không");
            CrossSummary.Text = "Dim: " + _settings.SelectedChoiceLabel("CrossDimension") +
                "  ·  Đai: " + _settings.SelectedChoiceLabel("CrossStirrupTag") +
                "  ·  Hiện tag: " + (_settings.IsOptionChecked("CrossShowTag") ? "Có" : "Không");
            var snapshot = _settings.Capture();
            Func<string, string> entry = name => snapshot.Entries.FirstOrDefault(x => x.Name == name)?.Value ?? "";
            SheetSummary.Text = "Sheet: " + entry("SheetNumber") + "  ·  " + entry("SheetName") +
                "  |  MCD 1:" + entry("LongScale") + " · MCN 1:" + entry("CrossScale");
        }

        private void Run_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Settings = _settings.CaptureValidated();
                // Existing deployment settings persistence is intentionally shared with V3/V4.
                V3DeploymentControl.PersistSettings(_document, Settings);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                StatusText.Text = ex.Message;
                MessageBox.Show(this, ex.Message, "KStruct - Column Drawing Studio",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}