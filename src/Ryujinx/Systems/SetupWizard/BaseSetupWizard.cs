using Avalonia.Controls.Presenters;
using Ryujinx.Ava.Common.Locale;
using System.Threading.Tasks;

namespace Ryujinx.Ava.Systems.SetupWizard
{
    public abstract class BaseSetupWizard(ContentPresenter presenter)
    {
        /// <summary>
        /// Define the logic and flow of this <see cref="BaseSetupWizard"/>.
        /// </summary>
        public abstract Task Start();

        protected ValueTask<bool> FirstPage()
        {
            SetupWizardPageBuilder builder = new(presenter, isFirstPage: true);

            return builder
                .WithTitle(LocaleKeys.SetupWizardFirstPageTitle)
                .WithContent(LocaleKeys.SetupWizardFirstPageContent)
                .WithActionContent(LocaleKeys.SetupWizardFirstPageAction)
                .Show();
        }

        protected SetupWizardPageBuilder NextPage()
            => new(presenter);
    }
}
