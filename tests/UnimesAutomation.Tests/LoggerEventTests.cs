using System;
using System.Collections.Generic;
using System.IO;
using UnimesAutomation;
using Xunit;

public class LoggerEventTests
{
    [Fact]
    public void LineWritten_fires_with_formatted_line()
    {
        var path = Path.Combine(Path.GetTempPath(), $"unimes_log_{Guid.NewGuid():N}.log");
        var captured = new List<string>();
        using (var logger = new SimpleLogger(path))
        {
            logger.LineWritten += line => captured.Add(line);
            logger.Info("hello");
            logger.Warn("careful");
        }
        try
        {
            Assert.Equal(2, captured.Count);
            Assert.Contains("[INFO] hello", captured[0]);
            Assert.Contains("[WARN] careful", captured[1]);
        }
        finally { File.Delete(path); }
    }
}
