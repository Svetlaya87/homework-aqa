using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace APIHandler.Tests.AqaHomework.Services
{
    public class RestaurantApiClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiBaseUrl;

        public RestaurantApiClient(string baseUrl)
        {
            _apiBaseUrl = baseUrl;
            _httpClient = new HttpClient { BaseAddress = new Uri(_apiBaseUrl) };
        }

        public void SetAuthToken(string token)
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        public void ClearAuthToken()
        {
            _httpClient.DefaultRequestHeaders.Authorization = null;
        }

        public async Task<string> SignInAsync(string email, string password)
        {
            var body = new { email, password };
            var jsonString = JsonSerializer.Serialize(body);
            var content = new StringContent(jsonString, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("signin", content);
            await LogHttpCallAsync("POST", "signin", jsonString, response);
            response.EnsureSuccessStatusCode();

            var resString = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(resString);
            return doc.RootElement.GetProperty("idToken").GetString();
        }

        // POST /reservations -> Створення нового бронювання за валідним tableId
        // POST /reservations -> Створення нового бронювання за обраним столом та слотом часу
public async Task<string> CreateReservationAsync(string tableId, string startTime, string endTime, int guestCount)
{
    var body = new 
    { 
        tableId,
        customerEmail = (string?)null,
        startTime, 
        endTime, 
        guestCount,
        customerId = (string?)null
    };
    var jsonString = JsonSerializer.Serialize(body);
    var content = new StringContent(jsonString, Encoding.UTF8, "application/json");

    var response = await _httpClient.PostAsync("reservations", content);
    await LogHttpCallAsync("POST", "reservations", jsonString, response);
    response.EnsureSuccessStatusCode();

    var resString = await response.Content.ReadAsStringAsync();
    using var doc = JsonDocument.Parse(resString);
    return doc.RootElement.GetProperty("id").GetString();
}


        public async Task<string> GetLatestReservationIdAsync()
        {
            var response = await _httpClient.GetAsync("reservations");
            await LogHttpCallAsync("GET", "reservations", null, response);
            response.EnsureSuccessStatusCode();

            var resString = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(resString);

            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
            {
                return doc.RootElement[0].GetProperty("id").GetString();
            }
            if (doc.RootElement.TryGetProperty("id", out var idProp))
            {
                return idProp.GetString();
            }

            throw new Exception("Неможливо знайти валідний Reservation ID.");
        }

        public async Task<string> GetInviteTokenAsync(string reservationId)
        {
            var response = await _httpClient.GetAsync($"reservations/{reservationId}/invite-token");
            await LogHttpCallAsync("GET", $"reservations/{reservationId}/invite-token", null, response);
            response.EnsureSuccessStatusCode();

            var resString = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(resString);
            return doc.RootElement.GetProperty("token").GetString();
        }

        public async Task<string> GetGroupReservationIdAsync(string inviteToken)
{
    using var anonymousClient = new HttpClient { BaseAddress = new Uri(_apiBaseUrl) };
    var response = await anonymousClient.GetAsync($"guest/invites/{inviteToken}");
    await LogHttpCallAsync("GET", $"guest/invites/{inviteToken}", null, response);
    response.EnsureSuccessStatusCode();

    var resString = await response.Content.ReadAsStringAsync();
    using var doc = JsonDocument.Parse(resString);
    
    // БЕЗПЕЧНИЙ ПОШУК КЛЮЧА: спочатку пробуємо CamelCase, якщо ні — PascalCase
    if (doc.RootElement.TryGetProperty("groupReservationId", out var camelProp))
    {
        return camelProp.GetString() ?? throw new Exception("groupReservationId є порожнім");
    }
    if (doc.RootElement.TryGetProperty("GroupReservationId", out var pascalProp))
    {
        return pascalProp.GetString() ?? throw new Exception("GroupReservationId є порожнім");
    }

    throw new KeyNotFoundException($"Не вдалося знайти ключ ідентифікатора групи у відповіді API. Отриманий JSON: {resString}");
}


        // POST /guest/sessions -> Створення сесії гостя (анонімно)
        public async Task<string> CreateGuestSessionAsync(string groupReservationId, string guestName)
        {
            using var anonymousClient = new HttpClient { BaseAddress = new Uri(_apiBaseUrl) };
            var body = new { groupReservationId, guestName };
            var jsonString = JsonSerializer.Serialize(body);
            var content = new StringContent(jsonString, Encoding.UTF8, "application/json");

            var response = await anonymousClient.PostAsync("guest/sessions", content);
            await LogHttpCallAsync("POST", "guest/sessions", jsonString, response);
            response.EnsureSuccessStatusCode();

            var resString = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(resString);
            if (doc.RootElement.TryGetProperty("SessionToken", out var pascalProp))
                {
                    return pascalProp.GetString();
                }
            if (doc.RootElement.TryGetProperty("sessionToken", out var camelProp))
            {
                return camelProp.GetString();
            }

    throw new KeyNotFoundException($"Не знайдено токен сесії у відповіді: {resString}");
        }

        // POST /guest/sessions/{id}/dishes -> Додавання страви в кошик сесії гостя
        public async Task AddDishToGuestSessionAsync(string guestSessionId, string dishId, int quantity)
        {
            using var anonymousClient = new HttpClient { BaseAddress = new Uri(_apiBaseUrl) };
            var body = new { dishId, quantity };
            var jsonString = JsonSerializer.Serialize(body);
            var content = new StringContent(jsonString, Encoding.UTF8, "application/json");

            var response = await anonymousClient.PostAsync($"guest/sessions/{guestSessionId}/dishes", content);
            await LogHttpCallAsync("POST", $"guest/sessions/{guestSessionId}/dishes", jsonString, response);
            response.EnsureSuccessStatusCode();
        }

        // GET /guest/groups/{id}/sessions -> Перегляд сесій учасників Owner-ом
        public async Task<HttpResponseMessage> GetGroupSessionResponseAsync(string groupId)
        {
            return await _httpClient.GetAsync($"guest/groups/{groupId}/sessions");
        }

        public async Task<string> GetLocationIdByAddressAsync(string targetAddress)
        {
            using var anonymousClient = new HttpClient { BaseAddress = new Uri(_apiBaseUrl) };
            var response = await anonymousClient.GetAsync("locations");
            response.EnsureSuccessStatusCode();

            var resString = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(resString);

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var location in doc.RootElement.EnumerateArray())
                {
                    if (location.TryGetProperty("address", out var addrProp) && addrProp.GetString() == targetAddress)
                    {
                        return location.GetProperty("id").GetString();
                    }
                }
            }

            throw new Exception($"Локацію з адресою '{targetAddress}' не знайдено в базі даних.");
        }

        public async Task<(string Id, double Price)> GetFirstAvailableDishAsync()
        {
            using var anonymousClient = new HttpClient { BaseAddress = new Uri(_apiBaseUrl) };
            var response = await anonymousClient.GetAsync("dishes"); 
            response.EnsureSuccessStatusCode();

            var resString = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(resString);

            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
            {
                var firstDish = doc.RootElement[0];
                string id = firstDish.GetProperty("id").GetString() ?? throw new Exception("ID страви відсутній");
                double price = firstDish.GetProperty("price").GetDouble();
                return (id, price);
            }

            throw new Exception("Масив страв порожній.");
        }

        private async Task LogHttpCallAsync(string method, string endpoint, string? requestBody, HttpResponseMessage response)
        {
            Console.WriteLine($"\n=== [HTTP {method}] {endpoint} ===");
            if (!string.IsNullOrEmpty(requestBody)) Console.WriteLine($"-> REQ: {requestBody}");
            Console.WriteLine($"<- STATUS: {(int)response.StatusCode} {response.StatusCode}");
            var responseBody = await response.Content.ReadAsStringAsync();
            if (!string.IsNullOrEmpty(responseBody)) Console.WriteLine($"<- RES: {responseBody}");
            Console.WriteLine("=====================================\n");
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
