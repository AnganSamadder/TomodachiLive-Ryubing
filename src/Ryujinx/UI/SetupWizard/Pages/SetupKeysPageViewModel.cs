using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gommon;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.UI.ViewModels;
using Ryujinx.Ava.Utilities;
using System.Threading.Tasks;

namespace Ryujinx.Ava.UI.SetupWizard.Pages
{
    public partial class SetupKeysPageViewModel : BaseModel
    {
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
    }
}
