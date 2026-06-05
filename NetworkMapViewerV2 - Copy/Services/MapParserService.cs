/*using NetworkMapViewerV2.Models;
using System.IO;

namespace NetworkMapViewerV2.Services
{
    public static class MapParserService
    {
        public static MapTabState ParseMapFile(string filePath)
        {
            var state = new MapTabState
            {
                FilePath = filePath,
                MapName = Path.GetFileNameWithoutExtension(filePath)
            };

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Could not find the map file: {filePath}");

            var lines = File.ReadAllLines(filePath);

            NetworkDevice currentDevice = null;
            NetworkLabel currentLabel = null;
            string currentSubSection = "";

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                // Handle Sections and Sub-Sections
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    currentSubSection = "";
                    if (line.StartsWith("[Device"))
                    {
                        currentDevice = new NetworkDevice();
                        state.Devices.Add(currentDevice);
                        currentLabel = null;
                    }
                    else if (line.StartsWith("[Label"))
                    {
                        currentLabel = new NetworkLabel();
                        state.Labels.Add(currentLabel);
                        currentDevice = null;
                    }
                    else if (line == "[Name]" || line == "[Hint]" || line == "[Text]")
                    {
                        currentSubSection = line.Trim('[', ']');
                    }
                    else
                    {
                        currentDevice = null;
                        currentLabel = null;
                    }
                    continue;
                }

                // Parse Key=Value pairs
                var parts = line.Split(new[] { '=' }, 2);
                if (parts.Length == 2)
                {
                    string key = parts[0].Trim();
                    string value = parts[1].Trim();

                    // --- DEVICE PARSING ---
                    if (currentDevice != null)
                    {
                        if (currentSubSection == "")
                        {
                            if (key == "Left") currentDevice.Left = double.Parse(value);
                            else if (key == "Top") currentDevice.Top = double.Parse(value);
                            else if (key == "Address") currentDevice.Address = value;
                            else if (key == "Name") currentDevice.Name = value;
                            else if (key == "MapFile") currentDevice.MapFile = value;
                            else if (key == "Image") currentDevice.ImagePath = value;
                            else if (key == "Group" && int.TryParse(value, out int groupVal)) currentDevice.Group = groupVal;
                            else if (key == "Hint") currentDevice.Hints.Add(CleanHtml(value));
                            // Note: We skip 'ID=' here because V2 uses an auto-generated SQLite ID instead
                        }
                        else if (currentSubSection == "Name" && key.StartsWith("Item"))
                        {
                            currentDevice.Labels.Add(value.Replace("%Address", currentDevice.Address ?? ""));
                        }
                        else if (currentSubSection == "Hint" && key.StartsWith("Item"))
                        {
                            currentDevice.Hints.Add(CleanHtml(value));
                        }
                    }
                    // --- LABEL PARSING ---
                    else if (currentLabel != null)
                    {
                        if (currentSubSection == "")
                        {
                            if (key == "Left") currentLabel.Left = double.Parse(value);
                            else if (key == "Top") currentLabel.Top = double.Parse(value);
                            else if (key == "Width") currentLabel.Width = double.Parse(value);
                            else if (key == "Height") currentLabel.Height = double.Parse(value);
                            else if (key == "Text") currentLabel.TextLines.Add(value);
                            else if (key == "BrushColor") currentLabel.BrushColor = value;
                            else if (key == "FrameColor") currentLabel.FrameColor = value;
                            else if (key == "HAlign" && int.TryParse(value, out int hVal)) currentLabel.HAlign = hVal;
                            else if (key == "VAlign" && int.TryParse(value, out int vVal)) currentLabel.VAlign = vVal;
                            else if (key == "Font")
                            {
                                var fParts = value.Split(',');

                                if (fParts.Length > 0 && !string.IsNullOrWhiteSpace(fParts[0]))
                                    currentLabel.FontFamily = fParts[0].Trim();

                                if (fParts.Length > 1 && double.TryParse(fParts[1].Trim(), out double fSize))
                                    currentLabel.FontSize = fSize;

                                if (fParts.Length > 2)
                                {
                                    string fStyle = fParts[2].Trim().ToUpper();
                                    currentLabel.IsBold = fStyle.Contains("B");
                                    currentLabel.IsItalic = fStyle.Contains("I");
                                    currentLabel.IsUnderline = fStyle.Contains("U");
                                }

                                if (fParts.Length > 4 && !string.IsNullOrWhiteSpace(fParts[4]))
                                    currentLabel.FontColor = fParts[4].Trim();
                            }
                        }
                        else if (currentSubSection == "Text" && key.StartsWith("Item"))
                        {
                            currentLabel.TextLines.Add(value);
                        }
                    }
                }
            }

            return state;
        }

        private static string CleanHtml(string input) => input.Replace("Ã", "");


        public static void ExportToMapFile(MapTabState mapState, string filePath)
        {
            // We use StreamWriter to write the file, overriding any existing file
            using var writer = new StreamWriter(filePath, false, System.Text.Encoding.UTF8);
            // ─── 1. EXPORT DEVICES ──────────────────────────────────────────────
            int countDevice = 0;
            writer.Write(@"[Appearance]
 BackGroundColor=cl3DDkShadow
 BackGroundFileName=
 TileBackGroundImage=1
 Indent=1
 NameFont=MS Sans Serif,8,B,0,clWhite,1
 InactiveColor=clSilver
 SelectedBevelColor=clYellow
");

            foreach (var device in mapState.Devices)
            {
                writer.WriteLine($"[Device{countDevice}]");
                if (!string.IsNullOrWhiteSpace(device.Name)) writer.WriteLine($" Name={device.Name}");
                writer.WriteLine($" Left={(int)device.Left}");
                writer.WriteLine($" Top={(int)device.Top}");
                writer.WriteLine($" Group={device.Group}");
                writer.WriteLine($" ID={countDevice + 1}");
                if (!string.IsNullOrWhiteSpace(device.Address)) writer.WriteLine($" Address={device.Address}");
                if (!string.IsNullOrWhiteSpace(device.ImagePath)) writer.WriteLine($" Image={device.ImagePath}");
                if (!string.IsNullOrWhiteSpace(device.MapFile)) writer.WriteLine($" MapFile={device.MapFile}");

                // Export Device Name Labels
                if (device.Labels != null && device.Labels.Count > 0)
                {
                    writer.WriteLine(" [Name]");
                    writer.WriteLine($"  Count={device.Labels.Count}");
                    writer.WriteLine($"  Item{0}=%Address");
                    for (int i = 1; i < device.Labels.Count; i++)
                    {
                        writer.WriteLine($"  Item{i}={device.Labels[i]}");
                    }
                }

                // Export Device Hints
                if (device.Hints != null && device.Hints.Count > 0)
                {
                    writer.WriteLine(" [Hint]");
                    writer.WriteLine($"  Count={device.Hints.Count}");
                    for (int i = 0; i < device.Hints.Count; i++)
                    {
                        writer.WriteLine($"  Item{i}={device.Hints[i]}");
                    }
                }
                countDevice++;
            }

            // ─── 2. EXPORT LABELS ───────────────────────────────────────────────
            int countLabel = 0;
            foreach (var label in mapState.Labels)
            {
                writer.WriteLine($"[Label{countLabel}]");
                writer.WriteLine($" Left={(int)label.Left}");
                writer.WriteLine($" Top={(int)label.Top}");
                writer.WriteLine($" Width={(int)label.Width}");
                writer.WriteLine($" Height={(int)label.Height}");
                writer.WriteLine($" AutoSize=0");
                writer.WriteLine($" ID={countLabel + 1}");

                if (!string.IsNullOrWhiteSpace(label.BrushColor)) writer.WriteLine($" BrushColor={label.BrushColor}");
                if (!string.IsNullOrWhiteSpace(label.FrameColor)) writer.WriteLine($" FrameColor={label.FrameColor}");
                if (label.HAlign.HasValue) writer.WriteLine($" HAlign={label.HAlign.Value}");
                if (label.VAlign.HasValue) writer.WriteLine($" VAlign={label.VAlign.Value}");

                // Reconstruct the complicated 5-part Font string
                string fontStyle = "";
                if (label.IsBold) fontStyle += "B";
                if (label.IsItalic) fontStyle += "I";
                if (label.IsUnderline) fontStyle += "U";
                if (string.IsNullOrEmpty(fontStyle)) fontStyle = " "; // Needs space if empty

                string fontFamily = string.IsNullOrEmpty(label.FontFamily) ? "Segoe UI" : label.FontFamily;
                double fontSize = label.FontSize > 0 ? label.FontSize : 12;
                string fontColor = string.IsNullOrEmpty(label.FontColor) ? "#000000" : label.FontColor;

                writer.WriteLine($" Font={fontFamily},{fontSize},{fontStyle}, ,{fontColor}");

                // Export Label Text Lines
                if (label.TextLines != null && label.TextLines.Count > 0)
                {
                    writer.WriteLine(" [Text]");
                    writer.WriteLine($"  Count={label.TextLines.Count}");

                    for (int i = 0; i < label.TextLines.Count; i++)
                    {
                        writer.WriteLine($"  Item{i}={label.TextLines[i]}");
                    }
                }
                countLabel++;
            }
        }
    }
}*/