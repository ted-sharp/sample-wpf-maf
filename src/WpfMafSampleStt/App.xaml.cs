using System;
using System.Windows;
using Microsoft.Extensions.Configuration;

namespace WpfMafSampleStt;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var baseDir = AppContext.BaseDirectory;
        var config = new ConfigurationBuilder()
            .SetBasePath(baseDir)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false)
            .Build();

        // 先に new でデフォルト値入りインスタンスを作り、その上に config を Bind で流し込む。
        // config.Get<AppSettings>() でも書けるが、それだと JSON が空の項目を null で潰す可能性があり、
        // AppSettings のプロパティ初期化子 (例: Endpoint = "http://localhost:1234/v1") が活きない。
        // この順序にすることで「JSON にあるキーだけ上書き、無いキーは C# 側デフォルトを温存」を実現している。
        Settings = new AppSettings();
        config.Bind(Settings);
    }
}
