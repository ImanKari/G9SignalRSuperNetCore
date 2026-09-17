using System.Security.Cryptography;
using System.Text;
using G9SignalRSuperNetCore.Server.Classes.Errors;
using G9SignalRSuperNetCore.Server.Classes.FileUpload;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace G9SignalRSuperNetCore.Tests;

/// <summary>
///     A completed upload is proven, not assumed from a file name, and one upload id stands for one piece
///     of content. Reporting an upload complete because something else was committed under that name would
///     tell a client its data is safe when it was never sent.
/// </summary>
public sealed class UploadIdentityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "g9-upload-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task A_second_upload_of_the_same_bytes_under_the_same_name_is_already_complete()
    {
        var service = Service();
        var bytes = Encoding.UTF8.GetBytes("the same content");
        await CommitAsync(service, "u1", "report.txt", bytes);

        var again = await service.BeginAsync("u2", "report.txt", bytes.Length, 64 * 1024, Sha256(bytes));

        Assert.True(again.AlreadyCompleted);
        Assert.Equal(bytes.Length, again.BytesAlreadyReceived);
    }

    [Fact]
    public async Task A_different_file_under_a_committed_name_is_refused_instead_of_reported_complete()
    {
        var service = Service();
        await CommitAsync(service, "u1", "report.txt", Encoding.UTF8.GetBytes("the original content"));
        var other = Encoding.UTF8.GetBytes("completely different content, and a different length too");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.BeginAsync("u2", "report.txt", other.Length, 64 * 1024, Sha256(other)));

        Assert.Equal(G9CErrorCodes.UploadNameConflict, failure.Message);
    }

    [Fact]
    public async Task Same_name_and_same_size_but_different_bytes_is_still_refused()
    {
        // The size check alone would pass this one: only the hash separates them.
        var service = Service();
        await CommitAsync(service, "u1", "report.txt", Encoding.UTF8.GetBytes("AAAAAAAAAAAAAAAA"));
        var other = Encoding.UTF8.GetBytes("BBBBBBBBBBBBBBBB");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.BeginAsync("u2", "report.txt", other.Length, 64 * 1024, Sha256(other)));

        Assert.Equal(G9CErrorCodes.UploadNameConflict, failure.Message);
    }

    [Fact]
    public async Task Resuming_an_upload_id_with_different_content_is_refused()
    {
        var service = Service();
        var first = Encoding.UTF8.GetBytes("the first content");
        var second = Encoding.UTF8.GetBytes("a different file entirely");
        await service.BeginAsync("u1", "a.txt", first.Length, 64 * 1024, Sha256(first));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.BeginAsync("u1", "b.txt", second.Length, 64 * 1024, Sha256(second)));

        Assert.Equal(G9CErrorCodes.UploadMetadataConflict, failure.Message);
    }

    [Fact]
    public async Task Resuming_the_same_upload_with_a_different_chunk_size_is_allowed()
    {
        // A resume may negotiate a different chunk size; that changes how the bytes are carried, not what they are.
        var service = Service();
        var bytes = Encoding.UTF8.GetBytes("partially uploaded content");
        await service.BeginAsync("u1", "a.txt", bytes.Length, 64 * 1024, Sha256(bytes));
        await service.AppendChunksAsync("u1", Chunks(bytes[..5]), null);

        var resumed = await service.BeginAsync("u1", "a.txt", bytes.Length, 16 * 1024, Sha256(bytes));

        Assert.False(resumed.AlreadyCompleted);
        Assert.Equal(5, resumed.BytesAlreadyReceived);
    }

    private async Task CommitAsync(G9CUploadService service, string uploadId, string name, byte[] bytes)
    {
        await service.BeginAsync(uploadId, name, bytes.Length, 64 * 1024, Sha256(bytes));
        var result = await service.AppendChunksAsync(uploadId, Chunks(bytes), null);
        Assert.Equal(G9EUploadStatus.Completed, result.Status);
    }

    private G9CUploadService Service() => new(
        Options.Create(new G9DtUploadOptions { RootDirectory = Path.Combine(_root, "files") }),
        NullLogger<G9CUploadService>.Instance);

    private static async IAsyncEnumerable<byte[]> Chunks(byte[] bytes)
    {
        await Task.CompletedTask;
        yield return bytes;
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
