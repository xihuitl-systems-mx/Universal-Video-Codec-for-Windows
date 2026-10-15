using System.Diagnostics;
using System.IO.Pipes;

namespace HevcCodec;

internal sealed class FfmpegGpuDecoder : IDisposable
{
    // Keep only a few uncompressed 4K frames in memory. Compressed input is written on
    // its own task, so an output pipe cannot block the synchronous MFT's input call.
    private const int MaximumReadyFrames = 2;
    private readonly object _gate = new();
    private readonly Queue<byte[]> _packets = new();
    private readonly Queue<(byte[] Pixels, long Time, long Duration)> _frames = new();
    private readonly PriorityQueue<(long Time, long Duration), long> _timings = new();
    private readonly AutoResetEvent _ready = new(false);
    private readonly SemaphoreSlim _inputReady = new(0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Process _process;
    private readonly Stream _input;
    private readonly NamedPipeServerStream _output;
    private readonly Task _reader;
    private readonly Task _writer;
    private readonly Task<string> _errors;
    private readonly int _frameSize;
    private Exception? _failure;
    private bool _inputComplete;
    private bool _readerComplete;
    private bool _disposed;

    internal static string HelperPath
    {
        get
        {
            string directory = Path.GetDirectoryName(typeof(FfmpegGpuDecoder).Assembly.Location)!;
            string helper = Path.Combine(directory, "ffmpeg.exe");
            if (File.Exists(helper)) return helper;
            string name = Path.GetFileName(directory);
            if (name.Equals("x86", StringComparison.OrdinalIgnoreCase) || name.Equals("x64", StringComparison.OrdinalIgnoreCase))
            {
                string? parent = Path.GetDirectoryName(directory);
                if (parent != null)
                {
                    string sharedHelper = Path.Combine(parent, "ffmpeg.exe");
                    if (File.Exists(sharedHelper)) return sharedHelper;
                }
            }
            return helper;
        }
    }

    internal static void Trace(string message)
    {
        if (Environment.GetEnvironmentVariable("UVC_CODEC_DIAGNOSTICS") == "1")
            Console.Error.WriteLine($"[UVC] {message}");
    }

    internal FfmpegGpuDecoder(int width, int height, bool p010, bool source10Bit)
    {
        string ffmpeg = HelperPath;
        if (!File.Exists(ffmpeg)) throw new FileNotFoundException("GPU decoder helper is missing", ffmpeg);
        _frameSize = checked(width * height * 3 / (p010 ? 1 : 2));
        string pipeName = $"UniversalVideoCodec-{Guid.NewGuid():N}";
        _output = new NamedPipeServerStream(pipeName, PipeDirection.In, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 1024 * 1024, 0);
        var start = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = false,
            RedirectStandardError = true
        };
        string sourceFormat = source10Bit ? "p010le" : "nv12";
        string outputFormat = p010 ? "p010le" : "nv12";
        string filter = sourceFormat == outputFormat
            ? $"hwdownload,format={sourceFormat}"
            : $"scale_cuda=format={outputFormat},hwdownload,format={outputFormat}";
        foreach (string arg in new[] {
            "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
            "-probesize", "32", "-analyzeduration", "0", "-fpsprobesize", "0",
            "-hwaccel", "cuda", "-hwaccel_output_format", "cuda",
            "-threads:v", "1", "-f", "hevc", "-i", "pipe:0", "-an", "-sn", "-dn",
            "-vf", filter, "-fps_mode", "passthrough",
            "-c:v", "rawvideo", "-threads:v", "1", "-pix_fmt", outputFormat,
            "-flush_packets", "1", "-avioflags", "direct", "-f", "rawvideo", $@"\\.\pipe\{pipeName}"
        }) start.ArgumentList.Add(arg);
        try { _process = Process.Start(start) ?? throw new InvalidOperationException("Could not start GPU decoder helper"); }
        catch { _output.Dispose(); throw; }
        Trace($"CUDA helper started, pid={_process.Id}, path={ffmpeg}, sourceFormat={sourceFormat}, outputFormat={outputFormat}");
        _input = _process.StandardInput.BaseStream;
        _errors = _process.StandardError.ReadToEndAsync();
        _reader = Task.Run(ReadFrames);
        _writer = Task.Run(WritePackets);
    }

    internal int Pending { get { lock (_gate) return _timings.Count + _frames.Count; } }
    internal int ReadyFrames { get { lock (_gate) return _frames.Count; } }
    internal bool Finished { get { lock (_gate) return _readerComplete; } }
    internal bool Failed
    {
        get
        {
            lock (_gate)
                return _failure != null || (_readerComplete && !_inputComplete) ||
                    (!_disposed && _process.HasExited && _process.ExitCode != 0);
        }
    }

