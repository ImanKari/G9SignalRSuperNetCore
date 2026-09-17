using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using G9SignalRSuperNetCore.Server.Classes.Abstracts;
using G9SignalRSuperNetCore.Server.Classes.Attributes;
using G9SignalRSuperNetCore.Server.Classes.FileUpload;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.SignalR;
using PolyType;

namespace G9SignalRSuperNetCore.Tests.Infrastructure;

public enum TestKind
{
    Soil,
    Leaf
}

/// <summary>An app DTO with the member kinds that trip serializers: GUID, date, floating point, arrays, bytes, enum, null.</summary>
public sealed record TestReading(Guid Id, string Label, double Value, DateTime TakenUtc, int[] Samples, byte[] Blob, TestKind Kind, string? Note)
{
    /// <summary>Value equality including the arrays (records compare array references).</summary>
    public string Describe() =>
        $"{Id:N}|{Label}|{Value:R}|{TakenUtc:O}|{string.Join(',', Samples)}|{Convert.ToHexString(Blob)}|{Kind}|{Note ?? "<null>"}";

    public static TestReading Create(int n) => new(
        new Guid(n, 7, 9, 1, 2, 3, 4, 5, 6, 7, 8),
        "reading-" + n,
        n / 3d,
        new DateTime(2026, 9, 17, 8, 0, 0, DateTimeKind.Utc).AddSeconds(n),
        [n, n * 2, -n],
        [(byte)n, 0, 255],
        n % 2 == 0 ? TestKind.Soil : TestKind.Leaf,
        n % 3 == 0 ? null : "note " + n);
}

/// <summary>The app's shapes: only its own hub types. File-transfer and presence types come from the library.</summary>
[GenerateShapeFor<TestReading>]
[GenerateShapeFor<List<TestReading>>]
[GenerateShapeFor<string>]
[GenerateShapeFor<int>]
[GenerateShapeFor<long>]
public sealed partial class TestShapes;

public interface ITestHubClient
{
    Task Poked(TestReading reading);

    Task UploadProgress(G9DtUploadProgress progress);
}

public sealed class TestHub : G9AHubBase<TestHub, ITestHubClient>
{
    public const string Route = "/test";

    [RequiresDynamicCode("Test hub; SignalR Hub<T> requires dynamic code.")]
    public TestHub()
    {
    }

    public override string RoutePattern() => Route;

    /// <summary>"Binary" or "Text": the transfer format of this connection, which the hub protocol decides.</summary>
    public Task<string> TransferFormat() =>
        Task.FromResult(Context.Features.Get<ITransferFormatFeature>()?.ActiveFormat.ToString() ?? "unknown");

    public Task<TestReading> Echo(TestReading reading) => Task.FromResult(reading);

    public Task<List<TestReading>> Batch(int count) => Task.FromResult(Enumerable.Range(1, count).Select(TestReading.Create).ToList());

    public async IAsyncEnumerable<TestReading> Readings(int count, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 1; i <= count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return TestReading.Create(i);
        }
    }

    public async Task<long> Sum(IAsyncEnumerable<int> values)
    {
        long total = 0;
        await foreach (var value in values) total += value;
        return total;
    }

    public Task Poke(int n) => Clients.Caller.Poked(TestReading.Create(n));

    // --- 2.7.0 call semantics (review T03) -------------------------------------------------------
    // Counters are static because the hub is constructed per invocation; every test reads a delta.

    /// <summary>How many times <see cref="SlowWork"/> has finished.</summary>
    public static int SlowWorkCompleted;

    /// <summary>A no-result method that takes a while: awaiting it must mean the server finished it.</summary>
    public async Task SlowWork(int delayMs)
    {
        await Task.Delay(delayMs, Context.ConnectionAborted);
        Interlocked.Increment(ref SlowWorkCompleted);
    }

    /// <summary>A no-result method that fails: awaiting it must surface the failure.</summary>
    public Task AlwaysFails() => throw new HubException("the server refused");

    /// <summary>Rate limited, so calling it makes the filter allocate a bucket for this connection.</summary>
    [G9AttrRateLimit(1000, 1000)]
    public Task<int> Limited(int n) => Task.FromResult(n);

    /// <summary>The opt-out: returns to the caller without waiting for the server.</summary>
    [G9AttrOneWay]
    public async Task FireAndForget(int delayMs)
    {
        await Task.Delay(delayMs, Context.ConnectionAborted);
        Interlocked.Increment(ref SlowWorkCompleted);
    }

    public Task<G9DtBeginUploadResult> BeginUpload(string uploadId, string fileName, long totalBytes, int chunkSize, string declaredSha256Hex) =>
        Uploads.BeginAsync(uploadId, fileName, totalBytes, chunkSize, declaredSha256Hex, Context.ConnectionAborted).AsTask();

    public Task<G9DtUploadResult> UploadChunks(string uploadId, IAsyncEnumerable<byte[]> chunks)
    {
        var caller = Clients.Caller;
        return Uploads.AppendChunksAsync(uploadId, chunks,
            async bytes => await caller.UploadProgress(new G9DtUploadProgress { UploadId = uploadId, BytesReceived = bytes, TotalBytes = 0 }),
            Context.ConnectionAborted).AsTask();
    }

    public Task<G9DtBeginDownloadResult> BeginDownload(string fileName, long resumeFrom, int chunkSize) =>
        Uploads.BeginDownloadAsync(fileName, resumeFrom, chunkSize, Context.ConnectionAborted).AsTask();

    public IAsyncEnumerable<byte[]> DownloadChunks(string fileName, long resumeFrom, int chunkSize, CancellationToken cancellationToken) =>
        Uploads.StreamFileAsync(fileName, resumeFrom, chunkSize, cancellationToken);

    private IG9UploadService Uploads =>
        Context.GetHttpContext()?.RequestServices.GetService(typeof(IG9UploadService)) as IG9UploadService
        ?? throw new InvalidOperationException("IG9UploadService is not registered.");
}
