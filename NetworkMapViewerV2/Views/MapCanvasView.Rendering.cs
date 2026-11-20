using NetworkMapViewerV2.Helpers;
using NetworkMapViewerV2.Helpers.Alignment;
using NetworkMapViewerV2.Models;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NetworkMapViewerV2.Views
{
    // Part: Rendering
    // Builds the canvas from the current MapTabState (labels, devices, tooltips)
    // and manages the canvas background (grid or plain).
    public partial class MapCanvasView : UserControl
    {
        private static readonly Dictionary<string, Size> _iconSizes = [];
        private static readonly Dictionary<string, BitmapImage?> _hintImageCache = [];

        /// <summary>Loads an image into memory and closes the file. Returns null if it's missing or the share is unreachable.</summary>
        private static BitmapImage? LoadFrozenBitmap(string path)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        /// <summary>
        /// Clears the canvas and fully redraws it for the given tab (labels first, then devices). Bindings are
        /// cleared first so WPF can release the old elements.
        /// </summary>
        internal void DrawMap(MapTabState? state)
        {
            // 1. DESTROY ALL BINDINGS SO WPF CAN DELETE THE OLD UI ELEMENTS
            foreach (UIElement child in DrawingCanvas.Children)
            {
                System.Windows.Data.BindingOperations.ClearAllBindings(child);

                // Because your Image is buried inside a Border -> StackPanel, we must clear those too!
                if (child is Border b && b.Child is StackPanel sp)
                {
                    foreach (UIElement spChild in sp.Children)
                    {
                        System.Windows.Data.BindingOperations.ClearAllBindings(spChild);
                    }
                }
            }

            // 2. Now it is safe to clear the canvas
            DrawingCanvas.Children.Clear();
            _selectedElements?.Clear();

            DrawLabels(state);
            DrawDevices(state);
        }

        // ─── DRAW LABELS ─────────────────────────────────────────

        /// <summary>
        /// Creates a bordered text block for each label with its colors, font and alignment, and attaches the
        /// context menu and mouse handlers.
        /// </summary>
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

        /// <summary>
        /// Creates the element for each device: an icon bound to its online status (sized from the group's
        /// icon), its title text, tooltip, context menu and mouse handlers.
        /// </summary>
        private void DrawDevices(MapTabState? state)
        {
            if (state == null) return;

            // 1. Fetch all groups ONCE before the loop so we don't spam the database
            var allGroups = new Data.MapRepository().GetDeviceGroupsCached().ToDictionary(g => g.GroupId);
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
                    if (!_iconSizes.TryGetValue(dbIconPath, out var iconSize))
                    {
                        var bmp = LoadFrozenBitmap(dbIconPath);
                        iconSize = bmp != null ? new Size(bmp.PixelWidth, bmp.PixelHeight) : new Size(40, 40);
                        _iconSizes[dbIconPath] = iconSize;
                    }
                    imgWidth = iconSize.Width;
                    imgHeight = iconSize.Height;

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


                var combinedTitles = new List<string>();
                foreach (var lbl in device.Titles)
                {
                    if (string.IsNullOrWhiteSpace(lbl)) continue;
                    combinedTitles.Add(lbl.Replace("%Address", device.Address));
                }

                if (combinedTitles.Count != 0)
                {
                    sp.Children.Add(new TextBlock
                    {
                        Text = string.Join("\n", combinedTitles), // One single TextBlock!
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

        // ─── TOOLTIPS & ALIGNMENT ────────────────────────────────

        /// <summary>
        /// Gives the device a hover tooltip (hint image + hint text). The content is built only when the mouse
        /// actually hovers, so drawing the map never touches the network share for hint images.
        /// </summary>
        private static void BuildDeviceTooltip(Border container, NetworkDevice device)
        {
            container.ToolTip = "";   // placeholder: WPF only raises ToolTipOpening when ToolTip is set
            container.ToolTipOpening += (s, e) =>
            {
                var panel = CreateTooltipContent(device);
                if (panel == null) { e.Handled = true; return; }   // nothing to show
                container.ToolTip = panel;
            };
        }

        private static StackPanel? CreateTooltipContent(NetworkDevice device)
        {
            var toolTipPanel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(5) };

            if (!string.IsNullOrWhiteSpace(device.HintImagePath))
            {
                if (!_hintImageCache.TryGetValue(device.HintImagePath, out var hintBmp))
                {
                    hintBmp = LoadFrozenBitmap(device.HintImagePath);
                    _hintImageCache[device.HintImagePath] = hintBmp;
                }
                if (hintBmp != null)
                    toolTipPanel.Children.Add(new Image { Source = hintBmp, MaxWidth = 100, MaxHeight = 100, Margin = new Thickness(1, 1, 1, 5) });
            }

            if (device.Hints.Count != 0)
            {
                var hintTextBlock = new TextBlock { Margin = new Thickness(0, 0, 0, 5) };
                RenderHtmlToTextBlock(hintTextBlock, string.Join("\n", device.Hints));
                toolTipPanel.Children.Add(hintTextBlock);
            }

            return toolTipPanel.Children.Count > 0 ? toolTipPanel : null;
        }

        /// <summary>
        /// Renders a small HTML subset (bold, italic, underline and line-break tags) into the inlines of a
        /// TextBlock.
        /// </summary>
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

        /// <summary>Lazily builds and caches the tiled grid brush used as the canvas background.</summary>
        private Brush GetGridBrush()
        {
            if (_gridBrush == null)
            {
                var brush = new DrawingBrush
                {
                    Viewport = new Rect(0, 0, 125, 130),
                    ViewportUnits = BrushMappingMode.Absolute,
                    TileMode = TileMode.Tile
                };

                var drawingGroup = new DrawingGroup();
                drawingGroup.Children.Add(new GeometryDrawing(_standardBrush, null, new RectangleGeometry(new Rect(0, 0, 125, 130))));

                var pen = new Pen(new BrushConverter().ConvertFrom("#191919") as Brush, 1);
                var lineGeometry = new GeometryGroup();
                lineGeometry.Children.Add(new LineGeometry(new Point(0, 0), new Point(125, 0)));
                lineGeometry.Children.Add(new LineGeometry(new Point(0, 0), new Point(0, 130)));

                drawingGroup.Children.Add(new GeometryDrawing(null, pen, lineGeometry));

                brush.Drawing = drawingGroup;
                brush.Freeze(); // Optimize for UI thread
                _gridBrush = brush;
            }
            return _gridBrush;
        }

        /// <summary>Shows the grid or the plain background depending on GlobalViewModel.IsGridVisible.</summary>
        private void UpdateBackground()
        {
            DrawingCanvas.Background = GlobalViewModel != null && GlobalViewModel.IsGridVisible ? GetGridBrush() : _standardBrush;
        }
    }
}
