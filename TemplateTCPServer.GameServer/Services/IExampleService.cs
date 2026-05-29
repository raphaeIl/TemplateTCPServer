namespace TemplateTCPServer.GameServer.Services
{
    /// <summary>
    /// Example game-side business-logic service. Stands in for the "Service" layer in the
    /// Handler -&gt; Service -&gt; Repository -&gt; DbContext chain. Scoped, so it (and the
    /// repository/DbContext it pulls) lives for exactly one packet.
    /// </summary>
    public interface IExampleService
    {
        /// <summary>Returns how many accounts exist (just to exercise the DB through the repo).</summary>
        Task<int> CountAccountsAsync(CancellationToken ct = default);
    }
}
