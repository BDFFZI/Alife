using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Alife.Foundation;

/// <summary>
/// 进程追踪器：利用 Windows Job Objects 确保子进程随父进程一同退出。
/// </summary>
public static class ProcessTracker
{
    /// <summary>
    /// 将指定进程加入追踪作业，使其生命周期与当前进程绑定。
    /// </summary>
    public static void Track(Process process)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) == false) return;
        if (JobHandle == IntPtr.Zero) return;

        AssignProcessToJobObject(JobHandle, process.Handle);
    }

    static readonly IntPtr JobHandle;

    static ProcessTracker()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) == false) return;

        JobHandle = CreateJobObject(IntPtr.Zero, null);

        JobObjectBasicLimitInformation info = new() {
            LimitFlags = JobObjectLimitKillOnJobClose
        };

        JobObjectExtendedLimitInformation extendedInfo = new() {
            BasicLimitInformation = info
        };

        int length = Marshal.SizeOf(typeof(JobObjectExtendedLimitInformation));
        IntPtr extendedInfoPtr = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(extendedInfo, extendedInfoPtr, false);
            if (SetInformationJobObject(JobHandle, 9, extendedInfoPtr, (uint)length) == false)
            {
                throw new Exception($"无法设置 Job Object 信息。错误代码: {Marshal.GetLastWin32Error()}");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(extendedInfoPtr);
        }
    }

    #region P/Invoke

    const int JobObjectLimitKillOnJobClose = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public long Affinitiy;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoCounters;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryLimit;
        public UIntPtr PeakJobMemoryLimit;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);
    [DllImport("kernel32.dll")]
    static extern bool SetInformationJobObject(IntPtr hJob, int jobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);
    [DllImport("kernel32.dll")]
    static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    #endregion
}
