using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;

namespace Win98Get.Modern
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? window;

        public enum ThemePreference
        {
            System = 0,
            Light = 1,
            Dark = 2,
        }

        private const string ThemePreferenceKey = "ThemePreference";

        public ThemePreference CurrentThemePreference { get; private set; } = ThemePreference.System;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            // Required for Windows App SDK single-file publish.
            try
            {
                Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);
            }
            catch
            {
                // ignore
            }

            this.InitializeComponent();

            CurrentThemePreference = LoadThemePreference();
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.  Other entry points
        /// will be used such as when the application is launched to open a specific file.
        /// </summary>
        /// <param name="e">Details about the launch request and process.</param>
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            window ??= new Window();

            window.Title = "Win98Get";

            // Fluent Win11 look.
            try
            {
                window.SystemBackdrop = new MicaBackdrop();
            }
            catch
            {
                // Backdrop may fail on older OS / certain environments.
            }

            if (window.Content is not Frame rootFrame)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                window.Content = rootFrame;
            }

            ApplyThemeToWindow();

            _ = rootFrame.Navigate(typeof(MainPage), e.Arguments);
            window.Activate();
        }

        public void SetThemePreference(ThemePreference preference)
        {
            CurrentThemePreference = preference;
            SaveThemePreference(preference);
            ApplyThemeToWindow();
        }

        private void ApplyThemeToWindow()
        {
            if (window?.Content is not FrameworkElement root)
            {
                return;
            }

            root.RequestedTheme = CurrentThemePreference switch
            {
                ThemePreference.Light => ElementTheme.Light,
                ThemePreference.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }

        private static ThemePreference LoadThemePreference()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue(ThemePreferenceKey, out var v) && v is int i)
                {
                    if (Enum.IsDefined(typeof(ThemePreference), i))
                    {
                        return (ThemePreference)i;
                    }
                }
            }
            catch
            {
                // ignore
            }

            return ThemePreference.System;
        }

        private static void SaveThemePreference(ThemePreference preference)
        {
            try
            {
                ApplicationData.Current.LocalSettings.Values[ThemePreferenceKey] = (int)preference;
            }
            catch
            {
                // ignore
            }
        }

        /// <summary>
        /// Invoked when Navigation to a certain page fails
        /// </summary>
        /// <param name="sender">The Frame which failed navigation</param>
        /// <param name="e">Details about the navigation failure</param>
        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }
    }
}
