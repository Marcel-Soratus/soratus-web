namespace Soratus.Web.Models;

public sealed record ChatTurn(string Role, string Content)
{
    public string Html { get; set; } = Content;
}

public sealed record ChatRequest(List<ChatTurn>? Messages);

public sealed record Lead(
    string Name,
    string Company,
    string Phone,
    string? Email,
    string? Note,
    bool Consent);

public sealed record Testimonial(string Quote, string Highlight, string Role, string Industry, string Chip, string AvatarStyle);

public sealed record ClientLogo(string Name, string Sub, string Badge, int StyleIndex);

public sealed record BrancheItem(string Number, string Title, string Hint);
