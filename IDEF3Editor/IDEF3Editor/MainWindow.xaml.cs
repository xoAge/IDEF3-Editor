using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Path = System.Windows.Shapes.Path;

namespace IDEF3Editor
{
    public partial class MainWindow : Window
    {
        // ─── Данные диаграммы ─────────────────────────────────────────
        private readonly List<Node> _nodes = new List<Node>();
        private readonly List<Link> _links = new List<Link>();

        // ─── Метаданные диаграммы ─────────────────────────────────────
        private string _diagramName = "Новая диаграмма";
        private string _diagramAuthor = Environment.UserName;
        private string _diagramDescription = "";
        private DateTime _diagramCreated = DateTime.Now;

        // ─── Недавние файлы ───────────────────────────────────────────
        private const int MaxRecentFiles = 6;
        private const string RecentFilesKey = "RecentFiles";
        private List<string> _recentFiles = new List<string>();

        // ─── Отмена / Повтор ──────────────────────────────────────────
        private UndoRedoManager _history = new UndoRedoManager();

        private bool _saveOrNot = false;
        // ─── Декомпозиция ─────────────────────────────────────────────
        // Плоский словарь всех декомпозиций: ключ = ID блока-хозяина
        private readonly Dictionary<string, DiagramLevel> _decompositions
            = new Dictionary<string, DiagramLevel>();

        // Стек навигации: каждая запись — состояние родительского уровня
        private readonly List<NavEntry> _navStack = new List<NavEntry>();

        private class NavEntry
        {
            public string BlockId { get; set; }
            public string BlockName { get; set; }
            public string Reference { get; set; } // ссылочное выражение (A1, A1.2 …)
            public List<Node> Nodes { get; set; }
            public List<Link> Links { get; set; }
            public UndoRedoManager History { get; set; }
            public double Zoom { get; set; }
            public double PanX { get; set; }
            public double PanY { get; set; }
        }
        private DiagramMediator _mediator;


        // ─── Выделение одного элемента ────────────────────────────────
        private Node _selectedNode;
        private Link _selectedLink;

        // ─── Множественное выделение ──────────────────────────────────
        private readonly List<Node> _selectedNodes = new List<Node>();
        // Исходные позиции узлов в начале группового перемещения
        private readonly Dictionary<Node, Point> _multiDragOrigins = new Dictionary<Node, Point>();
        private bool _isMultiDragging;

        // ─── Выделение областью (Rubber-band) ────────────────────────
        private bool _isRubberBanding;
        private Point _rubberBandStart;

        // ─── Перетаскивание ───────────────────────────────────────────
        private Point _mouseDownPoint;
        private double _originalX, _originalY;
        private bool _wasDragged;

        // ─── Отслеживание двойного клика ──────────────────────────────
        private DateTime _lastClickTime = DateTime.MinValue;
        private Node _lastClickedNode;
        private Link _lastClickedLink;

        // ─── Режим создания связи ─────────────────────────────────────
        private bool _isLinkMode;
        // После закрытия контекстного меню первый ЛКМ-клик не должен начинать перетаскивание
        private bool _ignoreNextDrag = false;


        // ─── Буфер обмена ─────────────────────────────────────────────
        private readonly List<Node> _clipboardNodes = new List<Node>();
        private readonly List<Link> _clipboardLinks = new List<Link>();
        private Node _linkSourceNode;

        // ─── Режим размещения элементов ───────────────────────────────
        // Одиночный: клик на кнопку → клик на холст → блок создан, режим завершён
        // Непрерывный (Ctrl + кнопка): каждый клик создаёт новый блок, Escape — выход
        private bool _isPlacementMode;
        private bool _isContinuousPlacement; // true = была зажата Ctrl при нажатии кнопки
        private string _placementType;          // "uob", "junction-and" и т.д.
        private bool _justPlaced;            // блокируем перетаскивание до отпускания кнопки мыши

        // ─── Изменение размера блока ──────────────────────────────────
        private bool _isResizing;
        private Node _resizeNode;
        private string _resizeDirection;
        private double _resizeStartX, _resizeStartY;
        private double _resizeStartWidth, _resizeStartHeight;

        // ─── Сетка ───────────────────────────────────────────────────
        private bool _showGrid = true;
        private bool _snapToGrid = true;
        private const double GridStep = 20;

        // ─── Панорамирование ─────────────────────────────────────────
        private bool _isPanning;
        private Point _panStartMouse;
        private double _panStartX, _panStartY;

        // ─── Масштаб ─────────────────────────────────────────────────
        private double _zoom = 1.0;
        // ZoomMin — динамический: нельзя уменьшить холст так, чтобы он стал меньше области просмотра.
        // Формула: viewport / canvas → при этом масштабе холст ровно заполняет экран.
        private double ZoomMin =>
            CanvasScroll.ViewportWidth > 0 && CanvasScroll.ViewportHeight > 0
                ? Math.Max(CanvasScroll.ViewportWidth / DrawingCanvas.Width,
                           CanvasScroll.ViewportHeight / DrawingCanvas.Height)
                : 0.1;
        private const double ZoomMax = 4.0;
        private const double ZoomStep = 0.15;

        // ═════════════════════════════════════════════════════════════
        // КОНСТРУКТОР
        // ═════════════════════════════════════════════════════════════

        public MainWindow()
        {
            InitializeComponent();
            Directory.CreateDirectory(DefaultDiagramDir);
            _mediator = new DiagramMediator(DrawingCanvas, _nodes, _links);
            _history.StackChanged += (_, __) => RefreshUndoRedoUI();
            _history.StackChanged += (_, __) => _saveOrNot = true;


            this.KeyDown += OnKeyDown;
            this.Loaded += (_, __) => { DrawGrid(); this.Focus(); UpdateTitleBar(); };

            // Кнопки панели инструментов
            AddUobButton.Click += (_, e) => EnterPlacementMode("uob", e);
            AddAndButton.Click += (_, e) => EnterPlacementMode("junction-and", e);
            AddAndSyncButton.Click += (_, e) => EnterPlacementMode("junction-and-sync", e);
            AddOrButton.Click += (_, e) => EnterPlacementMode("junction-or", e);
            AddOrSyncButton.Click += (_, e) => EnterPlacementMode("junction-or-sync", e);
            AddXorButton.Click += (_, e) => EnterPlacementMode("junction-xor", e);
            AddLinkButton.Click += ToggleLinkMode;
            DeleteButton.Click += (_, __) => ExecuteDelete();
            ToggleGridButton.Click += ToggleGrid;
            ToggleSnapButton.Click += ToggleSnap;
            SaveButton.Click += (_, __) => TrySave();
            LoadButton.Click += (_, __) => TryLoad();
            ExportPngButton.Click += (_, __) => ExportToPng();
            PropertiesButton.Click += (_, __) => ShowPropertiesDialog();
            ValidateButton.Click += (_, __) => RunValidation();
            ReferenceButton.Click += (_, __) => ShowReference();
            RecentButton.Click += RecentButton_Click;

            // Загружаем список недавних файлов из настроек приложения
            LoadRecentFiles();

            UndoButton.Click += (_, __) => Undo();
            RedoButton.Click += (_, __) => Redo();
            ZoomInButton.Click += (_, __) => SetZoom(_zoom + ZoomStep);
            ZoomOutButton.Click += (_, __) => SetZoom(_zoom - ZoomStep);
            ZoomResetButton.Click += (_, __) => SetZoom(1.0);
            HomeButton.Click += (_, __) => GoHome();
            BreadcrumbBackButton.Click += (_, __) => NavigateBack();

            // Горячие клавиши
            AddBinding(Key.Z, ModifierKeys.Control, () => Undo());
            AddBinding(Key.Y, ModifierKeys.Control, () => Redo());
            AddBinding(Key.S, ModifierKeys.Control, () => TrySave());
            AddBinding(Key.O, ModifierKeys.Control, () => TryLoad());
            AddBinding(Key.OemPlus, ModifierKeys.Control, () => SetZoom(_zoom + ZoomStep));
            AddBinding(Key.OemMinus, ModifierKeys.Control, () => SetZoom(_zoom - ZoomStep));
            AddBinding(Key.D0, ModifierKeys.Control, () => SetZoom(1.0));
            AddBinding(Key.R, ModifierKeys.Control, () => RunValidation());
            AddBinding(Key.F1, ModifierKeys.None, () => ShowReference());
            AddBinding(Key.Home, ModifierKeys.Control, () => GoHome());
            AddBinding(Key.C, ModifierKeys.Control, () => CopySelected());
            AddBinding(Key.V, ModifierKeys.Control, () => PasteFromClipboard());
        }

        private void AddBinding(Key key, ModifierKeys mod, Action action)
            => InputBindings.Add(new KeyBinding(new RelayCommand(_ => action()), key, mod));

