using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicData;
using Gommon;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.Utilities;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.Exceptions;
using Ryujinx.HLE.FileSystem;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Ryujinx.Ava.UI.SetupWizard.Pages
{
    public partial class SetupKeysPageContext : SetupWizardPageContext
    {
        public override Result CompleteStep() =>
            !Directory.Exists(KeysFolderPath)
                ? Result.Fail 
                : InstallKeys(KeysFolderPath);

        [ObservableProperty]
        public partial string KeysFolderPath { get; set; }

        [RelayCommand]
        private static async Task Browse(TextBox tb)
        {
            Optional<IStorageFolder> result = await RyujinxApp.MainWindow.ViewModel.StorageProvider.OpenSingleFolderPickerAsync(new FolderPickerOpenOptions 
            {
                Title = LocaleManager.Instance[LocaleKeys.SetupWizardKeysPageFolderPopupTitle]
            });

            if (result.TryGet(out IStorageFolder keyFolder)) 
            {
                tb.Text = keyFolder.TryGetLocalPath();
            }
        }

        private Result InstallKeys(string directory)
        {
            try
            {
                string systemDirectory = AppDataManager.KeysDirPath;
                if (AppDataManager.Mode == AppDataManager.LaunchMode.UserProfile &&
                    Directory.Exists(AppDataManager.KeysDirPathUser))
                {
                    systemDirectory = AppDataManager.KeysDirPathUser;
                }

                Logger.Info?.Print(LogClass.Application, $"Installing keys from {directory}");

                ContentManager.InstallKeys(directory, systemDirectory);

                Notifications.NotifyInformation(
                    title: LocaleManager.Instance[LocaleKeys.RyujinxInfo],
                    text: LocaleManager.Instance[LocaleKeys.DialogKeysInstallerKeysInstallSuccessMessage]);
            }
            catch (InvalidFirmwarePackageException ifwpe)
            {
                Notifications.NotifyError(ifwpe.Message, waitingExit: true);
                return Result.Failure(NoKeysFoundInFolder.Shared);
            }
            catch (MissingKeyException ex)
            {
                Notifications.NotifyError(ex.ToString(), waitingExit: true);
                return Result.Failure(NoKeysFoundInFolder.Shared);
            }
            catch (Exception ex)
            {
                string message = ex.Message;
                if (ex is FormatException)
                {
                    message = LocaleManager.Instance.UpdateAndGetDynamicValue(
                        LocaleKeys.DialogKeysInstallerKeysNotFoundErrorMessage, directory);
                }

                Notifications.NotifyError(message, waitingExit: true);

                return Result.Failure(new MessageError(message));
            }
            finally
            {
                RyujinxApp.MainWindow.VirtualFileSystem.ReloadKeySet();
            }

            return Result.Success;
        }
    }

    public struct NoKeysFoundInFolder : IErrorState
    {
        public static readonly NoKeysFoundInFolder Shared = new();
    }
}
