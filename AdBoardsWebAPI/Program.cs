using System.Text;
using AdBoards.Domain.Enums;
using AdBoardsWebAPI.Auth;
using AdBoardsWebAPI.Data;
using AdBoardsWebAPI.Extensions;
using AdBoardsWebAPI.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Services configuration.
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));

builder.Services.AddCors();
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Policies.NormalUser, pb =>
    {
        pb.AuthenticationSchemes.Add(JwtBearerDefaults.AuthenticationScheme);
        pb.RequireAuthenticatedUser();
    });
    options.AddPolicy(Policies.Admin, pb =>
    {
        pb.AuthenticationSchemes.Add(JwtBearerDefaults.AuthenticationScheme);
        pb.RequireAuthenticatedUser();
        pb.RequireClaim("rightId", RightType.Admin.ToString());
    });

    options.DefaultPolicy = options.GetPolicy(Policies.NormalUser)!;
    options.FallbackPolicy = options.GetPolicy(Policies.NormalUser)!;
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("JWT", new OpenApiSecurityScheme
    {
        Description = "JWT with Bearer",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "ApiKeyScheme"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                In = ParameterLocation.Header,
                Reference = new OpenApiReference
                {
                    Id = "JWT",
                    Type = ReferenceType.SecurityScheme
                }
            },
            new List<string>()
        }
    });
});

builder.Services.AddDbContext<AdBoardsContext>(o => o.UseNpgsql(builder.Configuration["ConnectionStrings:AdBoards"]));
builder.Services.AddDirectoryBrowser();

// Custom services.
builder.Services.AddScoped<FileManager>();

var app = builder.Build();

// App configuration.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseStaticFiles();

var fileProvider = new PhysicalFileProvider(Path.Combine(builder.Environment.WebRootPath, "images"));
const string requestPath = "/images";

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = fileProvider,
    RequestPath = requestPath
});

app.UseDirectoryBrowser(new DirectoryBrowserOptions
{
    FileProvider = fileProvider,
    RequestPath = requestPath
});

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Mapping.
var api = app.MapGroup("api/");

api.MapAdEndpoints();
api.MapComplaintEndpoints();
api.MapFavoritesEndpoints();
api.MapPeopleEndpoints();

// Starting the app.
app.Run();