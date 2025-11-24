using Gommon;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Ava.UI.ViewModels;

namespace Ryujinx.Ava.UI.SetupWizard
{
    public abstract class SetupWizardPageContext : BaseModel
    {
        public NotificationHelper Notifications { get; init; }

        public abstract Result CompleteStep();
    }
}
