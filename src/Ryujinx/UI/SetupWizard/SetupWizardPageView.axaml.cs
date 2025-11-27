using Avalonia.Controls;
using Avalonia.Interactivity;
using Ryujinx.Ava.UI.Controls;

using Ryujinx.Common.Helper;

namespace Ryujinx.Ava.UI.SetupWizard
{
    public partial class SetupWizardPageView : RyujinxControl<SetupWizardPage>
    {
        public SetupWizardPageView()
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

