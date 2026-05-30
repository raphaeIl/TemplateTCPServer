# TCP Server Architecture — DI + EF Core (Postgres)

> A TCP game server and an HTTP login server sharing **one ASP.NET Core Generic Host**, so
> packet handlers get the same `Controller → Service → Repository → DbContext`
> dependency-injection story as a normal ASP.NET app — driven by TCP packets instead of
> HTTP requests.
>
> Design decisions in effect:
> - **Host model:** a single Generic Host (`WebApplication`); the TCP listener runs as an `IHostedService`.
> - **Execution model:** **fully synchronous, thread-per-connection.** No `async`/`await`, no `Task<T>`, no `CancellationToken` on the app's own methods. Chosen because this is a low-population private server where the simplicity is worth the one parked thread per connected client. (See §7 for the trade-off.)
> - **DI scoping:** one `IServiceScope` **per packet** (the analog of an HTTP request scope).
> - **Handler resolution:** `[PacketHandler(MsgId)]` attribute scanning builds a routing map at startup, but handlers are **registered in DI** and resolved per-scope (no `Activator.CreateInstance`).
> - **Handler shape:** handlers are **proto-defined** — a `.proto` `service` is code-generated (by the `Raphael.Tcp.Tools` package, a forked `grpc_csharp_plugin`, §4.5) into an abstract `<Service>Base : IPacketHandler` you `override`. RPC methods take a **typed protobuf request** + `Connection` and **return** a typed response; the dispatcher (de)serializes and frames the reply. (gRPC ergonomics, raw-TCP transport — no HTTP/2, no `ServerCallContext`.)
> - **Layering:** `Handler → Service → Repository → DbContext`.
> - **SDKServer** = login/HTTP only. **GameServer** = the main server (the TCP side).

---

## 1. Project layout

```
TemplateTCPServer.sln
│
├── TemplateTCPServer            (Exe, Sdk.Web — composition root / host bootstrap)
│     Program.cs                 → builds ONE host, wires all DI, runs it
│     appsettings.json           → Postgres conn string, GameServer:Port, Kestrel, Serilog
│
├── TemplateTCPServer.Common     (Library — protocol; depends only on Google.Protobuf)
│     Protocol/MsgId.cs                    → enum (None, Ping, Pong)
│     Protocol/BasePacket.cs               → abstract envelope (MsgId + payload)
│     Protocol/RawPacket.cs                → minimal concrete packet
│     Protocol/IPacketSerializer.cs        → bytes ⇄ BasePacket body
│     Protocol/PassthroughPacketSerializer.cs → default no-op serializer
│     Protocol/PacketFramer.cs             → length-prefixed frame read/write (sync)
│     (no proto codegen here — the Raphael.Tcp.Tools package generates messages
│      AND the TCP base together in GameServer; see §4.5)
│
├── TemplateTCPServer.Data       (Library — EF Core persistence)
│     Core/AppDbContext.cs
│     Core/IRepository.cs, Core/Repository.cs   → generic repo base
│     Entities/Account.cs                        → sample entity
│     Repositories/AccountRepository.cs          → IAccountRepository + impl (one file)
│     DataExtensions.cs                          → AddDataLayer(this IServiceCollection, config)
│
├── TemplateTCPServer.GameServer (Library — the MAIN TCP server)
│     Hosting/GameServerHostedService.cs   → IHostedService, owns TcpListener
│     Networking/Connection.cs             → per-connection blocking read loop (not in DI)
│     Networking/ConnectionManager.cs      → tracks live connections (singleton)
│     Packets/PacketHandlerAttribute.cs    → [PacketHandler(MsgId, replyMsgId)]
│     Packets/IPacketHandler.cs            → marker interface
│     Packets/PacketHandlerRegistry.cs     → builds MsgId→(type,method,reqType,reply) map
│     Packets/PacketDispatcher.cs          → per-packet scope; parses request, frames reply
│     protos/*.proto                       → IDL drop folder (git-ignored); messages + bases
│                                            generated from here by Raphael.Tcp.Tools (§4.5)
│     Generated/PingTcp.cs                 → generated PingServiceBase (package plugin, §4.5)
│     Handlers/PingHandler.cs              → example handler: overrides PingServiceBase
│     Services/ExampleService.cs           → IExampleService + impl (example "service")
│     GameServerExtensions.cs              → AddGameServer(this IServiceCollection)
│
└── TemplateTCPServer.SDKServer  (Library — login/HTTP only, no Main)
      Controllers/SDKController.cs
      Services/AuthService.cs              → IAuthService + impl (one file)
      SdkServerExtensions.cs              → AddSdkServer(this IServiceCollection)
```

