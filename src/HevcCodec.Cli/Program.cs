using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using HevcCodec;

const string ProductName = "Universal Video Codec for Windows";

static unsafe int DirectShowTest(string path)
{
    Guid clsid = new("e436ebb3-524f-11ce-9f53-0020af0ba770");
    Guid graphId = new("56a868a9-0ad4-11ce-b03a-0020af0ba770");
    object graphObject = Activator.CreateInstance(Type.GetTypeFromCLSID(clsid, throwOnError: true)!)!;
    IntPtr unknown = Marshal.GetIUnknownForObject(graphObject), graph = IntPtr.Zero;
    IntPtr control = IntPtr.Zero, seeking = IntPtr.Zero;
    try
    {
        Mf.Check(Marshal.QueryInterface(unknown, ref graphId, out graph));
        void** v = *(void***)graph;
        var renderFile = (delegate* unmanaged[Stdcall]<IntPtr, char*, IntPtr, int>)v[13];
        string fullPath = Path.GetFullPath(path);
        fixed (char* file = fullPath) Mf.Check(renderFile(graph, file, IntPtr.Zero));

        // Hide the diagnostic renderer and mute it; the original is read without writing media files.
        Guid windowId = new("56a868b4-0ad4-11ce-b03a-0020af0ba770");
        if (Marshal.QueryInterface(unknown, ref windowId, out IntPtr window) >= 0)
        {
            try { ((delegate* unmanaged[Stdcall]<IntPtr, int, int>)(*(void***)window)[13])(window, 0); }
            finally { Mf.Release(ref window); }
        }
        Guid audioId = new("56a868b3-0ad4-11ce-b03a-0020af0ba770");
        if (Marshal.QueryInterface(unknown, ref audioId, out IntPtr audio) >= 0)
        {
            try { ((delegate* unmanaged[Stdcall]<IntPtr, int, int>)(*(void***)audio)[7])(audio, -10000); }
            finally { Mf.Release(ref audio); }
        }
        Guid controlId = new("56a868b1-0ad4-11ce-b03a-0020af0ba770");
        Guid seekingId = new("36b73880-c2c8-11cf-8b46-00805f6cef60");
        Mf.Check(Marshal.QueryInterface(unknown, ref controlId, out control));
        Mf.Check(Marshal.QueryInterface(unknown, ref seekingId, out seeking));
        Mf.Check(((delegate* unmanaged[Stdcall]<IntPtr, int>)(*(void***)control)[7])(control));
        Thread.Sleep(4000);
        long position = 0;
        Mf.Check(((delegate* unmanaged[Stdcall]<IntPtr, long*, int>)(*(void***)seeking)[12])(seeking, &position));
        if (position < 10_000_000) throw new InvalidDataException("DirectShow playback did not advance one second");
        Console.WriteLine($"DirectShow opened and played {Path.GetFileName(path)}; position advanced to {position / 10_000_000.0:F2} seconds. Test covers the beginning of the file.");
        return 0;
    }
    finally
    {
        if (control != IntPtr.Zero) ((delegate* unmanaged[Stdcall]<IntPtr, int>)(*(void***)control)[9])(control);
        Mf.Release(ref seeking); Mf.Release(ref control); Mf.Release(ref graph); Mf.Release(ref unknown);
        Marshal.FinalReleaseComObject(graphObject);
    }
}

static int Register(bool remove)
{
    string host = Path.Combine(AppContext.BaseDirectory, "HevcCodec.comhost.dll");
    if (!File.Exists(host)) throw new FileNotFoundException("Place HevcCodec.comhost.dll beside the CLI executable", host);
    Guid clsid = Mf.Clsid;
    if (remove)
    {
        Mf.Check(Mf.MFTUnregister(clsid));
        RunRegsvr($"/u /s \"{host}\"");
    }
    else
    {
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "libde265.dll")))
            throw new FileNotFoundException("libde265.dll must be beside HevcCodec.comhost.dll");
        RunRegsvr($"/s \"{host}\"");
        Guid category = Mf.DecoderCategory;
        MftRegisterType[] input = [new(Mf.Hevc), new(Mf.HevcEs)];
        MftRegisterType[] output = [new(Mf.Nv12), new(Mf.P010)];
        Mf.Check(Mf.MFTRegister(clsid, category, $"{ProductName} HEVC decoder",
            0, (uint)input.Length, input, (uint)output.Length, output, IntPtr.Zero));
        VerifyCatalog();
    }
    Console.WriteLine(remove ? "Decoder unregistered." : $"Decoder registered for {(Environment.Is64BitProcess ? "x64" : "x86")} Media Foundation desktop apps.");
    return 0;
}

