using System;
using System.Windows;

namespace GoogleCalendarWidget
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 전역 예외 처리
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            DispatcherUnhandledException += OnDispatcherUnhandledException;
        }

        private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(
                $"예기치 않은 오류가 발생했습니다:\n\n{e.Exception.Message}\n\n애플리케이션을 다시 시작해주세요.",
                "Google Calendar Widget - 오류",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            e.Handled = true;
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;
            MessageBox.Show(
                $"심각한 오류가 발생했습니다:\n\n{exception?.Message ?? "알 수 없는 오류"}\n\n애플리케이션이 종료됩니다.",
                "Google Calendar Widget - 심각한 오류",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}