namespace Nova.Server.Models;

public sealed class EncryptedMessage
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid SenderId { get; set; }
    public string Ciphertext { get; set; } = "";
    public string Nonce { get; set; } = "";
    public int EncryptionVersion { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
}
