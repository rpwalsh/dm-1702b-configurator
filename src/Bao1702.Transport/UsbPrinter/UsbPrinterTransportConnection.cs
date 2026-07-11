using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Bao1702.Transport.Abstractions;
using Bao1702.Transport.Diagnostics;
using Bao1702.Transport.Framing;

namespace Bao1702.Transport.UsbPrinter;

/// <summary>
/// Transport connection that communicates with a DM-1702 radio via the Windows USB printer-class
/// driver, using overlapped file I/O for framed request/response exchanges.
/// </summary>
public sealed class UsbPrinterTransportConnection : ITransportConnection
{
    private const string OpenPathProperty = "ProbeOpenPath";
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileFlagOverlapped = 0x40000000;

    private readonly TransportTimeouts _timeouts;
    private readonly Func<Stream>? _streamFactory;
    private SafeFileHandle? _handle;
    private Stream? _stream;

    public UsbPrinterTransportConnection(TransportEndpoint endpoint, TransportTimeouts? timeouts = null, Func<Stream>? streamFactory = null)
    {
        Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        _timeouts = timeouts ?? TransportTimeouts.Default;
        _streamFactory = streamFactory;
    }

    public TransportEndpoint Endpoint { get; }

    public bool IsOpen { get; private set; }

    public ITransportTraceSink? TraceSink { get; set; }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsOpen)
        {
            return Task.CompletedTask;
        }

        var openPath = ResolveOpenPath(Endpoint);
        if (_streamFactory is not null)
        {
            _stream = _streamFactory();
        }
        else
        {
            _handle = CreateFile(openPath, GenericRead | GenericWrite, FileShare.ReadWrite, IntPtr.Zero, FileMode.Open, FileFlagOverlapped, IntPtr.Zero);
            if (_handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                _handle.Dispose();
                _handle = null;
                throw new InvalidOperationException($"CreateFile failed for USB printer transport path '{openPath}': {new Win32Exception(error).Message}");
            }

            _stream = new FileStream(_handle, FileAccess.ReadWrite, bufferSize: 4096, isAsync: true);
        }

        IsOpen = true;
        TraceSink?.TraceMessage(TransportTraceLevel.Information, TransportTraceDirection.Internal, $"Opened USB printer-class transport handle for {openPath}.");
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsOpen)
        {
            return Task.CompletedTask;
        }

        TraceSink?.TraceMessage(TransportTraceLevel.Information, TransportTraceDirection.Internal, $"Closing USB printer-class transport handle for {Endpoint.DisplayName}.");
        _stream?.Dispose();
        _stream = null;
        _handle?.Dispose();
        _handle = null;
        IsOpen = false;
        return Task.CompletedTask;
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TraceSink?.TraceMessage(
            TransportTraceLevel.Information,
            TransportTraceDirection.Internal,
            $"Resetting USB printer-class transport for {Endpoint.DisplayName}.");

        await DisconnectAsync(cancellationToken).ConfigureAwait(false);
        await ConnectAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeouts.ReadTimeout);

        try
        {
            // Single read call — the caller (ReadExpectedPayloadAsync) already accumulates
            // partial reads in a loop, so coalescing here is unnecessary and adds latency
            // that can exceed the radio's session timeout during bulk transfers.
            var read = await _stream!.ReadAsync(buffer, timeoutCts.Token).ConfigureAwait(false);

            if (read > 0)
            {
                TraceSink?.TraceMessage(
                    TransportTraceLevel.Debug,
                    TransportTraceDirection.FromDevice,
                    $"USB printer read {read} byte(s).\n{HexDump.Format(buffer.Span[..read])}",
                    buffer[..read].ToArray());
            }

            return read;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out reading from USB printer transport endpoint {Endpoint.DisplayName}.", ex);
        }
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeouts.WriteTimeout);

        try
        {
            await _stream!.WriteAsync(buffer, timeoutCts.Token).ConfigureAwait(false);
            TraceSink?.TraceMessage(
                TransportTraceLevel.Debug,
                TransportTraceDirection.ToDevice,
                $"USB printer write {buffer.Length} byte(s).\n{HexDump.Format(buffer.Span)}",
                buffer.ToArray());
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out writing to USB printer transport endpoint {Endpoint.DisplayName}.", ex);
        }
    }

    public async Task<byte[]> ExchangeAsync(ReadOnlyMemory<byte> request, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeouts.ExchangeTimeout);

        await WriteAsync(request, timeoutCts.Token).ConfigureAwait(false);

        using var responseBuffer = new MemoryStream();
        var chunk = new byte[256];
        while (true)
        {
            var bytesRead = await ReadAsync(chunk, timeoutCts.Token).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                throw new TimeoutException($"Timed out waiting for a response frame from {Endpoint.DisplayName}.");
            }

            responseBuffer.Write(chunk, 0, bytesRead);
            var candidate = responseBuffer.ToArray();
            if (TransportFrameCodec.TryDecode(candidate, out _, out _))
            {
                return candidate;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
    }

    private static string ResolveOpenPath(TransportEndpoint endpoint)
    {
        if (endpoint.Properties.TryGetValue(OpenPathProperty, out var openPath) && !string.IsNullOrWhiteSpace(openPath))
        {
            return openPath;
        }

        throw new InvalidOperationException("USB printer transport endpoint does not define a probe-open path.");
    }

    private void EnsureOpen()
    {
        if (!IsOpen || _stream is null)
        {
            throw new InvalidOperationException("USB printer transport connection is not open.");
        }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, FileShare dwShareMode, IntPtr lpSecurityAttributes, FileMode dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);
}
