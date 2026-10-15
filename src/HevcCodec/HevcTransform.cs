using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace HevcCodec;

/// <summary>A synchronous, software HEVC decoder for Media Foundation desktop clients.</summary>
[ComVisible(true), Guid("069338a8-d42a-4b08-a6de-e00669f561d5")]
[ClassInterface(ClassInterfaceType.None)]
public sealed class HevcTransform : IMfTransform, IDisposable
{
    private readonly object _gate = new();
    private readonly Queue<Frame> _frames = new();
    private readonly Dictionary<long, long> _durations = new();
    private IntPtr _decoder;
    private IntPtr _inputType;
    private IntPtr _outputType;
    private Guid _outputSubtype;
    private Guid _inputSubtype;
    private int _width;
    private int _height;
    private bool _draining;
    private byte[] _codecConfig = [];
    private bool _sentCodecConfig;
    private int _naluLengthSize = 4;
    private bool _input10Bit;
    private FfmpegGpuDecoder? _gpu;
    private bool _gpuUnavailable;
    private readonly List<EncodedSample> _gpuReplay = new();
    private readonly Queue<EncodedSample> _cpuReplay = new();
    private long? _lastGpuOutputTime;
    private long? _skipCpuThrough;
    private bool _cpuFlushed;

    private sealed record Frame(byte[] Pixels, int Width, int Height, long Time, long Duration);
    private sealed record EncodedSample(byte[] Bytes, long Time, long Duration, bool RandomAccess);

    public HevcTransform()
    {
        _decoder = De265.de265_new_decoder();
        if (_decoder == IntPtr.Zero) throw new OutOfMemoryException("libde265 decoder creation failed");
        int threads = Math.Clamp(Environment.ProcessorCount - 1, 1, 16);
        De265.de265_start_worker_threads(_decoder, threads);
    }