        // ═════════════════════════════════════════════════════════════
        // УПРАВЛЕНИЕ ОКНОМ
        // ═════════════════════════════════════════════════════════════

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isManuallyMaximized)
            {
                DragMove();
            }
            else
            {
                // Запоминаем позицию курсора на экране до изменения размеров окна
                Point cursorOnScreen = PointToScreen(e.GetPosition(this));

                // Вычисляем относительную позицию курсора (0..1) по ширине развёрнутого окна
                double maxLeft = this.Left;
                double maxWidth = this.Width;
                double ratio = Math.Max(0, Math.Min(1,
                    (cursorOnScreen.X - maxLeft) / maxWidth));

                // Восстанавливаем прежние размеры
                double restoreWidth = _preMaxWidth;
                double restoreHeight = _preMaxHeight;
                this.Width = restoreWidth;
                this.Height = restoreHeight;

                // Размещаем окно так, чтобы курсор оказался на той же пропорциональной позиции
                this.Left = cursorOnScreen.X - restoreWidth * ratio;
                this.Top = cursorOnScreen.Y - 19;

                _isManuallyMaximized = false;
                MaximizeButton.Content = "□";

                DragMove();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private bool _isManuallyMaximized = false;
        private double _preMaxLeft, _preMaxTop, _preMaxWidth, _preMaxHeight;

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isManuallyMaximized)
            {
                // Сохраняем текущие размеры и позицию для восстановления
                _preMaxLeft = this.Left;
                _preMaxTop = this.Top;
                _preMaxWidth = this.Width;
                _preMaxHeight = this.Height;

                // Получаем рабочую область монитора, на котором находится окно
                var workArea = GetCurrentMonitorWorkArea();
                this.Left = workArea.Left;
                this.Top = workArea.Top;
                this.Width = workArea.Width;
                this.Height = workArea.Height;

                _isManuallyMaximized = true;
                MaximizeButton.Content = "❐";
            }
            else
            {
                // Восстанавливаем предыдущие размеры и позицию
                this.Left = _preMaxLeft;
                this.Top = _preMaxTop;
                this.Width = _preMaxWidth;
                this.Height = _preMaxHeight;

                _isManuallyMaximized = false;
                MaximizeButton.Content = "□";
            }
        }

        // Возвращает рабочую область (без панели задач) монитора,
        // на котором находится окно приложения. Использует Win32 interop.
        private System.Windows.Rect GetCurrentMonitorWorkArea()
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;

            // MONITOR_DEFAULTTONEAREST = 2 — ближайший монитор при частичном выходе за экран
            IntPtr hMonitor = MonitorFromWindow(hwnd, 2);

            var info = new MONITORINFO();
            info.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(info);
            GetMonitorInfo(hMonitor, ref info);

            // rcWork задан в физических пикселях — переводим в единицы WPF
            var source = System.Windows.PresentationSource.FromVisual(this);
            double dpiX = 1.0, dpiY = 1.0;
            if (source?.CompositionTarget != null)
            {
                dpiX = source.CompositionTarget.TransformFromDevice.M11;
                dpiY = source.CompositionTarget.TransformFromDevice.M22;
            }

            return new System.Windows.Rect(
                info.rcWork.left * dpiX,
                info.rcWork.top * dpiY,
                (info.rcWork.right - info.rcWork.left) * dpiX,
                (info.rcWork.bottom - info.rcWork.top) * dpiY);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [System.Runtime.InteropServices.StructLayout(
            System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [System.Runtime.InteropServices.StructLayout(
            System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void UpdateTitleBar()
        {
            if (_navStack.Count == 0)
            {
                // На корневом уровне просто показываем название диаграммы
                TitleDiagramName.Text = $"— {_diagramName}";
            }
            else
            {
                // Внутри декомпозиции: A0 Название › A1 Блок › A1.2 Блок
                string root = BuildLevelName("A0", _diagramName);
                string path = string.Join(" › ", _navStack.Select(e => e.Reference ?? e.BlockName));
                TitleDiagramName.Text = $"— {root} › {path}";
            }
        }

        // ═════════════════════════════════════════════════════════════
        // СВОЙСТВА ДИАГРАММЫ
        // ═════════════════════════════════════════════════════════════

        private void ShowPropertiesDialog()
        {
            var dlg = new DiagramPropertiesDialog(
                _diagramName, _diagramAuthor, _diagramDescription, _diagramCreated)
            { Owner = this };

            if (dlg.ShowDialog() == true)
            {
                _diagramName = dlg.DiagramName;
                _diagramAuthor = dlg.DiagramAuthor;
                _diagramDescription = dlg.DiagramDescription;
                _saveOrNot = true;
                UpdateTitleBar();
                SetStatus($"Свойства обновлены: «{_diagramName}»");
            }
        }

        // ═════════════════════════════════════════════════════════════
        // ПРОВЕРКА ДИАГРАММЫ
        // ═════════════════════════════════════════════════════════════

        private void RunValidation()
        {
            var issues = Idef3Validator.Validate(_nodes, _links);
            var dlg = new ValidationResultsDialog(issues) { Owner = this };

            // При клике на замечание, привязанное к блоку — прокручиваем к нему и выделяем
            dlg.NodeFocusRequested += nodeId =>
            {
                var node = _nodes.FirstOrDefault(n => n.Id == nodeId);
                if (node == null) return;
                SelectNode(node);
                CanvasPan.X = -(node.X * _zoom) + CanvasScroll.ViewportWidth / 2;
                CanvasPan.Y = -(node.Y * _zoom) + CanvasScroll.ViewportHeight / 2;
            };

            // Открываем немодально — пользователь может продолжать работать с диаграммой
            dlg.Show();
        }

        // ═════════════════════════════════════════════════════════════
        // СПРАВОЧНОЕ ОКНО
        // ═════════════════════════════════════════════════════════════

        private ReferenceWindow _referenceWindow;

        private void ShowReference()
        {
            if (_referenceWindow == null || !_referenceWindow.IsLoaded)
            {
                _referenceWindow = new ReferenceWindow { Owner = this };
                _referenceWindow.Show();
            }
            else
            {
                _referenceWindow.Activate();
            }
        }

        // ═════════════════════════════════════════════════════════════
        // НЕДАВНИЕ ФАЙЛЫ
        // ═════════════════════════════════════════════════════════════

        private void LoadRecentFiles()
        {
            try
            {
                string raw = System.Configuration.ConfigurationManager
                    .AppSettings[RecentFilesKey] ?? "";
                // Пути разделены «|», фильтруем несуществующие файлы
                _recentFiles = new List<string>(
                    raw.Split('|')
                       .Where(s => !string.IsNullOrWhiteSpace(s) && File.Exists(s)));
            }
            catch { _recentFiles = new List<string>(); }
        }

        private void SaveRecentFiles()
        {
            try
            {
                var cfg = System.Configuration.ConfigurationManager
                    .OpenExeConfiguration(System.Configuration.ConfigurationUserLevel.None);
                cfg.AppSettings.Settings.Remove(RecentFilesKey);
                cfg.AppSettings.Settings.Add(RecentFilesKey, string.Join("|", _recentFiles));
                cfg.Save();
                System.Configuration.ConfigurationManager.RefreshSection("appSettings");
            }
            catch { /* сохранение настроек — вспомогательная операция */ }
        }

        private void AddToRecentFiles(string path)
        {
            _recentFiles.Remove(path);          // убираем дубликат
            _recentFiles.Insert(0, path);        // новый файл — первым
            if (_recentFiles.Count > MaxRecentFiles)
                _recentFiles = _recentFiles.Take(MaxRecentFiles).ToList();
            SaveRecentFiles();
        }

        private void RecentButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();

            if (_recentFiles.Count == 0)
            {
                menu.Items.Add(new MenuItem { Header = "Нет недавних файлов", IsEnabled = false });
            }
            else
            {
                foreach (string path in _recentFiles)
                {
                    string p = path;
                    string name = System.IO.Path.GetFileName(p);
                    var item = new MenuItem { Header = name, ToolTip = p };
                    item.Click += (_, __) => TryLoadFile(p);
                    menu.Items.Add(item);
                }
                menu.Items.Add(new Separator());
                var clear = new MenuItem { Header = "Очистить список" };
                clear.Click += (_, __) => { _recentFiles.Clear(); SaveRecentFiles(); };
                menu.Items.Add(clear);
            }

            menu.PlacementTarget = RecentButton;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        // ═════════════════════════════════════════════════════════════
        // ЭКСПОРТ В PNG
        // ═════════════════════════════════════════════════════════════

        private void ExportToPng()
        {
            if (_nodes.Count == 0)
            { SetStatus("Нет элементов для экспорта."); return; }

            var dlg = new SaveFileDialog
            {
                Filter = "PNG изображение (*.png)|*.png|Все файлы (*.*)|*.*",
                DefaultExt = "png",
                FileName = _diagramName.Replace(" ", "_")
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                // Шаг 1 — ограничивающий прямоугольник всех блоков
                const double margin = 40;
                double minX = _nodes.Min(n => n.X) - margin;
                double minY = _nodes.Min(n => n.Y) - margin;
                double maxX = _nodes.Max(n => n.X + n.Width) + margin;
                double maxY = _nodes.Max(n => n.Y + n.Height) + margin;
                double diagW = maxX - minX;
                double diagH = maxY - minY;

                // Шаг 2 — внеэкранный Canvas точно под размер диаграммы
                var offscreen = new Canvas
                {
                    Width = diagW,
                    Height = diagH,
                    Background = Brushes.White
                };

                // Линии сетки (если включена)
                if (_showGrid)
                {
                    for (double gx = Math.Floor(minX / GridStep) * GridStep; gx <= maxX; gx += GridStep)
                        offscreen.Children.Add(new System.Windows.Shapes.Line
                        {
                            X1 = gx - minX,
                            Y1 = 0,
                            X2 = gx - minX,
                            Y2 = diagH,
                            Stroke = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                            StrokeThickness = 0.5
                        });
                    for (double gy = Math.Floor(minY / GridStep) * GridStep; gy <= maxY; gy += GridStep)
                        offscreen.Children.Add(new System.Windows.Shapes.Line
                        {
                            X1 = 0,
                            Y1 = gy - minY,
                            X2 = diagW,
                            Y2 = gy - minY,
                            Stroke = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                            StrokeThickness = 0.5
                        });
                }

                // Связи (ниже блоков)
                foreach (var link in _links)
                {
                    if (link.PathElement?.Data == null) continue;
                    offscreen.Children.Add(new System.Windows.Shapes.Path
                    {
                        Data = link.PathElement.Data.Clone(),
                        Stroke = link.PathElement.Stroke,
                        StrokeThickness = link.PathElement.StrokeThickness,
                        StrokeDashArray = link.PathElement.StrokeDashArray,
                        StrokeLineJoin = PenLineJoin.Round,
                        Fill = Brushes.Transparent,
                        RenderTransform = new TranslateTransform(-minX, -minY)
                    });
                    if (link.ArrowHead?.Data != null)
                        offscreen.Children.Add(new System.Windows.Shapes.Path
                        {
                            Data = link.ArrowHead.Data.Clone(),
                            Fill = link.ArrowHead.Fill,
                            Stroke = link.ArrowHead.Stroke,
                            StrokeThickness = link.ArrowHead.StrokeThickness,
                            RenderTransform = new TranslateTransform(-minX, -minY)
                        });
                }

                // Блоки (поверх связей, без ручки изменения размера)
                foreach (var node in _nodes)
                {
                    double nx = node.X - minX;
                    double ny = node.Y - minY;
                    var bb = new SolidColorBrush(Color.FromRgb(20, 20, 20));
                    var ar = new System.Windows.Media.FontFamily("Arial");
                    const double jOffset = 7; // отступ внутренних линий перекрёстка

                    if (node.IsJunction)
                    {
                        // Внешний квадрат перекрёстка
                        var sq = new System.Windows.Shapes.Rectangle
                        {
                            Width = node.Width,
                            Height = node.Height,
                            Fill = Brushes.White,
                            Stroke = bb,
                            StrokeThickness = 1.5,
                            RadiusX = 0,
                            RadiusY = 0
                        };
                        Canvas.SetLeft(sq, nx); Canvas.SetTop(sq, ny);
                        offscreen.Children.Add(sq);

                        // Левая вертикальная линия — у всех типов перекрёстков
                        offscreen.Children.Add(new System.Windows.Shapes.Line
                        {
                            X1 = nx + jOffset,
                            Y1 = ny,
                            X2 = nx + jOffset,
                            Y2 = ny + node.Height,
                            Stroke = bb,
                            StrokeThickness = 1.5
                        });

                        // Правая вертикальная линия — только у синхронных
                        if (node.IsSyncJunction)
                            offscreen.Children.Add(new System.Windows.Shapes.Line
                            {
                                X1 = nx + node.Width - jOffset,
                                Y1 = ny,
                                X2 = nx + node.Width - jOffset,
                                Y2 = ny + node.Height,
                                Stroke = bb,
                                StrokeThickness = 1.5
                            });

                        // Символ внутри квадрата
                        string sym;
                        switch (node.Type)
                        {
                            case "junction-and": case "junction-and-sync": sym = "&"; break;
                            case "junction-or": case "junction-or-sync": sym = "O"; break;
                            default: sym = "X"; break;
                        }
                        var lbl = new TextBlock
                        {
                            Text = sym,
                            FontSize = 15,
                            FontWeight = FontWeights.Bold,
                            Foreground = bb,
                            TextAlignment = TextAlignment.Center,
                            Width = node.Width,
                            FontFamily = ar
                        };
                        Canvas.SetLeft(lbl, nx); Canvas.SetTop(lbl, ny + node.Height / 2 - 13);
                        offscreen.Children.Add(lbl);

                        // Метка «Jn» справа сверху
                        var jnum = new TextBlock
                        {
                            Text = "J" + node.Number.ToString(),
                            FontSize = 8,
                            Foreground = bb,
                            FontFamily = ar
                        };
                        Canvas.SetLeft(jnum, nx + node.Width + 2); Canvas.SetTop(jnum, ny);
                        offscreen.Children.Add(jnum);
                    }
                    else
                    {
                        const double stripH = 16; // высота нижней полосы
                        double textAreaH = node.Height - stripH;
                        double midX = node.Width / 2;

                        // Прямоугольник UOB-блока
                        var rect = new System.Windows.Shapes.Rectangle
                        {
                            Width = node.Width,
                            Height = node.Height,
                            Fill = Brushes.White,
                            Stroke = bb,
                            StrokeThickness = 1.5,
                            RadiusX = 0,
                            RadiusY = 0
                        };
                        Canvas.SetLeft(rect, nx); Canvas.SetTop(rect, ny);
                        offscreen.Children.Add(rect);

                        // Горизонтальный разделитель над нижней полосой
                        offscreen.Children.Add(new System.Windows.Shapes.Line
                        {
                            X1 = nx,
                            Y1 = ny + textAreaH,
                            X2 = nx + node.Width,
                            Y2 = ny + textAreaH,
                            Stroke = bb,
                            StrokeThickness = 1.5
                        });

                        // Вертикальный разделитель нижней полосы — по центру
                        offscreen.Children.Add(new System.Windows.Shapes.Line
                        {
                            X1 = nx + midX,
                            Y1 = ny + textAreaH,
                            X2 = nx + midX,
                            Y2 = ny + node.Height,
                            Stroke = bb,
                            StrokeThickness = 1.5
                        });

                        // Название блока (заглавными)
                        var txt = new TextBlock
                        {
                            Text = node.Text.ToUpper(),
                            FontSize = 11,
                            FontWeight = FontWeights.Bold,
                            Foreground = bb,
                            TextAlignment = TextAlignment.Center,
                            TextWrapping = TextWrapping.Wrap,
                            Width = node.Width - 8,
                            FontFamily = ar
                        };
                        Canvas.SetLeft(txt, nx + 4); Canvas.SetTop(txt, ny + 6);
                        offscreen.Children.Add(txt);

                        // Номер блока — в левой ячейке нижней полосы
                        var num = new TextBlock
                        {
                            Text = node.Number.ToString(),
                            FontSize = 9,
                            Foreground = bb,
                            FontFamily = ar
                        };
                        Canvas.SetLeft(num, nx + 4); Canvas.SetTop(num, ny + textAreaH + 3);
                        offscreen.Children.Add(num);
                    }
                }

                // Шаг 3 — разметка, чтобы все элементы получили корректные размеры
                offscreen.Measure(new Size(diagW, diagH));
                offscreen.Arrange(new Rect(0, 0, diagW, diagH));
                offscreen.UpdateLayout();

                // Шаг 4 — рендеринг при удвоенном DPI (LayoutTransform масштабирует до разметки)
                const double dpiScale = 2.0;
                offscreen.LayoutTransform = new ScaleTransform(dpiScale, dpiScale);
                offscreen.Measure(new Size(diagW * dpiScale, diagH * dpiScale));
                offscreen.Arrange(new Rect(0, 0, diagW * dpiScale, diagH * dpiScale));
                offscreen.UpdateLayout();

                int pixW = (int)(diagW * dpiScale);
                int pixH = (int)(diagH * dpiScale);
                var rtb = new RenderTargetBitmap(pixW, pixH, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(offscreen);

                // Шаг 5 — сохранение PNG
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using (var fs = new FileStream(dlg.FileName, FileMode.Create))
                    encoder.Save(fs);

                SetStatus($"Экспортировано: {System.IO.Path.GetFileName(dlg.FileName)}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при экспорте:\n{ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ═════════════════════════════════════════════════════════════
        // ОТМЕНА / ПОВТОР
        // ═════════════════════════════════════════════════════════════

        private void Undo()
        {
            _history.Undo();
            DeselectAll(); RefreshAllLinks(); UpdateStatus();
            SetStatus(_history.CanUndo ? $"Отменено: {_history.UndoDescription}" : "Нечего отменять");
        }

        private void Redo()
        {
            _history.Redo();
            DeselectAll(); RefreshAllLinks(); UpdateStatus();
            SetStatus(_history.CanRedo ? $"Повторено: {_history.RedoDescription}" : "Нечего повторять");
        }

        private void RefreshUndoRedoUI()
        {
            UndoButton.IsEnabled = _history.CanUndo;
            RedoButton.IsEnabled = _history.CanRedo;
            UndoStatusText.Text = _history.CanUndo ? $"↩ {_history.UndoDescription}" : "";
        }

        // ═════════════════════════════════════════════════════════════
        // ДОБАВЛЕНИЕ ЭЛЕМЕНТОВ
        // ═════════════════════════════════════════════════════════════

        // ─── Режим размещения ─────────────────────────────────────────

        private void EnterPlacementMode(string type, RoutedEventArgs e)
        {
            // Определяем режим: была ли зажата Ctrl
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

            // Повторное нажатие той же кнопки в одиночном режиме — выходим из режима
            if (_isPlacementMode && _placementType == type && !_isContinuousPlacement)
            { CancelPlacementMode(); return; }

            CancelPlacementMode(); // сбрасываем предыдущий режим

            _isPlacementMode = true;
            _isContinuousPlacement = ctrl;
            _placementType = type;

            HighlightPlacementButton(type, true);

            string typeName = PlacementTypeName(type);
            if (ctrl)
                SetStatus($"Режим размещения (непрерывный): кликайте на холст для добавления «{typeName}». Escape — выход.");
            else
                SetStatus($"Режим размещения: кликните на холст для добавления «{typeName}». Escape — отмена.");

            LinkModeHintText.Text = ctrl
                ? $"Непрерывное размещение «{typeName}» — кликайте. Escape — выход"
                : $"Разместить «{typeName}» — кликните на холст";
            LinkModeHint.Visibility = Visibility.Visible;
            LinkModeHint.Background = new SolidColorBrush(Color.FromRgb(232, 101, 10));
        }

        private void CancelPlacementMode()
        {
            if (!_isPlacementMode) return;
            HighlightPlacementButton(_placementType, false);
            _isPlacementMode = false;
            _isContinuousPlacement = false;
            _placementType = null;
            LinkModeHint.Visibility = Visibility.Collapsed;
        }

        private void PlaceNodeAt(Point canvasPoint)
        {
            double cx = SnapToGrid(canvasPoint.X);
            double cy = SnapToGrid(canvasPoint.Y);

            Node node;
            if (_placementType == "uob")
            {
                node = new Node
                {
                    Type = "uob",
                    Text = "Блок поведения",
                    Width = 120,
                    Height = 60,
                    Number = NextUobNumber()
                };
                node.CreateVisual();
                node.AutoSize();
                // Центрируем блок по точке клика
                node.X = SnapToGrid(cx - node.Width / 2);
                node.Y = SnapToGrid(cy - node.Height / 2);
                node.UpdatePosition();
            }
            else
            {
                node = new Node { Type = _placementType, Number = NextJunctionNumber() };
                node.Text = "&";
                if (_placementType == "junction-or" || _placementType == "junction-or-sync") node.Text = "O";
                if (_placementType == "junction-xor") node.Text = "X";
                node.CreateVisual();
                node.X = SnapToGrid(cx - node.Width / 2);
                node.Y = SnapToGrid(cy - node.Height / 2);
                node.UpdatePosition();
            }

            _history.Execute(new AddNodeCommand(node, _mediator));
            UpdateStatus();
            SelectNode(node);
            SetStatus($"Добавлен «{PlacementTypeName(_placementType)}» #{node.Number}");

            // Блокируем перетаскивание до отпускания кнопки мыши,
            // чтобы не создавался ложный MoveNodeCommand в истории
            _justPlaced = true;

            if (!_isContinuousPlacement)
                CancelPlacementMode();
        }

        private void HighlightPlacementButton(string type, bool active)
        {
            var activeColor = new SolidColorBrush(Color.FromRgb(240, 160, 20));
            var uobColor = new SolidColorBrush(Color.FromRgb(232, 101, 10));
            var jColor = new SolidColorBrush(Color.FromRgb(196, 82, 8));

            switch (type)
            {
                case "uob": AddUobButton.Background = active ? activeColor : uobColor; break;
                case "junction-and": AddAndButton.Background = active ? activeColor : jColor; break;
                case "junction-and-sync": AddAndSyncButton.Background = active ? activeColor : jColor; break;
                case "junction-or": AddOrButton.Background = active ? activeColor : jColor; break;
                case "junction-or-sync": AddOrSyncButton.Background = active ? activeColor : jColor; break;
                case "junction-xor": AddXorButton.Background = active ? activeColor : jColor; break;
            }
        }

        private string PlacementTypeName(string type)
        {
            switch (type)
            {
                case "uob": return "UOB-блок";
                case "junction-and": return "AND асинхр.";
                case "junction-and-sync": return "AND синхр.";
                case "junction-or": return "OR асинхр.";
                case "junction-or-sync": return "OR синхр.";
                case "junction-xor": return "XOR";
                default: return type;
            }
        }

        // Методы для внутреннего использования
        private void ExecuteAddUob()
        {
            var node = BuildUobNode();
            _history.Execute(new AddNodeCommand(node, _mediator));
            UpdateStatus(); SelectNode(node);
        }

        private void ExecuteAddJunction(string type)
        {
            var node = BuildJunctionNode(type);
            _history.Execute(new AddNodeCommand(node, _mediator));
            UpdateStatus(); SelectNode(node);
        }

        private Node BuildUobNode()
        {
            var n = new Node { Type = "uob", Text = "Блок поведения", Width = 120, Height = 60, Number = NextUobNumber() };
            n.CreateVisual();
            n.AutoSize();
            var pos = FindFreePosition(n.Width, n.Height);
            n.X = pos.X; n.Y = pos.Y;
            n.UpdatePosition();
            return n;
        }

        private Node BuildJunctionNode(string type)
        {
            var n = new Node { Type = type, Number = NextJunctionNumber() };
            n.Text = "&";
            if (type == "junction-or" || type == "junction-or-sync") n.Text = "O";
            if (type == "junction-xor") n.Text = "X";
            n.CreateVisual();
            var pos = FindFreePosition(n.Width, n.Height);
            n.X = pos.X; n.Y = pos.Y;
            n.UpdatePosition();
            return n;
        }

        // Находит свободную позицию в видимой области холста,
        // где элемент w×h не перекрывается с существующими блоками.
        // Поиск ведётся от центра видимой области поочерёдно вверх и вниз.
        private System.Windows.Point FindFreePosition(double w, double h)
        {
            const double gap = 20; // минимальный отступ между блоками
            const double step = 20; // шаг горизонтального сканирования

            // Центр видимой области в координатах холста
            double viewCx = (CanvasScroll.ViewportWidth / 2 - CanvasPan.X) / _zoom;
            double viewCy = (CanvasScroll.ViewportHeight / 2 - CanvasPan.Y) / _zoom;

            double startX = SnapToGrid(Math.Max(20, viewCx - w / 2));
            double startY = SnapToGrid(Math.Max(20, viewCy - h / 2));

            double rowStep = SnapToGrid(h + gap);
            if (rowStep < GridStep) rowStep = GridStep;

            for (int rowOffset = 0; rowOffset < 60; rowOffset++)
            {
                int sign = (rowOffset % 2 == 0) ? 1 : -1;
                int mult = (rowOffset + 1) / 2;
                double y = SnapToGrid(startY + sign * mult * rowStep);
                if (y < 0) continue;

                for (double x = startX; x < startX + 3000; x += step)
                {
                    double sx = SnapToGrid(x);
                    var candidate = new Rect(sx, y, w, h);
                    bool overlaps = false;

                    foreach (var existing in _nodes)
                    {
                        var existingRect = new Rect(
                            existing.X - gap / 2, existing.Y - gap / 2,
                            existing.Width + gap, existing.Height + gap);
                        if (candidate.IntersectsWith(existingRect)) { overlaps = true; break; }
                    }

                    if (!overlaps) return new System.Windows.Point(sx, y);
                }
            }

            // Запасной вариант — центр видимой области без проверки перекрытий
            return new System.Windows.Point(startX, startY);
        }

        // ═════════════════════════════════════════════════════════════
        // УДАЛЕНИЕ
        // ═════════════════════════════════════════════════════════════

        private void ExecuteDelete()
        {
            // Удаление нескольких выбранных элементов
            if (HasMultiSelection)
            {
                var items = _selectedNodes
                    .Select(n => (n, _links.Where(l => l.SourceId == n.Id || l.TargetId == n.Id).ToList()))
                    .ToList();
                DeselectAll();
                _history.Execute(new RemoveMultipleNodesCommand(items, _mediator));
                UpdateStatus();
                SetStatus($"Удалено блоков: {items.Count}");
                return;
            }

            // Удаление одного блока
            if (_selectedNode != null)
            {
                var node = _selectedNode;
                var links = _links.Where(l => l.SourceId == node.Id || l.TargetId == node.Id).ToList();
                DeselectAll();
                _history.Execute(new RemoveNodeCommand(node, links, _mediator));
                UpdateStatus(); SetStatus($"Удалён блок «{node.Text}»");
            }
            else if (_selectedLink != null)
            {
                var link = _selectedLink;
                DeselectAll();
                _history.Execute(new RemoveLinkCommand(link, _mediator));
                UpdateStatus(); SetStatus("Связь удалена");
            }
            else SetStatus("Выберите элемент для удаления.");
        }

        private void ClearAll()
        {
            // Если мы внутри декомпозиции — сначала поднимаемся наверх
            while (_navStack.Count > 0)
            {
                foreach (var n in _nodes) _mediator.RemoveNodeVisualsOnly(n);
                foreach (var l in _links) _mediator.RemoveLinkVisualsOnly(l);
                var entry = _navStack[_navStack.Count - 1];
                _navStack.RemoveAt(_navStack.Count - 1);
                _nodes.Clear(); _links.Clear();
                foreach (var n in entry.Nodes) _nodes.Add(n);
                foreach (var l in entry.Links) _links.Add(l);
            }

            DrawingCanvas.Children.Clear();
            _nodes.Clear(); _links.Clear();
            _decompositions.Clear();
            _clipboardNodes.Clear(); _clipboardLinks.Clear();
            _selectedNode = null; _selectedLink = null;
            _selectedNodes.Clear(); _multiDragOrigins.Clear();
            _isMultiDragging = false; _isRubberBanding = false; _justPlaced = false;
            RubberBandRect.Visibility = Visibility.Collapsed;
            _history = new UndoRedoManager();
            _history.StackChanged += (_, __) => RefreshUndoRedoUI();
            _history.StackChanged += (_, __) => _saveOrNot = true;
            CancelLinkMode();
            CancelPlacementMode();
            _diagramName = "Новая диаграмма"; _diagramAuthor = Environment.UserName;
            _diagramDescription = ""; _diagramCreated = DateTime.Now;
            _saveOrNot = false;

            UpdateBreadcrumb();
            DrawGrid(); UpdateStatus(); RefreshUndoRedoUI(); UpdateTitleBar();

        }

        // ─── Нумерация блоков ─────────────────────────────────────────
        // UOB и перекрёстки нумеруются независимо.
        // Возвращается наименьшее незанятое целое число ≥ 1,
        // что позволяет переиспользовать номера удалённых блоков.

        private int NextUobNumber()
        {
            var used = new HashSet<int>(_nodes.Where(n => !n.IsJunction).Select(n => n.Number));
            for (int i = 1; ; i++) if (!used.Contains(i)) return i;
        }

        private int NextJunctionNumber()
        {
            var used = new HashSet<int>(_nodes.Where(n => n.IsJunction).Select(n => n.Number));
            for (int i = 1; ; i++) if (!used.Contains(i)) return i;
        }

        // ═════════════════════════════════════════════════════════════
        // ПОРЯДОК ОТОБРАЖЕНИЯ (Z-ORDER)
        // ═════════════════════════════════════════════════════════════

        private void BringToFront(Node node)
        {
            // Удаляем все визуальные части блока, затем добавляем в конец —
            // в Canvas наибольший индекс означает отрисовку поверх остальных
            DrawingCanvas.Children.Remove(node.MainShape);
            if (node.JLineLeft != null) DrawingCanvas.Children.Remove(node.JLineLeft);
            if (node.JLineRight != null) DrawingCanvas.Children.Remove(node.JLineRight);
            if (node.BottomLine != null) DrawingCanvas.Children.Remove(node.BottomLine);
            if (node.StripDivider != null) DrawingCanvas.Children.Remove(node.StripDivider);
            DrawingCanvas.Children.Remove(node.TextBlock);
            if (node.NumberText != null) DrawingCanvas.Children.Remove(node.NumberText);
            if (node.ResizeHandle != null) DrawingCanvas.Children.Remove(node.ResizeHandle);

            DrawingCanvas.Children.Add(node.MainShape);
            if (node.JLineLeft != null) DrawingCanvas.Children.Add(node.JLineLeft);
            if (node.JLineRight != null) DrawingCanvas.Children.Add(node.JLineRight);
            if (node.BottomLine != null) DrawingCanvas.Children.Add(node.BottomLine);
            if (node.StripDivider != null) DrawingCanvas.Children.Add(node.StripDivider);
            DrawingCanvas.Children.Add(node.TextBlock);
            if (node.NumberText != null) DrawingCanvas.Children.Add(node.NumberText);
            if (node.ResizeHandle != null) DrawingCanvas.Children.Add(node.ResizeHandle);

            // Синхронизируем порядок в списке данных для корректного хит-теста
            _nodes.Remove(node); _nodes.Add(node);
            SetStatus($"«{node.Text}» — на передний план");
        }

        private void SendToBack(Node node)
        {
            // Вставляем сразу после связей (каждая связь = PathElement + ArrowHead)
            int linkCount = _links.Count * 2;

            DrawingCanvas.Children.Remove(node.MainShape);
            if (node.JLineLeft != null) DrawingCanvas.Children.Remove(node.JLineLeft);
            if (node.JLineRight != null) DrawingCanvas.Children.Remove(node.JLineRight);
            if (node.BottomLine != null) DrawingCanvas.Children.Remove(node.BottomLine);
            if (node.StripDivider != null) DrawingCanvas.Children.Remove(node.StripDivider);
            DrawingCanvas.Children.Remove(node.TextBlock);
            if (node.NumberText != null) DrawingCanvas.Children.Remove(node.NumberText);
            if (node.ResizeHandle != null) DrawingCanvas.Children.Remove(node.ResizeHandle);

            int insertAt = Math.Min(linkCount, DrawingCanvas.Children.Count);
            DrawingCanvas.Children.Insert(insertAt, node.MainShape);
            int idx = insertAt + 1;
            if (node.JLineLeft != null) DrawingCanvas.Children.Insert(idx++, node.JLineLeft);
            if (node.JLineRight != null) DrawingCanvas.Children.Insert(idx++, node.JLineRight);
            if (node.BottomLine != null) DrawingCanvas.Children.Insert(idx++, node.BottomLine);
            if (node.StripDivider != null) DrawingCanvas.Children.Insert(idx++, node.StripDivider);
            DrawingCanvas.Children.Insert(idx++, node.TextBlock);
            if (node.NumberText != null) DrawingCanvas.Children.Insert(idx++, node.NumberText);
            if (node.ResizeHandle != null) DrawingCanvas.Children.Insert(idx, node.ResizeHandle);

            _nodes.Remove(node); _nodes.Insert(0, node);
            SetStatus($"«{node.Text}» — на задний план");
        }

        // ═════════════════════════════════════════════════════════════
        // ТИП СВЯЗИ
        // ═════════════════════════════════════════════════════════════

        private void SetLinkType(Link link, string type)
        {
            if (link.Type == type) return;
            _history.Execute(new ChangeLinkTypeCommand(link, link.Type, type));
            SetStatus($"Тип связи: {link.TypeLabel}");
        }

        // ═════════════════════════════════════════════════════════════
        // КОНТЕКСТНЫЕ МЕНЮ
        // ═════════════════════════════════════════════════════════════

        private ContextMenu BuildNodeContextMenu(Node node)
        {
            var menu = new ContextMenu();

            menu.Items.Add(new MenuItem
            {
                Header = node.IsJunction
                    ? $"Перекрёсток {Idef3Validator.JunctionName(node.Type)} #{node.Number}"
                    : $"Блок #{node.Number}: {node.Text}",
                IsEnabled = false,
                FontWeight = FontWeights.Bold
            });
            menu.Items.Add(new Separator());

            if (!node.IsJunction)
            {
                var editItem = new MenuItem { Header = "✎  Редактировать текст" };
                editItem.Click += (_, __) => EditNodeText(node);
                menu.Items.Add(editItem);
            }

            var numberItem = new MenuItem
            {
                Header = node.IsJunction
                    ? $"#  Изменить номер (J{node.Number})"
                    : $"#  Изменить номер ({node.Number})"
            };
            numberItem.Click += (_, __) => EditNodeNumber(node);
            menu.Items.Add(numberItem);

            if (!node.IsJunction)
            {
                var refItem = new MenuItem
                {
                    Header = string.IsNullOrEmpty(node.ReferenceExpression)
                        ? "⊞  Добавить ссылочное выражение"
                        : $"⊞  Ссылка: {node.ReferenceExpression}"
                };
                refItem.Click += (_, __) => EditReferenceExpression(node);
                menu.Items.Add(refItem);
            }

            var copyItem = new MenuItem { Header = "⎘  Копировать  (Ctrl+C)" };
            copyItem.Click += (_, __) => { SelectNode(node); CopySelected(); };
            menu.Items.Add(copyItem);

            if (!node.IsJunction)
            {
                bool hasDecomp = _decompositions.ContainsKey(node.Id);
                var decompItem = new MenuItem
                {
                    Header = hasDecomp
                        ? "▶  Открыть декомпозицию"
                        : "▶  Создать декомпозицию"
                };
                // При создании — сначала запросить ссылочное выражение, потом войти.
                // При открытии существующей — сразу войти.
                decompItem.Click += (_, __) =>
                {
                    if (hasDecomp) EnterDecomposition(node);
                    else CreateDecompositionWithReference(node);
                };
                menu.Items.Add(decompItem);

                if (hasDecomp)
                {
                    var delDecompItem = new MenuItem
                    {
                        Header = "✕  Удалить декомпозицию",
                        Foreground = Brushes.OrangeRed
                    };
                    delDecompItem.Click += (_, __) => DeleteDecomposition(node);
                    menu.Items.Add(delDecompItem);
                }
            }

            var frontItem = new MenuItem { Header = "▲  На передний план" };
            frontItem.Click += (_, __) => BringToFront(node);
            menu.Items.Add(frontItem);

            var backItem = new MenuItem { Header = "▼  На задний план" };
            backItem.Click += (_, __) => SendToBack(node);
            menu.Items.Add(backItem);

            menu.Items.Add(new Separator());

            var deleteItem = new MenuItem { Header = "✕  Удалить", Foreground = Brushes.Red };
            deleteItem.Click += (_, __) => { SelectNode(node); ExecuteDelete(); };
            menu.Items.Add(deleteItem);

            return menu;
        }

        private ContextMenu BuildLinkContextMenu(Link link)
        {
            var menu = new ContextMenu();

            menu.Items.Add(new MenuItem
            {
                Header = $"Связь: {link.TypeLabel}",
                IsEnabled = false,
                FontWeight = FontWeights.Bold
            });
            menu.Items.Add(new Separator());

            // Подменю выбора типа связи
            var typeMenu = new MenuItem { Header = "Тип связи" };

            var precedenceItem = new MenuItem
            {
                Header = "─────  Предшествования (сплошная)",
                IsChecked = link.Type == "precedence",
                IsCheckable = true
            };
            precedenceItem.Click += (_, __) => SetLinkType(link, "precedence");

            var relationalItem = new MenuItem
            {
                Header = "- - -  Реляционная (пунктир)",
                IsChecked = link.Type == "relational",
                IsCheckable = true
            };
            relationalItem.Click += (_, __) => SetLinkType(link, "relational");

            var objectFlowItem = new MenuItem
            {
                Header = "·····  Поток объектов (точки)",
                IsChecked = link.Type == "object-flow",
                IsCheckable = true
            };
            objectFlowItem.Click += (_, __) => SetLinkType(link, "object-flow");

            typeMenu.Items.Add(precedenceItem);
            typeMenu.Items.Add(relationalItem);
            typeMenu.Items.Add(objectFlowItem);
            menu.Items.Add(typeMenu);

            var labelItem = new MenuItem
            {
                Header = string.IsNullOrEmpty(link.Label)
                    ? "✎  Добавить подпись"
                    : $"✎  Подпись: {link.Label}"
            };
            labelItem.Click += (_, __) => EditLinkLabel(link);
            menu.Items.Add(labelItem);
            menu.Items.Add(new Separator());

            var deleteItem = new MenuItem { Header = "✕  Удалить связь", Foreground = Brushes.Red };
            deleteItem.Click += (_, __) => { SelectLink(link); ExecuteDelete(); };
            menu.Items.Add(deleteItem);

            return menu;
        }

        // ═════════════════════════════════════════════════════════════
        // РЕЖИМ СОЗДАНИЯ СВЯЗИ
        // ═════════════════════════════════════════════════════════════

        private void ToggleLinkMode(object sender, RoutedEventArgs e)
        {
            _isLinkMode = !_isLinkMode;
            if (_isLinkMode)
            {
                _linkSourceNode = null;
                AddLinkButton.Background = new SolidColorBrush(Color.FromRgb(240, 160, 20));
                LinkModeHint.Visibility = Visibility.Visible;
                LinkModeHintText.Text = "Режим связи: кликните на источник";
                SetStatus("Режим связи — выберите источник");
            }
            else CancelLinkMode();
        }

        private void CancelLinkMode()
        {
            _isLinkMode = false;
            _linkSourceNode?.Highlight(false);
            _linkSourceNode = null;
            AddLinkButton.Background = new SolidColorBrush(Color.FromRgb(240, 120, 32));
            LinkModeHint.Visibility = Visibility.Collapsed;
            SetStatus("Готово");
        }

        private void TryCreateLink(Node target)
        {
            if (_linkSourceNode == null)
            {
                // Первый клик — выбираем источник
                _linkSourceNode = target;
                _linkSourceNode.Highlight(true);
                LinkModeHintText.Text = "Теперь кликните на целевой блок";
                SetStatus($"Источник: «{_linkSourceNode.Text}» — выберите цель");
                return;
            }
            if (_linkSourceNode == target)
            { SetStatus("Нельзя соединить элемент сам с собой."); return; }
            // Проверяем связь в обоих направлениях: A→B и B→A считаются одной связью
            if (_links.Any(l => (l.SourceId == _linkSourceNode.Id && l.TargetId == target.Id)
                             || (l.SourceId == target.Id && l.TargetId == _linkSourceNode.Id)))
            { SetStatus("Связь между этими блоками уже существует."); CancelLinkMode(); return; }

            var link = new Link { SourceId = _linkSourceNode.Id, TargetId = target.Id };
            link.CreateVisual();
            _history.Execute(new AddLinkCommand(link, _mediator));
            UpdateStatus();
            SetStatus($"Связь создана: «{_linkSourceNode.Text}» → «{target.Text}»");
            CancelLinkMode();
        }

        // ═════════════════════════════════════════════════════════════
        // ВЫДЕЛЕНИЕ ЭЛЕМЕНТОВ
        // ═════════════════════════════════════════════════════════════

        private void SelectNode(Node n) { DeselectAll(); _selectedNode = n; n?.Highlight(true); }
        private void SelectLink(Link l) { DeselectAll(); _selectedLink = l; l?.Highlight(true); }

        private void DeselectAll()
        {
            _selectedNode?.Highlight(false); _selectedNode = null;
            _selectedLink?.Highlight(false); _selectedLink = null;
            foreach (var n in _selectedNodes) n.Highlight(false);
            _selectedNodes.Clear();
        }

        // Применяет групповое выделение по прямоугольнику резиновой рамки.
        private void ApplyRubberBandSelection(Rect selRect)
        {
            DeselectAll();
            foreach (var node in _nodes)
            {
                var nodeRect = new Rect(node.X, node.Y, node.Width, node.Height);
                if (selRect.IntersectsWith(nodeRect))
                {
                    _selectedNodes.Add(node);
                    node.Highlight(true);
                }
            }
            if (_selectedNodes.Count == 1)
            {
                // Один блок — повышаем до одиночного выделения
                _selectedNode = _selectedNodes[0];
                _selectedNodes.Clear();
            }
            else if (_selectedNodes.Count > 1)
                SetStatus($"Выбрано элементов: {_selectedNodes.Count} — перетащите для перемещения, Del — удалить");
        }

        private bool HasMultiSelection => _selectedNodes.Count > 1;

        // ═════════════════════════════════════════════════════════════
        // МЫШЬ — ЛЕВАЯ КНОПКА
        // ═════════════════════════════════════════════════════════════

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Point click = e.GetPosition(DrawingCanvas);

            // Режим размещения — наивысший приоритет
            if (_isPlacementMode)
            {
                if (CheckResizeStart(click)) { CancelPlacementMode(); return; }
                _wasDragged = false; // сбрасываем флаг перетаскивания
                PlaceNodeAt(click);
                _lastClickTime = DateTime.Now; _lastClickedNode = null;
                return;
            }

            // Проверка ручки изменения размера
            if (CheckResizeStart(click)) return;

            // Двойной клик:
            //   • есть декомпозиция → войти в неё
            //   • нет декомпозиции  → редактировать текст (прежнее поведение)
            bool dbl = (DateTime.Now - _lastClickTime).TotalMilliseconds < 300;
            if (dbl && _lastClickedNode != null)
            {
                var dblNode = _lastClickedNode;
                _lastClickedNode = null; _lastClickTime = DateTime.MinValue;
                if (!dblNode.IsJunction && _decompositions.ContainsKey(dblNode.Id))
                    EnterDecomposition(dblNode);
                else
                    EditNodeText(dblNode);
                return;
            }

            Node hitNode = HitTestNode(click);

            // Режим создания связи
            if (_isLinkMode)
            {
                if (hitNode != null) TryCreateLink(hitNode);
                _lastClickTime = DateTime.Now; _lastClickedNode = null;
                return;
            }

            // Попали в блок
            if (hitNode != null)
            {
                if (_selectedNodes.Contains(hitNode))
                {
                    // Блок в группе — начинаем групповое перетаскивание
                    _mouseDownPoint = click; _wasDragged = false;
                    _isMultiDragging = true;
                    _multiDragOrigins.Clear();
                    foreach (var n in _selectedNodes) _multiDragOrigins[n] = new Point(n.X, n.Y);
                    DrawingCanvas.CaptureMouse();
                    return;
                }
                SelectNode(hitNode);
                _originalX = hitNode.X; _originalY = hitNode.Y;
                _mouseDownPoint = click; _wasDragged = false;
                _lastClickedNode = hitNode; _lastClickTime = DateTime.Now;
                DrawingCanvas.CaptureMouse();
                return;
            }

            // Попали в связь
            Link hitLink = HitTestLink(click);
            if (hitLink != null)
            {
                SelectLink(hitLink);
                _lastClickedLink = hitLink; _lastClickTime = DateTime.Now;
                return;
            }

            // Пустое место — рисуем резиновую рамку
            DeselectAll();
            _lastClickedNode = null; _lastClickedLink = null;
            _lastClickTime = DateTime.Now;

            _isRubberBanding = true;
            _rubberBandStart = click;
            RubberBandRect.Visibility = Visibility.Collapsed;
            Canvas.SetLeft(RubberBandRect, click.X);
            Canvas.SetTop(RubberBandRect, click.Y);
            RubberBandRect.Width = 0; RubberBandRect.Height = 0;
            DrawingCanvas.CaptureMouse();
        }

        // ═════════════════════════════════════════════════════════════
        // МЫШЬ — ПРАВАЯ КНОПКА (панорамирование + контекстное меню)
        // ═════════════════════════════════════════════════════════════

        private void Canvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Сбрасываем любое незавершённое состояние от ЛКМ.
            // Без этого _selectedNode и _mouseDownPoint могут остаться "застрявшими"
            // после последовательности ЛКМ→ПКМ, вызывая телепортацию блока
            // при следующем ЛКМ по пустому месту холста.
            _wasDragged = false;
            _isRubberBanding = false;
            _isMultiDragging = false;
            _multiDragOrigins.Clear();
            RubberBandRect.Visibility = Visibility.Collapsed;
            DrawingCanvas.ReleaseMouseCapture(); // снимаем захват от ЛКМ (если был)

            _isPanning = true;
            _panStartMouse = e.GetPosition(this);
            _panStartX = CanvasPan.X; _panStartY = CanvasPan.Y;
            DrawingCanvas.CaptureMouse();
            PanHint.Visibility = Visibility.Visible;
            e.Handled = true;
        }

        private void Canvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isPanning = false;
            DrawingCanvas.ReleaseMouseCapture();
            PanHint.Visibility = Visibility.Collapsed;

            // Если мышь почти не двигалась — показываем контекстное меню
            Point releasePos = e.GetPosition(this);
            double moved = Math.Abs(releasePos.X - _panStartMouse.X)
                         + Math.Abs(releasePos.Y - _panStartMouse.Y);

            if (moved < 5)
            {
                Point click = e.GetPosition(DrawingCanvas);
                Node hitNode = HitTestNode(click);
                Link hitLink = hitNode == null ? HitTestLink(click) : null;

                if (hitNode != null)
                {
                    SelectNode(hitNode);
                    var menu = BuildNodeContextMenu(hitNode);
                    menu.PlacementTarget = DrawingCanvas; menu.IsOpen = true;
                    // Первый ЛКМ после закрытия меню не должен начинать перетаскивание
                    _ignoreNextDrag = true;
                }
                else if (hitLink != null)
                {
                    SelectLink(hitLink);
                    var menu = BuildLinkContextMenu(hitLink);
                    menu.PlacementTarget = DrawingCanvas; menu.IsOpen = true;
                    _ignoreNextDrag = true;
                }
            }

            e.Handled = true;
        }

        // ═════════════════════════════════════════════════════════════
        // ДВИЖЕНИЕ МЫШИ
        // ═════════════════════════════════════════════════════════════

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            Point pos = e.GetPosition(DrawingCanvas);

            // Изменение размера
            if (_isResizing && _resizeNode != null && e.LeftButton == MouseButtonState.Pressed)
            { HandleResize(pos); return; }

            // Групповое перетаскивание
            if (_isMultiDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                double dx = pos.X - _mouseDownPoint.X, dy = pos.Y - _mouseDownPoint.Y;
                foreach (var n in _selectedNodes)
                {
                    n.X = _multiDragOrigins[n].X + dx; n.Y = _multiDragOrigins[n].Y + dy;
                    n.UpdatePosition(); RefreshLinksForNode(n);
                }
                _wasDragged = true;
                return;
            }

            // Одиночное перетаскивание.
            // Пропускаем сразу после размещения блока во избежание ложного MoveNodeCommand.
            if (!_isPlacementMode && !_justPlaced && !_ignoreNextDrag &&
                _selectedNode != null &&
                e.LeftButton == MouseButtonState.Pressed && !_isResizing)
            {
                double dx = pos.X - _mouseDownPoint.X, dy = pos.Y - _mouseDownPoint.Y;
                _selectedNode.X = _originalX + dx; _selectedNode.Y = _originalY + dy;
                _selectedNode.UpdatePosition(); RefreshLinksForNode(_selectedNode);
                _wasDragged = true;
                return;
            }

            // Резиновая рамка выделения
            if (_isRubberBanding && e.LeftButton == MouseButtonState.Pressed)
            {
                double x = Math.Min(pos.X, _rubberBandStart.X);
                double y = Math.Min(pos.Y, _rubberBandStart.Y);
                double w = Math.Abs(pos.X - _rubberBandStart.X);
                double h = Math.Abs(pos.Y - _rubberBandStart.Y);
                Canvas.SetLeft(RubberBandRect, x); Canvas.SetTop(RubberBandRect, y);
                RubberBandRect.Width = w; RubberBandRect.Height = h;
                RubberBandRect.Visibility = w > 4 || h > 4 ? Visibility.Visible : Visibility.Collapsed;
                return;
            }

            // Панорамирование
            if (_isPanning && e.RightButton == MouseButtonState.Pressed)
            {
                Point cur = e.GetPosition(this);
                CanvasPan.X = _panStartX + (cur.X - _panStartMouse.X);
                CanvasPan.Y = _panStartY + (cur.Y - _panStartMouse.Y);
                return;
            }

            UpdateResizeCursor(pos);
            if (_isPlacementMode) DrawingCanvas.Cursor = Cursors.Cross;
        }

        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            DrawingCanvas.ReleaseMouseCapture();

            // Снимаем блокировку перетаскивания после отпускания кнопки
            _justPlaced = false;
            _ignoreNextDrag = false; // разрешаем перетаскивание после первого ЛКМ

            // Завершение изменения размера
            if (_isResizing)
            {
                if (_resizeNode != null)
                {
                    _history.PushAlreadyExecuted(new ResizeNodeCommand(
                        _resizeNode, _resizeStartWidth, _resizeStartHeight,
                        _resizeNode.Width, _resizeNode.Height, _mediator));
                    RefreshUndoRedoUI();
                }
                _isResizing = false; _resizeNode = null; RefreshAllLinks();
                return;
            }

            // Завершение резиновой рамки
            if (_isRubberBanding)
            {
                _isRubberBanding = false;
                RubberBandRect.Visibility = Visibility.Collapsed;
                double x = Canvas.GetLeft(RubberBandRect), y = Canvas.GetTop(RubberBandRect);
                var selRect = new Rect(x, y, RubberBandRect.Width, RubberBandRect.Height);
                if (selRect.Width > 4 || selRect.Height > 4) ApplyRubberBandSelection(selRect);
                return;
            }

            // Завершение группового перетаскивания — привязка к сетке и запись в историю
            if (_isMultiDragging)
            {
                _isMultiDragging = false;
                if (_wasDragged)
                {
                    var moves = new List<(Node, double, double, double, double)>();
                    foreach (var n in _selectedNodes)
                    {
                        double oldX = _multiDragOrigins[n].X, oldY = _multiDragOrigins[n].Y;
                        double newX = SnapToGrid(n.X), newY = SnapToGrid(n.Y);
                        n.X = newX; n.Y = newY; n.UpdatePosition(); RefreshLinksForNode(n);
                        moves.Add((n, oldX, oldY, newX, newY));
                    }
                    _history.PushAlreadyExecuted(new MoveMultipleNodesCommand(moves, _mediator));
                    RefreshUndoRedoUI();
                }
                _wasDragged = false; _multiDragOrigins.Clear();
                return;
            }

            // Завершение одиночного перетаскивания — привязка к сетке и запись в историю
            if (_selectedNode != null && _wasDragged)
            {
                double sx = SnapToGrid(_selectedNode.X), sy = SnapToGrid(_selectedNode.Y);
                _history.PushAlreadyExecuted(new MoveNodeCommand(
                    _selectedNode, _originalX, _originalY, sx, sy, _mediator));
                RefreshUndoRedoUI();
                _selectedNode.X = sx; _selectedNode.Y = sy;
                _selectedNode.UpdatePosition(); RefreshLinksForNode(_selectedNode);
            }
            _wasDragged = false;
        }

        private void Canvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Масштабирование: Ctrl + колесо мыши
            if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control) return;
            ZoomAround(e.GetPosition(DrawingCanvas), _zoom + (e.Delta > 0 ? ZoomStep : -ZoomStep));
            e.Handled = true;
        }

        // ═════════════════════════════════════════════════════════════
        // ХИТ-ТЕСТ
        // ═════════════════════════════════════════════════════════════

        private Node HitTestNode(Point p)
        {
            // Перебор в обратном порядке: верхние блоки (больший индекс) обрабатываются первыми
            for (int i = _nodes.Count - 1; i >= 0; i--)
            {
                var n = _nodes[i];
                if (p.X >= n.X && p.X <= n.X + n.Width && p.Y >= n.Y && p.Y <= n.Y + n.Height) return n;
            }
            return null;
        }

        private Link HitTestLink(Point p)
        {
            foreach (var l in _links) if (l.HitTest(p)) return l;
            return null;
        }

        // ═════════════════════════════════════════════════════════════
        // ИЗМЕНЕНИЕ РАЗМЕРА БЛОКА
        // ═════════════════════════════════════════════════════════════

        private bool CheckResizeStart(Point click)
        {
            foreach (var node in _nodes)
            {
                if (node.IsJunction) continue;
                double rx = node.X + node.Width - 5, ry = node.Y + node.Height - 5;
                if (click.X >= rx && click.X <= rx + 12 && click.Y >= ry && click.Y <= ry + 12)
                { StartResize(node, "corner", click); return true; }
                if (click.X >= rx && click.X <= rx + 12 && click.Y >= node.Y && click.Y <= node.Y + node.Height)
                { StartResize(node, "right", click); return true; }
                double by = node.Y + node.Height - 5;
                if (click.Y >= by && click.Y <= by + 12 && click.X >= node.X && click.X <= node.X + node.Width)
                { StartResize(node, "bottom", click); return true; }
            }
            return false;
        }

        private void StartResize(Node node, string dir, Point click)
        {
            _isResizing = true; _resizeNode = node; _resizeDirection = dir;
            _resizeStartX = click.X; _resizeStartY = click.Y;
            _resizeStartWidth = node.Width; _resizeStartHeight = node.Height;
        }

        private void HandleResize(Point pos)
        {
            double dX = pos.X - _resizeStartX, dY = pos.Y - _resizeStartY;
            const double stripH = 16; // высота нижней полосы
            const double padX = 24; // горизонтальный отступ текста
            const double padY = 16; // вертикальный отступ текста
            const double absMinW = 60; // абсолютный минимум ширины
            const double absMinH = 54; // абсолютный минимум высоты

            // Изменение ширины
            if (_resizeDirection == "corner" || _resizeDirection == "right")
            {
                double w = _resizeStartWidth + dX;
                if (w >= absMinW) { _resizeNode.Width = w; _resizeNode.MainShape.Width = w; }
            }

            // Изменение высоты с учётом переноса строк при текущей ширине
            if (_resizeDirection == "corner" || _resizeDirection == "bottom")
            {
                double h = _resizeStartHeight + dY, minH = absMinH;
                if (_resizeNode.TextBlock != null)
                {
                    double availW = Math.Max(_resizeNode.Width - padX, 20);
                    _resizeNode.TextBlock.Measure(new Size(availW, double.PositiveInfinity));
                    minH = Math.Max(_resizeNode.TextBlock.DesiredSize.Height + padY + stripH, absMinH);
                }
                if (h >= minH) { _resizeNode.Height = h; _resizeNode.MainShape.Height = h; }
            }

            _resizeNode.UpdatePosition();
            RefreshLinksForNode(_resizeNode);
        }

        // Вычисляет минимальный размер блока, при котором текст полностью помещается.
        // Высота учитывает перенос строк при текущей ширине.
        private void GetNodeMinSize(Node node, out double minW, out double minH)
        {
            const double stripH = 16, padX = 24, padY = 16;

            if (node.TextBlock == null) { minW = 80; minH = 54; return; }

            // Однострочная ширина текста
            node.TextBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            minW = Math.Max(node.TextBlock.DesiredSize.Width + padX, 80);

            // Высота с учётом переноса при текущей ширине
            double availW = Math.Max(node.Width - padX, 20);
            node.TextBlock.Measure(new Size(availW, double.PositiveInfinity));
            minH = Math.Max(node.TextBlock.DesiredSize.Height + padY + stripH, 54);
        }

        // Увеличивает размер блока, если он меньше необходимого для текста.
        // Уменьшение не происходит. Вызывается после редактирования текста.
        private void AutoSizeNode(Node node)
        {
            if (node == null || node.IsJunction || node.TextBlock == null) return;
            node.TextBlock.Text = node.Text.ToUpper();
            GetNodeMinSize(node, out double minW, out double minH);

            bool changed = false;
            if (node.Width < minW) { node.Width = minW; node.MainShape.Width = minW; changed = true; }
            if (node.Height < minH) { node.Height = minH; node.MainShape.Height = minH; changed = true; }

            if (changed) { node.UpdatePosition(); RefreshLinksForNode(node); }
        }

        private void UpdateResizeCursor(Point pos)
        {
            foreach (var node in _nodes)
            {
                if (node.IsJunction) continue;
                double rx = node.X + node.Width - 5, ry = node.Y + node.Height - 5;
                if (pos.X >= rx && pos.X <= rx + 12 && pos.Y >= ry && pos.Y <= ry + 12)
                { DrawingCanvas.Cursor = Cursors.SizeNWSE; return; }
                if (pos.X >= rx && pos.X <= rx + 12 && pos.Y >= node.Y && pos.Y <= node.Y + node.Height)
                { DrawingCanvas.Cursor = Cursors.SizeWE; return; }
                double by = node.Y + node.Height - 5;
                if (pos.Y >= by && pos.Y <= by + 12 && pos.X >= node.X && pos.X <= node.X + node.Width)
                { DrawingCanvas.Cursor = Cursors.SizeNS; return; }
            }
            DrawingCanvas.Cursor = Cursors.Arrow;
        }

        // ═════════════════════════════════════════════════════════════
        // РЕДАКТИРОВАНИЕ ТЕКСТА БЛОКА
        // ═════════════════════════════════════════════════════════════

        private void EditNodeText(Node node)
        {
            if (node.IsJunction) return;
            string oldText = node.Text;

            // Встроенное поле ввода поверх блока
            var box = new TextBox
            {
                Text = node.Text,
                FontSize = 11,
                Width = node.Width - 8,
                MinHeight = 24,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = false,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(232, 101, 10)),
                Padding = new Thickness(3),
                Background = new SolidColorBrush(Color.FromRgb(235, 245, 251)),
                FontFamily = new System.Windows.Media.FontFamily("Arial"),
                FontWeight = FontWeights.Bold
            };
            Canvas.SetLeft(box, node.X + 4); Canvas.SetTop(box, node.Y + 4);
            DrawingCanvas.Children.Add(box);
            box.Focus(); box.SelectAll();

            bool committed = false;
            Action commit = () =>
            {
                if (committed) return;
                committed = true;
                string newText = box.Text.Trim().Length > 0 ? box.Text.Trim() : oldText;
                DrawingCanvas.Children.Remove(box);
                if (newText != oldText)
                {
                    _history.Execute(new EditTextCommand(node, oldText, newText));
                    node.RefreshText();
                    AutoSizeNode(node); // автоподбор размера при реальном изменении текста
                }
            };

            box.LostFocus += (_, __) => commit();
            box.KeyDown += (_, args) =>
            {
                if (args.Key == Key.Enter) commit();
                if (args.Key == Key.Escape) DrawingCanvas.Children.Remove(box);
            };
        }

        // Встроенное поле ввода для смены номера блока или перекрёстка.
        // Проверяет: только целое число > 0, при конфликте — предупреждает.
        private void EditNodeNumber(Node node)
        {
            int oldNumber = node.Number;

            // Позиция и размер поля: у UOB — левая ячейка нижней полосы,
            // у перекрёстка — метка «Jn» справа сверху.
            double boxX, boxY, boxW;
            if (node.IsJunction)
            {
                boxX = node.X + node.Width + 2;
                boxY = node.Y - 1;
                boxW = 30;
            }
            else
            {
                double stripTop = node.Y + node.Height - 16;
                boxX = node.X + 2;
                boxY = stripTop + 1;
                boxW = node.Width / 2 - 4;
            }

            var box = new TextBox
            {
                Text = oldNumber.ToString(),
                FontSize = 9,
                Width = Math.Max(boxW, 28),
                Height = 15,
                Padding = new Thickness(1),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(232, 101, 10)),
                Background = new SolidColorBrush(Color.FromRgb(235, 245, 251)),
                FontFamily = new System.Windows.Media.FontFamily("Arial"),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            Canvas.SetLeft(box, boxX);
            Canvas.SetTop(box, boxY);
            DrawingCanvas.Children.Add(box);
            box.Focus(); box.SelectAll();

            bool committed = false;
            Action commit = () =>
            {
                if (committed) return;
                committed = true;
                DrawingCanvas.Children.Remove(box);

                // Проверяем: целое число > 0
                if (!int.TryParse(box.Text.Trim(), out int newNumber) || newNumber <= 0)
                { SetStatus("Номер должен быть целым числом больше нуля."); return; }

                if (newNumber == oldNumber) return;

                // Предупреждаем о конфликте, но разрешаем (пользователь сам разберётся)
                bool conflict = _nodes.Any(n => n != node && n.IsJunction == node.IsJunction
                                                          && n.Number == newNumber);
                if (conflict)
                {
                    var result = MessageBox.Show(
                        $"Номер {newNumber} уже занят другим блоком. Всё равно присвоить?",
                        "Конфликт номеров", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (result != MessageBoxResult.Yes) return;
                }

                _history.Execute(new ChangeNumberCommand(node, oldNumber, newNumber));
                RefreshUndoRedoUI();
                SetStatus($"Номер блока изменён: {(node.IsJunction ? "J" : "")}{newNumber}");
            };

            box.LostFocus += (_, __) => commit();
            box.KeyDown += (_, args) =>
            {
                if (args.Key == Key.Enter) commit();
                if (args.Key == Key.Escape) DrawingCanvas.Children.Remove(box);
            };
        }

        // ═════════════════════════════════════════════════════════════
        // КОПИРОВАНИЕ / ВСТАВКА

        private void CopySelected()
        {
            _clipboardNodes.Clear();
            _clipboardLinks.Clear();

            var toCopy = _selectedNodes.Count > 0
                ? new List<Node>(_selectedNodes)
                : _selectedNode != null ? new List<Node> { _selectedNode } : new List<Node>();

            if (toCopy.Count == 0) { SetStatus("Нет выбранных элементов для копирования."); return; }

            _clipboardNodes.AddRange(toCopy);

            // Копируем связи, у которых оба конца в выбранных блоках
            var ids = new HashSet<string>(toCopy.Select(n => n.Id));
            foreach (var l in _links)
                if (ids.Contains(l.SourceId) && ids.Contains(l.TargetId))
                    _clipboardLinks.Add(l);

            SetStatus($"Скопировано: {toCopy.Count} блок(ов)" +
                      (_clipboardLinks.Count > 0 ? $", {_clipboardLinks.Count} связ(ей)" : ""));
        }

        private void PasteFromClipboard()
        {
            if (_clipboardNodes.Count == 0) { SetStatus("Буфер обмена пуст."); return; }
            const double offset = 20;

            var idMap = new Dictionary<string, string>();
            var newNodes = new List<Node>();

            foreach (var orig in _clipboardNodes)
            {
                var n = new Node
                {
                    Type = orig.Type,
                    Text = orig.Text,
                    Width = orig.Width,
                    Height = orig.Height,
                    X = orig.X + offset,
                    Y = orig.Y + offset,
                    Number = orig.IsJunction ? NextJunctionNumber() : NextUobNumber(),
                    ReferenceExpression = orig.ReferenceExpression
                };
                n.CreateVisual();
                n.UpdatePosition();
                idMap[orig.Id] = n.Id;
                newNodes.Add(n);
            }

            var newLinks = new List<Link>();
            foreach (var orig in _clipboardLinks)
            {
                if (!idMap.ContainsKey(orig.SourceId) || !idMap.ContainsKey(orig.TargetId)) continue;
                var l = new Link
                {
                    SourceId = idMap[orig.SourceId],
                    TargetId = idMap[orig.TargetId],
                    Type = orig.Type,
                    Label = orig.Label
                };
                l.CreateVisual();
                newLinks.Add(l);
            }

            _history.Execute(new PasteCommand(newNodes, newLinks, _mediator));
            RefreshAllLinks();
            UpdateStatus();

            DeselectAll();
            if (newNodes.Count == 1) SelectNode(newNodes[0]);
            else { foreach (var n in newNodes) { _selectedNodes.Add(n); n.Highlight(true); } }

            SetStatus($"Вставлено: {newNodes.Count} блок(ов)");
        }

        // ─── Редактирование ссылочного выражения ──────────────────────

        private void EditReferenceExpression(Node node)
        {
            if (node.IsJunction) return;
            string oldRef = node.ReferenceExpression ?? "";
            double stripTop = node.Y + node.Height - 16;

            var box = new TextBox
            {
                Text = oldRef,
                FontSize = 11,
                Width = node.Width / 2 - 4,
                Height = 15,
                Padding = new Thickness(1),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(232, 101, 10)),
                Background = new SolidColorBrush(Color.FromRgb(235, 245, 251)),
                FontFamily = new System.Windows.Media.FontFamily("Arial"),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            Canvas.SetLeft(box, node.X + node.Width / 2 + 2);
            Canvas.SetTop(box, stripTop + 1);
            DrawingCanvas.Children.Add(box);
            box.Focus(); box.SelectAll();

            bool committed = false;
            Action commit = () =>
            {
                if (committed) return;
                committed = true;
                DrawingCanvas.Children.Remove(box);
                string newRef = box.Text.Trim();
                if (newRef == oldRef) return;
                _history.Execute(new ChangeReferenceCommand(node, oldRef, newRef));
                RefreshUndoRedoUI();
                SetStatus($"Ссылка обновлена: «{newRef}»");
            };
            box.LostFocus += (_, __) => commit();
            box.KeyDown += (_, a) =>
            {
                if (a.Key == Key.Enter) commit();
                if (a.Key == Key.Escape) DrawingCanvas.Children.Remove(box);
            };
        }

        // ─── Редактирование подписи к связи ───────────────────────────

        private void EditLinkLabel(Link link)
        {
            if (link.PathElement?.Data == null) return;

            // Получаем примерную позицию середины линии через BoundingBox
            var bounds = link.PathElement.Data.Bounds;
            double cx = bounds.Left + bounds.Width / 2;
            double cy = bounds.Top + bounds.Height / 2;

            var box = new TextBox
            {
                Text = link.Label ?? "",
                FontSize = 10,
                Width = 120,
                Padding = new Thickness(2),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(232, 101, 10)),
                Background = new SolidColorBrush(Color.FromRgb(235, 245, 251)),
                FontFamily = new System.Windows.Media.FontFamily("Arial")
            };
            Canvas.SetLeft(box, cx - 60);
            Canvas.SetTop(box, cy - 14);
            DrawingCanvas.Children.Add(box);
            box.Focus(); box.SelectAll();

            bool committed = false;
            string oldLabel = link.Label ?? "";
            Action commit = () =>
            {
                if (committed) return;
                committed = true;
                DrawingCanvas.Children.Remove(box);
                string newLabel = box.Text.Trim();
                if (newLabel == oldLabel) return;
                _history.Execute(new ChangeLinkLabelCommand(link, oldLabel, newLabel));
                RefreshUndoRedoUI();
                // Обновить позицию метки
                Node src = _nodes.FirstOrDefault(n => n.Id == link.SourceId);
                Node tgt = _nodes.FirstOrDefault(n => n.Id == link.TargetId);
                if (src != null && tgt != null) link.UpdatePosition(src, tgt, _nodes, _links);
                SetStatus(string.IsNullOrEmpty(newLabel) ? "Подпись удалена" : $"Подпись: «{newLabel}»");
            };
            box.LostFocus += (_, __) => commit();
            box.KeyDown += (_, a) =>
            {
                if (a.Key == Key.Enter) commit();
                if (a.Key == Key.Escape) DrawingCanvas.Children.Remove(box);
            };
        }

        // ═════════════════════════════════════════════════════════════
        // ДЕКОМПОЗИЦИЯ
        // ═════════════════════════════════════════════════════════════

        private List<SaveChildDiagram> SerializeDecompositions()
        {
            if (_decompositions.Count == 0) return null;
            var list = new List<SaveChildDiagram>();
            foreach (var kv in _decompositions)
            {
                var level = kv.Value;
                list.Add(new SaveChildDiagram
                {
                    Id = level.LevelId,
                    ParentBlockId = kv.Key,
                    Name = level.Name,
                    Nodes = level.Nodes.Select(n => new SaveNode
                    {
                        Id = n.Id,
                        Type = n.Type,
                        X = n.X,
                        Y = n.Y,
                        Width = n.Width,
                        Height = n.Height,
                        Text = n.Text,
                        Number = n.Number,
                        ReferenceExpression = n.ReferenceExpression
                    }).ToList(),
                    Links = level.Links.Select(l => new SaveLink
                    {
                        Id = l.Id,
                        SourceId = l.SourceId,
                        TargetId = l.TargetId,
                        Type = l.Type,
                        Label = l.Label,
                        Points = new List<Point>(l.UserPoints)
                    }).ToList()
                });
            }
            return list;
        }

        private void RestoreDecompositions(List<SaveChildDiagram> saved)
        {
            foreach (var sd in saved)
            {
                var level = new DiagramLevel
                {
                    LevelId = sd.Id,
                    Name = sd.Name,
                    ParentBlockId = sd.ParentBlockId
                };
                foreach (var sn in sd.Nodes)
                {
                    var node = Node.CreateForLoad();
                    node.Id = sn.Id; node.Type = sn.Type;
                    node.X = sn.X; node.Y = sn.Y;
                    node.Width = sn.Width; node.Height = sn.Height;
                    node.Text = sn.Text; node.Number = sn.Number;
                    node.ReferenceExpression = sn.ReferenceExpression ?? "";
                    node.CreateVisual(); node.UpdatePosition();
                    level.Nodes.Add(node);
                }
                foreach (var sl in sd.Links)
                {
                    var link = new Link
                    {
                        Id = sl.Id,
                        SourceId = sl.SourceId,
                        TargetId = sl.TargetId,
                        Type = sl.Type ?? "precedence",
                        Label = sl.Label ?? "",
                        UserPoints = sl.Points != null ? new List<Point>(sl.Points) : new List<Point>()
                    };
                    link.CreateVisual();
                    level.Links.Add(link);
                }
                _decompositions[sd.ParentBlockId] = level;
            }
        }

        // Входит в декомпозицию блока: сохраняет текущий уровень в стек,
        // очищает холст и показывает содержимое дочернего уровня.
        // Если декомпозиции ещё нет — создаёт пустую.
        private void EnterDecomposition(Node block)
        {
            DeselectAll();

            // 1. Сохраняем текущий уровень в стек навигации
            _navStack.Add(new NavEntry
            {
                BlockId = block.Id,
                BlockName = block.Text,
                // Имя уровня = «A1 Текст блока» (ссылка + название)
                Reference = BuildLevelName(block.ReferenceExpression, block.Text),
                Nodes = new List<Node>(_nodes),
                Links = new List<Link>(_links),
                History = _history,
                Zoom = _zoom,
                PanX = CanvasPan.X,
                PanY = CanvasPan.Y
            });

            // 2. Убираем визуалы текущего уровня с холста
            foreach (var n in _nodes) _mediator.RemoveNodeVisualsOnly(n);
            foreach (var l in _links) _mediator.RemoveLinkVisualsOnly(l);

            // 3. Очищаем активные списки
            _nodes.Clear();
            _links.Clear();

            // 4. Получаем или создаём дочерний уровень
            if (!_decompositions.TryGetValue(block.Id, out DiagramLevel child))
            {
                child = new DiagramLevel
                {
                    Name = block.Text,
                    ParentBlockId = block.Id
                };
                _decompositions[block.Id] = child;
            }

            // 5. Загружаем дочерний уровень
            _history = child.History;
            foreach (var n in child.Nodes) { _nodes.Add(n); _mediator.AddNodeVisualsOnly(n); }
            foreach (var l in child.Links) { _links.Add(l); _mediator.AddLinkVisualsOnly(l); }

            // 6. Восстанавливаем вид
            _zoom = child.Zoom > 0 ? child.Zoom : 1.0;
            CanvasScale.ScaleX = CanvasScale.ScaleY = _zoom;
            CanvasPan.X = child.PanX;
            CanvasPan.Y = child.PanY;
            ZoomLabel.Text = $"{(int)Math.Round(_zoom * 100)}%";

            // 7. Обновляем UI
            RefreshAllLinks();
            RefreshUndoRedoUI();
            UpdateBreadcrumb();
            UpdateStatus();
            SetStatus($"Декомпозиция: «{block.Text}»");
        }

        // Возвращается на уровень выше, сохраняя состояние текущего уровня.
        private void NavigateBack()
        {
            if (_navStack.Count == 0) return;
            DeselectAll();

            // 1. Сохраняем текущий дочерний уровень
            var current = _navStack[_navStack.Count - 1];
            if (_decompositions.TryGetValue(current.BlockId, out DiagramLevel child))
            {
                child.Nodes = new List<Node>(_nodes);
                child.Links = new List<Link>(_links);
                child.History = _history;
                child.Zoom = _zoom;
                child.PanX = CanvasPan.X;
                child.PanY = CanvasPan.Y;
            }

            // 2. Убираем визуалы дочернего уровня
            foreach (var n in _nodes) _mediator.RemoveNodeVisualsOnly(n);
            foreach (var l in _links) _mediator.RemoveLinkVisualsOnly(l);

            // 3. Восстанавливаем родительский уровень
            _nodes.Clear();
            _links.Clear();
            _navStack.RemoveAt(_navStack.Count - 1);

            foreach (var n in current.Nodes) { _nodes.Add(n); _mediator.AddNodeVisualsOnly(n); }
            foreach (var l in current.Links) { _links.Add(l); _mediator.AddLinkVisualsOnly(l); }

            // 4. Восстанавливаем историю и вид
            _history = current.History;
            _zoom = current.Zoom;
            CanvasScale.ScaleX = CanvasScale.ScaleY = _zoom;
            CanvasPan.X = current.PanX;
            CanvasPan.Y = current.PanY;
            ZoomLabel.Text = $"{(int)Math.Round(_zoom * 100)}%";

            RefreshAllLinks();
            RefreshUndoRedoUI();
            UpdateBreadcrumb();
            UpdateStatus();
            SetStatus("Вернулись на уровень выше");
        }

        // При СОЗДАНИИ декомпозиции сначала показывает встроенное поле ввода
        // ссылочного выражения прямо в правой ячейке нижней полосы блока.
        // После подтверждения (Enter / потеря фокуса) сохраняет ссылку и входит в декомпозицию.
        // Escape — отмена без создания декомпозиции.
        private void CreateDecompositionWithReference(Node node)
        {
            if (node.IsJunction) return;
            string oldRef = node.ReferenceExpression ?? "";
            double stripTop = node.Y + node.Height - 16;

            var box = new TextBox
            {
                Text = oldRef,
                FontSize = 11,
                Width = node.Width / 2 - 4,
                Height = 15,
                Padding = new Thickness(1),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(232, 101, 10)),
                Background = new SolidColorBrush(Color.FromRgb(255, 255, 210)),
                FontFamily = new System.Windows.Media.FontFamily("Arial"),
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Введите ссылочное выражение (напр. A3) и нажмите Enter"
            };
            Canvas.SetLeft(box, node.X + node.Width / 2 + 2);
            Canvas.SetTop(box, stripTop + 1);
            DrawingCanvas.Children.Add(box);
            box.Focus(); box.SelectAll();

            bool committed = false;
            Action<bool> finish = (enter) =>
            {
                if (committed) return;
                committed = true;

                if (DrawingCanvas.Children.Contains(box))
                    DrawingCanvas.Children.Remove(box);

                if (!enter) return; // Escape — отменяем, декомпозиция не создаётся

                // Сохраняем ссылку (если введена)
                string newRef = box.Text.Trim();
                if (!string.IsNullOrEmpty(newRef) && newRef != oldRef)
                {
                    _history.Execute(new ChangeReferenceCommand(node, oldRef, newRef));
                    RefreshUndoRedoUI();
                }

                // Входим в декомпозицию
                EnterDecomposition(node);
            };

            box.LostFocus += (_, __) => finish(true);
            box.KeyDown += (_, a) =>
            {
                if (a.Key == Key.Enter) { a.Handled = true; finish(true); }
                if (a.Key == Key.Escape) { a.Handled = true; finish(false); }
            };
        }

        // Удаляет декомпозицию блока после подтверждения.
        private void DeleteDecomposition(Node block)
        {
            if (!_decompositions.ContainsKey(block.Id)) return;
            var res = MessageBox.Show(
                $"Удалить всю декомпозицию блока «{block.Text}»?\nЭто действие нельзя отменить.",
                "Удаление декомпозиции", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res != MessageBoxResult.Yes) return;
            _decompositions.Remove(block.Id);
            SetStatus($"Декомпозиция «{block.Text}» удалена");
        }

        // Обновляет панель хлебных крошек:
        // [🏠 Главная] › [Блок А] › [Блок А.1] › ...
        private void UpdateBreadcrumb()
        {
            BreadcrumbItems.Items.Clear();

            if (_navStack.Count == 0)
            {
                BreadcrumbPanel.Visibility = Visibility.Collapsed;
                UpdateTitleBar();
                return;
            }

            BreadcrumbPanel.Visibility = Visibility.Visible;

            // Корень — A0 + название диаграммы
            var root = MakeCrumb(BuildLevelName("A0", _diagramName), -1);
            BreadcrumbItems.Items.Add(root);

            // Промежуточные уровни — показываем ссылочное выражение (A1, A1.2 …)
            for (int i = 0; i < _navStack.Count; i++)
            {
                BreadcrumbItems.Items.Add(MakeSeparator());
                string label = _navStack[i].Reference ?? _navStack[i].BlockName;
                int idx = i;
                var crumb = MakeCrumb(label, idx);
                BreadcrumbItems.Items.Add(crumb);
            }

            UpdateTitleBar();
        }

        // Возвращает строку текущего уровня для заголовка окна (A0, A1, A1.2 …).
        private string CurrentLevelLabel()
        {
            if (_navStack.Count == 0) return BuildLevelName("A0", _diagramName);
            var last = _navStack[_navStack.Count - 1];
            return last.Reference ?? last.BlockName;
        }

        // Формирует имя уровня: «A1 Текст блока».
        // Если ссылка пустая — только текст; если текст пустой — только ссылка.
        private static string BuildLevelName(string reference, string name)
        {
            string r = (reference ?? "").Trim();
            string n = (name ?? "").Trim();
            if (string.IsNullOrEmpty(r)) return n;
            if (string.IsNullOrEmpty(n)) return r;
            return $"{r} {n}";
        }

        private System.Windows.Controls.Button MakeCrumb(string text, int navIndex)
        {
            var btn = new System.Windows.Controls.Button
            {
                Content = text,
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                Padding = new Thickness(4, 1, 4, 1)
            };
            btn.Click += (_, __) =>
            {
                // stepsBack: Home(navIndex=-1) → Count шагов; крошка i → Count-1-i шагов
                int stepsBack = _navStack.Count - 1 - navIndex;
                for (int i = 0; i < stepsBack; i++) NavigateBack();
            };
            return btn;
        }

        private System.Windows.Controls.TextBlock MakeSeparator()
            => new System.Windows.Controls.TextBlock
            {
                Text = " › ",
                Foreground = new System.Windows.Media.SolidColorBrush(
                                 System.Windows.Media.Color.FromRgb(160, 160, 160)),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };

        // МАСШТАБ
        // ═════════════════════════════════════════════════════════════

        private void SetZoom(double z)
            => ZoomAround(new Point(CanvasScroll.ViewportWidth / 2, CanvasScroll.ViewportHeight / 2), z);

        // Сбрасывает масштаб до 100 % и перемещает вид в начало холста (0, 0).
        private void GoHome()
        {
            _zoom = 1.0;
            CanvasScale.ScaleX = CanvasScale.ScaleY = 1.0;
            CanvasPan.X = 0;
            CanvasPan.Y = 0;
            ZoomLabel.Text = "100%";
            SetStatus("Вид сброшен в начало холста");
        }

        private void ZoomAround(Point pivot, double newZoom)
        {
            newZoom = Math.Max(ZoomMin, Math.Min(ZoomMax, newZoom));
            if (Math.Abs(newZoom - _zoom) < 0.001) return;
            double ratio = newZoom / _zoom;
            // Масштабируем относительно точки под курсором
            CanvasPan.X = pivot.X - ratio * (pivot.X - CanvasPan.X);
            CanvasPan.Y = pivot.Y - ratio * (pivot.Y - CanvasPan.Y);
            _zoom = newZoom;
            CanvasScale.ScaleX = CanvasScale.ScaleY = _zoom;
            ZoomLabel.Text = $"{(int)Math.Round(_zoom * 100)}%";
        }

        // ═════════════════════════════════════════════════════════════
        // ОБНОВЛЕНИЕ СВЯЗЕЙ
        // ═════════════════════════════════════════════════════════════

        private void RefreshLinksForNode(Node node)
        {
            foreach (var l in _links)
                if (l.SourceId == node.Id || l.TargetId == node.Id) _mediator.RefreshLink(l);
        }

        private void RefreshAllLinks()
        {
            foreach (var l in _links) _mediator.RefreshLink(l);
        }

        // ═════════════════════════════════════════════════════════════
        // СЕТКА
        // ═════════════════════════════════════════════════════════════

        private void DrawGrid()
        {
            // Сетка теперь — DrawingBrush в XAML, перерисовывать не нужно.
            // Метод оставлен для совместимости с вызовами из других мест.
            if (GridCanvas == null) return;
            GridCanvas.Visibility = _showGrid ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ToggleGrid(object sender, RoutedEventArgs e)
        {
            _showGrid = !_showGrid;
            ToggleGridButton.Content = _showGrid ? "Сетка" : "Сетка скрыта";
            GridCanvas.Visibility = _showGrid ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ToggleSnap(object sender, RoutedEventArgs e)
        {
            _snapToGrid = !_snapToGrid;
            ToggleSnapButton.Content = _snapToGrid ? "Привязка" : "Без привязки";
        }

        private double SnapToGrid(double v)
            => _snapToGrid ? Math.Round(v / GridStep) * GridStep : v;

        // ═════════════════════════════════════════════════════════════
        // КЛАВИАТУРА
        // ═════════════════════════════════════════════════════════════

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete) { ExecuteDelete(); e.Handled = true; }
            if (e.Key == Key.Escape)
            {
                if (_isLinkMode) CancelLinkMode();
                if (_isPlacementMode) CancelPlacementMode();
                if (_isRubberBanding)
                {
                    _isRubberBanding = false;
                    RubberBandRect.Visibility = Visibility.Collapsed;
                    DrawingCanvas.ReleaseMouseCapture();
                }
                DeselectAll();
                e.Handled = true;
            }
        }

        // ═════════════════════════════════════════════════════════════
        // СОХРАНЕНИЕ / ЗАГРУЗКА
        // ═════════════════════════════════════════════════════════════

        private static string DefaultDiagramDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "Диаграммы");
        private void TrySave()
        {
            var dlg = new SaveFileDialog
            {
                Filter = "IDEF3 диаграмма (*.idef3)|*.idef3|Все файлы (*.*)|*.*",
                DefaultExt = "idef3",
                FileName = _diagramName.Replace(" ", "_") + ".idef3",
                InitialDirectory = DefaultDiagramDir

            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var diagram = new SaveDiagram
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = _diagramName,
                    Description = _diagramDescription,
                    Created = _diagramCreated,
                    Modified = DateTime.Now,
                    Author = _diagramAuthor,
                    Scale = _zoom,
                    Nodes = _nodes.Select(n => new SaveNode
                    {
                        Id = n.Id,
                        Type = n.Type,
                        X = n.X,
                        Y = n.Y,
                        Width = n.Width,
                        Height = n.Height,
                        Text = n.Text,
                        Number = n.Number,
                        ReferenceExpression = n.ReferenceExpression
                    }).ToList(),
                    Links = _links.Select(l => new SaveLink
                    {
                        Id = l.Id,
                        SourceId = l.SourceId,
                        TargetId = l.TargetId,
                        Type = l.Type,
                        Label = l.Label,
                        Points = new List<Point>(l.UserPoints)
                    }).ToList(),
                    Decompositions = SerializeDecompositions()
                };
                File.WriteAllText(dlg.FileName, JsonConvert.SerializeObject(diagram, Formatting.Indented));
                AddToRecentFiles(dlg.FileName);
                _saveOrNot = false;
                SetStatus($"Сохранено: {System.IO.Path.GetFileName(dlg.FileName)}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при сохранении:\n{ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TryLoad()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "IDEF3 диаграмма (*.idef3)|*.idef3|Все файлы (*.*)|*.*",
                InitialDirectory = DefaultDiagramDir
            };
            if (dlg.ShowDialog() != true) return;
            TryLoadFile(dlg.FileName);
        }

        private void TryLoadFile(string fileName)
        {
            if (!File.Exists(fileName))
            {
                MessageBox.Show($"Файл не найден:\n{fileName}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                _recentFiles.Remove(fileName); SaveRecentFiles();
                return;
            }
            try
            {
                var diagram = JsonConvert.DeserializeObject<SaveDiagram>(File.ReadAllText(fileName));
                if (diagram == null)
                { MessageBox.Show("Файл повреждён.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error); return; }

                ClearAll();

                _diagramName = diagram.Name ?? "Без названия";
                _diagramAuthor = diagram.Author ?? "";
                _diagramDescription = diagram.Description ?? "";
                _diagramCreated = diagram.Created;
                UpdateTitleBar();

                // Восстанавливаем блоки
                foreach (var sn in diagram.Nodes)
                {
                    var node = Node.CreateForLoad();
                    node.Id = sn.Id; node.Type = sn.Type; node.X = sn.X; node.Y = sn.Y;
                    node.Width = sn.Width; node.Height = sn.Height;
                    node.Text = sn.Text; node.Number = sn.Number;
                    node.ReferenceExpression = sn.ReferenceExpression ?? "";
                    node.CreateVisual(); node.UpdatePosition();
                    _mediator.AddNodeToCanvas(node);
                }
                // Счётчик нумерации не нужно синхронизировать вручную:
                // NextUobNumber() / NextJunctionNumber() динамически сканируют список _nodes.

                // Восстанавливаем связи
                foreach (var sl in diagram.Links)
                {
                    var link = new Link
                    {
                        Id = sl.Id,
                        SourceId = sl.SourceId,
                        TargetId = sl.TargetId,
                        Type = sl.Type ?? "precedence",
                        Label = sl.Label ?? "",
                        UserPoints = sl.Points != null ? new List<Point>(sl.Points) : new List<Point>()
                    };
                    link.CreateVisual();
                    _mediator.AddLinkToCanvas(link);
                }

                if (diagram.Scale > 0) SetZoom(diagram.Scale);
                // Восстанавливаем декомпозиции
                if (diagram.Decompositions != null)
                    RestoreDecompositions(diagram.Decompositions);
                RefreshAllLinks(); DrawGrid();
                AddToRecentFiles(fileName);
                _saveOrNot = false;
                SetStatus($"Загружено: {System.IO.Path.GetFileName(fileName)}");
                UpdateStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при загрузке:\n{ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ═════════════════════════════════════════════════════════════
        // СТРОКА СОСТОЯНИЯ
        // ═════════════════════════════════════════════════════════════

        private void SetStatus(string msg) => StatusText.Text = msg;

        private void UpdateStatus()
        {
            NodeCountText.Text = $"Блоков: {_nodes.Count}";
            LinkCountText.Text = $"Связей: {_links.Count}";
        }

        // ═════════════════════════════════════════════════════════════
        // ЗАКРЫТИЕ ОКНА
        // ═════════════════════════════════════════════════════════════

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_saveOrNot)
            {
                var dialog = new UnsavedDiagram { Owner = this };
                dialog.ShowDialog();

                // Крестик / Esc — пользователь передумал, отменяем закрытие
                if (dialog.Result == UnsavedDiagram.Choice.Cancel)
                {
                    e.Cancel = true;
                    return;
                }

                // «Да» — сохранить перед выходом
                if (dialog.Result == UnsavedDiagram.Choice.Save)
                {
                    TrySave();
                    if (_saveOrNot) { e.Cancel = true; return; }
                }
                // «Нет» — выйти без сохранения (идём дальше к base.OnClosing)
            }
            base.OnClosing(e);


        }

        // ─── Реализация ICommand для привязки горячих клавиш ─────────────
        internal class RelayCommand : ICommand
        {
            private readonly Action<object> _execute;
            public RelayCommand(Action<object> execute) => _execute = execute;
            public bool CanExecute(object p) => true;
            public void Execute(object p) => _execute(p);
            public event EventHandler CanExecuteChanged
            {
                add { CommandManager.RequerySuggested += value; }
                remove { CommandManager.RequerySuggested -= value; }
            }
        }
    }
}