> The TCP codegen plugin is **not** in this repo. It's a fork of gRPC's C# plugin,
> published as the **`Raphael.Tcp.Tools`** NuGet package (the fork's source lives in a
> separate grpc repo). GameServer consumes it as a `<PackageReference>` — no local
> protoc, plugin binary, or grpc clone is needed to build. See §4.5.

Reference graph (no cycles):

```
TemplateTCPServer (Exe)
   ├─> GameServer ─┐
   ├─> SDKServer ──┤
   └─> Data <──────┤  (both servers reference Data)
GameServer/SDKServer/Data ──> Common
```

Each layer's DI registrations live in that layer's own `*Extensions` class (the .NET
convention, e.g. `MvcServiceCollectionExtensions`). `Program.cs` calls the three
`Add*` methods explicitly.

---

## 2. One Generic Host, TCP listener as a hosted service

A `WebApplication` *is* a Generic Host with HTTP layered on top. We build a single host
that registers both:

- the **HTTP pipeline** (SDKServer controllers) — for login, and
- the **TCP listener** as an `IHostedService` (GameServer) — the main server,

…sharing the **same `IServiceProvider`**, the same `AppDbContext` registration, the same
Serilog logger, and the same `appsettings.json`.

### 2.1 `Program.cs` (composition root)

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, services, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console());                              // console only; no file logging

// Catch captive-dependency mistakes (scoped resolved from root) in dev.
builder.Host.UseDefaultServiceProvider((ctx, opt) =>
{
    opt.ValidateScopes  = ctx.HostingEnvironment.IsDevelopment();
    opt.ValidateOnBuild = ctx.HostingEnvironment.IsDevelopment();
});

builder.Services.Configure<KestrelServerOptions>(o => o.AllowSynchronousIO = true);

builder.Services.AddDataLayer(builder.Configuration);   // ---- EF Core + Postgres
builder.Services.AddSdkServer();                        // ---- HTTP login/config
builder.Services.AddGameServer();                       // ---- TCP main server

var app = builder.Build();
app.UseSerilogRequestLogging();
app.UseAuthorization();
app.MapControllers();
app.Run();   // starts BOTH the Kestrel HTTP server AND the GameServer TCP listener
```

There is exactly **one `Main`** (this one). `app.Run()` starts the Kestrel HTTP server and
the `GameServerHostedService` together and shuts both down on Ctrl-C / SIGTERM.

---

## 3. EF Core data layer (`TemplateTCPServer.Data`)

### 3.1 Packages

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.11" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="8.0.11" />
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="8.0.11" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.11">
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
```

Data is the migrations assembly (EFCore.Design lives here, not in SDKServer).

### 3.2 Entity + DbContext (`Core/`)

```csharp
public class Account
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    public DbSet<Account> Accounts => Set<Account>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Account>(e =>
        {
            e.HasKey(a => a.Id);
            e.HasIndex(a => a.Username).IsUnique();
            e.Property(a => a.Username).IsRequired();
        });
    }
}
```

`Account` is a placeholder; replace/extend with the real domain model.

### 3.3 Repository pattern — **synchronous**

A generic base (`Core/IRepository.cs` + `Core/Repository.cs`) kept in separate files, and
per-entity repositories where the interface + impl share one file:

```csharp
// Core/IRepository.cs
public interface IRepository<T> where T : class
{
    T? GetById(long id);
    void Add(T entity);
    void Remove(T entity);
    int SaveChanges();
}

// Core/Repository.cs
public class Repository<T>(AppDbContext db) : IRepository<T> where T : class
{
    protected readonly AppDbContext Db = db;   // primary ctor param -> protected field for subclasses

    public virtual T? GetById(long id) => Db.Set<T>().Find(id);
    public virtual void Add(T entity)  => Db.Set<T>().Add(entity);
    public virtual void Remove(T entity) => Db.Set<T>().Remove(entity);
    public virtual int SaveChanges() => Db.SaveChanges();
}

// Repositories/AccountRepository.cs  (impl + interface together, class first)
public sealed class AccountRepository(AppDbContext db) : Repository<Account>(db), IAccountRepository
{
    public Account? GetByUsername(string username)
        => Db.Accounts.SingleOrDefault(a => a.Username == username);
    public int Count() => Db.Accounts.Count();
}

public interface IAccountRepository : IRepository<Account>
{
    Account? GetByUsername(string username);
    int Count();
}
```

