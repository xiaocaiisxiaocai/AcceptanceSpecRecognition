using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace AcceptanceSpecSystem.Api.Services;

internal abstract class NativeTemporaryDirectory : IAsyncDisposable
{
    internal abstract Stream CreatePayloadWriteStream();
    internal abstract Stream OpenPayloadReadStream();
    internal abstract void Heartbeat(DateTimeOffset now);
    public abstract ValueTask DisposeAsync();
}

internal interface INativeTemporaryFileSystem : IDisposable
{
    NativeTemporaryDirectory CreateRequestDirectory(
        string requestId,
        string markerValue,
        DateTimeOffset createdAt,
        IFileCompareTemporaryStorageFaultHook? hook);

    int CleanupExpired(
        DateTimeOffset cutoff,
        IFileCompareTemporaryStorageFaultHook? hook,
        CancellationToken cancellationToken);
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsNativeTemporaryFileSystem : INativeTemporaryFileSystem
{
    private const string MarkerName = ".acceptance-file-compare";
    private const string PayloadName = "payload.tmp";

    private const uint Delete = 0x00010000;
    private const uint Synchronize = 0x00100000;
    private const uint FileReadData = 0x00000001;
    private const uint FileWriteData = 0x00000002;
    private const uint FileAppendData = 0x00000004;
    private const uint FileListDirectory = FileReadData;
    private const uint FileAddFile = FileWriteData;
    private const uint FileAddSubdirectory = FileAppendData;
    private const uint FileTraverse = 0x00000020;
    private const uint FileReadAttributes = 0x00000080;
    private const uint FileWriteAttributes = 0x00000100;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint FileOpen = 0x00000001;
    private const uint FileCreate = 0x00000002;
    private const uint FileOpenIf = 0x00000003;
    private const uint FileDirectoryFile = 0x00000001;
    private const uint FileSynchronousIoNonAlert = 0x00000020;
    private const uint FileNonDirectoryFile = 0x00000040;
    private const uint FileOpenReparsePoint = 0x00200000;
    private const uint ObjCaseInsensitive = 0x00000040;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeReparsePoint = 0x00000400;
    private const int FileBasicInfo = 0;
    private const int FileDispositionInfo = 4;
    private const int FileAttributeTagInfo = 9;
    private const int FileIdBothDirectoryInfo = 10;
    private const int FileIdBothDirectoryRestartInfo = 11;
    private const int ErrorNoMoreFiles = 18;
    private const int ErrorHandleEof = 38;
    private const int ErrorSharingViolation = 32;

    private readonly SafeFileHandle _root;
    private int _disposed;

    internal static int ObjectAttributesSize => Marshal.SizeOf<ObjectAttributes>();
    internal static int ObjectAttributesRootOffset =>
        checked((int)Marshal.OffsetOf<ObjectAttributes>(nameof(ObjectAttributes.RootDirectory)));
    internal static int ObjectAttributesNameOffset =>
        checked((int)Marshal.OffsetOf<ObjectAttributes>(nameof(ObjectAttributes.ObjectName)));
    internal static int IoStatusBlockSize => Marshal.SizeOf<IoStatusBlock>();

    internal WindowsNativeTemporaryFileSystem(string root)
    {
        ValidateAbi();
        _root = OpenOrCreateRoot(root);
    }

    internal static void ValidateAbi()
    {
        if (RuntimeInformation.ProcessArchitecture is not Architecture.X64 and not Architecture.Arm64)
            throw new PlatformNotSupportedException("文件比较原生临时存储仅支持 Windows x64/Arm64");
        if (Marshal.SizeOf<UnicodeString>() != 16 ||
            Marshal.OffsetOf<UnicodeString>(nameof(UnicodeString.Buffer)).ToInt32() != 8 ||
            ObjectAttributesSize != 48 ||
            ObjectAttributesRootOffset != 8 ||
            ObjectAttributesNameOffset != 16 ||
            Marshal.OffsetOf<ObjectAttributes>(nameof(ObjectAttributes.Attributes)).ToInt32() != 24 ||
            IoStatusBlockSize != 16 ||
            Marshal.OffsetOf<IoStatusBlock>(nameof(IoStatusBlock.Information)).ToInt32() != 8)
            throw new PlatformNotSupportedException("Windows 原生文件结构布局不受支持");
    }

    public NativeTemporaryDirectory CreateRequestDirectory(
        string requestId,
        string markerValue,
        DateTimeOffset createdAt,
        IFileCompareTemporaryStorageFaultHook? hook)
    {
        ThrowIfDisposed();
        ValidateRequestId(requestId);
        SafeFileHandle? directory = null;
        SafeFileHandle? marker = null;
        using var securityDescriptor = CreateRestrictedSecurityDescriptor();
        try
        {
            directory = OpenRelative(
                _root,
                requestId,
                Delete | FileListDirectory | FileTraverse | FileAddFile |
                FileReadAttributes | Synchronize,
                FileShareRead | FileShareWrite | FileShareDelete,
                FileCreate,
                FileDirectoryFile | FileOpenReparsePoint | FileSynchronousIoNonAlert,
                securityDescriptor.Pointer);
            EnsureDirectoryIsSafe(directory);
            hook?.AfterRequestDirectoryCreated();

            marker = OpenRelative(
                directory,
                MarkerName,
                FileReadData | FileWriteData | FileReadAttributes | FileWriteAttributes | Synchronize,
                FileShareRead,
                FileCreate,
                FileNonDirectoryFile | FileOpenReparsePoint | FileSynchronousIoNonAlert);
            EnsureRegularFileIsSafe(marker);
            var markerBytes = Encoding.ASCII.GetBytes(markerValue);
            RandomAccess.Write(marker, markerBytes, 0);
            hook?.BeforeMarkerFlush();
            RandomAccess.FlushToDisk(marker);
            SetLastWrite(marker, createdAt);
            hook?.AfterMarkerCreated();

            var ownedDirectory = directory;
            var ownedMarker = marker;
            directory = null;
            marker = null;
            return new WindowsNativeTemporaryDirectory(
                this,
                requestId,
                markerValue,
                ownedDirectory,
                ownedMarker,
                hook);
        }
        catch
        {
            marker?.Dispose();
            if (directory is not null)
            {
                try
                {
                    QuarantineAndDelete(directory, requestId, ".init-", null);
                }
                catch
                {
                }
                directory.Dispose();
            }
            throw;
        }
    }

    public int CleanupExpired(
        DateTimeOffset cutoff,
        IFileCompareTemporaryStorageFaultHook? hook,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var failures = 0;
        var initialNames = EnumerateNames(_root);
        foreach (var requestId in initialNames.Where(IsRequestId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            SafeFileHandle? directory = null;
            try
            {
                directory = OpenRelative(
                    _root,
                    requestId,
                    Delete | FileListDirectory | FileTraverse | FileReadAttributes | Synchronize,
                    FileShareRead | FileShareWrite | FileShareDelete,
                    FileOpen,
                    FileDirectoryFile | FileOpenReparsePoint | FileSynchronousIoNonAlert);
                EnsureDirectoryIsSafe(directory);
                hook?.AfterCleanupDirectoryOpened(requestId);
                if (!TryReadValidExpiredMarker(directory, requestId, cutoff))
                    continue;
                using var cleanupClaim = TryCreateCleanupClaim(requestId);
                if (cleanupClaim is null)
                    continue;
                hook?.BeforeCleanupDirectory();
                hook?.BeforeRequestDirectoryRename(requestId);
                QuarantineAndDelete(directory, requestId, ".gc-", hook);
            }
            catch (Win32Exception exception) when (
                exception.NativeErrorCode == ErrorSharingViolation)
            {
                // Another process owns a liveness handle without FILE_SHARE_DELETE.
            }
            catch (FileNotFoundException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (IOException)
            {
                failures++;
            }
            catch (UnauthorizedAccessException)
            {
                failures++;
            }
            catch (Win32Exception)
            {
                failures++;
            }
            finally
            {
                directory?.Dispose();
            }
        }
        foreach (var quarantineName in initialNames.Where(IsQuarantineName))
        {
            cancellationToken.ThrowIfCancellationRequested();
            SafeFileHandle? directory = null;
            try
            {
                using var claim = TryCreateCleanupClaim(quarantineName);
                if (claim is null)
                    continue;
                directory = OpenRelative(
                    _root,
                    quarantineName,
                    Delete | FileListDirectory | FileTraverse | FileReadAttributes | Synchronize,
                    FileShareRead | FileShareWrite | FileShareDelete,
                    FileOpen,
                    FileDirectoryFile | FileOpenReparsePoint | FileSynchronousIoNonAlert);
                EnsureDirectoryIsSafe(directory);
                DeleteQuarantined(directory, hook);
            }
            catch (FileNotFoundException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or Win32Exception)
            {
                failures++;
            }
            finally
            {
                directory?.Dispose();
            }
        }
        return failures;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _root.Dispose();
    }

    private bool TryReadValidExpiredMarker(
        SafeFileHandle directory,
        string requestId,
        DateTimeOffset cutoff)
    {
        using var marker = OpenRelative(
            directory,
            MarkerName,
            Delete | FileReadData | FileReadAttributes | Synchronize,
            FileShareRead | FileShareWrite | FileShareDelete,
            FileOpen,
            FileNonDirectoryFile | FileOpenReparsePoint | FileSynchronousIoNonAlert);
        EnsureRegularFileIsSafe(marker);
        var length = RandomAccess.GetLength(marker);
        if (length is <= 0 or > 65)
            return false;
        var bytes = new byte[checked((int)length)];
        if (RandomAccess.Read(marker, bytes, 0) != bytes.Length)
            return false;
        var value = Encoding.ASCII.GetString(bytes);
        var parts = value.Split(':');
        return parts.Length == 2 &&
               string.Equals(parts[0], requestId, StringComparison.Ordinal) &&
               parts[1].Length == 32 &&
               parts[1].All(IsLowerHex) &&
               GetLastWrite(marker) <= cutoff;
    }

    private SafeFileHandle? TryCreateCleanupClaim(string requestId)
    {
        try
        {
            var claim = OpenRelative(
                _root,
                $".claim-{requestId}",
                Delete | FileReadAttributes | Synchronize,
                FileShareRead | FileShareWrite | FileShareDelete,
                FileCreate,
                FileNonDirectoryFile | FileOpenReparsePoint | FileSynchronousIoNonAlert);
            try
            {
                EnsureRegularFileIsSafe(claim);
                MarkForDeletion(claim);
                return claim;
            }
            catch
            {
                claim.Dispose();
                throw;
            }
        }
        catch (Win32Exception exception) when (
            exception.NativeErrorCode is 5 or ErrorSharingViolation or 80 or 183)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void QuarantineAndDelete(
        SafeFileHandle directory,
        string requestId,
        string prefix,
        IFileCompareTemporaryStorageFaultHook? hook)
    {
        var quarantineName = $"{prefix}{requestId}-{Guid.NewGuid():N}";
        RenameRelative(directory, _root, quarantineName);
        hook?.AfterRequestDirectoryQuarantined(requestId);
        DeleteQuarantined(directory, hook);
    }

    private static void DeleteQuarantined(
        SafeFileHandle directory,
        IFileCompareTemporaryStorageFaultHook? hook)
    {
        var names = EnumerateNames(directory);
        if (names.Any(name => name is not MarkerName and not PayloadName))
            throw new IOException("临时资源包含未知条目，已保留隔离目录");
        foreach (var name in names)
        {
            using var entry = OpenRelative(
                directory,
                name,
                Delete | FileReadAttributes | Synchronize,
                FileShareRead | FileShareWrite | FileShareDelete,
                FileOpen,
                FileNonDirectoryFile | FileOpenReparsePoint | FileSynchronousIoNonAlert);
            EnsureRegularFileIsSafe(entry);
            hook?.BeforeEntryDisposition(name);
            MarkForDeletion(entry);
        }
        MarkForDeletion(directory);
    }

    private static SafeFileHandle OpenOrCreateRoot(string configuredRoot)
    {
        var fullPath = Path.GetFullPath(configuredRoot);
        if (!Path.IsPathFullyQualified(fullPath) ||
            fullPath.StartsWith(@"\\", StringComparison.Ordinal) ||
            fullPath.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            fullPath.StartsWith(@"\??\", StringComparison.Ordinal))
            throw new IOException("文件比较临时存储根目录必须是本地绝对路径");
        var pathRoot = Path.GetPathRoot(fullPath);
        if (pathRoot is null || pathRoot.Length != 3 || pathRoot[1] != ':')
            throw new IOException("文件比较临时存储根目录必须位于本地盘符");
        if (GetDriveType(pathRoot) != 3) // DRIVE_FIXED
            throw new IOException("文件比较临时存储根目录必须位于本地固定磁盘");

        var current = OpenAbsoluteNtDirectory(@"\??\" + pathRoot);
        try
        {
            var relative = Path.GetRelativePath(pathRoot, fullPath);
            if (relative == ".")
            {
                ValidateFinalRootHandle(current, pathRoot);
                return current;
            }
            foreach (var segment in relative.Split(
                         new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                ValidateSingleName(segment);
                var fullAccess = FileListDirectory | FileTraverse | FileAddSubdirectory |
                                 FileReadAttributes | Synchronize;
                SafeFileHandle next;
                try
                {
                    next = OpenRelative(
                        current,
                        segment,
                        fullAccess,
                        FileShareRead | FileShareWrite | FileShareDelete,
                        FileOpen,
                        FileDirectoryFile | FileOpenReparsePoint | FileSynchronousIoNonAlert);
                }
                catch (FileNotFoundException)
                {
                    next = OpenRelative(
                        current,
                        segment,
                        fullAccess,
                        FileShareRead | FileShareWrite | FileShareDelete,
                        FileCreate,
                        FileDirectoryFile | FileOpenReparsePoint | FileSynchronousIoNonAlert);
                }
                catch (UnauthorizedAccessException)
                {
                    next = OpenRelative(
                        current,
                        segment,
                        FileListDirectory | FileTraverse | FileReadAttributes | Synchronize,
                        FileShareRead | FileShareWrite | FileShareDelete,
                        FileOpen,
                        FileDirectoryFile | FileOpenReparsePoint | FileSynchronousIoNonAlert);
                }
                EnsureDirectoryIsSafe(next);
                current.Dispose();
                current = next;
            }
            ValidateFinalRootHandle(current, pathRoot);
            return current;
        }
        catch
        {
            current.Dispose();
            throw;
        }
    }

    private static SafeFileHandle OpenAbsoluteNtDirectory(string ntPath) =>
        OpenCore(
            null,
            ntPath,
            FileListDirectory | FileTraverse | FileAddSubdirectory |
            FileReadAttributes | Synchronize,
            FileShareRead | FileShareWrite | FileShareDelete,
            FileOpen,
            FileDirectoryFile | FileOpenReparsePoint | FileSynchronousIoNonAlert);

    private static void ValidateFinalRootHandle(
        SafeFileHandle handle,
        string expectedDriveRoot)
    {
        var capacity = 512;
        while (true)
        {
            var buffer = new StringBuilder(capacity);
            var length = GetFinalPathNameByHandle(handle, buffer, capacity, 0);
            if (length == 0)
                throw new Win32Exception(
                    Marshal.GetLastPInvokeError(),
                    "临时存储最终根句柄验证失败");
            if (length < capacity)
            {
                var finalPath = buffer.ToString();
                var expectedPrefix = @"\\?\" + expectedDriveRoot;
                if (!finalPath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase) ||
                    GetDriveType(finalPath.Substring(4, 3)) != 3)
                    throw new IOException("临时存储最终根句柄不属于预期本地固定卷");
                return;
            }
            capacity = checked((int)length + 1);
            if (capacity > 32_768)
                throw new IOException("临时存储最终根路径超过安全上限");
        }
    }

    private static SafeFileHandle OpenRelative(
        SafeFileHandle root,
        string name,
        uint desiredAccess,
        uint shareAccess,
        uint disposition,
        uint options,
        IntPtr securityDescriptor = default)
    {
        ValidateSingleName(name);
        return OpenCore(
            root,
            name,
            desiredAccess,
            shareAccess,
            disposition,
            options,
            securityDescriptor);
    }

    private static SafeFileHandle OpenCore(
        SafeFileHandle? root,
        string name,
        uint desiredAccess,
        uint shareAccess,
        uint disposition,
        uint options,
        IntPtr securityDescriptor = default)
    {
        var nameBytes = checked(name.Length * 2);
        if (nameBytes is 0 or > ushort.MaxValue)
            throw new IOException("原生文件名长度无效");
        var nameBuffer = Marshal.StringToHGlobalUni(name);
        var unicodeBuffer = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
        var rootReferenced = false;
        try
        {
            var unicode = new UnicodeString
            {
                Length = checked((ushort)nameBytes),
                MaximumLength = checked((ushort)nameBytes),
                Buffer = nameBuffer
            };
            Marshal.StructureToPtr(unicode, unicodeBuffer, false);
            root?.DangerousAddRef(ref rootReferenced);
            var attributes = new ObjectAttributes
            {
                Length = Marshal.SizeOf<ObjectAttributes>(),
                RootDirectory = root?.DangerousGetHandle() ?? IntPtr.Zero,
                ObjectName = unicodeBuffer,
                Attributes = ObjCaseInsensitive,
                SecurityDescriptor = securityDescriptor
            };
            var io = new IoStatusBlock();
            var status = NtCreateFile(
                out var rawHandle,
                desiredAccess,
                ref attributes,
                ref io,
                IntPtr.Zero,
                FileAttributeNormal,
                shareAccess,
                disposition,
                options,
                IntPtr.Zero,
                0);
            if (status < 0)
            {
                if (rawHandle != IntPtr.Zero && rawHandle != new IntPtr(-1))
                    CloseHandle(rawHandle);
                var error = unchecked((int)RtlNtStatusToDosError(status));
                if (error is 2 or 3)
                    throw new FileNotFoundException("临时资源不存在");
                if (error == 5)
                    throw new UnauthorizedAccessException("临时资源访问被拒绝");
                throw new Win32Exception(error, $"原生文件操作失败（NTSTATUS 0x{status:X8}）");
            }
            if (rawHandle == IntPtr.Zero || rawHandle == new IntPtr(-1))
                throw new IOException("原生文件操作未返回有效句柄");
            return new SafeFileHandle(rawHandle, ownsHandle: true);
        }
        finally
        {
            if (rootReferenced)
                root!.DangerousRelease();
            Marshal.FreeHGlobal(unicodeBuffer);
            Marshal.FreeHGlobal(nameBuffer);
        }
    }

    private static IReadOnlyList<string> EnumerateNames(SafeFileHandle directory)
    {
        const int bufferSize = 64 * 1024;
        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            var result = new List<string>();
            var restart = true;
            while (true)
            {
                if (!GetFileInformationByHandleEx(
                        directory,
                        restart ? FileIdBothDirectoryRestartInfo : FileIdBothDirectoryInfo,
                        buffer,
                        bufferSize))
                {
                    var error = Marshal.GetLastPInvokeError();
                    if (error is ErrorNoMoreFiles or ErrorHandleEof)
                        break;
                    throw new Win32Exception(error, "原生目录枚举失败");
                }
                restart = false;
                var offset = 0;
                while (true)
                {
                    if (offset < 0 || offset > bufferSize - 104)
                        throw new IOException("原生目录枚举数据无效");
                    var entry = IntPtr.Add(buffer, offset);
                    var next = Marshal.ReadInt32(entry, 0);
                    var fileNameLength = Marshal.ReadInt32(entry, 60);
                    if (fileNameLength < 0 || (fileNameLength & 1) != 0 ||
                        fileNameLength > bufferSize - offset - 104)
                        throw new IOException("原生目录枚举名称无效");
                    var name = Marshal.PtrToStringUni(IntPtr.Add(entry, 104), fileNameLength / 2)
                        ?? throw new IOException("原生目录枚举名称无效");
                    if (name is not "." and not "..")
                        result.Add(name);
                    if (next == 0)
                        break;
                    if (next < 104 || offset > bufferSize - next)
                        throw new IOException("原生目录枚举偏移无效");
                    offset += next;
                }
            }
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void RenameRelative(
        SafeFileHandle source,
        SafeFileHandle targetRoot,
        string targetName)
    {
        ValidateSingleName(targetName);
        var nameBytes = checked(targetName.Length * 2);
        var size = checked(20 + nameBytes);
        var buffer = Marshal.AllocHGlobal(size);
        var rootReferenced = false;
        try
        {
            for (var index = 0; index < size; index++)
                Marshal.WriteByte(buffer, index, 0);
            targetRoot.DangerousAddRef(ref rootReferenced);
            Marshal.WriteIntPtr(buffer, 8, targetRoot.DangerousGetHandle());
            Marshal.WriteInt32(buffer, 16, nameBytes);
            var bytes = Encoding.Unicode.GetBytes(targetName);
            Marshal.Copy(bytes, 0, IntPtr.Add(buffer, 20), bytes.Length);
            var io = new IoStatusBlock();
            var status = NtSetInformationFile(
                source,
                ref io,
                buffer,
                (uint)size,
                10); // FileRenameInformation
            if (status < 0)
            {
                var error = unchecked((int)RtlNtStatusToDosError(status));
                throw new Win32Exception(
                    error,
                    $"临时资源隔离失败（NTSTATUS 0x{status:X8}）");
            }
        }
        finally
        {
            if (rootReferenced)
                targetRoot.DangerousRelease();
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void MarkForDeletion(SafeFileHandle handle)
    {
        var buffer = Marshal.AllocHGlobal(1);
        try
        {
            Marshal.WriteByte(buffer, 1);
            if (!SetFileInformationByHandle(handle, FileDispositionInfo, buffer, 1))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "临时资源删除失败");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void EnsureDirectoryIsSafe(SafeFileHandle handle)
    {
        var info = GetAttributeTagInfo(handle);
        if ((info.FileAttributes & FileAttributeDirectory) == 0 ||
            (info.FileAttributes & FileAttributeReparsePoint) != 0)
            throw new IOException("文件比较临时目录不安全");
    }

    private static void EnsureRegularFileIsSafe(SafeFileHandle handle)
    {
        var info = GetAttributeTagInfo(handle);
        if ((info.FileAttributes & (FileAttributeDirectory | FileAttributeReparsePoint)) != 0)
            throw new IOException("文件比较临时文件不安全");
    }

    private static NativeSecurityDescriptor CreateRestrictedSecurityDescriptor()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new IOException("无法确定临时资源目录所有者");
        var sddl = $"D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;{sid})";
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(
                sddl,
                1,
                out var securityDescriptor,
                out _))
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                "临时资源目录安全描述符创建失败");
        return new NativeSecurityDescriptor(securityDescriptor);
    }

    private static FileAttributeTagInformation GetAttributeTagInfo(SafeFileHandle handle)
    {
        var size = Marshal.SizeOf<FileAttributeTagInformation>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!GetFileInformationByHandleEx(handle, FileAttributeTagInfo, buffer, (uint)size))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "临时资源属性查询失败");
            return Marshal.PtrToStructure<FileAttributeTagInformation>(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static DateTimeOffset GetLastWrite(SafeFileHandle handle)
    {
        var size = Marshal.SizeOf<FileBasicInformation>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!GetFileInformationByHandleEx(handle, FileBasicInfo, buffer, (uint)size))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "临时资源时间查询失败");
            var info = Marshal.PtrToStructure<FileBasicInformation>(buffer);
            return new DateTimeOffset(DateTime.FromFileTimeUtc(info.LastWriteTime));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void SetLastWrite(SafeFileHandle handle, DateTimeOffset value)
    {
        var fileTime = value.UtcDateTime.ToFileTimeUtc();
        if (!SetFileTime(handle, IntPtr.Zero, IntPtr.Zero, ref fileTime))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "临时资源心跳更新失败");
    }

    private static void ValidateRequestId(string value)
    {
        if (!IsRequestId(value))
            throw new ArgumentException("请求标识无效", nameof(value));
    }

    private static bool IsRequestId(string value) =>
        value.Length == 32 && value.All(IsLowerHex);

    private static bool IsQuarantineName(string value)
    {
        var prefixLength = value.StartsWith(".gc-", StringComparison.Ordinal)
            ? 4
            : value.StartsWith(".init-", StringComparison.Ordinal)
                ? 6
                : 0;
        return prefixLength != 0 &&
               value.Length == prefixLength + 32 + 1 + 32 &&
               value[prefixLength + 32] == '-' &&
               value.AsSpan(prefixLength, 32).ToArray().All(IsLowerHex) &&
               value.AsSpan(prefixLength + 33, 32).ToArray().All(IsLowerHex);
    }

    private static bool IsLowerHex(char value) =>
        value is >= '0' and <= '9' or >= 'a' and <= 'f';

    private static void ValidateSingleName(string value)
    {
        if (string.IsNullOrEmpty(value) ||
            value is "." or ".." ||
            value.IndexOfAny(['/', '\\', '\0', ':', '*', '?']) >= 0)
            throw new IOException("原生文件名必须是安全单段名称");
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    private sealed class WindowsNativeTemporaryDirectory(
        WindowsNativeTemporaryFileSystem owner,
        string requestId,
        string markerValue,
        SafeFileHandle directory,
        SafeFileHandle marker,
        IFileCompareTemporaryStorageFaultHook? hook) : NativeTemporaryDirectory
    {
        private int _disposed;

        internal override Stream CreatePayloadWriteStream()
        {
            ThrowIfDisposed();
            var payload = OpenRelative(
                directory,
                PayloadName,
                FileWriteData | FileReadAttributes,
                FileShareRead | FileShareWrite | FileShareDelete,
                FileCreate,
                FileNonDirectoryFile | FileOpenReparsePoint);
            try
            {
                EnsureRegularFileIsSafe(payload);
                return new FileStream(payload, FileAccess.Write, 64 * 1024, isAsync: true);
            }
            catch
            {
                payload.Dispose();
                throw;
            }
        }

        internal override Stream OpenPayloadReadStream()
        {
            ThrowIfDisposed();
            var payload = OpenRelative(
                directory,
                PayloadName,
                FileReadData | FileReadAttributes,
                FileShareRead | FileShareWrite | FileShareDelete,
                FileOpen,
                FileNonDirectoryFile | FileOpenReparsePoint);
            try
            {
                EnsureRegularFileIsSafe(payload);
                return new FileStream(payload, FileAccess.Read, 64 * 1024, isAsync: true);
            }
            catch
            {
                payload.Dispose();
                throw;
            }
        }

        internal override void Heartbeat(DateTimeOffset now)
        {
            ThrowIfDisposed();
            if (RandomAccess.GetLength(marker) != Encoding.ASCII.GetByteCount(markerValue))
                throw new IOException("文件比较临时标记无效");
            SetLastWrite(marker, now);
        }

        public override ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return ValueTask.CompletedTask;
            marker.Dispose();
            try
            {
                owner.QuarantineAndDelete(directory, requestId, ".gc-", hook);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or Win32Exception)
            {
            }
            finally
            {
                directory.Dispose();
            }
            return ValueTask.CompletedTask;
        }

        private void ThrowIfDisposed() =>
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    private sealed class NativeSecurityDescriptor(IntPtr pointer) : IDisposable
    {
        internal IntPtr Pointer { get; } = pointer;

        public void Dispose()
        {
            if (Pointer != IntPtr.Zero)
                LocalFree(Pointer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        internal ushort Length;
        internal ushort MaximumLength;
        internal IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectAttributes
    {
        internal int Length;
        internal IntPtr RootDirectory;
        internal IntPtr ObjectName;
        internal uint Attributes;
        internal IntPtr SecurityDescriptor;
        internal IntPtr SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatusBlock
    {
        internal IntPtr StatusOrPointer;
        internal UIntPtr Information;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributeTagInformation
    {
        internal uint FileAttributes;
        internal uint ReparseTag;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileBasicInformation
    {
        internal long CreationTime;
        internal long LastAccessTime;
        internal long LastWriteTime;
        internal long ChangeTime;
        internal uint FileAttributes;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtCreateFile(
        out IntPtr fileHandle,
        uint desiredAccess,
        ref ObjectAttributes objectAttributes,
        ref IoStatusBlock ioStatusBlock,
        IntPtr allocationSize,
        uint fileAttributes,
        uint shareAccess,
        uint createDisposition,
        uint createOptions,
        IntPtr eaBuffer,
        uint eaLength);

    [DllImport("ntdll.dll")]
    private static extern uint RtlNtStatusToDosError(int status);

    [DllImport("ntdll.dll")]
    private static extern int NtSetInformationFile(
        SafeFileHandle fileHandle,
        ref IoStatusBlock ioStatusBlock,
        IntPtr fileInformation,
        uint length,
        int fileInformationClass);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file,
        int informationClass,
        IntPtr information,
        uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle file,
        int informationClass,
        IntPtr information,
        uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileTime(
        SafeFileHandle file,
        IntPtr creationTime,
        IntPtr lastAccessTime,
        ref long lastWriteTime);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetDriveType(string rootPathName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle file,
        StringBuilder filePath,
        int filePathLength,
        uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(
        string stringSecurityDescriptor,
        uint stringSdRevision,
        out IntPtr securityDescriptor,
        out uint securityDescriptorSize);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
