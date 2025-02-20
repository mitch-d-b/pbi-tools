// Copyright (c) Mathias Thierbach
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.IO;

namespace PbiTools.Utils
{
    public static class ApplicationFolders
    {
        private const string LOCALAPPDATA = "%LOCALAPPDATA%";

        public static string AppDataFolder => Path.Combine(
            Environment.ExpandEnvironmentVariables(LOCALAPPDATA),
            "pbi-tools");

        internal static string GetShadowCopyDir(string winAppHandle) => Path.Combine(AppDataFolder, winAppHandle);

    }
}