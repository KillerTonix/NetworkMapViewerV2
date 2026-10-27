using NetworkMapViewerV2.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NetworkMapViewerV2.Views
{
    // Part: Interaction
    // Click, double-click and drag handling for devices and labels,
    // plus position clamping and the view-mode double-click action.
    public partial class MapCanvasView : UserControl
    {
        // ─── SELECTION & DRAG LOGIC ──────────────────────────────
        /// <summary>
        /// Handles a click on a device or label. Double-click opens the properties window (edit mode) or runs
        /// the device action (view mode); a single click updates the selection and, in edit mode, prepares a
        /// batch drag.
        /// </summary>
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
                            _ = new DevicePropertiesWindow(dev, true) { Owner = Window.GetWindow(this) };
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

        /// <summary>
        /// Moves all selected elements together while dragging (Shift locks to one axis, positions are clamped
        /// to the canvas) and marks the tab as modified after a small threshold.
        /// </summary>
        private void Element_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && _draggedElement != null)
            {
                Point currentPosition = e.GetPosition(DrawingCanvas);

                // Calculate total movement distance from the very beginning of the click
                double totalDeltaX = currentPosition.X - _clickPosition.X;
                double totalDeltaY = currentPosition.Y - _clickPosition.Y;

                // ==========================================
                // THE FIX: ADD A DRAG THRESHOLD & UPDATE TAB STATE
                // ==========================================
                // Only trigger the Red Tab if they moved it more than 3 pixels!
                if (Math.Abs(totalDeltaX) > 3 || Math.Abs(totalDeltaY) > 3)
                {
                    _currentState?.HasUnsavedChanges = true;
                }

                // SHIFT-LOCK LOGIC
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    if (Math.Abs(totalDeltaX) > Math.Abs(totalDeltaY))
                        totalDeltaY = 0;
                    else
                        totalDeltaX = 0;
                }

                // --- BATCH MOVEMENT ---
                if (_selectedElements != null)
                {
                    foreach (var el in _selectedElements)
                    {
                        if (el is FrameworkElement fw && _dragStartPositions.TryGetValue(fw, out Point startPos))
                        {
                            double newLeft = startPos.X + totalDeltaX;
                            double newTop = startPos.Y + totalDeltaY;

                            EnforceBounds(fw, ref newLeft, ref newTop);

                            Canvas.SetLeft(fw, newLeft);
                            Canvas.SetTop(fw, newTop);

                            UpdateModelPosition(fw, newLeft, newTop);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Ends a drag. A click without movement on an already selected item narrows the selection to that item
        /// or unselects it.
        /// </summary>
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

        /// <summary>Writes a new Left / Top back to the underlying device or label model.</summary>
        internal static void UpdateModelPosition(FrameworkElement el, double? left, double? top)
        {
            if (el.Tag is NetworkDevice device) { if (left.HasValue) device.Left = left.Value; if (top.HasValue) device.Top = top.Value; }
            else if (el.Tag is NetworkLabel label) { if (left.HasValue) label.Left = left.Value; if (top.HasValue) label.Top = top.Value; }
        }

        /// <summary>
        /// View-mode double-click action: opens the linked map for Map Link devices, otherwise runs the group's
        /// default command (falls back to Ping). Returns true if an action was run.
        /// </summary>
        private static bool HandleDeviceDoubleClick(NetworkDevice device)
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

        /// <summary>Clamps a proposed position so the element stays inside the canvas.</summary>
        internal void EnforceBounds(FrameworkElement el, ref double newLeft, ref double newTop)
        {
            // 1. Lock the Top/Left edges at 0
            if (newLeft < 0) newLeft = 0;
            if (newTop < 0) newTop = 0;

            // 2. CRITICAL CHANGE: Use .Width and .Height, NOT ActualWidth!
            double canvasWidth = DrawingCanvas.Width + 30;
            double canvasHeight = DrawingCanvas.Height;

            if (double.IsNaN(canvasWidth) || canvasWidth <= 0 || double.IsNaN(el.ActualWidth))
                return;

            // 3. Math now calculates based on your giant 5000x5000 area
            double maxLeft = canvasWidth - el.ActualWidth;
            double maxTop = canvasHeight - el.ActualHeight;

            if (maxLeft < 0) maxLeft = 0;
            if (maxTop < 0) maxTop = 0;

            // 4. Lock the Right/Bottom edges
            if (newLeft > maxLeft) newLeft = maxLeft;
            if (newTop > maxTop) newTop = maxTop;
        }
    }
}
