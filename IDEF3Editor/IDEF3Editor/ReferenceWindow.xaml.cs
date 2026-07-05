using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace IDEF3Editor
{
    /// <summary>
    /// Окно справки по нотации IDEF3: содержит вкладки с описанием
    /// обозначений, блоков, связей и управляющих элементов.
    /// </summary>
    public partial class ReferenceWindow : Window
    {
        // Индекс активной в данный момент вкладки
        private int _currentTab = 0;

        // Массивы кнопок-вкладок и соответствующих им страниц контента
        private readonly Button[]    _tabs;
        private readonly UIElement[] _pages;

        /// <summary>
        /// Инициализирует окно и активирует первую вкладку.
        /// </summary>
        public ReferenceWindow()
        {
            InitializeComponent();

            // Связываем кнопки и панели в том же порядке, что объявлены в XAML
            _tabs  = new[] { TabNotation, TabBlocks, TabLinks, TabControls };
            _pages = new UIElement[] { PageNotation, PageBlocks, PageLinks, PageControls };

            ActivateTab(0);
        }

        // Обработчик клика по любой кнопке-вкладке; индекс передаётся через Tag
        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tagStr &&
                int.TryParse(tagStr, out int idx))
                ActivateTab(idx);
        }

        /// <summary>
        /// Переключает активную вкладку: выделяет нужную кнопку акцентным цветом
        /// и показывает соответствующую страницу, скрывая остальные.
        /// </summary>
        private void ActivateTab(int idx)
        {
            _currentTab = idx;
            for (int i = 0; i < _tabs.Length; i++)
            {
                bool active = i == idx;

                // Активная вкладка — оранжевая рамка и текст, неактивная — серая
                _tabs[i].BorderBrush = active
                    ? new SolidColorBrush(Color.FromRgb(232, 101, 10))
                    : new SolidColorBrush(Colors.Transparent);
                _tabs[i].Foreground = active
                    ? new SolidColorBrush(Color.FromRgb(232, 101, 10))
                    : new SolidColorBrush(Color.FromRgb(127, 140, 141));

                // Показываем только страницу активной вкладки
                _pages[i].Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }
}
