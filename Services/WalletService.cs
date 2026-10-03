using ChessGame.Api.DTOs.User;
using ChessGame.Api.DTOs.Wallet;
using ChessGame.Api.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ChessGame.Api.Services;

public sealed class WalletService
{
    private readonly IMongoCollection<User> _users;
    private readonly IMongoCollection<CurrencyTransaction> _transactions;

    public WalletService(IMongoDatabase database)
    {
        _users = database.GetCollection<User>("users");
        _transactions = database.GetCollection<CurrencyTransaction>("currency_transactions");
    }

    public async Task<WalletResponse> GetWalletAsync(ObjectId userId, CancellationToken cancellationToken = default)
    {
        var user = await RequireActiveUserAsync(userId, cancellationToken);
        return new WalletResponse
        {
            Golds = user.Wallet.Golds,
            Diamonds = user.Wallet.Diamonds,
            Tickets = user.Wallet.Tickets
        };
    }

    public async Task<WalletTransactionsResponse> GetTransactionsAsync(
        ObjectId userId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var skip = ((long)page - 1) * pageSize;
        if (page < 1 || pageSize is < 1 or > 100 || skip > int.MaxValue)
            throw new WalletException("INVALID_PAGE");

        await RequireActiveUserAsync(userId, cancellationToken);
        var filter = Builders<CurrencyTransaction>.Filter.Eq(x => x.UserId, userId);
        var total = await _transactions.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
        var transactions = await _transactions.Find(filter)
            .SortByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((int)skip).Limit(pageSize).ToListAsync(cancellationToken);

        return new WalletTransactionsResponse
        {
            Page = page,
            PageSize = pageSize,
            Total = total,
            Items = transactions.Select(x => new WalletTransactionResponse
            {
                TransactionId = x.Id.ToString(),
                Currency = x.Currency,
                Amount = x.Amount,
                BalanceAfter = x.BalanceAfter,
                Source = x.Source,
                RequestId = x.RequestId,
                CreatedAt = x.CreatedAt
            }).ToList()
        };
    }

    private async Task<User> RequireActiveUserAsync(ObjectId userId, CancellationToken cancellationToken)
    {
        var user = await _users.Find(x => x.Id == userId).FirstOrDefaultAsync(cancellationToken);
        if (user is null) throw new WalletException("USER_NOT_FOUND");
        if (!user.IsActive) throw new WalletException("ACCOUNT_DISABLED");
        return user;
    }

    public Task EnsureIndexesAsync() => _transactions.Indexes.CreateOneAsync(
        new CreateIndexModel<CurrencyTransaction>(
            Builders<CurrencyTransaction>.IndexKeys.Ascending(x => x.UserId)
                .Descending(x => x.CreatedAt).Descending(x => x.Id),
            new CreateIndexOptions { Name = "idx_currency_transactions_user_createdAt_id" }));
}

public sealed class WalletException : Exception
{
    public string Code { get; }
    public WalletException(string code) : base(code) => Code = code;
}
