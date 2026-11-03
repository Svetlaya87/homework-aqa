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

            // ВИПРАВЛЕНО: відповідь є масивом, беремо "id" з першого об'єкта масиву
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
            
            // ВИПРАВЛЕНО: Ключ у JSON має назву "token", а не "inviteToken"
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

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
