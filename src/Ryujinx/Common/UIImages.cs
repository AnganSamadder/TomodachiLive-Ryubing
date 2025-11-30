using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Gommon;
using System;

namespace Ryujinx.Ava.Common
{
    // ReSharper disable once InconsistentNaming
    // UiImages is ugly, so no
    public static class UIImages
    {
        public const string LogoPathFormat = "resm:Ryujinx.Assets.UIImages.Logo_{0}_{1}.png?assembly=Ryujinx";
        public const string IconPathFormat = "resm:Ryujinx.Assets.UIImages.Icon_{0}.png?assembly=Ryujinx";

        public static Bitmap LoadBitmap(string uri)
            => new(AssetLoader.Open(new Uri(uri)));

        public static Bitmap GetIconByName(string iconName)
            => LoadBitmap(IconPathFormat.Format(iconName));

        public static Bitmap GetLogoByNameAndTheme(string iconName, bool isDarkTheme) =>
            LoadBitmap(LogoPathFormat.Format(iconName, 
                    isDarkTheme 
                        ? "Dark" 
                        : "Light"
                )
            );

        public static Bitmap GetLogoByNameAndVariant(string iconName, string theme) 
            => LoadBitmap(LogoPathFormat.Format(iconName, theme));
    }
}
