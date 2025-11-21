using DynamicData;
using Gommon;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.FileSystem;
using System;
using System.IO;
using System.Threading;

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

                Thread thread = new(() =>
                {
                    try
                    {
                        ContentManager.InstallKeys(directory, systemDirectory);

                        NotificationHelper.ShowInformation(
                            title: LocaleManager.Instance[LocaleKeys.RyujinxInfo],
                            text: LocaleManager.Instance[LocaleKeys.DialogKeysInstallerKeysInstallSuccessMessage]);
                    }
                    catch (Exception ex)
                    {
                        string message = ex.Message;
                        if (ex is FormatException)
                        {
                            message = LocaleManager.Instance.UpdateAndGetDynamicValue(
                                LocaleKeys.DialogKeysInstallerKeysNotFoundErrorMessage, directory);
                        }

                        NotificationHelper.ShowError(message);
                    }
                    finally
                    {
                        mwvm.VirtualFileSystem.ReloadKeySet();
                    }
                }) { Name = "GUI.KeysInstallerThread" };

                thread.Start();
            }
            catch (MissingKeyException ex)
            {
                NotificationHelper.ShowError(ex.ToString());
                return Result.Failure(NoKeysFoundInFolder.Shared);
            }
            catch (Exception ex)
            {
                return Result.Failure(new MessageError(ex.Message));
            }

            return Result.Success;
        }
    }

    public struct NoKeysFoundInFolder : IErrorState
    {
        public static readonly NoKeysFoundInFolder Shared = new();
    }
}
