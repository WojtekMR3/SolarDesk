using System.Runtime.InteropServices;

namespace SolarDesk;

/// <summary>Per-display Digital Vibrance, serialized by MonitorService's worker gate.</summary>
internal sealed class NvidiaVibrance : IDisposable
{
    // DVC is a private NVAPI extension. Resolve it dynamically and require a valid read
    // before exposing the control. ABI references are linked in docs/user-guide.md.
    [StructLayout(LayoutKind.Sequential)]
    internal struct Info
    {
        public uint Version;
        public int Current, Minimum, Maximum, Default;
        public static Info Create() => new() { Version = (uint)Marshal.SizeOf<Info>() | (1u << 16) };
        public bool IsValid => Minimum < Default && Default < Maximum && Current >= Minimum && Current <= Maximum;
        public int Percent => VibranceScale.ToPercent(Current, Minimum, Default, Maximum);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Query(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Lifecycle();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate int FindDisplay([MarshalAs(UnmanagedType.LPStr)] string name, out IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Dvc(IntPtr display, uint outputId, ref Info info);

    private IntPtr library;
    private FindDisplay? findDisplay;
    private Dvc? getInfo, setInfo;
    private Lifecycle? unload;
    private bool attempted;

    private bool EnsureLoaded()
    {
        if (attempted) return getInfo is not null;
        attempted = true;
        try
        {
            // Load the installed driver only, never a DLL from the app/working directory.
            string path = Path.Combine(Environment.SystemDirectory, Environment.Is64BitProcess ? "nvapi64.dll" : "nvapi.dll");
            if (!NativeLibrary.TryLoad(path, out library)) return false;
            var query = Marshal.GetDelegateForFunctionPointer<Query>(NativeLibrary.GetExport(library, "nvapi_QueryInterface"));
            T? Resolve<T>(uint id) where T : Delegate
            {
                IntPtr pointer = query(id);
                return pointer == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(pointer);
            }
            var initialize = Resolve<Lifecycle>(0x0150E828);
            var release = Resolve<Lifecycle>(0xD22BDD7E);
            var find = Resolve<FindDisplay>(0x35C29134);
            var read = Resolve<Dvc>(0x0E45002D);
            var write = Resolve<Dvc>(0x4A82C2B1);
            if (initialize is null || release is null || find is null || read is null || write is null || initialize() != 0)
            { Dispose(); return false; }
            unload = release; findDisplay = find; getInfo = read; setInfo = write;
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        { Dispose(); return false; }
    }

    public bool TryRead(string display, out Info info)
    {
        info = Info.Create();
        return EnsureLoaded() && findDisplay!(display, out var handle) == 0 && handle != IntPtr.Zero
            && getInfo!(handle, 0, ref info) == 0 && info.IsValid;
    }

    public string? Set(string display, int value, bool raw = false)
    {
        if (!EnsureLoaded() || findDisplay!(display, out var handle) != 0 || handle == IntPtr.Zero)
            return "Saturation unavailable; refresh displays.";
        var info = Info.Create();
        int result = getInfo!(handle, 0, ref info);
        if (result != 0 || !info.IsValid) return $"Could not read saturation (NVIDIA {result}).";
        int target = raw ? Math.Clamp(value, info.Minimum, info.Maximum)
            : VibranceScale.ToRaw(value, info.Minimum, info.Default, info.Maximum);
        if (info.Current == target) return null;
        info.Current = target;
        result = setInfo!(handle, 0, ref info);
        if (result != 0) return $"Saturation write failed (NVIDIA {result}).";
        var after = Info.Create();
        result = getInfo(handle, 0, ref after);
        return result == 0 && after.Current == target ? null : "NVIDIA did not accept saturation; check the display's color/HDR mode.";
    }

    public void Dispose()
    {
        unload?.Invoke(); unload = null;
        findDisplay = null; getInfo = null; setInfo = null;
        if (library != IntPtr.Zero) { NativeLibrary.Free(library); library = IntPtr.Zero; }
    }
}
