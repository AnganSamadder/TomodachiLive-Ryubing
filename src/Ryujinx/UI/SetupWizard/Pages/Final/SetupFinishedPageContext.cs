using Gommon;
using Ryujinx.Ava.Common.Locale;

namespace Ryujinx.Ava.UI.SetupWizard.Pages
{
    public class SetupFinishedPageContext() : SetupWizardPageContext(LocaleKeys.SetupWizardFinalPageTitle)
    {
        public override LocaleKeys ActionContent => LocaleKeys.SetupWizardFinalPageAction;

        // informative step; this implementation is not called.
        public override Result CompleteStep() => Result.Success;
    }
}