static void VerifyCatalog()
{
    Guid clsid = Mf.Clsid;
    IntPtr name = IntPtr.Zero, inputs = IntPtr.Zero, outputs = IntPtr.Zero, attributes = IntPtr.Zero;
    try
    {
        Mf.Check(Mf.MFTGetInfo(clsid, out name, out inputs, out uint inputCount,
            out outputs, out uint outputCount, out attributes));
        if (inputCount < 2 || outputCount < 2)
            throw new InvalidDataException($"Incomplete Media Foundation catalog entry ({inputCount} inputs, {outputCount} outputs)");
    }
    finally
    {
        if (name != IntPtr.Zero) Marshal.FreeCoTaskMem(name);
        if (inputs != IntPtr.Zero) Marshal.FreeCoTaskMem(inputs);
        if (outputs != IntPtr.Zero) Marshal.FreeCoTaskMem(outputs);
        Mf.Release(ref attributes);
    }
}

static unsafe int EnumerationTest()
{
    Mf.Check(Mf.MFStartup(0x20070, 0));
    IntPtr activations = IntPtr.Zero;
    uint count = 0;
    bool found = false;
    try
    {
        Guid category = Mf.DecoderCategory;
        MftRegisterType input = new(Mf.Hevc);
        Mf.Check(Mf.MFTEnumEx(category, 0x41, ref input, IntPtr.Zero, out activations, out count));
        for (int i = 0; i < count; i++)
        {
            IntPtr activation = Marshal.ReadIntPtr(activations, i * IntPtr.Size);
            if (Com.GetGuid(activation, Mf.TransformClsidAttribute, out Guid clsid) < 0 || clsid != Mf.Clsid) continue;
            Guid iid = new("bf94c121-5b05-4e6f-8000-ba598961414d");
            IntPtr transform = IntPtr.Zero;
            Mf.Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)(*(void***)activation)[33])(activation, &iid, &transform));
            Mf.Release(ref transform);
            found = true;
        }
        if (!found) throw new InvalidDataException("Windows did not enumerate the registered HEVC decoder");
        Console.WriteLine($"MFTEnumEx selected and activated the own HEVC decoder for {(Environment.Is64BitProcess ? "x64" : "x86")}.");
        return 0;
    }
    finally
    {
        if (activations != IntPtr.Zero)
        {
            for (int i = 0; i < count; i++) Marshal.Release(Marshal.ReadIntPtr(activations, i * IntPtr.Size));
            Marshal.FreeCoTaskMem(activations);
        }
        Mf.MFShutdown();
    }
}

static void RunRegsvr(string args)
{
    string system = Environment.Is64BitProcess ? "System32" : "SysWOW64";
    using Process process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), system, "regsvr32.exe"), args)
    { UseShellExecute = false, CreateNoWindow = true }) ?? throw new InvalidOperationException("Could not start regsvr32");
    process.WaitForExit();
    if (process.ExitCode != 0) throw new InvalidOperationException($"regsvr32 failed with exit code {process.ExitCode}; run as administrator");
}

static unsafe IntPtr CreateComTransform(out nint module, out IntPtr factory)
{
    string host = Path.Combine(AppContext.BaseDirectory, "HevcCodec.comhost.dll");
    module = NativeLibrary.Load(host);
    factory = IntPtr.Zero;
    Guid clsid = Mf.Clsid;
    Guid iidFactory = new("00000001-0000-0000-c000-000000000046");
    Guid iidTransform = new("bf94c121-5b05-4e6f-8000-ba598961414d");
    var getClassObject = (delegate* unmanaged[Stdcall]<Guid*, Guid*, IntPtr*, int>)NativeLibrary.GetExport(module, "DllGetClassObject");
    fixed (IntPtr* factoryPointer = &factory)
        Mf.Check(getClassObject(&clsid, &iidFactory, factoryPointer));
    void** factoryVtable = *(void***)factory;
    var createInstance = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)factoryVtable[3];
    IntPtr transform = IntPtr.Zero;
    Mf.Check(createInstance(factory, IntPtr.Zero, &iidTransform, &transform));
    return transform;
}

