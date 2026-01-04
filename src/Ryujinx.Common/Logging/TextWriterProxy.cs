using System;
using System.IO;
using System.Text;

namespace Ryujinx.Common.Logging
{
    internal class TextWriterProxy : TextWriter
    {
        public override Encoding Encoding => Console.OutputEncoding;

        public override void Write(string value)
        {
            if (value is null) return;

            foreach (var line in value.Split(Console.Out.NewLine))
            {
                Logger.Info?.PrintMsg(LogClass.Application, line);
            }
        }
    }
}
