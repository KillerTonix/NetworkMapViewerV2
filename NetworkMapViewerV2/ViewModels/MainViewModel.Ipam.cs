using NetworkMapViewerV2.Helpers;
using NetworkMapViewerV2.Models;
using System.Collections.ObjectModel;

namespace NetworkMapViewerV2.ViewModels
{
    public partial class MainViewModel
    {
        /// <summary>
        /// Used by the IPAM window's "Add to map" button: opens (or switches to) the map, turns edit mode on and puts a
        /// new device with this address on a free spot, exactly like a paste. Nothing is written to the database here.
        /// The tab is marked as having unsaved changes, so the normal Save (Ctrl+S) or the "save changes?" question
        /// when leaving edit mode decides whether the device is kept.
        /// </summary>
        public bool TryAddDeviceToMapInEditMode(int mapId, string address, string name, int groupId, out string error)
        {
            error = "";
            address = address.Trim();

            OpenMapFromDatabase(mapId); // switches to the tab if it is already open
            var tab = SelectedTab;
            if (tab == null || tab.MapId != mapId)
            {
                error = "Could not open the map.";
                return false;
            }

            // The map may already hold this IP in an edit that has not been saved yet (the IPAM list only knows saved devices).
            if (tab.Devices.Any(d => string.Equals((d.Address ?? "").Trim(), address, StringComparison.OrdinalIgnoreCase)))
            {
                error = $"{address} is already on map \"{tab.MapName}\" (not saved yet). Save the map or leave edit mode to confirm it.";
                return false;
            }

            var (left, top) = IpamHelper.FindFreeSlot(tab.Devices.Select(d => (d.Left, d.Top)));
            var device = new NetworkDevice
            {
                MapId = mapId,
                GroupId = groupId,
                Left = left,
                Top = top,
                Address = address,
                Titles = new ObservableCollection<string>(IpamHelper.BuildTitles([IpamHelper.AddressPlaceholder], name)),
                Hints = []
            };
            tab.Devices.Add(device);

            tab.IsEditingEnabled = true;
            IsEditingEnabled = true; // keep the toolbar button in step with the tab, like ToggleEditMode does
            tab.HasUnsavedChanges = true;

            tab.TriggerRedraw?.Invoke();
            SyncOpenTabDevices();
            return true;
        }
    }
}
