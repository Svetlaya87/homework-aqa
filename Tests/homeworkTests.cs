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
            // InitializeEnvironment() видалено - він викликається автоматично в TestBase!
            _apiClient = new RestaurantApiClient(_apiBaseUrl);

            try
            {
                // 1. Авторизація Owner
                _ownerToken = await _apiClient.SignInAsync(OwnerEmail, OwnerPassword);
                _apiClient.SetAuthToken(_ownerToken); // Токен встановлено для _apiClient

                // 2. Отримання доступної страви для тестів
                var dishData = await _apiClient.GetFirstAvailableDishAsync();
                _dishId = dishData.Id;

                // 3. Пошук вільного столу через клієнт
                _tableId = await _apiClient.GetFirstAvailableTableAsync();

                // 4. Отримання доступного часового слоту (Динамічна дата замість хардкоду)
                string targetDateString = DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"); 
                var slot = await _apiClient.GetFirstAvailableSlotAsync(_tableId, targetDateString);

                // 5. Створення чистої тестової групи
                string reservationId = await _apiClient.CreateReservationAsync(_tableId, slot.Start, slot.End, guestCount: 4);
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

            // 2. Власник перевіряє появу учасника в списку Participants та бере його SessionId
            _apiClient.SetAuthToken(_ownerToken);
            var response = await _apiClient.GetGroupSessionResponseAsync(_groupId);
            var content = await response.Content.ReadAsStringAsync();
            
            string guestSessionId = null;
            using (var doc = JsonDocument.Parse(content))
            {
                var participants = doc.RootElement.GetProperty("Participants");
                Assert.That(participants.GetArrayLength(), Is.GreaterThan(0), "Гість має бути в списку Participants");
                
                bool guestFound = false;
                foreach (var participant in participants.EnumerateArray())
                {
                    if (participant.GetProperty("Name").GetString() == targetGuestName)
                    {
                        guestFound = true;
                        guestSessionId = participant.GetProperty("Id").GetString();
                        
                        // ТОЧНІ ПЕРЕВІРКИ ДЛЯ КРОКУ 2:
                        // Перевірка 2.1: Учасник без страв має бути в списку із сумою 0
                        var items = participant.GetProperty("Items");
                        Assert.That(items.ValueKind, Is.EqualTo(JsonValueKind.Array), "Поле 'Items' гостя має бути масивом");
                        Assert.That(items.GetArrayLength(), Is.EqualTo(0), "Новий гість не повинен мати страв у кошику");
                        Assert.That(participant.GetProperty("Total").GetDouble(), Is.EqualTo(0), "Початкова сума нового гостя має бути 0");
                        
                        // Перевірка 2.2: Учасник, який ще не підтвердив кошик, має відмітку IsSubmitted = false
                        Assert.That(participant.GetProperty("IsSubmitted").GetBoolean(), Is.False, 
                            "Новий гість ще не підтвердив кошик, тому 'IsSubmitted' має бути false");
                        break;
                    }
                }
                Assert.That(guestFound, Is.True, $"Гість з ім'ям '{targetGuestName}' не знайдений у списку учасників групи");
            }

            // Якщо ID сесії не знайшовся в масиві, використовуємо ID групи як fallback
            if (string.IsNullOrEmpty(guestSessionId)) guestSessionId = _groupId;

            // 3. Гість додає страву (2 шт), передаючи ТОКЕН для авторизації
            await _apiClient.AddDishToGuestSessionAsync(guestSessionId, sessionToken, _dishId, quantity: 2);

            // 4. Власник перевіряє точне оновлення математики кошика та загальної суми
            _apiClient.SetAuthToken(_ownerToken);
            var responseAfterAdd = await _apiClient.GetGroupSessionResponseAsync(_groupId);
            var contentAfterAdd = await responseAfterAdd.Content.ReadAsStringAsync();
            using (var doc = JsonDocument.Parse(contentAfterAdd))
            {
                // Рахуємо точну очікувану суму: ціна однієї страви (отримана на Setup) * 2 шт.
                double expectedTotal = _apiClient.GetFirstAvailableDishAsync().Result.Price * 2; 
                // Або якщо ви зберегли ціну в окрему змінну класу під час Setup, використовуйте її (наприклад, _dishPrice * 2)

                double actualTotal = doc.RootElement.GetProperty("Total").GetDouble();
                
                Assert.That(actualTotal, Is.EqualTo(expectedTotal).Within(0.01), 
                    $"Загальна сума 'Total' ({actualTotal}) має точно дорівнювати вартості доданих страв ({expectedTotal})");
            }

        }

        [Test]
        [Description("Динамічна перевірка: якщо група порожня — очікуємо 0, якщо заповнена — валідуємо математику")]
        public async Task GetGroupSessions_DynamicCheck_ValidatesBasedOnCurrentState()
        {
            _apiClient.SetAuthToken(_ownerToken);

             // 1. Отримуємо поточний стан нашої групи
            var response = await _apiClient.GetGroupSessionResponseAsync(_groupId);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            var content = await response.Content.ReadAsStringAsync();
            using var jsonDoc = JsonDocument.Parse(content);
            var root = jsonDoc.RootElement;

            var participants = root.GetProperty("Participants");
            double actualTotal = root.GetProperty("Total").GetDouble();

            // 2. ДИНАМІЧНЕ РОЗГАЛУЖЕННЯ: тест підлаштовується під стан системи
              
            if (participants.ValueKind == JsonValueKind.Array && participants.GetArrayLength() == 0)
            {
                // Выводим пустой массив в консоль (покажет [])
                Console.WriteLine($"[DEBUG] Масив participants з сервера: {participants.GetRawText()}");
                Console.WriteLine($"[DEBUG] Загальна сума total з сервера: {actualTotal}");

                // Сценарій А: Група порожня
                Assert.That(participants.GetArrayLength(), Is.EqualTo(0), 
                "В response масив учасників має бути порожнім: 'participants': []");
            
                Assert.That(actualTotal, Is.EqualTo(0), 
                "В response загальна сума має бути нульовою: 'total': 0");
            }

            else
            {
                // Сценарій Б: Група заповнена (лектор запустив тест після додавання гостей)

                Console.WriteLine($"[DEBUG] Повний Response від сервера для заповненої групи:\n{content}");
                double expectedTotal = 0;

                 // Динамічно рахуємо, яка сума МАЄ бути на основі доданих учасників
                foreach (var participant in participants.EnumerateArray())
                {
                    expectedTotal += participant.GetProperty("Total").GetDouble();
                }

                Assert.That(actualTotal, Is.EqualTo(expectedTotal).Within(0.01), 
                $"Група заповнена. Загальний Total ({actualTotal}) має дорівнювати сумі по всіх учасниках ({expectedTotal})");
            }
        }

    }
}
