using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

// G4.Recorders.ClientApp is a manual test harness: it connects to the Uia, Chromium, and SAP
// recorder SignalR hubs and prints every recorder event they broadcast. UIA events stream
// automatically once the Uia recorder host is running (it captures global desktop input)
// Chromium events require a browser with the recorder extension, which this client can launch
// via the 's' key (StartRecorder). SAP events stream once the SAP recorder host runs and SAP GUI
// (or the SAP GUI simulator) is open; each SAP message carries user32 and/or nativeSap events.

// Verbose starts from the command line and can be toggled at runtime with 'v'.
var isVerbose = args.Contains("--verbose") || args.Contains("-v");

// Serializes console writes so events from different hub threads never interleave or bleed color.
var consoleLock = new object();

// The process id of the last browser this client started, used by StopRecorder.
int? lastChromiumProcessId = null;

// The process id of the SAP Logon the SAP recorder launched for this client, used by StopRecorder.
int? lastSapLogonProcessId = null;

// Load hub URLs and the Chromium binary from appsettings.json (copied next to the executable).
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var uiaUrl = configuration["Hubs:UiaUrl"] ?? "http://localhost:9955/hub/v4/g4/peek";
var chromiumUrl = configuration["Hubs:ChromiumUrl"] ?? "http://localhost:9956/hub/v4/g4/peek";
var sapUrl = configuration["Hubs:SapUrl"] ?? "http://localhost:9957/hub/v4/g4/peek";
var chromiumBinary = configuration["Chromium:BinaryPath"] ?? string.Empty;
var chromiumArguments = configuration.GetSection("Chromium:Arguments").Get<string[]>() ?? [];

// Resolve which hubs are active from config, with command-line overrides so a run can be limited
// to one source without editing appsettings (for example: --no-uia, or --chromium to force on).
var isUiaEnabled = configuration.GetValue("Hubs:UiaEnabled", defaultValue: true);
var isChromiumEnabled = configuration.GetValue("Hubs:ChromiumEnabled", defaultValue: true);
var isSapEnabled = configuration.GetValue("Hubs:SapEnabled", defaultValue: true);

if (args.Contains("--no-uia")) { isUiaEnabled = false; }
if (args.Contains("--uia")) { isUiaEnabled = true; }
if (args.Contains("--no-chromium")) { isChromiumEnabled = false; }
if (args.Contains("--chromium")) { isChromiumEnabled = true; }
if (args.Contains("--no-sap")) { isSapEnabled = false; }
if (args.Contains("--sap")) { isSapEnabled = true; }

// Read the SAP StartRecorder values; --sap-logon <path> overrides Sap:Logon:Path. Unset values stay null, so the
// recorder resolves them from its own environment and appsettings.json.
var sapLogonIndex = Array.IndexOf(args, "--sap-logon");
var sapLogonPath = sapLogonIndex >= 0 && sapLogonIndex + 1 < args.Length
    ? args[sapLogonIndex + 1]
    : NullIfEmpty(configuration["Sap:Logon:Path"]);
var sapParameters = new
{
    sapLogon = new
    {
        arguments = NullIfEmptyList(configuration.GetSection("Sap:Logon:Arguments").Get<string[]>()),
        connection = NullIfEmpty(configuration["Sap:Logon:Connection"]),
        path = sapLogonPath
    },
    scope = new
    {
        client = NullIfEmpty(configuration["Sap:Scope:Client"]),
        sessions = NullIfEmptyList(configuration.GetSection("Sap:Scope:Sessions").Get<string[]>()),
        systemName = NullIfEmpty(configuration["Sap:Scope:SystemName"])
    },
    setRecordMode = configuration.GetValue<bool?>("Sap:Recorder:SetRecordMode"),
    streams = NullIfEmptyList(configuration.GetSection("Sap:Recorder:Streams").Get<string[]>())
};

