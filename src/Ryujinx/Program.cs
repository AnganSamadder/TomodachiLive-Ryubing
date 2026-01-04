using Avalonia;
using Avalonia.Threading;
using DiscordRPC;
using Gommon;
using Projektanker.Icons.Avalonia;
using Projektanker.Icons.Avalonia.FontAwesome;
using Projektanker.Icons.Avalonia.MaterialDesign;
using Ryujinx.Ava.Systems;
using Ryujinx.Ava.Systems.Configuration;
using Ryujinx.Ava.Systems.Configuration.System;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Ava.UI.Windows;
using Ryujinx.Ava.Utilities;
using Ryujinx.Ava.Utilities.SystemInfo;
using Ryujinx.Common;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.GraphicsDriver;
using Ryujinx.Common.Logging;
using Ryujinx.Common.SystemInterop;
using Ryujinx.Common.Utilities;
using Ryujinx.Graphics.RenderDocApi;
using Ryujinx.Graphics.Vulkan.MoltenVK;
using Ryujinx.Headless;
using Ryujinx.SDL3.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;

namespace Ryujinx.Ava
{
    internal static class Program
    {
        public static double WindowScaleFactor { get; set; }
        public static double DesktopScaleFactor { get; set; } = 1.0;
        public static string Version { get; private set; }
        public static string ConfigurationPath { get; private set; }
        public static string GlobalConfigurationPath { get; private set; }
        public static bool UseExtraConfig { get; set; }
        public static bool PreviewerDetached { get; private set; }
        public static bool UseHardwareAcceleration { get; private set; }
        public static string BackendThreadingArg { get; private set; }

        private const uint MbIconwarning = 0x30;

