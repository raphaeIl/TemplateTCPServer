# TCP Server Architecture Plan — DI + EF Core (Postgres)

> Goal: take the current raw-socket `GameServer` + ASP.NET `SDKServer` template and put
> them both on the **ASP.NET Core Generic Host** so that packet handlers get the same
> `Controller → Service → Repository → DbContext` dependency-injection story as a normal
> ASP.NET app — but driven by TCP packets instead of HTTP requests.
>
> Decisions locked in for this plan:
> - **Host model:** single Generic Host; the TCP listener runs as an `IHostedService`/`BackgroundService`.
> - **DI scoping:** one `IServiceScope` **per packet** (the analog of an HTTP request scope).
> - **Handler resolution:** keep `[PacketHandler(MsgId)]` attribute scanning to build a routing map at startup, but **register handlers in DI** and resolve them per-scope (no `Activator.CreateInstance`).
> - **Data layer:** new shared `TemplateTCPServer.Data` project, referenced by both servers.
> - **Layering:** `Handler → Service → Repository → DbContext`.
> - **SDKServer** = login/HTTP only. **GameServer** = the main server (the TCP side).

---

## 1. Where we are today

| Project | Type | Role | DI? |
|---|---|---|---|
| `TemplateTCPServer` | Exe | Entry point. Configures Serilog, then `Task.Run(GameServer.Start)` + `SDKServer.Main(args)`. | No |
| `TemplateTCPServer.GameServer` | Library | Raw `TcpListener`, manual `Connection` read loop. Singleton `GameServer.Instance`, singleton `PacketHandlerFactory` with reflection + `Activator.CreateInstance`. | No |
| `TemplateTCPServer.SDKServer` | Web (`Sdk.Web`) | ASP.NET Core. Login/config HTTP endpoints. Already has `EFCore.Design`. | Yes (its own `WebApplication`) |

Problems for the target design:

1. **Two entry points / no shared container.** `Program.Main` runs the GameServer on a thread and then hands control to `SDKServer.Main`, which builds its *own* `WebApplication`. There is no single DI container; the GameServer can't resolve services.
2. **Singletons + reflection instantiation.** `GameServer.Instance` and `PacketHandlerFactory` (Singleton) create handler instances via `Activator.CreateInstance`, so handlers **cannot take constructor dependencies** (`IUserService`, repositories, `DbContext`). This is the core thing blocking the layered DI pattern.
3. **No per-request lifetime.** EF Core `DbContext` must be short-lived and is not thread-safe. A TCP connection is long-lived and multiplexes many messages, so we need an explicit scope boundary — one per packet.
4. **`Connection` is hand-rolled** and reads raw `BinaryReader.ReadString()`; there is no packet framing/`MsgId` dispatch wired to the factory yet.

---

## 2. Target project layout

```
TemplateTCPServer.sln
│
├── TemplateTCPServer            (Exe — composition root / host bootstrap)
│     Program.cs                 → builds ONE Generic Host, wires all DI, runs it
│
├── TemplateTCPServer.Common     (NEW, Library — protocol + cross-cutting)
│     Protocol/MsgId.cs
│     Protocol/BasePacket.cs
│     Protocol/IPacketSerializer.cs
│     Utils/...                  (move Singleton<T> here only if still needed elsewhere)
│
├── TemplateTCPServer.Data       (NEW, Library — EF Core persistence)
│     Entities/User.cs, ...
│     AppDbContext.cs
│     Repositories/IUserRepository.cs, UserRepository.cs
│     Repositories/IRepository.cs (generic base, optional)
│     UnitOfWork/IUnitOfWork.cs, UnitOfWork.cs (optional)
│     DependencyInjection.cs     → AddDataLayer(this IServiceCollection, config)
│     Migrations/                (EF migrations live here; Data is the migrations assembly)
│
├── TemplateTCPServer.GameServer (Library — the MAIN TCP server)
│     Hosting/GameServerHostedService.cs   → BackgroundService, owns TcpListener
│     Networking/Connection.cs             → per-connection read loop (no singletons)
│     Networking/ConnectionManager.cs      → tracks live connections (singleton)
│     Packets/PacketHandlerAttribute.cs    → (kept, improved namespace)
│     Packets/IPacketHandler.cs            → marker + base
│     Packets/PacketDispatcher.cs          → replaces PacketHandlerFactory; uses DI scopes
│     Packets/PacketHandlerRegistry.cs     → builds MsgId→Type map at startup
│     Handlers/...                         → concrete handlers (the "controllers")
│     Services/...                         → game business logic (the "services")
│     DependencyInjection.cs               → AddGameServer(this IServiceCollection)
│
└── TemplateTCPServer.SDKServer  (Web — login/HTTP only)
      Controllers/SDKController.cs
      Services/IAuthService.cs, AuthService.cs   (login logic)
      DependencyInjection.cs                      → AddSdkServer(...)
```

