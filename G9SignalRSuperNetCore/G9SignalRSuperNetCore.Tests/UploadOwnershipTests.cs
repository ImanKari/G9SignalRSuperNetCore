using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using G9SignalRSuperNetCore.Client.FileUpload;
using G9SignalRSuperNetCore.Server.Classes.Errors;
using G9SignalRSuperNetCore.Server.Classes.FileUpload;
using G9SignalRSuperNetCore.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ServerUploadStatus = G9SignalRSuperNetCore.Server.Classes.FileUpload.G9EUploadStatus;

namespace G9SignalRSuperNetCore.Tests;

/// <summary>
///     2.9 file-transfer ownership: per-user namespaces for partials, randomized committed names, the authorization
///     hook, and the periodic cleanup of expired partials (which must take a partial's .meta with its .bin).
/// </summary>
public sealed class UploadOwnershipTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "g9-upload-ownership-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Two_owners_with_the_same_upload_id_never_share_or_resume_each_others_partial()
    {
        var service = Service(o => o.PerUserNamespace = true);
        var alice = Encoding.UTF8.GetBytes("alice's private content");
        var bob = Encoding.UTF8.GetBytes("bob uploads something else entirely");

        // Alice sends part of her file.
        await service.BeginAsync("alice", "same-id", "a.txt", alice.Length, 64 * 1024, Sha256(alice));
        await service.AppendChunksAsync("alice", "same-id", Chunks(alice[..5]), null);

        // Bob uses the same upload id with different content: no metadata conflict, and nothing of Alice's to resume.
        var bobBegin = await service.BeginAsync("bob", "same-id", "b.txt", bob.Length, 64 * 1024, Sha256(bob));
        Assert.Equal(0, bobBegin.BytesAlreadyReceived);
        var bobResult = await service.AppendChunksAsync("bob", "same-id", Chunks(bob), null);
        Assert.Equal(ServerUploadStatus.Completed, bobResult.Status);

        // Alice resumes where SHE left off and completes with her own bytes.
        var aliceBegin = await service.BeginAsync("alice", "same-id", "a.txt", alice.Length, 64 * 1024, Sha256(alice));
        Assert.Equal(5, aliceBegin.BytesAlreadyReceived);
        var aliceResult = await service.AppendChunksAsync("alice", "same-id", Chunks(alice[5..]), null);
        Assert.Equal(ServerUploadStatus.Completed, aliceResult.Status);

        Assert.Equal(alice, await File.ReadAllBytesAsync(aliceResult.FinalPath!));
        Assert.Equal(bob, await File.ReadAllBytesAsync(bobResult.FinalPath!));
    }

    [Fact]
    public async Task Owners_whose_ids_only_differ_in_unsafe_characters_are_still_kept_apart()
    {
        var service = Service(o => o.PerUserNamespace = true);
        var first = Encoding.UTF8.GetBytes("first owner content");
        var second = Encoding.UTF8.GetBytes("second owner has other content");

        await service.BeginAsync("a.b", "id", "x.txt", first.Length, 64 * 1024, Sha256(first));
        // "a-b" and "a.b" reduce to the same readable segment; the owner hash must still separate them.
        var begin = await service.BeginAsync("a-b", "id", "y.txt", second.Length, 64 * 1024, Sha256(second));

        Assert.Equal(0, begin.BytesAlreadyReceived);
        Assert.Equal(2, Directory.GetFiles(PartialDirectory, "*.meta").Length);
    }

    [Fact]
    public async Task Without_the_namespace_the_same_upload_id_is_one_upload_as_before()
    {
        var service = Service();
        var alice = Encoding.UTF8.GetBytes("alice's content");
        var bob = Encoding.UTF8.GetBytes("bob's different content");
        await service.BeginAsync("alice", "same-id", "a.txt", alice.Length, 64 * 1024, Sha256(alice));

        var conflict = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.BeginAsync("bob", "same-id", "b.txt", bob.Length, 64 * 1024, Sha256(bob)));
        Assert.Equal(G9CErrorCodes.UploadMetadataConflict, conflict.Message);
    }

    [Fact]
    public async Task Randomized_names_commit_under_a_random_name_with_the_safe_extension_and_report_it()
    {
        var service = Service(o => o.RandomizeCommittedNames = true);
        var bytes = Encoding.UTF8.GetBytes("same bytes, same declared name");

        var first = await CommitAsync(service, "u1", "../Quarterly Report.TXT", bytes);
        var second = await CommitAsync(service, "u2", "../Quarterly Report.TXT", bytes);

        Assert.Matches(new Regex("^[0-9a-f]{32}\\.txt$"), first.StoredFileName!);
        Assert.Matches(new Regex("^[0-9a-f]{32}\\.txt$"), second.StoredFileName!);
        // The "already committed under this name" shortcut does not apply: the second upload is its own file.
        Assert.NotEqual(first.StoredFileName, second.StoredFileName);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(FilesDirectory, first.StoredFileName!)));
        Assert.False(File.Exists(Path.Combine(FilesDirectory, "Quarterly Report.TXT")));
    }

    [Theory]
    [InlineData("archive.TAR.GZ", ".gz")]
    [InlineData("noextension", "")]
    [InlineData("weird.p$d#f", ".pdf")]
    [InlineData("long.abcdefghijklmnopqrstuvwxyz", ".abcdefghijklmno")]
    public async Task Randomized_names_keep_only_a_safe_extension(string declared, string expectedExtension)
    {
        var service = Service(o => o.RandomizeCommittedNames = true);
        var result = await CommitAsync(service, "u1", declared, Encoding.UTF8.GetBytes("content of " + declared));

        Assert.Equal(32 + expectedExtension.Length, result.StoredFileName!.Length);
        Assert.EndsWith(expectedExtension, result.StoredFileName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_randomization_the_stored_name_is_the_declared_one_and_is_reported_on_begin_too()
    {
        var service = Service();
        var bytes = Encoding.UTF8.GetBytes("plain name content");
        var committed = await CommitAsync(service, "u1", "plain.txt", bytes);
        Assert.Equal("plain.txt", committed.StoredFileName);

        var again = await service.BeginAsync("u2", "plain.txt", bytes.Length, 64 * 1024, Sha256(bytes));
        Assert.True(again.AlreadyCompleted);
        Assert.Equal("plain.txt", again.StoredFileName);
    }

    [Fact]
    public async Task The_authorization_hook_is_asked_for_every_operation_and_its_refusal_is_G9_UPLOAD_FORBIDDEN()
    {
        var asked = new List<G9DtUploadAuthorizationContext>();
        var service = Service(o => o.Authorize = (context, _) =>
        {
            lock (asked) asked.Add(context);
            return ValueTask.FromResult(context.OwnerId == "alice");
        });
        var bytes = Encoding.UTF8.GetBytes("owned by alice");

        // Begin: refused for Bob with an exception carrying the code.
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.BeginAsync("bob", "u1", "a.txt", bytes.Length, 64 * 1024, Sha256(bytes)));
        Assert.Equal(G9CErrorCodes.UploadForbidden, refused.Message);

        // Append: Alice begins; Bob appending to that upload gets a Failed result with the code, and nothing is written.
        await service.BeginAsync("alice", "u1", "a.txt", bytes.Length, 64 * 1024, Sha256(bytes));
        var append = await service.AppendChunksAsync("bob", "u1", Chunks(bytes), null);
        Assert.Equal(ServerUploadStatus.Failed, append.Status);
        Assert.Equal(G9CErrorCodes.UploadForbidden, append.ErrorCode);
        Assert.False(File.Exists(Path.Combine(PartialDirectory, "u1.bin")));

        var done = await service.AppendChunksAsync("alice", "u1", Chunks(bytes), null);
        Assert.Equal(ServerUploadStatus.Completed, done.Status);

        // Downloads: refused for Bob, allowed for Alice.
        var download = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.BeginDownloadAsync("bob", done.StoredFileName!, 0, 1024));
        Assert.Equal(G9CErrorCodes.UploadForbidden, download.Message);
        Assert.False((await service.BeginDownloadAsync("alice", done.StoredFileName!, 0, 1024)).NotFound);
        var stream = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in service.StreamFileAsync("bob", done.StoredFileName!, 0, 1024)) { }
        });
        Assert.Equal(G9CErrorCodes.UploadForbidden, stream.Message);

        // What the hook saw: the operation, the owner, and the size (from the metadata on append).
        lock (asked)
        {
            Assert.Contains(asked, c => c is { Operation: G9EUploadOperation.Begin, OwnerId: "bob", UploadId: "u1", FileName: "a.txt" }
                                        && c.TotalBytes == bytes.Length);
            Assert.Contains(asked, c => c is { Operation: G9EUploadOperation.Append, OwnerId: "bob" } && c.TotalBytes == bytes.Length);
            Assert.Contains(asked, c => c is { Operation: G9EUploadOperation.BeginDownload, OwnerId: "alice" } && c.TotalBytes == bytes.Length);
            Assert.Contains(asked, c => c is { Operation: G9EUploadOperation.Download, OwnerId: "bob" });
        }
    }

    [Fact]
    public async Task Cleanup_removes_an_expired_partial_together_with_its_metadata()
    {
        var service = Service(o => o.PartialTtl = TimeSpan.FromHours(1));
        var bytes = Encoding.UTF8.GetBytes("abandoned half way");
        await service.BeginAsync("u1", "a.txt", bytes.Length, 64 * 1024, Sha256(bytes));
        await service.AppendChunksAsync("u1", Chunks(bytes[..4]), null);
        var bin = Path.Combine(PartialDirectory, "u1.bin");
        var meta = Path.Combine(PartialDirectory, "u1.meta");
        Age(bin, TimeSpan.FromHours(2));
        Age(meta, TimeSpan.FromHours(2));

        Assert.Equal(2, service.CleanupExpiredPartials());
        Assert.False(File.Exists(bin));
        Assert.False(File.Exists(meta));
    }

    [Fact]
    public async Task Cleanup_keeps_old_metadata_whose_data_is_still_being_written()
    {
        // The .meta is written once at begin; a slow upload keeps its .bin fresh. Judged alone, the .meta would expire
        // and the upload would lose its metadata mid-way.
        var service = Service(o => o.PartialTtl = TimeSpan.FromHours(1));
        var bytes = Encoding.UTF8.GetBytes("slow but alive upload");
        await service.BeginAsync("u1", "a.txt", bytes.Length, 64 * 1024, Sha256(bytes));
        await service.AppendChunksAsync("u1", Chunks(bytes[..4]), null);
        Age(Path.Combine(PartialDirectory, "u1.meta"), TimeSpan.FromHours(2));

        Assert.Equal(0, service.CleanupExpiredPartials());
        Assert.True(File.Exists(Path.Combine(PartialDirectory, "u1.meta")));
        Assert.True(File.Exists(Path.Combine(PartialDirectory, "u1.bin")));
    }

    [Fact]
    public async Task The_hosted_cleanup_service_runs_the_cleanup_every_interval()
    {
        var options = new G9DtUploadOptions
        {
            RootDirectory = FilesDirectory,
            PartialTtl = TimeSpan.FromHours(1),
            CleanupInterval = TimeSpan.FromMilliseconds(100)
        };
        var service = new G9CUploadService(Options.Create(options), NullLogger<G9CUploadService>.Instance);
        var bytes = Encoding.UTF8.GetBytes("left behind");
        await service.BeginAsync("u1", "a.txt", bytes.Length, 64 * 1024, Sha256(bytes));
        await service.AppendChunksAsync("u1", Chunks(bytes[..3]), null);
        Age(Path.Combine(PartialDirectory, "u1.bin"), TimeSpan.FromHours(2));
        Age(Path.Combine(PartialDirectory, "u1.meta"), TimeSpan.FromHours(2));

        using var cleanup = new G9CUploadCleanupService(service, Options.Create(options), NullLogger<G9CUploadCleanupService>.Instance);
        await cleanup.StartAsync(CancellationToken.None);
        try
        {
            Assert.True(await Eventually.TrueAsync(() => Directory.GetFiles(PartialDirectory).Length == 0));
        }
        finally
        {
            await cleanup.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void File_upload_registration_adds_the_cleanup_service_once()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddOptions();
        Server.G9SignalRSuperNetCoreServer.AddG9SignalRSuperNetCoreFileUpload(services, o => o.RootDirectory = FilesDirectory);
        Server.G9SignalRSuperNetCoreServer.AddG9SignalRSuperNetCoreFileUpload(services, o => o.RootDirectory = FilesDirectory);

        Assert.Single(services, d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)
                                     && d.ImplementationType == typeof(G9CUploadCleanupService));
    }

    [Theory]
    [InlineData(TestProtocol.Json)]
    [InlineData(TestProtocol.MessagePack)]
    public async Task The_client_result_carries_the_stored_name_of_a_randomized_upload(TestProtocol protocol)
    {
        await using var server = await TestServer.StartAsync(offerMessagePack: protocol == TestProtocol.MessagePack,
            configureServices: services => services.Configure<G9DtUploadOptions>(o => o.RandomizeCommittedNames = true));
        await using var client = await server.ConnectAsync(protocol);
        var source = Path.Combine(_root, "source", "Holiday Photo.JPG");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        await File.WriteAllBytesAsync(source, RandomNumberGenerator.GetBytes(100_000));

        var result = await new G9CFileUploader(client.Connection).UploadAsync(source);

        Assert.Equal(G9SignalRSuperNetCore.Client.FileUpload.G9EUploadStatus.Completed, result.Status);
        Assert.Matches(new Regex("^[0-9a-f]{32}\\.jpg$"), result.StoredFileName!);
        Assert.Equal(await File.ReadAllBytesAsync(source), await File.ReadAllBytesAsync(Path.Combine(server.UploadRoot, result.StoredFileName!)));
    }

    private string FilesDirectory => Path.Combine(_root, "files");

    private string PartialDirectory => Path.Combine(FilesDirectory, ".partial");

    private G9CUploadService Service(Action<G9DtUploadOptions>? configure = null)
    {
        var options = new G9DtUploadOptions { RootDirectory = FilesDirectory };
        configure?.Invoke(options);
        return new G9CUploadService(Options.Create(options), NullLogger<G9CUploadService>.Instance);
    }

    private static async Task<G9SignalRSuperNetCore.Server.Classes.FileUpload.G9DtUploadResult> CommitAsync(
        G9CUploadService service, string uploadId, string name, byte[] bytes)
    {
        await service.BeginAsync(uploadId, name, bytes.Length, 64 * 1024, Sha256(bytes));
        var result = await service.AppendChunksAsync(uploadId, Chunks(bytes), null);
        Assert.Equal(ServerUploadStatus.Completed, result.Status);
        return result;
    }

    private static void Age(string path, TimeSpan by) => File.SetLastWriteTimeUtc(path, DateTime.UtcNow - by);

    private static async IAsyncEnumerable<byte[]> Chunks(byte[] bytes)
    {
        await Task.CompletedTask;
        yield return bytes;
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A handle may still be closing; the OS cleans the temp folder eventually.
        }
    }
}
