using System;

namespace Ryujinx.Input.Tomodachi
{
    public enum TomodachiButtonAction
    {
        Press,
        Release,
    }

    public readonly record struct TomodachiInputCommand(
        string CommandId,
        TomodachiAuthorityEpoch Authority,
        long Sequence,
        DateTimeOffset ExpiresAt,
        GamepadButtonInputId Button,
        TomodachiButtonAction Action,
        string TraceId);
}
