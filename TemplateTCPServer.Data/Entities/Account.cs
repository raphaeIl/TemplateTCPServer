namespace TemplateTCPServer.Data.Entities
{
    /// <summary>
    /// Placeholder entity so the DbContext and repository pattern have something concrete
    /// to map. Replace / extend with the real domain model.
    /// </summary>
    public class Account
    {
        public long Id { get; set; }
        public string Username { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public DateTimeOffset CreatedAt { get; set; }
    }
}
