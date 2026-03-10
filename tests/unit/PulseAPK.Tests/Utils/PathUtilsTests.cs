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
    }
}
