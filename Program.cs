using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SmartInventory.Authorization;
using SmartInventory.Data;
using SmartInventory.Services;

var builder = WebApplication.CreateBuilder(args);

// === Провайдер БД: SqlServer (по умолчанию, локально) ИЛИ Postgres (Render) ===
// Database:Provider = "SqlServer" | "Postgres". Ничего не заменяем — только выбор.
var dbProvider = builder.Configuration["Database:Provider"] ?? "SqlServer";

// Render отдаёт DATABASE_URL в формате postgres://user:pass@host/db —
// конвертируем в строку Npgsql Key=Value. Иначе берём ConnectionStrings:Postgres.
static string ToNpgsql(string? url)
{
    if (string.IsNullOrWhiteSpace(url)) return "";
    if (!url.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
        && !url.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        return url; // уже Key=Value
    var uri = new Uri(url);
    var userInfo = uri.UserInfo.Split(':', 2);
    // SSL только для внешнего хоста (Render). На localhost/127.* — без него.
    var local = uri.Host is "localhost" or "127.0.0.1" or "::1";
    var ssl = local ? "Disable" : "Require";
    var cs = $"Host={uri.Host};Port={(uri.Port > 0 ? uri.Port : 5432)};Database={uri.AbsolutePath.TrimStart('/')};Username={userInfo[0]};Password={(userInfo.Length > 1 ? userInfo[1] : "")};SSL Mode={ssl};Trust Server Certificate=true";
    // sslmode из query (Render часто отдаёт ?sslmode=prefer/require)
    var q = uri.Query.TrimStart('?');
    foreach (var kv in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
    {
        var p = kv.Split('=', 2);
        if (p.Length == 2 && p[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase))
            cs = cs.Replace("SSL Mode=Require", $"SSL Mode={p[1]}");
    }
    return cs;
}

var isPostgres = string.Equals(dbProvider, "Postgres", StringComparison.OrdinalIgnoreCase);
var connectionString = isPostgres
    ? ToNpgsql(Environment.GetEnvironmentVariable("DATABASE_URL")
               ?? builder.Configuration.GetConnectionString("Postgres"))
    : builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (isPostgres)
        options.UseNpgsql(connectionString, x => x.MigrationsAssembly("SmartInventory"));
    else
        options.UseSqlServer(connectionString);
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.HttpOnly = true;
        // По HTTP (dev) браузер отказывается сохранять Secure-куку и вход "не держится".
        // Secure только в проде (HTTPS), в остальных — как пришло в запросе.
        options.Cookie.SecurePolicy = builder.Environment.IsProduction()
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
    });

builder.Services.AddScoped<IAuthorizationHandler, ManageRestaurantAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, ManageOrganizationAuthorizationHandler>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<HotOffersService>();
builder.Services.AddScoped<ShiftService>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient(nameof(AdvisorService));
builder.Services.AddScoped<AdvisorService>();

// SignalR + свип просрочки + шина событий склада
builder.Services.AddSignalR();
builder.Services.AddSingleton<InventoryEventBus>();
builder.Services.AddHostedService<ExpirySweepService>();
builder.Services.AddHostedService<AiInsightService>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(nameof(AppPolicy.CanManageRestaurant), policy =>
        policy.Requirements.Add(new ManageRestaurantRequirement()));
});

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddAutoMapper(cfg =>
{
    cfg.AddMaps(typeof(Program).Assembly);
});

var app = builder.Build();

// Render задаёт порт через env PORT; локально (dotnet run) его нет — тогда
// слушаем как обычно, ничего не переопределяем.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
    app.Urls.Add($"http://0.0.0.0:{port}");

// Сид только в Development ИЛИ по флагу SEED_DEMO=true (для демо-деплоя).
var shouldSeed = app.Environment.IsDevelopment()
    || string.Equals(Environment.GetEnvironmentVariable("SEED_DEMO"), "true", StringComparison.OrdinalIgnoreCase);
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    if (isPostgres)
    {
        // Postgres: свежая БД — создаём схему напрямую из модели.
        // Не используем MigrateAsync: Npgsql-дифф в EF10+N10 падает на
        // DateOnly/TimeOnly/enum-колонках при генерации миграции.
        await db.Database.EnsureCreatedAsync();
        // EnsureCreated НЕ патчит уже существующую схему (Render-БД создана
        // ранним деплоем). Добавляем новые колонки идемпотентно —
        // иначе любой запрос к Dishes падает «column does not exist».
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Dishes\" ADD COLUMN IF NOT EXISTS \"ManualStopped\" boolean NOT NULL DEFAULT FALSE");
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Dishes\" ADD COLUMN IF NOT EXISTS \"StopComment\" character varying(300) NULL");
        // Терминал кухни: колонка + заполнение для уже существующих ресторанов
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Restaurants\" ADD COLUMN IF NOT EXISTS \"KitchenToken\" uuid NULL");
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE \"Restaurants\" SET \"KitchenToken\" = gen_random_uuid() WHERE \"KitchenToken\" IS NULL OR \"KitchenToken\" = '00000000-0000-0000-0000-000000000000'");
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"Restaurants\" ALTER COLUMN \"KitchenToken\" SET NOT NULL");
        // AiInsight + LeadRequest — новые таблицы на существующей БД Render
        await db.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS ""AiInsights"" (
                ""Id"" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                ""RestaurantId"" integer NOT NULL,
                ""CreatedAt"" timestamp with time zone NOT NULL,
                ""Text"" text NOT NULL,
                ""ChipsJson"" text NOT NULL,
                ""UsedAi"" boolean NOT NULL)");
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_AiInsights_RestaurantId_CreatedAt\" ON \"AiInsights\" (\"RestaurantId\", \"CreatedAt\")");
        await db.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS ""LeadRequests"" (
                ""Id"" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                ""NetworkName"" character varying(120) NOT NULL,
                ""Contact"" character varying(120) NOT NULL,
                ""RestaurantCount"" integer NOT NULL,
                ""CreatedAt"" timestamp with time zone NOT NULL)");
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"LeadRequests\" ADD COLUMN IF NOT EXISTS \"Status\" character varying(20) NOT NULL DEFAULT 'новая'");
        // Привязка сотрудника к точке (nullable — NULL = берём из последней смены)
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"AccessCodes\" ADD COLUMN IF NOT EXISTS \"RestaurantId\" integer NULL");
    }
    else
    {
        // SQL Server: как раньше — через миграции.
        await db.Database.MigrateAsync();
    }
    if (shouldSeed)
        await DbInitializer.SeedAsync(scope.ServiceProvider);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    // За прокси Render корректные редиректы/URL — доверяем X-Forwarded-*.
    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                         | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
    });
}

app.UseStatusCodePagesWithReExecute("/Home/Error", "?statusCode={0}");

// Health-check для Render — до аутентификации.
app.MapGet("/health", () => Results.Ok("ok"));

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

// Временный дев-харнесс для ручной проверки OrderService — только в Development
if (app.Environment.IsDevelopment())
{
    app.MapControllerRoute(
        name: "dev",
        pattern: "Dev/{action=SeedStatus}",
        defaults: new { controller = "Dev" });
}

// SignalR-хаб склада
app.MapHub<SmartInventory.Hubs.InventoryHub>("/hubs/inventory");

app.Run();