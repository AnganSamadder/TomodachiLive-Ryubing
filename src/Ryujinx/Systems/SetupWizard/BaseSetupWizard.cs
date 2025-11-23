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

        protected SetupWizardPage FirstPage() 
            => new(presenter, isFirstPage: true);

        protected SetupWizardPage NextPage()
            => new(presenter);
    }
}
