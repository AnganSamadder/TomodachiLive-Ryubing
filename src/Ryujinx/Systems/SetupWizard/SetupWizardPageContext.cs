using Gommon;
using Ryujinx.Ava.UI.ViewModels;

namespace Ryujinx.Ava.Systems.SetupWizard
{
    public abstract class SetupWizardPageContext: BaseModel
    {
        public abstract Result CompleteStep();
    }
}
