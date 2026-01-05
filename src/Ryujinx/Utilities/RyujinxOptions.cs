using Avalonia.Controls;
using CommandLine;
using Gommon;
using Ryujinx.Ava.Systems.Configuration.System;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;

namespace Ryujinx.Ava.Utilities
{
    public partial class RyujinxOptions
    {
        public string[] InputArguments { get; private set; }

        public bool? DockedModeOverride { get; private set; }

        public bool? HardwareAccelerationOverride { get; private set; }

        public Optional<FilePath> FirmwareToInstallPath { get; set; }

        // Ideally I'd use an enum parse, like --docked-mode=Handheld,
        // but I want to maintain backwards compatibility with shortcuts made a long time ago, as best we can.
        public Result Init(string[] args)
        {
            InputArguments = args;

            {
                // Docked Mode Override
                if (DockedMode && HandheldMode)
                {
                    return Result.MessageFailure(
                        "Cannot be in both docked and handheld mode at the same time; choose only one.");
                }

                if (DockedMode) DockedModeOverride = true;
                if (HandheldMode) DockedModeOverride = false;
            }
            {
                // Hardware Acceleration Override
                if (SoftwareGui)
                {
                    HardwareAccelerationOverride = false;
                }
            }

            FirmwareToInstallPath = Optional.Of(FirmwareToInstallPathRaw)
                .Convert(x => new FilePath(x))
                .OnlyIf(fp =>
                {
                    bool result = fp is { ExistsAsFile: true, Extension: "xci" or "zip" } || fp.ExistsAsDirectory;
                    if (!result)
                    {
                        Logger.Notice.PrintMsg(LogClass.UI,
                            "Invalid firmware type provided. Path must be a directory, or a .zip or .xci file.");
                    }

                    return result;
                });

            return Result.Success;
        }

        [Option("docked-mode", Required = false, Default = false,
            HelpText = "Launch the game in Docked mode. Causes an error if used in tandem with --handheld-mode.")]
        public bool DockedMode { get; set; }

        [Option("handheld-mode", Required = false, Default = false,
            HelpText = "Launch the game in Handheld mode. Causes an error if used in tandem with --docked-mode.")]
        public bool HandheldMode { get; set; }

        [Option("software-gui", Required = false, Default = false,
            HelpText = "Disables hardware-accelerated rendering for Avalonia. Required for launching with RenderDoc.")]
        public bool SoftwareGui { get; set; }

        [Option('g', "graphics-backend", Required = false, Default = null,
            HelpText = "Select the Graphics backend to use when launching.")]
        public GraphicsBackend? GraphicsBackendOverride { get; set; }

        [Option("backend-threading", Required = false, Default = null,
            HelpText = "Select the Graphics backend threading option to use when launching.")]
        public BackendThreading? BackendThreadingOverride { get; set; }

        [Option("bt", Required = false, Default = null, Hidden = true)]
        public BackendThreading? BackendThreadingOverrideAfterReboot { get; set; }

        [Option("pptc", Required = false, Default = null,
            HelpText = "Enable/disable PPTC regardless of your settings when launching.")]
        public string PptcOverride { get; set; }

        [Option('m', "memory-manager-mode", Required = false, Default = null,
            HelpText = "Select the memory manager mode to use when launching.")]
        public MemoryManagerMode? MemoryManagerModeOverride { get; set; }

        [Option("system-region", Required = false, Default = null,
            HelpText = "Select the Region to use for the emulated Switch when launching.")]
        public Region? SystemRegionOverride { get; set; }

        [Option("system-language", Required = false, Default = null,
            HelpText = "Select the Language to use for the emulated Switch when launching.")]
        public Language? SystemLanguageOverride { get; set; }

        [Option("hide-cursor", Required = false, Default = null,
            HelpText = "Select the cursor hiding strategy to use when launching.")]
        public HideCursorMode? HideCursorOverride { get; set; }

        [Option('r', "root-data-dir", Required = false, Default = null,
            HelpText = "Select the folder to use for all of your Ryujinx save data, configs, etc.")]
        public string EmuDataBaseDirPath { get; set; }

        [Option("rd-capture-title-format", Required = false,
            HelpText =
                "Set the format string used for RenderDoc Capture titles when using the Start/Stop Capture buttons in Ryujinx.",
            Default = "{EmuVersion}\n{GuestName} {GuestVersion} {GuestTitleId} {GuestArch}")]
        public string RenderDocCaptureTitleFormat { get; set; }

        [Option("install-firmware", Required = false, Default = null,
            HelpText =
                "Specify a file path containing Switch firmware to install immediately after starting. Must be a directory or a .zip or .xci file.")]
        public string FirmwareToInstallPathRaw { get; set; }

        [Option('p', "profile", Required = false, Default = null,
            HelpText = "The profile name to open the application with. Defaults to your last used profile.")]
        public string Profile { get; set; }

        [Option('i', "application-id", Required = false, Default = null,
            HelpText = "Specify which application ID out of the specified content archive path to launch.")]
        public string LaunchApplicationId { get; set; }

        [Option('f', "fullscreen", Required = false, Default = false,
            HelpText = "Start the emulator in fullscreen mode.")]
        public bool StartFullscreen { get; set; }

        [Option("hide-updates", Required = false, Default = false, HelpText = "Hides update prompt/notification.")]
        public bool HideAvailableUpdates { get; set; }

        [Option("local-only-amiibo", Required = false, Default = false,
            HelpText = "Only use the local Amiibo cache; do not update it even if there is an update.")]
        public bool OnlyLocalAmiibo { get; set; }

        [Option("core-dumps", Required = false, Default = false,
            HelpText = "Enable coredumps on Linux platforms. They are disabled by default.")]
        public bool CoreDumpsEnabled { get; set; }

        [Value(0, Default = null, Required = false,
            HelpText =
                "The Nintendo Switch application content archive to launch immediately after starting, if desired.")]
        public string LaunchPath { get; set; }
    }
}
