using System.Runtime.InteropServices;

namespace HevcCodec;

internal static class Mf
{
    internal const int S_OK = 0;
    internal const int E_NOTIMPL = unchecked((int)0x80004001);
    internal const int E_POINTER = unchecked((int)0x80004003);
    internal const int E_FAIL = unchecked((int)0x80004005);
    internal const int E_INVALIDARG = unchecked((int)0x80070057);
    internal const int MF_E_NO_MORE_TYPES = unchecked((int)0xC00D36B9);
    internal const int MF_E_NOTACCEPTING = unchecked((int)0xC00D36B5);
    internal const int MF_E_TRANSFORM_TYPE_NOT_SET = unchecked((int)0xC00D6D60);
    internal const int MF_E_TRANSFORM_NEED_MORE_INPUT = unchecked((int)0xC00D6D72);
    internal const int MF_E_TRANSFORM_STREAM_CHANGE = unchecked((int)0xC00D6D61);

    internal static readonly Guid MajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    internal static readonly Guid Subtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    internal static readonly Guid FrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
    internal static readonly Guid MpegSequenceHeader = new("3c036de7-3ad0-4c9e-9216-ee6d6ac21cb3");
    internal static readonly Guid Compressed = new("3afd0cee-18f2-4ba5-a110-8bea502e1f92");
    internal static readonly Guid FixedSizeSamples = new("b8ebefaf-b718-4e04-b0a9-116775e3321b");
    internal static readonly Guid AllSamplesIndependent = new("c9173739-5e56-461c-b713-46fb995cb95f");
    internal static readonly Guid SampleSize = new("dad3ab78-1990-408b-bce2-eba673dacc10");
    internal static readonly Guid DefaultStride = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    internal static readonly Guid VideoProfile = new("ad76a80b-2d5c-4e0b-b375-64e520137036");
    internal static readonly Guid Video = new("73646976-0000-0010-8000-00aa00389b71");
    internal static readonly Guid Hevc = new("43564548-0000-0010-8000-00aa00389b71");
    internal static readonly Guid HevcEs = new("53564548-0000-0010-8000-00aa00389b71");
    internal static readonly Guid Nv12 = new("3231564e-0000-0010-8000-00aa00389b71");
    internal static readonly Guid P010 = new("30313050-0000-0010-8000-00aa00389b71");
    internal static readonly Guid DecoderCategory = new("d6c02d4b-6833-45b4-971a-05a4b04bab91");
    internal static readonly Guid Clsid = new("069338a8-d42a-4b08-a6de-e00669f561d5");
    internal static readonly Guid TransformClsidAttribute = new("6821c42b-65a4-4e82-99bc-9a88205ecd0c");

    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFCreateMediaType(out IntPtr type);
    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFCreateSample(out IntPtr sample);
    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFCreateMemoryBuffer(uint size, out IntPtr buffer);
    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFStartup(uint version, uint flags);
    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFShutdown();
    [DllImport("mfreadwrite.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    internal static extern int MFCreateSourceReaderFromURL(string url, IntPtr attributes, out IntPtr reader);
    [DllImport("mfplat.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    internal static extern int MFTRegister(Guid clsid, Guid category, string name,
        uint flags, uint inputCount, [In] MftRegisterType[] inputTypes,
        uint outputCount, [In] MftRegisterType[] outputTypes, IntPtr attributes);
    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFTUnregister(Guid clsid);
    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFTEnumEx(Guid category, uint flags, ref MftRegisterType inputType,
        IntPtr outputType, out IntPtr activations, out uint count);
    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFTGetInfo(Guid clsid, out IntPtr name,
        out IntPtr inputTypes, out uint inputCount, out IntPtr outputTypes,
        out uint outputCount, out IntPtr attributes);

    internal static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
    internal static void Release(ref IntPtr ptr) { if (ptr != IntPtr.Zero) { Marshal.Release(ptr); ptr = IntPtr.Zero; } }
}

[StructLayout(LayoutKind.Sequential)]
public struct MftInputStreamInfo
{
    public long MaxLatency;
    public uint Flags;
    public uint Size;
    public uint MaxLookahead;
    public uint Alignment;
}

[StructLayout(LayoutKind.Sequential)]
public struct MftOutputStreamInfo
{
    public uint Flags;
    public uint Size;
    public uint Alignment;
}

[StructLayout(LayoutKind.Sequential)]
public struct MftOutputDataBuffer
{
    public uint StreamId;
    public IntPtr Sample;
    public uint Status;
    public IntPtr Events;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MftRegisterType
{
    public Guid MajorType;
    public Guid Subtype;
    public MftRegisterType(Guid subtype) { MajorType = Mf.Video; Subtype = subtype; }
}

[ComVisible(true), Guid("bf94c121-5b05-4e6f-8000-ba598961414d")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMfTransform
{
    [PreserveSig] int GetStreamLimits(out uint inputMin, out uint inputMax, out uint outputMin, out uint outputMax);
    [PreserveSig] int GetStreamCount(out uint inputCount, out uint outputCount);
    [PreserveSig] int GetStreamIDs(uint inputIdSize, IntPtr inputIds, uint outputIdSize, IntPtr outputIds);
    [PreserveSig] int GetInputStreamInfo(uint streamId, out MftInputStreamInfo info);
    [PreserveSig] int GetOutputStreamInfo(uint streamId, out MftOutputStreamInfo info);
    [PreserveSig] int GetAttributes(out IntPtr attributes);
    [PreserveSig] int GetInputStreamAttributes(uint streamId, out IntPtr attributes);
    [PreserveSig] int GetOutputStreamAttributes(uint streamId, out IntPtr attributes);
    [PreserveSig] int DeleteInputStream(uint streamId);
    [PreserveSig] int AddInputStreams(uint count, IntPtr streamIds);
    [PreserveSig] int GetInputAvailableType(uint streamId, uint typeIndex, out IntPtr type);
    [PreserveSig] int GetOutputAvailableType(uint streamId, uint typeIndex, out IntPtr type);
    [PreserveSig] int SetInputType(uint streamId, IntPtr type, uint flags);
    [PreserveSig] int SetOutputType(uint streamId, IntPtr type, uint flags);
    [PreserveSig] int GetInputCurrentType(uint streamId, out IntPtr type);
    [PreserveSig] int GetOutputCurrentType(uint streamId, out IntPtr type);
    [PreserveSig] int GetInputStatus(uint streamId, out uint flags);
    [PreserveSig] int GetOutputStatus(out uint flags);
    [PreserveSig] int SetOutputBounds(long lowerBound, long upperBound);
    [PreserveSig] int ProcessEvent(uint inputStreamId, IntPtr mediaEvent);
    [PreserveSig] int ProcessMessage(uint message, UIntPtr param);
    [PreserveSig] int ProcessInput(uint inputStreamId, IntPtr sample, uint flags);
    [PreserveSig] int ProcessOutput(uint flags, uint outputBufferCount, IntPtr outputBuffers, out uint status);
}

internal static unsafe class Com
{
    private static void** Vtable(IntPtr p) => *(void***)p;
    internal static int AddRef(IntPtr p) => ((delegate* unmanaged[Stdcall]<IntPtr, int>)Vtable(p)[1])(p);
    internal static int GetGuid(IntPtr p, Guid key, out Guid value)
    { fixed (Guid* v = &value) return ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, Guid*, int>)Vtable(p)[10])(p, &key, v); }
    internal static int GetUInt64(IntPtr p, Guid key, out ulong value)
    { fixed (ulong* v = &value) return ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, ulong*, int>)Vtable(p)[8])(p, &key, v); }
    internal static int GetUInt32(IntPtr p, Guid key, out uint value)
    { fixed (uint* v = &value) return ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, uint*, int>)Vtable(p)[7])(p, &key, v); }
    internal static int SetGuid(IntPtr p, Guid key, Guid value)
    { return ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, Guid*, int>)Vtable(p)[24])(p, &key, &value); }
    internal static int SetUInt64(IntPtr p, Guid key, ulong value)
    { return ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, ulong, int>)Vtable(p)[22])(p, &key, value); }
    internal static int SetUInt32(IntPtr p, Guid key, uint value)
    { return ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, uint, int>)Vtable(p)[21])(p, &key, value); }
    internal static int GetBlobSize(IntPtr p, Guid key, out uint size)
    { fixed (uint* s = &size) return ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, uint*, int>)Vtable(p)[14])(p, &key, s); }
    internal static int GetBlob(IntPtr p, Guid key, byte[] data, out uint size)
    { fixed (byte* d = data) fixed (uint* s = &size) return ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, byte*, uint, uint*, int>)Vtable(p)[15])(p, &key, d, (uint)data.Length, s); }
    internal static int DeleteItem(IntPtr p, Guid key)
    { return ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, int>)Vtable(p)[19])(p, &key); }
    internal static int CopyAllItems(IntPtr source, IntPtr destination) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Vtable(source)[32])(source, destination);
    internal static int SampleTime(IntPtr p, out long value)
    { fixed (long* v = &value) return ((delegate* unmanaged[Stdcall]<IntPtr, long*, int>)Vtable(p)[35])(p, v); }
    internal static int SampleDuration(IntPtr p, out long value)
    { fixed (long* v = &value) return ((delegate* unmanaged[Stdcall]<IntPtr, long*, int>)Vtable(p)[37])(p, v); }
    internal static int SetSampleTime(IntPtr p, long value) => ((delegate* unmanaged[Stdcall]<IntPtr, long, int>)Vtable(p)[36])(p, value);
    internal static int SetSampleDuration(IntPtr p, long value) => ((delegate* unmanaged[Stdcall]<IntPtr, long, int>)Vtable(p)[38])(p, value);
    internal static int ContiguousBuffer(IntPtr p, out IntPtr buffer)
    { fixed (IntPtr* b = &buffer) return ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Vtable(p)[41])(p, b); }
    internal static int AddBuffer(IntPtr p, IntPtr buffer) => ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Vtable(p)[42])(p, buffer);
    internal static int LockBuffer(IntPtr p, out IntPtr data, out uint max, out uint length)
    { fixed (IntPtr* d = &data) fixed (uint* m = &max) fixed (uint* l = &length) return ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, uint*, uint*, int>)Vtable(p)[3])(p, d, m, l); }
    internal static int UnlockBuffer(IntPtr p) => ((delegate* unmanaged[Stdcall]<IntPtr, int>)Vtable(p)[4])(p);
    internal static int SetBufferLength(IntPtr p, uint length) => ((delegate* unmanaged[Stdcall]<IntPtr, uint, int>)Vtable(p)[6])(p, length);
}

