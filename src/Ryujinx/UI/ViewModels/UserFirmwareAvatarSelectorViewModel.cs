using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using DynamicData;
using Ryujinx.Ava.UI.Models;
using Ryujinx.HLE.FileSystem;
using System.Collections.ObjectModel;
using Color = Avalonia.Media.Color;

namespace Ryujinx.Ava.UI.ViewModels
{
    public partial class UserFirmwareAvatarSelectorViewModel : BaseModel
    {
        private static FirmwareAvatarCache _avatarCache;

        [ObservableProperty]
        public partial ObservableCollection<ProfileImageModel> Images { get; set; }
        
        public Color BackgroundColor
        {
            get;
            set
            {
                field = value;
                OnPropertyChanged();
                ChangeImageBackground();
            }
        } = Colors.White;

        public UserFirmwareAvatarSelectorViewModel()
        {
            Images = [];

            LoadImagesFromStore();
        }

        public int SelectedIndex
        {
            get;
            set
            {
                field = value;

                SelectedImage = field == -1 
                    ? null 
                    : Images[field].Data;

                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedImage));
            }
        }

        public byte[] SelectedImage { get; private set; }

        private void LoadImagesFromStore()
        {
            Images.Clear();

            Images.AddRange(_avatarCache.CreateProfileImageModels());
        }

        private void ChangeImageBackground()
        {
            foreach (ProfileImageModel image in Images)
            {
                image.BackgroundColor = new SolidColorBrush(BackgroundColor);
            }
        }

        public static void PreloadAvatars(ContentManager contentManager, VirtualFileSystem virtualFileSystem)
        {
            _avatarCache ??= new FirmwareAvatarCache(contentManager, virtualFileSystem);
        }
    }
}