    ~HevcTransform() => Dispose(false);
    public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }
    private void Dispose(bool disposing)
    {
        lock (_gate)
        {
            Mf.Release(ref _inputType);
            Mf.Release(ref _outputType);
            if (_decoder != IntPtr.Zero) { De265.de265_free_decoder(_decoder); _decoder = IntPtr.Zero; }
            _gpu?.Dispose(); _gpu = null;
            if (disposing) { _frames.Clear(); _durations.Clear(); _gpuReplay.Clear(); _cpuReplay.Clear(); }
        }
    }

    public int GetStreamLimits(out uint inputMin, out uint inputMax, out uint outputMin, out uint outputMax)
    { inputMin = inputMax = outputMin = outputMax = 1; return Mf.S_OK; }
    public int GetStreamCount(out uint inputCount, out uint outputCount)
    { inputCount = outputCount = 1; return Mf.S_OK; }
    public int GetStreamIDs(uint inputIdSize, IntPtr inputIds, uint outputIdSize, IntPtr outputIds) => Mf.E_NOTIMPL;
    public int GetInputStreamInfo(uint streamId, out MftInputStreamInfo info)
    {
        info = new() { Flags = 0x100, Size = 0, MaxLookahead = 0, Alignment = 0 };
        return streamId == 0 ? Mf.S_OK : Mf.E_INVALIDARG;
    }
    public int GetOutputStreamInfo(uint streamId, out MftOutputStreamInfo info)
    {
        info = new() { Flags = 0x107, Size = 0, Alignment = 0 };
        return streamId == 0 ? Mf.S_OK : Mf.E_INVALIDARG;
    }
    public int GetAttributes(out IntPtr attributes) { attributes = IntPtr.Zero; return Mf.E_NOTIMPL; }
    public int GetInputStreamAttributes(uint streamId, out IntPtr attributes) { attributes = IntPtr.Zero; return Mf.E_NOTIMPL; }
    public int GetOutputStreamAttributes(uint streamId, out IntPtr attributes) { attributes = IntPtr.Zero; return Mf.E_NOTIMPL; }
    public int DeleteInputStream(uint streamId) => Mf.E_NOTIMPL;
    public int AddInputStreams(uint count, IntPtr streamIds) => Mf.E_NOTIMPL;

    public int GetInputAvailableType(uint streamId, uint typeIndex, out IntPtr type)
    {
        type = IntPtr.Zero;
        if (streamId != 0) return Mf.E_INVALIDARG;
        if (typeIndex > 1) return Mf.MF_E_NO_MORE_TYPES;
        int hr = Mf.MFCreateMediaType(out type);
        if (hr < 0) return hr;
        hr = Com.SetGuid(type, Mf.MajorType, Mf.Video);
        if (hr >= 0) hr = Com.SetGuid(type, Mf.Subtype, typeIndex == 0 ? Mf.Hevc : Mf.HevcEs);
        if (hr < 0) Mf.Release(ref type);
        return hr;
    }

    public int GetOutputAvailableType(uint streamId, uint typeIndex, out IntPtr type)
    {
        type = IntPtr.Zero;
        if (streamId != 0) return Mf.E_INVALIDARG;
        if (typeIndex > 1) return Mf.MF_E_NO_MORE_TYPES;
        lock (_gate)
        {
            if (_inputType == IntPtr.Zero) return Mf.MF_E_TRANSFORM_TYPE_NOT_SET;
            int hr = Mf.MFCreateMediaType(out type);
            if (hr < 0) return hr;
            hr = Com.CopyAllItems(_inputType, type);
            if (hr < 0) { Mf.Release(ref type); return hr; }
            Com.DeleteItem(type, Mf.MpegSequenceHeader);
            hr = Com.SetGuid(type, Mf.MajorType, Mf.Video);
            Guid subtype = typeIndex == 0 ? Mf.Nv12 : Mf.P010;
            if (hr >= 0) hr = Com.SetGuid(type, Mf.Subtype, subtype);
            if (hr >= 0 && _width > 0 && _height > 0)
                hr = Com.SetUInt64(type, Mf.FrameSize, ((ulong)(uint)_width << 32) | (uint)_height);
            if (hr >= 0) hr = Com.SetUInt32(type, Mf.Compressed, 0);
            if (hr >= 0) hr = Com.SetUInt32(type, Mf.FixedSizeSamples, 1);
            if (hr >= 0) hr = Com.SetUInt32(type, Mf.AllSamplesIndependent, 1);
            if (hr >= 0 && _width > 0 && _height > 0)
                hr = Com.SetUInt32(type, Mf.SampleSize, checked((uint)(_width * _height * 3 / (subtype == Mf.P010 ? 1 : 2))));
            if (hr >= 0 && _width > 0)
                hr = Com.SetUInt32(type, Mf.DefaultStride, (uint)(_width * (subtype == Mf.P010 ? 2 : 1)));
            if (hr < 0) Mf.Release(ref type);
            return hr;
        }
    }

    public int SetInputType(uint streamId, IntPtr type, uint flags)
    {
        if (streamId != 0 || (flags & ~1u) != 0) return Mf.E_INVALIDARG;
        lock (_gate)
        {
            if (type != IntPtr.Zero)
            {
                if (Com.GetGuid(type, Mf.MajorType, out Guid major) < 0 || major != Mf.Video ||
                    Com.GetGuid(type, Mf.Subtype, out Guid subtype) < 0 || (subtype != Mf.Hevc && subtype != Mf.HevcEs))
                    return Mf.E_INVALIDARG;
                if (Com.GetUInt64(type, Mf.FrameSize, out ulong dimensions) < 0) return Mf.E_INVALIDARG;
                int width = (int)(dimensions >> 32), height = (int)(dimensions & 0xffffffff);
                if (!ValidDimensions(width, height)) return Mf.E_INVALIDARG;
                if (_outputType != IntPtr.Zero && (width != _width || height != _height)) return Mf.E_INVALIDARG;
                if ((flags & 1) != 0) return Mf.S_OK;
                _width = width; _height = height;
                _inputSubtype = subtype;
                ReadCodecConfiguration(type);
                FfmpegGpuDecoder.Trace($"Input type {_width}x{_height}, main10={_input10Bit}, configurationBytes={_codecConfig.Length}, assembly={typeof(HevcTransform).Assembly.Location}, helperExists={File.Exists(FfmpegGpuDecoder.HelperPath)}, helper={FfmpegGpuDecoder.HelperPath}");
                Com.AddRef(type);
            }
            else if ((flags & 1) != 0) return Mf.S_OK;
            else { _width = _height = 0; _inputSubtype = Guid.Empty; Mf.Release(ref _outputType); }
            Mf.Release(ref _inputType);
            _inputType = type;
            ResetDecoder();
            return Mf.S_OK;
        }
    }

    public int SetOutputType(uint streamId, IntPtr type, uint flags)
    {
        if (streamId != 0 || (flags & ~1u) != 0) return Mf.E_INVALIDARG;
        lock (_gate)
        {
            if (type != IntPtr.Zero)
            {
                if (Com.GetGuid(type, Mf.MajorType, out Guid major) < 0 || major != Mf.Video ||
                    Com.GetGuid(type, Mf.Subtype, out Guid subtype) < 0 || (subtype != Mf.Nv12 && subtype != Mf.P010))
                    return Mf.E_INVALIDARG;
                if (Com.GetUInt64(type, Mf.FrameSize, out ulong dimensions) < 0) return Mf.E_INVALIDARG;
                int width = (int)(dimensions >> 32), height = (int)(dimensions & 0xffffffff);
                if (!ValidDimensions(width, height) || (_inputType != IntPtr.Zero && (width != _width || height != _height)))
                    return Mf.E_INVALIDARG;
                if ((flags & 1) != 0) return Mf.S_OK;
                if (_outputSubtype != Guid.Empty && _outputSubtype != subtype) ResetDecoder();
                _width = width; _height = height;
                _outputSubtype = subtype;
                Com.AddRef(type);
            }
            else if ((flags & 1) != 0) return Mf.S_OK;
            else _outputSubtype = Guid.Empty;
            Mf.Release(ref _outputType);
            _outputType = type;
            if (type == IntPtr.Zero) ResetDecoder();
            return Mf.S_OK;
        }
    }

    private static bool ValidDimensions(int width, int height) =>
        width >= 2 && height >= 2 && width <= 8192 && height <= 8192 &&
        (width & 1) == 0 && (height & 1) == 0;

    public int GetInputCurrentType(uint streamId, out IntPtr type)
    {
        type = IntPtr.Zero;
        if (streamId != 0) return Mf.E_INVALIDARG;
        lock (_gate) { if (_inputType == IntPtr.Zero) return Mf.MF_E_TRANSFORM_TYPE_NOT_SET; type = _inputType; Com.AddRef(type); return Mf.S_OK; }
    }
    public int GetOutputCurrentType(uint streamId, out IntPtr type)
    {
        type = IntPtr.Zero;
        if (streamId != 0) return Mf.E_INVALIDARG;
        lock (_gate) { if (_outputType == IntPtr.Zero) return Mf.MF_E_TRANSFORM_TYPE_NOT_SET; type = _outputType; Com.AddRef(type); return Mf.S_OK; }
    }
    public int GetInputStatus(uint streamId, out uint flags)
    {
        flags = 0;
        if (streamId != 0) return Mf.E_INVALIDARG;
        lock (_gate) { if (!_draining && _cpuReplay.Count == 0 && _frames.Count < 4 && (_gpu == null || _gpu.ReadyFrames < 2)) flags = 1; }
        return Mf.S_OK;
    }
    public int GetOutputStatus(out uint flags)
    {
        lock (_gate) flags = _frames.Count > 0 || _cpuReplay.Count > 0 || (_gpu?.ReadyFrames ?? 0) > 0 ? 1u : 0u;
        return Mf.S_OK;
    }
    public int SetOutputBounds(long lowerBound, long upperBound) => Mf.E_NOTIMPL;
    public int ProcessEvent(uint inputStreamId, IntPtr mediaEvent) => Mf.E_NOTIMPL;
    public int ProcessMessage(uint message, UIntPtr param)
    {
        lock (_gate)
        {
            switch (message)
            {
                case 0: ResetDecoder(); break; // FLUSH
                case 1: // DRAIN
                    _draining = true;
                    if (_gpu != null) { _gpu.CompleteInput(); return Mf.S_OK; }
                    if (_cpuReplay.Count > 0) return PumpCpuReplay();
                    if (_decoder != IntPtr.Zero)
                    {
                        int hr = De265.de265_flush_data(_decoder);
                        if (De265.de265_isOK(hr) == 0) return Mf.E_FAIL;
                        _cpuFlushed = true;
                        return DecodeAvailable();
                    }
                    break;
                case 2: // Software only; accepting a device manager would imply DXVA support.
                    if (param != UIntPtr.Zero) return Mf.E_NOTIMPL;
                    break;
            }
            return Mf.S_OK;
        }
    }

    public unsafe int ProcessInput(uint inputStreamId, IntPtr sample, uint flags)
    {
        if (inputStreamId != 0 || sample == IntPtr.Zero || flags != 0) return Mf.E_INVALIDARG;
        lock (_gate)
        {
            if (_inputType == IntPtr.Zero || _outputType == IntPtr.Zero) return Mf.MF_E_TRANSFORM_TYPE_NOT_SET;
            if (_draining || _frames.Count >= 4) return Mf.MF_E_NOTACCEPTING;
            if (_gpu != null && (_gpu.Failed || _gpu.Pending >= 128))
            {
                // A synchronous MFT may reject input only when it can produce output.
                // Wait for that output instead of rejecting during the helper's startup.
                if (!_gpu.Failed && CaptureGpuFrame(10000)) return Mf.MF_E_NOTACCEPTING;
                FallBackToCpu(_gpu.Failed ? "GPU helper failed" : "No GPU output after 128 samples and a 10 second wait");
            }
            if (_cpuReplay.Count > 0)
            {
                int replayResult = PumpCpuReplay();
                if (replayResult < 0) return replayResult;
                if (_frames.Count > 0 || _cpuReplay.Count > 0) return Mf.MF_E_NOTACCEPTING;
            }
            IntPtr buffer = IntPtr.Zero;
            try
            {
                int hr = Com.ContiguousBuffer(sample, out buffer);
                if (hr < 0) return hr;
                hr = Com.LockBuffer(buffer, out IntPtr data, out _, out uint length);
                if (hr < 0) return hr;
                try
                {
                    if (length == 0 || length > int.MaxValue) return Mf.E_INVALIDARG;
                    long pts = Com.SampleTime(sample, out long time) >= 0 ? time : 0;
                    long duration = Com.SampleDuration(sample, out long sampleDuration) >= 0 ? sampleDuration : 0;
                    byte[] compressed = CopyCompressedSample((byte*)data, (int)length);
                    if (_gpu == null && !_gpuUnavailable) ReadAnnexBBitDepth(compressed);
                    if (ShouldUseGpu())
                    {
                        try
                        {
                            _gpu ??= new FfmpegGpuDecoder(_width, _height, _outputSubtype == Mf.P010,
                                _input10Bit);
                            var encoded = new EncodedSample(compressed, pts, duration, IsRandomAccess(compressed));
                            RememberGpuSample(encoded);
                            if (!_sentCodecConfig && _codecConfig.Length > 0)
                            {
                                byte[] withConfig = new byte[_codecConfig.Length + compressed.Length];
                                Buffer.BlockCopy(_codecConfig, 0, withConfig, 0, _codecConfig.Length);
                                Buffer.BlockCopy(compressed, 0, withConfig, _codecConfig.Length, compressed.Length);
                                compressed = withConfig;
                                _sentCodecConfig = true;
                            }
                            _gpu.Submit(compressed, pts, duration);
                            return Mf.S_OK;
                        }
                        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
                        {
                            FallBackToCpu($"GPU submission: {exception.GetType().Name}: {exception.Message}");
                            // RememberGpuSample already owns this sample if submission failed.
                            if (_cpuReplay.Count > 0) return PumpCpuReplay();
                        }
                    }
                    return SubmitToCpu(new EncodedSample(compressed, pts, duration, false));
                }
                finally { Com.UnlockBuffer(buffer); }
            }
            catch (DllNotFoundException) { return Mf.E_FAIL; }
            catch (EntryPointNotFoundException) { return Mf.E_FAIL; }
            catch (ArgumentException) { return Mf.E_INVALIDARG; }
            catch (OverflowException) { return Mf.E_INVALIDARG; }
            finally { Mf.Release(ref buffer); }
        }
    }

    public int ProcessOutput(uint flags, uint outputBufferCount, IntPtr outputBuffers, out uint status)
    {
        status = 0;
        if (flags != 0 || outputBufferCount != 1 || outputBuffers == IntPtr.Zero) return Mf.E_INVALIDARG;
        lock (_gate)
        {
            if (_inputType == IntPtr.Zero || _outputType == IntPtr.Zero) return Mf.MF_E_TRANSFORM_TYPE_NOT_SET;
            if (_gpu != null && _frames.Count == 0)
            {
                if (!CaptureGpuFrame(_draining ? 5000 : 5) &&
                    (_gpu.Failed || (_draining && !_gpu.Finished)))
                    FallBackToCpu(_gpu.Failed ? "GPU helper failed while getting output" : "GPU drain exceeded 5 seconds");
            }
            if (_gpu == null && _cpuReplay.Count > 0)
            {
                int replayResult = PumpCpuReplay();
                if (replayResult < 0) return replayResult;
            }
            if (_frames.Count == 0)
            {
                if (_draining && (_gpu == null || _gpu.Finished)) _draining = false;
                return Mf.MF_E_TRANSFORM_NEED_MORE_INPUT;
            }
            Frame frame = _frames.Peek();
            if (frame.Width != _width || frame.Height != _height)
            {
                _width = frame.Width; _height = frame.Height;
                Mf.Release(ref _outputType);
                return Mf.MF_E_TRANSFORM_STREAM_CHANGE;
            }
            MftOutputDataBuffer output = Marshal.PtrToStructure<MftOutputDataBuffer>(outputBuffers);
            if (output.Sample != IntPtr.Zero) return Mf.E_INVALIDARG; // MFT_OUTPUT_STREAM_PROVIDES_SAMPLES
            IntPtr sample = IntPtr.Zero, buffer = IntPtr.Zero;
            try
            {
                int hr = Mf.MFCreateSample(out sample);
                if (hr < 0) return hr;
                hr = Mf.MFCreateMemoryBuffer((uint)frame.Pixels.Length, out buffer);
                if (hr < 0) return hr;
                hr = Com.LockBuffer(buffer, out IntPtr data, out uint capacity, out _);
                if (hr < 0) return hr;
                try
                {
                    if (capacity < frame.Pixels.Length) return Mf.E_FAIL;
                    Marshal.Copy(frame.Pixels, 0, data, frame.Pixels.Length);
                    hr = Com.SetBufferLength(buffer, (uint)frame.Pixels.Length);
                    if (hr < 0) return hr;
                }
                finally { Com.UnlockBuffer(buffer); }
                hr = Com.AddBuffer(sample, buffer);
                if (hr < 0) return hr;
                Com.SetSampleTime(sample, frame.Time);
                if (frame.Duration > 0) Com.SetSampleDuration(sample, frame.Duration);
                output.Sample = sample;
                output.Status = 0;
                output.Events = IntPtr.Zero;
                Marshal.StructureToPtr(output, outputBuffers, false);
                sample = IntPtr.Zero; // Ownership transferred to the Media Foundation caller.
                _frames.Dequeue();
                if (_gpu != null) _lastGpuOutputTime = frame.Time;
                return Mf.S_OK;
            }
            finally { Mf.Release(ref buffer); Mf.Release(ref sample); }
        }
    }

    private void ResetDecoder()
    {
        _gpu?.Dispose(); _gpu = null;
        if (_decoder != IntPtr.Zero) De265.de265_reset(_decoder);
        _frames.Clear(); _durations.Clear(); _draining = false; _sentCodecConfig = false;
        _gpuReplay.Clear(); _cpuReplay.Clear(); _lastGpuOutputTime = _skipCpuThrough = null;
        _cpuFlushed = false;
    }

    private bool ShouldUseGpu() => !_gpuUnavailable && _width * (long)_height >= 3840L * 2160L &&
        File.Exists(FfmpegGpuDecoder.HelperPath);

    private bool CaptureGpuFrame(int timeoutMs)
    {
        if (!_gpu!.TryGetFrame(timeoutMs, out byte[] pixels, out long time, out long duration)) return false;
        _frames.Enqueue(new Frame(pixels, _width, _height, time, duration));
        return true;
    }

    private void RememberGpuSample(EncodedSample sample)
    {
        if (sample.RandomAccess)
        {
            // Retain two GOPs so CPU fallback can also recover frames still being reordered.
            int lastAccessPoint = _gpuReplay.FindLastIndex(item => item.RandomAccess);
            if (lastAccessPoint > 0) _gpuReplay.RemoveRange(0, lastAccessPoint);
        }
        _gpuReplay.Add(sample);
    }

    private void FallBackToCpu(string reason)
    {
        FfmpegGpuDecoder.Trace($"Falling back to libde265 CPU: {reason}; pending={_gpu?.Pending ?? 0}, ready={_gpu?.ReadyFrames ?? 0}, retainedSamples={_gpuReplay.Count}");
        _gpu?.Dispose(); _gpu = null;
        _gpuUnavailable = true;
        De265.de265_reset(_decoder);
        _sentCodecConfig = false; _cpuFlushed = false;
        _durations.Clear();
        _skipCpuThrough = _lastGpuOutputTime;
        foreach (EncodedSample sample in _gpuReplay) _cpuReplay.Enqueue(sample);
        _gpuReplay.Clear();
    }

    private int PumpCpuReplay()
    {
        while (_frames.Count < 4 && _cpuReplay.Count > 0)
        {
            int hr = SubmitToCpu(_cpuReplay.Dequeue());
            if (hr < 0) return hr;
        }
        if (_cpuReplay.Count == 0 && _draining && !_cpuFlushed)
        {
            if (De265.de265_isOK(De265.de265_flush_data(_decoder)) == 0) return Mf.E_FAIL;
            _cpuFlushed = true;
            return DecodeAvailable();
        }
        return Mf.S_OK;
    }

    private unsafe int SubmitToCpu(EncodedSample sample)
    {
        int hr;
        _durations[sample.Time] = sample.Duration;
        if (!_sentCodecConfig && _codecConfig.Length > 0)
        {
            fixed (byte* config = _codecConfig)
                hr = De265.de265_push_data(_decoder, (IntPtr)config, _codecConfig.Length, sample.Time, IntPtr.Zero);
            if (De265.de265_isOK(hr) == 0) return Mf.E_FAIL;
            _sentCodecConfig = true;
        }
        fixed (byte* converted = sample.Bytes)
            hr = De265.de265_push_data(_decoder, (IntPtr)converted, sample.Bytes.Length, sample.Time, IntPtr.Zero);
        if (De265.de265_isOK(hr) == 0) return Mf.E_FAIL;
        if (_inputSubtype == Mf.Hevc) De265.de265_push_end_of_frame(_decoder);
        return DecodeAvailable();
    }

    private static bool IsRandomAccess(ReadOnlySpan<byte> annexB)
    {
        for (int i = 0; i + 4 < annexB.Length; i++)
        {
            if (annexB[i] != 0 || annexB[i + 1] != 0) continue;
            int header = annexB[i + 2] == 1 ? i + 3 :
                annexB[i + 2] == 0 && annexB[i + 3] == 1 ? i + 4 : -1;
            if (header < 0 || header >= annexB.Length) continue;
            int nalType = (annexB[header] >> 1) & 0x3f;
            if (nalType is >= 16 and <= 23) return true;
        }
        return false;
    }

    private unsafe byte[] CopyCompressedSample(byte* data, int length)
    {
        if (HasStartCode(data, length)) return new ReadOnlySpan<byte>(data, length).ToArray();
        return ConvertLengthPrefixed(data, length, _naluLengthSize);
    }

    private void ReadCodecConfiguration(IntPtr type)
    {
        _codecConfig = [];
        _input10Bit = false;
        _naluLengthSize = 4;
        if (Com.GetBlobSize(type, Mf.MpegSequenceHeader, out uint size) < 0 || size < 3 || size > 1024 * 1024) return;
        byte[] hvcc = new byte[size];
        if (Com.GetBlob(type, Mf.MpegSequenceHeader, hvcc, out uint read) < 0 || read != size) return;
        if (hvcc[0] == 0)
        {
            _codecConfig = hvcc;
            ReadAnnexBBitDepth(hvcc);
            return;
        }
        if (size < 23 || hvcc[0] != 1) return;
        _input10Bit = (hvcc[17] & 7) > 0 || (hvcc[1] & 0x1f) == 2;
        _naluLengthSize = (hvcc[21] & 3) + 1;
        using var annexB = new MemoryStream();
        int offset = 23;
        int arrays = hvcc[22];
        for (int a = 0; a < arrays && offset + 3 <= hvcc.Length; a++)
        {
            offset++; // array completeness and NAL type
            int count = (hvcc[offset] << 8) | hvcc[offset + 1]; offset += 2;
            for (int n = 0; n < count && offset + 2 <= hvcc.Length; n++)
            {
                int length = (hvcc[offset] << 8) | hvcc[offset + 1]; offset += 2;
                if (length <= 0 || offset + length > hvcc.Length) { _codecConfig = []; return; }
                annexB.Write([0, 0, 0, 1]);
                annexB.Write(hvcc, offset, length);
                offset += length;
            }
        }
        _codecConfig = annexB.ToArray();
    }

    private void ReadAnnexBBitDepth(ReadOnlySpan<byte> annexB)
    {
        // Media Foundation can expose HEVC_ES with in-band SPS instead of an hvcC blob.
        // The hardware download format must match that SPS even when output is NV12.
        for (int i = 0; i + 5 < annexB.Length; i++)
        {
            if (annexB[i] != 0 || annexB[i + 1] != 0) continue;
            int header = annexB[i + 2] == 1 ? i + 3 :
                annexB[i + 2] == 0 && annexB[i + 3] == 1 ? i + 4 : -1;
            if (header < 0 || header + 3 >= annexB.Length || ((annexB[header] >> 1) & 0x3f) != 33) continue;
            int end = header + 2;
            while (end + 3 < annexB.Length && !(annexB[end] == 0 && annexB[end + 1] == 0 &&
                (annexB[end + 2] == 1 || (annexB[end + 2] == 0 && annexB[end + 3] == 1)))) end++;
            if (end + 3 >= annexB.Length) end = annexB.Length;
            ReadOnlySpan<byte> escaped = annexB.Slice(header + 2, end - header - 2);
            byte[] rbsp = new byte[escaped.Length];
            int length = 0;
            for (int j = 0; j < escaped.Length; j++)
            {
                if (j >= 2 && escaped[j] == 3 && escaped[j - 1] == 0 && escaped[j - 2] == 0) continue;
                rbsp[length++] = escaped[j];
            }
            try
            {
                var bits = new SpsBits(rbsp.AsSpan(0, length));
                bits.Skip(4); // sps_video_parameter_set_id
                int subLayers = (int)bits.Read(3);
                bits.Skip(1); // sps_temporal_id_nesting_flag
                bits.Skip(96); // general profile_tier_level, including level_idc
                Span<bool> subProfile = stackalloc bool[7];
                Span<bool> subLevel = stackalloc bool[7];
                for (int layer = 0; layer < subLayers; layer++)
                {
                    subProfile[layer] = bits.Read(1) != 0;
                    subLevel[layer] = bits.Read(1) != 0;
                }
                if (subLayers > 0) bits.Skip((8 - subLayers) * 2);
                for (int layer = 0; layer < subLayers; layer++)
                {
                    if (subProfile[layer]) bits.Skip(88);
                    if (subLevel[layer]) bits.Skip(8);
                }
                bits.UnsignedExpGolomb(); // sps_seq_parameter_set_id
                uint chromaFormat = bits.UnsignedExpGolomb();
                if (chromaFormat == 3) bits.Skip(1);
                bits.UnsignedExpGolomb(); // pic_width_in_luma_samples
                bits.UnsignedExpGolomb(); // pic_height_in_luma_samples
                if (bits.Read(1) != 0)
                    for (int edge = 0; edge < 4; edge++) bits.UnsignedExpGolomb();
                uint extraBits = bits.UnsignedExpGolomb();
                if (extraBits > 4) return;
                _input10Bit = extraBits > 0;
                FfmpegGpuDecoder.Trace($"In-band HEVC SPS: bitDepth={8 + extraBits}");
                return;
            }
            catch (ArgumentException) { return; } // Malformed SPS is handled by the decoder.
        }
    }

    private ref struct SpsBits(ReadOnlySpan<byte> bytes)
    {
        private readonly ReadOnlySpan<byte> _bytes = bytes;
        private int _position;

        internal uint Read(int count)
        {
            if (count < 0 || count > 32 || _position + count > _bytes.Length * 8)
                throw new ArgumentException("Truncated HEVC SPS");
            uint value = 0;
            for (int bit = 0; bit < count; bit++, _position++)
                value = (value << 1) | (uint)((_bytes[_position / 8] >> (7 - _position % 8)) & 1);
            return value;
        }

        internal void Skip(int count)
        {
            if (count < 0 || _position + count > _bytes.Length * 8)
                throw new ArgumentException("Truncated HEVC SPS");
            _position += count;
        }

        internal uint UnsignedExpGolomb()
        {
            int zeros = 0;
            while (Read(1) == 0)
                if (++zeros > 30) throw new ArgumentException("Invalid HEVC SPS value");
            return ((1u << zeros) - 1) + Read(zeros);
        }
    }

    private static unsafe bool HasStartCode(byte* data, int length) => length >= 3 &&
        (data[0] == 0 && data[1] == 0 && (data[2] == 1 || (length >= 4 && data[2] == 0 && data[3] == 1)));

    private static unsafe byte[] ConvertLengthPrefixed(byte* data, int length, int lengthSize)
    {
        if (lengthSize is < 1 or > 4) throw new ArgumentException("Invalid HEVC NAL length size");
        using var output = new MemoryStream(length + 64);
        int offset = 0;
        while (offset + lengthSize <= length)
        {
            int naluLength = 0;
            for (int i = 0; i < lengthSize; i++) naluLength = checked((naluLength << 8) | data[offset + i]);
            offset += lengthSize;
            if (naluLength <= 0 || naluLength > length - offset) throw new ArgumentException("Invalid length-prefixed HEVC sample");
            output.Write([0, 0, 0, 1]);
            output.Write(new ReadOnlySpan<byte>(data + offset, naluLength));
            offset += naluLength;
        }
        if (offset != length) throw new ArgumentException("Trailing bytes in HEVC sample");
        return output.ToArray();
    }

    private int DecodeAvailable()
    {
        for (int i = 0; i < 1024; i++)
        {
            int hr = De265.de265_decode(_decoder, out int more);
            int captured = 0;
            while (De265.de265_peek_next_picture(_decoder) is var picture && picture != IntPtr.Zero)
            {
                try
                {
                    long time = De265.de265_get_image_PTS(picture);
                    if (_skipCpuThrough is long skip && time <= skip) _durations.Remove(time);
                    else _frames.Enqueue(CopyPicture(picture));
                }
                catch (ArgumentException) { return Mf.E_FAIL; }
                finally { De265.de265_release_next_picture(_decoder); }
                captured++;
            }
            if (hr == 13 || more == 0) break; // Need another compressed sample.
            if (hr == 9 && captured > 0) continue; // Decoded-picture buffer was full; now drained.
            if (De265.de265_isOK(hr) == 0) return Mf.E_FAIL;
            if (i == 1023) return Mf.E_FAIL;
        }
        return Mf.S_OK;
    }

    private unsafe Frame CopyPicture(IntPtr image)
    {
        int width = De265.de265_get_image_width(image, 0);
        int height = De265.de265_get_image_height(image, 0);
        if (!ValidDimensions(width, height)) throw new ArgumentException("Unsupported HEVC frame size");
        int bits = De265.de265_get_bits_per_pixel(image, 0);
        if (bits < 8 || bits > 12) throw new ArgumentException("Unsupported HEVC bit depth");
        bool p010 = _outputSubtype == Mf.P010;
        byte[] pixels = new byte[checked(width * height * 3 / (p010 ? 1 : 2))];
        byte* y = (byte*)De265.de265_get_image_plane(image, 0, out int yStride);
        byte* u = (byte*)De265.de265_get_image_plane(image, 1, out int uStride);
        byte* v = (byte*)De265.de265_get_image_plane(image, 2, out int vStride);
        if (y == null || u == null || v == null) throw new ArgumentException("Missing HEVC image plane");
        int bytesPerSourcePixel = bits <= 8 ? 1 : 2;
        if (yStride < width * bytesPerSourcePixel || uStride < width / 2 * bytesPerSourcePixel || vStride < width / 2 * bytesPerSourcePixel)
            throw new ArgumentException("Invalid HEVC image stride");
        fixed (byte* dst = pixels)
        {
            if (!p010)
            {
                for (int row = 0; row < height; row++)
                    for (int col = 0; col < width; col++)
                        dst[row * width + col] = bits <= 8 ? y[row * yStride + col] :
                            (byte)(*(ushort*)(y + row * yStride + col * 2) >> (bits - 8));
                byte* uv = dst + width * height;
                for (int row = 0; row < height / 2; row++)
                    for (int col = 0; col < width / 2; col++)
                    {
                        uv[row * width + col * 2] = bits <= 8 ? u[row * uStride + col] :
                            (byte)(*(ushort*)(u + row * uStride + col * 2) >> (bits - 8));
                        uv[row * width + col * 2 + 1] = bits <= 8 ? v[row * vStride + col] :
                            (byte)(*(ushort*)(v + row * vStride + col * 2) >> (bits - 8));
                    }
            }
            else
            {
                ushort* yOut = (ushort*)dst;
                for (int row = 0; row < height; row++)
                    for (int col = 0; col < width; col++)
                    {
                        int value = bits <= 8 ? y[row * yStride + col] : *(ushort*)(y + row * yStride + col * 2);
                        yOut[row * width + col] = (ushort)(bits <= 10 ? value << (16 - bits) : value >> (bits - 10) << 6);
                    }
                ushort* uvOut = yOut + width * height;
                for (int row = 0; row < height / 2; row++)
                    for (int col = 0; col < width / 2; col++)
                    {
                        int uu = bits <= 8 ? u[row * uStride + col] : *(ushort*)(u + row * uStride + col * 2);
                        int vv = bits <= 8 ? v[row * vStride + col] : *(ushort*)(v + row * vStride + col * 2);
                        uvOut[row * width + col * 2] = (ushort)(bits <= 10 ? uu << (16 - bits) : uu >> (bits - 10) << 6);
                        uvOut[row * width + col * 2 + 1] = (ushort)(bits <= 10 ? vv << (16 - bits) : vv >> (bits - 10) << 6);
                    }
            }
        }
        long pts = De265.de265_get_image_PTS(image);
        _durations.Remove(pts, out long duration);
        return new Frame(pixels, width, height, pts, duration);
    }
}
