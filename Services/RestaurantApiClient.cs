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
            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("signin", content);
            response.EnsureSuccessStatusCode();

            var resString = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(resString);
            return doc.RootElement.GetProperty("idToken").GetString();
        }

        public async Task<string> GetLatestReservationIdAsync()
        {
            var response = await _httpClient.GetAsync("reservations");
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
            response.EnsureSuccessStatusCode();

            var resString = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(resString);
            return doc.RootElement.GetProperty("token").GetString();
        }

        public async Task<string> GetGroupReservationIdAsync(string inviteToken)
        {
            using var anonymousClient = new HttpClient { BaseAddress = new Uri(_apiBaseUrl) };
            var response = await anonymousClient.GetAsync($"guest/invites/{inviteToken}");
            response.EnsureSuccessStatusCode();

            var resString = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(resString);
            return doc.RootElement.GetProperty("GroupReservationId").GetString();
        }

        public async Task<HttpResponseMessage> GetGroupSessionResponseAsync(string groupId)
        {
            return await _httpClient.GetAsync($"guest/groups/{groupId}/sessions");
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

            throw new Exception("Масив страв порожній або невалідний.");
        }

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
            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("reservations", content);
            response.EnsureSuccessStatusCode();

            var resString = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(resString);
            return doc.RootElement.GetProperty("id").GetString();
        }

        public async Task<string> CreateGuestSessionAsync(string groupReservationId, string guestName)
        {
            using var anonymousClient = new HttpClient { BaseAddress = new Uri(_apiBaseUrl) };
            var body = new { groupReservationId, guestName };
            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            var response = await anonymousClient.PostAsync("guest/sessions", content);
            response.EnsureSuccessStatusCode();

            var resString = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(resString);
            
            if (doc.RootElement.TryGetProperty("SessionToken", out var pascalProp)) return pascalProp.GetString();
            if (doc.RootElement.TryGetProperty("sessionToken", out var camelProp)) return camelProp.GetString();

            throw new KeyNotFoundException("Не знайдено токен сесії гостя.");
        }

        public async Task AddDishToGuestSessionAsync(string guestSessionId, string dishId, int quantity)
        {
            using var anonymousClient = new HttpClient { BaseAddress = new Uri(_apiBaseUrl) };
            var body = new { dishId, quantity };
            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            var response = await anonymousClient.PostAsync($"guest/sessions/{guestSessionId}/dishes", content);
            response.EnsureSuccessStatusCode();
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
