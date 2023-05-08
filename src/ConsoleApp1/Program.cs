using LanguageExt;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

            var f = SuccessEff((string value1, int value2, bool value3) => new SomeSetting(value1, value2));

            var eff = f.Apply(
                read("test", c.Configuration),
                readInt("test2", c.Configuration));

            var result = eff.Apply(readBool("hello", c.Configuration));

            var sett = result.Run();

        });

        var app = builder.Build();

        await app.StartAsync();
        await app.StopAsync();
    }

    public record SomeSetting(string Value1, int Value2);

    public static class SettingReader
    {
        public static Eff<SomeSetting> readSomeSetting(IConfiguration config, string baseSectionName) =>
            from value1 in read($"{baseSectionName}:Value1", config)
            from value2 in readInt($"{baseSectionName}:Value1", config)
            select new SomeSetting(value1, value2);
    }
}