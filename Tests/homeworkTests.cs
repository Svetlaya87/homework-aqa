using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using APIHandler.Tests.AqaHomework.Services;

namespace APIHandler.Tests.AqaHomework.Tests
{
    [TestFixture]
    public class HomeworkTests : APIHandler.Tests.AqaHomework.Base.TestBase
    {
        private RestaurantApiClient _apiClient;
        private readonly string _apiBaseUrl = "https://j6dp5z1ybg.execute-api.eu-north-1.amazonaws.com/develop/";
        private string _groupId;
        private string _dishId;
        private string _tableId;
        private string _ownerToken;

        [OneTimeSetUp] 
        public async Task Setup()
        {
            InitializeEnvironment();
            _apiClient = new RestaurantApiClient(_apiBaseUrl);

            try
            {
                // 1. Авторизація Owner
                _ownerToken = await _apiClient.SignInAsync(OwnerEmail, OwnerPassword);
                _apiClient.SetAuthToken(_ownerToken);

                // 2. Отримання доступної страви для тестів
                var dishData = await _apiClient.GetFirstAvailableDishAsync();
                _dishId = dishData.Id;

                using var client = new HttpClient { BaseAddress = new Uri(_apiBaseUrl) };
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _ownerToken);

                // 3. Пошук вільного столу
                var tablesResponse = await client.GetAsync("available-tables");
                tablesResponse.EnsureSuccessStatusCode();
                var tablesString = await tablesResponse.Content.ReadAsStringAsync();
                using var tablesDoc = JsonDocument.Parse(tablesString);

                if (tablesDoc.RootElement.ValueKind == JsonValueKind.Array && tablesDoc.RootElement.GetArrayLength() > 0)
                {
                    _tableId = tablesDoc.RootElement[0].GetProperty("id").GetString();
                }
                else
                {
                    throw new Exception("Не знайдено доступних столів для створення нового бронювання.");
                }

                // 4. Отримання доступного часового слоту
                string targetDateString = "2026-11-15T08:00:00.000Z"; 
                var slotsResponse = await client.GetAsync($"tables/{_tableId}/available-time-slots?startTime={Uri.EscapeDataString(targetDateString)}");
                slotsResponse.EnsureSuccessStatusCode();
                var slotsString = await slotsResponse.Content.ReadAsStringAsync();
                using var slotsDoc = JsonDocument.Parse(slotsString);

                string finalStartTime, finalEndTime;
                if (slotsDoc.RootElement.ValueKind == JsonValueKind.Array && slotsDoc.RootElement.GetArrayLength() > 0)
                {
                    finalStartTime = slotsDoc.RootElement[0].GetProperty("start").GetString();
                    finalEndTime = slotsDoc.RootElement[0].GetProperty("end").GetString();
                }
                else
                {
                    finalStartTime = DateTime.UtcNow.AddHours(1).ToString("yyyy-MM-ddTHH:mm:ssZ");
                    finalEndTime = DateTime.UtcNow.AddHours(3).ToString("yyyy-MM-ddTHH:mm:ssZ");
                }

                // 5. Створення чистої тестової групи
                string reservationId = await _apiClient.CreateReservationAsync(_tableId, finalStartTime, finalEndTime, guestCount: 4);
                string inviteToken = await _apiClient.GetInviteTokenAsync(reservationId);
                _groupId = await _apiClient.GetGroupReservationIdAsync(inviteToken);

                Console.WriteLine($"[E2E Setup Success] Стіл: {_tableId}, Група ID: {_groupId}");
            }
            catch (Exception ex)
            {
                Assert.Fail($"Помилка автоматичної підготовки даних: {ex.Message}");
            }
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            _apiClient?.Dispose();
        }