    internal void Submit(byte[] annexB, long time, long duration)
    {
        lock (_gate)
        {
            if (_disposed || _inputComplete) throw new InvalidOperationException("GPU decoder is closed");
            if (_failure != null) throw new IOException("GPU decoder failed", _failure);
            _timings.Enqueue((time, duration), time);
            _packets.Enqueue(annexB);
        }
        _inputReady.Release();
    }

    internal void CompleteInput()
    {
        lock (_gate)
        {
            if (_inputComplete || _disposed) return;
            _inputComplete = true;
        }
        // The writer closes stdin only after writing every accepted sample.
        _inputReady.Release();
    }

    internal bool TryGetFrame(int timeoutMs, out byte[] pixels, out long time, out long duration)
    {
        long deadline = Environment.TickCount64 + timeoutMs;
        for (;;)
        {
            lock (_gate)
            {
                if (_frames.Count > 0)
                {
                    (pixels, time, duration) = _frames.Dequeue();
                    Monitor.PulseAll(_gate);
                    return true;
                }
                if (_readerComplete || _disposed || _failure != null) break;
            }
            int remaining = (int)Math.Max(0, deadline - Environment.TickCount64);
            if (!_ready.WaitOne(remaining)) break;
        }
        pixels = []; time = duration = 0;
        return false;
    }

    private async Task WritePackets()
    {
        try
        {
            while (true)
            {
                await _inputReady.WaitAsync(_stop.Token).ConfigureAwait(false);
                byte[]? packet;
                lock (_gate)
                {
                    if (_disposed) return;
                    if (_packets.Count == 0)
                    {
                        if (_inputComplete) break;
                        continue;
                    }
                    packet = _packets.Dequeue();
                }
                await _input.WriteAsync(packet, _stop.Token).ConfigureAwait(false);
                await _input.FlushAsync(_stop.Token).ConfigureAwait(false);
            }
            _input.Close();
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException)
        {
            RecordFailure(exception);
        }
    }

    private async Task ReadFrames()
    {
        Stream output = _output;
        try
        {
            await _output.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
            while (true)
            {
                byte[] frame = new byte[_frameSize];
                int offset = 0;
                while (offset < frame.Length)
                {
                    int read = await output.ReadAsync(frame.AsMemory(offset), _stop.Token).ConfigureAwait(false);
                    if (read == 0)
                    {
                        if (offset != 0) RecordFailure(new EndOfStreamException("GPU decoder returned an incomplete frame"));
                        return;
                    }
                    offset += read;
                }
                lock (_gate)
                {
                    while (_frames.Count >= MaximumReadyFrames && !_disposed) Monitor.Wait(_gate);
                    if (_disposed) return;
                    // HEVC samples arrive in decoding order; rawvideo arrives in display order.
                    (long time, long duration) = _timings.Count > 0 ? _timings.Dequeue() : (0, 0);
                    _frames.Enqueue((frame, time, duration));
                }
                SignalReady();
            }
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException)
        {
            RecordFailure(exception);
        }
        finally
        {
            lock (_gate) _readerComplete = true;
            SignalReady();
        }
    }

    private void RecordFailure(Exception exception)
    {
        lock (_gate) { if (!_disposed) _failure ??= exception; }
        Trace($"GPU helper I/O: {exception.GetType().Name}: {exception.Message}");
        SignalReady();
    }

    private void SignalReady()
    {
        try { _ready.Set(); } catch (ObjectDisposedException) { }
    }

    internal string ErrorText => _errors.IsCompletedSuccessfully ? _errors.Result : string.Empty;

    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; Monitor.PulseAll(_gate); }
        _stop.Cancel();
        try { _input.Dispose(); } catch (IOException) { }
        _output.Dispose();
        if (!_process.HasExited)
        {
            try { _process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        }
        try { _process.WaitForExit(500); _errors.Wait(500); } catch (AggregateException) { }
        SignalReady();
        try { Task.WaitAll([_reader, _writer], 1000); } catch (AggregateException) { }
        if (!string.IsNullOrWhiteSpace(ErrorText)) Trace($"GPU helper stderr: {ErrorText.Trim()}");
        _process.Dispose();
        // A task still unwinding cancellation may signal the event; SignalReady handles that race.
        _ready.Dispose();
    }
}
