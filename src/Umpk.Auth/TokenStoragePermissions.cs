using System.Runtime.Versioning;

namespace Umpk.Auth;

internal static class TokenStoragePermissions
{
    internal static void RestrictFile(string path)
    {
        if (!OperatingSystem.IsWindows())
            SetUnixMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

    }

    internal static void RestrictDirectory(string path)
    {
        if (!OperatingSystem.IsWindows())
            SetUnixMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

    }

    [UnsupportedOSPlatform("windows")]
    private static void SetUnixMode(string path, UnixFileMode mode) => File.SetUnixFileMode(path, mode);
}
