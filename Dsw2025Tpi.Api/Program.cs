using System.Reflection;
using Dsw2025Tpi.Application.Services;
using Dsw2025Tpi.Data;
using Dsw2025Tpi.Data.Repositories;
using Dsw2025Tpi.Domain.Entities;
using Dsw2025Tpi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Dsw2025Tpi.Data.Helpers;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.OpenApi.Models;

namespace Dsw2025Tpi.Api;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // ---------------------------------
        //           CORS
        // ---------------------------------
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowFrontend", policy =>
            {
                policy
                    .WithOrigins("http://localhost:5173")
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        // Controllers
        builder.Services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });

        builder.Services.AddEndpointsApiExplorer();

        // Swagger + JWT
        builder.Services.AddSwaggerGen(c =>
        {
            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            c.IncludeXmlComments(xmlPath);

            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Desarrollo de Software",
                Version = "v1",
            });

            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                In = ParameterLocation.Header,
                Name = "Authorization",
                Description = "Ingresar el token",
                Type = SecuritySchemeType.ApiKey
            });

            c.AddSecurityRequirement(new OpenApiSecurityRequirement
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

        builder.Services.AddHealthChecks();

        // Identity
        builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
        {
            options.Password = new PasswordOptions
            {
                RequiredLength = 8,
            };
        })
        .AddEntityFrameworkStores<Dsw2025TpiContext>()
        .AddDefaultTokenProviders();

        // JWT
        var jwtConfig = builder.Configuration.GetSection("Jwt");
        var keyText = jwtConfig["Key"];
        if (string.IsNullOrWhiteSpace(keyText) || Encoding.UTF8.GetByteCount(keyText) < 32)
        {
            throw new InvalidOperationException("Configure Jwt:Key with at least 32 UTF-8 bytes using environment variables or user secrets.");
        }
        var key = Encoding.UTF8.GetBytes(keyText);

        var adminEmail = builder.Configuration["BootstrapAdmin:Email"];
        var adminUserName = builder.Configuration["BootstrapAdmin:UserName"];
        var adminPassword = builder.Configuration["BootstrapAdmin:Password"];
        var bootstrapAdmin = adminEmail != null || adminUserName != null || adminPassword != null;
        if (bootstrapAdmin && (string.IsNullOrWhiteSpace(adminEmail)
            || string.IsNullOrWhiteSpace(adminUserName) || string.IsNullOrWhiteSpace(adminPassword)))
        {
            throw new InvalidOperationException("Configure BootstrapAdmin:Email, BootstrapAdmin:UserName and BootstrapAdmin:Password together, or omit all three.");
        }

        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtConfig["Issuer"],
                ValidAudience = jwtConfig["Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(key)
            };
        });

        // DBContexts
        builder.Services.AddDbContext<Dsw2025TpiContext>(options =>
        {
            options.UseSqlServer(builder.Configuration.GetConnectionString("Dsw2025TpiEntities"));
            //options.UseSeeding((c, t) =>
            //{
            //    ((Dsw2025TpiContext)c).Seedwork<Customer>("Sources\\customers.json");
            //});
        });

        //builder.Services.AddDbContext<AuthenticateContext>(options =>
        //{
        //    options.UseSqlServer(builder.Configuration.GetConnectionString("Dsw2025TpiEntities"));
        //});

        // Services
        builder.Services.AddTransient<IRepository, EfRepository>();
        builder.Services.AddScoped<ProductsManagementService>();
        builder.Services.AddScoped<OrdersManagementService>();
        builder.Services.AddSingleton<JwtTokenService>();

        var app = builder.Build();

        // ---------------------------------
        //   CREAR ROLES Y USUARIO ADMIN
        // ---------------------------------
        using (var scope = app.Services.CreateScope())
        {
            var services = scope.ServiceProvider;

            var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = services.GetRequiredService<UserManager<IdentityUser>>();

            // 1) Crear roles ADMIN y USER si no existen
            string[] roles = { "ADMIN", "USER" };

            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole(role));
                }
            }

            // Bootstrap an administrator only with explicit credentials.
            if (bootstrapAdmin)
            {
                var existingAdmin = await userManager.FindByEmailAsync(adminEmail!);

                if (existingAdmin == null)
                {
                    var adminUser = new IdentityUser
                    {
                        UserName = adminUserName,
                        Email = adminEmail,
                        EmailConfirmed = true
                    };

                    var createResult = await userManager.CreateAsync(adminUser, adminPassword!);

                    if (!createResult.Succeeded)
                    {
                        throw new InvalidOperationException("Bootstrap administrator creation failed. Check the configured credentials against the Identity requirements.");
                    }

                    var roleResult = await userManager.AddToRoleAsync(adminUser, "ADMIN");
                    if (!roleResult.Succeeded)
                    {
                        throw new InvalidOperationException("Bootstrap administrator role assignment failed. Resolve the account's role before retrying.");
                    }
                }
            }
        }


        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseHttpsRedirection();

        app.UseCors("AllowFrontend");

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();
        app.MapHealthChecks("/healthcheck");

        app.Run();
    }
}
