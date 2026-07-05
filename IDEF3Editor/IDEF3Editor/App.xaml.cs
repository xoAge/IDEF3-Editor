using System.Windows;

namespace IDEF3Editor
{
    // Точка входа приложения.
    // При запуске проверяет доступность DWM (Desktop Window Manager).
    // Если DWM отключён (Windows 7 Classic / Basic тема без Aero),
    // то кастомный chrome с AllowsTransparency не будет работать корректно —
    // в этом случае используем стандартный заголовок Windows.
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (!IsCompositionEnabled())
            {
                // DWM недоступен — переходим к стандартному хрому Windows
                var win = new MainWindow();
                win.AllowsTransparency = false;
                win.WindowStyle        = WindowStyle.SingleBorderWindow;
                win.Background         = SystemColors.WindowBrush;
                win.Show();
            }
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmIsCompositionEnabled(out bool enabled);

        // Возвращает true, если Desktop Window Manager (Aero) включён.
        // На Windows 7 с Classic/Basic темой DWM может быть отключён.
        private static bool IsCompositionEnabled()
        {
            try
            {
                DwmIsCompositionEnabled(out bool enabled);
                return enabled;
            }
            catch
            {
                // dwmapi.dll отсутствует (очень старая ОС) — считаем, что DWM недоступен
                return false;
            }
        }
    }
}
