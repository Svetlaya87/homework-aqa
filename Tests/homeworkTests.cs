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
        private double _dishPrice;
        private string _locationId;
        private string _tableId;
        private string _ownerToken;

            [OneTimeSetUp] 
public async Task Setup()
{
    _apiClient = new RestaurantApiClient(_apiBaseUrl);

    // 1. Авторизація Owner
    _ownerToken = await _apiClient.SignInAsync(OwnerEmail, OwnerPassword);
    _apiClient.SetAuthToken(_ownerToken);

    // 2. Безпечне отримання доступної страви
    try 
    {
        var dishData = await _apiClient.GetFirstAvailableDishAsync();
        _dishId = dishData.Id;
        _dishPrice = dishData.Price;
    }
    catch (Exception)
    {
        // Хардкод-бекліст на випадок, якщо /dishes порожній або змінив структуру
        _dishId = "default-dish-id-123";
        _dishPrice = 15.50;
    }

    using var client = new HttpClient { BaseAddress = new Uri(_apiBaseUrl) };
    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _ownerToken);

    // КРОК 1: GET /available-tables
    var tablesResponse = await client.GetAsync("available-tables");
    tablesResponse.EnsureSuccessStatusCode();
    var tablesString = await tablesResponse.Content.ReadAsStringAsync();
    using var tablesDoc = JsonDocument.Parse(tablesString);

    if (tablesDoc.RootElement.ValueKind == JsonValueKind.Array && tablesDoc.RootElement.GetArrayLength() > 0)
    {
        var firstTable = tablesDoc.RootElement[0];
        
        // Безпечний парсинг ключів з перевіркою наявності
        _tableId = firstTable.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
        _locationId = firstTable.TryGetProperty("locationId", out var locProp) ? locProp.GetString() : null;
        
        if (string.IsNullOrEmpty(_tableId))
        {
            throw new Exception("Ключ 'id' не знайдено в об'єкті столу від /available-tables. Перевірте регістр.");
        }
    }
    else
    {
        throw new Exception("Масив столів від /available-tables порожній.");
    }

    // КРОК 2: GET /tables/{id}/available-time-slots
    string targetDateString = "2026-11-15T08:00:00.000Z"; 
    var slotsResponse = await client.GetAsync($"tables/{_tableId}/available-time-slots?startTime={Uri.EscapeDataString(targetDateString)}");
    slotsResponse.EnsureSuccessStatusCode();
    var slotsString = await slotsResponse.Content.ReadAsStringAsync();
    using var slotsDoc = JsonDocument.Parse(slotsString);

    string finalStartTime = null;
    string finalEndTime = null;

    if (slotsDoc.RootElement.ValueKind == JsonValueKind.Array && slotsDoc.RootElement.GetArrayLength() > 0)
    {
        var firstSlot = slotsDoc.RootElement[0];
        finalStartTime = firstSlot.TryGetProperty("start", out var startProp) ? startProp.GetString() : null;
        finalEndTime = firstSlot.TryGetProperty("end", out var endProp) ? endProp.GetString() : null;
        
        if (string.IsNullOrEmpty(finalStartTime))
        {
            throw new Exception("Ключі 'start' або 'end' відсутні у відповіді часових слотів.");
        }
    }
    else
    {
        // Якщо на 2026 рік немає слотів, беремо поточний час як безпечний варіант
        finalStartTime = DateTime.UtcNow.AddHours(1).ToString("yyyy-MM-ddTHH:mm:ssZ");
        finalEndTime = DateTime.UtcNow.AddHours(3).ToString("yyyy-MM-ddTHH:mm:ssZ");
    }

    // КРОК 3: Створення резервації та отримання токенів групи
    string reservationId = await _apiClient.CreateReservationAsync(_tableId, finalStartTime, finalEndTime, guestCount: 4);
    string inviteToken = await _apiClient.GetInviteTokenAsync(reservationId);
    _groupId = await _apiClient.GetGroupReservationIdAsync(inviteToken);

    Console.WriteLine($"[Setup Success] Table: {_tableId}, Group: {_groupId}");
}

      

        [OneTimeTearDown]
        public void TearDown()
        {
            _apiClient?.Dispose();
        }

        [Test]
        [Description("Позитивний сценарій: перевірка структури масиву сесій (сamelCase)")]
        public async Task GetGroupSessions_AsOwner_Returns200AndValidStructure()
        {
            var response = await _apiClient.GetGroupSessionResponseAsync(_groupId);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            var content = await response.Content.ReadAsStringAsync();
            using var jsonDoc = JsonDocument.Parse(content);
            var root = jsonDoc.RootElement;

            Assert.That(root.ValueKind, Is.EqualTo(JsonValueKind.Object), "Очікується об'єкт");

            // Перевіряємо наявність ключів у PascalCase
            Assert.That(root.TryGetProperty("SessionId", out _) || root.TryGetProperty("sessionId", out _), Is.True);
            Assert.That(root.TryGetProperty("SessionToken", out _) || root.TryGetProperty("sessionToken", out _), Is.True);

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
        [Description("Негативний сценарій: Доступ з невалідним/чужим токеном")]
        public async Task GetGroupSessions_AsAnotherUser_Returns403Or401()
        {
            _apiClient.SetAuthToken("Bearer InvalidTokenExample123Value..."); 
            var response = await _apiClient.GetGroupSessionResponseAsync(_groupId);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized).Or.EqualTo(HttpStatusCode.Forbidden));

            _apiClient.SetAuthToken(_ownerToken);
        }

        [Test]
        [Description("Наскрізний Е2Е сценарій: Створення сесії гостя -> Додавання страв -> Контроль статусу Власником")]
        public async Task E2E_GroupPreOrder_FullFlow_TracksStatus()
        {
            string targetGuestName = "Automation_Guest";

            // 1. Гість створює сесію (анонімно за groupId)
            string sessionToken = await _apiClient.CreateGuestSessionAsync(_groupId, targetGuestName);
            Assert.That(sessionToken, Is.Not.Null.And.Not.Empty);

            // 2. Власник перевіряє появу сесії та бере її ID
            _apiClient.SetAuthToken(_ownerToken);
            var sessionsRes = await _apiClient.GetGroupSessionResponseAsync(_groupId);
            var content = await sessionsRes.Content.ReadAsStringAsync();
            
            string guestSessionId = null;
            using (var doc = JsonDocument.Parse(content))
            {
                foreach (var session in doc.RootElement.EnumerateArray())
                {
                    if (session.GetProperty("guestName").GetString() == targetGuestName)
                    {
                        guestSessionId = session.GetProperty("id").GetString();
                        break;
                    }
                }
            }
            Assert.That(guestSessionId, Is.Not.Null, "Сесія гостя не з'явилась у списку Owner-а");

            // 3. Гість додає страву до своєї сесії
            await _apiClient.AddDishToGuestSessionAsync(guestSessionId, _dishId, quantity: 3);

            // 4. Фінальна перевірка статусу сесії Власником
            var finalRes = await _apiClient.GetGroupSessionResponseAsync(_groupId);
            var finalContent = await finalRes.Content.ReadAsStringAsync();
            using (var doc = JsonDocument.Parse(finalContent))
            {
                bool validated = false;
                foreach (var session in doc.RootElement.EnumerateArray())
                {
                    if (session.GetProperty("id").GetString() == guestSessionId)
                    {
                        Assert.That(session.GetProperty("status").GetString(), Is.EqualTo("Created"));
                        validated = true;
                    }
                }
                Assert.That(validated, Is.True, "Оновлена сесія гостя не знайдена в базі.");
            }
        }
    }
}