All methods use EF Core's **synchronous** APIs (`Find`, `SingleOrDefault`, `Count`,
`SaveChanges`).

> Conventions used in this template: the generic base `IRepository`/`Repository` stay in
> separate files; an interface that has a single concrete implementation
> (`IAccountRepository`/`AccountRepository`, `IExampleService`/`ExampleService`,
> `IAuthService`/`AuthService`) is kept in one file, with the class first and the interface
> after it. Dependencies are injected via **primary constructors** (C# 12).

### 3.4 DI extension — `AddDataLayer`

```csharp
public static class DataExtensions
{
    public static IServiceCollection AddDataLayer(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<AppDbContext>(opt =>
            opt.UseNpgsql(
                config.GetConnectionString("Postgres"),
                npg => npg.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));

        services.AddScoped<IAccountRepository, AccountRepository>();
        return services;
    }
}
```

`AddDbContext<T>` registers `AppDbContext` as **Scoped** — one `DbContext` per packet
scope (§6).

### 3.5 Connection string (`appsettings.json`)

```json
{ "ConnectionStrings": { "Postgres": "Host=localhost;Port=5432;Database=templatetcp;Username=postgres;Password=postgres" } }
```

The DbContext registration is lazy, so the host boots fine without a reachable database;
queries fail only when actually executed.

### 3.6 Migrations

```
dotnet ef migrations add InitialCreate -p TemplateTCPServer.Data -s TemplateTCPServer
dotnet ef database update              -p TemplateTCPServer.Data -s TemplateTCPServer
```

`-p` = migrations project (Data), `-s` = startup project (the Exe with the host + conn string).

---

## 4. Packet handler system

Handlers are **proto-defined and code-generated**. A `.proto` `service` describes the RPC
surface; a *forked* `grpc_csharp_plugin` (see §4.5) generates an abstract
`<Service>Base : IPacketHandler` whose virtual methods carry the `[PacketHandler(...)]`
routing attribute and take **typed protobuf** request/response objects. You subclass the
base and `override` the RPCs — the gRPC "extend the base, override the rpcs" model, but
dispatched over the raw TCP listener instead of HTTP/2 (no `ServerCallContext`).

The routing logic is split in two: discovery (reflection, once at startup) and dispatch
(per packet, via DI). No `Activator.CreateInstance`.

### 4.1 `PacketHandlerRegistry` — build the routing map once (singleton)

Reflection discovers the `MsgId → HandlerEntry` mapping; no instances are created here. Two
details matter:

- It reads `[PacketHandler]` via `GetBaseDefinition()` so the attribute declared on the
  **generated abstract base** is found on the concrete **override** (the override itself
  carries no attribute), and binds to the most-derived `MethodInfo` so dispatch hits your code.
- Each entry captures the method's **request parameter type** (to parse the payload into) and
  the **reply `MsgId`** (to frame the return value).

```csharp
foreach (var method in type.GetMethods())
{
    var attr = method.GetCustomAttribute<PacketHandlerAttribute>(inherit: true)
               ?? method.GetBaseDefinition().GetCustomAttribute<PacketHandlerAttribute>(inherit: false);
    if (attr is null) continue;

    var requestType = method.GetParameters().Length > 0
        ? method.GetParameters()[0].ParameterType : null;
    _map.TryAdd(attr.MsgId, new HandlerEntry(type, method, requestType, attr.ReplyMsgId));
}

public readonly record struct HandlerEntry(Type Type, MethodInfo Method, Type? RequestType, MsgId ReplyMsgId);
```

(Abstract types are filtered out, so the generated `<Service>Base` is never itself
registered — only your concrete subclass is.)

### 4.2 Handlers — override the generated base with **typed** request/response

Subclass the generated `<Service>Base`, inject dependencies via a **primary constructor**,
and `override` each RPC. No `[PacketHandler]` attribute here — it's inherited from the base.
Methods are **synchronous**: they take `(TRequest request, Connection connection)` and
**return** the response message (the dispatcher sends it).

```csharp
public sealed class PingHandler(
    IExampleService example,                              // ← constructor injection works
    ILogger<PingHandler> logger) : PingService.PingServiceBase
{
    public override PongReply Ping(PingRequest request, Connection connection)
    {
        try
        {
            int accounts = example.CountAccounts();        // Service → Repository → DbContext
            logger.LogInformation("Ping from {Id} (accounts in db: {Count})", connection.Id, accounts);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ping from {Id} (db unavailable, replying anyway)", connection.Id);
        }
        return new PongReply();                            // dispatcher frames this as MsgId.Pong
    }
}
```

`PingHandler` is the bundled **example** demonstrating the full chain; the DB call is
guarded so it still replies when no database is configured. The generated base
(`PingServiceBase`) lands in `TemplateTCPServer.GameServer/Generated/PingTcp.cs`; the
message types (`PingRequest`, `PongReply`, …) in GameServer's `obj/` — both produced from
`protos/` by the `Raphael.Tcp.Tools` package at build time (§4.5), neither committed.

### 4.3 `PacketDispatcher` — parse, resolve per scope, invoke, reply (singleton)

A singleton (stateless), but for **each packet** it opens a fresh DI scope, resolves the
handler from it (so it and everything it injects — `IExampleService`, `IAccountRepository`,
`AppDbContext` — are scoped to a single packet), parses the payload into the handler's
protobuf request type, invokes the override, and frames the returned message as the reply.
This is the TCP-side equivalent of MVC's per-request controller activation + model binding.

```csharp
public void Dispatch(Connection connection, BasePacket packet)
{
    if (!registry.TryGet(packet.MsgId, out var entry))
    {
        logger.LogWarning("No handler for {MsgId}; packet dropped", packet.MsgId);
        return;
    }

    using var scope = rootProvider.CreateScope();            // one DI scope per packet
    var handler = scope.ServiceProvider.GetRequiredService(entry.Type);
    try
    {
        var request = ParseRequest(entry.RequestType, packet.Payload);   // protobuf Parser
        var result  = entry.Method.Invoke(handler, new object?[] { request, connection });

        if (entry.ReplyMsgId != MsgId.None && result is IMessage reply)
            connection.Send(new RawPacket(entry.ReplyMsgId, reply.ToByteArray()));
    }
    catch (Exception ex)
    {
        var actual = (ex as TargetInvocationException)?.InnerException ?? ex;
        logger.LogError(actual, "Handler {Type}.{Method} failed for {MsgId}",
            entry.Type.Name, entry.Method.Name, packet.MsgId);
    }
    // scope disposed here -> DbContext disposed
}
```

`ParseRequest` resolves the generated static `MessageParser` for the request type (cached per
type) and parses `packet.Payload`. The wire payload is therefore the protobuf-encoded message;
`PassthroughPacketSerializer` still frames it unchanged (the framing layer stays bytes-only).

### 4.4 DI extension — `AddGameServer`

```csharp
public static class GameServerExtensions
{
    public static IServiceCollection AddGameServer(this IServiceCollection services)
    {
        // Local copy only to enumerate handler types; the resolvable singleton is
        // registered via factory so it gets the host logger.
        var registry = new PacketHandlerRegistry(new[] { typeof(GameServerExtensions).Assembly });

        services.AddSingleton(sp => new PacketHandlerRegistry(
            new[] { typeof(GameServerExtensions).Assembly },
            sp.GetService<ILogger<PacketHandlerRegistry>>()));

        foreach (var handlerType in registry.HandlerTypes)   // each handler Scoped
            services.AddScoped(handlerType);

        services.AddScoped<IExampleService, ExampleService>();

        services.AddSingleton<IPacketSerializer, PassthroughPacketSerializer>();
        services.AddSingleton<PacketDispatcher>();
        services.AddSingleton<ConnectionManager>();
        services.AddHostedService<GameServerHostedService>();
        return services;
    }
}
```

**To add a handler:**
1. Add the `rpc` and its request/response `message`s to a `.proto` (rpc name must match a
   `MsgId` member; response name minus `Reply`/`Response` must match the reply `MsgId` member).
2. Regenerate (§4.5) — you get the message types and a `<Service>Base`.
3. Subclass the base and `override` the new RPC. It's auto-discovered and auto-registered —
   no change to `AddGameServer` (only register any *new service* it depends on).

### 4.5 Code generation — the `Raphael.Tcp.Tools` package (`tcp` mode)

The base classes are generated at build time by **`Raphael.Tcp.Tools`**, a NuGet package
that wraps a **fork of gRPC's C# plugin**. (The fork's source lives in a separate grpc repo,
not in this tree.) The fork adds a `tcp` generator mode that, instead of the usual gRPC
service stub, emits:

