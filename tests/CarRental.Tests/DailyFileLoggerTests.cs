using Microsoft.Extensions.Logging;
using Xunit;

namespace CarRental.Tests;

public sealed class DailyFileLoggerTests
{
    [Fact]
    public void Writes_log_entries_to_a_daily_file()
    {
        var logDirectory = Path.Combine(Path.GetTempPath(), "CarRentalTests", Guid.NewGuid().ToString("N"));

        try
        {
            using var provider = new DailyFileLoggerProvider(logDirectory);
            var logger = provider.CreateLogger("CarRental.Tests.Logging");

            logger.LogWarning("Test warning {Code}", 42);
            logger.LogError(new InvalidOperationException("test exception"), "Test error");

            var expectedFile = Path.Combine(logDirectory, $"car-rental-{DateTimeOffset.Now:yyyy-MM-dd}.log");
            Assert.True(File.Exists(expectedFile));

            var content = File.ReadAllText(expectedFile);
            Assert.Contains("[Warning] CarRental.Tests.Logging: Test warning 42", content);
            Assert.Contains("[Error] CarRental.Tests.Logging: Test error", content);
            Assert.Contains("System.InvalidOperationException: test exception", content);
        }
        finally
        {
            if (Directory.Exists(logDirectory))
                Directory.Delete(logDirectory, recursive: true);
        }
    }
}
