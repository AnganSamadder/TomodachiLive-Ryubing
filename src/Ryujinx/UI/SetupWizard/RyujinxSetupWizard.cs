using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Ryujinx.Ava.Common;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.Systems.Configuration;
using Ryujinx.Ava.UI.Controls;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Ava.UI.ViewModels;
using System;
using System.Threading.Tasks;

namespace Ryujinx.Ava.UI.SetupWizard
{
    public partial class RyujinxSetupWizard : BaseModel, IDisposable
    {
        private bool _configWasModified;

        private readonly RyujinxSetupWizardWindow _window;
        private readonly bool _overwrite;

        public void SetWindowTitle(string titleText)
        {
            _window.Title = titleText;
            ToolTip.SetTip(_window.RyuLogo, titleText);
        }

        public RyujinxSetupWizard(RyujinxSetupWizardWindow wizardWindow, bool overwriteMode)
        {
            _window = wizardWindow;
            _overwrite = overwriteMode;

            if (Program.PreviewerDetached)
            {
                UpdateLogoTheme(ConfigurationState.Instance.UI.BaseStyle);
                RyujinxApp.ThemeChanged += Ryujinx_ThemeChanged;
            }
            else
            {
                UpdateLogoTheme("Dark");
            }
        }

        private SetupWizardPage FirstPage() => new(_window.WizardPresenter, this, isFirstPage: true);

        private SetupWizardPage NextPage() => new(_window.WizardPresenter, this);

        private SetupWizardPage NextPage<TControl, TContext>(out TContext boundContext)
            where TControl : RyujinxControl<TContext>, new()
            where TContext : SetupWizardPageContext, new()
            => NextPage()
                .WithContent<TControl, TContext>(out boundContext)
                .WithTitle(boundContext.Title)
                .WithActionContent(boundContext.ActionContent);

        public static bool HasFirmware => RyujinxApp.MainWindow.ContentManager.GetCurrentFirmwareVersion() != null;

        public RyujinxNotificationManager NotificationManager { get; private set; }

        internal void ModifyConfig(Action<ConfigurationState> modifier)
        {
            modifier(ConfigurationState.Instance);
            _configWasModified = true;
        }

        public async Task Start()
        {
            NotificationManager = _window.CreateNotificationManager(
                // I wanted to do bottom center but that...literally just shows top center? Okay.
                // Fuck it, weird window height hack to do it instead.
                // 120 is not exact, just a random number. Looks fine though.
                NotificationPosition.TopCenter,
                margin: new Thickness(0, _window.Height - 135, 0, 0)
            );

            RyujinxSetupWizardWindow.IsOpen = true;
            Start:
            await FirstPage()
                .WithTitle(LocaleKeys.SetupWizardFirstPageTitle)
                .WithContent(LocaleKeys.SetupWizardFirstPageContent)
                .WithActionContent(LocaleKeys.SetupWizardFirstPageAction)
                .Show();
            // result is unhandled as the first page cannot display anything other than the next button.
            // back does not need to be handled

            Keys:
            if (!await SetupKeys())
                goto Start;

            Firmware:
            if (!await SetupFirmware())
                goto Keys;
            
            GameDirs:
            if (!await SetupGameDirs())
                goto Firmware;

            if (!await Finish())
                goto GameDirs;

            Return:
            if (_configWasModified)
                ConfigurationState.Instance.ToFileFormat().SaveConfig(Program.GlobalConfigurationPath);

            NotificationManager = null;
            _window.Close();
            RyujinxSetupWizardWindow.IsOpen = false;
        }

        #region Discord logo stuff

        [ObservableProperty] public partial Bitmap DiscordLogo { get; set; }

        private void Ryujinx_ThemeChanged()
        {
            Dispatcher.UIThread.Post(() => UpdateLogoTheme(ConfigurationState.Instance.UI.BaseStyle));
        }

        private void UpdateLogoTheme(string theme)
        {
            bool isDarkTheme = theme == "Dark" ||
                               (theme == "Auto" && RyujinxApp.DetectSystemTheme() == ThemeVariant.Dark);

            DiscordLogo = UIImages
                .GetLogoByNameAndTheme("Discord", isDarkTheme)
                .CreateScaledBitmap(new PixelSize(32, 24));
        }

        public void Dispose()
        {
            RyujinxApp.ThemeChanged -= Ryujinx_ThemeChanged;

            DiscordLogo.Dispose();

            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
