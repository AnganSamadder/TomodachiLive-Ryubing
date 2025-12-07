using Avalonia.Controls;
using Avalonia.Layout;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using Gommon;
using Ryujinx.Ava;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.Systems.Configuration;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Ava.UI.SetupWizard;
using Ryujinx.Common;
using System;
using System.Linq;

namespace Ryujinx.UI.SetupWizard.Pages
{
    public partial class SetupGameDirsPageContext() : SetupWizardPageContext(LocaleKeys.SetupWizardGameDirsPageTitle)
    {
        [ObservableProperty] 
        public partial ObservableCollection<string> GameDirs { get; set; } 
            = new(ConfigurationState.Instance.UI.GameDirs);
        [ObservableProperty] 
        public partial ObservableCollection<string> UpdateAndDlcDirs { get; set; } 
            = new(ConfigurationState.Instance.UI.AutoloadDirs);

        public override Result CompleteStep()
        {
            if (GameDirs.Count is 0)
            {
                NotificationManager.Error(LocaleManager.Instance[LocaleKeys.SetupWizardGameDirsPageNoFoldersSelectedError]);
                return Result.Fail;
            }

            ConfigurationState.Instance.UI.GameDirs.Value = GameDirs.ToList();
            ConfigurationState.Instance.UI.AutoloadDirs.Value = UpdateAndDlcDirs.ToList();
            OwningWizard.SignalConfigModified();
            RyujinxApp.MainWindow.LoadApplications();

            return Result.Success;
        }

        public override object CreateHelpContent()
        {
            Grid grid = new()
            {
                RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto)],
                HorizontalAlignment = HorizontalAlignment.Center
            };

            grid.Children.Add(new TextBlock
            {
                Text = LocaleManager.Instance[LocaleKeys.SetupWizardGameDirsPageHelpText],
                HorizontalAlignment = HorizontalAlignment.Center,
                GridRow = 0
            });

            grid.Children.Add(new HyperlinkButton
            {
                Content = LocaleManager.Instance[LocaleKeys.SetupWizardHelpLinkButton],
                HorizontalAlignment = HorizontalAlignment.Center,
                NavigateUri = new Uri(SharedConstants.DumpContentWikiUrl),
                GridRow = 1
            });

            return grid;
        }
    }
}