static unsafe int NativeSetInputType(IntPtr p, IntPtr type)
{
    void** v = *(void***)p;
    return ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, uint, int>)v[15])(p, 0, type, 0);
}
static unsafe int NativeGetOutputAvailableType(IntPtr p, uint index, out IntPtr type)
{
    void** v = *(void***)p;
    fixed (IntPtr* output = &type)
        return ((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr*, int>)v[14])(p, 0, index, output);
}
static unsafe int NativeSetOutputType(IntPtr p, IntPtr type)
{
    void** v = *(void***)p;
    return ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, uint, int>)v[16])(p, 0, type, 0);
}
static unsafe int NativeProcessInput(IntPtr p, IntPtr sample)
{
    void** v = *(void***)p;
    return ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, uint, int>)v[24])(p, 0, sample, 0);
}
static unsafe int NativeProcessMessage(IntPtr p, uint message)
{
    void** v = *(void***)p;
    return ((delegate* unmanaged[Stdcall]<IntPtr, uint, UIntPtr, int>)v[23])(p, message, UIntPtr.Zero);
}
static unsafe int NativeProcessOutput(IntPtr p, IntPtr outputBuffer)
{
    void** v = *(void***)p;
    uint status;
    return ((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr, uint*, int>)v[25])(p, 0, 1, outputBuffer, &status);
}

static unsafe int PlaybackTest(string path, int maxFrames = int.MaxValue)
{
    Mf.Check(Mf.MFStartup(0x20070, 0));
    IntPtr reader = IntPtr.Zero, outputType = IntPtr.Zero;
    try
    {
        Mf.Check(Mf.MFCreateSourceReaderFromURL(Path.GetFullPath(path), IntPtr.Zero, out reader));
        Mf.Check(Mf.MFCreateMediaType(out outputType));
        Mf.Check(Com.SetGuid(outputType, Mf.MajorType, Mf.Video));
        Mf.Check(Com.SetGuid(outputType, Mf.Subtype, Mf.P010));
        void** v = *(void***)reader;
        var setType = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, int>)v[7];
        const uint FirstVideoStream = 0xfffffffc;
        Mf.Check(setType(reader, FirstVideoStream, IntPtr.Zero, outputType));
        var readSample = (delegate* unmanaged[Stdcall]<IntPtr, uint, uint, uint*, uint*, long*, IntPtr*, int>)v[9];
        int frames = 0;
        var elapsed = Stopwatch.StartNew();
        for (;;)
        {
            uint actualStream = 0, streamFlags = 0;
            long timestamp = 0;
            IntPtr sample = IntPtr.Zero;
            int hr = readSample(reader, FirstVideoStream, 0, &actualStream, &streamFlags, &timestamp, &sample);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
            try { if (sample != IntPtr.Zero) frames++; }
            finally { Mf.Release(ref sample); }
            if ((streamFlags & 0x2) != 0 || frames >= maxFrames) break;
        }
        Console.WriteLine($"Media Foundation {(Environment.Is64BitProcess ? "x64" : "x86")} decoded {frames} frames from {Path.GetFileName(path)} in {elapsed.Elapsed.TotalSeconds:F2}s.");
        return frames > 0 ? 0 : throw new InvalidDataException("Media Foundation produced no video frames");
    }
    finally
    {
        Mf.Release(ref outputType); Mf.Release(ref reader); Mf.MFShutdown();
    }
}

