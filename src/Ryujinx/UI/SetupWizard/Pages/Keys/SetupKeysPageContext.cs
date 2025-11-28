using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicData;
using Gommon;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Ava.Utilities;
using Ryujinx.Common;
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

        public override object CreateHelpContent()
        {
            Grid grid = new()
            {
                RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto)],
                HorizontalAlignment = HorizontalAlignment.Center
            };

            grid.Children.Add(new TextBlock
            {
                Text = "Not sure how to get your keys?",
                HorizontalAlignment = HorizontalAlignment.Center,
                GridRow = 0
            });

            grid.Children.Add(new HyperlinkButton
            {
                Content = "Click here to view a guide.",
                HorizontalAlignment = HorizontalAlignment.Center,
                NavigateUri = new Uri(SharedConstants.DumpKeysWikiUrl),
                GridRow = 1
            });

            return grid;
        }

        [ObservableProperty] public partial string KeysFolderPath { get; set; }

        [RelayCommand]
        private static async Task Browse(TextBox tb)
        {
            Optional<IStorageFolder> result =
                await RyujinxApp.MainWindow.ViewModel.StorageProvider.OpenSingleFolderPickerAsync(
                    new FolderPickerOpenOptions
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

                NotificationManager.Information(
                    title: LocaleManager.Instance[LocaleKeys.RyujinxInfo],
                    text: LocaleManager.Instance[LocaleKeys.DialogKeysInstallerKeysInstallSuccessMessage]);
            }
            catch (InvalidFirmwarePackageException ifwpe)
            {
                NotificationManager.Error(ifwpe.Message, waitingExit: true);
                return Result.Failure(NoKeysFoundInFolder.Shared);
            }
            catch (MissingKeyException ex)
            {
                NotificationManager.Error(ex.ToString(), waitingExit: true);
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

                NotificationManager.Error(message, waitingExit: true);

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
