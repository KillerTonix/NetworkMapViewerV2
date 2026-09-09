using NetworkMapViewerV2.Helpers.Alignment;
using NetworkMapViewerV2.Models;
using NetworkMapViewerV2.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static NetworkMapViewerV2.Helpers.Alignment.Align;

namespace NetworkMapViewerV2.Services
{
    public static class MapKeyboardService
    {
        public static void HandlePreviewKeyDown(KeyEventArgs e, MapCanvasView view)
        {
            if (view._currentState == null) return;

            bool isCtrlDown = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            bool isShiftDown = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            bool isAltDown = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);

            // --- UNDO LOGIC (Ctrl + Z) ---
            if (isCtrlDown && e.Key == Key.Z && view._currentState.IsEditingEnabled)
            {
                if (view._undoStack.Count > 0)
                {
                    var undoAction = view._undoStack.Pop();
                    undoAction.Invoke();

                    view._selectedElements.Clear();
                    view._currentState.HasUnsavedChanges = true;
                    view.DrawMap(view._currentState);
                }
                e.Handled = true;
                return;
            }

            // --- EDIT LOGIC (F2) ---
            if (e.Key == Key.F2 && view._selectedElements.Count == 1)
            {
                var el = view._selectedElements[0];

                if (el.Tag is NetworkDevice d)
                {
                    bool isEditeMode = view._currentState.IsEditingEnabled;
                    var dlg = new DevicePropertiesWindow(d, isEditeMode) { Owner = Window.GetWindow(view) };
                    if (dlg.ShowDialog() == true)
                    {
                        view._currentState.HasUnsavedChanges = true;
                        view.DrawMap(view._currentState);
                    }
                }
                else if (el.Tag is NetworkLabel l)
                {
                    var dlg = new LabelPropertiesWindow(l, true) { Owner = Window.GetWindow(view) };
                    if (dlg.ShowDialog() == true)
                    {
                        view._currentState.HasUnsavedChanges = true;
                        view.DrawMap(view._currentState);
                    }
                }

                e.Handled = true;
                return;
            }

            // --- DELETION LOGIC (Delete) ---
            if (e.Key == Key.Delete && view._currentState.IsEditingEnabled && view._selectedElements.Count > 0)
            {
                var result = MessageBox.Show($"Delete {view._selectedElements.Count} selected item(s)?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    var deletedDevices = new List<NetworkDevice>();
                    var deletedLabels = new List<NetworkLabel>();

                    foreach (var el in view._selectedElements)
                    {
                        if (el.Tag is NetworkDevice d)
                        {
                            if (d.DeviceId > 0) view._currentState.PendingDeletedDeviceIds.Add(d.DeviceId);
                            deletedDevices.Add(d);
                            view._currentState.Devices.Remove(d);
                        }
                        else if (el.Tag is NetworkLabel l)
                        {
                            if (l.LabelId > 0) view._currentState.PendingDeletedLabelIds.Add(l.LabelId);
                            deletedLabels.Add(l);
                            view._currentState.Labels.Remove(l);
                        }
                    }

                    if (deletedDevices.Count > 0 || deletedLabels.Count > 0)
                    {
                        view._undoStack.Push(() =>
                        {
                            // UNDO: Put them back on screen, and remove them from the pending delete list!
                            // (Notice we NO LONGER set d.DeviceId = 0 here!)
                            foreach (var d in deletedDevices)
                            {
                                view._currentState.Devices.Add(d);
                                if (d.DeviceId > 0) view._currentState.PendingDeletedDeviceIds.Remove(d.DeviceId);
                            }
                            foreach (var l in deletedLabels)
                            {
                                view._currentState.Labels.Add(l);
                                if (l.LabelId > 0) view._currentState.PendingDeletedLabelIds.Remove(l.LabelId);
                            }
                        });
                        view._currentState.HasUnsavedChanges = true;
                    }

                    view._selectedElements.Clear();
                    view.DrawMap(view._currentState);
                }
                e.Handled = true;
                return;
            }

            // --- FIND AND REPLACE LOGIC (Ctrl + H) ---
            if (isCtrlDown && e.Key == Key.H && view._currentState.IsEditingEnabled)
            {
                view.ExecuteFindAndReplace();
                e.Handled = true;
                return;
            }

