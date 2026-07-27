using System.ComponentModel;
using System.Runtime.InteropServices;
using AcceptanceSpecSystem.Application.Services;
using Microsoft.Win32.SafeHandles;

namespace AcceptanceSpecSystem.Api.Services;

public interface ISafeFileDeletionRaceHook
{
    void AfterTargetOpened(string relativePath);

    void AfterTargetIsolated(string relativePath)
    {
    }
}

/// <summary>
/// 使用已固定的句柄/目录句柄删除文件，避免校验后路径被替换造成越界删除。
/// </summary>
public sealed class SafeUploadedFileDeleter
{
    private readonly string _basePath;
    private readonly ISafeFileDeletionRaceHook? _raceHook;

    public SafeUploadedFileDeleter(string basePath, ISafeFileDeletionRaceHook? raceHook = null)
    {
        _basePath = Path.GetFullPath(basePath);
        _raceHook = raceHook;
    }

    public void DeleteIfExists(string relativePath)
    {
        if (OperatingSystem.IsWindows())
        {
            DeleteWindows(relativePath);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            DeleteLinux(relativePath);
            return;
        }

        throw new PlatformNotSupportedException("当前平台不支持安全的持久上传文件删除");
    }

    private void DeleteWindows(string relativePath)
    {
        using var rootHandle = OpenWindowsHandle(_basePath, directory: true, deleteAccess: false);
        if (rootHandle.IsInvalid)
            ThrowWindowsError(Marshal.GetLastPInvokeError(), missingIsSuccess: false);

        var fullPath = Path.GetFullPath(Path.Combine(
            _basePath,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        using var targetHandle = OpenWindowsHandle(fullPath, directory: false, deleteAccess: true);
        if (targetHandle.IsInvalid)
        {
            ThrowWindowsError(Marshal.GetLastPInvokeError(), missingIsSuccess: true);
            return;
        }

        var rootFinalPath = NormalizeWindowsFinalPath(GetWindowsFinalPath(rootHandle));
        var targetFinalPath = NormalizeWindowsFinalPath(GetWindowsFinalPath(targetHandle));
        var expectedFinalPath = Path.GetFullPath(Path.Combine(
            rootFinalPath,
            relativePath.Replace('/', '\\')));
        if (!string.Equals(targetFinalPath, expectedFinalPath, StringComparison.OrdinalIgnoreCase))
            throw new UnsafeWordFilePathException();

        if (!GetFileInformationByHandle(targetHandle, out var information))
            ThrowWindowsError(Marshal.GetLastPInvokeError(), missingIsSuccess: false);
        if ((information.FileAttributes & FileAttributeDirectory) != 0)
            throw new UnsafeWordFilePathException();

        _raceHook?.AfterTargetOpened(relativePath);

        var disposition = new FileDispositionInfo { DeleteFile = true };
        if (!SetFileInformationByHandle(
                targetHandle,
                FileInfoByHandleClass.FileDispositionInfo,
                ref disposition,
                Marshal.SizeOf<FileDispositionInfo>()))
        {
            ThrowWindowsError(Marshal.GetLastPInvokeError(), missingIsSuccess: false);
        }
    }

    private void DeleteLinux(string relativePath)
    {
        try
        {
            var parts = relativePath.Split('/');
            using var root = OpenLinuxDirectory(AtCurrentWorkingDirectory, _basePath);
            using var uploads = OpenLinuxDirectory(root.Value, parts[0]);
            using var fileNamespace = OpenLinuxDirectory(uploads.Value, parts[1]);
            using var dateDirectory = OpenLinuxDirectory(fileNamespace.Value, parts[2]);

            var targetFd = openat(
                dateDirectory.Value,
                parts[3],
                OpenPath | OpenNoFollow | OpenCloseOnExec);
            if (targetFd < 0)
                ThrowLinuxError(Marshal.GetLastPInvokeError());

            using var target = new SafeLinuxFd(targetFd);
            EnsureLinuxRegularFile(target.Value);
            _raceHook?.AfterTargetOpened(relativePath);

            var isolationName = MoveLinuxEntryToIsolation(dateDirectory.Value, parts[3]);
            if (isolationName == null)
                return;
            var isolationNeedsRestore = true;
            try
            {
                _raceHook?.AfterTargetIsolated(relativePath);
                var isolatedFd = openat(
                    dateDirectory.Value,
                    isolationName,
                    OpenPath | OpenNoFollow | OpenCloseOnExec);
                if (isolatedFd < 0)
                    ThrowLinuxError(Marshal.GetLastPInvokeError());

                using var isolated = new SafeLinuxFd(isolatedFd);
                EnsureLinuxRegularFile(isolated.Value);
                if (GetLinuxFileIdentity(target.Value) != GetLinuxFileIdentity(isolated.Value))
                    throw new UnsafeWordFilePathException();

                if (unlinkat(dateDirectory.Value, isolationName, 0) != 0)
                    ThrowLinuxError(Marshal.GetLastPInvokeError());
                isolationNeedsRestore = false;
            }
            finally
            {
                if (isolationNeedsRestore)
                    RestoreLinuxEntry(dateDirectory.Value, isolationName, parts[3]);
            }
        }
        catch (FileNotFoundException)
        {
            // 任一级目录或目标文件已不存在，幂等删除视为成功。
        }
    }

    private static string? MoveLinuxEntryToIsolation(int directoryFd, string originalName)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var isolationName = OrphanFilePathRules.CreateDeletionQuarantineFileName();
            if (renameat2(
                    directoryFd,
                    originalName,
                    directoryFd,
                    isolationName,
                    RenameNoReplace) == 0)
                return isolationName;

            var error = Marshal.GetLastPInvokeError();
            if (error == ErrorAlreadyExists)
                continue;
            if (error == ErrorNoEntry)
                return null;
            ThrowLinuxError(error);
        }

        throw new IOException("无法创建持久文件删除隔离项");
    }