Reference graph (no cycles):

```
TemplateTCPServer (Exe)
   ├─> GameServer ─┐
   ├─> SDKServer ──┤
   └─> Data <──────┤  (both servers reference Data)
GameServer/SDKServer/Data ──> Common
```

---

## 3. The big idea: one Generic Host, TCP listener as a hosted service

ASP.NET Core's DI, configuration, logging, and `IHostedService` lifecycle all live on
the **Generic Host** (`Microsoft.Extensions.Hosting`). A `WebApplication` *is* a generic
host with HTTP layered on top. So we build a single host that:

- registers the **HTTP pipeline** (SDKServer controllers) — for login, and
- registers the **TCP listener** as a `BackgroundService` (GameServer) — the main server,

…both sharing the **same `IServiceProvider`**, the same `AppDbContext` registration, the
same Serilog logger, and the same `appsettings.json`.

### 3.1 `Program.cs` (composition root)

```csharp
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Serilog;
using TemplateTCPServer.Data;
using TemplateTCPServer.GameServer;
using TemplateTCPServer.SDKServer;

var builder = WebApplication.CreateBuilder(args);

// ---- logging (Serilog) -------------------------------------------------
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console()
    .WriteTo.File("logs/log.txt", shared: true));

// Kestrel sync IO flag preserved from the old SDKServer
builder.Services.Configure<KestrelServerOptions>(o => o.AllowSynchronousIO = true);

// ---- data layer (EF Core + Postgres) ----------------------------------
builder.Services.AddDataLayer(builder.Configuration);   // AddDbContext + repositories

// ---- HTTP side (login / SDK) -------------------------------------------
builder.Services.AddSdkServer();                        // controllers + IAuthService

// ---- TCP side (main game server) ---------------------------------------
builder.Services.AddGameServer(builder.Configuration);  // hosted service + handlers + dispatcher

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseAuthorization();
app.MapControllers();

// host.Run() starts BOTH the Kestrel HTTP server AND the
// GameServerHostedService (the TCP listener) under one lifecycle.
app.Run();
```

Key point: there is **no more `Task.Run(GameServer.Instance.Start)`** and **no second
`Main`**. The host starts the TCP `BackgroundService` for us, and shuts it down cleanly
on Ctrl-C / SIGTERM via the `stoppingToken`.

---

## 4. EF Core data layer (`TemplateTCPServer.Data`)

