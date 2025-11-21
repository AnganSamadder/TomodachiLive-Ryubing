using Avalonia.Controls.Presenters;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.Systems.Configuration;
using Ryujinx.Ava.Systems.SetupWizard;
using Ryujinx.Ava.UI.ViewModels;
using Ryujinx.Common.Logging;
using Ryujinx.UI.SetupWizard;
using Ryujinx.UI.SetupWizard.Pages;
using System;
using System.IO;
using System.Threading.Tasks;
using Logger = Ryujinx.Common.Logging.Logger;

namespace Ryujinx.Ava.UI.SetupWizard
{
    public class RyujinxSetupWizard(ContentPresenter presenter, MainWindowViewModel mwvm, Action onClose) : BaseSetupWizard(presenter)
    {
        private bool _configWasModified = false;

        public bool HasFirmware => mwvm.ContentManager.GetCurrentFirmwareVersion() != null;

        public override async ValueTask Start()
        {
            RyujinxSetupWizardWindow.IsUsingSetupWizard = true;
            Start:
            await FirstPage();

            Keys:
            if (!mwvm.VirtualFileSystem.HasKeySet)
            { 
                Retry:
                SetupKeysPageViewModel kpvm = new();
                bool result = await NextPage()
                    .WithTitle(LocaleKeys.SetupWizardKeysPageTitle)
                    .WithContent<SetupKeysPage>(kpvm)
                    .Show();

                if (!result)
                    goto Start;

                if (!Directory.Exists(kpvm.KeysFolderPath))
                    goto Retry;

                await mwvm.HandleKeysInstallation(kpvm.KeysFolderPath);
            }

            Firmware:
            // ReSharper disable once ConditionIsAlwaysTrueOrFalse
            // i know its always false thats the fucking point, its not done
            if (!HasFirmware && false)
            {
                if (!mwvm.VirtualFileSystem.HasKeySet)
                    goto Keys;
                
                Retry:
                SetupKeysPageViewModel kpvm = new();
                bool result = await NextPage()
                    .WithTitle(LocaleKeys.SetupWizardKeysPageTitle)
                    .WithContent<SetupKeysPage>(kpvm)
                    .Show();
                
                if (!result)
                    goto Keys;

                if (!Directory.Exists(kpvm.KeysFolderPath))
                    goto Retry;

                await mwvm.HandleKeysInstallation(kpvm.KeysFolderPath);
            }

            Return:
            onClose();

            if (_configWasModified)
                ConfigurationState.Instance.ToFileFormat().SaveConfig(Program.GlobalConfigurationPath);
        }
    }
}
