using PristonTale.Web.Hosting;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
    EnvironmentName = Environments.Production
});

// Settings come only from the file beside the executable and the command line (AGENTS.md: no environment variables).
builder.Configuration.Sources.Clear();
builder.Configuration
    .AddJsonFile(WebSettings.BesideExecutable(WebSettings.FileName), optional: false, reloadOnChange: false)
    .AddCommandLine(args);

var settings = WebSettings.Read(builder.Configuration);
builder.AddPristonWeb(settings);

var app = builder.Build();
app.UsePristonWeb();
app.Run();
