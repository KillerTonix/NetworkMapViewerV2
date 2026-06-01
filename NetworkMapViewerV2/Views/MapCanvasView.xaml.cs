using NetworkMapViewerV2.Helpers;
using NetworkMapViewerV2.Helpers.Alignment;
using NetworkMapViewerV2.Helpers.LocalFetcher;
using NetworkMapViewerV2.Helpers.Passwords;
using NetworkMapViewerV2.Models;
using NetworkMapViewerV2.Services;
using NetworkMapViewerV2.ViewModels;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using static NetworkMapViewerV2.Helpers.Alignment.Align;

namespace NetworkMapViewerV2.Views
{
    public partial class MapCanvasView : UserControl
    {

        // --- NEW: Interactivity State ---
        private List<FrameworkElement>? _selectedElements = [];
        public MapTabState? _currentState; // Keep track of the current map
        private DropShadowEffect? _selectionGlow = new() { Color = Colors.Cyan, BlurRadius = 20, ShadowDepth = 0 };
        private Brush? _gridBrush;
        private readonly Brush _standardBrush = new SolidColorBrush(Color.FromRgb(105, 105, 105));

        private bool _isDragging = false;
        private bool _wasAlreadySelected = false;
        private Point _clickPosition;
        private FrameworkElement? _draggedElement;
        private int _originalZIndex;
        private Dictionary<FrameworkElement, Point> _dragStartPositions = new();
        private Point _lastRightClickPosition;

        // --- NEW: Marquee Selection & Clipboard State ---
        private Point _selectionStartPoint;
        private Rectangle? _selectionBox;
        private bool _isMarqueeSelecting = false;

        private static List<NetworkDevice> _copiedDevices = [];
        private static List<NetworkLabel> _copiedLabels = [];
        private static int _pasteOffsetMultiplier = 1; // Makes multiple pastes cascade nicely!

        // Helper to get global state from our ViewModel
        private MainViewModel? GlobalViewModel => Application.Current.MainWindow.DataContext as MainViewModel;


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

        private void MapCanvasView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Unsubscribe from the OLD tab's property changes to prevent memory leaks
            if (e.OldValue is MapTabState oldState)
            {
                oldState.PropertyChanged -= CurrentState_PropertyChanged;
                oldState.RequestGatherDevices -= GatherOutOfBoundsDevices;
            }
            if (e.NewValue is MapTabState state)
            {
                _currentState = state;
                state.RequestGatherDevices += GatherOutOfBoundsDevices;


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

        private void DrawingCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Only start marquee if we click the empty canvas background, NOT an icon
            if (e.OriginalSource == DrawingCanvas && _currentState != null && _currentState.IsEditingEnabled)
            {
                SelectElement(null, false); // Clear old selection

                _isMarqueeSelecting = true;
                _selectionStartPoint = e.GetPosition(DrawingCanvas);

                _selectionBox = new Rectangle
                {
                    Stroke = Brushes.DodgerBlue,
                    StrokeThickness = 1,
                    Fill = new SolidColorBrush(Color.FromArgb(40, 30, 144, 255)), // Semi-transparent blue
                    StrokeDashArray = [2, 2]
                };

                Canvas.SetLeft(_selectionBox, _selectionStartPoint.X);
                Canvas.SetTop(_selectionBox, _selectionStartPoint.Y);
                Panel.SetZIndex(_selectionBox, 999);

                DrawingCanvas.Children.Add(_selectionBox);
                DrawingCanvas.CaptureMouse();
                this.Loaded += (s, e) => this.Focus();
            }
        }

