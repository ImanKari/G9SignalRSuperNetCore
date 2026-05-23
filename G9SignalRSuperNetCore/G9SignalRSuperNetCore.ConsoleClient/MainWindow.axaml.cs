using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using G9SignalRSuperNetCore.Client;
using G9SignalRSuperNetCore.Client.FileUpload;
using G9SignalRSuperNetCore.Sample.Shared;
using Microsoft.AspNetCore.SignalR.Client;

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
    /// <summary>Rooms / presence tab log lines.</summary>
    public ObservableCollection<string> RoomsLines { get; } = new();
    /// <summary>Streaming tab log lines.</summary>
    public ObservableCollection<string> StreamingLines { get; } = new();
    /// <summary>Encrypted tab log lines.</summary>
    public ObservableCollection<string> EncryptedLines { get; } = new();

    private TestChatClient? _client;
    private CancellationTokenSource? _uploadCts;
    private CancellationTokenSource? _downloadCts;
    private CancellationTokenSource? _streamCts;
    private byte[]? _serverPubKey;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        // Bind list boxes to observable collections.
        Find<ListBox>("ConnectionLog")!.ItemsSource = ConnectionLines;
        Find<ListBox>("ChatLog")!.ItemsSource = ChatLines;
        Find<ListBox>("RecentLog")!.ItemsSource = RecentLines;
        Find<ListBox>("UploadLog")!.ItemsSource = UploadLines;
        Find<ListBox>("RateLimitLog")!.ItemsSource = RateLimitLines;
        Find<ListBox>("RoomsLog")!.ItemsSource = RoomsLines;
        Find<ListBox>("StreamingLog")!.ItemsSource = StreamingLines;
        Find<ListBox>("EncryptedLog")!.ItemsSource = EncryptedLines;

        // Wire commands.
        Find<Button>("ConnectButton")!.Click += async (_, _) => await ConnectAsync().ConfigureAwait(false);
        Find<Button>("DisconnectButton")!.Click += async (_, _) => await DisconnectAsync().ConfigureAwait(false);
        Find<Button>("SendButton")!.Click += async (_, _) => await SendAsync().ConfigureAwait(false);
        Find<Button>("GetRecentButton")!.Click += async (_, _) => await GetRecentAsync().ConfigureAwait(false);
        Find<Button>("UploadButton")!.Click += async (_, _) => await UploadAsync(breakAfterBytes: long.MaxValue).ConfigureAwait(false);
        Find<Button>("UploadBreakButton")!.Click += async (_, _) => await UploadAsync(breakAfterBytes: 8L * 1024 * 1024).ConfigureAwait(false);
        Find<Button>("UploadResumeButton")!.Click += async (_, _) => await UploadAsync(breakAfterBytes: long.MaxValue).ConfigureAwait(false);
        Find<Button>("CancelUploadButton")!.Click += (_, _) => _uploadCts?.Cancel();
        Find<Button>("BrowseButton")!.Click += async (_, _) => await BrowseAsync().ConfigureAwait(false);
        Find<Button>("DownloadButton")!.Click += async (_, _) => await DownloadAsync(breakAfterBytes: long.MaxValue).ConfigureAwait(false);
        Find<Button>("DownloadBreakButton")!.Click += async (_, _) => await DownloadAsync(breakAfterBytes: 8L * 1024 * 1024).ConfigureAwait(false);
        Find<Button>("DownloadResumeButton")!.Click += async (_, _) => await DownloadAsync(breakAfterBytes: long.MaxValue).ConfigureAwait(false);
        Find<Button>("CancelDownloadButton")!.Click += (_, _) => _downloadCts?.Cancel();
        Find<Button>("RateLimitButton")!.Click += async (_, _) => await RateLimitBurstAsync().ConfigureAwait(false);

        // Bundle 3: rooms + presence.
        Find<Button>("JoinRoomButton")!.Click += async (_, _) => await JoinRoomAsync().ConfigureAwait(false);
        Find<Button>("LeaveRoomButton")!.Click += async (_, _) => await LeaveRoomAsync().ConfigureAwait(false);
        Find<Button>("SendToRoomButton")!.Click += async (_, _) => await SendToRoomAsync().ConfigureAwait(false);
        Find<Button>("ListMembersButton")!.Click += async (_, _) => await ListMembersAsync().ConfigureAwait(false);
        Find<Button>("ListOnlineButton")!.Click += async (_, _) => await ListOnlineAsync().ConfigureAwait(false);

        // Bundle 4: streaming.
        Find<Button>("StartStreamButton")!.Click += async (_, _) => await StartTickerAsync().ConfigureAwait(false);
        Find<Button>("StopStreamButton")!.Click += (_, _) => _streamCts?.Cancel();
        Find<Button>("BulkPushButton")!.Click += async (_, _) => await BulkPushAsync().ConfigureAwait(false);

        // Bundle 5: encrypted.
        Find<Button>("HandshakeButton")!.Click += async (_, _) => await HandshakeAsync().ConfigureAwait(false);
        Find<Button>("EncryptedEchoButton")!.Click += async (_, _) => await EncryptedEchoAsync().ConfigureAwait(false);

        Find<Button>("ClearConnectionButton")!.Click += (_, _) => ConnectionLines.Clear();
        Find<Button>("ClearChatButton")!.Click += (_, _) => ChatLines.Clear();
        Find<Button>("ClearRecentButton")!.Click += (_, _) => RecentLines.Clear();
        Find<Button>("ClearUploadButton")!.Click += (_, _) => UploadLines.Clear();
        Find<Button>("ClearRateLimitButton")!.Click += (_, _) => RateLimitLines.Clear();
        Find<Button>("ClearRoomsButton")!.Click += (_, _) => RoomsLines.Clear();
        Find<Button>("ClearStreamingButton")!.Click += (_, _) => StreamingLines.Clear();
        Find<Button>("ClearEncryptedButton")!.Click += (_, _) => EncryptedLines.Clear();
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
            // Surface lifecycle transitions in the Connection tab — Reconnecting,
            // Reconnected, Disconnected, ConnectFailed, etc.
            _client.StateChanged += OnConnectionStateChanged;
            await _client.ConnectAsync().ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Find<Button>("ConnectButton")!.IsEnabled = false;
                Find<Button>("DisconnectButton")!.IsEnabled = true;
                Find<Button>("SendButton")!.IsEnabled = true;
                Find<Button>("GetRecentButton")!.IsEnabled = true;
                Find<Button>("UploadButton")!.IsEnabled = true;
                Find<Button>("UploadBreakButton")!.IsEnabled = true;
                Find<Button>("UploadResumeButton")!.IsEnabled = true;
                Find<Button>("DownloadButton")!.IsEnabled = true;
                Find<Button>("DownloadBreakButton")!.IsEnabled = true;
                Find<Button>("DownloadResumeButton")!.IsEnabled = true;
                Find<Button>("RateLimitButton")!.IsEnabled = true;
                Find<Button>("JoinRoomButton")!.IsEnabled = true;
                Find<Button>("LeaveRoomButton")!.IsEnabled = true;
                Find<Button>("SendToRoomButton")!.IsEnabled = true;
                Find<Button>("ListMembersButton")!.IsEnabled = true;
                Find<Button>("ListOnlineButton")!.IsEnabled = true;
                Find<Button>("StartStreamButton")!.IsEnabled = true;
                Find<Button>("BulkPushButton")!.IsEnabled = true;
                Find<Button>("HandshakeButton")!.IsEnabled = true;
                Find<Button>("EncryptedEchoButton")!.IsEnabled = true;
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
            _client.StateChanged -= OnConnectionStateChanged;
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
                Find<Button>("UploadBreakButton")!.IsEnabled = false;
                Find<Button>("UploadResumeButton")!.IsEnabled = false;
                Find<Button>("DownloadButton")!.IsEnabled = false;
                Find<Button>("DownloadBreakButton")!.IsEnabled = false;
                Find<Button>("DownloadResumeButton")!.IsEnabled = false;
                Find<Button>("CancelDownloadButton")!.IsEnabled = false;
                Find<Button>("RateLimitButton")!.IsEnabled = false;
                Find<Button>("JoinRoomButton")!.IsEnabled = false;
                Find<Button>("LeaveRoomButton")!.IsEnabled = false;
                Find<Button>("SendToRoomButton")!.IsEnabled = false;
                Find<Button>("ListMembersButton")!.IsEnabled = false;
                Find<Button>("ListOnlineButton")!.IsEnabled = false;
                Find<Button>("StartStreamButton")!.IsEnabled = false;
                Find<Button>("StopStreamButton")!.IsEnabled = false;
                Find<Button>("BulkPushButton")!.IsEnabled = false;
                Find<Button>("HandshakeButton")!.IsEnabled = false;
                Find<Button>("EncryptedEchoButton")!.IsEnabled = false;
                Find<TextBlock>("StatusText")!.Text = " G9SignalRSuperNetCore  |  Disconnected ";
            });
        }
    }

    private void OnConnectionStateChanged(G9DtConnectionState state)
    {
        var (text, statusBar) = state.Phase switch
        {
            G9EConnectionPhase.Connecting     => ($"Connecting … ({state.UtcTimestamp:HH:mm:ss})",                  " G9SignalRSuperNetCore  |  Connecting …"),
            G9EConnectionPhase.Connected      => ($"Connected (id={state.Detail})",                                  $" G9SignalRSuperNetCore  |  Connected ({state.Detail}) "),
            G9EConnectionPhase.ConnectFailed  => ($"Connect failed: {state.Detail}",                                 " G9SignalRSuperNetCore  |  Connect failed "),
            G9EConnectionPhase.Reconnecting   => ($"Reconnecting … reason: {state.Detail ?? "(unknown)"}",            " G9SignalRSuperNetCore  |  Reconnecting … "),
            G9EConnectionPhase.Reconnected    => ($"Reconnected (new id={state.Detail})",                            $" G9SignalRSuperNetCore  |  Reconnected ({state.Detail}) "),
            G9EConnectionPhase.Disconnected   => (state.Detail is null
                                                       ? "Disconnected (graceful)."
                                                       : $"Disconnected — {state.Detail}",                          " G9SignalRSuperNetCore  |  Disconnected "),
            _                                 => ($"State: {state.Phase}",                                          $" G9SignalRSuperNetCore  |  {state.Phase} ")
        };

        Log(ConnectionLines, $"[{state.Phase}] {text}");
        Dispatcher.UIThread.Post(() => Find<TextBlock>("StatusText")!.Text = statusBar);

        // When the auto-reconnect budget is exhausted, the connection ends in Disconnected.
        // Re-enable the Connect button so the user can manually try again.
        if (state.Phase == G9EConnectionPhase.Disconnected)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Find<Button>("ConnectButton")!.IsEnabled = true;
                Find<Button>("DisconnectButton")!.IsEnabled = false;
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

    private async Task UploadAsync(long breakAfterBytes)
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
            Find<Button>("UploadBreakButton")!.IsEnabled = false;
            Find<Button>("UploadResumeButton")!.IsEnabled = false;
            Find<Button>("CancelUploadButton")!.IsEnabled = true;
            Find<TextBlock>("UploadStatus")!.Text = breakAfterBytes != long.MaxValue
                ? $"Uploading … will simulate a drop after {HumanBytes(breakAfterBytes)}"
                : "Hashing & beginning upload …";
            Find<ProgressBar>("UploadBar")!.Value = 0;
        });

        var uploader = new G9CFileUploader(_client.Connection, new G9DtUploadClientOptions
        {
            ChunkSize = chunkKb * 1024,
            // Disable retry-loop for the test harness so a "broken" attempt returns Interrupted
            // immediately rather than retrying inside the uploader.
            MaxRetries = breakAfterBytes != long.MaxValue ? 0 : 5,
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

            // Test-mode break: cancel the CTS once we've crossed the threshold to simulate
            // the user closing the upload mid-transfer.
            if (p.BytesSent >= breakAfterBytes && !ct.IsCancellationRequested)
            {
                Log(UploadLines, $"** simulating break at {HumanBytes(p.BytesSent)} **");
                _uploadCts?.Cancel();
            }

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
        if (breakAfterBytes != long.MaxValue)
            Log(UploadLines, $"  test:   simulated drop at {HumanBytes(breakAfterBytes)}");

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
                    Log(UploadLines, $"Interrupted at {result.BytesWritten} B (resumable). Click [ Resume upload ] to continue.");
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
            Log(UploadLines, breakAfterBytes != long.MaxValue
                ? "Upload broken (resumable). Click [ Resume upload ] to continue."
                : "Upload cancelled.");
            await Dispatcher.UIThread.InvokeAsync(() =>
                Find<TextBlock>("UploadStatus")!.Text = "Interrupted (resumable).");
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
                Find<Button>("UploadBreakButton")!.IsEnabled = true;
                Find<Button>("UploadResumeButton")!.IsEnabled = true;
                Find<Button>("CancelUploadButton")!.IsEnabled = false;
            });
            _uploadCts?.Dispose();
            _uploadCts = null;
        }
    }

    // ---------------- Download tab ----------------

    private async Task DownloadAsync(long breakAfterBytes)
    {
        if (_client is null) return;
        var serverFile = Find<TextBox>("DownloadServerFileBox")!.Text ?? "file_upload_test.zip";
        var typedLocal = Find<TextBox>("DownloadLocalPathBox")!.Text;
        var localPath = string.IsNullOrWhiteSpace(typedLocal)
            ? Path.Combine(AppContext.BaseDirectory, "downloads", serverFile)
            : typedLocal;
        await Dispatcher.UIThread.InvokeAsync(() =>
            Find<TextBox>("DownloadLocalPathBox")!.Text = localPath);

        if (!int.TryParse(Find<TextBox>("ChunkSizeBox")!.Text, out var chunkKb) || chunkKb <= 0) chunkKb = 64;

        _downloadCts = new CancellationTokenSource();
        var ct = _downloadCts.Token;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Find<Button>("DownloadButton")!.IsEnabled = false;
            Find<Button>("DownloadBreakButton")!.IsEnabled = false;
            Find<Button>("DownloadResumeButton")!.IsEnabled = false;
            Find<Button>("CancelDownloadButton")!.IsEnabled = true;
            Find<TextBlock>("DownloadStatus")!.Text = breakAfterBytes != long.MaxValue
                ? $"Downloading … will simulate a drop after {HumanBytes(breakAfterBytes)}"
                : "Calling BeginDownload …";
            Find<ProgressBar>("DownloadBar")!.Value = 0;
        });

        var downloader = new G9CFileDownloader(_client.Connection, new G9DtDownloadClientOptions
        {
            ChunkSize = chunkKb * 1024,
            MaxRetries = breakAfterBytes != long.MaxValue ? 0 : 5,
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

        var progress = new Progress<G9DtDownloadClientProgress>(p =>
        {
            if (sw.Elapsed - lastReport < TimeSpan.FromMilliseconds(100) && p.BytesReceived < p.TotalBytes) return;
            lastReport = sw.Elapsed;

            if (p.BytesReceived >= breakAfterBytes && !ct.IsCancellationRequested)
            {
                Log(UploadLines, $"** simulating download break at {HumanBytes(p.BytesReceived)} **");
                _downloadCts?.Cancel();
            }

            var percent = p.TotalBytes > 0 ? 100.0 * p.BytesReceived / p.TotalBytes : 0;
            Dispatcher.UIThread.Post(() =>
            {
                Find<ProgressBar>("DownloadBar")!.Value = percent;
                Find<TextBlock>("DownloadStatus")!.Text = string.Format(
                    "{0,6:F2}%  recv={1}/{2}  {3:F0} KB/s  elapsed={4:mm\\:ss}",
                    percent,
                    HumanBytes(p.BytesReceived), HumanBytes(p.TotalBytes),
                    p.BytesPerSecond / 1024.0,
                    p.Elapsed);
            });
        });

        Log(UploadLines, $"Starting download of {serverFile}");
        Log(UploadLines, $"  target: {localPath}");
        Log(UploadLines, $"  chunk:  {chunkKb} KB");
        if (breakAfterBytes != long.MaxValue)
            Log(UploadLines, $"  test:   simulated drop at {HumanBytes(breakAfterBytes)}");

        try
        {
            var result = await downloader.DownloadAsync(serverFile, localPath, progress, ct).ConfigureAwait(false);
            switch (result.Status)
            {
                case G9EUploadStatus.Completed:
                    Log(UploadLines, $"OK — committed {result.BytesWritten} B at {result.LocalPath}");
                    Log(UploadLines, $"     SHA-256 = {result.Sha256}");
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Find<ProgressBar>("DownloadBar")!.Value = 100;
                        Find<TextBlock>("DownloadStatus")!.Text = "Completed.";
                    });
                    break;
                case G9EUploadStatus.Interrupted:
                    Log(UploadLines, $"Interrupted at {result.BytesWritten} B (resumable). Click [ Resume download ] to continue.");
                    await Dispatcher.UIThread.InvokeAsync(() =>
                        Find<TextBlock>("DownloadStatus")!.Text = "Interrupted (resumable).");
                    break;
                case G9EUploadStatus.Failed:
                    Log(UploadLines, $"FAIL — {result.ErrorCode}: {result.ErrorMessage}");
                    await Dispatcher.UIThread.InvokeAsync(() =>
                        Find<TextBlock>("DownloadStatus")!.Text = $"Failed: {result.ErrorCode}");
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            Log(UploadLines, breakAfterBytes != long.MaxValue
                ? "Download broken (resumable). Click [ Resume download ] to continue."
                : "Download cancelled.");
            await Dispatcher.UIThread.InvokeAsync(() =>
                Find<TextBlock>("DownloadStatus")!.Text = "Interrupted (resumable).");
        }
        catch (Exception ex)
        {
            Log(UploadLines, "Download error: " + ex.Message);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Find<Button>("DownloadButton")!.IsEnabled = true;
                Find<Button>("DownloadBreakButton")!.IsEnabled = true;
                Find<Button>("DownloadResumeButton")!.IsEnabled = true;
                Find<Button>("CancelDownloadButton")!.IsEnabled = false;
            });
            _downloadCts?.Dispose();
            _downloadCts = null;
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

    // ---------------- Bundle 3: rooms & presence ----------------

    private async Task JoinRoomAsync()
    {
        if (_client is null) return;
        var room = Find<TextBox>("RoomNameBox")!.Text ?? "lobby";
        try
        {
            await _client.Connection.InvokeAsync("JoinRoom", room).ConfigureAwait(false);
            Log(RoomsLines, $"-> JoinRoom(\"{room}\")");
        }
        catch (Exception ex) { Log(RoomsLines, "JoinRoom failed: " + ex.Message); }
    }

    private async Task LeaveRoomAsync()
    {
        if (_client is null) return;
        var room = Find<TextBox>("RoomNameBox")!.Text ?? "lobby";
        try
        {
            await _client.Connection.InvokeAsync("LeaveRoom", room).ConfigureAwait(false);
            Log(RoomsLines, $"-> LeaveRoom(\"{room}\")");
        }
        catch (Exception ex) { Log(RoomsLines, "LeaveRoom failed: " + ex.Message); }
    }

    private async Task SendToRoomAsync()
    {
        if (_client is null) return;
        var room = Find<TextBox>("RoomNameBox")!.Text ?? "lobby";
        var user = Find<TextBox>("UserNameBox")!.Text ?? "anon";
        var msg = Find<TextBox>("RoomMessageBox")!.Text ?? string.Empty;
        try
        {
            await _client.Connection.InvokeAsync("SendToRoom", room, user, msg).ConfigureAwait(false);
            Log(RoomsLines, $"-> SendToRoom(\"{room}\", \"{user}\", \"{msg}\")");
        }
        catch (Exception ex) { Log(RoomsLines, "SendToRoom failed: " + ex.Message); }
    }

    private async Task ListMembersAsync()
    {
        if (_client is null) return;
        var room = Find<TextBox>("RoomNameBox")!.Text ?? "lobby";
        try
        {
            var members = await _client.Connection.InvokeAsync<List<string>>("ListRoomMembers", room).ConfigureAwait(false);
            Log(RoomsLines, $"<- ListRoomMembers(\"{room}\") -> {members.Count} member(s)");
            foreach (var m in members) Log(RoomsLines, "    " + m);
        }
        catch (Exception ex) { Log(RoomsLines, "ListRoomMembers failed: " + ex.Message); }
    }

    private async Task ListOnlineAsync()
    {
        if (_client is null) return;
        try
        {
            var users = await _client.Connection.InvokeAsync<List<string>>("ListOnlineUsers").ConfigureAwait(false);
            Log(RoomsLines, $"<- ListOnlineUsers() -> {users.Count} user(s)");
            foreach (var u in users) Log(RoomsLines, "    " + u);
        }
        catch (Exception ex) { Log(RoomsLines, "ListOnlineUsers failed: " + ex.Message); }
    }

    // ---------------- Bundle 4: streaming ----------------

    private async Task StartTickerAsync()
    {
        if (_client is null) return;
        if (!int.TryParse(Find<TextBox>("TickCountBox")!.Text, out var count) || count <= 0) count = 20;
        if (!int.TryParse(Find<TextBox>("TickIntervalBox")!.Text, out var interval) || interval < 10) interval = 100;

        _streamCts = new CancellationTokenSource();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Find<Button>("StartStreamButton")!.IsEnabled = false;
            Find<Button>("StopStreamButton")!.IsEnabled = true;
        });

        Log(StreamingLines, $"Starting LiveTickerStream(count={count}, intervalMs={interval}) ...");
        try
        {
            var stream = _client.Connection.StreamAsync<int>("LiveTickerStream", count, interval, _streamCts.Token);
            await foreach (var tick in stream.WithCancellation(_streamCts.Token).ConfigureAwait(false))
            {
                Log(StreamingLines, $"   tick = {tick}");
            }
            Log(StreamingLines, "Stream completed.");
        }
        catch (OperationCanceledException) { Log(StreamingLines, "Stream cancelled."); }
        catch (Exception ex) { Log(StreamingLines, "Stream error: " + ex.Message); }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Find<Button>("StartStreamButton")!.IsEnabled = true;
                Find<Button>("StopStreamButton")!.IsEnabled = false;
            });
            _streamCts?.Dispose();
            _streamCts = null;
        }
    }

    private async Task BulkPushAsync()
    {
        if (_client is null) return;
        Log(StreamingLines, "Pushing 100 ints to server (BulkCounterStream) ...");
        try
        {
            async IAsyncEnumerable<int> Producer()
            {
                for (var i = 1; i <= 100; i++)
                {
                    await Task.Yield();
                    yield return i;
                }
            }
            var total = await _client.Connection.InvokeAsync<long>("BulkCounterStream", Producer()).ConfigureAwait(false);
            Log(StreamingLines, $"<- BulkCounterStream returned total={total} (expected 5050).");
        }
        catch (Exception ex) { Log(StreamingLines, "BulkCounterStream failed: " + ex.Message); }
    }

    // ---------------- Bundle 5: encrypted ----------------

    private async Task HandshakeAsync()
    {
        if (_client is null) return;
        try
        {
            _serverPubKey = await _client.Connection.InvokeAsync<byte[]>("GetServerPublicKey").ConfigureAwait(false);
            Log(EncryptedLines, $"<- GetServerPublicKey -> {_serverPubKey.Length} bytes");
            Log(EncryptedLines, "   sha256(pk) = " + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(_serverPubKey))[..16] + "…");
        }
        catch (Exception ex) { Log(EncryptedLines, "Handshake failed: " + ex.Message); }
    }

    private async Task EncryptedEchoAsync()
    {
        if (_client is null) return;
        if (_serverPubKey is null)
        {
            Log(EncryptedLines, "Click [ Handshake ] first to fetch the server public key.");
            return;
        }
        var plaintext = System.Text.Encoding.UTF8.GetBytes(Find<TextBox>("EncPlaintextBox")!.Text ?? string.Empty);
        try
        {
            var (ephemeralPub, sessionKey) = G9SignalRSuperNetCore.Server.Classes.Crypto.G9CHandshake.ClientHandshake(_serverPubKey);
            var envelope = G9SignalRSuperNetCore.Server.Classes.Crypto.G9CHandshake.Seal(sessionKey, plaintext);
            Log(EncryptedLines, $"-> EncryptedEcho({plaintext.Length} B plaintext, sealed envelope = {envelope.Length} B)");

            var sealedReply = await _client.Connection.InvokeAsync<byte[]>("EncryptedEcho", ephemeralPub, envelope).ConfigureAwait(false);
            var recovered = G9SignalRSuperNetCore.Server.Classes.Crypto.G9CHandshake.Open(sessionKey, sealedReply);
            var recoveredText = System.Text.Encoding.UTF8.GetString(recovered);
            Log(EncryptedLines, $"<- {recovered.Length} B recovered = \"{recoveredText}\"");
        }
        catch (Exception ex) { Log(EncryptedLines, "EncryptedEcho failed: " + ex.Message); }
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

    public TestChatClient(MainWindow owner, string serverUrl)
        : base(serverUrl, configureHttpConnection: G9CHttpResilience.ApplyDefault)
        => _owner = owner;

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
