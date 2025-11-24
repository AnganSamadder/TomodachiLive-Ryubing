using Avalonia.Controls.Presenters;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.UI.ViewModels;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.Ava.UI.SetupWizard
{
    public partial class SetupWizardPage(ContentPresenter contentPresenter, RyujinxSetupWizard ownerWizard, bool isFirstPage = false) : BaseModel
    {
        private bool? _result;
        private readonly CancellationTokenSource _cts = new();

        public bool IsFirstPage { get; } = isFirstPage;

        [ObservableProperty]
        public partial string? Title { get; set; }

        [ObservableProperty]
        public partial object? Content { get; set; }

        [ObservableProperty] public partial object? HelpContent { get; set; }

        [ObservableProperty]
        public partial object? ActionContent { get; set; } = LocaleManager.Instance[LocaleKeys.SetupWizardActionNext];

        [RelayCommand]
        private void MoveBack()
        {
            _result = false;
            _cts.Cancel();
        }

        [RelayCommand]
        private void MoveNext()
        {
            _result = true;
            _cts.Cancel();
        }

        public async ValueTask<bool> Show()
        {
            contentPresenter.Content = new SetupWizardPageView { ViewModel = this };

            try
            {
                await Task.Delay(-1, _cts.Token);
            }
            catch (TaskCanceledException)
            {
                return _result ?? false;
            }

            return false;
        }
    }
}
