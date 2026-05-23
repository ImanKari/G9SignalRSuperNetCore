using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using G9SignalRSuperNetCore.Client.FileUpload;
using G9SignalRSuperNetCore.Sample.Shared;

namespace G9SignalRSuperNetCore.ConsoleClient;

/// <summary>
///     Tabbed Consolonia test harness for the G9SignalRSuperNetCore stack.
///     Demonstrates: connect/disconnect, chat send/receive, fetch recent messages,
///     resumable file upload with progress, and rate-limit verification.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Connection-tab log lines.</summary>
    public ObservableCollection<string> ConnectionLines { get; } = new();
    /// <summary>Chat-tab log lines.</summary>
    public ObservableCollection<string> ChatLines { get; } = new();
    /// <summary>Recent-messages tab log lines.</summary>
    public ObservableCollection<string> RecentLines { get; } = new();
    /// <summary>File-upload tab log lines.</summary>
    public ObservableCollection<string> UploadLines { get; } = new();
    /// <summary>Rate-limit tab log lines.</summary>
    public ObservableCollection<string> RateLimitLines { get; } = new();

    private TestChatClient? _client;
    private CancellationTokenSource? _uploadCts;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        // Bind list boxes to observable collections.
        Find<ListBox>("ConnectionLog")!.ItemsSource = ConnectionLines;
        Find<ListBox>("ChatLog")!.ItemsSource = ChatLines;
        Find<ListBox>("RecentLog")!.ItemsSource = RecentLines;
        Find<ListBox>("UploadLog")!.ItemsSource = UploadLines;
        Find<ListBox>("RateLimitLog")!.ItemsSource = RateLimitLines;

        // Wire commands.
        Find<Button>("ConnectButton")!.Click += async (_, _) => await ConnectAsync().ConfigureAwait(false);
        Find<Button>("DisconnectButton")!.Click += async (_, _) => await DisconnectAsync().ConfigureAwait(false);
        Find<Button>("SendButton")!.Click += async (_, _) => await SendAsync().ConfigureAwait(false);
        Find<Button>("GetRecentButton")!.Click += async (_, _) => await GetRecentAsync().ConfigureAwait(false);
        Find<Button>("UploadButton")!.Click += async (_, _) => await UploadAsync().ConfigureAwait(false);
        Find<Button>("CancelUploadButton")!.Click += (_, _) => _uploadCts?.Cancel();
        Find<Button>("BrowseButton")!.Click += async (_, _) => await BrowseAsync().ConfigureAwait(false);
        Find<Button>("RateLimitButton")!.Click += async (_, _) => await RateLimitBurstAsync().ConfigureAwait(false);
        Find<Button>("ClearConnectionButton")!.Click += (_, _) => ConnectionLines.Clear();
        Find<Button>("ClearChatButton")!.Click += (_, _) => ChatLines.Clear();
        Find<Button>("ClearRecentButton")!.Click += (_, _) => RecentLines.Clear();
        Find<Button>("ClearUploadButton")!.Click += (_, _) => UploadLines.Clear();
        Find<Button>("ClearRateLimitButton")!.Click += (_, _) => RateLimitLines.Clear();
        Find<Button>("ExitButton")!.Click += (_, _) =>
            (Application.Current?.ApplicationLifetime as IControlledApplicationLifetime)?.Shutdown();
    }

    private T Find<T>(string name) where T : Control => this.FindControl<T>(name)!;

    // ---------------- Connection tab ----------------

    private async Task ConnectAsync()
    {
        var url = Find<TextBox>("ServerUrlBox")!.Text ?? "https://localhost:7159";
        Log(ConnectionLines, $"Connecting to {url} ...");
        try
        {
            _client = new TestChatClient(this, url);
            await _client.ConnectAsync().ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Find<Button>("ConnectButton")!.IsEnabled = false;
                Find<Button>("DisconnectButton")!.IsEnabled = true;
                Find<Button>("SendButton")!.IsEnabled = true;
                Find<Button>("GetRecentButton")!.IsEnabled = true;
                Find<Button>("UploadButton")!.IsEnabled = true;
                Find<Button>("RateLimitButton")!.IsEnabled = true;
                Find<TextBlock>("StatusText")!.Text = $" G9SignalRSuperNetCore  |  Connected to {url} ";
            });
            Log(ConnectionLines, "Connected.");
        }
        catch (Exception ex)
        {
            Log(ConnectionLines, "Connect failed: " + ex.Message);
        }
    }

    private async Task DisconnectAsync()
    {
        if (_client is null) return;
        try
        {
            await _client.DisconnectAsync().ConfigureAwait(false);
            await _client.DisposeAsync().ConfigureAwait(false);
            _client = null;
            Log(ConnectionLines, "Disconnected.");
        }
        catch (Exception ex)
        {
            Log(ConnectionLines, "Disconnect failed: " + ex.Message);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Find<Button>("ConnectButton")!.IsEnabled = true;
                Find<Button>("DisconnectButton")!.IsEnabled = false;
                Find<Button>("SendButton")!.IsEnabled = false;
                Find<Button>("GetRecentButton")!.IsEnabled = false;
                Find<Button>("UploadButton")!.IsEnabled = false;
                Find<Button>("RateLimitButton")!.IsEnabled = false;
                Find<TextBlock>("StatusText")!.Text = " G9SignalRSuperNetCore  |  Disconnected ";
            });
        }
    }

    // ---------------- Chat tab ----------------

    private async Task SendAsync()
    {
        if (_client is null) return;
        var user = Find<TextBox>("UserNameBox")!.Text ?? "anon";
        var msg = Find<TextBox>("MessageBox")!.Text ?? string.Empty;
        try
        {
            await _client.Server.SendMessage(user, msg).ConfigureAwait(false);
            Log(ChatLines, $"-> SendMessage(\"{user}\", \"{msg}\")");
        }
        catch (Exception ex)
        {
            Log(ChatLines, "Send failed: " + ex.Message);
        }
    }

    // ---------------- Recent tab ----------------

    private async Task GetRecentAsync()
    {
        if (_client is null) return;
        try
        {
            var list = await _client.Server.GetRecentMessages().ConfigureAwait(false);
            Log(RecentLines, $"<- GetRecentMessages returned {list.Count} items");
            foreach (var item in list) Log(RecentLines, "    " + item);
        }
        catch (Exception ex)
        {
            Log(RecentLines, "GetRecentMessages failed: " + ex.Message);
        }
    }

    // ---------------- File upload tab ----------------

    private async Task BrowseAsync()
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null)
            {
                Log(UploadLines, "Browse not available: no top-level window.");
                return;
            }

            var picker = top.StorageProvider;
            if (picker is null || !picker.CanOpen)
            {
                // Headless / picker not supported: tell the user to type the path manually.
                Log(UploadLines, "File picker isn't available on this terminal. Type a path into the File path field.");
                return;
            }

            var files = await picker.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Pick a file to upload",
                AllowMultiple = false
            }).ConfigureAwait(false);

            if (files is null || files.Count == 0) return;

            // file://… -> local path
            var path = files[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(path))
            {
                Log(UploadLines, $"Selected: {files[0].Name} (no local path; the picker returned a stream-only handle)");
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
                Find<TextBox>("UploadPathBox")!.Text = path);
        }
        catch (Exception ex)
        {
            Log(UploadLines, "Browse failed: " + ex.Message);
        }
    }

    /// <summary>
    ///     Resolves a typed-in file path against three candidate locations so the user can run
    ///     the harness from any working directory: the literal path, the running app's base
    ///     directory, and the current working directory.
    /// </summary>
    private static string? ResolveFilePath(string typed)
    {
        if (string.IsNullOrWhiteSpace(typed)) return null;
        if (Path.IsPathRooted(typed) && File.Exists(typed)) return typed;

        var candidates = new[]
        {
            typed,
            Path.Combine(AppContext.BaseDirectory, typed),
            Path.Combine(Directory.GetCurrentDirectory(), typed)
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return Path.GetFullPath(c);

        return null;
    }

    private async Task UploadAsync()
    {
        if (_client is null) return;
        var typed = Find<TextBox>("UploadPathBox")!.Text ?? "file_upload_test.zip";
        var path = ResolveFilePath(typed);
        if (path is null)
        {
            Log(UploadLines, $"File not found. Looked for:");
            Log(UploadLines, $"   • {typed}");
            Log(UploadLines, $"   • {Path.Combine(AppContext.BaseDirectory, typed)}");
            Log(UploadLines, $"   • {Path.Combine(Directory.GetCurrentDirectory(), typed)}");
            Log(UploadLines, "Click [ Browse... ] to pick a file or drop the test zip into the project folder.");
            return;
        }

        if (!int.TryParse(Find<TextBox>("ChunkSizeBox")!.Text, out var chunkKb) || chunkKb <= 0) chunkKb = 64;

        _uploadCts = new CancellationTokenSource();
        var ct = _uploadCts.Token;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Find<Button>("UploadButton")!.IsEnabled = false;
            Find<Button>("CancelUploadButton")!.IsEnabled = true;
            Find<TextBlock>("UploadStatus")!.Text = "Hashing & beginning upload ...";
            Find<ProgressBar>("UploadBar")!.Value = 0;
        });

        var uploader = new G9CFileUploader(_client.Connection, new G9DtUploadClientOptions
        {
            ChunkSize = chunkKb * 1024,
            OnRetry = info =>
            {
                var reason = info.Reason switch
                {
                    G9EUploadRetryReason.AttemptFailed =>
                        $"attempt {info.Attempt + 1}/{info.MaxRetries} failed: {info.Exception?.GetType().Name} {info.Exception?.Message}",
                    G9EUploadRetryReason.Interrupted =>
                        $"attempt {info.Attempt + 1}/{info.MaxRetries} interrupted at {info.BytesAlreadyOnServer} B",
                    _ => "retry"
                };
                Log(UploadLines, $"… {reason}; resuming from {info.BytesAlreadyOnServer} B in {info.Backoff.TotalMilliseconds:F0} ms");
            }
        });

        var sw = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;

        var progress = new Progress<G9DtUploadClientProgress>(p =>
        {
            // Throttle UI updates to ~10 Hz.
            if (sw.Elapsed - lastReport < TimeSpan.FromMilliseconds(100) && p.BytesSent < p.TotalBytes) return;
            lastReport = sw.Elapsed;

            var percent = p.TotalBytes > 0 ? 100.0 * p.BytesSent / p.TotalBytes : 0;
            Dispatcher.UIThread.Post(() =>
            {
                Find<ProgressBar>("UploadBar")!.Value = percent;
                Find<TextBlock>("UploadStatus")!.Text = string.Format(
                    "{0,6:F2}%  sent={1}  acked={2}/{3}  {4:F0} KB/s  elapsed={5:mm\\:ss}",
                    percent,
                    HumanBytes(p.BytesSent), HumanBytes(p.BytesAcknowledged), HumanBytes(p.TotalBytes),
                    p.BytesPerSecond / 1024.0,
                    p.Elapsed);
            });
        });

        Log(UploadLines, $"Starting upload of {Path.GetFileName(path)} ({HumanBytes(new FileInfo(path).Length)})");
        Log(UploadLines, $"  source: {path}");
        Log(UploadLines, $"  chunk:  {chunkKb} KB");

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                Find<TextBlock>("UploadStatus")!.Text = "Hashing file & calling BeginUpload …");
            var result = await uploader.UploadAsync(path, progress, serverAckProgress: true, ct).ConfigureAwait(false);
            switch (result.Status)
            {
                case G9EUploadStatus.Completed:
                    Log(UploadLines, $"OK — committed {result.BytesWritten} B at {result.FinalPath}");
                    Log(UploadLines, $"     SHA-256 = {result.Sha256}");
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Find<ProgressBar>("UploadBar")!.Value = 100;
                        Find<TextBlock>("UploadStatus")!.Text = "Completed.";
                    });
                    break;
                case G9EUploadStatus.Interrupted:
                    Log(UploadLines, $"Interrupted at {result.BytesWritten} B (resumable). Click [ Upload ] again to resume.");
                    await Dispatcher.UIThread.InvokeAsync(() =>
                        Find<TextBlock>("UploadStatus")!.Text = "Interrupted (resumable).");
                    break;
                case G9EUploadStatus.Failed:
                    Log(UploadLines, $"FAIL — {result.ErrorCode}: {result.ErrorMessage}");
                    await Dispatcher.UIThread.InvokeAsync(() =>
                        Find<TextBlock>("UploadStatus")!.Text = $"Failed: {result.ErrorCode}");
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            Log(UploadLines, "Upload cancelled.");
            await Dispatcher.UIThread.InvokeAsync(() =>
                Find<TextBlock>("UploadStatus")!.Text = "Cancelled (resumable).");
        }
        catch (Exception ex)
        {
            Log(UploadLines, "Upload error: " + ex.Message);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Find<Button>("UploadButton")!.IsEnabled = true;
                Find<Button>("CancelUploadButton")!.IsEnabled = false;
            });
            _uploadCts?.Dispose();
            _uploadCts = null;
        }
    }

    // ---------------- Rate-limit tab ----------------

    private async Task RateLimitBurstAsync()
    {
        if (_client is null) return;
        // Send 20 calls; with [G9AttrRateLimit(perSecond: 5, burst: 10)] expect ~10 OK then ~10 G9_RATE_LIMITED.
        const int totalCalls = 20;
        Log(RateLimitLines, $"Sending {totalCalls} SendMessage calls (expect ~10 OK then G9_RATE_LIMITED) ...");

        var ok = 0;
        var rateLimited = 0;
        var other = 0;

        for (var i = 0; i < totalCalls; i++)
        {
            try
            {
                await _client.Server.SendMessage("burst", $"#{i}").ConfigureAwait(false);
                ok++;
            }
            catch (Microsoft.AspNetCore.SignalR.HubException hub) when (hub.Message.Contains("G9_RATE_LIMITED"))
            {
                rateLimited++;
            }
            catch (Exception ex)
            {
                other++;
                if (other <= 3) Log(RateLimitLines, "Other error: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        Log(RateLimitLines, $"Result: ok={ok}  rate_limited={rateLimited}  other={other}");
    }

    // ---------------- helpers ----------------

    /// <summary>Pushes a line onto a log collection, marshalling onto the UI thread.</summary>
    public void Log(ObservableCollection<string> sink, string line)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            sink.Add(line);
            if (sink.Count > 1000) sink.RemoveAt(0);
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                sink.Add(line);
                if (sink.Count > 1000) sink.RemoveAt(0);
            });
        }
    }

    private static string HumanBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}

