using ChessGame.Api.Models;
using ChessGame.Api.Services;
using ChessGame.Api.Settings;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

// Controllers
builder.Services.AddControllers();

// MongoDB Settings
builder.Services.Configure<MongoDbSettings>(
    builder.Configuration.GetSection("MongoDb")
);

// JWT Settings
builder.Services.Configure<JwtSettings>(
    builder.Configuration.GetSection("Jwt")
);

// MongoClient
builder.Services.AddSingleton<IMongoClient>(
    serviceProvider =>
    {
        var settings = serviceProvider
            .GetRequiredService<
                IOptions<MongoDbSettings>>()
            .Value;

        if (string.IsNullOrWhiteSpace(
                settings.ConnectionString))
        {
            throw new InvalidOperationException(
                "MongoDb:ConnectionString chưa được cấu hình."
            );
        }

        return new MongoClient(
            settings.ConnectionString
        );
    });

// Database
builder.Services.AddSingleton<IMongoDatabase>(
    serviceProvider =>
    {
        var settings = serviceProvider
            .GetRequiredService<
                IOptions<MongoDbSettings>>()
            .Value;

        if (string.IsNullOrWhiteSpace(
                settings.DatabaseName))
        {
            throw new InvalidOperationException(
                "MongoDb:DatabaseName chưa được cấu hình."
            );
        }

        var mongoClient =
            serviceProvider
                .GetRequiredService<IMongoClient>();

        return mongoClient.GetDatabase(
            settings.DatabaseName
        );
    });

// Application Services
builder.Services.AddScoped<UserService>();

builder.Services.AddScoped<AuthService>();

builder.Services.AddScoped<InventoryService>();

builder.Services.AddScoped<JwtService>();

builder.Services.AddScoped<RefreshTokenService>();

// ASP.NET Core built-in password hasher
builder.Services
    .AddScoped<
        IPasswordHasher<User>,
        PasswordHasher<User>>();

var jwtSettings =
    builder.Configuration
        .GetSection("Jwt")
        .Get<JwtSettings>()
    ?? throw new InvalidOperationException(
        "Jwt settings chưa được cấu hình."
    );

if (string.IsNullOrWhiteSpace(jwtSettings.Key))
{
    throw new InvalidOperationException(
        "Jwt:Key chưa được cấu hình."
    );
}

builder.Services
    .AddAuthentication(
        JwtBearerDefaults.AuthenticationScheme
    )
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,

                ValidIssuer =
                    jwtSettings.Issuer,

                ValidAudience =
                    jwtSettings.Audience,

                IssuerSigningKey =
                    new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(
                            jwtSettings.Key
                        )
                    ),

                ClockSkew =
                    TimeSpan.FromSeconds(30)
            };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Tạo MongoDB indexes
using (var scope = app.Services.CreateScope())
{
    var userService =
        scope.ServiceProvider
            .GetRequiredService<UserService>();

    var inventoryService =
        scope.ServiceProvider
            .GetRequiredService<InventoryService>();

    var refreshTokenService =
        scope.ServiceProvider
            .GetRequiredService<RefreshTokenService>();

    await userService.EnsureIndexesAsync();

    await inventoryService.EnsureIndexesAsync();

    await refreshTokenService.EnsureIndexesAsync();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();