static unsafe int TransformFileTest(string path, int maxInputFrames)
{
    Mf.Check(Mf.MFStartup(0x20070, 0));
    IntPtr reader = IntPtr.Zero, inputType = IntPtr.Zero, outputType = IntPtr.Zero;
    IntPtr transform = IntPtr.Zero, factory = IntPtr.Zero;
    nint module = IntPtr.Zero;
    IntPtr outputBuffer = Marshal.AllocHGlobal(Marshal.SizeOf<MftOutputDataBuffer>());
    int inputs = 0, frames = 0;
    var hashes = new HashSet<uint>();
    var elapsed = Stopwatch.StartNew();
    try
    {
        Mf.Check(Mf.MFCreateSourceReaderFromURL(Path.GetFullPath(path), IntPtr.Zero, out reader));
        void** v = *(void***)reader;
        const uint FirstVideo = 0xfffffffc;
        var select = (delegate* unmanaged[Stdcall]<IntPtr, uint, int, int>)v[4];
        Mf.Check(select(reader, 0xfffffffe, 0));
        Mf.Check(select(reader, FirstVideo, 1));
        var nativeType = (delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr*, int>)v[5];
        Mf.Check(nativeType(reader, FirstVideo, 0, &inputType));
        var setType = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, int>)v[7];
        Mf.Check(setType(reader, FirstVideo, IntPtr.Zero, inputType));
        transform = CreateComTransform(out module, out factory);
        Mf.Check(NativeSetInputType(transform, inputType));
        Mf.Check(NativeGetOutputAvailableType(transform, 0, out outputType));
        Mf.Check(NativeSetOutputType(transform, outputType));
        var read = (delegate* unmanaged[Stdcall]<IntPtr, uint, uint, uint*, uint*, long*, IntPtr*, int>)v[9];
        for (;;)
        {
            uint actual = 0, flags = 0;
            long timestamp = 0;
            IntPtr sample = IntPtr.Zero;
            Mf.Check(read(reader, FirstVideo, 0, &actual, &flags, &timestamp, &sample));
            try
            {
                if (sample != IntPtr.Zero)
                {
                    int hr;
                    var blocked = Stopwatch.StartNew();
                    while ((hr = NativeProcessInput(transform, sample)) == Mf.MF_E_NOTACCEPTING)
                    {
                        PumpOutput();
                        if (blocked.Elapsed.TotalSeconds > 15) throw new TimeoutException("Decoder input remained blocked for 15 seconds");
                    }
                    Mf.Check(hr);
                    inputs++;
                    PumpOutput();
                }
            }
            finally { Mf.Release(ref sample); }
            if ((flags & 2) != 0 || inputs >= maxInputFrames) break;
        }
        Mf.Check(NativeProcessMessage(transform, 1));
        PumpOutput();
        Console.WriteLine($"Own COM MFT {(Environment.Is64BitProcess ? "x64" : "x86")}: {inputs} input samples, {frames} frames, {hashes.Count} different sampled pixel hashes in {elapsed.Elapsed.TotalSeconds:F2}s.");
        if (frames < inputs - 2 || hashes.Count < 2) throw new InvalidDataException("Decoder did not produce the expected changing video frames");
        return 0;

        void PumpOutput()
        {
            for (;;)
            {
                Marshal.StructureToPtr(new MftOutputDataBuffer(), outputBuffer, false);
                int hr = NativeProcessOutput(transform, outputBuffer);
                if (hr == Mf.MF_E_TRANSFORM_NEED_MORE_INPUT) return;
                Mf.Check(hr);
                var output = Marshal.PtrToStructure<MftOutputDataBuffer>(outputBuffer);
                IntPtr buffer = IntPtr.Zero;
                try
                {
                    if (output.Sample == IntPtr.Zero) throw new InvalidDataException("Missing output frame");
                    Mf.Check(Com.ContiguousBuffer(output.Sample, out buffer));
                    Mf.Check(Com.LockBuffer(buffer, out IntPtr pixels, out _, out uint size));
                    try
                    {
                        uint hash = 2166136261;
                        uint step = Math.Max(1, size / 4096);
                        for (uint i = 0; i < size; i += step) hash = unchecked((hash ^ Marshal.ReadByte(pixels, (int)i)) * 16777619);
                        hashes.Add(hash);
                    }
                    finally { Com.UnlockBuffer(buffer); }
                    frames++;
                }
                finally { Mf.Release(ref buffer); Mf.Release(ref output.Sample); Mf.Release(ref output.Events); }
            }
        }
    }
    finally
    {
        Marshal.FreeHGlobal(outputBuffer);
        Mf.Release(ref outputType); Mf.Release(ref inputType); Mf.Release(ref reader);
        Mf.Release(ref transform); Mf.Release(ref factory);
        if (module != IntPtr.Zero) NativeLibrary.Free(module);
        Mf.MFShutdown();
    }
}

