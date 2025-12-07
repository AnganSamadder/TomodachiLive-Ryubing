using Ryujinx.Ava.UI.SetupWizard.Pages;
using Ryujinx.UI.SetupWizard.Pages;
using System.Threading.Tasks;

namespace Ryujinx.Ava.UI.SetupWizard
{
    public partial class RyujinxSetupWizard
    {
        private async ValueTask<bool> SetupKeys()
        {
            if (_overwrite || !RyujinxApp.MainWindow.VirtualFileSystem.HasKeySet)
            {
                Retry:
                bool result = await NextPage<SetupKeysPage, SetupKeysPageContext>(out SetupKeysPageContext keyContext)
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
                bool result =
                    await NextPage<SetupFirmwarePage, SetupFirmwarePageContext>(out SetupFirmwarePageContext fwContext)
                        .Show();

                if (!result)
                    return false;

                if (!fwContext.CompleteStep())
                    goto Retry;

                OnPropertyChanged(nameof(HasFirmware));
            }

            return true;
        }

        private async ValueTask<bool> SetupGameDirs()
        {

            if (!HasFirmware)
            {
                NotificationManager.Error("Firmware still seems to not be installed. Please try again.");
                return false;
            }

            Retry:
            bool result =
                await NextPage<SetupGameDirsPage, SetupGameDirsPageContext>(out SetupGameDirsPageContext gdContext)
                    .Show();

            if (!result)
                return false;

            if (!gdContext.CompleteStep())
                goto Retry;

            return true;
        }

        private ValueTask<bool> Finish() 
            => NextPage<SetupFinishedPage, SetupFinishedPageContext>(out _)
                .WithHelpButtonVisible(false)
                .Show();
    }
}