### 4.1 Packages (add to `TemplateTCPServer.Data.csproj`)

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.*" />
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="8.0.*" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.*">
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
```

(Move the EFCore.Design reference out of SDKServer; Data becomes the migrations assembly.)

### 4.2 Entity + DbContext

```csharp
public class User
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.Username).IsUnique();
        });
    }
}
```

### 4.3 Repository pattern

```csharp
public interface IUserRepository
{
    Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
}

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _db;
    public UserRepository(AppDbContext db) => _db = db;   // DbContext injected, scoped

    public Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default)
        => _db.Users.SingleOrDefaultAsync(u => u.Username == username, ct);

    public async Task AddAsync(User user, CancellationToken ct = default)
    {
        await _db.Users.AddAsync(user, ct);
        await _db.SaveChangesAsync(ct);
    }
}
```

> Optional `IUnitOfWork` if you want services to control the transaction/`SaveChanges`
> boundary instead of repositories calling `SaveChanges` themselves. For a template,
> per-repo `SaveChanges` is fine; note it as a future refinement.

### 4.4 DI extension — `AddDataLayer`

```csharp
public static class DependencyInjection
{
    public static IServiceCollection AddDataLayer(
        this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<AppDbContext>(opt =>
            opt.UseNpgsql(
                config.GetConnectionString("Postgres"),
                npg => npg.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));

        services.AddScoped<IUserRepository, UserRepository>();
        // services.AddScoped<IUnitOfWork, UnitOfWork>();
        return services;
    }
}
```

`AddDbContext<T>` registers `AppDbContext` as **Scoped** by default — exactly what we want:
one `DbContext` per packet scope (Section 6).

### 4.5 Connection string (`appsettings.json`)

```json
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5432;Database=ntr;Username=postgres;Password=postgres"
  }
}
```

### 4.6 Migrations

```
dotnet ef migrations add InitialCreate -p TemplateTCPServer.Data -s TemplateTCPServer
dotnet ef database update            -p TemplateTCPServer.Data -s TemplateTCPServer
```

`-p` = migrations project (Data), `-s` = startup project (the Exe that has the host +
connection string). Optionally apply migrations on boot in dev with `db.Database.Migrate()`.

---

## 5. Packet handler system — adapting your factory to DI

Your current `PacketHandlerFactory` does two jobs: (a) **discover** handlers via reflection
and build a `MsgId → MethodInfo` map, and (b) **instantiate + invoke** them via
`Activator.CreateInstance`. We keep (a), and replace (b) with DI resolution. Split into
two types:

### 5.1 `PacketHandlerRegistry` — build the routing map once at startup (singleton)

Reflection is used **only** to discover the `MsgId → handler type/method` mapping. No
instances are created here.

```csharp
public sealed class PacketHandlerRegistry
{
    // MsgId -> (handler CLR type, the [PacketHandler] method on it)
    private readonly Dictionary<MsgId, HandlerEntry> _map = new();

    public PacketHandlerRegistry(IEnumerable<Assembly> handlerAssemblies)
    {
        foreach (var type in handlerAssemblies
                     .SelectMany(a => a.GetTypes())
                     .Where(t => typeof(IPacketHandler).IsAssignableFrom(t)
                                 && t is { IsInterface: false, IsAbstract: false }))
        {
            foreach (var method in type.GetMethods())
            {
                var attr = method.GetCustomAttribute<PacketHandlerAttribute>(false);
                if (attr is null) continue;
                if (!_map.TryAdd(attr.MsgId, new HandlerEntry(type, method)))
                    Log.Warning("Duplicate handler for {MsgId}", attr.MsgId);
                else
                    Log.Information("Mapped {MsgId} -> {Type}.{Method}",
                        attr.MsgId, type.Name, method.Name);
            }
        }
    }

    public bool TryGet(MsgId id, out HandlerEntry entry) => _map.TryGetValue(id, out entry!);

    public IEnumerable<Type> HandlerTypes => _map.Values.Select(e => e.Type).Distinct();
}

public readonly record struct HandlerEntry(Type Type, MethodInfo Method);
```

### 5.2 `IPacketHandler` — give it a real contract

Two viable styles; pick one for the template:

**Style A — attribute on methods (keeps your current model, multiple MsgIds per class).**
The dispatcher uses the registry's `MethodInfo` and resolves the declaring type from DI:

```csharp
public interface IPacketHandler { }   // marker, as today

