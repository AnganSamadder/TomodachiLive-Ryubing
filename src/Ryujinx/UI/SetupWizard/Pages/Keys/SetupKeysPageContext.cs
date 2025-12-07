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
    public partial class SetupKeysPageContext() : SetupWizardPageContext(LocaleKeys.SetupWizardKeysPageTitle)
    {
        public override object CreateHelpContent()
        {
            Grid grid = new()
            {
                RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto)],
                HorizontalAlignment = HorizontalAlignment.Center
            };

            grid.Children.Add(new TextBlock
            {
                Text = LocaleManager.Instance[LocaleKeys.SetupWizardKeysPageHelpText],
                HorizontalAlignment = HorizontalAlignment.Center,
                GridRow = 0
            });

            grid.Children.Add(new HyperlinkButton
            {
                Content = LocaleManager.Instance[LocaleKeys.SetupWizardHelpLinkButton],
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

        public override Result CompleteStep()
        {
            if (string.IsNullOrEmpty(KeysFolderPath) && RyujinxApp.MainWindow.VirtualFileSystem.HasKeySet)
            {
                NotificationManager.Information(
                    title: LocaleManager.Instance[LocaleKeys.RyujinxInfo],
                    text: LocaleManager.GetFormatted(
                        LocaleKeys.SetupWizardKeysPageSkipText,
                        LocaleManager.Instance[LocaleKeys.SetupWizardActionBack]
                    ));
                return Result.Success; // This handles the user selecting no folder and just hitting Next.
            }

            if (!Directory.Exists(KeysFolderPath))
                return Result.Fail;

            try
            {
                string systemDirectory = AppDataManager.KeysDirPath;
                if (AppDataManager.Mode == AppDataManager.LaunchMode.UserProfile &&
                    Directory.Exists(AppDataManager.KeysDirPathUser))
                {
                    systemDirectory = AppDataManager.KeysDirPathUser;
                }

                Logger.Info?.Print(LogClass.Application, $"Installing keys from {KeysFolderPath}");

                ContentManager.InstallKeys(KeysFolderPath, systemDirectory);

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
                        LocaleKeys.DialogKeysInstallerKeysNotFoundErrorMessage, KeysFolderPath);
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
