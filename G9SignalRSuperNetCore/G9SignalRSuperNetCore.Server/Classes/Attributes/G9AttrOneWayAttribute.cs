namespace G9SignalRSuperNetCore.Server.Classes.Attributes;

/// <summary>
///     Marks a hub method as fire-and-forget: the generated client sends it and returns without waiting
///     for the server.
/// </summary>
/// <remarks>
///     <para>By default a generated method returning <see cref="System.Threading.Tasks.Task"/> or
///     <see cref="System.Threading.Tasks.ValueTask"/> is an <i>acknowledged</i> call, so awaiting it
///     waits for the server method to finish and a server-side exception surfaces at the caller - the
///     same contract as a method that returns a value. Put this attribute on a method where that wait
///     is not wanted (telemetry pings, presence beacons, anything where throughput matters more than
///     knowing it arrived).</para>
///     <para>What you give up: the returned task completes once the message is written to the
///     connection. It does not mean the server ran the method, and an exception thrown there cannot be
///     observed through that task.</para>
///     <para>Before 2.7.0 every no-result method behaved this way, whether or not you wanted it. If you
///     were relying on that, add this attribute to keep it.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class G9AttrOneWayAttribute : Attribute;