public class AuthHandler : IPacketHandler
{
    private readonly IAuthService _auth;          // ← constructor injection now works
    public AuthHandler(IAuthService auth) => _auth = auth;

    [PacketHandler(MsgId.Login)]
    public async Task Login(Connection conn, BasePacket packet)
    {
        var req = packet.As<LoginRequest>();
        var result = await _auth.LoginAsync(req.Username, req.Password);
        conn.Send(result.ToPacket());
    }
}
```

**Style B — one handler class per MsgId, strongly typed (more idiomatic DI).**

```csharp
public interface IPacketHandler<TPacket> where TPacket : BasePacket
{
    Task HandleAsync(Connection conn, TPacket packet, CancellationToken ct);
}

[PacketHandler(MsgId.Login)]
public class LoginHandler : IPacketHandler<LoginRequest>
{
    private readonly IAuthService _auth;
    public LoginHandler(IAuthService auth) => _auth = auth;
    public Task HandleAsync(Connection conn, LoginRequest p, CancellationToken ct) => ...;
}
```

> Recommendation: **Style A** keeps your existing attribute-on-method ergonomics (the
> thing you said to preserve), and the only change vs today is that the instance comes
> from `scope.ServiceProvider` instead of `Activator.CreateInstance`. The plan's snippets
> below use Style A.

### 5.3 `PacketDispatcher` — resolve per scope and invoke (singleton)

This replaces `PacketHandlerFactory.InvokePacketHandler`. It is a **singleton** (cheap,
stateless), but it **creates a scope per packet** and resolves the handler from that scope,
so the handler and everything it injects (`IAuthService`, `IUserRepository`, `AppDbContext`)
are scoped to the single packet.

```csharp
public sealed class PacketDispatcher
{
    private readonly IServiceProvider _root;        // the application's root provider
    private readonly PacketHandlerRegistry _registry;

    public PacketDispatcher(IServiceProvider root, PacketHandlerRegistry registry)
    {
        _root = root;
        _registry = registry;
    }

