using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CopilotActivityViewer.Model;

namespace CopilotActivityViewer.ToolWindow
{
    public partial class ActivityToolWindowControl : UserControl, IDisposable
    {
        private bool initialized;
        private ActivityItem renderedItem;
        private bool renderPending;

        public ActivityToolWindowControl()
            : this(new ActivityViewModel(SessionCatalog.CreateDefault()))
        {
        }

        public ActivityToolWindowControl(ActivityViewModel viewModel)
        {
            ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            InitializeComponent();
            DataContext = viewModel;
            viewModel.ActivitiesAppended += OnActivitiesAppended;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            Loaded += OnLoaded;
            RenderDetails();
        }

        public ActivityViewModel ViewModel { get; }

        public void Dispose()
        {
            ViewModel.ActivitiesAppended -= OnActivitiesAppended;
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            Track(null);
            ViewModel.Dispose();
        }

        /// <summary>Rebuilds the formatted details of the selected activity immediately.</summary>
        public void RenderDetails()
        {
            renderPending = false;
            var builder = new DetailsDocumentBuilder(
                DetailsPalette.For(Background),
                Foreground,
                TextElement.GetFontFamily(this),
                TextElement.GetFontSize(this));
            try
            {
                DetailsViewer.Document = builder.Build(ViewModel.SelectedActivity);
            }
            catch (Exception ex)
            {
                DetailsViewer.Document = builder.BuildPlaceholder("Could not format the details: " + ex.Message + Environment.NewLine + ViewModel.SelectedActivity?.Details);
            }
        }

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == BackgroundProperty || e.Property == ForegroundProperty || e.Property == TextElement.FontSizeProperty)
            {
                ScheduleRender();
            }
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ActivityViewModel.SelectedActivity))
            {
                Track(ViewModel.SelectedActivity);
                ScheduleRender();
            }
        }

        private void Track(ActivityItem item)
        {
            if (renderedItem != null)
            {
                renderedItem.PropertyChanged -= OnSelectedItemChanged;
            }

            renderedItem = item;
            if (renderedItem != null)
            {
                renderedItem.PropertyChanged += OnSelectedItemChanged;
            }
        }

        private void OnSelectedItemChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ActivityItem.Details) || e.PropertyName == nameof(ActivityItem.Status))
            {
                ScheduleRender();
            }
        }

        private void ScheduleRender()
        {
            if (renderPending || DetailsViewer == null)
            {
                return;
            }

            renderPending = true;
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(RenderDetails));
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            try
            {
                await ViewModel.InitializeAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Copilot Activity: " + ex);
            }
        }

        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            try
            {
                await ViewModel.RefreshAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Copilot Activity: " + ex);
            }
        }

        private void OnAgentSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is AgentNode agent)
            {
                ViewModel.SelectedAgent = agent;
            }
        }

        private void OnClearFiltersClick(object sender, RoutedEventArgs e) => ViewModel.ResetFilters();

        private void OnKindChipMouseDown(object sender, MouseButtonEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && sender is ToggleButton chip && chip.DataContext is KindFilter filter)
            {
                ViewModel.SoloKind(filter);
                e.Handled = true;
            }
        }

        private void OnKindChipRightClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is ToggleButton chip && chip.DataContext is KindFilter filter)
            {
                ViewModel.SoloKind(filter);
                e.Handled = true;
            }
        }

        private void OnCopyDetailsClick(object sender, RoutedEventArgs e)
        {
            var text = ViewModel.SelectedActivity?.Details;
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            try
            {
                Clipboard.SetText(text);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // The clipboard can be locked by another process; copying again usually works.
            }
        }

        private void OnActivitiesAppended(object sender, EventArgs e)
        {
            if (!ViewModel.AutoScroll)
            {
                return;
            }

            _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                var count = ActivityList.Items.Count;
                if (count > 0)
                {
                    ActivityList.ScrollIntoView(ActivityList.Items[count - 1]);
                }
            }));
        }
    }
}
