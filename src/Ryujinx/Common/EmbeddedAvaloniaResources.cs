using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Gommon;
using System;

namespace Ryujinx.Ava.Common
{
    public static class EmbeddedAvaloniaResources
    {
        public const string LogoPathFormat = "resm:Ryujinx.Assets.UIImages.Logo_{0}_{1}.png?assembly=Ryujinx";

        public static Bitmap LoadBitmap(string uri) 
            => new(AssetLoader.Open(new Uri(uri)));

        public static Bitmap GetIconByNameAndTheme(string iconName, bool isDarkTheme)
        {
            string themeName = isDarkTheme ? "Dark" : "Light";

            return LoadBitmap(LogoPathFormat.Format(iconName, themeName));
        }

        public static Bitmap GetIconByNameAndTheme(string iconName, string theme)
        {
            bool isDarkTheme = theme == "Dark" ||
                               (theme == "Auto" && RyujinxApp.DetectSystemTheme() == ThemeVariant.Dark);

            return GetIconByNameAndTheme(iconName, isDarkTheme);
        }
    }
}
