using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace InsoleApi.Utils;

internal sealed class RhinoProcessJob : IDisposable
{
    private const int BasicProcessIdList = 3;
    private const int ExtendedLimitInformation = 9;
    private const uint KillOnJobClose = 0x2000;
    private const int MoreData = 234;
    private readonly SafeFileHandle _handle;

    public RhinoProcessJob()
    {
        _handle = CreateJobObject(IntPtr.Zero, IntPtr.Zero);
        if (_handle.IsInvalid)
            throw new Win32Exception();

        var limits = new ExtendedLimits();
        limits.BasicLimits.Flags = KillOnJobClose;
        if (!SetInformationJobObject(_handle, ExtendedLimitInformation, in limits, Marshal.SizeOf<ExtendedLimits>()))
        {
            var error = new Win32Exception();
            _handle.Dispose();
            throw error;
        }
    }

    public void Add(Process process)
    {
        if (!AssignProcessToJobObject(_handle, process.SafeHandle))
            throw new Win32Exception();
    }

    public Process? FindGeometry()
    {
        foreach (var pid in GetProcessIds())
        {
            Process? process = null;
            try
            {
                process = Process.GetProcessById(pid);
                if (!process.HasExited && process.ProcessName.Equals("compute.geometry", StringComparison.OrdinalIgnoreCase))
                    return process;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                // The process exited before it could be opened.
            }

            process?.Dispose();
        }

        return null;
    }

    private int[] GetProcessIds()
    {
        const int headerSize = 2 * sizeof(int);
        var capacity = 8;
        while (true)
        {
            var size = headerSize + capacity * IntPtr.Size;
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (QueryInformationJobObject(_handle, BasicProcessIdList, buffer, size, IntPtr.Zero))
                {
                    var count = Marshal.ReadInt32(buffer, sizeof(int));
                    var ids = new int[count];
                    for (var i = 0; i < count; i++)
                        ids[i] = Marshal.ReadIntPtr(buffer, headerSize + i * IntPtr.Size).ToInt32();
                    return ids;
                }

                var error = Marshal.GetLastPInvokeError();
                if (error != MoreData)
                    throw new Win32Exception(error);

                capacity = Math.Max(capacity * 2, Marshal.ReadInt32(buffer));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    public void Dispose() => _handle.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint Flags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits BasicLimits;
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(IntPtr attributes, IntPtr name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass, in ExtendedLimits info, int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeProcessHandle process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(SafeFileHandle job, int informationClass, IntPtr info, int length, IntPtr returnLength);
}
