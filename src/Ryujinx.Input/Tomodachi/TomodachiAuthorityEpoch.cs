namespace Ryujinx.Input.Tomodachi
{
    public readonly record struct TomodachiAuthorityEpoch(
        string ServerInstanceId,
        string RoomId,
        string ControlLeaseId,
        string SessionId);
}
