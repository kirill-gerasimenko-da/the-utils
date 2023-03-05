using ConsoleApp1;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateDefaultBuilder();

        builder.ConfigureServices((c, s) =>
        {
            s.AddAllFunctions();
            s.AddSingleton<SomeService>();
        });

        var app = builder.Build();

        await app.StartAsync();

        await app.Services.GetService<SomeService>().SomeMethod();

        await app.StopAsync();
    }
}