namespace G9SignalRSuperNetCore.Server.Classes.DataTypes;

public class G9DtClientSideMethod
{
    public required string MethodName { set; get; }

    public required G9DtClientSideMethodParameter[] Parameters { set; get; }
}