    public async Task DispatchAsync(Connection conn, BasePacket packet, CancellationToken ct)
    {
        if (!_registry.TryGet(packet.MsgId, out var entry))
        {
            Log.Warning("No handler for {MsgId}; dropped", packet.MsgId);
            return;
        }

        // ── one DI scope per packet == the "request scope" ──────────────
        await using var scope = _root.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService(entry.Type); // DI-built handler

        try
        {
            var result = entry.Method.Invoke(handler, new object[] { conn, packet });
            if (result is Task task) await task;     // support async handlers
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Handler {Type} failed for {MsgId}", entry.Type.Name, packet.MsgId);
        }
        // scope disposed here -> DbContext disposed -> connection returned to pool
    }
}
```

### 5.4 DI extension — `AddGameServer`

```csharp
public static class DependencyInjection
{
    public static IServiceCollection AddGameServer(
        this IServiceCollection services, IConfiguration config)
    {
        // routing map built once from the GameServer assembly (and any others)
        var registry = new PacketHandlerRegistry(new[] { typeof(DependencyInjection).Assembly });
        services.AddSingleton(registry);

        // register every discovered handler type as SCOPED so it can take scoped deps
        foreach (var t in registry.HandlerTypes)
            services.AddScoped(t);

        services.AddSingleton<PacketDispatcher>();
        services.AddSingleton<ConnectionManager>();

        // game business-logic services (scoped — they use repositories/DbContext)
        services.AddScoped<IInventoryService, InventoryService>();
        // ... more game services

        // the TCP listener itself
        services.AddHostedService<GameServerHostedService>();
        return services;
    }
}
```

---

## 6. Lifetimes — the rule that makes this safe

| Component | Lifetime | Why |
|---|---|---|
| `GameServerHostedService` (TCP listener) | Singleton (hosted) | One listener for the process. |
| `ConnectionManager` | Singleton | Global registry of live connections. |
| `Connection` | **Not in DI** — `new`'d per socket | Owns the socket + read loop; lives as long as the client. |
| `PacketDispatcher`, `PacketHandlerRegistry` | Singleton | Stateless routing; cheap. |
| **Per-packet scope** | `CreateAsyncScope()` per message | The "request" boundary. |
| Packet handlers (`AuthHandler`, …) | **Scoped** | Resolved inside the per-packet scope; can inject scoped deps. |
| Services (`IAuthService`, game services) | **Scoped** | Business logic, use repositories. |
| Repositories (`IUserRepository`) | **Scoped** | Wrap `DbContext`. |
| `AppDbContext` | **Scoped** | Short-lived, not thread-safe → must not outlive one packet. |

**Critical anti-pattern to avoid:** do **not** inject `AppDbContext`, a repository, or any
scoped service directly into the singleton `Connection`/listener/dispatcher constructors.
Captive-dependency: a scoped `DbContext` captured by a singleton would be shared across all
connections and never disposed. Always go through `CreateAsyncScope()` per packet (which
the dispatcher does). The DI container will actually throw on this if validation is on —
keep `builder.Host.UseDefaultServiceProvider(o => o.ValidateScopes = true)` in dev.

---

## 7. The connection read loop (`Connection` + hosted service)

### 7.1 `GameServerHostedService`

Replaces the singleton `GameServer` + `Task.Run`. Owns the `TcpListener`, accepts clients,
and hands each to a `Connection`. It receives the **dispatcher** and **manager** by
constructor injection (both singletons → safe).

```csharp
public sealed class GameServerHostedService : BackgroundService
{
    private readonly PacketDispatcher _dispatcher;
    private readonly ConnectionManager _connections;
    private readonly TcpListener _listener;
    private readonly ILogger<GameServerHostedService> _log;

    public GameServerHostedService(
        PacketDispatcher dispatcher, ConnectionManager connections,
        IConfiguration config, ILogger<GameServerHostedService> log)
    {
        _dispatcher = dispatcher;
        _connections = connections;
        _log = log;
        var port = config.GetValue("GameServer:Port", 6969);
        _listener = new TcpListener(IPAddress.Any, port);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _listener.Start();
        _log.LogInformation("GameServer listening on {Port}",
            ((IPEndPoint)_listener.LocalEndpoint).Port);

        while (!stoppingToken.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync(stoppingToken);
            var conn = new Connection(client, _dispatcher, _connections);
            _ = conn.RunAsync(stoppingToken);     // fire-and-forget per connection
        }
    }

    public override Task StopAsync(CancellationToken ct)
    {
        _listener.Stop();
        return base.StopAsync(ct);
    }
}
```

### 7.2 `Connection` — read, frame, dispatch (no singletons, no DI of scoped deps)

```csharp
public sealed class Connection
{
    private readonly TcpClient _client;
    private readonly PacketDispatcher _dispatcher;
    private readonly NetworkStream _stream;
    public string Id { get; }

