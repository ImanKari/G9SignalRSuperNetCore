namespace G9SignalRSuperNetCore.ConsoleClient;

public class DtSimpleResultWithExtraData : DtSimpleResult
{
    public object? Data { set; get; }

    public static DtSimpleResultWithExtraData True(object data)
    {
        return new DtSimpleResultWithExtraData
        {
            RequestStatus = true,
            RejectMessage = null,
            Data = data
        };
    }

    public new static DtSimpleResultWithExtraData False(string? rejectMessage = null)
    {
        return new DtSimpleResultWithExtraData
        {
            RequestStatus = false,
            RejectMessage = rejectMessage
        };
    }
}