    private static void RestoreLinuxEntry(int directoryFd, string isolationName, string originalName)
    {
        if (renameat2(
                directoryFd,
                isolationName,
                directoryFd,
                originalName,
                RenameNoReplace) == 0)
            return;

        var error = Marshal.GetLastPInvokeError();
        if (error == ErrorNoEntry)
            throw new IOException("持久文件删除隔离项在恢复前已不存在");
        if (error == ErrorAlreadyExists)
            throw new IOException("持久文件删除原名称已被占用，隔离项已保留等待巡检");
        if (error is ErrorPermissionDenied or ErrorOperationNotPermitted)
            throw new UnauthorizedAccessException("持久文件删除隔离项恢复被拒绝");
        throw new IOException("持久文件删除隔离项恢复失败", new Win32Exception(error));
    }

    private static LinuxFileIdentity GetLinuxFileIdentity(int fd)
    {
        if (fstat(fd, out var stat) != 0)
            ThrowLinuxError(Marshal.GetLastPInvokeError());
        return new LinuxFileIdentity(stat.Device, stat.Inode);
    }

    private static void EnsureLinuxRegularFile(int fd)
    {
        if (fstat(fd, out var stat) != 0)
            ThrowLinuxError(Marshal.GetLastPInvokeError());
        var mode = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => stat.ModeX64,
            Architecture.Arm64 => stat.ModeArm64,
            _ => throw new PlatformNotSupportedException("当前 Linux 架构不支持安全的持久文件类型校验")
        };
        if ((mode & LinuxFileTypeMask) != LinuxRegularFile)
            throw new UnsafeWordFilePathException();
    }

    private static SafeLinuxFd OpenLinuxDirectory(int parentFd, string path)
    {
        var fd = parentFd == AtCurrentWorkingDirectory
            ? open(path, OpenReadOnly | OpenDirectory | OpenNoFollow | OpenCloseOnExec)
            : openat(parentFd, path, OpenReadOnly | OpenDirectory | OpenNoFollow | OpenCloseOnExec);
        if (fd < 0)
            ThrowLinuxError(Marshal.GetLastPInvokeError());
        return new SafeLinuxFd(fd);
    }

    private static SafeFileHandle OpenWindowsHandle(string path, bool directory, bool deleteAccess)
    {
        var flags = directory ? FileFlagBackupSemantics : 0u;
        return CreateFileW(
            path,
            (deleteAccess ? DeleteAccess : 0u) | FileReadAttributes,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            flags,
            IntPtr.Zero);
    }

    private static string GetWindowsFinalPath(SafeFileHandle handle)
    {
        var capacity = 512;
        while (true)
        {
            var buffer = new char[capacity];
            var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
            if (length == 0)
                ThrowWindowsError(Marshal.GetLastPInvokeError(), missingIsSuccess: false);
            if (length < buffer.Length)
                return new string(buffer, 0, (int)length);
            capacity = checked((int)length + 1);
        }
    }

    private static string NormalizeWindowsFinalPath(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            return @"\\" + path[8..];
        return path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase) ? path[4..] : path;
    }

    private static void ThrowWindowsError(int error, bool missingIsSuccess)
    {
        if (missingIsSuccess && error is ErrorFileNotFound or ErrorPathNotFound)
            return;
        if (error == ErrorAccessDenied)
            throw new UnauthorizedAccessException("持久文件句柄访问被拒绝");
        throw new IOException("持久文件句柄操作失败", new Win32Exception(error));
    }

    private static void ThrowLinuxError(int error)
    {
        if (error == ErrorNoEntry)
            throw new FileNotFoundException("持久文件已不存在");
        if (error is ErrorPermissionDenied or ErrorOperationNotPermitted)
            throw new UnauthorizedAccessException("持久文件目录句柄访问被拒绝");
        if (error == ErrorTooManySymbolicLinks)
            throw new UnsafeWordFilePathException();
        throw new IOException("持久文件目录句柄操作失败", new Win32Exception(error));
    }

    private sealed class SafeLinuxFd : IDisposable
    {
        private int _fd;

        public SafeLinuxFd(int fd) => _fd = fd;
        public int Value => _fd;

        public void Dispose()
        {
            var fd = Interlocked.Exchange(ref _fd, -1);
            if (fd >= 0)
                close(fd);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInfo
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool DeleteFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxStat
    {
        [FieldOffset(0)]
        public ulong Device;

        [FieldOffset(8)]
        public ulong Inode;

        // glibc/musl x86_64: st_nlink 位于 16，st_mode 位于 24。
        [FieldOffset(24)]
        public uint ModeX64;

        // glibc/musl aarch64: st_mode 位于 16。
        [FieldOffset(16)]
        public uint ModeArm64;
    }

    private readonly record struct LinuxFileIdentity(ulong Device, ulong Inode);

    private enum FileInfoByHandleClass
    {
        FileDispositionInfo = 4
    }

    private const uint DeleteAccess = 0x00010000;
    private const uint FileReadAttributes = 0x00000080;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileAttributeDirectory = 0x00000010;
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;
    private const int ErrorAccessDenied = 5;

    private const int AtCurrentWorkingDirectory = -100;
    private const int OpenReadOnly = 0;
    private const int OpenDirectory = 0x10000;
    private const int OpenNoFollow = 0x20000;
    private const int OpenCloseOnExec = 0x80000;
    private const int OpenPath = 0x200000;
    private const uint LinuxFileTypeMask = 0xF000;
    private const uint LinuxRegularFile = 0x8000;
    private const int ErrorOperationNotPermitted = 1;
    private const int ErrorNoEntry = 2;
    private const int ErrorPermissionDenied = 13;
    private const int ErrorAlreadyExists = 17;
    private const int ErrorTooManySymbolicLinks = 40;
    private const uint RenameNoReplace = 1;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle file,
        [Out] char[] filePath,
        uint filePathLength,
        uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle file,
        FileInfoByHandleClass fileInformationClass,
        ref FileDispositionInfo fileInformation,
        int bufferSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle file,
        out ByHandleFileInformation fileInformation);

    [DllImport("libc", SetLastError = true)]
    private static extern int open(string pathname, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int openat(int directoryFd, string pathname, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int unlinkat(int directoryFd, string pathname, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int renameat2(
        int oldDirectoryFd,
        string oldPath,
        int newDirectoryFd,
        string newPath,
        uint flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int fstat(int fd, out LinuxStat stat);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);
}