internal static class De265
{
    static De265()
    {
        NativeLibrary.SetDllImportResolver(typeof(De265).Assembly, (name, assembly, searchPath) =>
            name.Equals("libde265.dll", StringComparison.OrdinalIgnoreCase)
                ? NativeLibrary.Load(Path.Combine(Path.GetDirectoryName(assembly.Location)!, "libde265.dll"))
                : IntPtr.Zero);
    }
    [DllImport("libde265.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr de265_new_decoder();
    [DllImport("libde265.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern int de265_start_worker_threads(IntPtr decoder, int numberOfThreads);
    [DllImport("libde265.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern int de265_free_decoder(IntPtr decoder);
    [DllImport("libde265.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern int de265_push_data(IntPtr decoder, IntPtr data, int length, long pts, IntPtr userData);
    [DllImport("libde265.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern void de265_push_end_of_frame(IntPtr decoder);
    [DllImport("libde265.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern int de265_flush_data(IntPtr decoder);
    [DllImport("libde265.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern int de265_decode(IntPtr decoder, out int more);
    [DllImport("libde265.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern int de265_isOK(int error);
    [DllImport("libde265.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern void de265_reset(IntPtr decoder);
    [DllImport("libde265.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr de265_peek_next_picture(IntPtr decoder);
    [DllImport("libde265.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern void de265_release_next_picture(IntPtr decoder);
    [DllImport("libde265.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern int de265_get_image_width(IntPtr image, int channel);
    [DllImport("libde265.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern int de265_get_image_height(IntPtr image, int channel);
    [DllImport("libde265.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern int de265_get_bits_per_pixel(IntPtr image, int channel);
    [DllImport("libde265.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr de265_get_image_plane(IntPtr image, int channel, out int stride);
    [DllImport("libde265.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern long de265_get_image_PTS(IntPtr image);
}
