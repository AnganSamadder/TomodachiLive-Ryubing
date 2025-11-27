using Avalonia;
using Avalonia.Controls.Notifications;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Gommon;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.Systems.Configuration;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Ava.UI.SetupWizard.Pages;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Ryujinx.Ava.UI.SetupWizard
{
    public class RyujinxSetupWizard : IDisposable, INotifyPropertyChanged
    {
        private bool _configWasModified;

        public bool HasFirmware => RyujinxApp.MainWindow.ContentManager.GetCurrentFirmwareVersion() != null;

        public RyujinxNotificationManager NotificationManager { get; private set; }

        public async Task Start()
        {
            NotificationManager = _window.CreateNotificationManager(
                // I wanted to do bottom center but that...literally just shows top center? Okay.
                // Fuck it, weird window height hack to do it instead.
                // 120 is not exact, just a random number. Looks fine though.
                NotificationPosition.TopCenter,
                margin: new Thickness(0, _window.Height - 120, 0, 0)
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

            Return:
            if (_configWasModified)
                ConfigurationState.Instance.ToFileFormat().SaveConfig(Program.GlobalConfigurationPath);

            NotificationManager = null;
            _window.Close();
            RyujinxSetupWizardWindow.IsOpen = false;
        }

        public Bitmap DiscordLogo
        {
            get;
            set => SetField(ref field, value);
        }

        private void Ryujinx_ThemeChanged()
        {
            Dispatcher.UIThread.Post(() => UpdateLogoTheme(ConfigurationState.Instance.UI.BaseStyle.Value));
        }

        private const string LogoPathFormat = "resm:Ryujinx.Assets.UIImages.Logo_{0}_{1}.png?assembly=Ryujinx";

        private void UpdateLogoTheme(string theme)
        {
            bool isDarkTheme = theme == "Dark" ||
                               (theme == "Auto" && RyujinxApp.DetectSystemTheme() == ThemeVariant.Dark);

            string themeName = isDarkTheme ? "Dark" : "Light";

            DiscordLogo = LoadBitmap(LogoPathFormat.Format("Discord", themeName));
        }

        private static Bitmap LoadBitmap(string uri) => new(Avalonia.Platform.AssetLoader.Open(new Uri(uri)));

        private async ValueTask<bool> SetupKeys()
        {
            if (_overwrite || !RyujinxApp.MainWindow.VirtualFileSystem.HasKeySet)
            {
                Retry:
                bool result = await NextPage()
                    .WithTitle(LocaleKeys.SetupWizardKeysPageTitle)
                    .WithContent<SetupKeysPage, SetupKeysPageContext>(out SetupKeysPageContext keyContext)
                    .Show();

                if (!result)
                    return false;

                if (!keyContext.CompleteStep())
                    goto Retry;
            }

            return true;
        }

        private async ValueTask<bool> SetupFirmware()
        {
            if (_overwrite || !HasFirmware)
            {
                if (!RyujinxApp.MainWindow.VirtualFileSystem.HasKeySet)
                {
                    NotificationManager.Error("Keys still seem to not be installed. Please try again.");
                    return false;
                }

                Retry:
                bool result = await NextPage()
                    .WithTitle(LocaleKeys.SetupWizardFirmwarePageTitle)
                    .WithContent<SetupFirmwarePage, SetupFirmwarePageContext>(out SetupFirmwarePageContext fwContext)
                    .Show();

                if (!result)
                    return false;

                if (!fwContext.CompleteStep())
                    goto Retry;
            }

            return true;
        }

        private SetupWizardPage FirstPage() => new(_window.WizardPresenter, this, isFirstPage: true);

        private SetupWizardPage NextPage() => new(_window.WizardPresenter, this);

        public void SignalConfigModified()
        {
            _configWasModified = true;
        }

        private readonly RyujinxSetupWizardWindow _window;
        private readonly bool _overwrite;

        public RyujinxSetupWizard(RyujinxSetupWizardWindow wizardWindow, bool overwriteMode)
        {
            _window = wizardWindow;
            _overwrite = overwriteMode;

            if (Program.PreviewerDetached)
            {
                UpdateLogoTheme(ConfigurationState.Instance.UI.BaseStyle);
                RyujinxApp.ThemeChanged += Ryujinx_ThemeChanged;
            }
        }

        public void Dispose()
        {
            RyujinxApp.ThemeChanged -= Ryujinx_ThemeChanged;

            DiscordLogo.Dispose();

            GC.SuppressFinalize(this);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