        public static int Main(string[] args)
        {
            Version = ReleaseInformation.Version;

            if (OperatingSystem.IsWindows())
            {
                if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
                {
                    _ = Win32NativeInterop.MessageBoxA(nint.Zero, "You are running an outdated version of Windows.\n\nRyujinx supports Windows 10 version 20H1 and newer.\n", $"Ryujinx {Version}", MbIconwarning);
                    return 0;
                }

                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

                if (Environment.CurrentDirectory.StartsWithIgnoreCase(programFiles) ||
                    Environment.CurrentDirectory.StartsWithIgnoreCase(programFilesX86))
                {
                    _ = Win32NativeInterop.MessageBoxA(nint.Zero, "Ryujinx is not intended to be run from the Program Files folder. Please move it out and relaunch.", $"Ryujinx {Version}", MbIconwarning);
                    return 0;
                }

                // The names of everything here makes no sense for what this actually checks for. Thanks, Microsoft.
                // If you can't tell by the error string,
                // this actually checks if the current process was run with "Run as Administrator"
                // ...but this reads like it checks if the current is in/has the Windows admin role? lol
                if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                {
                    _ = Win32NativeInterop.MessageBoxA(nint.Zero, "Ryujinx is not intended to be run as administrator.", $"Ryujinx {Version}", MbIconwarning);
                    return 0;
                }
            }

            bool noGuiArg = ConsumeCommandLineArgument(ref args, "--no-gui") || ConsumeCommandLineArgument(ref args, "nogui");
            bool coreDumpArg = ConsumeCommandLineArgument(ref args, "--core-dumps");

            // TODO: Ryujinx causes core dumps on Linux when it exits "uncleanly", eg. through an unhandled exception.
            //       This is undesirable and causes very odd behavior during development (the process stops responding, 
            //       the .NET debugger freezes or suddenly detaches, /tmp/ gets filled etc.), unless explicitly requested by the user.
            //       This needs to be investigated, but calling prctl() is better than modifying system-wide settings or leaving this be.
            if (!coreDumpArg)
            {
                OsUtils.SetCoreDumpable(false);
            }

            PreviewerDetached = true;

            if (noGuiArg)
            {
                HeadlessRyujinx.Entrypoint(args);
                return 0;
            }

            try
            {
                Initialize(args);
            }
            catch
            {
                return 0;
            }

            LoggerAdapter.Register();

            IconProvider.Current
                .Register<FontAwesomeIconProvider>()
                .Register<MaterialDesignIconProvider>();

            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<RyujinxApp>()
                .UsePlatformDetect()
                .With(new X11PlatformOptions
                {
                    EnableMultiTouch = true,
                    EnableIme = true,
                    EnableInputFocusProxy = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") == "gamescope",
                    RenderingMode = UseHardwareAcceleration
                        ? [X11RenderingMode.Glx, X11RenderingMode.Software]
                        : [X11RenderingMode.Software]
                })
                .With(new Win32PlatformOptions
                {
                    WinUICompositionBackdropCornerRadius = 8.0f,
                    RenderingMode = UseHardwareAcceleration
                        ? [Win32RenderingMode.AngleEgl, Win32RenderingMode.Software]
                        : [Win32RenderingMode.Software]
                });

        private static bool ConsumeCommandLineArgument(ref string[] args, string targetArgument)
        {
            List<string> argList = [.. args];
            bool found = argList.Remove(targetArgument);
            args = argList.ToArray();
            return found;
        }

        private static void Initialize(string[] args)
        {
            // Ensure Discord presence timestamp begins at the absolute start of when Ryujinx is launched
            DiscordIntegrationModule.EmulatorStartedAt = Timestamps.Now;

            // Parse arguments
            RyujinxOptions.Read(args, out RyujinxOptions options);

            if (OperatingSystem.IsMacOS())
            {
                MVKInitialization.InitializeResolver();
            }

            // Delete backup files after updating.
            Task.Run(Updater.CleanupUpdate);

            Console.Title = $"{RyujinxApp.FullAppName} Console {Version}";

            // Hook unhandled exception and process exit events.
            AppDomain.CurrentDomain.UnhandledException += (sender, e)
                => ProcessUnhandledException(sender, e.ExceptionObject as Exception, e.IsTerminating);
            TaskScheduler.UnobservedTaskException += (sender, e)
                => ProcessUnhandledException(sender, e.Exception, false);
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Exit();

            // Setup base data directory.
            AppDataManager.Initialize(options.EmuDataBaseDirPath);

            // Initialize the configuration.
            ConfigurationState.Initialize();

            // Initialize the logger system.
            LoggerModule.Initialize();

            // Initialize Discord integration.
            DiscordIntegrationModule.Initialize();

            // Initialize SDL3 driver
            SDL3Driver.MainThreadDispatcher = action => Dispatcher.UIThread.InvokeAsync(action, DispatcherPriority.Input);

            ReloadConfig();

            WindowScaleFactor = ForceDpiAware.GetWindowScaleFactor();

            // Logging system information.
            PrintSystemInfo();

            // Enable OGL multithreading on the driver, and some other flags.
            DriverUtilities.InitDriverConfig(ConfigurationState.Instance.Graphics.BackendThreading == BackendThreading.Off);

            // Check if keys exists.
            if (!File.Exists(Path.Combine(AppDataManager.KeysDirPath, "prod.keys")))
            {
                if (!(AppDataManager.Mode == AppDataManager.LaunchMode.UserProfile && File.Exists(Path.Combine(AppDataManager.KeysDirPathUser, "prod.keys"))))
                {
                    MainWindow.ShowKeyErrorOnLoad = true;
                }
            }

            if (options.LaunchPath != null)
            {
                MainWindow.DeferLoadApplication(options.LaunchPath, options.LaunchApplicationId, options.StartFullscreen);
            }
        }

        public static string GetDirGameUserConfig(string gameId, bool changeFolderForGame = false)
        {
            if (string.IsNullOrEmpty(gameId))
            {
                return "";
            }

            string gameDir = Path.Combine(AppDataManager.GamesDirPath, gameId, ReleaseInformation.ConfigName);

            if (changeFolderForGame)
            {
                ConfigurationPath = gameDir;
                UseExtraConfig = true;
            }

            return gameDir;
        }

        public static void ReloadConfig(bool isRunGameWithCustomConfig = false)
        {
            string localConfigurationPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ReleaseInformation.ConfigName);
            string appDataConfigurationPath = Path.Combine(AppDataManager.BaseDirPath, ReleaseInformation.ConfigName);


            if (!isRunGameWithCustomConfig) // To return settings from the game folder if the user configuration exists
            {
                // Now load the configuration as the other subsystems are now registered
                if (File.Exists(localConfigurationPath))
                {
                    ConfigurationPath = localConfigurationPath;
                }
                else if (File.Exists(appDataConfigurationPath))
                {
                    ConfigurationPath = appDataConfigurationPath;
                }
            }
        
            if (ConfigurationPath == null)
            {
                // No configuration, we load the default values and save it to disk
                ConfigurationPath = appDataConfigurationPath;
                Logger.Notice.Print(LogClass.Application, $"No configuration file found. Saving default configuration to: {ConfigurationPath}");

                ConfigurationState.Instance.LoadDefault();
                ConfigurationState.Instance.ToFileFormat().SaveConfig(ConfigurationPath);
            }
            else
            {
                Logger.Notice.Print(LogClass.Application, $"Loading configuration from: {ConfigurationPath}");

                if (ConfigurationFileFormat.TryLoad(ConfigurationPath, out ConfigurationFileFormat configurationFileFormat))
                {
                    ConfigurationState.Instance.Load(configurationFileFormat, ConfigurationPath);
                }
                else
                {
                    Logger.Warning?.PrintMsg(LogClass.Application, $"Failed to load config! Loading the default config instead.\nFailed config location: {ConfigurationPath}");

                    ConfigurationFileFormat.RenameInvalidConfigFile(ConfigurationPath);

                    ConfigurationState.Instance.LoadDefault();
                }
            }

            // When you first load the program, copy to remember the path for the global configuration
            GlobalConfigurationPath ??= ConfigurationPath;

            UseHardwareAcceleration = ConfigurationState.Instance.EnableHardwareAcceleration;

            // Check if graphics backend was overridden
            if (RyujinxOptions.Shared.GraphicsBackendOverride is not null)
                ConfigurationState.Instance.Graphics.GraphicsBackend.Value =
                    RyujinxOptions.Shared.GraphicsBackendOverride.Value;

            // Check if backend threading was overridden
            if (RyujinxOptions.Shared.BackendThreadingOverride is not null)
                ConfigurationState.Instance.Graphics.BackendThreading.Value =
                    RyujinxOptions.Shared.BackendThreadingOverride.Value;

            if (RyujinxOptions.Shared.BackendThreadingOverrideAfterReboot is not null)
                BackendThreadingArg = RyujinxOptions.Shared.BackendThreadingOverrideAfterReboot.Value.ToString();


            // Check if docked mode was overriden.
            if (RyujinxOptions.Shared.DockedModeOverride.HasValue)
                ConfigurationState.Instance.System.EnableDockedMode.Value =
                    RyujinxOptions.Shared.DockedModeOverride.Value;

            // Check if HideCursor was overridden.
            if (RyujinxOptions.Shared.HideCursorOverride is not null)
                ConfigurationState.Instance.HideCursor.Value = RyujinxOptions.Shared.HideCursorOverride.Value;

            // Check if memoryManagerMode was overridden. 
            if (RyujinxOptions.Shared.MemoryManagerModeOverride is not null)
                ConfigurationState.Instance.System.MemoryManagerMode.Value = RyujinxOptions.Shared.MemoryManagerModeOverride.Value;

            // Check if PPTC was overridden. 
            if (RyujinxOptions.Shared.PptcOverride is not null)
                if (Enum.TryParse(RyujinxOptions.Shared.PptcOverride, true, out bool result))
                {
                    ConfigurationState.Instance.System.EnablePtc.Value = result;
                }

            // Check if region was overridden. 
            if (RyujinxOptions.Shared.SystemRegionOverride is not null)
                ConfigurationState.Instance.System.Region.Value = RyujinxOptions.Shared.SystemRegionOverride.Value;

            //Check if language was overridden. 
            if (RyujinxOptions.Shared.SystemLanguageOverride is not null)
                ConfigurationState.Instance.System.Language.Value = RyujinxOptions.Shared.SystemLanguageOverride.Value;

            // Check if hardware-acceleration was overridden.
            if (RyujinxOptions.Shared.HardwareAccelerationOverride is not null)
                UseHardwareAcceleration = RyujinxOptions.Shared.HardwareAccelerationOverride.Value;
        }