```csharp
public abstract partial class PingServiceBase : IPacketHandler
{
    [PacketHandler(MsgId.Ping, MsgId.Pong)]                       // request id, reply id
    public virtual PongReply Ping(PingRequest request, Connection connection)
        => throw new NotImplementedException("Ping is not implemented.");
}
```

- The request `MsgId` comes from the **rpc name** (`Ping` → `MsgId.Ping`); the reply `MsgId`
  from the **response type name** minus a `Reply`/`Response` suffix (`PongReply` → `MsgId.Pong`).
  Both are matched against the `MsgId` enum.
- The server-side type names (`IPacketHandler`, the `PacketHandler` attribute, `Connection`)
  are **not** hardcoded in the generator — they're passed as generator options, so the fork
  carries no project-specific namespace. `MsgId` is taken from the proto's `csharp_namespace`.
- Streaming rpcs are skipped (the dispatcher is unary-only).

**Generation is automatic on `dotnet build`** — no manual `protoc` runs, no local plugin
binary, no grpc clone. The package (and its `Grpc.Tools` dependency) bring their own protoc
and run codegen as an MSBuild step. To regenerate, just drop/edit a `.proto` and build.

Setup is entirely in [TemplateTCPServer.GameServer.csproj](TemplateTCPServer.GameServer/TemplateTCPServer.GameServer.csproj):

