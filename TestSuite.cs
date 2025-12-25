using Microsoft.Playwright;
using NUnit.Framework.Legacy;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TestSuite
{
    [TestFixture]
    public class BrowserConfig
    {
        public IBrowser Browser;
        public IPage Page;
        private IPlaywright _playwright;

        [OneTimeSetUp]
        public async Task Setup()
        {
            _playwright = await Playwright.CreateAsync();

            Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = false,
            });

            Page = await Browser.NewPageAsync();
        }

        [OneTimeTearDown]
        public async Task TearDown()
        {
            await Browser.CloseAsync();
            _playwright.Dispose();
        }
    }

    [TestFixture]
    public class Tests : BrowserConfig
    {
        [Test, Order(1)]
        public async Task TestFrontend()
        {
            //************* Start *************
            await Login();

            string todosLocator = "[class='todo']";
            int countTodos = await Page.Locator(todosLocator).CountAsync();

            Console.WriteLine($"Total todos count: '{countTodos}'");

            string lastTodo = await GenerateToDos(countTodos);

            int thCountReal = await Page.Locator(todosLocator).CountAsync();
            int thCountExpected = 30;
            bool testCountTodosOk = thCountExpected == thCountReal;

            Console.WriteLine($"Actual todos '{thCountReal}' vs expected todos '{thCountExpected}' ({testCountTodosOk})");

            bool testLastTodoOk = !await Page.Locator("[class='todo-details']").Nth(29).GetByText(lastTodo).IsVisibleAsync();

            Console.WriteLine($"The last todo inserted does not appear when the maximum limit is reached ({testLastTodoOk})");

            //************* End Test *************
            ClassicAssert.IsTrue(testCountTodosOk, $"The number of todos in the list '{thCountReal}' does not match the expected maximum '{thCountExpected}'");
            ClassicAssert.IsTrue(testLastTodoOk, $"The last todo inserted should not appear when the maximum number of todos is achieved");
        }

        [Test, Order(2)]
        public async Task TestBackend()
        {
            //************* Start *************
            string settingsPath = Path.Combine(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."), "settings.json");
            string jsonString = File.ReadAllText(settingsPath);
            using JsonDocument doc = JsonDocument.Parse(jsonString);

            JsonElement root = doc.RootElement;

            string token = root.GetProperty("token").GetString();
            string backendUrl = root.GetProperty("backendUrl").GetString();
            string user = root.GetProperty("user").GetString();

            string filePath = Path.Combine(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."), "openapi.txt");
            string fileContent = File.ReadAllText(filePath);
            fileContent = File.ReadAllText(filePath);

            using JsonDocument fileDoc = JsonDocument.Parse(fileContent);
            JsonElement fileRoot = fileDoc.RootElement;

            var pathsElement = fileRoot.GetProperty("paths");
            string pathUnderTest = "";
            foreach (var path in pathsElement.EnumerateObject())
            {
                pathUnderTest = path.Name;
                break;
            }

            Console.WriteLine($"OpenAPI: Endpoint/Path under test: {pathUnderTest}");

            var specificPath = pathsElement.GetProperty(pathUnderTest);
            var getMethod = specificPath.GetProperty("get");
            var parametersList = getMethod.GetProperty("parameters")[0];

            string name = parametersList.GetProperty("name").GetString();

            var queryParams = new Dictionary<string, object>();
            queryParams.Add(name, user);

            using var playwright = await Playwright.CreateAsync();
            var requestContext = await playwright.APIRequest.NewContextAsync(new()
            {
                BaseURL = backendUrl,
                ExtraHTTPHeaders = new Dictionary<string, string>
                {
                    { "Authorization", $"Bearer {token}" },
                    { "Accept", "application/json, text/plain, */*" }
                }
            });

            string apiUrl = backendUrl + pathUnderTest;

            var response = await requestContext.GetAsync(apiUrl, new APIRequestContextOptions
            {
                Params = queryParams
            });

            var body = await response.TextAsync();

            bool testStatusOk = response.Status == 200;
            Console.WriteLine($"Api Status '{response.Status}' ({testStatusOk})");

            bool testBodyOk = body.Length > 2;
            Console.WriteLine($"API response contains data ({testBodyOk})");

            //************* End Test *************
            ClassicAssert.IsTrue(testStatusOk, $"API status did not return 200 OK");
            ClassicAssert.IsTrue(testBodyOk, $"API response returned no results");
        }

        #region Helper
        private List<string> GetEnvVariablesAsync()
        {
            List<string> envVariables = new List<string>();
            string path = Path.Combine(AppContext.BaseDirectory, "settings.json");

            string jsonString = File.ReadAllText(path);

            using JsonDocument doc = JsonDocument.Parse(jsonString);
            JsonElement root = doc.RootElement;

            string url = root.GetProperty("baseUrl").GetString();
            string loginUrl = root.GetProperty("loginUrl").GetString();
            string user = root.GetProperty("user").GetString();
            string email = root.GetProperty("email").GetString();
            string password = root.GetProperty("password").GetString();

            envVariables.Add(url);
            envVariables.Add(loginUrl);
            envVariables.Add(user);
            envVariables.Add(password);
            envVariables.Add(email);

            return envVariables;
        }

        private async Task Login(bool getToken = false)
        {
            List<string> envs = GetEnvVariablesAsync();
            string url = envs[0];
            string loginUrl = envs[1];
            string user = envs[2];
            string password = envs[3];

            loginUrl = loginUrl.Replace("*", user);
            Console.WriteLine("Access link");
            await Page.GotoAsync(url);

            Console.WriteLine($"    Fill username: {user}");
            await Page.GetByLabel("username").FillAsync(user);

            Console.WriteLine($"    Fill password: {password}");
            await Page.Locator("[name='password']").FillAsync(password);
            await Page.Locator("[type='submit']").ClickAsync();

            try
            {
                var responseTask = Page.WaitForResponseAsync(r => r.Url.Contains(loginUrl));
                var response = await responseTask;

                if (response.Status == 200)
                {
                    Console.WriteLine("Access OK.");

                    await GetToken();

                    bool homePageOk = await Page.IsVisibleAsync("[class='card']");

                    if (!homePageOk)
                    {
                        throw new Exception($"Access nOK due to Frontend error Home Page Card not visible");
                    }
                }
                else
                {
                    throw new Exception($"Access nOK due to API error: {response.Status}");
                }
            }
            catch (Exception ex)
            {
                Assert.Fail(ex.Message);
            }
        }

        private async Task<string> GenerateToDos(int countTodos)
        {
            int maxTodos = 31 - countTodos;

            string title = "Title_"; string detail = "Detail_";
            string lastInsertTodo = "";
            int count = 0;
            Random random = new Random();
            for (int i = 0; i < maxTodos; i++)
            {
                int num = random.Next(1, 2000);

                Console.WriteLine($"Create a todo:");
                Console.WriteLine($"    Fill title: {title}{num}");
                await Page.Locator("#title").FillAsync($"{title}{num}");

                Console.WriteLine($"    Fill detail: {detail}{num}");
                await Page.Locator("#details").FillAsync($"{detail}{num}");

                await Page.Locator("[type='submit']").ClickAsync();

                if (count == maxTodos - 1)
                {
                    lastInsertTodo = $"{detail}{num}";
                }
                count++;
            }

            return lastInsertTodo;
        }

        private async Task GetToken()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
            string path = Path.Combine(projectRoot, "settings.json");

            var keys = await Page.EvaluateAsync<string[]>("Object.keys(localStorage)");

            var idToken = keys.FirstOrDefault(k => k.Contains("idToken"));
            string token = await Page.EvaluateAsync<string>($"localStorage.getItem('{idToken}')");

            string jsonOriginal = await File.ReadAllTextAsync(path);
            var rootNode = JsonNode.Parse(jsonOriginal);
            rootNode["token"] = token;

            await File.WriteAllTextAsync(path, rootNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        #endregion
    }
}

