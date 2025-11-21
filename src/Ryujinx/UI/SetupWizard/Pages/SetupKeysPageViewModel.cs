using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ryujinx.Ava;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.UI.ViewModels;
using System.Threading.Tasks;

namespace Ryujinx.Ava.UI.SetupWizard.Pages
{
    public partial class SetupKeysPageViewModel : BaseModel
    {
        [ObservableProperty]
        public partial string? KeysFolderPath { get; set; }

        [RelayCommand]
        private static async Task Browse(TextBox tb)
        {
            var result = await RyujinxApp.MainWindow.ViewModel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions {
                Title = LocaleManager.Instance[LocaleKeys.SetupWizardKeysPageFolderPopupTitle],
                AllowMultiple = false
            }) switch {
                [var target] => target.TryGetLocalPath(),
                _ => null
            };

            if (result is not null) 
            {
                tb.Text = result;
            }
        }
    }
}
