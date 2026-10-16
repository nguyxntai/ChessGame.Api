namespace ChessGame.Api.DTOs.Wallet;

public sealed class WalletTransactionsResponse
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public long Total { get; set; }
    public List<WalletTransactionResponse> Items { get; set; } = new();
}

public sealed class WalletTransactionResponse
{
    public string TransactionId { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public long Amount { get; set; }
    public long BalanceAfter { get; set; }
    public string Source { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
