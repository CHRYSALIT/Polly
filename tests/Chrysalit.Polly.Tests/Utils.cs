using Meziantou.Extensions.Logging.Xunit.v3;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Chrysalit.Polly.Tests;

/// <summary>
/// Utility methods for test infrastructure.
/// </summary>
internal static class Utils
{
    /// <summary>
    /// Creates a logger that outputs to the xUnit test output.
    /// 
    /// This allows test methods to log diagnostic information that appears
    /// in the test output, useful for debugging retry behavior and connection management.
    /// </summary>
    /// <typeparam name="TLogger">The type to use for logger category.</typeparam>
    /// <param name="outputHelper">The xUnit output helper for capturing logs.</param>
    /// <returns>A configured logger instance.</returns>
    public static ILogger CreateLogger<TLogger>(ITestOutputHelper outputHelper)
    {
        var loggerfactory = LoggerFactory.Create(logbuilder =>
        {
            logbuilder.Services.AddSingleton<ILoggerProvider>(new XUnitLoggerProvider(outputHelper, appendScope: false));
        });
        return loggerfactory.CreateLogger<TLogger>();
    }
}
