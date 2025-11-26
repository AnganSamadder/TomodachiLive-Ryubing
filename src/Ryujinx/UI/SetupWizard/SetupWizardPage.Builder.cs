using Avalonia;
using Avalonia.Controls;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.UI.Controls;

namespace Ryujinx.Ava.UI.SetupWizard
{
    public partial class SetupWizardPage
    {
        public SetupWizardPage WithTitle(LocaleKeys title) => WithTitle(LocaleManager.Instance[title]);

        public SetupWizardPage WithTitle(string title)
        {
            Title = title;
            return this;
        }

        public SetupWizardPage WithContent(LocaleKeys content) => WithContent(LocaleManager.Instance[content]);

        public SetupWizardPage WithContent(object? content)
        {
            if (content is StyledElement { Parent: ContentControl parent })
            {
                parent.Content = null;
            }

            Content = content;
            return this;
        }

        public SetupWizardPage WithHelpContent(LocaleKeys content) =>
            WithHelpContent(LocaleManager.Instance[content]);

        public SetupWizardPage WithHelpContent(object? content)
        {
            HelpContent = content;
            return this;
        }

        public SetupWizardPage WithContent<TControl>(object? context = null) where TControl : Control, new()
        {
            Content = new TControl { DataContext = context };

            return this;
        }

        public SetupWizardPage WithContent<TControl, TViewModel>(out TViewModel boundViewModel)
            where TControl : RyujinxControl<TViewModel>, new()
            where TViewModel : SetupWizardPageContext, new()
        {
            boundViewModel = new() { NotificationManager = ownerWizard.NotificationManager };

            return WithContent<TControl>(boundViewModel);
        }

        public SetupWizardPage WithActionContent(LocaleKeys content) =>
            WithActionContent(LocaleManager.Instance[content]);

        public SetupWizardPage WithActionContent(object? content)
        {
            ActionContent = content;
            return this;
        }
    }
}
