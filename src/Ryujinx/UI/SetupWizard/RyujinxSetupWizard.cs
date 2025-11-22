using Avalonia.Controls.Presenters;
using Gommon;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.Systems.Configuration;
using Ryujinx.Ava.Systems.SetupWizard;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Ava.UI.ViewModels;
using Ryujinx.Ava.UI.SetupWizard.Pages;
using Ryujinx.Common.Configuration;
using Ryujinx.HLE.FileSystem;
using Ryujinx.UI.SetupWizard.Pages;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Ryujinx.Ava.UI.SetupWizard
{
    public partial class RyujinxSetupWizard(ContentPresenter presenter, MainWindowViewModel mwvm, Action onClose)
        : BaseSetupWizard(presenter)
    {
        private bool _configWasModified = false;

        public bool HasFirmware => mwvm.ContentManager.GetCurrentFirmwareVersion() != null;

        public override async Task Start()
        {
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
            onClose();

            if (_configWasModified)
                ConfigurationState.Instance.ToFileFormat().SaveConfig(Program.GlobalConfigurationPath);
        }

        private async ValueTask<bool> SetupKeys()
        {
            if (!mwvm.VirtualFileSystem.HasKeySet)
            {
                Retry:
                SetupKeysPageViewModel kpvm = new();
                bool result = await NextPage()
                    .WithTitle(LocaleKeys.SetupWizardKeysPageTitle)
                    .WithContent<SetupKeysPage>(kpvm)
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
                if (!mwvm.VirtualFileSystem.HasKeySet)
                {
                    NotificationHelper.ShowError("Keys still seem to not be installed. Are you sure they're in that folder?");
                    return false;
                }

                Retry:
                SetupFirmwarePageViewModel fwvm = new();
                bool result = await NextPage()
                    .WithTitle(LocaleKeys.SetupWizardFirmwarePageTitle)
                    .WithContent<SetupFirmwarePage>(fwvm)
                    .Show();

                if (!result)
                    return false;

                if (!Directory.Exists(fwvm.FirmwareSourcePath))
                    goto Retry;

                try
                {
                    mwvm.ContentManager.InstallFirmware(fwvm.FirmwareSourcePath);
                    SystemVersion installedFwVer = mwvm.ContentManager.GetCurrentFirmwareVersion();
                    NotificationHelper.ShowInformation(
                        "Firmware installed",
                        $"Installed firmware version {installedFwVer.VersionString}."
                    );
                    mwvm.RefreshFirmwareStatus(installedFwVer);

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
