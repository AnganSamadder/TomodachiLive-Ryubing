using Gommon;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.Systems.Configuration;
using Ryujinx.Ava.Systems.SetupWizard;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Ava.UI.SetupWizard.Pages;
using Ryujinx.Ava.UI.Windows;
using Ryujinx.Common.Configuration;
using Ryujinx.HLE.FileSystem;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Ryujinx.Ava.UI.SetupWizard
{
    public partial class RyujinxSetupWizard(RyujinxSetupWizardWindow wizardWindow) 
        : BaseSetupWizard(wizardWindow.WizardPresenter)
    {
        private readonly MainWindow _mainWindow = RyujinxApp.MainWindow;

        private bool _configWasModified = false;

        public bool HasFirmware => _mainWindow.ContentManager.GetCurrentFirmwareVersion() != null;

        public override async Task Start()
        {
            NotificationHelper.SetNotificationManager(wizardWindow);
            RyujinxSetupWizardWindow.IsUsingSetupWizard = true;
            Start:
            await FirstPage();

            Keys:
            if (!await SetupKeys())
                goto Start;

            Firmware:
            if (!await SetupFirmware())
                goto Keys;

            Return:
            NotificationHelper.SetNotificationManager(_mainWindow);
            wizardWindow.Close();
            RyujinxSetupWizardWindow.IsUsingSetupWizard = false;

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
                    .WithContent<SetupKeysPage, SetupKeysPageViewModel>(out SetupKeysPageViewModel kpvm)
                    .Show();

                if (!result)
                    return false;

                if (!Directory.Exists(kpvm.KeysFolderPath))
                    goto Retry;

                Result installResult = InstallKeys(kpvm.KeysFolderPath);
                if (!installResult.IsSuccess)
                {
                    goto Retry;
                }
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
                    .WithContent<SetupFirmwarePage, SetupFirmwarePageViewModel>(out SetupFirmwarePageViewModel fwvm)
                    .Show();

                if (!result)
                    return false;

                if (!Directory.Exists(fwvm.FirmwareSourcePath))
                    goto Retry;

                try
                {
                    _mainWindow.ContentManager.InstallFirmware(fwvm.FirmwareSourcePath);
                    SystemVersion installedFwVer = _mainWindow.ContentManager.GetCurrentFirmwareVersion();
                    if (installedFwVer != null)
                    {
                        NotificationHelper.ShowInformation(
                            "Firmware installed",
                            $"Installed firmware version {installedFwVer.VersionString}."
                        );
                    }
                    else
                    {
                        NotificationHelper.ShowError(
                            "Firmware not installed",
                            $"It seems some error occurred when trying to install the firmware at path '{fwvm.FirmwareSourcePath}'. " +
                            "\nPlease check the log or try again."
                        );
                    }
                    _mainWindow.ViewModel.RefreshFirmwareStatus(installedFwVer, allowNullVersion: true);

                    // Purge Applet Cache.

                    DirectoryInfo miiEditorCacheFolder = new(
                        Path.Combine(AppDataManager.GamesDirPath, "0100000000001009", "cache")
                    );

                    if (miiEditorCacheFolder.Exists)
                    {
                        miiEditorCacheFolder.Delete(true);
                    }
                }
                catch (Exception e)
                {
                    NotificationHelper.ShowError(e.Message, waitingExit: true);
                    goto Retry;
                }
            }

            return true;
        }
    }
}
