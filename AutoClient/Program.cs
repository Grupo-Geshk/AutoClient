using AutoClient.Data;
using AutoClient.Services;
using AutoClient.Settings;
using AutoClient.Services.Email;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// === CONFIGURACIONES BASE ===
DotNetEnv.Env.Load();

// DB
var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
    ?? throw new InvalidOperationException("Falta ConnectionStrings__DefaultConnection");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

// Controllers + FluentValidation
builder.Services.AddControllers()
    .AddFluentValidation(config =>
        config.RegisterValidatorsFromAssemblyContaining<Program>());

// SMTP (una sola vez, por env vars)
builder.Services.Configure<SmtpSettings>(options =>
{
    options.Host = Environment.GetEnvironmentVariable("Smtp__Host") ?? "";
    options.Port = int.Parse(Environment.GetEnvironmentVariable("Smtp__Port") ?? "587");
    options.Username = Environment.GetEnvironmentVariable("Smtp__Username") ?? "";
    options.Password = Environment.GetEnvironmentVariable("Smtp__Password") ?? "";
    options.SenderName = Environment.GetEnvironmentVariable("Smtp__SenderName") ?? "AutoClient";
    options.SenderEmail = Environment.GetEnvironmentVariable("Smtp__SenderEmail") ?? "no-reply@example.com";
});

// JWT
var jwtKey = Environment.GetEnvironmentVariable("Jwt__Key")
    ?? throw new InvalidOperationException("Falta Jwt__Key");
var jwtIssuer = Environment.GetEnvironmentVariable("Jwt__Issuer")
    ?? throw new InvalidOperationException("Falta Jwt__Issuer");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

// Servicios propios
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<IInvoiceMailer, InvoiceMailer>();  // mailer HTML + PDF

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "AutoClient API", Version = "v1" });

    var jwtScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Add your JWT token",
        Reference = new OpenApiReference
        {
            Id = JwtBearerDefaults.AuthenticationScheme,
            Type = ReferenceType.SecurityScheme
        }
    };

    c.AddSecurityDefinition(jwtScheme.Reference.Id, jwtScheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement { { jwtScheme, Array.Empty<string>() } });
});

// === CORS ===
var allowedOrigins = (Environment.GetEnvironmentVariable("AllowedOrigins")
                     ?? "http://localhost:5173,https://autoclientst.grupogeshk.com")
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontends", p =>
        p.WithOrigins(allowedOrigins)
         .AllowAnyHeader()
         .AllowAnyMethod()
         .WithExposedHeaders("Content-Disposition")
         .AllowCredentials());
});

var app = builder.Build();

// === PIPELINE ===
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseCors("Frontends");
app.UseAuthentication();
app.UseAuthorization();

// Preflight helper (opcional)
app.MapMethods("{*path}", new[] { "OPTIONS" }, () => Results.Ok());

app.MapControllers();
app.Run();
