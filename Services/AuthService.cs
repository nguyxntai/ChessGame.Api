using ChessGame.Api.DTOs.Auth;
using ChessGame.Api.Models;
using Microsoft.AspNetCore.Identity;
using MongoDB.Driver;

namespace ChessGame.Api.Services;

public class AuthService
{
    private readonly UserService _userService;

    private readonly IPasswordHasher<User> _passwordHasher;

    public AuthService(
        UserService userService,
        IPasswordHasher<User> passwordHasher)
    {
        _userService = userService;
        _passwordHasher = passwordHasher;
    }

    public async Task<RegisterResponse> RegisterAsync(
        RegisterRequest request)
    {
        string username = request.Username.Trim();

        string email =
            request.Email
                .Trim()
                .ToLowerInvariant();

        string normalizedUsername =
            username.ToUpperInvariant();

        string normalizedEmail =
            email.ToUpperInvariant();

        if (await _userService.UsernameExistsAsync(
                normalizedUsername))
        {
            throw new InvalidOperationException(
                "USERNAME_EXISTS"
            );
        }

        if (await _userService.EmailExistsAsync(
                normalizedEmail))
        {
            throw new InvalidOperationException(
                "EMAIL_EXISTS"
            );
        }

        var user = new User
        {
            Username = username,

            NormalizedUsername =
                normalizedUsername,

            Email = email,

            NormalizedEmail =
                normalizedEmail,

            IsActive = true,

            Profile = new UserProfile
            {
                DisplayName = username,
                AvatarId = null
            },

            Wallet = new PlayerWallet
            {
                Golds = 1000,
                Diamonds = 0,
                Tickets = 10
            },

            Stats = new PlayerStats
            {
                Elo = 1000,
                Wins = 0,
                Losses = 0,
                Draws = 0,
                GamesPlayed = 0
            },

            Equipped = new EquippedSkins
            {
                ChessSkinId = null,
                BoardSkinId = null
            },

            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        user.PasswordHash =
            _passwordHasher.HashPassword(
                user,
                request.Password
            );

        try
        {
            await _userService.CreateAsync(user);
        }
        catch (MongoWriteException ex)
            when (
                ex.WriteError?.Category ==
                ServerErrorCategory.DuplicateKey
            )
        {
            throw new InvalidOperationException(
                "USER_ALREADY_EXISTS"
            );
        }

        return new RegisterResponse
        {
            UserId = user.Id.ToString(),

            Username = user.Username,

            Email = user.Email,

            Golds = user.Wallet.Golds,

            Diamonds = user.Wallet.Diamonds,

            Tickets = user.Wallet.Tickets,

            Elo = user.Stats.Elo,

            CreatedAt = user.CreatedAt
        };
    }
}