if (!isUiaEnabled && !isChromiumEnabled && !isSapEnabled)
{
    Console.WriteLine(
        "Every hub is disabled. Enable at least one in appsettings.json " +
        "(Hubs:UiaEnabled / Hubs:ChromiumEnabled / Hubs:SapEnabled) or via --uia / --chromium / --sap.");

    return;
}

// Build a connection only for each enabled hub; each reconnects automatically after first connect.
var uiaConnection = isUiaEnabled ? CreateConnection(uiaUrl) : null;
var chromiumConnection = isChromiumEnabled ? CreateConnection(chromiumUrl) : null;
var sapConnection = isSapEnabled ? CreateConnection(sapUrl) : null;

if (uiaConnection is not null)
{
    RegisterHandlers(uiaConnection, source: "UIA", color: ConsoleColor.Cyan);
}

if (chromiumConnection is not null)
{
    RegisterHandlers(chromiumConnection, source: "CHROMIUM", color: ConsoleColor.Green);
}

if (sapConnection is not null)
{
    RegisterHandlers(sapConnection, source: "SAP", color: ConsoleColor.Magenta);
}

// Cancelled by Ctrl+C or the 'q' key; drives both the connect retries and the key loop shutdown.
using var cancellation = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    // Prevent the runtime from killing the process so shutdown can dispose connections cleanly.
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

WriteBanner();

// Bring the enabled connections up (retrying independently) before entering the interactive loop.
var connectTasks = new List<Task>();

if (uiaConnection is not null)
{
    connectTasks.Add(ConnectWithRetryAsync(uiaConnection, "UIA", ConsoleColor.Cyan, cancellation.Token));
}

if (chromiumConnection is not null)
{
    connectTasks.Add(ConnectWithRetryAsync(chromiumConnection, "CHROMIUM", ConsoleColor.Green, cancellation.Token));
}

if (sapConnection is not null)
{
    connectTasks.Add(ConnectWithRetryAsync(sapConnection, "SAP", ConsoleColor.Magenta, cancellation.Token));
}

await Task.WhenAll(connectTasks);

// Apply this client's SAP values once connected; the recorder fills unset ones from its environment and settings.
await StartSapRecorderAsync();

// Process key commands until the user quits or Ctrl+C is pressed.
await RunKeyLoopAsync(cancellation);

// Tear the active connections down so the browser-facing hub notices the client left.
WriteLine(ConsoleColor.DarkGray, "Shutting down...");

if (uiaConnection is not null)
{
    await uiaConnection.DisposeAsync();
}

if (chromiumConnection is not null)
{
    await chromiumConnection.DisposeAsync();
}

if (sapConnection is not null)
{
    await sapConnection.DisposeAsync();
}

// Creates a hub connection with automatic reconnect (applies only after the first connect).
HubConnection CreateConnection(string url)
{
    return new HubConnectionBuilder()
        .WithUrl(url)
        .WithAutomaticReconnect()
        .Build();
}

// Subscribes to the recording-event broadcast and reports connection lifecycle changes.
void RegisterHandlers(HubConnection connection, string source, ConsoleColor color)
{
    // Every hub broadcasts the same "ReceiveRecordingEvent" message with a { value: {...} } envelope.
    connection.On<JsonElement>("ReceiveRecordingEvent", envelope => PrintRecordingEvent(source, color, envelope));

    connection.Reconnecting += error =>
    {
        WriteLine(ConsoleColor.DarkGray, $"[{source}] reconnecting... {error?.Message}");

        return Task.CompletedTask;
    };

    connection.Reconnected += _ =>
    {
        WriteLine(ConsoleColor.DarkGray, $"[{source}] reconnected.");

        return Task.CompletedTask;
    };

    connection.Closed += error =>
    {
        WriteLine(ConsoleColor.DarkGray, $"[{source}] connection closed. {error?.Message}");

        return Task.CompletedTask;
    };
}

