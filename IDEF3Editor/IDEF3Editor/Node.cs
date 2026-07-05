using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace IDEF3Editor
{
    // Один элемент диаграммы IDEF3 — либо UOB-блок (единица поведения),
    // либо перекрёсток (Junction). Содержит данные и все визуальные элементы WPF.
    public class Node
    {
        // ─── Данные ───────────────────────────────────────────────────
        public string Id     { get; set; } = Guid.NewGuid().ToString();
        public string Type   { get; set; } = "uob"; // "uob", "junction-and", "junction-or" и т.д.
        public double X      { get; set; }
        public double Y      { get; set; }
        public double Width  { get; set; } = 120;
        public double Height { get; set; } = 60;
        public string Text                { get; set; } = "Новый блок";
        public int    Number              { get; set; }
        public string ReferenceExpression { get; set; } = ""; // правая ячейка нижней полосы

        // ─── Визуальные элементы ──────────────────────────────────────
        public Shape     MainShape    { get; set; }  // основной прямоугольник (UOB или перекрёсток)
        public Line      JLineRight   { get; set; }  // правая вертикальная линия (синхронные перекрёстки)
        public Line      JLineLeft    { get; set; }  // левая вертикальная линия (все типы перекрёстков)
        public Line      BottomLine   { get; set; }  // горизонтальный разделитель UOB
        public Line      StripDivider { get; set; }  // вертикальный разделитель нижней полосы
        public TextBlock TextBlock    { get; set; }  // название / символ перекрёстка
        public TextBlock NumberText    { get; set; }  // числовой номер
        public TextBlock RefText       { get; set; }  // ссылочное выражение (правая ячейка)
        public Rectangle ResizeHandle  { get; set; }  // ручка изменения размера (только UOB)

        // ─── Цвета ────────────────────────────────────────────────────
        private static readonly SolidColorBrush BlackBrush    = new SolidColorBrush(Color.FromRgb(20, 20, 20));
        private static readonly Color            SelectedColor = Color.FromRgb(232, 101, 10); // оранжевый

        // Номер присваивается снаружи (MainWindow знает все занятые номера)
        public Node() { }
        public static Node CreateForLoad() => new Node();

        // ─── Вспомогательные свойства ─────────────────────────────────

        // Возвращает true для любого типа перекрёстка.
        public bool IsJunction =>
            Type == "junction-and"      || Type == "junction-or"      ||
            Type == "junction-xor"      || Type == "junction-and-sync" ||
            Type == "junction-or-sync";

        // Возвращает true только для синхронных перекрёстков (двойная полоса).
        public bool IsSyncJunction =>
            Type == "junction-and-sync" || Type == "junction-or-sync";

        public double    RadiusX  => 0; // все блоки имеют острые углы
        public Rectangle Rectangle => MainShape as Rectangle;

        // Константы компоновки
        private const double StripH       = 16; // высота нижней полосы UOB
        private const double JunctionSize = 44; // сторона квадрата перекрёстка
        private const double JLineOffset  = 7;  // отступ внутренних линий от краёв квадрата

        // ═════════════════════════════════════════════════════════════
        // СОЗДАНИЕ ВИЗУАЛА
        // ═════════════════════════════════════════════════════════════

        public void CreateVisual()
        {
            if (IsJunction) CreateJunctionVisual();
            else            CreateUobVisual();

            ResizeHandle = new Rectangle
            {
                Width = 10, Height = 10,
                Fill = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
                Stroke = Brushes.White, StrokeThickness = 1,
                RadiusX = 2, RadiusY = 2,
                Cursor     = System.Windows.Input.Cursors.SizeNWSE,
                Tag        = this,
                Visibility = IsJunction ? Visibility.Collapsed : Visibility.Visible
            };
            UpdatePosition();
        }

        private void CreateUobVisual()
        {
            // Прямоугольник с острыми углами 
            MainShape = new Rectangle
            {
                Width = Width, Height = Height,
                Fill = Brushes.White, Stroke = BlackBrush,
                StrokeThickness = 1.5, RadiusX = 0, RadiusY = 0
            };

            // Горизонтальная линия над нижней полосой
            BottomLine = new Line { Stroke = BlackBrush, StrokeThickness = 1.5 };

            // Вертикальная линия, делящая нижнюю полосу пополам
            StripDivider = new Line { Stroke = BlackBrush, StrokeThickness = 1.5 };

            // Название блока всегда выводится заглавными буквами (стандарт IDEF3)
            TextBlock = new TextBlock
            {
                Text          = Text.ToUpper(),
                FontSize      = 11,
                FontWeight    = FontWeights.Bold,
                Foreground    = BlackBrush,
                TextAlignment = TextAlignment.Center,
                TextWrapping  = TextWrapping.Wrap,
                FontFamily    = new FontFamily("Arial")
            };

            // Номер — в левой ячейке нижней полосы
            NumberText = new TextBlock
            {
                Text       = Number.ToString(),
                FontSize   = 12,
                Foreground = BlackBrush,
                FontFamily = new FontFamily("Arial")
            };

            // Ссылочное выражение — в правой ячейке нижней полосы
            RefText = new TextBlock
            {
                Text          = ReferenceExpression,
                FontSize      = 11,
                Foreground    = BlackBrush,
                FontFamily    = new FontFamily("Arial"),
                TextAlignment = TextAlignment.Center
            };
        }

        private void CreateJunctionVisual()
        {
            Width = JunctionSize; Height = JunctionSize;

            // Квадрат с острыми углами
            MainShape = new Rectangle
            {
                Width = Width, Height = Height,
                Fill = Brushes.White, Stroke = BlackBrush,
                StrokeThickness = 1.5, RadiusX = 0, RadiusY = 0
            };

            // Левая вертикальная линия — у всех типов перекрёстков
            JLineLeft = new Line { Stroke = BlackBrush, StrokeThickness = 1.5 };

            // Правая вертикальная линия — только у синхронных (AND sync, OR sync)
            if (IsSyncJunction)
                JLineRight = new Line { Stroke = BlackBrush, StrokeThickness = 1.5 };

            // Метка «Jn» — выводится справа сверху от квадрата
            NumberText = new TextBlock
            {
                Text       = "J" + Number.ToString(),
                FontSize   = 8,
                Foreground = BlackBrush,
                FontFamily = new FontFamily("Arial")
            };

            // Символ внутри квадрата
            string sym;
            switch (Type)
            {
                case "junction-and": case "junction-and-sync": sym = "&"; break;
                case "junction-or":  case "junction-or-sync":  sym = "O"; break;
                default:                                        sym = "X"; break;
            }
            TextBlock = new TextBlock
            {
                Text          = sym,
                FontSize      = 16,
                FontWeight    = FontWeights.Bold,
                Foreground    = BlackBrush,
                TextAlignment = TextAlignment.Center,
                Width         = Width,
                FontFamily    = new FontFamily("Arial")
            };
        }

        // ═════════════════════════════════════════════════════════════
        // РАЗМЕР И ПОЗИЦИЯ
        // ═════════════════════════════════════════════════════════════

        // Подбирает минимальный размер блока под его текст.
        // Вызывается при создании UOB-блока до добавления на холст.
        public void AutoSize()
        {
            if (TextBlock == null || MainShape == null || IsJunction) return;
            TextBlock.Text = Text.ToUpper();
            TextBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size s  = TextBlock.DesiredSize;
            double cx = X + Width / 2, cy = Y + Height / 2;
            Width  = Math.Max(s.Width + 24, 100);
            Height = Math.Max(s.Height + StripH + 20, 60);
            X = cx - Width / 2; Y = cy - Height / 2;
            MainShape.Width  = Width;
            MainShape.Height = Height;
            UpdatePosition();
        }

        // Применяет координаты X, Y, Width, Height ко всем визуальным элементам.
        // Вызывается после любого изменения позиции или размера.
        // Важно: также вызывается из DiagramMediator.AddNodeToCanvas, поскольку
        // при удалении блока с холста Canvas.SetLeft/SetTop сбрасываются.
        public void UpdatePosition()
        {
            if (MainShape == null) return;
            Canvas.SetLeft(MainShape, X);
            Canvas.SetTop(MainShape,  Y);

            if (IsJunction)
            {
                // Левая линия — от верхнего до нижнего края квадрата (без отступа по вертикали)
                if (JLineLeft != null)
                {
                    JLineLeft.X1 = X + JLineOffset; JLineLeft.Y1 = Y;
                    JLineLeft.X2 = X + JLineOffset; JLineLeft.Y2 = Y + Height;
                }
                // Правая линия — только у синхронных
                if (JLineRight != null)
                {
                    JLineRight.X1 = X + Width - JLineOffset; JLineRight.Y1 = Y;
                    JLineRight.X2 = X + Width - JLineOffset; JLineRight.Y2 = Y + Height;
                }
                Canvas.SetLeft(TextBlock,  X);
                Canvas.SetTop(TextBlock,   Y + Height / 2 - 13);
                Canvas.SetLeft(NumberText, X + Width + 2);
                Canvas.SetTop(NumberText,  Y - 1);
            }
            else
            {
                double textAreaH = Height - StripH;
                double midX      = Width / 2;

                if (BottomLine != null)
                {
                    BottomLine.X1 = X;         BottomLine.Y1 = Y + textAreaH;
                    BottomLine.X2 = X + Width; BottomLine.Y2 = Y + textAreaH;
                }
                if (StripDivider != null)
                {
                    StripDivider.X1 = X + midX; StripDivider.Y1 = Y + textAreaH;
                    StripDivider.X2 = X + midX; StripDivider.Y2 = Y + Height;
                }
                TextBlock.Width = Width - 8;
                Canvas.SetLeft(TextBlock,  X + 4);
                Canvas.SetTop(TextBlock,   Y + 6);
                Canvas.SetLeft(NumberText, X + 4);
                Canvas.SetTop(NumberText,  Y + textAreaH + 3);
                if (RefText != null)
                {
                    RefText.Width = Width / 2 - 4;
                    Canvas.SetLeft(RefText, X + midX + 2);
                    Canvas.SetTop(RefText,  Y + textAreaH + 3);
                }
                if (ResizeHandle != null)
                {
                    Canvas.SetLeft(ResizeHandle, X + Width  - 6);
                    Canvas.SetTop(ResizeHandle,  Y + Height - 6);
                }
            }
        }

        // ═════════════════════════════════════════════════════════════
        // ОБРАТНАЯ СВЯЗЬ
        // ═════════════════════════════════════════════════════════════

        // Выделяет блок оранжевым цветом или снимает выделение.
        public void Highlight(bool isSelected)
        {
            if (MainShape == null) return;
            var stroke   = isSelected ? new SolidColorBrush(SelectedColor) : BlackBrush;
            double thick = isSelected ? 2.5 : 1.5;

            MainShape.Stroke          = stroke;
            MainShape.StrokeThickness = thick;
            if (JLineRight   != null) { JLineRight.Stroke   = stroke; JLineRight.StrokeThickness   = thick; }
            if (JLineLeft    != null) { JLineLeft.Stroke    = stroke; JLineLeft.StrokeThickness    = thick; }
            if (BottomLine   != null) { BottomLine.Stroke   = stroke; BottomLine.StrokeThickness   = thick; }
            if (StripDivider != null) { StripDivider.Stroke = stroke; StripDivider.StrokeThickness = thick; }

            // Оранжевая тень при выделении
            MainShape.Effect = isSelected
                ? new System.Windows.Media.Effects.DropShadowEffect
                  { BlurRadius = 10, ShadowDepth = 0, Opacity = 0.4, Color = SelectedColor }
                : null;
        }

        // Обновляет отображаемый текст после изменения свойства Text.
        // TextBlock.Text всегда = Text.ToUpper().
        public void RefreshText()
        {
            if (TextBlock != null && !IsJunction)
                TextBlock.Text = Text.ToUpper();
        }

        // Обновляет текстовую метку с номером блока.
        // UOB: «1», «2»; перекрёсток: «J1», «J2».
        // Вызывается из AddNodeToCanvas, чтобы номер корректно отображался после undo/redo.
        public void RefreshNumber()
        {
            if (NumberText == null) return;
            NumberText.Text = IsJunction
                ? "J" + Number.ToString()
                : Number.ToString();
        }

        // Обновляет отображение ссылочного выражения в правой ячейке.
        public void RefreshReference()
        {
            if (RefText != null)
                RefText.Text = ReferenceExpression ?? "";
        }
    }
}
