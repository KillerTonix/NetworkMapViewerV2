using NetworkMapViewerV2.Helpers.Alignment;
using NetworkMapViewerV2.Models;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using static NetworkMapViewerV2.Helpers.Alignment.Align;

namespace NetworkMapViewerV2.Views
{
    // Part: Tools
    // Editing helpers: auto-align device / label pairs and find and replace.
    public partial class MapCanvasView : UserControl
    {

        /// <summary>
        /// Pairs each selected device with its nearest selected label and aligns each pair (phones: top and
        /// right, others: middle and center), then restores the original selection.
        /// </summary>
        internal void AutoAlignSelectedPairs()
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
            foreach (var (device, label) in pairings)
            {
                // Temporarily isolate the selection to JUST this one pair
                _selectedElements.Clear();
                _selectedElements.Add(device);
                _selectedElements.Add(label);

                var devData = (NetworkDevice)device.Tag;

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

        /// <summary>
        /// Replaces text in device addresses, titles and hints across the current map, redraws, and reports how
        /// many devices changed.
        /// </summary>
        internal void ExecuteFindAndReplace()
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
        
    }
}
