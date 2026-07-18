using System;

namespace Ryujinx.Input.Tomodachi.Ipc
{
    public readonly record struct TomodachiStatusProofSnapshot(
        string State,
        string GameSaveIdentityDigest,
        long ProviderEpoch,
        DateTimeOffset ObservedAt);

    public interface ITomodachiStatusProofSource
    {
        bool TrySample(out TomodachiStatusProofSnapshot snapshot);
    }
}
