using System.Text.Json;

namespace ProjectC.Api.Decks;

public interface ICardCatalog
{
    string Version { get; }
    int RequiredCardCount { get; }
    int MaximumCopiesPerCard { get; }
    bool IsMainDeckCard(int cardId);
}

// Minimal exported validation metadata only; no card definitions are stored in the database.
public sealed class CardCatalog : ICardCatalog
{
    public string Version { get; }
    public int RequiredCardCount { get; }
    public int MaximumCopiesPerCard { get; }
    private readonly Dictionary<int, bool> cards;

    public CardCatalog(string path)
    {
        using var file = File.OpenRead(path);
        using var document = JsonDocument.Parse(file);
        var root = document.RootElement;
        Version = root.GetProperty("version").GetString()!;
        RequiredCardCount = root.GetProperty("requiredCardCount").GetInt32();
        MaximumCopiesPerCard = root.GetProperty("maximumCopiesPerCard").GetInt32();
        cards = root.GetProperty("cards").EnumerateArray().ToDictionary(
            c => c.GetProperty("cardId").GetInt32(), c => c.GetProperty("mainDeckEligible").GetBoolean());
        if (string.IsNullOrWhiteSpace(Version) || cards.Count == 0 ||
            RequiredCardCount <= 0 || MaximumCopiesPerCard <= 0)
            throw new InvalidOperationException("Invalid card catalog metadata.");
    }

    public bool IsMainDeckCard(int cardId) => cards.TryGetValue(cardId, out var eligible) && eligible;
}
