using DynamicData;
using Gommon;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.Exceptions;
using Ryujinx.HLE.FileSystem;
using System;
using System.IO;

namespace Ryujinx.Ava.UI.SetupWizard
{
    public partial class RyujinxSetupWizard
    {
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

                NotificationHelper.ShowInformation(
                    title: LocaleManager.Instance[LocaleKeys.RyujinxInfo],
                    text: LocaleManager.Instance[LocaleKeys.DialogKeysInstallerKeysInstallSuccessMessage]);
            }
            catch (InvalidFirmwarePackageException ifwpe)
            {
                NotificationHelper.ShowError(ifwpe.Message, waitingExit: true);
                return Result.Failure(NoKeysFoundInFolder.Shared);
            }
            catch (MissingKeyException ex)
            {
                NotificationHelper.ShowError(ex.ToString(), waitingExit: true);
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

                NotificationHelper.ShowError(message, waitingExit: true);

                return Result.Failure(new MessageError(ex.Message));
            }
            finally
            {
                mwvm.VirtualFileSystem.ReloadKeySet();
            }

            return Result.Success;
        }
    }

    public struct NoKeysFoundInFolder : IErrorState
    {
        public static readonly NoKeysFoundInFolder Shared = new();
    }
}
