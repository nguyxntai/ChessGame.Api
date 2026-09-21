using ChessGame.Api.Models;
using MongoDB.Driver;

namespace ChessGame.Api.Services;

public class UserService
{
    private readonly IMongoCollection<User> _users;

    public UserService(IMongoDatabase database)
    {
        _users = database.GetCollection<User>("users");
    }

    public async Task<bool> EmailExistsAsync(
        string normalizedEmail)
    {
        return await _users.Find(
            user => user.NormalizedEmail == normalizedEmail
        ).AnyAsync();
    }

    public async Task<bool> UsernameExistsAsync(
        string normalizedUsername)
    {
        return await _users.Find(
            user => user.NormalizedUsername == normalizedUsername
        ).AnyAsync();
    }

    public async Task CreateAsync(User user)
    {
        await _users.InsertOneAsync(user);
    }

    public async Task EnsureIndexesAsync()
    {
        var usernameIndex =
            new CreateIndexModel<User>(
                Builders<User>.IndexKeys.Ascending(
                    user => user.NormalizedUsername
                ),
                new CreateIndexOptions
                {
                    Unique = true,
                    Name = "uq_users_normalizedUsername"
                }
            );

        var emailIndex =
            new CreateIndexModel<User>(
                Builders<User>.IndexKeys.Ascending(
                    user => user.NormalizedEmail
                ),
                new CreateIndexOptions
                {
                    Unique = true,
                    Name = "uq_users_normalizedEmail"
                }
            );

        var eloIndex =
            new CreateIndexModel<User>(
                Builders<User>.IndexKeys.Descending(
                    user => user.Stats.Elo
                ),
                new CreateIndexOptions
                {
                    Name = "idx_users_elo"
                }
            );

        await _users.Indexes.CreateManyAsync(
            new[]
            {
                usernameIndex,
                emailIndex,
                eloIndex
            }
        );
    }
}