using CommandLine;
using NUnit.Framework;
using Ryujinx.Ava.Utilities;
using Ryujinx.Headless;

namespace Ryujinx.Tests.Tomodachi
{
    [TestFixture]
    public class TomodachiRuntimeBootstrapTests
    {
        [Test]
        public void GuiAndHeadlessProviderFlagsAreDisabledByDefaultAndExplicitlyEnabled()
        {
            Assert.That(CommandLineState.EnableTomodachiInputProvider, Is.False);
            Assert.That(new Options().EnableTomodachiInputProvider, Is.False);

            CommandLineState.ParseArguments(["--enable-tomodachi-input-provider"]);
            Options parsed = null;
            Parser.Default
                .ParseArguments<Options>(["--enable-tomodachi-input-provider", "game.nsp"])
                .WithParsed(options => parsed = options);

            Assert.Multiple(() =>
            {
                Assert.That(CommandLineState.EnableTomodachiInputProvider, Is.True);
                Assert.That(CommandLineState.Arguments, Does.Contain("--enable-tomodachi-input-provider"));
                Assert.That(parsed, Is.Not.Null);
                Assert.That(parsed.EnableTomodachiInputProvider, Is.True);
            });
        }
    }
}
