using System.Windows;

namespace IDEF3Editor
{
    /// <summary>
    /// Диалог подтверждения выхода при несохранённых изменениях.
    /// Выбор пользователя читается через свойство Result после ShowDialog().
    /// </summary>
    public partial class UnsavedDiagram : Window
    {
        // Возможные ответы пользователя
        public enum Choice { Cancel, Save, DontSave }

        // Текущий выбор. По умолчанию Cancel — это значение остаётся,
        // если окно закрыли крестиком или Esc (ни одна кнопка не нажата).
        public Choice Result { get; private set; } = Choice.Cancel;

        public UnsavedDiagram()
        {
            InitializeComponent();
        }

        // «Да» — сохранить перед выходом
        private void YesButton_Click(object sender, RoutedEventArgs e)
        {
            Result = Choice.Save;
            Close();
        }

        // «Нет» — выйти без сохранения
        private void NoButton_Click(object sender, RoutedEventArgs e)
        {
            Result = Choice.DontSave;
            Close();
        }
    }
}
