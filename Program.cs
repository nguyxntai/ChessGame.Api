using ChessGame.Api.Models;
using ChessGame.Api.Services;
using ChessGame.Api.Settings;
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

// ASP.NET Core built-in password hasher
builder.Services
    .AddScoped<
        IPasswordHasher<User>,
        PasswordHasher<User>>();

var app = builder.Build();

// Tạo MongoDB indexes
using (var scope = app.Services.CreateScope())
{
    var userService =
        scope.ServiceProvider
            .GetRequiredService<UserService>();

    await userService.EnsureIndexesAsync();
}

app.UseHttpsRedirection();

app.MapControllers();

app.Run();