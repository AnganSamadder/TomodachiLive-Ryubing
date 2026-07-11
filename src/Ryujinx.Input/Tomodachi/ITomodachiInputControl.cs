namespace Ryujinx.Input.Tomodachi
{
    public interface ITomodachiInputControl
    {
        ArmResult Arm(string armId, TomodachiAuthorityEpoch authority);
        ApplyResult Apply(in TomodachiInputCommand command);
        NeutralizeResult NeutralizeAndLatch(string stopId, NeutralizeReason reason);
        void ObserveBridgeHeartbeat(TomodachiAuthorityEpoch authority);
        bool TryGetCommandReceipt(string commandId, out CommandReceipt receipt);
        NeutralSampleReceipt? GetLastAllNeutralSampleReceipt();
        ProviderHealth GetHealth();
    }

    public interface ITomodachiInputPollSource
    {
        PollResult PollMappedSnapshot();
    }
}
