﻿namespace XIVLauncher.Common
{
    public static class EnvironmentSettings
    {
        public static bool IsWine => CheckEnvBool("XL_WINEONLINUX");
        public static bool IsHardwareRendered => CheckEnvBool("XL_HWRENDER");
        public static bool IsDisableUpdates => CheckEnvBool("XL_NOAUTOUPDATE");
        public static bool IsPreRelease => CheckEnvBool("XL_PRERELEASE");
        public static bool IsNoRunas => CheckEnvBool("XL_NO_RUNAS");
        private static bool CheckEnvBool(string var) => bool.Parse(System.Environment.GetEnvironmentVariable(var) ?? "false");
    }
}