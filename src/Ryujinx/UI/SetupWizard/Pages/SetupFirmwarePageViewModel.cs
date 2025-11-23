using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gommon;
using Ryujinx.Ava;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.UI.ViewModels;
using Ryujinx.Ava.Utilities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Ryujinx.Ava.UI.SetupWizard.Pages
{
    public partial class SetupFirmwarePageViewModel : BaseModel
    {
        [ObservableProperty]
        public partial string FirmwareSourcePath { get; set; }

        [RelayCommand]
        private static async Task BrowseFile(TextBox tb)
        {
            Optional<IStorageFile> result = await RyujinxApp.MainWindow.ViewModel.StorageProvider.OpenSingleFilePickerAsync(new FilePickerOpenOptions
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
            Optional<IStorageFolder> result = await RyujinxApp.MainWindow.ViewModel.StorageProvider.OpenSingleFolderPickerAsync(new FolderPickerOpenOptions 
            {
                Title = LocaleManager.Instance[LocaleKeys.SetupWizardFirmwarePageFolderPopupTitle]
            });

            if (result.TryGet(out IStorageFolder firmwareFolder)) 
            {
                tb.Text = firmwareFolder.TryGetLocalPath();
            }
        }
    }
}
