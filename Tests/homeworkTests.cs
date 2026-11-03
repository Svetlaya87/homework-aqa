using System;
using System.Net;
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

        [OneTimeSetUp] 
        public async Task Setup()
        {
            // Переконаємося, що змінні середовища з .env завантажені
            InitializeEnvironment();

            _apiClient = new RestaurantApiClient(_apiBaseUrl);

            try
            {
                // Повністю динамічний ланцюжок запитів на основі точних ключів вашого API
                string ownerToken = await _apiClient.SignInAsync(OwnerEmail, OwnerPassword);
                _apiClient.SetAuthToken(ownerToken);

                string reservationId = await _apiClient.GetLatestReservationIdAsync();
                string inviteToken = await _apiClient.GetInviteTokenAsync(reservationId);
                
                _groupId = await _apiClient.GetGroupReservationIdAsync(inviteToken);

                Console.WriteLine($"[E2E Setup Success] Динамічно отримано Group ID: {_groupId}");
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
        [Description("Позитивний наскрізний сценарій: перевірка структури сесії та математики кошика")]
        public async Task GetGroupSessions_AsOwner_Returns200AndValidStructure()
        {
            var response = await _apiClient.GetGroupSessionResponseAsync(_groupId);
           
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            var content = await response.Content.ReadAsStringAsync();
            using var jsonDoc = JsonDocument.Parse(content);
            var root = jsonDoc.RootElement;

            Assert.That(root.TryGetProperty("Participants", out var participants), Is.True);
            Assert.That(root.TryGetProperty("Total", out var grandTotal), Is.True);

            if (participants.GetArrayLength() == 0)
            {
                Assert.That(grandTotal.GetDouble(), Is.EqualTo(0), "Список учасників порожній, сума має бути 0.");
                return; 
            }

            double calculatedGrandTotal = 0;

            foreach (var participant in participants.EnumerateArray())
            {
                Assert.That(participant.TryGetProperty("IsSubmitted", out _), Is.True);

                var items = participant.GetProperty("Items");
                double participantTotal = 0;

                foreach (var item in items.EnumerateArray())
                {
                    double price = item.GetProperty("dish").GetProperty("price").GetDouble();
                    int quantity = item.GetProperty("quantity").GetInt32();
                    participantTotal += price * quantity;
                }

                Assert.That(participant.GetProperty("Total").GetDouble(), Is.EqualTo(participantTotal).Within(0.01));
                calculatedGrandTotal += participantTotal;
            }

            Assert.That(grandTotal.GetDouble(), Is.EqualTo(calculatedGrandTotal).Within(0.01));
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
            
            // Відновлюємо токен для інших тестів
            string ownerToken = await _apiClient.SignInAsync(OwnerEmail, OwnerPassword);
            _apiClient.SetAuthToken(ownerToken);
        }

        [Test]
        [Description("Негативний сценарій: Спроба доступу з токеном іншого користувача (Alien User) має повертати 403 або 401")]
        public async Task GetGroupSessions_AsAnotherUser_Returns403()
        {
            // Імітуємо токен іншого користувача (Alien User), як було у вашому першому файлу
            _apiClient.SetAuthToken("hwD0z6vUBG67zimkl/LW326LM2KRpWSp9oo9Cmlb9Fw="); 

            var response = await _apiClient.GetGroupSessionResponseAsync(_groupId);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized)
                .Or.EqualTo(HttpStatusCode.Forbidden));

            // Після тесту відновлюємо токен Власника, щоб інші запуски не ламалися
            string ownerToken = await _apiClient.SignInAsync(OwnerEmail, OwnerPassword);
            _apiClient.SetAuthToken(ownerToken);
        }

    }
}
