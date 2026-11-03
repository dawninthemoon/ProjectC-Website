namespace ProjectC.Api.Decks;

public sealed record CardEntry(int CardId, int Quantity);
public sealed record SaveDeckRequest(string? Name, List<CardEntry?>? Cards);

public sealed class DeckValidator(ICardCatalog catalog)
{
    public Dictionary<string, string[]> Validate(SaveDeckRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 64)
            errors["name"] = ["덱 이름은 1~64자여야 합니다."];
        if (request.Cards is null || request.Cards.Count == 0 || request.Cards.Count > catalog.RequiredCardCount)
        {
            errors["cards"] = ["올바른 카드 목록이 필요합니다."];
            return errors;
        }
        var issues = new List<string>();
        var ids = new HashSet<int>();
        long total = 0;
        foreach (var entry in request.Cards)
        {
            if (entry is null) { issues.Add("카드 항목은 null일 수 없습니다."); continue; }
            if (!ids.Add(entry.CardId)) issues.Add($"CardId {entry.CardId}: 중복 항목을 수량으로 합쳐 주세요.");
            if (!catalog.IsMainDeckCard(entry.CardId)) issues.Add($"CardId {entry.CardId}: 존재하지 않거나 메인 덱에 사용할 수 없습니다.");
            if (entry.Quantity < 1 || entry.Quantity > catalog.MaximumCopiesPerCard)
                issues.Add($"CardId {entry.CardId}: 수량은 1~{catalog.MaximumCopiesPerCard}여야 합니다.");
            total += entry.Quantity;
        }
        if (total != catalog.RequiredCardCount) issues.Add($"덱은 정확히 {catalog.RequiredCardCount}장이어야 합니다.");
        if (issues.Count > 0) errors["cards"] = issues.ToArray();
        return errors;
    }
}
