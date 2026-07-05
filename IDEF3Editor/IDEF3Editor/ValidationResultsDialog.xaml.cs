using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace IDEF3Editor
{
    /// <summary>
    /// Диалоговое окно с результатами валидации диаграммы IDEF3.
    /// Отображает список ошибок, предупреждений и замечаний с цветовой индикацией.
    /// </summary>
    public partial class ValidationResultsDialog : Window
    {
        // Событие, вызываемое при клике на строку, привязанную к конкретному узлу,
        // чтобы MainWindow мог выделить этот элемент на холсте
        public event Action<string> NodeFocusRequested;

        /// <summary>
        /// Инициализирует диалог и заполняет список найденных нарушений.
        /// </summary>
        public ValidationResultsDialog(List<ValidationIssue> issues)
        {
            InitializeComponent();
            PopulateIssues(issues);
        }

        /// <summary>
        /// Заполняет панель строками нарушений и обновляет итоговую строку статистики.
        /// </summary>
        private void PopulateIssues(List<ValidationIssue> issues)
        {
            // Подсчитываем количество нарушений по каждому уровню серьёзности
            int errors   = issues.Count(i => i.Severity == ValidationSeverity.Error);
            int warnings = issues.Count(i => i.Severity == ValidationSeverity.Warning);
            int infos    = issues.Count(i => i.Severity == ValidationSeverity.Info);

            SummaryText.Text = $"— {errors} ошиб. · {warnings} предупр. · {infos} замеч.";

            // Формируем итоговое сообщение в зависимости от наличия критических нарушений
            if (errors == 0 && warnings == 0)
                FooterText.Text = "✓ Критических нарушений не обнаружено.";
            else if (errors > 0)
                FooterText.Text = $"Обнаружено {errors} ошибок нотации — исправьте перед сдачей.";
            else
                FooterText.Text = $"Ошибок нет, но есть {warnings} предупреждений.";

            // Добавляем строку для каждого нарушения
            foreach (var issue in issues)
            {
                var row = BuildRow(issue);
                IssuePanel.Children.Add(row);
            }
        }

        /// <summary>
        /// Создаёт визуальную строку для одного нарушения валидации.
        /// Цвет фона и акцента зависит от уровня серьёзности.
        /// </summary>
        private Border BuildRow(ValidationIssue issue)
        {
            // Тёмные фоны с цветным акцентом — читаемо на фоне окна #2B2D30
            Color bg, accent;
            switch (issue.Severity)
            {
                case ValidationSeverity.Error:
                    bg     = Color.FromRgb(70, 32, 28);    // тёмно-красный фон
                    accent = Color.FromRgb(210, 80,  60);  // тёплый красный акцент
                    break;
                case ValidationSeverity.Warning:
                    bg     = Color.FromRgb(68, 50, 22);    // тёмно-янтарный фон
                    accent = Color.FromRgb(232, 101, 10);  // оранжевый акцент (ColOrange)
                    break;
                default:
                    bg     = Color.FromRgb(50, 52, 56);    // чуть светлее фона окна
                    accent = Color.FromRgb(155, 158, 163); // серый акцент (ColGrayLt)
                    break;
            }

            // Рамка строки с левым цветным бордером и скруглёнными правыми углами
            var row = new Border
            {
                Background      = new SolidColorBrush(bg),
                BorderBrush     = new SolidColorBrush(accent),
                BorderThickness = new Thickness(3, 0, 0, 0),
                Padding         = new Thickness(10, 8, 10, 8),
                Margin          = new Thickness(0, 0, 0, 3),
                CornerRadius    = new CornerRadius(0, 4, 4, 0),
                // Курсор-рука — если строка привязана к узлу диаграммы
                Cursor          = issue.NodeId != null
                    ? System.Windows.Input.Cursors.Hand
                    : System.Windows.Input.Cursors.Arrow
            };

            // Двухколоночная сетка: иконка слева, текст сообщения справа
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Иконка нарушения (символ уровня серьёзности)
            var icon = new TextBlock
            {
                Text              = issue.Icon,
                FontSize          = 14,
                FontWeight        = FontWeights.Bold,
                Foreground        = new SolidColorBrush(accent),
                VerticalAlignment = VerticalAlignment.Top,
                Margin            = new Thickness(0, 1, 0, 0)
            };
            Grid.SetColumn(icon, 0);

            // Текст описания нарушения
            var msg = new TextBlock
            {
                Text         = issue.Message,
                FontSize     = 12,
                Foreground   = new SolidColorBrush(Color.FromRgb(220, 222, 225)),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(msg, 1);

            grid.Children.Add(icon);
            grid.Children.Add(msg);
            row.Child = grid;

            // Для строк, привязанных к узлу: клик выделяет узел на холсте,
            // наведение курсора подсвечивает строку осветлением фона
            if (issue.NodeId != null)
            {
                string nodeId = issue.NodeId;
                row.MouseLeftButtonDown += (_, __) =>
                    NodeFocusRequested?.Invoke(nodeId);

                // Цвет при наведении — фон на 20 единиц светлее базового
                var hoverBg = Color.FromArgb(255,
                    (byte)Math.Min(bg.R + 20, 255),
                    (byte)Math.Min(bg.G + 20, 255),
                    (byte)Math.Min(bg.B + 20, 255));
                row.MouseEnter += (_, __) =>
                    row.Background = new SolidColorBrush(hoverBg);
                row.MouseLeave += (_, __) =>
                    row.Background = new SolidColorBrush(bg);
            }

            return row;
        }

        // Обработчик кнопки «Закрыть»
        private void CloseBtn_Click(object sender, RoutedEventArgs e)
            => Close();
    }
}