```xml
<PackageReference Include="Raphael.Tcp.Tools" Version="1.0.0" />

<PropertyGroup>
  <RaphaelTcpIPacketHandler>TemplateTCPServer.GameServer.Packets.IPacketHandler</RaphaelTcpIPacketHandler>
  <RaphaelTcpPacketHandlerAttr>TemplateTCPServer.GameServer.Packets.PacketHandlerAttribute</RaphaelTcpPacketHandlerAttr>
  <RaphaelTcpConnection>TemplateTCPServer.GameServer.Networking.Connection</RaphaelTcpConnection>
</PropertyGroup>

<ItemGroup>
  <Protobuf Include="protos\**\*.proto" GrpcServices="None" />
</ItemGroup>
```

- Drop `.proto` files into `TemplateTCPServer.GameServer/protos/` (git-ignored). On build:
  **messages** are generated by `Grpc.Tools` into `obj/`; the **TCP base** (`*Tcp.cs`) by the
  forked plugin into `Generated/`. Both are compiled in; neither is committed.
- The `RaphaelTcp*` properties supply the three server-side type names the generator needs
  (they are generator options, so the plugin itself carries no project-specific namespace).
  `MsgId` is taken from the proto's `csharp_namespace`.

> The package generates **messages and the TCP base together in GameServer** — the base
> references `IPacketHandler`/`Connection`, so it must live there, and the messages follow.
> (Common no longer hosts codegen; it depends only on `Google.Protobuf` for the runtime types.)
> This is also why the proto/override model is *not* gRPC: gRPC runs over HTTP/2 and its
> generated base takes `ServerCallContext` — unusable on a raw socket. The fork keeps the
> generation ergonomics and swaps the transport-bound parts for `Connection` + `MsgId`.
>
> The package is **Windows-x64 only** in 1.0.0; other platforms fail the build with a clear
> message (the fork must be built per-platform to extend it).

---

## 5. Protocol / framing (`TemplateTCPServer.Common`)

- `enum MsgId : ushort` — message identifiers (`None`, `Ping`, `Pong` in the template).
- `abstract class BasePacket` — carries `MsgId` + a `ReadOnlyMemory<byte> Payload`.
- `RawPacket : BasePacket` — minimal concrete packet the framing layer produces.
- `IPacketSerializer` — `Deserialize(MsgId, payload)` / `Serialize(packet)`; default
  `PassthroughPacketSerializer` keeps the payload bytes as-is. Message bodies are encoded
  with **protobuf** (the generated message types, produced into GameServer's `obj/` from
  `protos/`); the dispatcher does that (de)serialization per handler, so the framing layer
  itself stays bytes-only.
