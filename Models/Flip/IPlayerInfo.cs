namespace Coflnet.Sky.Commands.Shared;
public interface IPlayerInfo
{
    long Purse { get; set; }
    long AhSlotsOpen { get; set; }
    AccountTier SessionTier { get; set; }
}