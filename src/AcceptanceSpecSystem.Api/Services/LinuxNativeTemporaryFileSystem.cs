using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace AcceptanceSpecSystem.Api.Services;

[SupportedOSPlatform("linux")]
internal sealed class LinuxNativeTemporaryFileSystem : INativeTemporaryFileSystem
{
    private const string MarkerName = ".acceptance-file-compare";
    private const string PayloadName = "payload.tmp";
    private const string DeleteMarkerName = ".delete-.acceptance-file-compare";
    private const string DeletePayloadName = ".delete-payload.tmp";

    private const int OpenReadOnly = 0;
    private const int OpenWriteOnly = 1;
    private const int OpenReadWrite = 2;
    private const int OpenCreate = 0x40;
    private const int OpenExclusive = 0x80;
    private const int OpenNonBlocking = 0x800;
    private const int OpenTruncate = 0x200;
    private const int OpenDirectoryFlag = 0x10000;
    private const int OpenNoFollow = 0x20000;
    private const int OpenCloseOnExec = 0x80000;
    private const int OpenPath = 0x200000;
    private const int AtEmptyPath = 0x1000;
    private const int AtSymlinkNoFollow = 0x100;
    private const int AtRemoveDirectory = 0x200;
    private const uint StatxBasicStats = 0x7FF;
    private const uint StatxRequiredFields = 0x143; // TYPE | MODE | MTIME | INO
    private const int StatxBufferSize = 256;
    private const uint RenameNoReplace = 1;
    private const int LockExclusive = 2;
    private const int LockNonBlocking = 4;
    private const uint DirectoryMode = 0x1C0; // 0700
    private const uint FileMode = 0x180; // 0600
    private const uint ReadOnlyFileMode = 0x100; // 0400
    private const uint FileTypeMask = 0xF000;
    private const uint RegularFileType = 0x8000;
    private const uint DirectoryFileType = 0x4000;
    private const int ErrorAccessDenied = 13;
    private const int ErrorAlreadyExists = 17;
    private const int ErrorBusy = 16;
    private const int ErrorInvalid = 22;
    private const int ErrorIsDirectory = 21;
    private const int ErrorLoop = 40;
    private const int ErrorNoEntry = 2;
    private const int ErrorNotDirectory = 20;
    private const int ErrorNotEmpty = 39;
    private const int ErrorFunctionNotImplemented = 38;
    private const int ErrorOperationNotSupported = 95;
    private const int ErrorPermission = 1;
    private const int ErrorWouldBlock = 11;
    private const int DuplicateCloseOnExec = 1030;
    private const long UTimeOmit = (1L << 30) - 2;

    private readonly SafeFileHandle _root;
    private int _disposed;

    internal LinuxNativeTemporaryFileSystem(string root)
    {
        if (RuntimeInformation.ProcessArchitecture is not Architecture.X64 and not Architecture.Arm64)
            throw new PlatformNotSupportedException("Linux 原生临时存储仅支持 x64/Arm64");
        _root = OpenOrCreateRoot(root);
    }