    public Connection(TcpClient client, PacketDispatcher dispatcher, ConnectionManager mgr)
    {
        _client = client;
        _dispatcher = dispatcher;
        _stream = client.GetStream();
        Id = client.Client.RemoteEndPoint!.ToString()!;
        mgr.Add(this);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (_client.Connected && !ct.IsCancellationRequested)
            {
                // 1. read framed bytes off the wire (length-prefixed, see §8)
                BasePacket? packet = await ReadPacketAsync(ct);
                if (packet is null) break;

                // 2. hand to dispatcher -> creates per-packet scope -> handler
                await _dispatcher.DispatchAsync(this, packet, ct);
            }
        }
        catch (Exception ex) { Log.Warning(ex, "{Id} read loop ended", Id); }
        finally { _client.Close(); }
    }

    public ValueTask Send(BasePacket packet) => /* serialize + write to _stream */;
    private Task<BasePacket?> ReadPacketAsync(CancellationToken ct) => /* framing + deserialize */;
}
```

> The current code reads with `BinaryReader.ReadString()` and busy-polls `DataAvailable`.
> Replace both: use async stream reads (`ReadExactlyAsync`) and **length-prefixed framing**
> so a `BasePacket` is fully assembled before dispatch. See §8.

---

## 8. Protocol / framing (in `TemplateTCPServer.Common`)

You referenced `MsgId`, `BasePacket`, and `IPacketHandler` from `NTRSimulator.Common.Protocol`.
Define the protocol surface in the new `Common` project so all projects share it:

- `enum MsgId { ... }` — message identifiers.
- `abstract class BasePacket { MsgId MsgId { get; } ... T As<T>() }` — base envelope + helper to read the typed body.
- `IPacketSerializer` — turns bytes ⇄ `BasePacket`. Pick JSON (you already ship Newtonsoft) or a binary scheme.
- **Framing:** `[4-byte length][2-byte MsgId][payload]`. `Connection.ReadPacketAsync` reads the length, then `ReadExactlyAsync(length)`, then deserializes. This removes the `DataAvailable` busy-loop and the `ReadString` ambiguity.

---

## 9. SDKServer (login) under the shared host

**`SDKServer.Main` is deleted entirely.** There is exactly one `Main` — in the
`TemplateTCPServer` startup project (§3.1). The SDKServer project becomes a plain class
library of controllers + login services with **no entry point of its own**. The startup
project's host pulls those controllers in as an *application part*.

### 9.1 Minimal form (what you proposed) — register the controllers directly

In the startup project's `Main`, register the SDKServer assembly's controllers straight
onto the shared host:

```csharp
// in TemplateTCPServer/Program.cs (the ONLY Main)
builder.Services
    .AddControllers()
    .AddApplicationPart(typeof(TemplateTCPServer.SDKServer.SDKServer).Assembly);
```

`AddApplicationPart` tells MVC to scan that assembly for `[ApiController]` types, so
`SDKController` is discovered and mapped by `app.MapControllers()` even though it lives in
a referenced library. This is the whole reason it worked before — the old `SDKServer.Main`
called the same thing on its own builder; now it runs on the shared builder instead.

> Note: pick any type that lives in the SDKServer assembly as the `typeof(...)` anchor.
> Today that's the `SDKServer` class itself; once you remove its `Main` you can keep the
> class as an empty marker, or just anchor on `typeof(SDKController)` and delete the
> `SDKServer` class outright.

### 9.2 Cleaner form (optional) — wrap it in an `AddSdkServer` extension

Functionally identical, but keeps the startup `Main` tidy and co-locates SDK service
registrations (`IAuthService`) with the controllers they back:

```csharp
// in TemplateTCPServer.SDKServer/DependencyInjection.cs
public static class DependencyInjection
{
    public static IMvcBuilder AddSdkServer(this IServiceCollection s)
    {
        s.AddScoped<IAuthService, AuthService>();       // shared login logic
        return s.AddControllers()
                .AddApplicationPart(typeof(SDKController).Assembly);
    }
}

// in Program.cs
builder.Services.AddSdkServer();
```

Either way: because `SDKController` now lives in the **same container**, it can inject the
same `IAuthService` / `IUserRepository` the GameServer uses. `AuthService` (scoped) depends
on `IUserRepository` (scoped) → `AppDbContext` (scoped). HTTP requests already create a
scope per request automatically, so the `Handler→Service→Repo→DbContext` chain works
unchanged on the HTTP side; the TCP side reproduces that scope manually via the dispatcher.

> ⚠️ Watch the namespace/type collision: the project has a class `SDKServer` inside
> namespace `TemplateTCPServer.SDKServer`. Referencing `typeof(SDKServer)` from
> `Program.cs` needs the full path `TemplateTCPServer.SDKServer.SDKServer` (or anchor on
> `SDKController` to avoid the awkwardness). Removing the now-empty `SDKServer` class after
> deleting its `Main` sidesteps this.

---

## 10. End-to-end flow (the payoff)

**HTTP login (SDKServer):**
```
POST /login → SDKController (scope auto-created by ASP.NET)
   → IAuthService.LoginAsync
      → IUserRepository.GetByUsernameAsync
         → AppDbContext (scoped, disposed at end of request)
