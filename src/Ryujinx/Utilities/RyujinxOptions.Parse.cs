using CommandLine;
using Gommon;
using Ryujinx.Common.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using Error = Gommon.Error;

namespace Ryujinx.Ava.Utilities
{
    public partial class RyujinxOptions
    {
        public static RyujinxOptions Shared { get; private set; }

        public static Result Read(string[] args, out RyujinxOptions options)
        {
            options = null;
            args = PatchLegacyArgumentNames(args);

            ParserResult<RyujinxOptions> parseResult =
                Parser.ParseArguments<RyujinxOptions>(args);

            if (parseResult is NotParsed<RyujinxOptions> notParsed)
            {
                if (notParsed.Errors.None(x =>
                        x.Tag is ErrorType.HelpRequestedError or ErrorType.HelpVerbRequestedError))
                {
                    Logger.Notice.Print(LogClass.Application, "Failed to parse command-line arguments:");
                    foreach (var error in notParsed.Errors)
                    {
                        Logger.Notice.Print(LogClass.Application, $"    - {error.Tag}");
                    }
                }

                return Result.Fail;
            }

            options = Shared = parseResult.Value;

            return parseResult.Value.Init(args);
        }

        private static readonly Lazy<Parser> _parser = new(() => new Parser(settings =>
        {
            settings.HelpWriter = Logger.WriterProxy;
            settings.CaseInsensitiveEnumValues = true;
            settings.CaseSensitive = false;
            settings.MaximumDisplayWidth -= (int)(settings.MaximumDisplayWidth * 0.175);
        }));

        public static Parser Parser => _parser.Value;

        private static readonly Dictionary<string, string> _legacyArgs = new()
        {
            { "-rdct", "--rd-capture-title-format" },
            { "-la", "--local-only-amiibo" }
        };

        public static string[] PatchLegacyArgumentNames(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
                args[i] = Patch(args[i]);

            return args;

            string Patch(string arg) => _legacyArgs.TryGetValue(arg, out string newArgName) ? newArgName : arg;
        }
    }
}
