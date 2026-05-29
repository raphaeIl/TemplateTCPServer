namespace TemplateTCPServer.Data.Entities
{
    public class Account
    {
        public long Id { get; set; }
        public string Username { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public DateTimeOffset CreatedAt { get; set; }
    }
}
