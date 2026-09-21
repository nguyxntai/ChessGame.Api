using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ChessGame.Api.Models;
using ChessGame.Api.Settings;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ChessGame.Api.Services;

public class JwtService
{
    private readonly JwtSettings _settings;

    public JwtService(
        IOptions<JwtSettings> settings)
    {
        _settings = settings.Value;
    }

    public (string Token, DateTime ExpiresAt)
        GenerateAccessToken(User user)
    {
        if (string.IsNullOrWhiteSpace(_settings.Key))
        {
            throw new InvalidOperationException(
                "Jwt:Key chưa được cấu hình."
            );
        }

        var now = DateTime.UtcNow;

        var expiresAt =
            now.AddMinutes(
                _settings.AccessTokenMinutes
            );

        var claims = new List<Claim>
        {
            new(
                JwtRegisteredClaimNames.Sub,
                user.Id.ToString()
            ),

            new(
                JwtRegisteredClaimNames.Email,
                user.Email
            ),

            new(
                "username",
                user.Username
            ),

            new(
                JwtRegisteredClaimNames.Jti,
                Guid.NewGuid().ToString()
            )
        };

        var key =
            new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(
                    _settings.Key
                )
            );

        var credentials =
            new SigningCredentials(
                key,
                SecurityAlgorithms.HmacSha256
            );

        var token =
            new JwtSecurityToken(
                issuer: _settings.Issuer,
                audience: _settings.Audience,
                claims: claims,
                notBefore: now,
                expires: expiresAt,
                signingCredentials: credentials
            );

        string tokenString =
            new JwtSecurityTokenHandler()
                .WriteToken(token);

        return (tokenString, expiresAt);
    }
}