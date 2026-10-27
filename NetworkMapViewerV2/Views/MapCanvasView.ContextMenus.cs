using NetworkMapViewerV2.Helpers;
using NetworkMapViewerV2.Helpers.Alignment;
using NetworkMapViewerV2.Helpers.LocalFetcher;
using NetworkMapViewerV2.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static NetworkMapViewerV2.Helpers.Alignment.Align;

namespace NetworkMapViewerV2.Views
{
    public partial class MapCanvasView : UserControl
    {
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


                menu.Items.Add(new Separator());
                var miCopy = new MenuItem { Icon = "🗐", Header = "Copy IPAddress" };
                miCopy.Click += (sender, args) =>
                {
                    Clipboard.SetText(device.Address.ToString());
                };
                menu.Items.Add(miCopy);


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
                    var miAutoFill = new MenuItem { Icon = "🔍", Header = "Auto-Fill Specs" };

                    var miDomain = new MenuItem { Icon = "💻", Header = "Domain Joined PC" };
                    miDomain.Click += async (sender, args) => await AutoFill.RunAutoFillScript(GlobalViewModel, device, "SystemInfo.ps1");

                    var miNonDomain = new MenuItem { Icon = "🖥️", Header = "Non-Domain PC" };
                    miNonDomain.Click += async (sender, args) => await AutoFill.RunAutoFillScript(GlobalViewModel, device, $"SystemInfo Non Domain.ps1");

                    var miDefaultPC = new MenuItem { Icon = "🖥️", Header = "Default PC" };
                    miDefaultPC.Click += async (sender, args) => await AutoFill.RunAutoFillScript(GlobalViewModel, device, $"SystemInfo Default.ps1");

                    var miLinux = new MenuItem { Icon = "🐧", Header = "Linux PC (SSH)" };
                    miLinux.Click += async (sender, args) => await AutoFill.RunAutoFillScript(GlobalViewModel, device, $"SystemInfo Linux.ps1");

                    var miPrinter = new MenuItem { Icon = "🖨️", Header = "Printer" };
                    miPrinter.Click += async (sender, args) => await AutoFill.RunPrinterAutoFill(GlobalViewModel, device);

                    var miPhone = new MenuItem { Icon = "☎️", Header = "Grandstream" };
                    miPhone.Click += async (sender, args) => await AutoFill.RunGrandstreamAutoFill(GlobalViewModel, device);

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
                            var result = MessageBox.Show("Are you sure you want to delete this item?", "Delete", MessageBoxButton.YesNo);
                            if (result == MessageBoxResult.Yes)
                            {
                                _currentState.Devices.Remove(device);
                                _currentState?.HasUnsavedChanges = true;
                                DrawMap(_currentState);
                            }
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
                        _currentState?.HasUnsavedChanges = true;
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
                        _currentState?.HasUnsavedChanges = true;
                        DrawMap(_currentState);
                    }
                };
                contextMenu.Items.Add(miDelete);

                container.ContextMenu = contextMenu;
            };
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

            var miGetLastPC = new MenuItem { Icon = "🔍", Header = "Get Newly Added Computers...", FontWeight = FontWeights.Bold };
            miGetLastPC.Click += async (s, args) => await GetNewlyAddedComputers();

            var miDiscover = new MenuItem { Icon = "🔍", Header = "Auto-Discover Devices...", FontWeight = FontWeights.Bold };
            miDiscover.Click += async (s, args) => await RunAutoDiscoveryAsync();

            var miUpdateDevices = new MenuItem
            {
                Icon = "🔄",
                Header = "Update All Devices...",
                Command = GlobalViewModel.UpdateGroupDataCommand
            };

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
                    if (GlobalViewModel != null) _currentState?.HasUnsavedChanges = true;
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

            if (_selectedElements != null)
            {
                if (_selectedElements.Any(e => e.Tag is NetworkDevice) && _selectedElements.Any(e => e.Tag is NetworkLabel))
                {
                    var miAutoAlign = new MenuItem { Icon = "✨", Header = "Auto-Align Devices (Alt+A)" };
                    miAutoAlign.Click += (sender, args) => AutoAlignSelectedPairs();
                    menu.Items.Add(miAutoAlign);
                }
            }    

            if (string.Equals(_currentState.MapType, "Branch", StringComparison.OrdinalIgnoreCase))
            {
                menu.Items.Add(miDiscover);
                menu.Items.Add(new Separator());
            }
            
            menu.Items.Add(miGetLastPC);
            menu.Items.Add(new Separator());
            menu.Items.Add(miUpdateDevices);
            menu.Items.Add(new Separator());
            menu.Items.Add(miAddDevice);
            menu.Items.Add(miBatchAdd);
            menu.Items.Add(new Separator());
            menu.Items.Add(miAddLabel);
            menu.Items.Add(miBatchLabels);
            menu.Items.Add(new Separator());
            menu.Items.Add(miAdvancedGrid);

            DrawingCanvas.ContextMenu = menu;
        }
    }
}
