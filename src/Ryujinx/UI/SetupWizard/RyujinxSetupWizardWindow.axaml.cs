using Avalonia.Controls;
using Ryujinx.Ava.Systems.Configuration;
using Ryujinx.Ava.UI.Windows;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Ryujinx.Ava.UI.SetupWizard
{
    public partial class RyujinxSetupWizardWindow : StyleableAppWindow
    {
        public static bool IsOpen { get; set; }

        public RyujinxSetupWizardWindow() : base(useCustomTitleBar: true)
        {
            InitializeComponent();

            if (Program.PreviewerDetached)
            {
                FlushControls.IsVisible = !ConfigurationState.Instance.ShowOldUI;
            }
        }

        public static Task ShowAsync(bool overwriteMode, Window owner = null)
        {
            if (!CanShowSetupWizard)
                return Task.CompletedTask;

            Task windowTask = ShowAsync(
                CreateWindow(out RyujinxSetupWizard wiz, overwriteMode),
                owner
            );
            _ = wiz.Start();
            return windowTask.ContinueWith(_ => wiz.Dispose());
        }

        public static RyujinxSetupWizardWindow CreateWindow(out RyujinxSetupWizard setupWizard, bool overwriteMode = false)
        {
            RyujinxSetupWizardWindow window = new();
            window.DataContext = setupWizard = new RyujinxSetupWizard(window, overwriteMode);
            window.Height = 700;
            window.Width = 825;
            return window;
        }

        public static bool CanShowSetupWizard =>
            !File.Exists(Path.Combine(AppDataManager.BaseDirPath, ".DoNotShowSetupWizard"));

        public static bool DisableSetupWizard()
        {
            if (!CanShowSetupWizard)
                return false; //cannot disable; file exists, so it's already disabled.

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
                return false; //cannot enable; file does not exist, so it's already enabled.

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
