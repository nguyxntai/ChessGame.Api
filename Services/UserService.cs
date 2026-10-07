using ChessGame.Api.Models;
using MongoDB.Driver;
using MongoDB.Bson;
using ChessGame.Api.DTOs.User;
using System.ComponentModel.DataAnnotations;

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
        return await _users
            .Find(user =>
                user.NormalizedEmail == normalizedEmail)
            .AnyAsync();
    }
    public async Task<User?> GetByNormalizedEmailAsync(
    string normalizedEmail)
    {
        return await _users
            .Find(user =>
                user.NormalizedEmail == normalizedEmail)
            .FirstOrDefaultAsync();
    }
    public async Task<User?> GetByIdAsync(ObjectId id)
    {
        return await _users
            .Find(user => user.Id == id)
            .FirstOrDefaultAsync();
    }

    public async Task<User?> UpdateProfileAsync(ObjectId id, UpdateProfileRequest request)
    {
        Validator.ValidateObject(request, new ValidationContext(request), validateAllProperties: true);

        var updates = new List<UpdateDefinition<User>>
        {
            Builders<User>.Update.Set(user => user.UpdatedAt, DateTime.UtcNow)
        };

        if (request.HasDisplayName)
            updates.Add(Builders<User>.Update.Set(user => user.Profile.DisplayName, request.DisplayName!.Trim()));

        if (request.HasAvatarId)
            updates.Add(Builders<User>.Update.Set(user => user.Profile.AvatarId, request.AvatarId));

        // Update only supplied profile fields; never replace the user, wallet or stats.
        return await _users.FindOneAndUpdateAsync(
            user => user.Id == id && user.IsActive,
            Builders<User>.Update.Combine(updates),
            new FindOneAndUpdateOptions<User> { ReturnDocument = ReturnDocument.After });
    }
    public async Task<bool> UsernameExistsAsync(
        string normalizedUsername)
    {
        return await _users
            .Find(user =>
                user.NormalizedUsername == normalizedUsername)
            .AnyAsync();
    }

    // Dùng khi không cần transaction
    public async Task CreateAsync(User user)
    {
        await _users.InsertOneAsync(user);
    }

    // Dùng khi đang ở trong MongoDB transaction
    public async Task CreateAsync(
        IClientSessionHandle session,
        User user)
    {
        await _users.InsertOneAsync(
            session,
            user
        );
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
                eloIndex,
                new CreateIndexModel<User>(Builders<User>.IndexKeys.Ascending(user => user.IsActive)
                    .Descending(user => user.Ratings.Classic.Rating).Ascending(user => user.Id),
                    new CreateIndexOptions { Name = "idx_users_leaderboard_classic" }),
                new CreateIndexModel<User>(Builders<User>.IndexKeys.Ascending(user => user.IsActive)
                    .Descending(user => user.Ratings.Aram.Rating).Ascending(user => user.Id),
                    new CreateIndexOptions { Name = "idx_users_leaderboard_aram" }),
                new CreateIndexModel<User>(Builders<User>.IndexKeys.Ascending(user => user.IsActive).Descending(user => user.Ratings.Classic.Rating).Ascending(user => user.Id),
                    new CreateIndexOptions<User> { Name = "idx_users_established_classic", PartialFilterExpression = Builders<User>.Filter.Gte(user => user.Ratings.Classic.RatedGames, 10) }),
                new CreateIndexModel<User>(Builders<User>.IndexKeys.Ascending(user => user.IsActive).Descending(user => user.Ratings.Aram.Rating).Ascending(user => user.Id),
                    new CreateIndexOptions<User> { Name = "idx_users_established_aram", PartialFilterExpression = Builders<User>.Filter.Gte(user => user.Ratings.Aram.RatedGames, 10) })
            }
        );
    }
}