- `ping.proto` (`TemplateTCPServer.GameServer/protos/ping.proto`) — the example IDL: the
  `PingService` rpcs and their messages.
  Source of truth for both the message types and the generated handler base (§4.5).
- **Framing** (`PacketFramer`, synchronous): wire format
  `[4-byte big-endian length][2-byte big-endian MsgId][payload]`.
  `Read` blocks on `stream.Read` until a whole frame is assembled, returns `null` on a clean
  EOF at a frame boundary, and throws if the stream ends mid-frame. `Write` emits the
  header + payload and flushes.

---

## 6. Lifetimes — the rule that makes this safe

| Component | Lifetime | Why |
|---|---|---|
| `GameServerHostedService` (TCP listener) | Singleton (hosted) | One listener for the process. |
| `ConnectionManager` | Singleton | Global registry of live connections. |
| `Connection` | **Not in DI** — `new`'d per socket | Owns the socket + read loop; lives as long as the client. |
| `PacketDispatcher`, `PacketHandlerRegistry`, `IPacketSerializer` | Singleton | Stateless; cheap. |
| **Per-packet scope** | `CreateScope()` per message | The "request" boundary. |
| Packet handlers (`PingHandler`, …) | **Scoped** | Resolved inside the per-packet scope; can inject scoped deps. |
| Services (`IExampleService`, `IAuthService`) | **Scoped** | Business logic, use repositories. |
| Repositories (`IAccountRepository`) | **Scoped** | Wrap `DbContext`. |
| `AppDbContext` | **Scoped** | Short-lived, not thread-safe → must not outlive one packet. |

**Critical anti-pattern to avoid:** never inject `AppDbContext`, a repository, or any
scoped service directly into the singleton `Connection`/listener/dispatcher constructors.
A scoped `DbContext` captured by a singleton would be shared across all connections and
never disposed (a *captive dependency*). Always go through `CreateScope()` per packet (the
dispatcher does this). `ValidateScopes`/`ValidateOnBuild` are on in Development to make this
throw at startup rather than corrupt state at runtime.

**Connection-scoped state** (authenticated account, session) belongs on the `Connection`
object — it lives per socket. The DI scope is per *packet* and must not hold session state.

---

## 7. The connection loop — synchronous, thread-per-connection

### 7.1 `GameServerHostedService`

An `IHostedService` (not `BackgroundService`, since the loop is blocking, not async-await).
`StartAsync` starts the listener and spins up a dedicated **accept thread**; each accepted
client runs its blocking loop on its **own thread**. Only singletons are injected here.

```csharp
public sealed class GameServerHostedService(
    PacketDispatcher dispatcher,
    IPacketSerializer serializer,
    ConnectionManager connections,
    IConfiguration config,
    ILoggerFactory loggerFactory) : IHostedService      // all injected deps are singletons
{
    private readonly ILogger<GameServerHostedService> _logger = loggerFactory.CreateLogger<GameServerHostedService>();
    private readonly int _port = config.GetValue("GameServer:Port", 6969);

    private TcpListener? _listener;
    private Thread? _acceptThread;
    private volatile bool _stopping;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();
        _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "GameServer-Accept" };
        _acceptThread.Start();
        return Task.CompletedTask;
    }

    private void AcceptLoop()
    {
        var connectionLogger = loggerFactory.CreateLogger<Connection>();
        try
        {
            while (!_stopping)
            {
                TcpClient client = _listener!.AcceptTcpClient();           // blocking
                var connection = new Connection(client, dispatcher, serializer, connections, connectionLogger);
                new Thread(connection.Run) { IsBackground = true }.Start(); // one thread per connection
            }
        }
        catch (SocketException) when (_stopping) { /* listener stopped during shutdown */ }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _stopping = true;
        foreach (var connection in connections.Connections) connection.Close();
        _listener?.Stop();
        return Task.CompletedTask;
    }
}
```

### 7.2 `Connection`

`new`'d per socket (not a DI service); only references singletons. Its `Run()` blocks the
connection's thread reading frames and dispatching them sequentially.

