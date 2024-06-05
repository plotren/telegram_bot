using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace MyTelegramBot
{
    class Program
    {
        private static readonly HttpClient client = new HttpClient();
        private static TelegramBotClient Bot;
        private static string UnsplashAccessKey = "1EJFsbcsGuIm5UY3JCayOzwNPZsbAMiFGpuURPJBQF4";
        private static Dictionary<long, UserState> userStates = new Dictionary<long, UserState>();

        static async Task Main()
        {
            string botToken = "6525231379:AAGEV1e-220ASOwEvyfv21AGPYiGYVTmJAI";
            Bot = new TelegramBotClient(botToken);

            User me = await Bot.GetMeAsync();
            Console.WriteLine($"Bot id: {me.Id}. Bot Name: {me.FirstName}");

            using var cts = new CancellationTokenSource();
            ReceiverOptions receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = { }
            };
            Bot.StartReceiving(
                HandleUpdateAsync,
                HandleErrorAsync,
                receiverOptions,
                cancellationToken: cts.Token
            );

            Console.WriteLine($"Start listening for @{me.Username}");
            Console.ReadLine();
            cts.Cancel();
        }

        static async Task ShowStartMessage(long chatId)
        {
            var startKeyboard = new ReplyKeyboardMarkup(new[]
            {
                new KeyboardButton("Почати")
            })
            {
                ResizeKeyboard = true,
                OneTimeKeyboard = true
            };

            await Bot.SendTextMessageAsync(
                chatId: chatId,
                text: "Ласкаво просимо! Натисніть кнопку 'Почати' для початку роботи.",
                replyMarkup: startKeyboard
            );
        }

        static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            if (update.Type == UpdateType.Message && update.Message.Type == MessageType.Text)
            {
                var message = update.Message;

                if (!userStates.ContainsKey(message.Chat.Id))
                {
                    userStates[message.Chat.Id] = new UserState();
                }

                var userState = userStates[message.Chat.Id];

                if (userState.IsAwaitingMinPrice)
                {
                    userState.PriceFrom = message.Text;
                    userState.IsAwaitingMinPrice = false;
                    userState.IsAwaitingMaxPrice = true;

                    await botClient.SendTextMessageAsync(
                        chatId: message.Chat.Id,
                        text: "Введіть максимальну ціну:",
                        cancellationToken: cancellationToken
                    );
                    return;
                }
                if (userState.IsAwaitingMaxPrice)
                {
                    userState.PriceTo = message.Text;
                    userState.IsAwaitingMaxPrice = false;
                    userState.IsAwaitingMinArea = true;

                    await botClient.SendTextMessageAsync(
                        chatId: message.Chat.Id,
                        text: "Введіть мінімальну площу (кв. м):",
                        cancellationToken: cancellationToken
                    );
                    return;
                }
                if (userState.IsAwaitingMinArea)
                {
                    userState.MinArea = message.Text;
                    userState.IsAwaitingMinArea = false;
                    userState.IsAwaitingMaxArea = true;

                    await botClient.SendTextMessageAsync(
                        chatId: message.Chat.Id,
                        text: "Введіть максимальну площу (кв. м):",
                        cancellationToken: cancellationToken
                    );
                    return;
                }
                if (userState.IsAwaitingMaxArea)
                {
                    userState.MaxArea = message.Text;
                    userState.IsAwaitingMaxArea = false;
                    userState.Page = 1;

                    await SendRealtyResults(botClient, message.Chat.Id, userState, cancellationToken);
                    return;
                }
                if (userState.IsAwaitingPropertyType)
                {
                    userState.PropertyType = message.Text;
                    userState.IsAwaitingPropertyType = false;
                    userState.IsAwaitingDescription = true;

                    await botClient.SendTextMessageAsync(
                        chatId: message.Chat.Id,
                        text: "Введіть опис нерухомості:",
                        cancellationToken: cancellationToken
                    );
                    return;
                }
                if (userState.IsAwaitingDescription)
                {
                    userState.Description = message.Text;
                    userState.IsAwaitingDescription = false;
                    userState.IsAwaitingPrice = true;

                    await botClient.SendTextMessageAsync(
                        chatId: message.Chat.Id,
                        text: "Введіть ціну:",
                        cancellationToken: cancellationToken
                    );
                    return;
                }
                if (userState.IsAwaitingPrice)
                {
                    userState.Price = message.Text;
                    userState.IsAwaitingPrice = false;

                    await botClient.SendTextMessageAsync(
                        chatId: message.Chat.Id,
                        text: "Ваше оголошення було створено!",
                        cancellationToken: cancellationToken
                    );

                    
                    string confirmationMessage = $"Тип нерухомості: {userState.PropertyType}\nОпис: {userState.Description}\nЦіна: {userState.Price}";
                    await botClient.SendTextMessageAsync(
                        chatId: message.Chat.Id,
                        text: confirmationMessage,
                        cancellationToken: cancellationToken
                    );
                    return;
                }

                switch (message.Text.ToLower())
                {
                    case "почати":
                        await ShowStatesKeyboard(botClient, message.Chat.Id, cancellationToken);
                        break;
                    case "/start":
                        await ShowStartMessage(message.Chat.Id);
                        break;
                    case "/getstates":
                        await ShowStatesKeyboard(botClient, message.Chat.Id, cancellationToken);
                        break;
                }
            }
            else if (update.Type == UpdateType.CallbackQuery)
            {
                var callbackQuery = update.CallbackQuery;

                if (!userStates.ContainsKey(callbackQuery.Message.Chat.Id))
                {
                    userStates[callbackQuery.Message.Chat.Id] = new UserState();
                }

                await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken: cancellationToken); 

                if (callbackQuery.Data.StartsWith("state_"))
                {
                    string stateId = callbackQuery.Data.Substring(6);
                    string citiesInfo = await FetchDataFromApi($"https://developers.ria.com/dom/cities/{stateId}?api_key=PQzTDbeDDAV8Qn15qG4qmLEWX0eOJCWAlkJIPYpY&lang_id=4");
                    var citiesKeyboard = GetCitiesKeyboard(citiesInfo);

                    var userState = userStates[callbackQuery.Message.Chat.Id];
                    userState.StateId = stateId;

                    await botClient.EditMessageTextAsync(
                        chatId: callbackQuery.Message.Chat.Id,
                        messageId: callbackQuery.Message.MessageId,
                        text: "Оберіть місто:",
                        replyMarkup: citiesKeyboard,
                        cancellationToken: cancellationToken
                    );
                }
                else if (callbackQuery.Data.StartsWith("city_"))
                {
                    string cityId = callbackQuery.Data.Substring(5);
                    var actionKeyboard = GetActionKeyboard(cityId);
                    await botClient.EditMessageTextAsync(
                        chatId: callbackQuery.Message.Chat.Id,
                        messageId: callbackQuery.Message.MessageId,
                        text: "Що ви хочете зробити?",
                        replyMarkup: actionKeyboard,
                        cancellationToken: cancellationToken
                    );
                }
                else if (callbackQuery.Data.StartsWith("action_sell"))
                {
                    var propertyTypeKeyboard = GetPropertyTypeKeyboard();
                    var userState = userStates[callbackQuery.Message.Chat.Id];
                    userState.Action = "sell";
                    await botClient.EditMessageTextAsync(
                        chatId: callbackQuery.Message.Chat.Id,
                        messageId: callbackQuery.Message.MessageId,
                        text: "Оберіть тип нерухомості:",
                        replyMarkup: propertyTypeKeyboard,
                        cancellationToken: cancellationToken
                    );
                }
                else if (callbackQuery.Data.StartsWith("action_buy"))
                {
                    var userState = userStates[callbackQuery.Message.Chat.Id];
                    userState.Action = "buy";
                    userState.IsAwaitingMinPrice = true;

                    await botClient.EditMessageTextAsync(
                        chatId: callbackQuery.Message.Chat.Id,
                        messageId: callbackQuery.Message.MessageId,
                        text: "Введіть мінімальну ціну:",
                        cancellationToken: cancellationToken
                    );
                }
                else if (callbackQuery.Data.StartsWith("propertytype_"))
                {
                    string propertyType = callbackQuery.Data.Substring(13);
                    var userState = userStates[callbackQuery.Message.Chat.Id];
                    userState.PropertyType = propertyType;
                    userState.IsAwaitingPropertyType = true;

                    await botClient.SendTextMessageAsync(
                        chatId: callbackQuery.Message.Chat.Id,
                        text: "Введіть тип нерухомості:",
                        cancellationToken: cancellationToken
                    );
                }
                else if (callbackQuery.Data.StartsWith("page_"))
                {
                    string[] parts = callbackQuery.Data.Split('_');
                    string pageStr = parts[1];
                    var userState = userStates[callbackQuery.Message.Chat.Id];
                    userState.Page = int.Parse(pageStr);

                    await SendRealtyResults(botClient, callbackQuery.Message.Chat.Id, userState, cancellationToken);
                }
            }
        }

        static async Task ShowStatesKeyboard(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
        {
            string statesInfo = await FetchDataFromApi("https://developers.ria.com/dom/states?api_key=PQzTDbeDDAV8Qn15qG4qmLEWX0eOJCWAlkJIPYpY");
            var statesKeyboard = GetStatesKeyboard(statesInfo);
            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "Оберіть область:",
                replyMarkup: statesKeyboard,
                cancellationToken: cancellationToken
            );
        }

        static InlineKeyboardMarkup GetStatesKeyboard(string jsonResponse)
        {
            var states = JArray.Parse(jsonResponse);
            var keyboardInline = new List<InlineKeyboardButton[]>();
            foreach (var state in states)
            {
                string name = state["name"].ToString();
                string stateId = state["stateID"].ToString();
                var row = new[] { InlineKeyboardButton.WithCallbackData(name, $"state_{stateId}") };
                keyboardInline.Add(row);
            }
            return new InlineKeyboardMarkup(keyboardInline);
        }

        static InlineKeyboardMarkup GetCitiesKeyboard(string jsonResponse)
        {
            var cities = JArray.Parse(jsonResponse);
            var keyboardInline = new List<InlineKeyboardButton[]>();
            foreach (var city in cities)
            {
                string name = city["name"].ToString();
                string cityId = city["cityID"].ToString();
                var row = new[] { InlineKeyboardButton.WithCallbackData(name, $"city_{cityId}") };
                keyboardInline.Add(row);
            }
            return new InlineKeyboardMarkup(keyboardInline);
        }

        static InlineKeyboardMarkup GetActionKeyboard(string cityId)
        {
            var keyboardInline = new List<InlineKeyboardButton[]>
            {
                new[] { InlineKeyboardButton.WithCallbackData("Продати", $"action_sell_{cityId}") },
                new[] { InlineKeyboardButton.WithCallbackData("Купити", $"action_buy_{cityId}") }
            };
            return new InlineKeyboardMarkup(keyboardInline);
        }

        static InlineKeyboardMarkup GetPropertyTypeKeyboard()
        {
            var keyboardInline = new List<InlineKeyboardButton[]>
            {
                new[] { InlineKeyboardButton.WithCallbackData("Квартири/Кімнати", $"propertytype_flat") },
                new[] { InlineKeyboardButton.WithCallbackData("Будинки", $"propertytype_house") }
            };
            return new InlineKeyboardMarkup(keyboardInline);
        }

        static async Task<string> FetchDataFromApi(string apiUrl)
        {
            // уникнути перевищення ліміту
            await Task.Delay(1000);
            HttpResponseMessage response = await client.GetAsync(apiUrl);
            response.EnsureSuccessStatusCode();
            string responseBody = await response.Content.ReadAsStringAsync();
            return responseBody;
        }

        static async Task<string> GenerateImage(string query)
        {
            string url = $"https://api.unsplash.com/photos/random?query={query}&client_id={UnsplashAccessKey}";
            HttpResponseMessage response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            string responseBody = await response.Content.ReadAsStringAsync();

            JObject json = JObject.Parse(responseBody);
            return json["urls"]["regular"].ToString();
        }

        static async Task SendRealtyResults(ITelegramBotClient botClient, long chatId, UserState userState, CancellationToken cancellationToken)
        {
            string url = $"https://developers.ria.com/dom/search?category={userState.Category}&realty_type={GetRealtyType(userState.Category)}&operation_type={GetOperationType(userState.Action)}&state_id={userState.StateId}&city_id={userState.CityId}&price_from={userState.PriceFrom}&price_to={userState.PriceTo}&price_cur=1&api_key=PQzTDbeDDAV8Qn15qG4qmLEWX0eOJCWAlkJIPYpY";
            string response = await FetchDataFromApi(url);

            JObject json = JObject.Parse(response);
            var items = json["items"];

            if (items != null && items.HasValues)
            {
                int count = 0;
                foreach (var itemId in items)
                {
                    if (count >= 5) break; // Обмеження 
                    count++;

                    string itemInfo = await FetchDataFromApi($"https://developers.ria.com/dom/info/{itemId}?api_key=PQzTDbeDDAV8Qn15qG4qmLEWX0eOJCWAlkJIPYpY");
                    JObject itemJson = JObject.Parse(itemInfo);
                    string advertTitle = itemJson["advert_title"]?.ToString();
                    string price = itemJson["price"]?.ToString();
                    string city = itemJson["city_name"]?.ToString();
                    string description = itemJson["description_uk"]?.ToString();
                    string urlAdvert = itemJson["beautiful_url"]?.ToString();
                    string mainPhotoUrl = itemJson["main_photo"]?.ToString();

                    // AI
                    string generatedImageUrl = await GenerateImage(description ?? "real estate");

                    string message = $"Назва: {advertTitle}\nЦіна: {price}\nМісто: {city}\nОпис: {description}\nПосилання: {urlAdvert}\nФото: {mainPhotoUrl}\nGenerated Image: {generatedImageUrl}";

                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: message,
                        cancellationToken: cancellationToken
                    );
                }

                
                var nextPageKeyboard = new InlineKeyboardMarkup(new[]
                {
                    InlineKeyboardButton.WithCallbackData("Наступна сторінка", $"page_{userState.Page + 1}")
                });

                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Бажаєте побачити більше результатів?",
                    replyMarkup: nextPageKeyboard,
                    cancellationToken: cancellationToken
                );
            }
            else
            {
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Немає результатів за вашим запитом.",
                    cancellationToken: cancellationToken
                );
            }
        }

        static int GetRealtyType(string category)
        {
            return category switch
            {
                "flat" => 2,
                "house" => 5,
                _ => 0
            };
        }

        static int GetOperationType(string action)
        {
            return action switch
            {
                "buy" => 1,
                "sell" => 2,
                _ => 0
            };
        }

        static Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var ErrorMessage = exception switch
            {
                ApiRequestException apiRequestException => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                _ => exception.ToString()
            };

            Console.WriteLine(ErrorMessage);
            return Task.CompletedTask;
        }

        class UserState
        {
            public string Action { get; set; }
            public string CityId { get; set; }
            public string Category { get; set; }
            public string PropertyType { get; set; }
            public string Description { get; set; }
            public string Price { get; set; }
            public string PhotoFileId { get; set; }
            public string PriceFrom { get; set; }
            public string PriceTo { get; set; }
            public string MinArea { get; set; }
            public string MaxArea { get; set; }
            public string StateId { get; set; }
            public int Page { get; set; }
            public bool IsAwaitingMinPrice { get; set; }
            public bool IsAwaitingMaxPrice { get; set; }
            public bool IsAwaitingMinArea { get; set; }
            public bool IsAwaitingMaxArea { get; set; }
            public bool IsAwaitingPropertyType { get; set; }
            public bool IsAwaitingDescription { get; set; }
            public bool IsAwaitingPrice { get; set; }
        }
    }
}
