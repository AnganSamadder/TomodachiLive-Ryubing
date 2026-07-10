namespace Ryujinx.Input.Tomodachi
{
    public interface ITomodachiInputControl
    {
        ArmResult Arm(string armId, TomodachiAuthorityEpoch authority);
        ApplyResult Apply(in TomodachiInputCommand command);
        NeutralizeResult NeutralizeAndLatch(string stopId, NeutralizeReason reason);
        void ObserveBridgeHeartbeat(TomodachiAuthorityEpoch authority);
        ProviderHealth GetHealth();
    }

    public interface ITomodachiInputPollSource
    {
        PollResult PollMappedSnapshot();
    }
}