```

**TCP login packet (GameServer):**
```
bytes on socket → Connection.ReadPacketAsync → BasePacket{MsgId.Login}
   → PacketDispatcher.DispatchAsync  ── creates IServiceScope (the request boundary) ──
      → AuthHandler (resolved from scope)
         → IAuthService.LoginAsync
            → IUserRepository.GetByUsernameAsync
               → AppDbContext (scoped, disposed when scope disposes)
   → Connection.Send(LoginResult)
```

Same layered chain, same DI semantics, two transports.

---

## 11. Migration steps (suggested order)

1. **Create `TemplateTCPServer.Common`**; move/define `MsgId`, `BasePacket`, `IPacketHandler`, serializer + framing. Have GameServer/SDKServer/Data reference it.
2. **Create `TemplateTCPServer.Data`**; add Npgsql + EFCore packages, `AppDbContext`, `User`, `IUserRepository`/`UserRepository`, `AddDataLayer`. Add connection string to `appsettings.json`. Generate `InitialCreate` migration.
3. **Rework GameServer**: add `GameServerHostedService` (delete singleton `GameServer`), rewrite `Connection` (async framing, no `Activator`), add `PacketHandlerRegistry` + `PacketDispatcher` (replace `PacketHandlerFactory`), add `AddGameServer`. Keep `PacketHandlerAttribute` as-is.
4. **Rework SDKServer**: **delete `SDKServer.Main`** (the startup project's `Main` is now the only entry point); register its controllers on the shared host via `AddControllers().AddApplicationPart(typeof(SDKController).Assembly)` — directly in `Program.cs` (§9.1) or wrapped in `AddSdkServer` (§9.2); move login logic into `IAuthService`. Optionally delete the now-empty `SDKServer` class.
5. **Rewrite `Program.cs`** as the single composition root (Section 3.1). Delete the second `Main` invocation and the `Task.Run`.
6. **Wire a sample handler** (`AuthHandler` for `MsgId.Login`) that goes all the way to the DB, to prove the chain.
7. **Turn on scope validation** in dev (`ValidateScopes = true`) to catch captive dependencies.

---

## 12. Things to watch / future refinements

- **Captive dependencies**: never inject scoped services into the singleton listener/dispatcher/connection. Only the per-packet scope touches `DbContext`. (Section 6.)
- **`AllowSynchronousIO`**: was needed for the old `BinaryReader`/`Results.Text` sync paths. Once `Connection` is fully async you may be able to drop it.
- **`MethodInfo.Invoke` reflection cost**: fine to start; if it shows up in profiling, compile delegates (`Delegate.CreateDelegate` / expression trees) once in the registry and cache them.
- **UnitOfWork / transactions**: per-repo `SaveChanges` is fine for a template; introduce `IUnitOfWork` when a handler needs multiple repos in one transaction.
- **Connection-scoped state** (authenticated user, session): store on the `Connection` object (singleton-per-socket), not in the DI scope. The DI scope is per-packet and must not hold session state.
- **Graceful shutdown**: the hosted service's `stoppingToken` already stops accepting; also iterate `ConnectionManager` to close live sockets in `StopAsync`.
- **Backpressure / ordering**: dispatching each packet on the connection's read loop processes them sequentially per connection (good for ordering). If a handler is slow, consider a per-connection channel/queue rather than blocking the read loop.
