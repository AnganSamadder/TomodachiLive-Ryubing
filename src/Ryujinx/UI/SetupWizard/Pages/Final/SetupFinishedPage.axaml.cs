using Avalonia.Controls;
using Avalonia.Interactivity;
using Ryujinx.Ava.UI.Controls;
using Ryujinx.Common.Helper;

namespace Ryujinx.Ava.UI.SetupWizard.Pages
{
    public partial class SetupFinishedPage : RyujinxControl<SetupFinishedPageContext>
    {
        public SetupFinishedPage()
        {
            InitializeComponent();
        }
        
        private void Button_OnClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string url })
                OpenHelper.OpenUrl(url);
        }
    }
}