            // --- COPY LOGIC (Ctrl + C) ---
            if (isCtrlDown && e.Key == Key.C && view._currentState.IsEditingEnabled)
            {
                MapCanvasView._copiedDevices.Clear();
                MapCanvasView._copiedLabels.Clear();
                MapCanvasView._pasteOffsetMultiplier = 1;

                foreach (var el in view._selectedElements)
                {
                    if (el.Tag is NetworkDevice d) MapCanvasView._copiedDevices.Add(d);
                    else if (el.Tag is NetworkLabel l) MapCanvasView._copiedLabels.Add(l);
                }
                e.Handled = true;
                return;
            }

            // --- CUT LOGIC (Ctrl + X) ---
            if (isCtrlDown && e.Key == Key.X && view._currentState.IsEditingEnabled)
            {
                MapCanvasView._copiedDevices.Clear();
                MapCanvasView._copiedLabels.Clear();
                MapCanvasView._pasteOffsetMultiplier = 1;

                var cutDevices = new List<NetworkDevice>();
                var cutLabels = new List<NetworkLabel>();

                foreach (var el in view._selectedElements)
                {
                    if (el.Tag is NetworkDevice d)
                    {
                        MapCanvasView._copiedDevices.Add(d);
                        cutDevices.Add(d);
                        view._currentState.Devices.Remove(d);
                        if (d.DeviceId > 0) view._currentState.PendingDeletedDeviceIds.Add(d.DeviceId);
                    }
                    else if (el.Tag is NetworkLabel l)
                    {
                        MapCanvasView._copiedLabels.Add(l);
                        cutLabels.Add(l);
                        view._currentState.Labels.Remove(l);
                        if (l.LabelId > 0) view._currentState.PendingDeletedLabelIds.Add(l.LabelId);
                    }
                }

                view._undoStack.Push(() =>
                {
                    // UNDO: Restore from Cut
                    foreach (var d in cutDevices)
                    {
                        view._currentState.Devices.Add(d);
                        if (d.DeviceId > 0) view._currentState.PendingDeletedDeviceIds.Remove(d.DeviceId);
                    }
                    foreach (var l in cutLabels)
                    {
                        view._currentState.Labels.Add(l);
                        if (l.LabelId > 0) view._currentState.PendingDeletedLabelIds.Remove(l.LabelId);
                    }
                });

                view._selectedElements.Clear();
                view._currentState.HasUnsavedChanges = true;
                view.DrawMap(view._currentState);
                e.Handled = true;
                return;
            }

