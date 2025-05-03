namespace G9SignalRSuperNetCore.ConsoleClient;

public class DtSimpleResult
{
    public bool RequestStatus { set; get; }

    public string? RejectMessage { set; get; }

    public static DtSimpleResult True()
    {
        return new DtSimpleResult
        {
            RequestStatus = true,
            RejectMessage = null
        };
    }

    public static DtSimpleResult False(string? rejectMessage = null)
    {
        return new DtSimpleResult
        {
            RequestStatus = false,
            RejectMessage = rejectMessage
        };
    }
}