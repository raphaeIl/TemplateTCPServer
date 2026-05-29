using Microsoft.AspNetCore.Server.Kestrel.Core;
using Serilog;
using Serilog.Events;
using TemplateTCPServer.Data;
using TemplateTCPServer.GameServer;
using TemplateTCPServer.SDKServer;

namespace TemplateTCPServer
{
    /// <summary>
    /// The single composition root for the whole application. One generic host runs both
    /// the SDK HTTP pipeline (login) and the GameServer TCP listener (the main server, as a
    /// hosted background service), sharing one DI container, one EF Core context
    /// registration, one logger, and one configuration.
    /// </summary>
    internal static class Program
    {
        public static void Main(string[] args)
        {
            RotateLogFile();

            // Bootstrap logger so startup failures before the host is built still log.
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .CreateBootstrapLogger();

            try
            {
                Log.Information("Starting host...");

                var builder = WebApplication.CreateBuilder(args);

                // Serilog as the host logger, reading sinks/levels from configuration.
                builder.Host.UseSerilog((ctx, services, cfg) => cfg
                    .ReadFrom.Configuration(ctx.Configuration)
                    .WriteTo.Console()
                    .WriteTo.File(LogFilePath, restrictedToMinimumLevel: LogEventLevel.Verbose, shared: true));

                // Catch captive-dependency mistakes (scoped resolved from root) in dev.
                builder.Host.UseDefaultServiceProvider((ctx, opt) =>
                {
                    opt.ValidateScopes = ctx.HostingEnvironment.IsDevelopment();
                    opt.ValidateOnBuild = ctx.HostingEnvironment.IsDevelopment();
                });

                // Preserved from the original SDKServer (sync IO for the legacy controller paths).
                builder.Services.Configure<KestrelServerOptions>(o => o.AllowSynchronousIO = true);

                // ---- shared data layer (EF Core + Postgres) ----
                builder.Services.AddDataLayer(builder.Configuration);

                // ---- HTTP side: SDK login/config ----
                builder.Services.AddSdkServer();

                // ---- TCP side: the main game server (hosted background service) ----
                builder.Services.AddGameServer();

                var app = builder.Build();

                app.UseSerilogRequestLogging();
                app.UseAuthorization();
                app.MapControllers();

                // Runs the Kestrel HTTP server AND the GameServer TCP listener together.
                app.Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Host terminated unexpectedly");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        private static string LogFilePath =>
            Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory)!, "logs", "log.txt");

        /// <summary>Rotates the previous run's log to log-prev.txt (preserved behavior).</summary>
        private static void RotateLogFile()
        {
            var logFilePath = LogFilePath;
            if (!File.Exists(logFilePath))
                return;

            var prevLogFilePath = Path.Combine(Path.GetDirectoryName(logFilePath)!, "log-prev.txt");
            if (File.Exists(prevLogFilePath))
                File.Delete(prevLogFilePath);

            File.Move(logFilePath, prevLogFilePath);
        }
    }
}
