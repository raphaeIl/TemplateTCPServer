using Microsoft.AspNetCore.Server.Kestrel.Core;
using NTRSimulator.Common.Table;
using Serilog;
using TemplateTCPServer.Data;
using TemplateTCPServer.GameServer;
using TemplateTCPServer.SDKServer;
using TemplateTCPServer.Table;

namespace TemplateTCPServer
{
    internal static class Program
    {
        public static void Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .CreateBootstrapLogger();

            try
            {
                Log.Information("Starting host...");

                var builder = WebApplication.CreateBuilder(args);

                builder.Host.UseSerilog((ctx, services, cfg) => cfg
                    .ReadFrom.Configuration(ctx.Configuration)
                    .WriteTo.Console());

                // Catch captive-dependency mistakes (scoped resolved from root) in dev.
                builder.Host.UseDefaultServiceProvider((ctx, opt) =>
                {
                    opt.ValidateScopes = ctx.HostingEnvironment.IsDevelopment();
                    opt.ValidateOnBuild = ctx.HostingEnvironment.IsDevelopment();
                });

                builder.Services.Configure<KestrelServerOptions>(o => o.AllowSynchronousIO = true);

                // ---- shared data layer (EF Core + Postgres) ----
                builder.Services.AddDataLayer(builder.Configuration);

                // ---- HTTP side: SDK login/config ----
                builder.Services.AddSdkServer();

                // ---- table loader: protobuf .bytes file loading + caching ----
                builder.Services.AddTableService();
                // ---- TCP side: the main game server (hosted background service) ----
                builder.Services.AddGameServer();

                var app = builder.Build();

                app.UseSerilogRequestLogging();
                app.UseAuthorization();
                app.MapControllers();

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
    }
}
