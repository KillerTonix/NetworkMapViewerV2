using NetworkMapViewerV2.Models;
using NetworkMapViewerV2.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace NetworkMapViewerV2.Views
{
    // Part: Core
    // Shared state, constructor and DataContext / property-change wiring.
    // Other responsibilities live in MapCanvasView.*.cs (Rendering, Highlight, Selection,
    // Interaction, ContextMenus, AddItems, Tools, Discovery).
    public partial class MapCanvasView : UserControl
    {
        // --- NEW: Interactivity State ---
        /// <summary>Elements currently selected (shown with a cyan glow) on the canvas.</summary>
        internal readonly List<FrameworkElement> _selectedElements = [];

        /// <summary>The map tab currently displayed by this view (set when the DataContext changes).</summary>
        public MapTabState _currentState = new(); // Keep track of the current map
        private readonly DropShadowEffect _selectionGlow = new() { Color = Colors.Cyan, BlurRadius = 20, ShadowDepth = 0 };
        private Brush? _gridBrush;
        internal readonly Brush _standardBrush = new SolidColorBrush(Color.FromRgb(105, 105, 105));

        internal bool _isDragging = false;
        internal bool _wasAlreadySelected = false;
        internal Point _clickPosition;
        internal FrameworkElement? _draggedElement;
        internal int _originalZIndex;
        internal readonly Dictionary<FrameworkElement, Point> _dragStartPositions = [];

        /// <summary>Canvas position of the last right-click; used as the spawn point for new devices and labels.</summary>
        internal Point _lastRightClickPosition;

        // --- NEW: Marquee Selection & Clipboard State ---
        internal Point _selectionStartPoint;
        internal Rectangle? _selectionBox;
        internal bool _isMarqueeSelecting = false;
        internal Stack<Action> _undoStack = new();
        internal readonly static List<NetworkDevice> _copiedDevices = [];
        internal readonly static List<NetworkLabel> _copiedLabels = [];
        internal static int _pasteOffsetMultiplier = 1; // Makes multiple pastes cascade nicely!

        // Helper to get global state from our ViewModel
        internal static MainViewModel GlobalViewModel => (MainViewModel)Application.Current.MainWindow.DataContext;

        /// <summary>
        /// Initializes the view and hooks the canvas mouse events used for background clicks and marquee
        /// selection.
        /// </summary>
        public MapCanvasView()
        {
            InitializeComponent();
            this.DataContextChanged += MapCanvasView_DataContextChanged;

            // Clear selection if the user clicks the empty canvas background
            DrawingCanvas.MouseLeftButtonDown += (s, e) =>
            {
                if (e.OriginalSource == DrawingCanvas)
                {
                    this.Focus();
                    Keyboard.Focus(this);
                    SelectElement(null, false);
                }
            };
            this.DataContextChanged += MapCanvasView_DataContextChanged;
            // --- NEW: Marquee Selection Events ---
            DrawingCanvas.MouseLeftButtonDown += DrawingCanvas_MouseLeftButtonDown;
            DrawingCanvas.MouseMove += DrawingCanvas_MouseMove;
            DrawingCanvas.MouseLeftButtonUp += DrawingCanvas_MouseLeftButtonUp;
            this.Focusable = true;
            this.IsTabStop = true;
        }

        /// <summary>
        /// Switches the view to a new map tab: unsubscribes from the old tab, wires the redraw callback, draws
        /// the map, and listens to global ViewModel changes.
        /// </summary>
        private void MapCanvasView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Unsubscribe from the OLD tab's property changes to prevent memory leaks
            if (e.OldValue is MapTabState oldState)
            {
                oldState.PropertyChanged -= CurrentState_PropertyChanged;
            }
            if (e.NewValue is MapTabState state)
            {
                _currentState = state;

                state.TriggerRedraw = () =>
                {
                    // Ensure UI updates happen on the main UI thread
                    Application.Current.Dispatcher.Invoke(() => DrawMap(state));
                };

                // Subscribe to per-tab property changes (e.g. IsEditingEnabled toggled from another tab)
                state.PropertyChanged += CurrentState_PropertyChanged;

                // Redraw the map elements
                DrawMap(state);
                UpdateBackground();

                // --- NEW: As soon as a newly opened tab finishes drawing, check if we need to animate! ---
                CheckForPendingHighlight();

                // FIX: Grab keyboard focus when the tab is activated so Ctrl+C / Ctrl+V work immediately
                Dispatcher.InvokeAsync(
                    () => { this.Focus(); Keyboard.Focus(this); },
                    System.Windows.Threading.DispatcherPriority.Input);
            }

            // Listen for Global ViewModel changes (like Grid Mode toggling!)
            if (GlobalViewModel != null)
            {
                // Remove old handler to prevent memory leaks, then attach the new one
                GlobalViewModel.PropertyChanged -= GlobalViewModel_PropertyChanged;
                GlobalViewModel.PropertyChanged += GlobalViewModel_PropertyChanged;
            }
        }

        // Redraws the canvas when a per-tab property changes (e.g. IsEditingEnabled)
        private void CurrentState_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MapTabState.IsEditingEnabled))
                DrawMap(_currentState);
        }

        /// <summary>
        /// Reacts to global ViewModel changes: grid visibility updates the background, a highlight request
        /// starts the highlight animation.
        /// </summary>
        private void GlobalViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(GlobalViewModel.IsGridVisible))
            {
                UpdateBackground();
            }
            // --- CATCH THE ANIMATION SIGNAL! ---
            else if (e.PropertyName == nameof(GlobalViewModel.HighlightedDeviceId))
            {
                CheckForPendingHighlight();
            }
        }

        /// <summary>
        /// Forwards keyboard shortcuts (e.g. copy / paste) to MapKeyboardService. Referenced from
        /// MapCanvasView.xaml.
        /// </summary>
        private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            Services.MapKeyboardService.HandlePreviewKeyDown(e, this);
        }

        /// <summary>
        /// Re-applies Left / Top from the label and device models to their canvas elements, then refreshes the
        /// layout.
        /// </summary>
        public void SyncVisualPositions()
        {
            foreach (UIElement child in DrawingCanvas.Children)
            {
                if (child is FrameworkElement fe)
                {
                    // Check if child DataContext is a Label model
                    if (fe.DataContext is NetworkLabel label)
                    {
                        Canvas.SetLeft(fe, label.Left);
                        Canvas.SetTop(fe, label.Top);
                    }
                    // Check if child DataContext is a Device model
                    else if (fe.DataContext is NetworkDevice device)
                    {
                        Canvas.SetLeft(fe, device.Left);
                        Canvas.SetTop(fe, device.Top);
                    }
                }
            }

            DrawingCanvas.UpdateLayout();
        }
    }
}
