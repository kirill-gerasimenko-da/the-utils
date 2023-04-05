using ConsoleApp1;
using LanguageExt;
using LanguageExt.Pipes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TheUtils.DependencyInjection;
using static LanguageExt.Prelude;
using static TheUtils.ConfigurationExtensions;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateDefaultBuilder();

        builder.ConfigureServices((c, s) =>
        {
            var config = 
                from test in read("test", c.Configuration)
                from test2 in read("test2", c.Configuration)
                select new { test, test2 };
            
            var val = config.Run();
            
            s.AddSingleton<SomeService>();
            s.AddGetLatestUserFunction();
            s.AddStartJobFunction();
            s.AddDeleteUserFunction();
        });

        var app = builder.Build();

        await app.StartAsync();

        await app.Services.GetService<SomeService>().SomeMethod();

        await app.StopAsync();
    }
}