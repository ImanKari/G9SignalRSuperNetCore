using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using G9SignalRSuperNetCore.Sample.Shared;

namespace G9SignalRSuperNetCore.ConsoleClient;

/// <summary>
///     Main window of the Consolonia test harness. Wires up the source-generated
///     <c>ChatHubClient</c> to UI controls so a developer can verify the typed end-to-end
///     pipeline interactively without leaving the terminal.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Live log lines bound to <c>LogList</c>.</summary>
    public ObservableCollection<string> LogLines { get; } = new();

    private TestChatClient? _client;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        var logList = this.FindControl<ListBox>("LogList")!;
        logList.ItemsSource = LogLines;

        this.FindControl<Button>("ConnectButton")!.Click += async (_, _) => await ConnectAsync().ConfigureAwait(false);
        this.FindControl<Button>("DisconnectButton")!.Click += async (_, _) => await DisconnectAsync().ConfigureAwait(false);
        this.FindControl<Button>("SendButton")!.Click += async (_, _) => await SendAsync().ConfigureAwait(false);
        this.FindControl<Button>("GetRecentButton")!.Click += async (_, _) => await GetRecentAsync().ConfigureAwait(false);
        this.FindControl<Button>("ExitButton")!.Click += (_, _) =>
        {
            (Application.Current?.ApplicationLifetime as IControlledApplicationLifetime)?.Shutdown();
        };
    }

    private async Task ConnectAsync()
    {
        var url = this.FindControl<TextBox>("ServerUrlBox")!.Text ?? "https://localhost:7159";
        Log($"Connecting to {url} ...");
        try
        {
            _client = new TestChatClient(this, url);
            await _client.ConnectAsync().ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                this.FindControl<Button>("ConnectButton")!.IsEnabled = false;
                this.FindControl<Button>("DisconnectButton")!.IsEnabled = true;
                this.FindControl<Button>("SendButton")!.IsEnabled = true;
                this.FindControl<Button>("GetRecentButton")!.IsEnabled = true;
            });
            Log("Connected.");
        }
        catch (Exception ex)
        {
            Log("Connect failed: " + ex.Message);
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
            Log("Disconnected.");
        }
        catch (Exception ex)
        {
            Log("Disconnect failed: " + ex.Message);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                this.FindControl<Button>("ConnectButton")!.IsEnabled = true;
                this.FindControl<Button>("DisconnectButton")!.IsEnabled = false;
                this.FindControl<Button>("SendButton")!.IsEnabled = false;
                this.FindControl<Button>("GetRecentButton")!.IsEnabled = false;
            });
        }
    }

    private async Task SendAsync()
    {
        if (_client is null) return;
        var user = this.FindControl<TextBox>("UserNameBox")!.Text ?? "anon";
        var msg = this.FindControl<TextBox>("MessageBox")!.Text ?? string.Empty;
        try
        {
            await _client.Server.SendMessage(user, msg).ConfigureAwait(false);
            Log($"-> SendMessage(\"{user}\", \"{msg}\")");
        }
        catch (Exception ex)
        {
            Log("Send failed: " + ex.Message);
        }
    }

    private async Task GetRecentAsync()
    {
        if (_client is null) return;
        try
        {
            var list = await _client.Server.GetRecentMessages().ConfigureAwait(false);
            Log($"<- GetRecentMessages returned {list.Count} items");
            foreach (var item in list) Log("    " + item);
        }
        catch (Exception ex)
        {
            Log("GetRecentMessages failed: " + ex.Message);
        }
    }

    /// <summary>Pushes a line onto the log list, marshalling onto the UI thread.</summary>
    public void Log(string line)
    {
        if (Dispatcher.UIThread.CheckAccess())
            LogLines.Add(line);
        else
            Dispatcher.UIThread.Post(() => LogLines.Add(line));
    }
}

/// <summary>
///     Concrete subclass of the source-generated <c>ChatHubClient</c> that overrides the
///     listener methods to forward server-pushed events into the UI log.
/// </summary>
internal sealed class TestChatClient : ChatHubClient
{
    private readonly MainWindow _owner;

    public TestChatClient(MainWindow owner, string serverUrl) : base(serverUrl) => _owner = owner;

    public override Task ReceiveMessage(string user, string message)
    {
        _owner.Log($"<- ReceiveMessage from {user}: {message}");
        return Task.CompletedTask;
    }

    public override Task UserJoined(string user)
    {
        _owner.Log($"<- UserJoined: {user}");
        return Task.CompletedTask;
    }

    public override Task UserLeft(string user)
    {
        _owner.Log($"<- UserLeft: {user}");
        return Task.CompletedTask;
    }

    public override Task LoginResult(bool accepted)
    {
        _owner.Log("<- LoginResult: " + accepted);
        return Task.CompletedTask;
    }
}
