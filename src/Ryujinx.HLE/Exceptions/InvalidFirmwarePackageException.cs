using System;

namespace Ryujinx.HLE.Exceptions
{
    public class InvalidFirmwarePackageException : Exception
    {
        public InvalidFirmwarePackageException(string message) : base(message) { }
    }
}
