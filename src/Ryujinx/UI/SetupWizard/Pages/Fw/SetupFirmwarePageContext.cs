using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gommon;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Ava.Utilities;
using Ryujinx.Common;
using Ryujinx.Common.Configuration;
using Ryujinx.HLE.FileSystem;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Ryujinx.Ava.UI.SetupWizard.Pages
{
    public partial class SetupFirmwarePageContext() : SetupWizardPageContext(LocaleKeys.SetupWizardFirmwarePageTitle)
    {
        [ObservableProperty] public partial string FirmwareSourcePath { get; set; }

        [RelayCommand]
        private static async Task BrowseFile(TextBox tb)
        {
            Optional<IStorageFile> result =
                await RyujinxApp.MainWindow.ViewModel.StorageProvider.OpenSingleFilePickerAsync(
                    new FilePickerOpenOptions
                    {
                        Title = LocaleManager.Instance[LocaleKeys.SetupWizardFirmwarePageFilePopupTitle],
                        FileTypeFilter = new List<FilePickerFileType>
                        {
                            new(LocaleManager.Instance[LocaleKeys.FileDialogAllTypes])
                            {
                                Patterns = ["*.xci", "*.zip"],
                                AppleUniformTypeIdentifiers = ["com.ryujinx.xci", "public.zip-archive"],
                                MimeTypes = ["application/x-nx-xci", "application/zip"],
                            },
                            new("XCI")
                            {
                                Patterns = ["*.xci"],
                                AppleUniformTypeIdentifiers = ["com.ryujinx.xci"],
                                MimeTypes = ["application/x-nx-xci"],
                            },
                            new("ZIP")
                            {
                                Patterns = ["*.zip"],
                                AppleUniformTypeIdentifiers = ["public.zip-archive"],
                                MimeTypes = ["application/zip"],
                            }
                        }
                    });

            if (result.TryGet(out IStorageFile firmwareFile))
            {
                tb.Text = firmwareFile.TryGetLocalPath();
            }
        }

        [RelayCommand]
        private static async Task BrowseFolder(TextBox tb)
        {
            Optional<IStorageFolder> result =
                await RyujinxApp.MainWindow.ViewModel.StorageProvider.OpenSingleFolderPickerAsync(
                    new FolderPickerOpenOptions
                    {
                        Title = LocaleManager.Instance[LocaleKeys.SetupWizardFirmwarePageFolderPopupTitle]
                    });

            if (result.TryGet(out IStorageFolder firmwareFolder))
            {
                tb.Text = firmwareFolder.TryGetLocalPath();
            }
        }

        public override object CreateHelpContent()
        {
            Grid grid = new()
            {
                RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto)],
                HorizontalAlignment = HorizontalAlignment.Center
            };

            grid.Children.Add(new TextBlock
            {
                Text = LocaleManager.Instance[LocaleKeys.SetupWizardFirmwarePageHelpText],
                HorizontalAlignment = HorizontalAlignment.Center,
                GridRow = 0
            });

            grid.Children.Add(new HyperlinkButton
            {
                Content = LocaleManager.Instance[LocaleKeys.SetupWizardHelpLinkButton],
                NavigateUri = new Uri(SharedConstants.DumpFirmwareWikiUrl),
                HorizontalAlignment = HorizontalAlignment.Center,
                GridRow = 1
            });

            return grid;
        }

        public override Result CompleteStep()
        {
            if (string.IsNullOrEmpty(FirmwareSourcePath) && RyujinxSetupWizard.HasFirmware)
            {
                NotificationManager.Information(
                    title: LocaleManager.Instance[LocaleKeys.RyujinxInfo],
                    text: LocaleManager.GetFormatted(
                        LocaleKeys.SetupWizardFirmwarePageSkipText,
                        LocaleManager.Instance[LocaleKeys.SetupWizardActionBack]
                    )
                );
                return Result.Success; // This handles the user selecting no file/dir and just hitting Next.
            }

            if (!Directory.Exists(FirmwareSourcePath))
                return Result.Fail;

            try
            {
                RyujinxApp.MainWindow.ContentManager.InstallFirmware(FirmwareSourcePath);
                SystemVersion installedFwVer = RyujinxApp.MainWindow.ContentManager.GetCurrentFirmwareVersion();
                if (installedFwVer != null)
                {
                    NotificationManager.Information(
                        LocaleManager.Instance[LocaleKeys.SetupWizardFirmwarePageInstallSuccessNotificationTitle],
                        LocaleManager.GetFormatted(
                            LocaleKeys.SetupWizardFirmwarePageInstallSuccessNotificationTitle,
                            installedFwVer.VersionString
                        )
                    );
                }
                else
                {
                    NotificationManager.Error(
                        LocaleManager.Instance[LocaleKeys.SetupWizardFirmwarePageInstallFailNotificationTitle],
                        LocaleManager.GetFormatted(
                            LocaleKeys.SetupWizardFirmwarePageInstallFailNotificationText,
                            FirmwareSourcePath
                        )
                    );
                }

                RyujinxApp.MainWindow.ViewModel.RefreshFirmwareStatus(installedFwVer, allowNullVersion: true);

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
                NotificationManager.Error(e.Message, waitingExit: true);
                return Result.Fail;
            }

            return Result.Success;
        }
    }
}
