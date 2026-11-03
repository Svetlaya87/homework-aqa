using System;
using dotenv.net;
using NUnit.Framework;

namespace APIHandler.Tests.AqaHomework.Base
{
    public class TestBase
    {
        protected string OwnerEmail { get; private set; }
        protected string OwnerPassword { get; private set; }

        [OneTimeSetUp]
        public void InitializeEnvironment()
        {
            // Автоматично шукає файл .env у папці проєкту та завантажує змінні
            DotEnv.Load(options: new DotEnvOptions(probeForEnv: true));

            OwnerEmail = Environment.GetEnvironmentVariable("OWNER_EMAIL") 
                ?? throw new Exception("Помилка: OWNER_EMAIL не знайдено в .env файлі");
                
            OwnerPassword = Environment.GetEnvironmentVariable("OWNER_PASSWORD") 
                ?? throw new Exception("Помилка: OWNER_PASSWORD не знайдено в .env файлі");
        }
    }
}