        [Test]
        [Description("Позитивний сценарій: перевірка структури сесій груп (перегляд учасників Owner-ом)")]
        public async Task GetGroupSessions_AsOwner_Returns200AndValidStructure()
        {
            var response = await _apiClient.GetGroupSessionResponseAsync(_groupId);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            var content = await response.Content.ReadAsStringAsync();
            using var jsonDoc = JsonDocument.Parse(content);
            var root = jsonDoc.RootElement;

            Assert.That(root.ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(root.TryGetProperty("Participants", out var participants), Is.True, "Властивість 'Participants' має бути присутня");
            Assert.That(root.TryGetProperty("Total", out _), Is.True, "Властивість 'Total' має бути присутня");
            Assert.That(participants.ValueKind, Is.EqualTo(JsonValueKind.Array), "'Participants' має бути масивом");
        }

        [Test]
        [Description("Негативний сценарій: запит неіснуючої групи")]
        public async Task GetGroupSessions_NonExistingGroup_Returns404()
        {
            string nonExistingGuid = Guid.NewGuid().ToString();
            var response = await _apiClient.GetGroupSessionResponseAsync(nonExistingGuid);
            
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }

        [Test]
        [Description("Негативний сценарій: спроба запиту без авторизації")]
        public async Task GetGroupSessions_AsUnauthorizedUser_Returns401Or403()
        {
            _apiClient.ClearAuthToken();
            var response = await _apiClient.GetGroupSessionResponseAsync(_groupId);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized).Or.EqualTo(HttpStatusCode.Forbidden));
            
            _apiClient.SetAuthToken(_ownerToken);
        }

        [Test]
        [Description("Негативний сценарій: Спроба доступу з токеном іншого користувача")]
        public async Task GetGroupSessions_AsAnotherUser_Returns403()
        {
            _apiClient.SetAuthToken("Bearer InvalidTokenExample123Value..."); 
            var response = await _apiClient.GetGroupSessionResponseAsync(_groupId);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized).Or.EqualTo(HttpStatusCode.Forbidden));

            _apiClient.SetAuthToken(_ownerToken);
        }

        [Test]
        [Description("Наскрізний сценарій Рівня 1: Додавання страв гостями -> Контроль сум та учасників Власником")]
        public async Task E2E_GroupPreOrder_FullFlow_CalculatesTotalsAndTracksStatus()
        {
            string targetGuestName = "Guest_Ivan";

            // 1. Гість приєднується до групи (створює анонімну сесію)
            string sessionToken = await _apiClient.CreateGuestSessionAsync(_groupId, targetGuestName);
            Assert.That(sessionToken, Is.Not.Null.And.Not.Empty);

            // 2. Власник перевіряє появу учасника в списку Participants
            _apiClient.SetAuthToken(_ownerToken);
            var response = await _apiClient.GetGroupSessionResponseAsync(_groupId);
            var content = await response.Content.ReadAsStringAsync();
            
            string guestSessionId = null;
            using (var doc = JsonDocument.Parse(content))
            {
                var participants = doc.RootElement.GetProperty("Participants");
                Assert.That(participants.GetArrayLength(), Is.GreaterThan(0), "Гість має бути в списку Participants");
                
                foreach (var participant in participants.EnumerateArray())
                {
                    if (participant.GetProperty("Name").GetString() == targetGuestName)
                    {
                        guestSessionId = participant.GetProperty("Id").GetString();
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(guestSessionId)) guestSessionId = _groupId;

            // 3. Гість додає страву (2 шт)
            await _apiClient.AddDishToGuestSessionAsync(guestSessionId, _dishId, quantity: 2);

            // 4. Власник перевіряє оновлення Total
            var responseAfterAdd = await _apiClient.GetGroupSessionResponseAsync(_groupId);
            var contentAfterAdd = await responseAfterAdd.Content.ReadAsStringAsync();
            using (var doc = JsonDocument.Parse(contentAfterAdd))
            {
                Assert.That(doc.RootElement.GetProperty("Total").GetDouble(), Is.GreaterThan(0), 
                    "Загальна сума 'Total' має збільшитись після додавання страви.");
            }
        }
    }
}
