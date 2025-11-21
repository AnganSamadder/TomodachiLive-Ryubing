using Ryujinx.Ava;
using Ryujinx.Ava.Systems.Configuration;
using Ryujinx.Ava.Systems.SetupWizard;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Ava.UI.SetupWizard;
using Ryujinx.Ava.UI.ViewModels;
using Ryujinx.Ava.UI.Windows;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using System;
using System.IO;

namespace Ryujinx.UI.SetupWizard
{
    public partial class RyujinxSetupWizardWindow : StyleableAppWindow
    {
        public static bool IsUsingSetupWizard { get; set; }
        
        public RyujinxSetupWizardWindow() : base(useCustomTitleBar: true)
        {
            InitializeComponent();

            if (Program.PreviewerDetached)
            {
                FlushControls.IsVisible = !ConfigurationState.Instance.ShowOldUI;
            }
        }

        public static RyujinxSetupWizardWindow CreateWindow(MainWindowViewModel mwvm, out BaseSetupWizard setupWizard)
        {
            RyujinxSetupWizardWindow window = new();
            window.DataContext = setupWizard = new RyujinxSetupWizard(window.WizardPresenter, mwvm, () =>
            {
                NotificationHelper.SetNotificationManager(RyujinxApp.MainWindow);
                window.Close();
                IsUsingSetupWizard = false;
            });
            window.Height = 600;
            window.Width = 750;
            NotificationHelper.SetNotificationManager(window);
            return window;
        }

        public static bool CanShowSetupWizard =>
            !File.Exists(Path.Combine(AppDataManager.BaseDirPath, ".DoNotShowSetupWizard"));

        public static bool DisableSetupWizard()
        {
            if (!CanShowSetupWizard)
                return false; //cannot disable; file already doesn't exist, so it's disabled.

            string disableFile = Path.Combine(AppDataManager.BaseDirPath, ".DoNotShowSetupWizard");

            try
            {
                File.Create(disableFile, 0).Dispose();
                File.SetAttributes(disableFile, File.GetAttributes(disableFile) | FileAttributes.Hidden);
                return true;
            }
            catch (Exception e)
            {
                Logger.Error?.PrintStack(LogClass.Application, e.Message);
                return false;
            }
        }

        public static bool EnableSetupWizard()
        {
            if (CanShowSetupWizard)
                return false; //cannot enable; file already exists, so it's enabled.

            string disableFile = Path.Combine(AppDataManager.BaseDirPath, ".DoNotShowSetupWizard");

            try
            {
                File.Delete(disableFile);
                return true;
            }
            catch (Exception e)
            {
                Logger.Error?.PrintStack(LogClass.Application, e.Message);
                return false;
            }
        }
    }
}

