using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace IDEF3Editor
{
    // Вспомогательный класс для отрисовки сетки на холсте.
    public static class GridHelper
    {
        // Очищает холст и рисует равномерную сетку с шагом step.
        // Параметры offsetX / offsetY позволяют смещать начало сетки
        // (в текущей реализации передаются как 0).
        public static void DrawGrid(Canvas canvas, double step, double offsetX, double offsetY)
        {
            canvas.Children.Clear(); // удаляем старые линии сетки

            double width  = canvas.ActualWidth;
            double height = canvas.ActualHeight;

            // Холст ещё не отрисован — ничего не делаем
            if (width <= 0 || height <= 0) return;

            // Вертикальные линии
            for (double x = offsetX % step; x < width; x += step)
            {
                canvas.Children.Add(new Line
                {
                    X1 = x, Y1 = 0, X2 = x, Y2 = height,
                    Stroke = Brushes.LightGray, StrokeThickness = 0.5
                });
            }

            // Горизонтальные линии
            for (double y = offsetY % step; y < height; y += step)
            {
                canvas.Children.Add(new Line
                {
                    X1 = 0, Y1 = y, X2 = width, Y2 = y,
                    Stroke = Brushes.LightGray, StrokeThickness = 0.5
                });
            }
        }
    }
}
