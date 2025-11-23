using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.UI.Controls;
using Ryujinx.Ava.UI.ViewModels;
using System.Threading.Tasks;

namespace Ryujinx.Ava.Systems.SetupWizard
{
    public class SetupWizardPageBuilder(ContentPresenter presenter, bool isFirstPage = false)
    {
        private readonly SetupWizardPage _page = new(isFirstPage);

        public SetupWizardPage Build()
        {
            return _page;
        }

        public SetupWizardPageBuilder WithTitle(LocaleKeys title) => WithTitle(LocaleManager.Instance[title]);

        public SetupWizardPageBuilder WithTitle(string title)
        {
            _page.Title = title;
            return this;
        }

        public SetupWizardPageBuilder WithContent(LocaleKeys content) => WithContent(LocaleManager.Instance[content]);

        public SetupWizardPageBuilder WithContent(object? content)
        {
            if (content is StyledElement { Parent: ContentControl parent })
            {
                parent.Content = null;
            }

            _page.Content = content;
            return this;
        }

        public SetupWizardPageBuilder WithHelpContent(LocaleKeys content) =>
            WithHelpContent(LocaleManager.Instance[content]);

        public SetupWizardPageBuilder WithHelpContent(object? content)
        {
            _page.HelpContent = content;
            return this;
        }

        public SetupWizardPageBuilder WithContent<TControl>(object? context = null) where TControl : Control, new()
        {
            _page.Content = new TControl { DataContext = context };

            return this;
        }

        public SetupWizardPageBuilder WithContent<TControl, TViewModel>(out TViewModel boundViewModel) 
            where TControl : RyujinxControl<TViewModel>, new()
            where TViewModel : BaseModel, new()
        {
            boundViewModel = new();

            return WithContent<TControl>(boundViewModel);
        }

        public SetupWizardPageBuilder WithActionContent(LocaleKeys content) =>
            WithActionContent(LocaleManager.Instance[content]);

        public SetupWizardPageBuilder WithActionContent(object? content)
        {
            _page.ActionContent = content;
            return this;
        }

        public ValueTask<bool> Show()
        {
            return _page.Show(presenter);
        }
    }
}
