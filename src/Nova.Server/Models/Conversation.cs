namespace Nova.Server.Models;

public enum ConversationType
{
    DirectMessage = 0,
    GroupDirectMessage = 1
}

public sealed class Conversation
{
    public Guid Id { get; set; }
    public ConversationType Type { get; set; }
    public string? Name { get; set; }
    public string? IconUrl { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<ConversationMember> Members { get; set; } = new List<ConversationMember>();
}