        private void DrawingCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isMarqueeSelecting && _selectionBox != null)
            {
                Point currentPoint = e.GetPosition(DrawingCanvas);

                // Calculate the true X/Y and Width/Height (allows dragging backwards!)
                double x = Math.Min(currentPoint.X, _selectionStartPoint.X);
                double y = Math.Min(currentPoint.Y, _selectionStartPoint.Y);
                double width = Math.Abs(currentPoint.X - _selectionStartPoint.X);
                double height = Math.Abs(currentPoint.Y - _selectionStartPoint.Y);

                Canvas.SetLeft(_selectionBox, x);
                Canvas.SetTop(_selectionBox, y);
                _selectionBox.Width = width;
                _selectionBox.Height = height;
            }
        }

        private void DrawingCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isMarqueeSelecting && _selectionBox != null)
            {
                _isMarqueeSelecting = false;
                DrawingCanvas.ReleaseMouseCapture();

                // 1. Calculate the bounding box of our selection rectangle
                Rect selectionRect = new Rect(Canvas.GetLeft(_selectionBox), Canvas.GetTop(_selectionBox), _selectionBox.Width, _selectionBox.Height);

                // 2. Loop through all children and see if they intersect with the box!
                foreach (UIElement child in DrawingCanvas.Children)
                {
                    if (child is Border b && (b.Tag is NetworkDevice || b.Tag is NetworkLabel))
                    {
                        Rect elementBounds = new Rect(Canvas.GetLeft(b), Canvas.GetTop(b), b.ActualWidth, b.ActualHeight);
                        if (selectionRect.IntersectsWith(elementBounds))
                        {
                            SelectElement(b, true); // True = Multi-select!
                        }
                    }
                }

                // 3. Clean up the blue rectangle
                DrawingCanvas.Children.Remove(_selectionBox);
                _selectionBox = null;
            }
        }

        private void GlobalViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
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

        private void CheckForPendingHighlight()
        {
            if (GlobalViewModel != null && GlobalViewModel.HighlightedDeviceId > 0)
            {
                int targetId = GlobalViewModel.HighlightedDeviceId;

                // DispatcherPriority.Loaded forces WPF to wait until all elements are fully drawn and arranged!
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (UIElement child in DrawingCanvas.Children)
                    {
                        if (child is Border b && b.Tag is NetworkDevice d && d.DeviceId == targetId)
                        {
                            b.BringIntoView();
                            double cx = Canvas.GetLeft(b) + (b.ActualWidth / 2);
                            double cy = Canvas.GetTop(b) + (b.ActualHeight / 2);

                            PlayRippleAnimation(DrawingCanvas, cx, cy);

                            GlobalViewModel.HighlightedDeviceId = 0; // Clear the signal
                            break;
                        }
                    }
                }, System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        public void DrawMap(MapTabState? state)
        {
            DrawingCanvas.Children.Clear();
            _selectedElements?.Clear();

            DrawLabels(state);
            DrawDevices(state);
        }

        // ─── DRAW LABELS ─────────────────────────────────────────

        private void DrawLabels(MapTabState? state)
        {
            if (state == null) return;

            foreach (var label in state.Labels)
            {
                var border = new Border
                {
                    Width = label.Width > 0 ? label.Width : double.NaN,
                    Height = label.Height > 0 ? label.Height : double.NaN,
                    Background = ColorHelper.GetColorBrush(label.Background, Brushes.White),
                    BorderBrush = ColorHelper.GetColorBrush(label.BorderBrush, Brushes.Transparent),
                    BorderThickness = new Thickness(1),
                    Tag = label,
                    Cursor = _currentState != null && _currentState.IsEditingEnabled ? Cursors.SizeAll : Cursors.Arrow
                };
                TextOptions.SetTextFormattingMode(border, TextFormattingMode.Display);
                TextOptions.SetTextRenderingMode(border, TextRenderingMode.ClearType);

                AttachLabelContextMenu(border, label);

                if (label.TextLines.Count != 0)
                {
                    var (hAlign, vAlign, tAlign) = Align.ResolveLabelAlignment(label);

                    var textBlock = new TextBlock
                    {
                        Text = string.Join("\n", label.TextLines),
                        FontFamily = new FontFamily(label.FontFamily),
                        FontSize = label.FontSize + 2,
                        FontWeight = label.FontWeight == "Bold" ? FontWeights.Bold : FontWeights.Normal,
                        FontStyle = label.FontStyle == "Italic" ? FontStyles.Italic : FontStyles.Normal,
                        Foreground = ColorHelper.GetColorBrush(label.Foreground, Brushes.White),
                        HorizontalAlignment = hAlign,
                        VerticalAlignment = vAlign,
                        TextAlignment = tAlign,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(6)
                    };

                    border.Child = textBlock;
                }

                Canvas.SetLeft(border, label.Left);
                Canvas.SetTop(border, label.Top);
                Panel.SetZIndex(border, 0);

                border.MouseLeftButtonDown += Element_MouseLeftButtonDown;
                border.MouseMove += Element_MouseMove;
                border.MouseLeftButtonUp += Element_MouseLeftButtonUp;

                DrawingCanvas.Children.Add(border);
            }
        }

        // ─── DRAW DEVICES ────────────────────────────────────────

        private void DrawDevices(MapTabState? state)
        {
            if (state == null) return;

            // 1. Fetch all groups ONCE before the loop so we don't spam the database
            var repo = new Data.MapRepository();
            var allGroups = repo.GetAllDeviceGroups().ToDictionary(g => g.GroupId);
            var iconSizes = new Dictionary<string, Size>();
            foreach (var device in state.Devices)
            {
                var container = new Border
                {
                    Background = Brushes.Transparent,
                    Padding = new Thickness(2),
                    BorderThickness = new Thickness(2),
                    Tag = device,
                    Cursor = _currentState != null && _currentState.IsEditingEnabled ? Cursors.SizeAll : Cursors.Arrow
                };

                TextOptions.SetTextFormattingMode(container, TextFormattingMode.Display);
                TextOptions.SetTextRenderingMode(container, TextRenderingMode.ClearType);
                BuildDeviceTooltip(container, device);
                AttachDeviceContextMenu(container, device);

                var sp = new StackPanel { Orientation = Orientation.Vertical };

                // 2. Look up the Group Info from our Dictionary
                string typeName = "Unknown";
                string dbIconPath = "";

                if (allGroups.TryGetValue(device.GroupId, out var groupInfo))
                {
                    typeName = groupInfo.GroupName;
                    dbIconPath = groupInfo.IconPath ?? "";
                }

                // We still use 40x40 as a fallback ONLY if the image doesn't exist
                double imgWidth = 40, imgHeight = 40;

                if (!string.IsNullOrWhiteSpace(dbIconPath))
                {
                    // 1. Check if we already know the original size of this image
                    if (!iconSizes.ContainsKey(dbIconPath))
                    {
                        try
                        {
                            if (File.Exists(dbIconPath))
                            {
                                // --- THE FIX: Use PixelWidth and PixelHeight instead of Width and Height! ---
                                var tempBmp = new BitmapImage(new Uri(dbIconPath, UriKind.Absolute));
                                iconSizes[dbIconPath] = new Size(tempBmp.PixelWidth, tempBmp.PixelHeight);
                            }
                            else
                            {
                                iconSizes[dbIconPath] = new Size(40, 40); // Missing file fallback
                            }
                        }
                        catch
                        {
                            iconSizes[dbIconPath] = new Size(40, 40); // Error fallback
                        }
                    }

                    // 2. Apply the true original size!
                    imgWidth = iconSizes[dbIconPath].Width;
                    imgHeight = iconSizes[dbIconPath].Height;

                    var img = new Image { Width = imgWidth, Height = imgHeight };

                    // 3. Bind to IsOnline using your Converter
                    img.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding("IsOnline")
                    {
                        Source = device,
                        Converter = new PingStatusImageConverter(),
                        ConverterParameter = dbIconPath
                    });

                    sp.Children.Add(img);
                }
                else
                {
                    sp.Children.Add(new Border
                    {
                        Background = Brushes.Gray,
                        Width = imgWidth,
                        Height = imgHeight,
                        ToolTip = "Missing Icon Path in Database"
                    });
                }

                var displayLabels = device.Titles;
                foreach (var lbl in displayLabels)
                {
                    if (string.IsNullOrWhiteSpace(lbl)) continue;
                    string parsedText = lbl.Replace("%Address", device.Address);
                    sp.Children.Add(new TextBlock
                    {
                        Text = parsedText,
                        FontSize = 12,
                        FontFamily = new FontFamily("MS Sans Serif"),
                        FontWeight = FontWeights.Bold,
                        TextAlignment = TextAlignment.Center,
                        Foreground = Brushes.White
                    });
                }

                container.Child = sp;
                container.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double offsetX = container.DesiredSize.Width > imgWidth ? (container.DesiredSize.Width / 2) - (imgWidth / 2) : 0;
                container.Margin = new Thickness(-offsetX, -1, 0, 0);

                Canvas.SetLeft(container, device.Left);
                Canvas.SetTop(container, device.Top);
                Panel.SetZIndex(container, 10);

                // --- Click Events ---
                container.MouseLeftButtonDown += Element_MouseLeftButtonDown;
                container.MouseRightButtonDown += Element_MouseLeftButtonDown;
                container.MouseMove += Element_MouseMove;
                container.MouseLeftButtonUp += Element_MouseLeftButtonUp;
                container.MouseRightButtonUp += Element_MouseLeftButtonUp;

                DrawingCanvas.Children.Add(container);
            }
        }


        // ─── SELECTION & DRAG LOGIC ──────────────────────────────
        private void Element_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                // ==========================================
                // 1. HANDLE DOUBLE CLICKS FIRST
                // ==========================================
                if (e.ChangedButton != MouseButton.Left) return;
                if (e.ClickCount == 2)
                {
                    e.Handled = true; // Stop it from starting a drag!

                    if (_currentState != null && _currentState.IsEditingEnabled)
                    {
                        // EDIT MODE: Open the properties window
                        if (element.Tag is NetworkDevice dev)
                        {
                            var dlg = new DevicePropertiesWindow(dev, true) { Owner = Window.GetWindow(this) };
                            if (dlg.ShowDialog() == true) { _currentState?.HasUnsavedChanges = true; DrawMap(_currentState); }
                        }
                        else if (element.Tag is NetworkLabel lbl)
                        {
                            var dlg = new LabelPropertiesWindow(lbl, true) { Owner = Window.GetWindow(this) };
                            if (dlg.ShowDialog() == true) { _currentState?.HasUnsavedChanges = true; DrawMap(_currentState); }
                        }
                    }
                    else
                    {
                        // VIEW MODE: Run the Ping command or Open the Map Link
                        if (element.Tag is NetworkDevice dev)
                        {
                            var dlg = new DevicePropertiesWindow(dev, true) { Owner = Window.GetWindow(this) };
                            HandleDeviceDoubleClick(dev);
                        }
                    }
                    return; // Stop processing the rest of the mouse down logic!
                }
                // ==========================================
                // 2. SMART SELECTION LOGIC (Works in ALL modes!)
                // ==========================================
                bool isCtrlDown = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

                // Track if it was already highlighted before we clicked it
                _wasAlreadySelected = _selectedElements.Contains(element);

                if (isCtrlDown)
                {
                    if (_wasAlreadySelected)
                    {
                        // --- NEW: Ctrl + Click instantly toggles an item OFF! ---
                        element.Effect = null;
                        _selectedElements.Remove(element);
                        e.Handled = true;
                        return; // Stop right here so we don't accidentally start a drag!
                    }
                    else
                    {
                        SelectElement(element, true);
                    }
                }
                else
                {
                    if (!_wasAlreadySelected)
                    {
                        SelectElement(null, false);    // Clear old group
                        SelectElement(element, false); // Select this single item
                    }
                }

                // IMPORTANT: Stop the click from bleeding through to the canvas background!
                e.Handled = true;

                // ==========================================
                // 3. BATCH DRAG SETUP (ONLY in Edit Mode!)
                // ==========================================
                if (_currentState != null && _currentState.IsEditingEnabled)
                {
                    _draggedElement = element;
                    _isDragging = true;
                    _clickPosition = e.GetPosition(DrawingCanvas);

                    _dragStartPositions.Clear();
                    foreach (var el in _selectedElements)
                    {
                        if (el is FrameworkElement fw)
                        {
                            double currentLeft = double.IsNaN(Canvas.GetLeft(fw)) ? 0 : Canvas.GetLeft(fw);
                            double currentTop = double.IsNaN(Canvas.GetTop(fw)) ? 0 : Canvas.GetTop(fw);
                            _dragStartPositions[fw] = new Point(currentLeft, currentTop);
                        }
                    }

                    _originalZIndex = Panel.GetZIndex(element);
                    Panel.SetZIndex(element, 100);
                    element.CaptureMouse();
                }
            }
        }

        private void Element_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && _draggedElement != null)
            {
                Point currentPosition = e.GetPosition(DrawingCanvas);

                // Calculate total movement distance from the very beginning of the click
                double totalDeltaX = currentPosition.X - _clickPosition.X;
                double totalDeltaY = currentPosition.Y - _clickPosition.Y;

                // SHIFT-LOCK LOGIC (Still works for batch movement!)
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    if (Math.Abs(totalDeltaX) > Math.Abs(totalDeltaY))
                        totalDeltaY = 0; // Lock vertically, move horizontally
                    else
                        totalDeltaX = 0; // Lock horizontally, move vertically
                }

                // --- BATCH MOVEMENT: Apply the movement delta to ALL selected items! ---
                if (_selectedElements != null)
                {
                    foreach (var el in _selectedElements)
                    {
                        if (el is FrameworkElement fw && _dragStartPositions.TryGetValue(fw, out Point startPos))
                        {
                            // Apply the distance to their ORIGINAL starting positions
                            double newLeft = startPos.X + totalDeltaX;
                            double newTop = startPos.Y + totalDeltaY;

                            EnforceBounds(fw, ref newLeft, ref newTop); // Keep them on screen

                            Canvas.SetLeft(fw, newLeft);
                            Canvas.SetTop(fw, newTop);

                            // Update the SQLite memory model
                            UpdateModelPosition(fw, newLeft, newTop);
                        }
                    }
                }
            }
        }

        private void Element_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (_isDragging && _draggedElement != null)
            {
                _draggedElement.ReleaseMouseCapture();
                Panel.SetZIndex(_draggedElement, _originalZIndex);

                // ==========================================
                // --- NEW: REPEAT CLICK TOGGLE LOGIC ---
                // ==========================================
                Point upPosition = e.GetPosition(DrawingCanvas);

                // Calculate how far the mouse actually moved
                double moveDistanceX = Math.Abs(upPosition.X - _clickPosition.X);
                double moveDistanceY = Math.Abs(upPosition.Y - _clickPosition.Y);

                // If they didn't really move the mouse (it was a click, not a drag)
                if (moveDistanceX < 2 && moveDistanceY < 2)
                {
                    if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                    {
                        if (_wasAlreadySelected && _selectedElements?.Count == 1)
                        {
                            // It was the ONLY item selected. Unselect it!
                            SelectElement(null, false);
                        }
                        else if (_wasAlreadySelected && _selectedElements?.Count > 1)
                        {
                            // Multiple items were selected. Clear the group and select ONLY this one!
                            SelectElement(null, false);
                            SelectElement(_draggedElement, false);
                        }
                    }
                }

                _isDragging = false;
                _draggedElement = null;

                e.Handled = true;
            }
        }

        public static void UpdateModelPosition(FrameworkElement el, double? left, double? top)
        {
            if (el.Tag is NetworkDevice device) { if (left.HasValue) device.Left = left.Value; if (top.HasValue) device.Top = top.Value; }
            else if (el.Tag is NetworkLabel label) { if (left.HasValue) label.Left = left.Value; if (top.HasValue) label.Top = top.Value; }
        }

        private bool HandleDeviceDoubleClick(NetworkDevice device)
        {
            var repo = new Data.MapRepository();

            // 1. IS THIS A MAP LINK DEVICE?
            var groups = repo.GetAllDeviceGroups();
            var myGroup = groups.FirstOrDefault(g => g.GroupId == device.GroupId);

            if (myGroup != null && myGroup.IsMapLink)
            {
                if (device.TargetMapId.HasValue)
                {
                    GlobalViewModel?.OpenMapFromDatabase(device.TargetMapId.Value);
                    return true;
                }
                else
                {
                    MessageBox.Show("This device is a Map Link, but no target map is selected!\nRight-click and configure it.", "No Link", MessageBoxButton.OK, MessageBoxImage.Information);
                    return false;
                }
            }

            // 2. NORMAL EXTERNAL COMMAND LOGIC
            var appSettings = Services.SettingsService.Load();
            string targetCommandName = myGroup?.DefaultCommand ?? "Ping";

            ExternalCommand? commandToExecute = null;
            ExternalCommand? fallbackPingCommand = null;

            foreach (var cmd in appSettings.Commands)
            {
                if (cmd.Name == targetCommandName) commandToExecute = cmd;
                if (cmd.Name == "Ping") fallbackPingCommand = cmd;
            }

            commandToExecute ??= fallbackPingCommand;

            if (commandToExecute != null)
            {
                Helpers.CommandHelper.ExecuteExternalCommand(commandToExecute, device.Address);
                return true;
            }

            return false;
        }



        private void SelectElement(FrameworkElement? element, bool multiSelect)
        {
            // Clear old selection if we aren't holding CTRL
            if (!multiSelect)
            {
                if (_selectedElements != null)
                {
                    foreach (var el in _selectedElements)
                    {
                        el.Effect = null; // Remove glow
                    }
                    _selectedElements.Clear();
                }
            }

            if (element != null && !_selectedElements?.Contains(element) == true)
            {
                _selectedElements?.Add(element);
                this.Loaded += (s, e) =>
                {
                    this.Focus();
                    Keyboard.Focus(this);
                };
                element.Effect = _selectionGlow; // Apply visual cyan glow!
            }
        }
        // ─── CONTEXT MENUS ───────────────────────────────────────

        private void AttachDeviceContextMenu(Border container, NetworkDevice device)
        {

            // 1. Give it a blank menu immediately so WPF knows it can be right-clicked!
            container.ContextMenu = new ContextMenu();

            container.ContextMenuOpening += (s, e) =>
            {
                // ==========================================
                // --- AUTO-SELECT ON RIGHT CLICK ---
                // ==========================================
                if (!_selectedElements?.Contains(container) == true)
                {
                    SelectElement(null, false);      // Clear old group
                    SelectElement(container, false); // Select this specific item
                }

                // 2. Grab the existing menu and clear out the old items!
                var menu = container.ContextMenu;
                menu.Items.Clear();

                // ==========================================
                // 1. ALWAYS VISIBLE (View & Edit Mode)
                // ==========================================

                // Load your commands from settings (Ping, SSH, Web, etc.)
                var appSettings = Services.SettingsService.Load();
                foreach (var cmd in appSettings.Commands)
                {
                    var miCmd = new MenuItem { Icon = cmd.Icon, Header = cmd.Name };
                    miCmd.Click += (sender, args) =>
                    {
                        CommandHelper.ExecuteExternalCommand(cmd, device.Address);
                    };
                    menu.Items.Add(miCmd);
                }

                menu.Items.Add(new Separator());

                var miEdit = new MenuItem { Icon = "⚙️", Header = "Properties (F2)" };
                miEdit.Click += (sender, args) =>
                {
                    bool isEditeMode = true;
                    if (_currentState == null || !_currentState.IsEditingEnabled) isEditeMode = false;

                    var dlg = new DevicePropertiesWindow(device, isEditeMode) { Owner = Window.GetWindow(this) };
                    if (dlg.ShowDialog() == true)
                    {
                        _currentState?.HasUnsavedChanges = true;
                        DrawMap(_currentState);
                    }
                };
                menu.Items.Add(miEdit);


                // If it's a Map Link, add the option to jump to the other map!
                var repo = new Data.MapRepository();
                var groups = repo.GetAllDeviceGroups();
                var myGroup = groups.FirstOrDefault(g => g.GroupId == device.GroupId);

                if (myGroup != null && myGroup.IsMapLink && device.TargetMapId.HasValue)
                {
                    menu.Items.Add(new Separator());
                    var miOpenMap = new MenuItem { Icon = "🗺️", Header = "Open Linked Map", FontWeight = FontWeights.Bold };
                    miOpenMap.Click += (sender, args) =>
                    {
                        GlobalViewModel?.OpenMapFromDatabase(device.TargetMapId.Value);
                    };
                    menu.Items.Add(miOpenMap);
                }

                // ==========================================
                // 2. EDIT MODE ONLY
                // ==========================================
                if (_currentState != null && _currentState.IsEditingEnabled)
                {

                    // --- AUTO-FILL SCRIPTS ---
                    var miAutoFill = new MenuItem { Icon = "🔍", Header = "Auto-Fill PC Specs" };

                    var miDomain = new MenuItem { Icon = "💻", Header = "Domain Joined PC" };
                    miDomain.Click += async (sender, args) => await AutoFill.RunAutoFillScript(this, device, "SystemInfo.ps1");

                    var miNonDomain = new MenuItem { Icon = "🖥️", Header = "Non-Domain PC" };
                    miNonDomain.Click += async (sender, args) => await AutoFill.RunAutoFillScript(this, device, $"SystemInfo Non Domain.ps1");

                    var miDefaultPC = new MenuItem { Icon = "🖥️", Header = "Default PC" };
                    miDefaultPC.Click += async (sender, args) => await AutoFill.RunAutoFillScript(this, device, $"SystemInfo Default.ps1");

                    var miLinux = new MenuItem { Icon = "🐧", Header = "Linux PC (SSH)" };
                    miLinux.Click += async (sender, args) => await AutoFill.RunAutoFillScript(this, device, $"SystemInfo Linux.ps1");

                    var miPrinter = new MenuItem { Icon = "🖨️", Header = "Printer" };
                    miPrinter.Click += async (sender, args) => await AutoFill.RunPrinterAutoFill(GlobalViewModel, this, device);

                    var miPhone = new MenuItem { Icon = "☎️", Header = "Grandstream" };
                    miPhone.Click += async (sender, args) => await AutoFill.RunGrandstreamAutoFill(GlobalViewModel, this, device);

                    miAutoFill.Items.Add(miDomain);
                    miAutoFill.Items.Add(miNonDomain);
                    miAutoFill.Items.Add(miDefaultPC);
                    miAutoFill.Items.Add(miLinux);
                    miAutoFill.Items.Add(new Separator());
                    miAutoFill.Items.Add(miPrinter);
                    miAutoFill.Items.Add(miPhone);

                    menu.Items.Add(miAutoFill);

                    var miDelete = new MenuItem { Icon = "🗑️", Header = "Delete (Del)" };
                    miDelete.Click += (sender, args) =>
                    {
                        if (_currentState != null)
                        {
                            _currentState.Devices.Remove(device);
                            _currentState?.HasUnsavedChanges = true;
                            DrawMap(_currentState);
                        }
                    };
                    menu.Items.Add(miDelete);
                }

                // NOTICE: We removed the `container.ContextMenu = menu;` line from down here!
            };
        }
        private void AttachLabelContextMenu(Border container, NetworkLabel label)
        {
            // Everything happens inside this event the moment the user right-clicks!
            container.ContextMenuOpening += (s, e) =>
            {
                // 1. If we are in View Mode, block the menu entirely!
                if (_currentState == null || !_currentState.IsEditingEnabled)
                {
                    e.Handled = true;
                    return;
                }

                // ==========================================
                // 2. AUTO-SELECT ON RIGHT CLICK
                // ==========================================
                if (!_selectedElements.Contains(container))
                {
                    SelectElement(null, false);      // Clear old group
                    SelectElement(container, false); // Select this specific item
                }

                // ==========================================
                // 3. BUILD THE MENU DYNAMICALLY
                // ==========================================
                var contextMenu = new ContextMenu();

                var configMenu = new MenuItem { Icon = "⚙️", Header = "Configure Label (F2)", FontWeight = FontWeights.Bold };

                configMenu.Click += (sender, args) =>
                {
                    var dlg = new LabelPropertiesWindow(label, true) { Owner = Window.GetWindow(this) };
                    if (dlg.ShowDialog() == true)
                    {
                        if (_currentState != null) _currentState.HasUnsavedChanges = true;
                        DrawMap(_currentState);
                    }
                };
                contextMenu.Items.Add(configMenu);

                // Bonus: Added the Delete button here so it matches your Device menu!
                var miDelete = new MenuItem { Icon = "🗑️", Header = "Delete (Del)" };
                miDelete.Click += (sender, args) =>
                {
                    if (_currentState != null)
                    {
                        _currentState.Labels.Remove(label);
                        if (_currentState != null) _currentState.HasUnsavedChanges = true;
                        DrawMap(_currentState);
                    }
                };
                contextMenu.Items.Add(miDelete);

                container.ContextMenu = contextMenu;
            };
        }





        // ─── TOOLTIPS & ALIGNMENT ────────────────────────────────

        private static void BuildDeviceTooltip(Border container, NetworkDevice device)
        {
            var toolTipPanel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(5) };

            if (!string.IsNullOrWhiteSpace(device.HintImagePath) && File.Exists(device.HintImagePath))
            {
                try
                {
                    var hintImg = new Image { MaxWidth = 100, MaxHeight = 100, Margin = new Thickness(1, 1, 1, 5) };
                    var hintBmp = new BitmapImage(new Uri(device.HintImagePath, UriKind.Absolute));
                    hintImg.Source = hintBmp;
                    toolTipPanel.Children.Add(hintImg);
                }
                catch { }
            }

            if (device.Hints.Count != 0)
            {
                var hintTextBlock = new TextBlock { Margin = new Thickness(0, 0, 0, 5) };
                string rawHintText = string.Join("\n", device.Hints);
                RenderHtmlToTextBlock(hintTextBlock, rawHintText);
                toolTipPanel.Children.Add(hintTextBlock);
            }

            container.ToolTip = toolTipPanel.Children.Count > 0 ? toolTipPanel : null;
        }

        private static void RenderHtmlToTextBlock(TextBlock tb, string text)
        {
            tb.Inlines.Clear();
            string[] parts = Regex.Split(text, @"(<[^>]+>)");
            bool isBold = false, isItalic = false, isUnderline = false;

            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part)) continue;
                string lowerPart = part.ToLower();

                if (lowerPart == "<b>") isBold = true;
                else if (lowerPart == "</b>") isBold = false;
                else if (lowerPart == "<i>") isItalic = true;
                else if (lowerPart == "</i>") isItalic = false;
                else if (lowerPart == "<u>") isUnderline = true;
                else if (lowerPart == "</u>") isUnderline = false;
                else if (lowerPart == "<br>" || lowerPart == "<br/>") tb.Inlines.Add(new LineBreak());
                else
                {
                    var run = new Run(part)
                    {
                        FontWeight = isBold ? FontWeights.Bold : FontWeights.Normal,
                        FontStyle = isItalic ? FontStyles.Italic : FontStyles.Normal,
                        TextDecorations = isUnderline ? TextDecorations.Underline : null
                    };
                    tb.Inlines.Add(run);
                }
            }
        }


















        // --- HELPER: Finds a specific control inside a container ---



        private Brush GetGridBrush()
        {
            if (_gridBrush == null)
            {
                var brush = new DrawingBrush
                {
                    Viewport = new Rect(0, 0, 10, 10),
                    ViewportUnits = BrushMappingMode.Absolute,
                    TileMode = TileMode.Tile
                };

                var drawingGroup = new DrawingGroup();
                drawingGroup.Children.Add(new GeometryDrawing(_standardBrush, null, new RectangleGeometry(new Rect(0, 0, 10, 10))));

                var pen = new Pen((Brush)new BrushConverter().ConvertFrom("#191919"), 1);
                var lineGeometry = new GeometryGroup();
                lineGeometry.Children.Add(new LineGeometry(new Point(0, 0), new Point(10, 0)));
                lineGeometry.Children.Add(new LineGeometry(new Point(0, 0), new Point(0, 10)));

                drawingGroup.Children.Add(new GeometryDrawing(null, pen, lineGeometry));

                brush.Drawing = drawingGroup;
                brush.Freeze(); // Optimize for UI thread
                _gridBrush = brush;
            }
            return _gridBrush;
        }

        private void UpdateBackground()
        {
            DrawingCanvas.Background = GlobalViewModel != null && GlobalViewModel.IsGridVisible ? GetGridBrush() : _standardBrush;
        }



        private static void PlayRippleAnimation(Canvas canvas, double centerX, double centerY)
        {
            const double startSize = 10;
            const double endSize = 220;
            int ringCount = 5;
            var ringColor = Color.FromArgb(200, 255, 200, 0); // bright gold

            for (int i = 0; i < ringCount; i++)
            {
                var ring = new Ellipse
                {
                    Width = startSize,
                    Height = startSize,
                    Fill = Brushes.Transparent,
                    Stroke = new SolidColorBrush(ringColor),
                    StrokeThickness = 10,
                    IsHitTestVisible = false
                };

                Canvas.SetLeft(ring, centerX - startSize / 2);
                Canvas.SetTop(ring, centerY - startSize / 2);
                Panel.SetZIndex(ring, 999);
                canvas.Children.Add(ring);

                var delay = TimeSpan.FromMilliseconds(i * 200);
                var duration = TimeSpan.FromMilliseconds(800);

                var animWidth = new DoubleAnimation { From = startSize, To = endSize, Duration = duration, BeginTime = delay, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                var animHeight = new DoubleAnimation { From = startSize, To = endSize, Duration = duration, BeginTime = delay, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                var animLeft = new DoubleAnimation { From = centerX - startSize / 2, To = centerX - endSize / 2, Duration = duration, BeginTime = delay, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                var animTop = new DoubleAnimation { From = centerY - startSize / 2, To = centerY - endSize / 2, Duration = duration, BeginTime = delay, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                var animOpacity = new DoubleAnimation { From = 1.0, To = 0.0, Duration = duration, BeginTime = delay, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
                var animStroke = new DoubleAnimation { From = 3.0, To = 0.5, Duration = duration, BeginTime = delay };

                animOpacity.Completed += (s, e) => canvas.Children.Remove(ring);

                ring.BeginAnimation(FrameworkElement.WidthProperty, animWidth);
                ring.BeginAnimation(FrameworkElement.HeightProperty, animHeight);
                ring.BeginAnimation(Canvas.LeftProperty, animLeft);
                ring.BeginAnimation(Canvas.TopProperty, animTop);
                ring.BeginAnimation(UIElement.OpacityProperty, animOpacity);
                ring.BeginAnimation(Shape.StrokeThicknessProperty, animStroke);
            }
        }

        // ─── DYNAMIC CREATION MENUS ──────────────────────────────
        private void DrawingCanvas_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (e.OriginalSource != DrawingCanvas) return;
            if (_currentState == null || !_currentState.IsEditingEnabled)
            {
                e.Handled = true;
                return;
            }

            _lastRightClickPosition = Mouse.GetPosition(DrawingCanvas);

            // Dynamically build the menu!
            var menu = new ContextMenu();

            var miAddDevice = new MenuItem { Icon = "🖥️", Header = "Add Device Here...", FontWeight = FontWeights.Bold };

            // Fetch groups from DB so you can select the type BEFORE adding!
            var repo = new Data.MapRepository();
            var groups = repo.GetDeviceGroups();
            foreach (var group in groups)
            {
                var subItem = new MenuItem { Header = group.DisplayName, Tag = group.GroupId };
                subItem.Click += (s, ev) => AddSpecificDevice(group.GroupId, _lastRightClickPosition);
                miAddDevice.Items.Add(subItem);
            }

            var miBatchAdd = new MenuItem { Icon = "📦", Header = "Batch Add Devices..." };
            miBatchAdd.Click += BatchAdd_Click;

            var miAddLabel = new MenuItem { Icon = "📝", Header = "Add Label Here...", FontWeight = FontWeights.Bold };
            miAddLabel.Click += AddLabel_Click;

            var miBatchLabels = new MenuItem { Icon = "📦", Header = "Batch Add Labels..." };
            miBatchLabels.Click += BatchAddLabels_Click;

            var miAdvancedGrid = new MenuItem { Icon = "⚙️", Header = "Advanced Grid Generator..." };
            miAdvancedGrid.Click += (s, ev) =>
            {
                if (_currentState == null) return;

                var dlg = new GridLabelGeneratorWindow(_currentState.MapId, _lastRightClickPosition)
                {
                    Owner = Window.GetWindow(this)
                };

                if (dlg.ShowDialog() == true && dlg.GeneratedLabels.Count > 0)
                {
                    foreach (var lbl in dlg.GeneratedLabels)
                    {
                        _currentState.Labels.Add(lbl);
                    }
                    if (GlobalViewModel != null) if (_currentState != null) _currentState.HasUnsavedChanges = true;
                    DrawMap(_currentState);
                }
            };


            // --- NEW: ALIGNMENT MENU (Only visible when multiple items are selected!) ---
            if (_selectedElements != null && _selectedElements.Count > 1)
            {
                menu.Items.Add(new Separator());
                var miAlign = new MenuItem { Icon = "📐", Header = "Align Selected...", FontWeight = FontWeights.Bold };

                var miAlignLeft = new MenuItem { Header = "Align Left" };
                miAlignLeft.Click += (s, ev) => Align.AlignSelectedElements(_selectedElements, GlobalViewModel, this, AlignMode.Left);

                var miAlignCenter = new MenuItem { Header = "Align Center (Horizontal)" };
                miAlignCenter.Click += (s, ev) => Align.AlignSelectedElements(_selectedElements, GlobalViewModel, this, AlignMode.Center);

                var miAlignRight = new MenuItem { Header = "Align Right" };
                miAlignRight.Click += (s, ev) => Align.AlignSelectedElements(_selectedElements, GlobalViewModel, this, AlignMode.Right);

                var miAlignTop = new MenuItem { Header = "Align Top" };
                miAlignTop.Click += (s, ev) => Align.AlignSelectedElements(_selectedElements, GlobalViewModel, this, AlignMode.Top);

                var miAlignMiddle = new MenuItem { Header = "Align Middle (Vertical)" };
                miAlignMiddle.Click += (s, ev) => Align.AlignSelectedElements(_selectedElements, GlobalViewModel, this, AlignMode.Middle);

                var miAlignBottom = new MenuItem { Header = "Align Bottom" };
                miAlignBottom.Click += (s, ev) => Align.AlignSelectedElements(_selectedElements, GlobalViewModel, this, AlignMode.Bottom);

                miAlign.Items.Add(miAlignLeft);
                miAlign.Items.Add(miAlignCenter);
                miAlign.Items.Add(miAlignRight);
                miAlign.Items.Add(new Separator());
                miAlign.Items.Add(miAlignTop);
                miAlign.Items.Add(miAlignMiddle);
                miAlign.Items.Add(miAlignBottom);

                menu.Items.Add(miAlign);
            }

            if (_selectedElements.Any(e => e.Tag is NetworkDevice) && _selectedElements.Any(e => e.Tag is NetworkLabel))
            {
                var miAutoAlign = new MenuItem { Icon = "✨", Header = "Auto-Align Devices (Alt+A)" };
                miAutoAlign.Click += (sender, args) => AutoAlignSelectedPairs();
                menu.Items.Add(miAutoAlign);
            }

            menu.Items.Add(miAddDevice);
            menu.Items.Add(miBatchAdd);
            menu.Items.Add(new Separator());
            menu.Items.Add(miAddLabel);
            menu.Items.Add(miBatchLabels);
            menu.Items.Add(new Separator());
            menu.Items.Add(miAdvancedGrid);

            DrawingCanvas.ContextMenu = menu;
        }

        private void AddSpecificDevice(int groupId, Point position)
        {
            if (_currentState == null) return;

            var newDevice = new NetworkDevice
            {
                MapId = _currentState.MapId,
                Address = "0.0.0.0",
                Left = position.X,
                Top = position.Y,
                GroupId = groupId
            };
            newDevice.Titles.Add("%Address");

            // --- NEW: Predefined hints based on device type (GroupId) ---
            switch (groupId)
            {
                case 1: // Windows PC
                    newDevice.Hints.Add("<b>NAME:</b> ");
                    newDevice.Hints.Add("<b>MAC:</b> ");
                    newDevice.Hints.Add("<b>IP:</b> ");
                    newDevice.Hints.Add("<b>CPU:</b>");
                    newDevice.Hints.Add("<b>RAM:</b> 16GB");
                    newDevice.Hints.Add("<b>SSD:</b>");
                    newDevice.Hints.Add("<b>GRAPHIC:</b> Intel(R) UHD Graphics 730");
                    newDevice.Hints.Add("<b>OS:</b> Microsoft Windows 11 Pro");
                    break;

                case 2: // Ubuntu Server
                    newDevice.Hints.Add("<b>NAME:</b> ");
                    newDevice.Hints.Add("<b>MAC:</b> ");
                    newDevice.Hints.Add("<b>IP:</b> ");
                    newDevice.Hints.Add("<b>CPU:</b> ");
                    newDevice.Hints.Add("<b>RAM:</b> ");
                    newDevice.Hints.Add("<b>OS:</b> Ubuntu 20.04 LTS");
                    break;

                case 9: // Grandstream Phone
                    newDevice.Hints.Add("<b>NAME:</b> GRANDSTREAM");
                    newDevice.Hints.Add("<b>MODEL:</b> GXP1628");
                    newDevice.Hints.Add("<b>MAC:</b> ");
                    newDevice.Hints.Add("<b>IP:</b> ");
                    break;

                case 12: // Printer
                    newDevice.Hints.Add("<b>NAME:</b> HP LaserJet");
                    newDevice.Hints.Add("<b>MODEL:</b> MFP ");
                    newDevice.Hints.Add("<b>MAC:</b> ");
                    newDevice.Hints.Add("<b>IP:</b> ");
                    newDevice.Hints.Add("<b>Host Name:</b> ");
                    break;

                default: // Fallback for any other device type not listed above
                    newDevice.Hints.Add("<b>NAME:</b> ");
                    newDevice.Hints.Add("<b>IP:</b> ");
                    break;
            }

            // AUTO-OPEN THE EDIT WINDOW!
            var dlg = new DevicePropertiesWindow(newDevice, true) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                _currentState.Devices.Add(newDevice);
                if (GlobalViewModel != null) if (_currentState != null) _currentState.HasUnsavedChanges = true;
                DrawMap(_currentState);
            }
        }

        private void AddLabel_Click(object sender, RoutedEventArgs e)
        {
            if (_currentState == null) return;

            var newLabel = new NetworkLabel
            {
                MapId = _currentState.MapId,
                Left = _lastRightClickPosition.X,
                Top = _lastRightClickPosition.Y,
                Width = 100,
                Height = 30,
                FontSize = 12,
                Foreground = "#000000"
            };
            newLabel.TextLines.Add("New Label Text");

            // AUTO-OPEN THE EDIT WINDOW!
            var dlg = new LabelPropertiesWindow(newLabel, true) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                _currentState.Labels.Add(newLabel);
                if (GlobalViewModel != null) if (_currentState != null) _currentState.HasUnsavedChanges = true;
                DrawMap(_currentState);
            }
        }

        private void BatchAddLabels_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Window { Title = "Batch Add Labels", Width = 300, Height = 420, WindowStyle = WindowStyle.ToolWindow, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Window.GetWindow(this) };
            var sp = new StackPanel { Margin = new Thickness(15) };

            sp.Children.Add(new TextBlock { Text = "Quantity to add:" });
            var txtCount = new TextBox { Text = "5", Margin = new Thickness(0, 0, 0, 10) };
            sp.Children.Add(txtCount);

            sp.Children.Add(new TextBlock { Text = "Width:" });
            var txtWidth = new TextBox { Text = "125", Margin = new Thickness(0, 0, 0, 10) };
            sp.Children.Add(txtWidth);

            sp.Children.Add(new TextBlock { Text = "Height:" });
            var txtHeight = new TextBox { Text = "120", Margin = new Thickness(0, 0, 0, 10) };
            sp.Children.Add(txtHeight);

            sp.Children.Add(new TextBlock { Text = "Left (Start X):" });
            // Default to the mouse click position instead of 0!
            var txtLeft = new TextBox { Text = _lastRightClickPosition.X.ToString(), Margin = new Thickness(0, 0, 0, 10) };
            sp.Children.Add(txtLeft);

            sp.Children.Add(new TextBlock { Text = "Top (Start Y):" });
            // Default to the mouse click position instead of 0!
            var txtTop = new TextBox { Text = _lastRightClickPosition.Y.ToString(), Margin = new Thickness(0, 0, 0, 10) };
            sp.Children.Add(txtTop);

            sp.Children.Add(new TextBlock { Text = "Color:" });
            var colorPicker = new Xceed.Wpf.Toolkit.ColorPicker { Margin = new Thickness(0, 0, 0, 10) };
            sp.Children.Add(colorPicker);

            sp.Children.Add(new TextBlock { Text = "Orientation:" });
            var txtHorizontal = new RadioButton { Content = "Horizontally", Margin = new Thickness(0, 0, 0, 0) };
            var txtVertical = new RadioButton { Content = "Vertically", IsChecked = true, Margin = new Thickness(0, 0, 0, 10) };
            sp.Children.Add(txtHorizontal);
            sp.Children.Add(txtVertical);

            var btn = new Button { Content = "Spawn Labels", IsDefault = true, Padding = new Thickness(5) };
            btn.Click += (s, ev) => { dlg.DialogResult = true; dlg.Close(); };
            sp.Children.Add(btn);
            dlg.Content = sp;

            if (dlg.ShowDialog() == true && int.TryParse(txtCount.Text, out int count))
            {
                // 1. Properly parse ALL the text boxes
                double.TryParse(txtWidth.Text, out double w);
                double.TryParse(txtHeight.Text, out double h);
                double.TryParse(txtLeft.Text, out double baseLeft);
                double.TryParse(txtTop.Text, out double baseTop);
                string selectedColor = colorPicker.SelectedColor?.ToString() ?? "Transparent";
                for (int i = 0; i < count; i++)
                {
                    // 2. Calculate offsets. (Horizontal uses Width + 10 gap, Vertical uses 40 as requested)
                    double leftOffset = txtHorizontal.IsChecked == true ? (i * w) : 0;
                    double topOffset = txtVertical.IsChecked == true ? (i * 40) : 0;

                    var newLabel = new NetworkLabel
                    {
                        MapId = _currentState.MapId,
                        Left = baseLeft + leftOffset, // Add the offset to the base position!
                        Top = baseTop + topOffset,    // Add the offset to the base position!
                        Width = w,                    // Use the parsed Width
                        Height = h,                   // Use the parsed Height
                        FontSize = 12,
                        Foreground = "#FFFFFF",
                        Background = selectedColor,
                        BorderBrush = "#000000",
                    };
                    newLabel.TextLines.Add($"Label {i + 1}");
                    _currentState.Labels.Add(newLabel);
                }

                if (GlobalViewModel != null) if (_currentState != null) _currentState.HasUnsavedChanges = true;
                DrawMap(_currentState);
            }
        }

        private void BatchAdd_Click(object sender, RoutedEventArgs e)
        {
            // Creates a fast, dynamic popup window just for batch adding!
            var repo = new Data.MapRepository();
            var groups = repo.GetDeviceGroups();

            var dlg = new Window { Title = "Batch Add", Width = 300, Height = 250, WindowStyle = WindowStyle.ToolWindow, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Window.GetWindow(this) };
            var sp = new StackPanel { Margin = new Thickness(15) };

            sp.Children.Add(new TextBlock { Text = "Device Type:" });
            var cmb = new ComboBox { ItemsSource = groups, DisplayMemberPath = "DisplayName", SelectedValuePath = "GroupId", SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 10) };
            sp.Children.Add(cmb);

            sp.Children.Add(new TextBlock { Text = "Quantity to add:" });
            var txtCount = new TextBox { Text = "5", Margin = new Thickness(0, 0, 0, 10) };
            sp.Children.Add(txtCount);

            sp.Children.Add(new TextBlock { Text = "Orientation:" });
            var txtHorizontal = new RadioButton { Content = "Horizontally", Margin = new Thickness(0, 0, 0, 0) };
            var txtVertical = new RadioButton { Content = "Vertically", IsChecked = true, Margin = new Thickness(0, 0, 0, 10) };
            sp.Children.Add(txtHorizontal);
            sp.Children.Add(txtVertical);

            var btn = new Button { Content = "Spawn Devices", IsDefault = true, Padding = new Thickness(5) };
            btn.Click += (s, ev) => { dlg.DialogResult = true; dlg.Close(); };
            sp.Children.Add(btn);
            dlg.Content = sp;

            if (dlg.ShowDialog() == true && int.TryParse(txtCount.Text, out int count) && cmb.SelectedValue != null)
            {
                int groupId = (int)cmb.SelectedValue;
                for (int i = 0; i < count; i++)
                {
                    // 2. Calculate offsets. (Horizontal uses Width + 10 gap, Vertical uses 40 as requested)
                    double leftOffset = txtHorizontal.IsChecked == true ? (i * 125) : 0;
                    double topOffset = txtVertical.IsChecked == true ? (i * 50) : 0;

                    var newDevice = new NetworkDevice
                    {
                        MapId = _currentState.MapId,
                        Address = "0.0.0.0",
                        GroupId = groupId,
                        Left = _lastRightClickPosition.X + leftOffset,
                        Top = _lastRightClickPosition.Y + topOffset
                    };
                    newDevice.Titles.Add("%Address");
                    _currentState.Devices.Add(newDevice);
                }
                if (GlobalViewModel != null) if (_currentState != null) _currentState.HasUnsavedChanges = true;
                DrawMap(_currentState);
            }
        }

        private void AddDevice_Click(object sender, RoutedEventArgs e)
        {
            if (_currentState == null) return;

            var newDevice = new NetworkDevice
            {
                MapId = _currentState.MapId,
                Address = "0.0.0.0",
                Left = _lastRightClickPosition.X,
                Top = _lastRightClickPosition.Y,
                GroupId = 0
            };

            newDevice.Titles.Add("New Device"); // Adds text safely to the JSON list!

            _currentState.Devices.Add(newDevice);
            if (GlobalViewModel != null) if (_currentState != null) _currentState.HasUnsavedChanges = true;
            DrawMap(_currentState);
        }


        private void EnforceBounds(FrameworkElement element, ref double newLeft, ref double newTop)
        {
            // Floor constraint (Top/Left)
            if (newLeft < 0) newLeft = 0;
            if (newTop < 0) newTop = 0;

            // Ceiling constraint (Bottom/Right)
            if (DrawingCanvas.ActualWidth > 0 && newLeft > DrawingCanvas.ActualWidth - element.ActualWidth)
                newLeft = DrawingCanvas.ActualWidth;

            if (DrawingCanvas.ActualHeight > 0 && newTop > DrawingCanvas.ActualHeight - element.ActualHeight)
                newTop = DrawingCanvas.ActualHeight;
        }


        private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_currentState == null) return;

            // --- EDIT LOGIC (F2) ---
            if (e.Key == Key.F2 && _selectedElements.Count == 1)
            {
                var el = _selectedElements[0];

                if (el.Tag is NetworkDevice d)
                {
                    bool isEditeMode = true;
                    if (!_currentState.IsEditingEnabled) isEditeMode = false;
                    var dlg = new DevicePropertiesWindow(d, isEditeMode) { Owner = Window.GetWindow(this) };
                    if (dlg.ShowDialog() == true)
                    {
                        _currentState?.HasUnsavedChanges = true;
                        DrawMap(_currentState);
                    }
                }
                else if (el.Tag is NetworkLabel l)
                {
                    var dlg = new LabelPropertiesWindow(l, true) { Owner = Window.GetWindow(this) };
                    if (dlg.ShowDialog() == true)
                    {
                        _currentState?.HasUnsavedChanges = true;
                        DrawMap(_currentState);
                    }
                }

                e.Handled = true;
                return;
            }

            // 1. DELETION LOGIC
            if (e.Key == Key.Delete)
            {
                var result = MessageBox.Show($"Delete {_selectedElements.Count} selected item(s)?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    var repo = new Data.MapRepository();
                    foreach (var el in _selectedElements)
                    {
                        if (el.Tag is NetworkDevice d)
                        {
                            _currentState?.Devices.Remove(d);
                            if (d.DeviceId > 0) repo.DeleteDevice(d.DeviceId);
                        }
                        else if (el.Tag is NetworkLabel l)
                        {
                            _currentState?.Labels.Remove(l);
                            if (l.LabelId > 0) repo.DeleteLabel(l.LabelId);
                        }
                    }
                    _selectedElements.Clear();
                    if (_currentState != null) _currentState.HasUnsavedChanges = true;
                    DrawMap(_currentState);
                }
                e.Handled = true;
                return;
            }
            bool isCtrlDown = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

            // --- FIND AND REPLACE LOGIC (Ctrl + H) ---
            if (isCtrlDown && e.Key == Key.H)
            {
                ExecuteFindAndReplace();
                e.Handled = true;
                return;
            }


            // --- COPY LOGIC (Ctrl + C) ---
            if (isCtrlDown && e.Key == Key.C)
            {
                _copiedDevices.Clear();
                _copiedLabels.Clear();
                _pasteOffsetMultiplier = 1; // Reset offset

                foreach (var el in _selectedElements)
                {
                    if (el.Tag is NetworkDevice d) _copiedDevices.Add(d);
                    else if (el.Tag is NetworkLabel l) _copiedLabels.Add(l);
                }
                e.Handled = true;
                return;
            }

            // --- PASTE LOGIC (Ctrl + V / Ctrl + Shift + V) ---
            if (isCtrlDown && e.Key == Key.V && _currentState != null)
            {
                // 1. Detect if Shift is held down for "Paste in Place"
                bool isShiftDown = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

                // 2. If Shift is down, offset is 0. Otherwise, calculate normal offset.
                double offset = isShiftDown ? 0 : 30 * _pasteOffsetMultiplier;

                SelectElement(null, false); // Clear current selection

                if (_copiedDevices.Count == 0 && _copiedLabels.Count == 0)
                    return;

                // Paste Devices
                foreach (var d in _copiedDevices)
                {
                    var newDev = new NetworkDevice
                    {
                        MapId = _currentState.MapId,
                        GroupId = d.GroupId,
                        TargetMapId = d.TargetMapId,
                        Address = d.Address,
                        Left = d.Left + offset,
                        Top = d.Top + offset,
                        HintImagePath = d.HintImagePath
                    };
                    foreach (var t in d.Titles) newDev.Titles.Add(t);
                    foreach (var h in d.Hints) newDev.Hints.Add(h);

                    _currentState.Devices.Add(newDev);
                }

                // Paste Labels
                foreach (var l in _copiedLabels)
                {
                    var newLab = new NetworkLabel
                    {
                        MapId = _currentState.MapId,
                        Left = l.Left + offset,
                        Top = l.Top + offset,
                        Width = l.Width,
                        Height = l.Height,
                        FontSize = l.FontSize,
                        FontFamily = l.FontFamily,
                        FontWeight = l.FontWeight,
                        FontStyle = l.FontStyle,
                        Background = l.Background,
                        BorderBrush = l.BorderBrush,
                        Foreground = l.Foreground,
                        HorizontalAlignment = l.HorizontalAlignment,
                        VerticalAlignment = l.VerticalAlignment
                    };
                    foreach (var t in l.TextLines) newLab.TextLines.Add(t);

                    _currentState.Labels.Add(newLab);
                }

                // 3. Only increment the offset multiplier if it was a normal paste!
                if (!isShiftDown)
                {
                    _pasteOffsetMultiplier++;
                }

                if (_currentState != null) _currentState.HasUnsavedChanges = true;
                DrawMap(_currentState);

                e.Handled = true;
                return;
            }

            // 2. ARROW KEY MOVEMENT LOGIC
            double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10.0 : 1.0;
            double dx = 0, dy = 0;

            if (e.Key == Key.Left) dx = -step;
            else if (e.Key == Key.Right) dx = step;
            else if (e.Key == Key.Up) dy = -step;
            else if (e.Key == Key.Down) dy = step;

            if (dx != 0 || dy != 0)
            {
                foreach (var el in _selectedElements)
                {
                    double newLeft = Canvas.GetLeft(el) + dx;
                    double newTop = Canvas.GetTop(el) + dy;

                    EnforceBounds(el, ref newLeft, ref newTop); // Keeps keyboard movement on screen too!

                    Canvas.SetLeft(el, newLeft);
                    Canvas.SetTop(el, newTop);
                    UpdateModelPosition(el, newLeft, newTop);
                }
                if (_currentState != null) _currentState.HasUnsavedChanges = true;
                e.Handled = true; // Prevents the window from scrolling if it's inside a ScrollViewer
            }


            bool isAltDown = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);

            // --- ALIGNMENT SHORTCUTS (Alt + Keys) ---
            if (isAltDown && _selectedElements.Count > 1 && _currentState != null && _currentState.IsEditingEnabled)
            {
                // Because Alt is a System modifier, WPF puts the actual key inside e.SystemKey!
                Key actualKey = e.Key == Key.System ? e.SystemKey : e.Key;

                switch (actualKey)
                {
                    case Key.Up: Align.AlignSelectedElements(_selectedElements, GlobalViewModel, this, AlignMode.Top); e.Handled = true; return;
                    case Key.Down: Align.AlignSelectedElements(_selectedElements, GlobalViewModel, this, AlignMode.Bottom); e.Handled = true; return;
                    case Key.Left: Align.AlignSelectedElements(_selectedElements, GlobalViewModel, this, AlignMode.Left); e.Handled = true; return;
                    case Key.Right: Align.AlignSelectedElements(_selectedElements, GlobalViewModel, this, AlignMode.Right); e.Handled = true; return;
                    case Key.S: Align.AlignSelectedElements(_selectedElements, GlobalViewModel, this, AlignMode.Middle); e.Handled = true; return;
                    case Key.C: Align.AlignSelectedElements(_selectedElements, GlobalViewModel, this, AlignMode.Center); e.Handled = true; return;
                    case Key.A: AutoAlignSelectedPairs(); e.Handled = true; return;
                }
            }
        }


        private void AutoAlignSelectedPairs()
        {
            if (_currentState == null || !_currentState.IsEditingEnabled) return;

            var deviceElements = _selectedElements.Where(e => e.Tag is NetworkDevice).ToList();
            var labelElements = _selectedElements.Where(e => e.Tag is NetworkLabel).ToList();

            if (deviceElements.Count == 0 || labelElements.Count == 0) return;

            // 1. Force layout update so sizes are accurate
            foreach (var el in _selectedElements) el.UpdateLayout();

            // 2. Backup the original selection so we can restore it at the end
            var originalSelection = _selectedElements.ToList();

            // 3. Match devices and labels 1-to-1 using Center Distance
            var unassignedLabels = labelElements.ToList();
            var unassignedDevices = deviceElements.ToList();
            var pairings = new List<(FrameworkElement device, FrameworkElement label)>();

            while (unassignedLabels.Count > 0 && unassignedDevices.Count > 0)
            {
                double minDistance = double.MaxValue;
                FrameworkElement? bestDevice = null;
                FrameworkElement? bestLabel = null;

                foreach (var lbl in unassignedLabels)
                {
                    double lblCX = Canvas.GetLeft(lbl) + (lbl.ActualWidth / 2);
                    double lblCY = Canvas.GetTop(lbl) + (lbl.ActualHeight / 2);

                    foreach (var dev in unassignedDevices)
                    {
                        double devCX = Canvas.GetLeft(dev) + (dev.ActualWidth / 2);
                        double devCY = Canvas.GetTop(dev) + (dev.ActualHeight / 2);

                        // Strict vertical penalty so it prefers the label directly above/below it
                        double dist = Math.Pow((lblCX - devCX) * 10, 2) + Math.Pow(lblCY - devCY, 2);
                        if (dist < minDistance)
                        {
                            minDistance = dist;
                            bestDevice = dev;
                            bestLabel = lbl;
                        }
                    }
                }

                if (bestDevice != null && bestLabel != null)
                {
                    pairings.Add((bestDevice, bestLabel));
                    unassignedLabels.Remove(bestLabel);
                    unassignedDevices.Remove(bestDevice);
                }
            }

            // =======================================================
            // 4. USE YOUR EXISTING ALIGNMENT LOGIC!
            // =======================================================
            foreach (var pair in pairings)
            {
                // Temporarily isolate the selection to JUST this one pair
                _selectedElements.Clear();
                _selectedElements.Add(pair.device);
                _selectedElements.Add(pair.label);

                var devData = (NetworkDevice)pair.device.Tag;

                if (devData.GroupId == 9) // Phones
                {
                    Align.AlignSelectedElements(_selectedElements, GlobalViewModel, this, AlignMode.Top);
                    Align.AlignSelectedElements(_selectedElements, GlobalViewModel, this, AlignMode.Right);
                }
                else // Computers, Printers, etc.
                {
                    Align.AlignSelectedElements(_selectedElements, GlobalViewModel, this, AlignMode.Middle);
                    Align.AlignSelectedElements(_selectedElements, GlobalViewModel, this, AlignMode.Center);
                }
            }

            // 5. Restore the user's original massive selection
            _selectedElements.Clear();
            _selectedElements.AddRange(originalSelection);

            _currentState.HasUnsavedChanges = true;
            DrawMap(_currentState);
        }



        private void ExecuteFindAndReplace()
        {
            if (_currentState == null || !_currentState.IsEditingEnabled) return;

            var dlg = new FindReplaceWindow { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                string find = dlg.FindText;
                string replace = dlg.ReplaceText;
                int affectedDevices = 0;

                // Loop through EVERY device on the current map
                foreach (var device in _currentState.Devices)
                {
                    bool wasModified = false;

                    // 1. Check and Replace IP Address
                    if (!string.IsNullOrEmpty(device.Address) && device.Address.Contains(find))
                    {
                        device.Address = device.Address.Replace(find, replace);
                        wasModified = true;
                    }

                    // 2. Check and Replace Titles (The visible text on the map)
                    for (int i = 0; i < device.Titles.Count; i++)
                    {
                        if (device.Titles[i].Contains(find))
                        {
                            device.Titles[i] = device.Titles[i].Replace(find, replace);
                            wasModified = true;
                        }
                    }

                    // 3. Check and Replace Hints (The tooltip hover text)
                    for (int i = 0; i < device.Hints.Count; i++)
                    {
                        if (device.Hints[i].Contains(find))
                        {
                            device.Hints[i] = device.Hints[i].Replace(find, replace);
                            wasModified = true;
                        }
                    }

                    if (wasModified) affectedDevices++;
                }

                // If we changed anything, tell the system there are unsaved changes and redraw!
                if (affectedDevices > 0)
                {
                    _currentState.HasUnsavedChanges = true;
                    DrawMap(_currentState);
                    MessageBox.Show($"Successfully replaced '{find}' with '{replace}' in {affectedDevices} devices.", "Replace Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"No matches found for '{find}' on this map.", "No Results", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }



        public void GatherOutOfBoundsDevices()
        {
            // Define the safe boundaries (assuming MapCanvas is the name of your WPF Canvas)
            // We subtract 100 to account for the width/height of the device icon itself
            double maxWidth = DrawingCanvas.ActualWidth > 0 ? DrawingCanvas.ActualWidth : 1200;
            double maxHeight = DrawingCanvas.ActualHeight > 0 ? DrawingCanvas.ActualHeight : 800;

            bool mapRequiresRedraw = false;
            double cascadeOffset = 20.0; // Start at X:20, Y:20

            // Adjust '_currentState.Devices' to match whatever collection holds your network items
            foreach (var device in _currentState.Devices)
            {
                // Check if the device is outside the visible screen
                if (device.Left < 0 || device.Left > maxWidth || device.Top < 0 || device.Top > maxHeight)
                {
                    // Snap it back to a safe location
                    device.Left = cascadeOffset;
                    device.Top = cascadeOffset;

                    // Increment the offset so the next lost device doesn't perfectly hide under this one
                    cascadeOffset += 30.0;

                    // Prevent the offset from going off-screen again
                    if (cascadeOffset > 300) cascadeOffset = 20.0;

                    mapRequiresRedraw = true;
                }
            }

            if (mapRequiresRedraw)
            {
                if (GlobalViewModel != null)
                {
                    GlobalViewModel.HasUnsavedChanges = true;
                }

                DrawMap(_currentState);
            }
        }
    }
}