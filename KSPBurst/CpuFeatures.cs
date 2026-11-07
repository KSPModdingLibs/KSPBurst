using System;
using System.Runtime.InteropServices;

namespace KSPBurst;

// The x86 instruction set levels that the burst targets need, from lowest to highest.
public enum CpuLevel
{
    Sse2,
    Sse4,
    Avx,
    Avx2
}

// Finds out which burst target levels the CPU of this machine supports. Neither Unity nor the Mono version in KSP
// has an API for this, so we run the cpuid instruction ourselves. To do that, we copy two small functions into
// executable memory and call them through delegates. This works the same way on every platform. Under an
// emulator, like Rosetta on Apple CPUs, cpuid reports the features that the emulator supports.
internal static class CpuFeatures
{
    // Returns the highest level that the CPU supports, or null if we can't tell.
    public static unsafe CpuLevel? Detect()
    {
#if KSP_RETOOLED
        // The code below assume x86-64
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
            return null;
#endif

        int size = CpuidLength + XgetbvCode.Length;
        IntPtr memory;
        try
        {
            memory = AllocateExecutable(size);
        }
        catch (Exception e)
        {
            KSPBurst.LogError($"Failed to allocate memory for cpuid: {e}");
            return null;
        }

        if (memory == IntPtr.Zero)
            return null;

        try
        {
            // The first argument arrives in a different register on Windows than on Linux and macOS. The prefix
            // moves it into r8, so the rest of the code is the same everywhere.
            byte[] prefix = PathUtil.SelectByPlatform(WindowsPrefix, UnixPrefix, UnixPrefix);
            Marshal.Copy(prefix, 0, memory, prefix.Length);
            Marshal.Copy(CpuidBody, 0, memory + prefix.Length, CpuidBody.Length);
            IntPtr xgetbvAddress = memory + CpuidLength;
            Marshal.Copy(XgetbvCode, 0, xgetbvAddress, XgetbvCode.Length);

            var cpuidFunction = Marshal.GetDelegateForFunctionPointer<CpuidFunction>(memory);
            var xgetbv = Marshal.GetDelegateForFunctionPointer<XgetbvFunction>(xgetbvAddress);

            int* registers = stackalloc int[4];
            void Cpuid(int leaf)
            {
                registers[0] = leaf;
                registers[2] = 0;
                cpuidFunction(registers);
            }

            Cpuid(0);
            int maxLeaf = registers[0];

            Cpuid(1);
            int ecx1 = registers[2];

            int ebx7 = 0;
            if (maxLeaf >= 7)
            {
                Cpuid(7);
                ebx7 = registers[1];
            }

            static bool Bit(int value, int bit) => (value & (1 << bit)) != 0;

            // The features that each level needs on top of the level below it. These follow the features that
            // burst enables for each target.
            bool sse4 = Bit(ecx1, 19) && Bit(ecx1, 20) && Bit(ecx1, 23); // SSE 4.1, SSE 4.2, POPCNT
            if (!sse4)
                return CpuLevel.Sse2;

            // The CPU can support AVX while the OS doesn't save the AVX registers. The OS sets the OSXSAVE bit
            // when it lets us read XCR0, and XCR0 has bit 1 (SSE) and bit 2 (AVX) set when it saves those
            // registers.
            bool osSavesAvx = Bit(ecx1, 27) && (xgetbv() & 0x6) == 0x6;
            if (!osSavesAvx || !Bit(ecx1, 28)) // AVX
                return CpuLevel.Sse4;

            bool avx2 = Bit(ebx7, 5) && Bit(ecx1, 12) && Bit(ecx1, 29) // AVX2, FMA, F16C
                        && Bit(ebx7, 3) && Bit(ebx7, 8); //               BMI1, BMI2
            return avx2 ? CpuLevel.Avx2 : CpuLevel.Avx;
        }
        finally
        {
            FreeExecutable(memory, size);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate void CpuidFunction(int* registers);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int XgetbvFunction();

    // void cpuid(int* registers). Before the call, registers[0] holds the leaf and registers[2] holds the
    // subleaf. After the call, the array holds eax, ebx, ecx and edx in that order. The function is a prefix
    // that depends on the calling convention, and then a body that is the same on all platforms.
    private static readonly byte[] WindowsPrefix = [0x49, 0x89, 0xC8]; // mov r8, rcx
    private static readonly byte[] UnixPrefix = [0x49, 0x89, 0xF8]; //    mov r8, rdi

    private static readonly byte[] CpuidBody =
    [
        0x53, //                   push rbx
        0x41, 0x8B, 0x00, //       mov eax, [r8]
        0x41, 0x8B, 0x48, 0x08, // mov ecx, [r8 + 8]
        0x0F, 0xA2, //             cpuid
        0x41, 0x89, 0x00, //       mov [r8], eax
        0x41, 0x89, 0x58, 0x04, // mov [r8 + 4], ebx
        0x41, 0x89, 0x48, 0x08, // mov [r8 + 8], ecx
        0x41, 0x89, 0x50, 0x0C, // mov [r8 + 12], edx
        0x5B, //                   pop rbx
        0xC3 //                    ret
    ];

    // Both prefixes are the same length, so this is the length of the whole function on every platform.
    private static readonly int CpuidLength = WindowsPrefix.Length + CpuidBody.Length;

    // int xgetbv(), which returns the low half of XCR0. XCR0 tells us which registers the OS saves. This
    // function takes no arguments, so it doesn't depend on the calling convention.
    private static readonly byte[] XgetbvCode =
    [
        0x31, 0xC9, //       xor ecx, ecx
        0x0F, 0x01, 0xD0, // xgetbv
        0xC3 //              ret
    ];

    [DllImport("kernel32", SetLastError = true)]
    private static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint allocationType, uint protect);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool VirtualFree(IntPtr address, UIntPtr size, uint freeType);

    private const uint MemCommit = 0x1000;
    private const uint MemReserve = 0x2000;
    private const uint MemRelease = 0x8000;
    private const uint PageExecuteReadWrite = 0x40;

    [DllImport("libc", SetLastError = true)]
    private static extern IntPtr mmap(IntPtr address, UIntPtr length, int protection, int flags, int fd,
        IntPtr offset);

    [DllImport("libc", SetLastError = true)]
    private static extern int munmap(IntPtr address, UIntPtr length);

    private const int ProtReadWriteExec = 0x7;
    private const int MapPrivate = 0x02;
    private const int LinuxMapAnonymous = 0x20;
    private const int MacMapAnonymous = 0x1000;

    // macOS refuses memory that is both writable and executable to a process with the hardened runtime, unless
    // the memory is mapped with MAP_JIT. Mono needs MAP_JIT for its own code too, so KSP is always allowed to use
    // it.
    private const int MacMapJit = 0x800;

    private static readonly IntPtr MapFailed = new(-1);

    // Returns memory that we can write code into and then run, or IntPtr.Zero if the OS refuses.
    private static IntPtr AllocateExecutable(int size)
    {
        if (PathUtil.SelectByPlatform(true, false, false))
        {
            IntPtr memory = VirtualAlloc(IntPtr.Zero, (UIntPtr) size, MemCommit | MemReserve,
                PageExecuteReadWrite);
            if (memory == IntPtr.Zero)
                KSPBurst.LogError($"VirtualAlloc failed with error {Marshal.GetLastWin32Error()}");
            return memory;
        }

        int flags = MapPrivate | PathUtil.SelectByPlatform(0, LinuxMapAnonymous, MacMapAnonymous | MacMapJit);
        IntPtr mapped = mmap(IntPtr.Zero, (UIntPtr) size, ProtReadWriteExec, flags, -1, IntPtr.Zero);
        if (mapped == MapFailed)
        {
            KSPBurst.LogError($"mmap failed with errno {Marshal.GetLastWin32Error()}");
            return IntPtr.Zero;
        }

        return mapped;
    }

    private static void FreeExecutable(IntPtr memory, int size)
    {
        if (PathUtil.SelectByPlatform(true, false, false))
            VirtualFree(memory, UIntPtr.Zero, MemRelease);
        else
            munmap(memory, (UIntPtr) size);
    }
}
