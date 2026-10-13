using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

// EventGenerator — генератор нагрузки для демо «СВЕЖАК».
// Ходит по HTTP в служебные эндпоинты с авторизацией access-кодом.
//
//   --url=<baseUrl>           (или env SVEZHAK_URL)      — например http://localhost:5099
//   --code=<accessCode>       (или env SVEZHAK_CODE)     — MANAGER1234
//   --scenario=normal         — 3–5 заказов/мин, поставка раз в 10 мин
//   --scenario=rush           — 10–15 заказов/мин (демо стоп-листа)
//   --scenario=short-expiry   — поставки со сроком 4–8 ч (демо Hot Offers)

var url = Arg("--url") ?? Env("SVEZHAK_URL") ?? "http://localhost:5099";
var code = Arg("--code") ?? Env("SVEZHAK_CODE") ?? "MANAGER1234";
var scenario = (Arg("--scenario") ?? Env("SVEZHAK_SCENARIO") ?? "normal").ToLowerInvariant();

var rnd = new Random();
using var http = new HttpClient(new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer() });
http.BaseAddress = new Uri(url.TrimEnd('/') + "/");

// ---------- авторизация access-кодом ----------
async Task<bool> LoginAsync()
{
    var loginPage = await http.GetStringAsync("Account/Login");
    var token = Regex.Match(loginPage, @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""").Groups[1].Value;

    var form = new FormUrlEncodedContent(new Dictionary<string, string>
    {
        ["code"] = code,
        ["__RequestVerificationToken"] = token,
    });
    var resp = await http.PostAsync("Account/Login", form);
    return resp.IsSuccessStatusCode;
}

// ---------- вызовы симуляции ----------
async Task<string> OrderAsync(List<object> items)
{
    var resp = await http.PostAsJsonAsync("Inventory/SimulateOrder", new { items });
    var body = await resp.Content.ReadAsStringAsync();
    return resp.IsSuccessStatusCode ? body : $"HTTP {(int)resp.StatusCode}: {body}";
}

async Task<string> DeliveryAsync(string number, List<object> items)
{
    var resp = await http.PostAsJsonAsync("Inventory/SimulateDelivery", new { number, items });
    var body = await resp.Content.ReadAsStringAsync();
    return resp.IsSuccessStatusCode ? body : $"HTTP {(int)resp.StatusCode}: {body}";
}

// ---------- вспомогательные ----------
string? Arg(string name)
{
    var prefix = name + "=";
    return args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];
}
string? Env(string name) => Environment.GetEnvironmentVariable(name);
int Between(int a, int b) => rnd.Next(a, b + 1);
T Pick<T>(List<T> xs) => xs[rnd.Next(xs.Count)];

// ---------- запуск ----------
Console.WriteLine($"[gen] url={url} code={code} scenario={scenario}");

if (!await LoginAsync())
{
    Console.WriteLine("[gen] Не удалось войти — проверь url и code");
    return 1;
}
Console.WriteLine("[gen] Вход выполнен");

var J = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var menuJson = await http.GetStringAsync("Inventory/Menu");
var menu = JsonSerializer.Deserialize<Menu>(menuJson, J)!;
Console.WriteLine($"[gen] Меню: {menu.Dishes.Count} блюд, {menu.Products.Count} продуктов, restaurantId={menu.RestaurantId}");

int ordersPerMin, deliveriesEveryMin;
double minShelfH, maxShelfH;
switch (scenario)
{
    case "rush":
        ordersPerMin = Between(10, 15); deliveriesEveryMin = 10; minShelfH = 24; maxShelfH = 72;
        break;
    case "short-expiry":
        ordersPerMin = Between(3, 5); deliveriesEveryMin = 5; minShelfH = 4; maxShelfH = 8;
        break;
    default:
        ordersPerMin = Between(3, 5); deliveriesEveryMin = 10; minShelfH = 24; maxShelfH = 96;
        break;
}

var nextOrder = DateTime.UtcNow;
var nextDelivery = DateTime.UtcNow.AddSeconds(5);
var simNo = 0;

Console.WriteLine($"[gen] Старт: {ordersPerMin} зак/мин, поставка раз в {deliveriesEveryMin} мин, срок {minShelfH}–{maxShelfH} ч. Ctrl+C — выход.");

while (true)
{
    var now = DateTime.UtcNow;

    if (now >= nextOrder)
    {
        var items = new List<object>();
        for (var i = 0; i < Between(1, 2); i++)
        {
            var dish = Pick(menu.Dishes);
            var qty = scenario == "rush" ? Between(3, 10) : Between(1, 4);
            items.Add(new { dishId = dish.Id, qty });
        }
        Console.WriteLine($"[order] {await OrderAsync(items)}");
        nextOrder = now.AddSeconds(60.0 / ordersPerMin);
    }

    if (now >= nextDelivery)
    {
        simNo++;
        var items = new List<object>();
        for (var i = 0; i < Between(2, 5); i++)
        {
            var p = Pick(menu.Products);
            var qty = Math.Round(Between(1, 10) + (decimal)rnd.NextDouble(), 2);
            var shelfHours = Math.Round(minShelfH + rnd.NextDouble() * (maxShelfH - minShelfH), 1);
            items.Add(new { productId = p.Id, qty, shelfHours });
        }
        Console.WriteLine($"[delivery] {await DeliveryAsync($"SIM-{simNo:000}", items)}");
        nextDelivery = now.AddMinutes(deliveriesEveryMin);
    }

    await Task.Delay(500);
}

// ---------- модели меню ----------
record Dish(int Id, string Name, decimal Price);
record Product(int Id, string Name, string Unit);
record Menu(int RestaurantId, List<Dish> Dishes, List<Product> Products);
