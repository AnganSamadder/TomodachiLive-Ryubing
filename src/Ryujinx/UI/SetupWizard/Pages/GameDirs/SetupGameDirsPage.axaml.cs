using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Ryujinx.Ava;
using Ryujinx.Ava.UI.Controls;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Ava.Utilities;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Ryujinx.UI.SetupWizard.Pages
{
    public partial class SetupGameDirsPage : RyujinxControl<SetupGameDirsPageContext>
    {
        public SetupGameDirsPage()
        {
            InitializeComponent();
            AddGameDirButton.Command =
                Commands.Create(() => AddDirButton(GameDirPathBox, ViewModel.GameDirs));
            AddAutoloadDirButton.Command =
                Commands.Create(() => AddDirButton(AutoloadDirPathBox, ViewModel.UpdateAndDlcDirs));
        }

        private async Task AddDirButton(TextBox addDirBox, ObservableCollection<string> directories)
        {
            string path = addDirBox.Text;

            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path) && !directories.Contains(path))
            {
                directories.Add(path);

                addDirBox.Clear();
            }
            else
            {
                Gommon.Optional<IStorageFolder> folder = await RyujinxApp.MainWindow.ViewModel.StorageProvider.OpenSingleFolderPickerAsync();

                if (folder.HasValue)
                {
                    directories.Add(folder.Value.Path.LocalPath);
                }
            }
        }

        private void RemoveGameDirButton_OnClick(object sender, RoutedEventArgs e)
        {
            int oldIndex = GameDirsList.SelectedIndex;

            foreach (string path in new List<string>(GameDirsList.SelectedItems.Cast<string>()))
            {
                ViewModel.GameDirs.Remove(path);
            }

            if (GameDirsList.ItemCount > 0)
            {
                GameDirsList.SelectedIndex = oldIndex < GameDirsList.ItemCount ? oldIndex : 0;
            }
        }

        private void RemoveAutoloadDirButton_OnClick(object sender, RoutedEventArgs e)
        {
            int oldIndex = AutoloadDirsList.SelectedIndex;

            foreach (string path in new List<string>(AutoloadDirsList.SelectedItems.Cast<string>()))
            {
                ViewModel.UpdateAndDlcDirs.Remove(path);
            }

            if (AutoloadDirsList.ItemCount > 0)
            {
                AutoloadDirsList.SelectedIndex = oldIndex < AutoloadDirsList.ItemCount ? oldIndex : 0;
            }
        }
    }
}