static int SelfTest(string path, int expected, bool p010, bool viaCom, int width = 64, int height = 64)
{
    byte[] bitstream = File.ReadAllBytes(path);
    if (bitstream.Length == 0) throw new InvalidDataException("Empty HEVC bitstream");
    Mf.Check(Mf.MFStartup(0x20070, 0));
    IntPtr inputType = IntPtr.Zero, outputType = IntPtr.Zero, sample = IntPtr.Zero, buffer = IntPtr.Zero;
    using var transform = viaCom ? null : new HevcTransform();
    nint module = IntPtr.Zero;
    IntPtr factory = IntPtr.Zero, nativeTransform = IntPtr.Zero;
    try
    {
        if (viaCom) nativeTransform = CreateComTransform(out module, out factory);
        Mf.Check(Mf.MFCreateMediaType(out inputType));
        Mf.Check(Com.SetGuid(inputType, Mf.MajorType, Mf.Video));
        Mf.Check(Com.SetGuid(inputType, Mf.Subtype, Mf.HevcEs));
        Mf.Check(Com.SetUInt64(inputType, Mf.FrameSize, ((ulong)(uint)width << 32) | (uint)height));
        Mf.Check(viaCom ? NativeSetInputType(nativeTransform, inputType) : transform!.SetInputType(0, inputType, 0));
        Mf.Check(viaCom ? NativeGetOutputAvailableType(nativeTransform, p010 ? 1u : 0u, out outputType) :
            transform!.GetOutputAvailableType(0, p010 ? 1u : 0u, out outputType));
        Mf.Check(viaCom ? NativeSetOutputType(nativeTransform, outputType) : transform!.SetOutputType(0, outputType, 0));
        Mf.Check(Mf.MFCreateSample(out sample));
        Mf.Check(Mf.MFCreateMemoryBuffer((uint)bitstream.Length, out buffer));
        Mf.Check(Com.LockBuffer(buffer, out IntPtr data, out _, out _));
        try
        {
            Marshal.Copy(bitstream, 0, data, bitstream.Length);
            Mf.Check(Com.SetBufferLength(buffer, (uint)bitstream.Length));
        }
        finally { Com.UnlockBuffer(buffer); }
        Mf.Check(Com.AddBuffer(sample, buffer));
        Mf.Check(Com.SetSampleTime(sample, 0));
        Mf.Check(viaCom ? NativeProcessInput(nativeTransform, sample) : transform!.ProcessInput(0, sample, 0));
        Mf.Check(viaCom ? NativeProcessMessage(nativeTransform, 1) : transform!.ProcessMessage(1, UIntPtr.Zero));
        int count = 0;
        IntPtr outputPointer = Marshal.AllocHGlobal(Marshal.SizeOf<MftOutputDataBuffer>());
        try
        {
            while (true)
            {
                Marshal.StructureToPtr(new MftOutputDataBuffer(), outputPointer, false);
                int hr = viaCom ? NativeProcessOutput(nativeTransform, outputPointer) :
                    transform!.ProcessOutput(0, 1, outputPointer, out _);
                if (hr == Mf.MF_E_TRANSFORM_NEED_MORE_INPUT) break;
                Mf.Check(hr);
                MftOutputDataBuffer output = Marshal.PtrToStructure<MftOutputDataBuffer>(outputPointer);
                if (output.Sample == IntPtr.Zero) throw new InvalidDataException("Decoder returned no output sample");
                count++;
                Marshal.Release(output.Sample);
            }
        }
        finally { Marshal.FreeHGlobal(outputPointer); }
        Console.WriteLine($"Decoded {count} frames to {(p010 ? "P010" : "NV12")} from {Path.GetFileName(path)}{(viaCom ? " through COM" : "") }.");
        if (count < expected) throw new InvalidDataException($"Expected at least {expected} decoded frames");
        return 0;
    }
    finally
    {
        Mf.Release(ref buffer); Mf.Release(ref sample); Mf.Release(ref outputType); Mf.Release(ref inputType);
        Mf.Release(ref nativeTransform); Mf.Release(ref factory);
        if (module != IntPtr.Zero) NativeLibrary.Free(module);
        Mf.MFShutdown();
    }
}

