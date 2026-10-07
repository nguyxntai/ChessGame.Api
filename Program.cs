using ChessGame.Api.Models;
using ChessGame.Api.Services;
using ChessGame.Api.Settings;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Microsoft.OpenApi.Models;
using ChessGame.Api.Online;

var builder = WebApplication.CreateBuilder(args);

// Controllers
builder.Services.AddControllers();
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 32 * 1024;
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    options.KeepAliveInterval = TimeSpan.FromSeconds(10);
});
builder.Services.AddOptions<OnlineOptions>()
    .Bind(builder.Configuration.GetSection("Online"))
    .Validate(o => o.ProtocolVersion > 0 && o.QueueSeconds > 0 && o.ReadySeconds > 0 &&
        o.ReconnectSeconds > 0 && o.DrawOfferSeconds > 0 && o.RoomSeconds > 0 &&
        o.InitialRatingRange >= 0 && o.RatingRangePerSecond >= 0 && o.MaximumRatingRange >= o.InitialRatingRange &&
        o.CandidatesPerPool is >= 4 and <= 128 && o.CandidatesPerPool % 2 == 0 && o.PoolsPerSweep is >= 1 and <= 8 &&
        o.MaximumPairsPerSweep is >= 1 and <= 16 && o.RecentOpponentSeconds >= 0 && o.RecentOpponentRelaxSeconds >= 0 &&
        o.MaximumLatencyDifferenceMilliseconds > 0,
        "Online deadlines, protocol and rating configuration are invalid.")
    .ValidateOnStart();
builder.Services.AddSingleton<OnlineRuntimeLease>();
builder.Services.AddSingleton<OnlineStore>();
builder.Services.AddSingleton<AramEngine>();
builder.Services.AddSingleton<GameRules>();
builder.Services.AddSingleton<OnlineService>();
builder.Services.AddSingleton<LiveConnections>();
builder.Services.AddSingleton<NetworkQualityTracker>();
builder.Services.AddScoped<OnlineExceptionFilter>();
builder.Services.AddHostedService<OnlineWorker>();

// Swagger / OpenAPI configuration
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ChessGame.Api",
        Version = "v1",
        Description = "API documentation for ChessGame Backend Service"
    });

    // Cấu hình Nút Authorize (JWT Token) trên Swagger UI
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Nhập JWT Bearer token để xác thực API"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

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
builder.Services.AddScoped<LeaderboardService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<GachaService>();
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
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                // Browser WebSockets cannot set a bearer header. Restrict query-token auth to this hub.
                if (context.Request.Path.StartsWithSegments("/hubs/game") &&
                    context.Request.Query.TryGetValue("access_token", out var token))
                    context.Token = token.ToString();
                return Task.CompletedTask;
            }
        };
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

// Enable Swagger UI (Enabled in all environments including Production/Render)
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "ChessGame API v1");
    options.RoutePrefix = "swagger";
});

// Tạo MongoDB indexes
await ChessGame.Api.Services.Ratings.RatingMigration.InitializeAsync(app.Services.GetRequiredService<IMongoDatabase>());
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
    await scope.ServiceProvider.GetRequiredService<GachaService>().EnsureIndexesAsync();
    await scope.ServiceProvider.GetRequiredService<OnlineStore>().EnsureIndexesAsync();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<GameHub>("/hubs/game", options => options.CloseOnAuthenticationExpiration = true);

app.Run();