// Repeatedly attempts the initial connection until it succeeds or the client is shutting down.
// WithAutomaticReconnect only covers drops after a first successful connect, so the initial
// attempt needs its own retry loop for the common case of starting the client before the server.
async Task ConnectWithRetryAsync(HubConnection connection, string source, ConsoleColor color, CancellationToken token)
{
    while (!token.IsCancellationRequested)
    {
        try
        {
            await connection.StartAsync(token);

            WriteLine(color, $"[{source}] connected ({connection.ConnectionId}).");

            return;
        }
        catch (Exception error) when (!token.IsCancellationRequested)
        {
            WriteLine(ConsoleColor.DarkGray, $"[{source}] connect failed ({error.Message}); retrying in 3s...");

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), token);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }
}

// Reads key commands until quit/cancel. Falls back to a plain wait when input is redirected so the
// client still works as a pure listener when run without an interactive console.
async Task RunKeyLoopAsync(CancellationTokenSource cancellationSource)
{
    if (Console.IsInputRedirected)
    {
        WriteLine(ConsoleColor.DarkGray, "(input redirected - listen-only mode; press Ctrl+C to exit)");

        try
        {
            await Task.Delay(Timeout.Infinite, cancellationSource.Token);
        }
        catch (TaskCanceledException)
        {
            // Expected on shutdown.
        }

        return;
    }

    while (!cancellationSource.IsCancellationRequested)
    {
        // Poll for a key so Ctrl+C (which cancels the token) can break the loop between presses.
        if (!Console.KeyAvailable)
        {
            try
            {
                await Task.Delay(100, cancellationSource.Token);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            continue;
        }

        var key = Console.ReadKey(intercept: true).Key;

        switch (key)
        {
            case ConsoleKey.Q:
                await cancellationSource.CancelAsync();
                break;

            case ConsoleKey.V:
                isVerbose = !isVerbose;
                WriteLine(ConsoleColor.Yellow, $"verbose = {isVerbose}");
                break;

            case ConsoleKey.S:
                await StartChromiumAsync();
                break;

            case ConsoleKey.X:
                await StopChromiumAsync();
                break;

            case ConsoleKey.K:
                await StopSapLogonAsync();
                break;

            case ConsoleKey.H:
                WriteBanner();
                break;
        }
    }
}

// Asks the Chromium recorder hub to launch a recorder browser from the configured binary.
async Task StartChromiumAsync()
{
    if (chromiumConnection is null)
    {
        WriteLine(ConsoleColor.Red, "Chromium is disabled; enable it to start a browser.");

        return;
    }

    if (chromiumConnection.State != HubConnectionState.Connected)
    {
        WriteLine(ConsoleColor.Red, "Chromium hub is not connected; cannot start a browser.");

        return;
    }

    if (string.IsNullOrWhiteSpace(chromiumBinary))
    {
        WriteLine(ConsoleColor.Red, "Chromium:BinaryPath is not set in appsettings.json.");

        return;
    }

    // Shape the payload as the server's DriverParametersModel: the Chromium options live under the
    // W3C vendor key "goog:chromeOptions", so a dictionary is used to emit that literal key.
    var driverParameters = new
    {
        capabilities = new
        {
            alwaysMatch = new Dictionary<string, object>
            {
                ["goog:chromeOptions"] = new { binary = chromiumBinary, args = chromiumArguments }
            }
        }
    };

    try
    {
        var processId = await chromiumConnection.InvokeAsync<int>("StartRecorder", driverParameters);
        lastChromiumProcessId = processId;

        WriteLine(ConsoleColor.Yellow, $"StartRecorder -> browser launched, processId={processId}.");
    }
    catch (Exception error)
    {
        WriteLine(ConsoleColor.Red, $"StartRecorder failed: {error.Message}");
    }
}

// Asks the Chromium recorder hub to stop the browser this client last started.
async Task StopChromiumAsync()
{
    if (chromiumConnection is null)
    {
        WriteLine(ConsoleColor.Red, "Chromium is disabled; enable it to stop a browser.");

        return;
    }

    if (chromiumConnection.State != HubConnectionState.Connected)
    {
        WriteLine(ConsoleColor.Red, "Chromium hub is not connected; cannot stop a browser.");

        return;
    }

    if (lastChromiumProcessId is null)
    {
        WriteLine(ConsoleColor.Red, "No browser has been started from this client yet.");

        return;
    }

    try
    {
        var isStopped = await chromiumConnection.InvokeAsync<bool>("StopRecorder", lastChromiumProcessId.Value);

        if (isStopped)
        {
            WriteLine(ConsoleColor.Yellow, $"StopRecorder -> stopped processId={lastChromiumProcessId}.");
            lastChromiumProcessId = null;
        }
        else
        {
            WriteLine(ConsoleColor.Yellow, $"StopRecorder -> processId={lastChromiumProcessId} was not running.");
        }
    }
    catch (Exception error)
    {
        WriteLine(ConsoleColor.Red, $"StopRecorder failed: {error.Message}");
    }
}

// Sends StartRecorder to the SAP hub and prints the resolved values and the SAP Logon process.
async Task StartSapRecorderAsync()
{
    if (sapConnection is null || sapConnection.State != HubConnectionState.Connected)
    {
        return;
    }

    try
    {
        var result = await sapConnection.InvokeAsync<JsonElement>("StartRecorder", sapParameters);
        var processId = result.GetProperty("processId").GetInt32();
        var sources = result.GetProperty("options").GetProperty("sources").GetRawText();
        lastSapLogonProcessId = processId > 0 ? processId : null;

        WriteLine(ConsoleColor.Magenta, $"[SAP] StartRecorder -> processId={processId}  sources={sources}");
    }
    catch (Exception error)
    {
        WriteLine(ConsoleColor.Red, $"[SAP] StartRecorder failed: {error.Message}");
    }
}

// Asks the SAP hub to stop the SAP Logon it launched for this client.
async Task StopSapLogonAsync()
{
    if (sapConnection is null || sapConnection.State != HubConnectionState.Connected || lastSapLogonProcessId is null)
    {
        WriteLine(ConsoleColor.Red, "No SAP Logon was launched for this client.");

        return;
    }

    try
    {
        var isStopped = await sapConnection.InvokeAsync<bool>("StopRecorder", lastSapLogonProcessId.Value);
        WriteLine(ConsoleColor.Magenta, $"[SAP] StopRecorder -> processId={lastSapLogonProcessId} stopped={isStopped}.");
        lastSapLogonProcessId = null;
    }
    catch (Exception error)
    {
        WriteLine(ConsoleColor.Red, $"[SAP] StopRecorder failed: {error.Message}");
    }
}

// Returns null for empty text, so the recorder resolves the value itself.
static string NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

// Returns null for an empty list, so the recorder resolves the value itself.
static string[] NullIfEmptyList(string[] values) => values is { Length: > 0 } ? values : null;

// Prints one event: a compact colored line by default, or the full JSON when verbose is on.
void PrintRecordingEvent(string source, ConsoleColor color, JsonElement envelope)
{
    // Unwrap the { value: {...} } envelope every hub uses; tolerate an unexpected shape.
    if (!envelope.TryGetProperty("value", out var recordingEvent))
    {
        WriteLine(color, $"[{source}] event without 'value': {envelope.GetRawText()}");

        return;
    }

    // SAP messages carry up to two events of the shared shape, one per stream; print each under its stream name.
    var isSapMessage = recordingEvent.TryGetProperty("user32", out _) || recordingEvent.TryGetProperty("nativeSap", out _);
    if (isSapMessage)
    {
        foreach (var stream in new[] { "user32", "nativeSap" })
        {
            if (recordingEvent.TryGetProperty(stream, out var streamEvent))
            {
                PrintEvent($"{source}/{stream}", color, streamEvent);
            }
        }

        return;
    }

    PrintEvent(source, color, recordingEvent);
}

// Prints one event of the shared shape: a compact colored line by default, or the full JSON when verbose is on.
void PrintEvent(string source, ConsoleColor color, JsonElement recordingEvent)
{
    if (isVerbose)
    {
        var pretty = JsonSerializer.Serialize(recordingEvent, new JsonSerializerOptions { WriteIndented = true });

        WriteLine(color, $"[{source}]{Environment.NewLine}{pretty}");

        return;
    }

    var eventName = GetString(recordingEvent, "event");
    var type = GetString(recordingEvent, "type");
    var machine = GetString(recordingEvent, "machineName");
    var time = FormatTimestamp(recordingEvent);
    var value = FormatValue(recordingEvent);
    var chainCount = CountChainNodes(recordingEvent);

    WriteLine(color, $"[{source} {time}] {type} / {eventName}  machine={machine}  chain={chainCount}  value={value}");
}

// Prints the startup banner and key reference.
void WriteBanner()
{
    var uiaLine = isUiaEnabled ? uiaUrl : "(disabled)";
    var chromiumLine = isChromiumEnabled ? chromiumUrl : "(disabled)";
    var sapLine = isSapEnabled ? sapUrl : "(disabled)";

    // Only advertise the Chromium start/stop keys when the Chromium hub is active.
    var chromiumKeys = isChromiumEnabled ? "[s] start Chromium  [x] stop Chromium  " : string.Empty;
    var sapKeys = isSapEnabled ? "[k] stop SAP Logon  " : string.Empty;
    var keys = $"Keys: {chromiumKeys}{sapKeys}[v] verbose  [h] help  [q] quit";

    WriteLine(ConsoleColor.White,
        $"G4.Recorders.ClientApp - recorder event monitor{Environment.NewLine}" +
        $"  UIA:      {uiaLine}{Environment.NewLine}" +
        $"  Chromium: {chromiumLine}{Environment.NewLine}" +
        $"  SAP:      {sapLine}{Environment.NewLine}" +
        keys);
}

// Writes a colored line under the shared lock so concurrent hub callbacks never interleave.
void WriteLine(ConsoleColor color, string text)
{
    lock (consoleLock)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = previous;
    }
}