```csharp
public sealed class Connection(
    TcpClient client,
    PacketDispatcher dispatcher,
    IPacketSerializer serializer,
    ConnectionManager manager,
    ILogger logger)
{
    private readonly NetworkStream _stream = client.GetStream();
    public string Id { get; } = client.Client.RemoteEndPoint!.ToString()!;

    public void Run()
    {
        manager.Add(this);
        logger.LogInformation("{Id} connected", Id);
        try
        {
            while (true)
            {
                BasePacket? packet = PacketFramer.Read(_stream, serializer);    // blocking
                if (packet is null) break;                                      // peer closed
                dispatcher.Dispatch(this, packet);                             // per-packet scope
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "{Id} read loop ended", Id); }
        finally
        {
            manager.Remove(this);
            client.Close();
            logger.LogInformation("{Id} disconnected", Id);
        }
    }

    public void Send(BasePacket packet) => PacketFramer.Write(_stream, serializer, packet);
    public void Close() => client.Close();
}
```

### 7.3 The trade-off

Thread-per-connection means **each connected client permanently occupies one OS thread**
parked in a blocking socket read, even when idle. For a low-population private server that
is fine and buys real simplicity (no `async`/`await`/`Task`/`CancellationToken` anywhere in
the app's own code). It is the first thing to revisit if the server ever needs to handle
hundreds of simultaneous connections — at which point an async read loop (`BackgroundService`
+ `await ReadAsync`) would scale better. Packets on a single connection are processed
sequentially in arrival order (good for per-connection ordering); a slow handler blocks only
that one connection's thread.

---

## 8. SDKServer (login) under the shared host

`SDKServer` has **no `Main`** — it is a class library of controllers + login services. The
startup host pulls its controllers in as an *application part*:

```csharp
public static class SdkServerExtensions
{
    public static IServiceCollection AddSdkServer(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddControllers().AddApplicationPart(typeof(SDKController).Assembly);
        return services;
    }
}
```

`AddApplicationPart` makes MVC discover `[ApiController]` types in the referenced library so
`app.MapControllers()` maps them. Because `SDKController` lives in the same container, it can
inject the same `IAuthService` / `IAccountRepository` the GameServer uses. `AuthService`
(scoped) → `IAccountRepository` (scoped) → `AppDbContext` (scoped). HTTP requests already get
a scope per request automatically; the TCP side reproduces that scope per packet via the
dispatcher.

`IAuthService`/`AuthService` are synchronous (`bool ValidateCredentials(username, password)`).
The bundled `SDKController` actions are already synchronous (`IResult`, no `async`).

---

## 9. End-to-end flow (the payoff)

**HTTP login (SDKServer):**
```
GET /... → SDKController (scope auto-created by ASP.NET)
   → IAuthService.ValidateCredentials
      → IAccountRepository.GetByUsername
         → AppDbContext (scoped, disposed at end of request)
```

**TCP packet (GameServer), e.g. the Ping example:**
```
bytes on socket → Connection.Run → PacketFramer.Read → BasePacket{MsgId.Ping}
   → PacketDispatcher.Dispatch  ── creates IServiceScope (the per-packet boundary) ──
      → PingHandler (resolved from scope)
         → IExampleService.CountAccounts
            → IAccountRepository.Count
               → AppDbContext (scoped, disposed when scope disposes)
   → Connection.Send(RawPacket{MsgId.Pong})
```

Same layered chain, same DI semantics, two transports — both fully synchronous.

---

## 10. Things to watch / future refinements

- **Captive dependencies** — never inject scoped services into the singleton
  listener/dispatcher/connection. Only the per-packet scope touches `DbContext`. (§6)
- **Thread-per-connection scaling** — fine for a private server; revisit with an async read
  loop if simultaneous-connection counts grow large. (§7.3)
- **`AllowSynchronousIO`** — currently enabled; matches the synchronous controller/IO paths.
- **Password hashing** — `AuthService.ValidateCredentials` has a `// TODO` plaintext
  comparison; replace with a real hash before any real use.
- **`MethodInfo.Invoke` reflection cost** — fine to start; if it ever matters, cache compiled
  delegates (`Delegate.CreateDelegate` / expression trees) in the registry.
- **UnitOfWork / transactions** — repositories expose `SaveChanges` directly; introduce an
  `IUnitOfWork` if a handler ever needs multiple repositories in one transaction.
- **Serializer** — `PassthroughPacketSerializer` is a placeholder; swap for JSON or a binary
  scheme (with typed packet classes) when the protocol is fleshed out.
- **Graceful shutdown** — `StopAsync` sets a stop flag, stops the listener (unblocking the
  accept thread), and closes all live sockets (unblocking each connection thread); all
  server threads are background threads.
```
