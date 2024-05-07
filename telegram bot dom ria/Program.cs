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

        static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            if (update.Type == UpdateType.Message && update.Message.Type == MessageType.Text)
            {
                var message = update.Message;
                switch (message.Text.ToLower())
                {
                    case "/start":
                        await botClient.SendTextMessageAsync(
                            chatId: message.Chat.Id,
                            text: "Ласкаво просимо! Використовуйте /getstates, щоб отримати список областей.",
                            cancellationToken: cancellationToken
                        );
                        break;
                    case "/getstates":
                        string statesInfo = await FetchDataFromApi("https://developers.ria.com/dom/states?api_key=PQzTDbeDDAV8Qn15qG4qmLEWX0eOJCWAlkJIPYpY");
                        var statesKeyboard = GetStatesKeyboard(statesInfo);
                        await botClient.SendTextMessageAsync(
                            chatId: message.Chat.Id,
                            text: "Оберіть область:",
                            replyMarkup: statesKeyboard,
                            cancellationToken: cancellationToken
                        );
                        break;
                }
            }
            else if (update.Type == UpdateType.CallbackQuery)
            {
                var callbackQuery = update.CallbackQuery;
                await botClient.AnswerCallbackQueryAsync(callbackQuery.Id);

                if (callbackQuery.Data.StartsWith("state_"))
                {
                    string stateId = callbackQuery.Data.Substring(6); 
                    string citiesInfo = await FetchDataFromApi($"https://developers.ria.com/dom/cities/{stateId}?api_key=PQzTDbeDDAV8Qn15qG4qmLEWX0eOJCWAlkJIPYpY&lang_id=4");
                    var citiesKeyboard = GetCitiesKeyboard(citiesInfo);
                    await botClient.EditMessageTextAsync(
                        chatId: callbackQuery.Message.Chat.Id,
                        messageId: callbackQuery.Message.MessageId,
                        text: "Оберіть місто:",
                        replyMarkup: citiesKeyboard,
                        cancellationToken: cancellationToken
                    );
                }
            }
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
            int count = 0;
            foreach (var city in cities)
            {
                string name = city["name"].ToString();
                string cityId = city["cityID"].ToString();
                var row = new[] { InlineKeyboardButton.WithCallbackData(name, $"city_{cityId}") };
                keyboardInline.Add(row);
                if (++count == 30) break; 
            }
            return new InlineKeyboardMarkup(keyboardInline);
        }

        static async Task<string> FetchDataFromApi(string apiUrl)
        {
            HttpResponseMessage response = await client.GetAsync(apiUrl);
            response.EnsureSuccessStatusCode();
            string responseBody = await response.Content.ReadAsStringAsync();
            return responseBody;
        }

        static string FormatStateInfo(string jsonResponse)
        {
            var states = JArray.Parse(jsonResponse);
            List<string> formattedStates = new List<string>();
            foreach (var state in states)
            {
                string name = state["name"].ToString();
                formattedStates.Add(name);
            }
            return string.Join(", ", formattedStates);
        }

        static string FormatCityInfo(string jsonResponse)
        {
            var cities = JArray.Parse(jsonResponse);
            List<string> formattedCities = new List<string>();
            foreach (var city in cities)
            {
                string name = city["name"].ToString();
                formattedCities.Add(name);
            }
            return string.Join(", ", formattedCities);
        }

        static string TrimMessage(string message)
        {
            return message.Length <= 4096 ? message : message.Substring(0, 4093) + "...";
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
    }
}
