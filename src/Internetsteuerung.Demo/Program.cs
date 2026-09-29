using Internetsteuerung.Core;
using Internetsteuerung.Demo;
using Internetsteuerung.Demo.Simulation;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Same SperrService as the web app. Only the ports to MariaDB, the OPNsense API and the lab
// clients are replaced by simulations that run in the browser.
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<SimFirewall>();
builder.Services.AddSingleton<IFirewallClient>(sp => sp.GetRequiredService<SimFirewall>());
builder.Services.AddSingleton<BrowserSpeicher>();
builder.Services.AddSingleton<IRaumRepository>(sp => sp.GetRequiredService<BrowserSpeicher>());
builder.Services.AddSingleton<ISperreSpeicher>(sp => sp.GetRequiredService<BrowserSpeicher>());
builder.Services.AddSingleton<IProtokoll>(sp => sp.GetRequiredService<BrowserSpeicher>());
builder.Services.AddSingleton<SimKlassenraum>();
builder.Services.AddSingleton<SperrService>();
builder.Services.AddSingleton<DemoSitzung>();
builder.Services.AddSingleton<DemoTakt>();

var host = builder.Build();
host.Services.GetRequiredService<DemoTakt>().Starten();
await host.RunAsync();