static unsafe int ComTest()
{
    nint module = IntPtr.Zero;
    IntPtr factory = IntPtr.Zero, transform = IntPtr.Zero;
    try
    {
        transform = CreateComTransform(out module, out factory);
        void** transformVtable = *(void***)transform;
        var getStreamCount = (delegate* unmanaged[Stdcall]<IntPtr, uint*, uint*, int>)transformVtable[4];
        uint input, output;
        Mf.Check(getStreamCount(transform, &input, &output));
        if (input != 1 || output != 1) throw new InvalidDataException($"Unexpected COM stream counts {input}/{output}");
        Console.WriteLine("COM host exposed IMFTransform with one input and one output stream.");
        return 0;
    }
    finally
    {
        Mf.Release(ref transform); Mf.Release(ref factory);
        if (module != IntPtr.Zero) NativeLibrary.Free(module);
    }
}

try
{
    if (args.Length == 1 && args[0].Equals("cleanupold", StringComparison.OrdinalIgnoreCase))
    {
        MovAssociation.Restore(); MovAssociation.UnregisterLauncher();
        Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\UniversalVideoCodecForWindows", throwOnMissingSubKey: false);
        Console.WriteLine("Previous launcher registration removed; Windows' selected player was retained.");
        return 0;
    }
    if (args.Length == 1 && args[0].Equals("register", StringComparison.OrdinalIgnoreCase)) return Register(false);
    if (args.Length == 1 && args[0].Equals("unregister", StringComparison.OrdinalIgnoreCase)) return Register(true);
    if (args.Length == 1 && args[0].Equals("comtest", StringComparison.OrdinalIgnoreCase)) return ComTest();
    if (args.Length == 1 && args[0].Equals("catalogtest", StringComparison.OrdinalIgnoreCase)) { VerifyCatalog(); Console.WriteLine("Media Foundation catalog entry is present."); return 0; }
    if (args.Length == 1 && args[0].Equals("enumtest", StringComparison.OrdinalIgnoreCase)) return EnumerationTest();
    if (args.Length >= 2 && args[0].Equals("playbacktest", StringComparison.OrdinalIgnoreCase)) return PlaybackTest(args[1], args.Length > 2 ? int.Parse(args[2]) : int.MaxValue);
    if (args.Length >= 2 && args[0].Equals("transformfiletest", StringComparison.OrdinalIgnoreCase)) return TransformFileTest(args[1], args.Length > 2 ? int.Parse(args[2]) : 360);
    if (args.Length == 2 && args[0].Equals("directshowtest", StringComparison.OrdinalIgnoreCase)) return DirectShowTest(args[1]);
    if (args.Length >= 2 && args[0].Equals("comselftest", StringComparison.OrdinalIgnoreCase))
        return SelfTest(args[1], args.Length >= 3 ? int.Parse(args[2]) : 1,
            args.Length >= 4 && args[3].Equals("p010", StringComparison.OrdinalIgnoreCase), true,
            args.Length >= 5 ? int.Parse(args[4]) : 64, args.Length >= 6 ? int.Parse(args[5]) : 64);
    if (args.Length >= 2 && args[0].Equals("selftest", StringComparison.OrdinalIgnoreCase))
        return SelfTest(args[1], args.Length >= 3 ? int.Parse(args[2]) : 1,
            args.Length >= 4 && args[3].Equals("p010", StringComparison.OrdinalIgnoreCase), false);
    Console.Error.WriteLine("Usage: HevcCodec.Cli register | unregister | cleanupold | comtest | catalogtest | enumtest | playbacktest <media file> [frames] | transformfiletest <media file> [frames] | directshowtest <media file> | selftest | comselftest <Annex-B .h265 file> [minimum frames] [nv12|p010] [width height]");
    return 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}