    public NativeTemporaryDirectory CreateRequestDirectory(
        string requestId,
        string markerValue,
        DateTimeOffset createdAt,
        IFileCompareTemporaryStorageFaultHook? hook)
    {
        ThrowIfDisposed();
        ValidateRequestId(requestId);
        var markerToken = GetMarkerToken(markerValue, requestId);
        var initializationName = $".init-{requestId}-{markerToken}";
        CreateDirectory(_root, initializationName);
        SafeFileHandle? directory = null;
        SafeFileHandle? marker = null;
        try
        {
            directory = OpenDirectory(_root, initializationName);
            LockDirectory(directory, LockExclusive);
            hook?.AfterRequestDirectoryCreated();

            marker = OpenRegularFile(
                directory,
                MarkerName,
                OpenReadWrite | OpenCreate | OpenExclusive,
                FileMode);
            var markerBytes = Encoding.ASCII.GetBytes(markerValue);
            RandomAccess.Write(marker, markerBytes, 0);
            hook?.BeforeMarkerFlush();
            RandomAccess.FlushToDisk(marker);
            SetLastWrite(marker, createdAt);
            hook?.AfterMarkerCreated();
            hook?.BeforeRequestDirectoryPublished(requestId);
            RenameAndVerify(
                _root,
                initializationName,
                requestId,
                directory,
                expectDirectory: true);
            if (fchmod(marker, ReadOnlyFileMode) != 0)
                ThrowNative("临时资源标记权限收敛失败");

            var ownedDirectory = directory;
            var ownedMarker = marker;
            directory = null;
            marker = null;
            return new LinuxNativeTemporaryDirectory(
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
                    LockDirectory(directory, LockExclusive);
                    var currentName = NameStillRefersTo(
                        _root,
                        initializationName,
                        directory,
                        expectDirectory: true)
                        ? initializationName
                        : requestId;
                    QuarantineAndDelete(
                        directory,
                        currentName,
                        requestId,
                        markerToken,
                        ".init-",
                        null);
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
        cancellationToken.ThrowIfCancellationRequested();
        var failures = 0;
        foreach (var recoveryName in EnumerateNames(_root, cancellationToken)
                     .Where(IsRecoveryCandidateName))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryParseRecoveryName(
                    recoveryName,
                    out var prefix,
                    out var requestId,
                    out var markerToken))
            {
                failures++;
                continue;
            }

            SafeFileHandle? directory = null;
            try
            {
                directory = OpenDirectory(_root, recoveryName);
                if (!TryLockDirectory(directory, LockExclusive | LockNonBlocking))
                    continue;
                if (!HasValidRecoveryOwnership(directory, prefix, requestId, markerToken))
                {
                    failures++;
                    continue;
                }
                DeleteQuarantined(recoveryName, directory, hook);
            }
            catch (Exception exception) when (IsExpectedFileSystemFailure(exception))
            {
                failures++;
            }
            finally
            {
                directory?.Dispose();
            }
        }

        foreach (var requestId in EnumerateNames(_root, cancellationToken).Where(IsRequestId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            SafeFileHandle? directory = null;
            try
            {
                directory = OpenDirectory(_root, requestId);
                if (!TryLockDirectory(directory, LockExclusive | LockNonBlocking))
                    continue;
                hook?.AfterCleanupDirectoryOpened(requestId);
                if (!TryReadValidExpiredMarker(directory, requestId, cutoff, out var markerToken))
                    continue;
                hook?.BeforeCleanupDirectory();
                hook?.BeforeRequestDirectoryRename(requestId);
                QuarantineAndDelete(
                    directory,
                    requestId,
                    requestId,
                    markerToken,
                    ".gc-",
                    hook);
            }
            catch (Exception exception) when (IsExpectedFileSystemFailure(exception))
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
        DateTimeOffset cutoff,
        out string markerToken)
    {
        markerToken = string.Empty;
        using var marker = OpenRegularFile(directory, MarkerName, OpenReadOnly);
        var length = RandomAccess.GetLength(marker);
        if (length != 65)
            return false;
        var bytes = new byte[65];
        if (RandomAccess.Read(marker, bytes, 0) != bytes.Length)
            return false;
        var value = Encoding.ASCII.GetString(bytes);
        var parts = value.Split(':');
        if (parts.Length != 2 ||
            !string.Equals(parts[0], requestId, StringComparison.Ordinal) ||
            parts[1].Length != 32 ||
            !parts[1].All(IsLowerHex) ||
            GetLastWrite(marker) > cutoff)
            return false;
        markerToken = parts[1];
        return true;
    }

    private static bool HasValidRecoveryOwnership(
        SafeFileHandle directory,
        string prefix,
        string requestId,
        string markerToken)
    {
        var expected = $"{requestId}:{markerToken}";
        try
        {
            using var marker = OpenRegularFile(directory, MarkerName, OpenReadOnly);
            return MarkerHasExpectedValue(marker, expected);
        }
        catch (FileNotFoundException)
        {
            try
            {
                using var marker = OpenRegularFile(
                    directory,
                    DeleteMarkerName,
                    OpenReadOnly);
                return MarkerHasExpectedValue(marker, expected);
            }
            catch (FileNotFoundException)
            {
                return prefix is ".init-" or ".gc-" &&
                       EnumerateNames(directory, CancellationToken.None).Count == 0;
            }
        }
    }

    private static bool MarkerHasExpectedValue(
        SafeFileHandle marker,
        string expected)
    {
        if (RandomAccess.GetLength(marker) != Encoding.ASCII.GetByteCount(expected))
            return false;
        Span<byte> bytes = stackalloc byte[65];
        return RandomAccess.Read(marker, bytes, 0) == bytes.Length &&
               bytes.SequenceEqual(Encoding.ASCII.GetBytes(expected));
    }

    private void QuarantineAndDelete(
        SafeFileHandle directory,
        string sourceName,
        string requestId,
        string markerToken,
        string prefix,
        IFileCompareTemporaryStorageFaultHook? hook)
    {
        LockDirectory(directory, LockExclusive);
        var quarantineName = $"{prefix}{requestId}-{markerToken}";
        if (!string.Equals(sourceName, quarantineName, StringComparison.Ordinal))
        {
            RenameAndVerify(
                _root,
                sourceName,
                quarantineName,
                directory,
                expectDirectory: true);
        }
        hook?.AfterRequestDirectoryQuarantined(requestId);
        DeleteQuarantined(quarantineName, directory, hook);
    }

    private void DeleteQuarantined(
        string quarantineName,
        SafeFileHandle directory,
        IFileCompareTemporaryStorageFaultHook? hook)
    {
        var ownedNames = EnumerateNames(directory, CancellationToken.None);
        if (ownedNames.Any(name =>
                name is not MarkerName and
                    not PayloadName and
                    not DeleteMarkerName and
                    not DeletePayloadName) ||
            ownedNames.Contains(PayloadName) && ownedNames.Contains(DeletePayloadName) ||
            ownedNames.Contains(MarkerName) && ownedNames.Contains(DeleteMarkerName))
            throw new IOException("临时资源包含未知或冲突条目，已保留隔离目录");

        DeleteOwnedEntry(
            directory,
            ownedNames,
            PayloadName,
            DeletePayloadName,
            hook);
        DeleteOwnedEntry(
            directory,
            ownedNames,
            MarkerName,
            DeleteMarkerName,
            hook);

        if (!NameStillRefersTo(_root, quarantineName, directory, expectDirectory: true))
            throw new IOException("临时资源隔离目录身份已变化");
        if (unlinkat(_root, quarantineName, AtRemoveDirectory) != 0)
            ThrowNative("临时资源目录删除失败");
    }

    private static void DeleteOwnedEntry(
        SafeFileHandle directory,
        IReadOnlyCollection<string> ownedNames,
        string entryName,
        string deletionName,
        IFileCompareTemporaryStorageFaultHook? hook)
    {
        if (ownedNames.Contains(deletionName))
        {
            using var quarantined = OpenRegularMetadata(directory, deletionName);
            if (unlinkat(directory, deletionName, 0) != 0)
                ThrowNative("临时资源文件删除失败");
            hook?.AfterEntryDeleted(entryName);
            return;
        }
        if (!ownedNames.Contains(entryName))
            return;

        using var entry = OpenRegularMetadata(directory, entryName);
        hook?.BeforeEntryDisposition(entryName);
        RenameAndVerify(
            directory,
            entryName,
            deletionName,
            entry,
            expectDirectory: false);
        hook?.AfterEntryQuarantined(entryName);
        if (unlinkat(directory, deletionName, 0) != 0)
            ThrowNative("临时资源文件删除失败");
        hook?.AfterEntryDeleted(entryName);
    }

    private static SafeFileHandle OpenOrCreateRoot(string configuredRoot)
    {
        var fullPath = Path.GetFullPath(configuredRoot);
        if (!Path.IsPathFullyQualified(fullPath) ||
            fullPath[0] != '/' ||
            string.Equals(fullPath, "/", StringComparison.Ordinal))
            throw new IOException("文件比较临时存储根目录必须是本地绝对路径且不能是根目录");

        var current = OpenAbsoluteDirectory("/");
        try
        {
            foreach (var segment in fullPath.Split(
                         '/',
                         StringSplitOptions.RemoveEmptyEntries))
            {
                ValidateSingleName(segment);
                if (mkdirat(current, segment, DirectoryMode) != 0)
                {
                    var error = Marshal.GetLastPInvokeError();
                    if (error != ErrorAlreadyExists)
                        ThrowNative("临时存储根目录创建失败", error);
                }
                var next = OpenDirectory(current, segment);
                current.Dispose();
                current = next;
            }
            if (fchmod(current, DirectoryMode) != 0)
                ThrowNative("临时存储根目录权限收敛失败");
            return current;
        }
        catch
        {
            current.Dispose();
            throw;
        }
    }

    private static SafeFileHandle OpenAbsoluteDirectory(string path)
    {
        var descriptor = open(
            path,
            OpenReadOnly | OpenDirectoryFlag | OpenNoFollow | OpenCloseOnExec);
        return WrapDescriptor(descriptor, "临时存储根目录打开失败");
    }

    private static void CreateDirectory(SafeFileHandle root, string name)
    {
        ValidateSingleName(name);
        if (mkdirat(root, name, DirectoryMode) != 0)
            ThrowNative("临时请求目录创建失败");
    }

    private static SafeFileHandle OpenDirectory(SafeFileHandle root, string name)
    {
        ValidateSingleName(name);
        var descriptor = openat(
            root,
            name,
            OpenReadOnly | OpenDirectoryFlag | OpenNoFollow | OpenCloseOnExec,
            0);
        var handle = WrapDescriptor(descriptor, "临时目录打开失败");
        try
        {
            EnsureDirectoryIsSafe(handle);
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static SafeFileHandle OpenRegularFile(
        SafeFileHandle root,
        string name,
        int flags,
        uint mode = 0)
    {
        ValidateSingleName(name);
        if ((flags & OpenCreate) == 0)
        {
            using var metadata = OpenRegularMetadata(root, name);
            var existingDescriptor = open(
                $"/proc/self/fd/{metadata.DangerousGetHandle()}",
                flags | OpenCloseOnExec);
            var reopened = WrapDescriptor(
                existingDescriptor,
                "临时文件数据句柄打开失败");
            try
            {
                EnsureRegularFileIsSafe(reopened);
                if (GetIdentity(metadata) != GetIdentity(reopened))
                    throw new IOException("临时文件元数据与数据句柄身份不匹配");
                return reopened;
            }
            catch
            {
                reopened.Dispose();
                throw;
            }
        }

        var descriptor = openat(
            root,
            name,
            flags | OpenNoFollow | OpenCloseOnExec,
            mode);
        var handle = WrapDescriptor(descriptor, "临时文件打开失败");
        try
        {
            EnsureRegularFileIsSafe(handle);
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static SafeFileHandle OpenRegularMetadata(
        SafeFileHandle root,
        string name)
    {
        ValidateSingleName(name);
        var descriptor = openat(
            root,
            name,
            OpenPath | OpenNoFollow | OpenCloseOnExec,
            0);
        var handle = WrapDescriptor(descriptor, "临时文件元数据句柄打开失败");
        try
        {
            EnsureRegularFileIsSafe(handle);
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static SafeFileHandle WrapDescriptor(int descriptor, string operation)
    {
        if (descriptor < 0)
            ThrowNative(operation);
        return new SafeFileHandle(new IntPtr(descriptor), ownsHandle: true);
    }

    private static IReadOnlyList<string> EnumerateNames(
        SafeFileHandle directory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var duplicate = fcntl(directory, DuplicateCloseOnExec, 0);
        if (duplicate < 0)
            ThrowNative("临时目录句柄复制失败");
        var stream = fdopendir(duplicate);
        if (stream == IntPtr.Zero)
        {
            close(duplicate);
            ThrowNative("临时目录枚举打开失败");
        }
        Exception? primaryFailure = null;
        try
        {
            var names = new List<string>();
            rewinddir(stream);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Marshal.SetLastPInvokeError(0);
                var entry = readdir(stream);
                if (entry == IntPtr.Zero)
                {
                    var error = Marshal.GetLastPInvokeError();
                    if (error != 0)
                        ThrowNative("临时目录枚举失败", error);
                    return names;
                }
                var name = Marshal.PtrToStringUTF8(IntPtr.Add(entry, 19))
                    ?? throw new IOException("临时目录枚举名称无效");
                if (name is "." or "..")
                    continue;
                names.Add(name);
            }
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
            throw;
        }
        finally
        {
            if (closedir(stream) != 0 && primaryFailure is null)
                ThrowNative("临时目录枚举关闭失败");
        }
    }

    private static void RenameAndVerify(
        SafeFileHandle sourceRoot,
        string sourceName,
        string targetName,
        SafeFileHandle expected,
        bool expectDirectory)
    {
        ValidateSingleName(sourceName);
        ValidateSingleName(targetName);
        if (!NameStillRefersTo(sourceRoot, sourceName, expected, expectDirectory))
            throw new IOException("临时资源原子隔离前的对象身份不匹配");
        if (renameat2(
                sourceRoot,
                sourceName,
                sourceRoot,
                targetName,
                RenameNoReplace) != 0)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error is
                ErrorInvalid or
                ErrorOperationNotSupported or
                ErrorFunctionNotImplemented)
                throw new PlatformNotSupportedException(
                    "当前 Linux 文件系统不支持安全的 RENAME_NOREPLACE 隔离");
            ThrowNative("临时资源原子隔离失败", error);
        }
        if (!NameStillRefersTo(sourceRoot, targetName, expected, expectDirectory))
        {
            _ = renameat2(
                sourceRoot,
                targetName,
                sourceRoot,
                sourceName,
                RenameNoReplace);
            throw new IOException("临时资源原子隔离后的对象身份不匹配");
        }
    }

    private static bool NameStillRefersTo(
        SafeFileHandle root,
        string name,
        SafeFileHandle expected,
        bool expectDirectory)
    {
        try
        {
            using var current = expectDirectory
                ? OpenDirectory(root, name)
                : OpenRegularMetadata(root, name);
            var expectedIdentity = GetIdentity(expected);
            var currentIdentity = GetIdentity(current);
            return expectedIdentity == currentIdentity;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    private static void EnsureDirectoryIsSafe(SafeFileHandle handle)
    {
        var status = GetStatus(handle);
        if ((status.Mode & FileTypeMask) != DirectoryFileType)
            throw new IOException("文件比较临时目录不安全");
    }

    private static void EnsureRegularFileIsSafe(SafeFileHandle handle)
    {
        var status = GetStatus(handle);
        if ((status.Mode & FileTypeMask) != RegularFileType)
            throw new IOException("文件比较临时文件不安全");
    }

    private static (uint DeviceMajor, uint DeviceMinor, ulong Inode) GetIdentity(
        SafeFileHandle handle)
    {
        var status = GetStatus(handle);
        return (status.DeviceMajor, status.DeviceMinor, status.Inode);
    }

    private static LinuxStatus GetStatus(SafeFileHandle handle)
    {
        var buffer = Marshal.AllocHGlobal(StatxBufferSize);
        try
        {
            for (var offset = 0; offset < StatxBufferSize; offset++)
                Marshal.WriteByte(buffer, offset, 0);
            if (statx(
                    handle,
                    string.Empty,
                    AtEmptyPath | AtSymlinkNoFollow,
                    StatxBasicStats,
                    buffer) != 0)
                ThrowNative("临时资源身份查询失败");
            var returnedFields = unchecked((uint)Marshal.ReadInt32(buffer, 0));
            if ((returnedFields & StatxRequiredFields) != StatxRequiredFields)
                throw new IOException("临时资源身份查询结果不完整");
            return new LinuxStatus(
                Mode: unchecked((ushort)Marshal.ReadInt16(buffer, 28)),
                Inode: unchecked((ulong)Marshal.ReadInt64(buffer, 32)),
                ModifiedSeconds: Marshal.ReadInt64(buffer, 112),
                ModifiedNanoseconds: unchecked((uint)Marshal.ReadInt32(buffer, 120)),
                DeviceMajor: unchecked((uint)Marshal.ReadInt32(buffer, 136)),
                DeviceMinor: unchecked((uint)Marshal.ReadInt32(buffer, 140)));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static DateTimeOffset GetLastWrite(SafeFileHandle handle)
    {
        var status = GetStatus(handle);
        return DateTimeOffset.FromUnixTimeSeconds(status.ModifiedSeconds)
            .AddTicks(status.ModifiedNanoseconds / 100);
    }

    private static void SetLastWrite(SafeFileHandle handle, DateTimeOffset value)
    {
        var seconds = value.ToUnixTimeSeconds();
        var nanoseconds = (value - DateTimeOffset.FromUnixTimeSeconds(seconds)).Ticks * 100;
        var times =
            new[]
            {
                new LinuxTimeSpec(0, UTimeOmit),
                new LinuxTimeSpec(seconds, nanoseconds)
            };
        if (futimens(handle, times) != 0)
            ThrowNative("临时资源心跳更新失败");
    }

    private static bool TryLockDirectory(SafeFileHandle directory, int operation)
    {
        if (flock(directory, operation) == 0)
            return true;
        var error = Marshal.GetLastPInvokeError();
        if (error is ErrorWouldBlock or ErrorBusy)
            return false;
        ThrowNative("临时资源目录锁定失败", error);
        return false;
    }

    private static void LockDirectory(SafeFileHandle directory, int operation)
    {
        if (flock(directory, operation) != 0)
            ThrowNative("临时资源目录锁定失败");
    }

    private static bool IsExpectedFileSystemFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or Win32Exception;

    private static void ThrowNative(string operation, int? errorOverride = null)
    {
        var error = errorOverride ?? Marshal.GetLastPInvokeError();
        if (error == ErrorNoEntry)
            throw new FileNotFoundException("临时资源不存在");
        if (error is ErrorAccessDenied or ErrorPermission)
            throw new UnauthorizedAccessException("临时资源访问被拒绝");
        if (error is ErrorLoop or ErrorNotDirectory or ErrorIsDirectory)
            throw new IOException($"{operation}：资源类型或链接边界不安全");
        if (error == ErrorNotEmpty)
            throw new IOException($"{operation}：目录包含未接管条目");
        throw new IOException($"{operation}（errno {error}）");
    }

    private static void ValidateRequestId(string value)
    {
        if (!IsRequestId(value))
            throw new ArgumentException("请求标识无效", nameof(value));
    }

    private static bool IsRequestId(string value) =>
        value.Length == 32 && value.All(IsLowerHex);

    private static string GetMarkerToken(string markerValue, string requestId)
    {
        var expectedPrefix = requestId + ":";
        if (markerValue.Length != 65 ||
            !markerValue.StartsWith(expectedPrefix, StringComparison.Ordinal) ||
            !markerValue.AsSpan(expectedPrefix.Length).ToArray().All(IsLowerHex))
            throw new ArgumentException("临时资源标记无效", nameof(markerValue));
        return markerValue[expectedPrefix.Length..];
    }

    private static bool IsRecoveryCandidateName(string value) =>
        value.StartsWith(".gc-", StringComparison.Ordinal) ||
        value.StartsWith(".init-", StringComparison.Ordinal);

    private static bool TryParseRecoveryName(
        string value,
        out string prefix,
        out string requestId,
        out string markerToken)
    {
        prefix = value.StartsWith(".gc-", StringComparison.Ordinal)
            ? ".gc-"
            : value.StartsWith(".init-", StringComparison.Ordinal)
                ? ".init-"
                : string.Empty;
        requestId = string.Empty;
        markerToken = string.Empty;
        if (prefix.Length == 0 ||
            value.Length != prefix.Length + 32 + 1 + 32 ||
            value[prefix.Length + 32] != '-')
            return false;
        requestId = value.Substring(prefix.Length, 32);
        markerToken = value[(prefix.Length + 33)..];
        return IsRequestId(requestId) &&
               markerToken.Length == 32 &&
               markerToken.All(IsLowerHex);
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

    private sealed class LinuxNativeTemporaryDirectory(
        LinuxNativeTemporaryFileSystem owner,
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
            var payload = OpenRegularFile(
                directory,
                PayloadName,
                OpenWriteOnly | OpenCreate | OpenExclusive | OpenTruncate,
                FileMode);
            try
            {
                return new FileStream(payload, FileAccess.Write, 64 * 1024, isAsync: false);
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
            var payload = OpenRegularFile(directory, PayloadName, OpenReadOnly);
            try
            {
                return new FileStream(payload, FileAccess.Read, 64 * 1024, isAsync: false);
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
            EnsureMarkerOwned();
            SetLastWrite(marker, now);
        }

        public override ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return ValueTask.CompletedTask;
            try
            {
                EnsureMarkerOwned();
            }
            catch
            {
                marker.Dispose();
                directory.Dispose();
                throw;
            }
            marker.Dispose();
            try
            {
                owner.QuarantineAndDelete(
                    directory,
                    requestId,
                    requestId,
                    GetMarkerToken(markerValue, requestId),
                    ".gc-",
                    hook);
            }
            finally
            {
                directory.Dispose();
            }
            return ValueTask.CompletedTask;
        }

        private void EnsureMarkerOwned()
        {
            var expected = Encoding.ASCII.GetBytes(markerValue);
            if (RandomAccess.GetLength(marker) != expected.Length)
                throw new IOException("文件比较临时标记无效");
            Span<byte> actual = stackalloc byte[65];
            if (RandomAccess.Read(marker, actual, 0) != actual.Length ||
                !actual.SequenceEqual(expected) ||
                !NameStillRefersTo(
                    directory,
                    MarkerName,
                    marker,
                    expectDirectory: false))
                throw new IOException("文件比较临时标记身份或内容无效");
        }

        private void ThrowIfDisposed() =>
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref _disposed) != 0,
                nameof(TemporaryFileLease));
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct LinuxTimeSpec(long seconds, long nanoseconds)
    {
        internal readonly long Seconds = seconds;
        internal readonly long Nanoseconds = nanoseconds;
    }

    private readonly record struct LinuxStatus(
        ushort Mode,
        ulong Inode,
        long ModifiedSeconds,
        uint ModifiedNanoseconds,
        uint DeviceMajor,
        uint DeviceMinor);

    [DllImport("libc", SetLastError = true)]
    private static extern int open(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string pathname,
        int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int openat(
        SafeFileHandle directory,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string pathname,
        int flags,
        uint mode);

    [DllImport("libc", SetLastError = true)]
    private static extern int mkdirat(
        SafeFileHandle directory,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string pathname,
        uint mode);

    [DllImport("libc", SetLastError = true)]
    private static extern int renameat2(
        SafeFileHandle sourceDirectory,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string sourceName,
        SafeFileHandle targetDirectory,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string targetName,
        uint flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int unlinkat(
        SafeFileHandle directory,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string pathname,
        int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int statx(
        SafeFileHandle directory,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string pathname,
        int flags,
        uint mask,
        IntPtr status);

    [DllImport("libc", SetLastError = true)]
    private static extern int fchmod(SafeFileHandle descriptor, uint mode);

    [DllImport("libc", SetLastError = true)]
    private static extern int futimens(
        SafeFileHandle descriptor,
        [In] LinuxTimeSpec[] times);

    [DllImport("libc", SetLastError = true)]
    private static extern int flock(SafeFileHandle descriptor, int operation);

    [DllImport("libc", SetLastError = true)]
    private static extern int fcntl(
        SafeFileHandle descriptor,
        int command,
        int argument);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int descriptor);

    [DllImport("libc", SetLastError = true)]
    private static extern IntPtr fdopendir(int descriptor);

    [DllImport("libc", SetLastError = true)]
    private static extern IntPtr readdir(IntPtr directory);

    [DllImport("libc")]
    private static extern void rewinddir(IntPtr directory);

    [DllImport("libc", SetLastError = true)]
    private static extern int closedir(IntPtr directory);
}
