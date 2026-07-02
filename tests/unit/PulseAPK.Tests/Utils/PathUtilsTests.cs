using System;
using System.IO;
using PulseAPK.Core.Utils;
using Xunit;

namespace PulseAPK.Tests.Utils
{
    public class PathUtilsTests
    {
        [Fact]
        public void GetDefaultReportsPath_ShouldEndWithReportsDirectory()
        {
            var path = PathUtils.GetDefaultReportsPath();

            Assert.False(string.IsNullOrWhiteSpace(path));
            Assert.EndsWith($"PulseAPK{Path.DirectorySeparatorChar}reports", path, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void GetDefaultWorkspaceRoot_ShouldResolveWritableDirectory()
        {
            var path = PathUtils.GetDefaultWorkspaceRoot();

            Assert.False(string.IsNullOrWhiteSpace(path));
            Assert.True(Directory.Exists(path));
        }

        [Fact]
        public void GetDefaultDecompilePath_ShouldUseWorkspaceDirectory()
        {
            var workspaceRoot = PathUtils.GetDefaultWorkspaceRoot();
            var path = PathUtils.GetDefaultDecompilePath();

            Assert.False(string.IsNullOrWhiteSpace(path));
            Assert.Equal(Path.Combine(workspaceRoot, "decompiled"), path);
        }

        [Fact]
        public void GetDefaultCompiledPath_ShouldUseWorkspaceDirectory()
        {
            var workspaceRoot = PathUtils.GetDefaultWorkspaceRoot();
            var path = PathUtils.GetDefaultCompiledPath();

            Assert.False(string.IsNullOrWhiteSpace(path));
            Assert.Equal(Path.Combine(workspaceRoot, "compiled"), path);
        }
    }
}
