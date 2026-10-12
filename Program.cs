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
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
    });

builder.Services.AddScoped<IAuthorizationHandler, ManageRestaurantAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, ManageOrganizationAuthorizationHandler>();
builder.Services.AddScoped<OrderService>();

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

app.Run();