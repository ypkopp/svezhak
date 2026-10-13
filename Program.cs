using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SmartInventory.Authorization;
using SmartInventory.Data;
using SmartInventory.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

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

// SignalR + свип просрочки + шина событий склада
builder.Services.AddSignalR();
builder.Services.AddSingleton<InventoryEventBus>();
builder.Services.AddHostedService<ExpirySweepService>();

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

// Применяем миграции и наполняем пустую БД демо-данными
using (var scope = app.Services.CreateScope())
{
    await DbInitializer.SeedAsync(scope.ServiceProvider);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/Home/Error", "?statusCode={0}");

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