            // --- PASTE LOGIC (Ctrl + V / Ctrl + Shift + V) ---
            if (isCtrlDown && e.Key == Key.V && view._currentState.IsEditingEnabled)
            {
                double offset = isShiftDown ? 0 : 30 * MapCanvasView._pasteOffsetMultiplier;
                view.SelectElement(null, false);

                if (MapCanvasView._copiedDevices.Count == 0 && MapCanvasView._copiedLabels.Count == 0) return;

                var newlyPastedDevices = new List<NetworkDevice>();
                var newlyPastedLabels = new List<NetworkLabel>();

                foreach (var d in MapCanvasView._copiedDevices)
                {
                    var newDev = new NetworkDevice
                    {
                        MapId = view._currentState.MapId,
                        GroupId = d.GroupId,
                        TargetMapId = d.TargetMapId,
                        Address = d.Address,
                        Left = d.Left + offset,
                        Top = d.Top + offset,
                        HintImagePath = d.HintImagePath
                    };
                    foreach (var t in d.Titles) newDev.Titles.Add(t);
                    foreach (var h in d.Hints) newDev.Hints.Add(h);

                    view._currentState.Devices.Add(newDev);
                    newlyPastedDevices.Add(newDev);
                }

                foreach (var l in MapCanvasView._copiedLabels)
                {
                    var newLab = new NetworkLabel
                    {
                        MapId = view._currentState.MapId,
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

                    view._currentState.Labels.Add(newLab);
                    newlyPastedLabels.Add(newLab);
                }

                view._undoStack.Push(() =>
                {
                    // UNDO PASTE: Remove the pasted items from the screen.
                    // If they somehow got saved and got an ID, we queue them for deletion.
                    foreach (var d in newlyPastedDevices)
                    {
                        view._currentState.Devices.Remove(d);
                        if (d.DeviceId > 0) view._currentState.PendingDeletedDeviceIds.Add(d.DeviceId);
                    }
                    foreach (var l in newlyPastedLabels)
                    {
                        view._currentState.Labels.Remove(l);
                        if (l.LabelId > 0) view._currentState.PendingDeletedLabelIds.Add(l.LabelId);
                    }
                });

                if (!isShiftDown) MapCanvasView._pasteOffsetMultiplier++;

                view._currentState.HasUnsavedChanges = true;
                view.DrawMap(view._currentState);

                foreach (FrameworkElement child in view.DrawingCanvas.Children)
                {
                    if (child.Tag != null && (newlyPastedDevices.Contains(child.Tag) || newlyPastedLabels.Contains(child.Tag)))
                    {
                        view.SelectElement(child, true);
                    }
                }
                e.Handled = true;
                return;
            }

            if (view._currentState.IsEditingEnabled)
            {
                // --- ARROW KEY MOVEMENT LOGIC ---
                double step = isShiftDown ? 10.0 : 1.0;
                double dx = 0, dy = 0;

                if (e.Key == Key.Left) dx = -step;
                else if (e.Key == Key.Right) dx = step;
                else if (e.Key == Key.Up) dy = -step;
                else if (e.Key == Key.Down) dy = step;

                if (dx != 0 || dy != 0)
                {
                    var moveHistory = new List<Tuple<object, double, double>>();

                    foreach (var el in view._selectedElements)
                    {
                        double oldLeft = Canvas.GetLeft(el);
                        double oldTop = Canvas.GetTop(el);

                        moveHistory.Add(new Tuple<object, double, double>(el.Tag, oldLeft, oldTop));

                        double newLeft = oldLeft + dx;
                        double newTop = oldTop + dy;

                        view.EnforceBounds(el, ref newLeft, ref newTop);

                        Canvas.SetLeft(el, newLeft);
                        Canvas.SetTop(el, newTop);
                        MapCanvasView.UpdateModelPosition(el, newLeft, newTop);
                    }

                    view._undoStack.Push(() =>
                    {
                        foreach (var historyItem in moveHistory)
                        {
                            if (historyItem.Item1 is NetworkDevice d) { d.Left = historyItem.Item2; d.Top = historyItem.Item3; }
                            if (historyItem.Item1 is NetworkLabel l) { l.Left = historyItem.Item2; l.Top = historyItem.Item3; }
                        }
                    });

                    view._currentState.HasUnsavedChanges = true;
                    e.Handled = true;
                }
            }

            // --- ALIGNMENT SHORTCUTS (Alt + Keys) ---
            if (isAltDown && view._selectedElements.Count > 1 && view._currentState.IsEditingEnabled)
            {
                Key actualKey = e.Key == Key.System ? e.SystemKey : e.Key;

                switch (actualKey)
                {
                    case Key.Up: Align.AlignSelectedElements(view._selectedElements, MapCanvasView.GlobalViewModel, view, AlignMode.Top); e.Handled = true; return;
                    case Key.Down: Align.AlignSelectedElements(view._selectedElements, MapCanvasView.GlobalViewModel, view, AlignMode.Bottom); e.Handled = true; return;
                    case Key.Left: Align.AlignSelectedElements(view._selectedElements, MapCanvasView.GlobalViewModel, view, AlignMode.Left); e.Handled = true; return;
                    case Key.Right: Align.AlignSelectedElements(view._selectedElements, MapCanvasView.GlobalViewModel, view, AlignMode.Right); e.Handled = true; return;
                    case Key.S: Align.AlignSelectedElements(view._selectedElements, MapCanvasView.GlobalViewModel, view, AlignMode.Middle); e.Handled = true; return;
                    case Key.C: Align.AlignSelectedElements(view._selectedElements, MapCanvasView.GlobalViewModel, view, AlignMode.Center); e.Handled = true; return;
                    case Key.A: view.AutoAlignSelectedPairs(); e.Handled = true; return;
                }
            }
        }
    }
}