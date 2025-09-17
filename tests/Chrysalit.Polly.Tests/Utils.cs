using Meziantou.Extensions.Logging.Xunit.v3;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Chrysalit.Polly.Tests;

internal static class Utils
{
    public static ILogger CreateLogger<TLogger>(ITestOutputHelper outputHelper)
    {
        var loggerfactory = LoggerFactory.Create(logbuilder =>
        {
            logbuilder.Services.AddSingleton<ILoggerProvider>(new XUnitLoggerProvider(outputHelper, appendScope: false));
        });
        return loggerfactory.CreateLogger<TLogger>();
    }
}
