using Gommon;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Ava.UI.ViewModels;


namespace Ryujinx.Ava.UI.SetupWizard
{
    public abstract class SetupWizardPageContext(LocaleKeys title) : BaseModel
    {
        public RyujinxSetupWizard OwningWizard
        {
            get;
            init
            {
                field = value;
                NotificationManager = field.NotificationManager;
            }
        }

        public RyujinxNotificationManager NotificationManager { get; private init; }

        public LocaleKeys Title => title;

        public virtual LocaleKeys ActionContent => LocaleKeys.SetupWizardActionNext;

        // ReSharper disable once UnusedMemberInSuper.Global
        // it's used implicitly as we use this type as a where guard for generics for WithContent<TControl, TContext>,
        // it also ensures all context types implement completion
        public abstract Result CompleteStep();
        
#nullable enable
        public virtual object? CreateHelpContent() => null;
    }
}