/// <summary>
///     Concrete subclass of the source-generated <c>ChatHubClient</c> that overrides the
///     listener methods to forward server-pushed events into the appropriate UI tab.
/// </summary>
internal sealed class TestChatClient : ChatHubClient
{
    private readonly MainWindow _owner;

    public TestChatClient(MainWindow owner, string serverUrl) : base(serverUrl) => _owner = owner;

    public override Task ReceiveMessage(string user, string message)
    {
        _owner.Log(_owner.ChatLines, $"<- {user}: {message}");
        return Task.CompletedTask;
    }

    public override Task UserJoined(string user)
    {
        _owner.Log(_owner.ConnectionLines, $"<- UserJoined: {user}");
        return Task.CompletedTask;
    }

    public override Task UserLeft(string user)
    {
        _owner.Log(_owner.ConnectionLines, $"<- UserLeft: {user}");
        return Task.CompletedTask;
    }

    public override Task LoginResult(bool accepted)
    {
        _owner.Log(_owner.ConnectionLines, "<- LoginResult: " + accepted);
        return Task.CompletedTask;
    }

    public override Task UploadProgress(G9SignalRSuperNetCore.Server.Classes.FileUpload.G9DtUploadProgress progress)
    {
        // Server-side ack routed here. Already merged into the IProgress<> sink in the uploader,
        // but we surface it in the upload log for visibility.
        _owner.Log(_owner.UploadLines, $"<- ack {progress.BytesReceived} B for {progress.UploadId.Substring(0, 8)}…");
        return Task.CompletedTask;
    }
}
