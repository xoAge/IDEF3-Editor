using System;
using System.Windows;

namespace IDEF3Editor
{
    
    /// Диалоговое окно редактирования свойств диаграммы (название, автор, описание).
    
    public partial class DiagramPropertiesDialog : Window
    {
        // Свойства, доступные после закрытия диалога с результатом true
        public string DiagramName        { get; private set; }
        public string DiagramAuthor      { get; private set; }
        public string DiagramDescription { get; private set; }

        
        /// Инициализирует диалог и заполняет поля текущими значениями свойств диаграммы.
        
        public DiagramPropertiesDialog(string name, string author,
                                       string description, DateTime created)
        {
            InitializeComponent();

            // Заполняем поля ввода переданными значениями
            NameBox.Text        = name;
            AuthorBox.Text      = author;
            DescriptionBox.Text = description;
            CreatedLabel.Text   = $"Создано: {created:dd.MM.yyyy HH:mm}";

            // Устанавливаем фокус на поле названия и выделяем весь текст
            NameBox.Focus();
            NameBox.SelectAll();
        }

        // Обработчик кнопки «ОК»: валидирует поля и сохраняет результат
        private void OkBtn_Click(object sender, RoutedEventArgs e)
        {
            // Название диаграммы обязательно для заполнения
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                MessageBox.Show("Название диаграммы не может быть пустым.",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                NameBox.Focus();
                return;
            }

            // Сохраняем отредактированные значения и закрываем диалог с успехом
            DiagramName        = NameBox.Text.Trim();
            DiagramAuthor      = AuthorBox.Text.Trim();
            DiagramDescription = DescriptionBox.Text.Trim();
            this.DialogResult  = true;
        }

        // Обработчик кнопки «Отмена»: закрываем диалог без сохранения
        private void CancelBtn_Click(object sender, RoutedEventArgs e)
            => this.DialogResult = false;
    }
}