        internal static void PrintSystemInfo()
        {
            Logger.Notice.Print(LogClass.Application,  "   ___                 __    _              ");
            Logger.Notice.Print(LogClass.Application, @"  / _ \  __ __ __ __  / /   (_)  ___   ___ _");
            Logger.Notice.Print(LogClass.Application, @" / , _/ / // // // / / _ \ / /  / _ \ / _ `/");
            Logger.Notice.Print(LogClass.Application, @"/_/|_|  \_, / \_,_/ /_.__//_/  /_//_/ \_, / ");
            Logger.Notice.Print(LogClass.Application,  "       /___/                         /___/  ");


            Logger.Notice.Print(LogClass.Application, $"{RyujinxApp.FullAppName} Version: {Version}");
            Logger.Notice.Print(LogClass.Application, $".NET Runtime: {RuntimeInformation.FrameworkDescription}");
            SystemInfo.Gather().Print();

            Logger.Notice.Print(LogClass.Application, $"Logs Enabled: {Logger.GetEnabledLevels()
                    .FormatCollection(
                        x => x.ToString(),
                        separator: ", ",
                        emptyCollectionFallback: "<None>")}");

            Logger.Notice.Print(LogClass.Application,
                AppDataManager.Mode == AppDataManager.LaunchMode.Custom
                    ? $"Launch Mode: Custom Path {AppDataManager.BaseDirPath}"
                    : $"Launch Mode: {AppDataManager.Mode}");
        }

        internal static void ProcessUnhandledException(object sender, Exception initialException, bool isTerminating)
        {
            Logger.Log log = Logger.Error ?? Logger.Notice;

            List<Exception> exceptions = [];

            if (initialException is AggregateException ae)
            {
                exceptions.AddRange(ae.InnerExceptions);
            }
            else
            {
                exceptions.Add(initialException);
            }

            foreach (Exception e in exceptions)
            {
                string message = $"Unhandled exception caught: {e}";
                // ReSharper disable once ConstantConditionalAccessQualifier
                if (sender?.GetType()?.AsPrettyString() is { } senderName)
                    log.Print(LogClass.Application, message, senderName);
                else
                    log.PrintMsg(LogClass.Application, message);
            }


            if (isTerminating)
            {
                Logger.Flush();
                Exit();
            }
        }

        internal static void Exit()
        {
            DiscordIntegrationModule.Exit();

            Logger.Shutdown();
        }
    }
}
