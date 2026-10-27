using System;
using System.Windows;
using NightreignRelicTool.FinalUI;

namespace NightreignRelicTool
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            Localization.LocalizationService.Current.Initialize(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UserData", "language.txt"));
            Window window = CreateMainWindow();
            MainWindow = window;

            window.Show();
        }

        public static Window CreateMainWindow()
        {
            return new FinalUI.MainWindow();
        }
    }
}
