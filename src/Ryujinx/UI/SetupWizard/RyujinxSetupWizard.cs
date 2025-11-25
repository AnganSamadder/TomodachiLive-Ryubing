using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.Systems.Configuration;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Ava.UI.SetupWizard.Pages;
using Ryujinx.Ava.UI.Windows;
using System.Threading.Tasks;

namespace Ryujinx.Ava.UI.SetupWizard
{
    public class RyujinxSetupWizard(RyujinxSetupWizardWindow wizardWindow, bool overwriteMode)
    {
        private bool _configWasModified;

        public bool HasFirmware => RyujinxApp.MainWindow.ContentManager.GetCurrentFirmwareVersion() != null;

        public NotificationHelper Notification { get; private set; }

        public async Task Start()
        {
            NotificationHelper = new NotificationHelper(wizardWindow);
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
            NotificationHelper = null;
            wizardWindow.Close();
            RyujinxSetupWizardWindow.IsOpen = false;

            if (_configWasModified)
                ConfigurationState.Instance.ToFileFormat().SaveConfig(Program.GlobalConfigurationPath);
        }

        private async ValueTask<bool> SetupKeys()
        {
            if (!_mainWindow.VirtualFileSystem.HasKeySet)
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
            if (!HasFirmware)
            {
                if (!_mainWindow.VirtualFileSystem.HasKeySet)
                {
                    NotificationHelper.ShowError("Keys still seem to not be installed. Please try again.");
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

        private SetupWizardPage FirstPage() => new(wizardWindow.WizardPresenter, this, isFirstPage: true);

        private SetupWizardPage NextPage() => new(wizardWindow.WizardPresenter, this);

        public void SignalConfigModified()
        {
            _configWasModified = true;
        }
    }
}
