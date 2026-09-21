using System.Security.Cryptography;
using System.Text;
using ChessGame.Api.Models;
using ChessGame.Api.Settings;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ChessGame.Api.Services;

public class RefreshTokenService
{
    private readonly IMongoCollection<RefreshToken>
        _refreshTokens;

    private readonly IMongoClient _mongoClient;

    private readonly JwtSettings _jwtSettings;

    public RefreshTokenService(
        IMongoDatabase database,
        IMongoClient mongoClient,
        IOptions<JwtSettings> jwtSettings)
    {
        _refreshTokens =
            database.GetCollection<RefreshToken>(
                "refresh_tokens"
            );

        _mongoClient = mongoClient;

        _jwtSettings = jwtSettings.Value;
    }

    public async Task<(
        string RawToken,
        DateTime ExpiresAt)>
        CreateAsync(ObjectId userId)
    {
        string rawToken =
            GenerateToken();

        string tokenHash =
            HashToken(rawToken);

        DateTime now =
            DateTime.UtcNow;

        DateTime expiresAt =
            now.AddDays(
                _jwtSettings.RefreshTokenDays
            );

        var refreshToken =
            new RefreshToken
            {
                UserId = userId,
                TokenHash = tokenHash,
                CreatedAt = now,
                ExpiresAt = expiresAt,
                RevokedAt = null
            };

        await _refreshTokens
            .InsertOneAsync(refreshToken);

        return (
            rawToken,
            expiresAt
        );
    }

    public async Task<(
        ObjectId UserId,
        string RawToken,
        DateTime ExpiresAt)>
        RotateAsync(string oldRawToken)
    {
        string oldHash =
            HashToken(oldRawToken);

        string newRawToken =
            GenerateToken();

        string newHash =
            HashToken(newRawToken);

        DateTime now =
            DateTime.UtcNow;

        DateTime newExpiresAt =
            now.AddDays(
                _jwtSettings.RefreshTokenDays
            );

        using var session =
            await _mongoClient
                .StartSessionAsync();

        session.StartTransaction();

        try
        {
            var filter =
                Builders<RefreshToken>
                    .Filter.And(
                        Builders<RefreshToken>
                            .Filter.Eq(
                                x => x.TokenHash,
                                oldHash
                            ),

                        Builders<RefreshToken>
                            .Filter.Eq(
                                x => x.RevokedAt,
                                null
                            ),

                        Builders<RefreshToken>
                            .Filter.Gt(
                                x => x.ExpiresAt,
                                now
                            )
                    );

            var update =
                Builders<RefreshToken>
                    .Update
                    .Set(
                        x => x.RevokedAt,
                        now
                    )
                    .Set(
                        x => x.ReplacedByTokenHash,
                        newHash
                    );

            var oldToken =
                await _refreshTokens
                    .FindOneAndUpdateAsync(
                        session,
                        filter,
                        update,
                        new FindOneAndUpdateOptions<
                            RefreshToken>
                        {
                            ReturnDocument =
                                ReturnDocument.Before
                        }
                    );

            if (oldToken is null)
            {
                await session
                    .AbortTransactionAsync();

                throw new InvalidOperationException(
                    "INVALID_REFRESH_TOKEN"
                );
            }

            var newRefreshToken =
                new RefreshToken
                {
                    UserId =
                        oldToken.UserId,

                    TokenHash =
                        newHash,

                    CreatedAt =
                        now,

                    ExpiresAt =
                        newExpiresAt,

                    RevokedAt =
                        null
                };

            await _refreshTokens
                .InsertOneAsync(
                    session,
                    newRefreshToken
                );

            await session
                .CommitTransactionAsync();

            return (
                oldToken.UserId,
                newRawToken,
                newExpiresAt
            );
        }
        catch
        {
            if (session.IsInTransaction)
            {
                await session
                    .AbortTransactionAsync();
            }

            throw;
        }
    }

    public async Task RevokeAsync(
        string rawToken)
    {
        string tokenHash =
            HashToken(rawToken);

        var filter =
            Builders<RefreshToken>
                .Filter.And(
                    Builders<RefreshToken>
                        .Filter.Eq(
                            x => x.TokenHash,
                            tokenHash
                        ),

                    Builders<RefreshToken>
                        .Filter.Eq(
                            x => x.RevokedAt,
                            null
                        )
                );

        var update =
            Builders<RefreshToken>
                .Update
                .Set(
                    x => x.RevokedAt,
                    DateTime.UtcNow
                );

        await _refreshTokens
            .UpdateOneAsync(
                filter,
                update
            );
    }

    private static string GenerateToken()
    {
        byte[] bytes =
            RandomNumberGenerator
                .GetBytes(64);

        return Convert
            .ToBase64String(bytes);
    }

    private static string HashToken(
        string token)
    {
        byte[] hash =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(token)
            );

        return Convert
            .ToHexString(hash);
    }

    public async Task EnsureIndexesAsync()
    {
        var tokenIndex =
            new CreateIndexModel<RefreshToken>(
                Builders<RefreshToken>
                    .IndexKeys
                    .Ascending(
                        x => x.TokenHash
                    ),
                new CreateIndexOptions
                {
                    Unique = true,
                    Name =
                        "uq_refresh_token_hash"
                }
            );

        var userIndex =
            new CreateIndexModel<RefreshToken>(
                Builders<RefreshToken>
                    .IndexKeys
                    .Ascending(
                        x => x.UserId
                    ),
                new CreateIndexOptions
                {
                    Name =
                        "idx_refresh_tokens_user"
                }
            );

        var expirationIndex =
            new CreateIndexModel<RefreshToken>(
                Builders<RefreshToken>
                    .IndexKeys
                    .Ascending(
                        x => x.ExpiresAt
                    ),
                new CreateIndexOptions
                {
                    ExpireAfter =
                        TimeSpan.Zero,

                    Name =
                        "ttl_refresh_tokens"
                }
            );

        await _refreshTokens
            .Indexes
            .CreateManyAsync(
                new[]
                {
                    tokenIndex,
                    userIndex,
                    expirationIndex
                }
            );
    }
}