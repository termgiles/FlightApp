using FlightApp.Repository;
using FlightApp.Database;
using FlightApp.Models;
using Microsoft.AspNetCore.Identity;
using FlightApp.Service;
using FlightApp.Mapper;
using Microsoft.EntityFrameworkCore;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using FlightApp.Helpers;
using FlightApp.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.Text.Json;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.InMemory;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "FlightApp", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Please Bearer and then token in the field",
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement {
                   {
                     new OpenApiSecurityScheme
                     {
                       Reference = new OpenApiReference
                       {
                         Type = ReferenceType.SecurityScheme,
                         Id = "Bearer"
                       }
                      },
                      new string[] { }
                    }
                });
});

builder.Services.AddHttpClient("FlightApi", client =>
    client.BaseAddress = new Uri(builder.Configuration.GetValue<string>("FlightApi")!));

//builder.Services.AddDbContext<FlightAppDbContext>(options=>
//options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddDbContext<FlightAppDbContext>(options =>
options.UseInMemoryDatabase("FlightAppInMemory"));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
    .AddDefaultTokenProviders()
    .AddEntityFrameworkStores<FlightAppDbContext>();
var apiAuthSettingsSection = builder.Configuration.GetSection("ApiAuthenticationSettings");
builder.Services.Configure<ApiAuthenticationSettings>(apiAuthSettingsSection);

var apiSettings = apiAuthSettingsSection.Get<ApiAuthenticationSettings>();
var key = Encoding.ASCII.GetBytes(apiSettings.SecretKey);

builder.Services.AddAuthentication(opt =>
{
    opt.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    opt.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    opt.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(x =>
{
    x.RequireHttpsMetadata = false;
    x.SaveToken = true;
    x.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidAudience = apiSettings.ValidAudience,
        ValidIssuer = apiSettings.ValidIssuer,
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddDbContext<FlightAppDbContext>();

builder.Services.AddScoped<IFlightApiRepository, FlightApiRepository>();
builder.Services.AddScoped<IFlightService, FlightApiService>();
builder.Services.AddScoped<INotesRepository, NotesRepository>();
builder.Services.AddScoped<INotesService, NotesService>();
builder.Services.AddScoped<IRepliesRepository, RepliesRepository>();
builder.Services.AddScoped<IRepliesService, RepliesService>();
builder.Services.AddScoped<IVotesService, VotesService>();
builder.Services.AddScoped<IVotesRepository, VotesRepository>();
builder.Services.AddScoped<INotificationsService, NotificationsService>();
builder.Services.AddScoped<INotificationsRepository, NotificationsRepository>();

builder.Services.AddAutoMapper(typeof(MapperProfile).Assembly);
builder.Services.AddScoped<IAccountService, AccountService>();

builder.Services.AddHealthChecks()
    .AddCheck<AviationApiHealthCheck>("AviationApiHealthCheck", failureStatus: HealthStatus.Unhealthy)
    .AddCheck<FlightAppDBHealthCheck>("FlightAppDBHealthCheck", failureStatus: HealthStatus.Unhealthy)
    .AddCheck<WeatherApiHealthcheck>("WeatherApiHealthCheck", failureStatus: HealthStatus.Unhealthy)
    .AddCheck<OpenCageDataHealthCheck>("OpenCageDataHealthCheck", failureStatus: HealthStatus.Unhealthy)
    .AddCheck<PlaneSpottersHealthCheck>("PlaneSpottersHealthCheck", failureStatus: HealthStatus.Unhealthy)
    .AddCheck<MapDataHealthCheck>("MapDataHealthCheck", failureStatus: HealthStatus.Unhealthy)
    .AddCheck<AirLineLogosHealthCheck>("AirLineLogosHealthCheck", failureStatus: HealthStatus.Unhealthy);

builder.Services.AddRateLimiter(ops => ops
    .AddPolicy("token", httpContext =>
    RateLimitPartition.GetTokenBucketLimiter(httpContext.User.Identity?.Name ?? httpContext.Request.Headers.Host.ToString(),
    partition => new TokenBucketRateLimiterOptions
    {
        TokenLimit = 7,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit = 1,
        ReplenishmentPeriod = TimeSpan.FromSeconds(45),
        TokensPerPeriod = 7,
        AutoReplenishment = true
    })));

builder.Services.AddMemoryCache();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var response = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description
            }),
            totalDuration = report.TotalDuration
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }
});

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.UseRateLimiter();
app.Run();