// Reads a string property, returning empty when absent or not a string.
static string GetString(JsonElement element, string name)
{
    return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        ? value.GetString() ?? string.Empty
        : string.Empty;
}

// Formats the event's epoch-millisecond timestamp as local wall-clock time.
static string FormatTimestamp(JsonElement recordingEvent)
{
    if (recordingEvent.TryGetProperty("timestamp", out var timestamp)
        && timestamp.TryGetInt64(out var milliseconds)
        && milliseconds > 0)
    {
        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).LocalDateTime.ToString("HH:mm:ss.fff");
    }

    return DateTime.Now.ToString("HH:mm:ss.fff");
}

// Returns the event value as compact JSON, truncated so a single line stays readable.
static string FormatValue(JsonElement recordingEvent)
{
    if (!recordingEvent.TryGetProperty("value", out var value))
    {
        return "null";
    }

    var raw = value.GetRawText();
    const int maximumLength = 160;

    return raw.Length <= maximumLength
        ? raw
        : string.Concat(raw.AsSpan(0, maximumLength), "...");
}

// Counts the nodes in the event's chain (ChainModel.Path), or 0 when there is no chain.
static int CountChainNodes(JsonElement recordingEvent)
{
    if (recordingEvent.TryGetProperty("chain", out var chain)
        && chain.ValueKind == JsonValueKind.Object
        && chain.TryGetProperty("path", out var path)
        && path.ValueKind == JsonValueKind.Array)
    {
        return path.GetArrayLength();
    }

    return 0;
}
