using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

class Program
{
    private static readonly TelegramBotClient botClient = new("8024660428:AAFTnnOrNPy6srKgYIJ74IOidhUFORYFUds");
    private static readonly Dictionary<long, string> userStates = new();
    private static readonly Dictionary<long, string> userLang = new();
    private static readonly Dictionary<string, string> registeredUsers = new();
    private static readonly Dictionary<long, string> userIin = new();
    private static readonly Dictionary<long, List<Dictionary<string, string>>> userConversations = new();

    private static readonly HttpClient client = new();
    private const string apiKey = "AIzaSyBqrFqFrD0PNeSljLD6awnPn4Hgsj5Yzxc";
    private const string geminiEndpoint = "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key=" + apiKey;

    static async Task Main()
    {
        using var cts = new CancellationTokenSource();
        botClient.StartReceiving(
            HandleUpdateAsync,
            HandleErrorAsync,
            new ReceiverOptions { AllowedUpdates = Array.Empty<UpdateType>() },
            cancellationToken: cts.Token
        );

        var me = await botClient.GetMe();
        Console.WriteLine($"Бот запущен: @{me.Username}");
        Console.ReadLine();
    }

    private static async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken token)
    {
        if (update.Message?.Text is not string messageText)
            return;

        var chatId = update.Message.Chat.Id;
        if (!userLang.TryGetValue(chatId, out var lang))
        {
            userLang[chatId] = "waiting_language";
            await bot.SendTextMessageAsync(chatId, "Выберите язык / Тілді таңдаңыз:\n1. Русский\n2. Қазақша", cancellationToken: token);
            return;
        }

        if (lang == "waiting_language")
        {
            userLang[chatId] = messageText switch
            {
                "1" => "ru",
                "2" => "kz",
                _ => null
            };
            if (userLang[chatId] == null)
            {
                await bot.SendTextMessageAsync(chatId, "Неверный выбор / Таңдау дұрыс емес", cancellationToken: token);
                return;
            }

            userStates[chatId] = "choose_action";
            await bot.SendTextMessageAsync(chatId,
                userLang[chatId] == "ru" ? "Выберите действие:" : "Әрекетті таңдаңыз:",
                replyMarkup: GetMainActionKeyboard(userLang[chatId]),
                cancellationToken: token
            );
            return;
        }

        userStates.TryGetValue(chatId, out var state);
        switch (state)
        {
            case "choose_action":
                if (messageText.Contains("Регистрация") || messageText.Contains("Тіркелу"))
                    userStates[chatId] = "register_iin";
                else if (messageText.Contains("Вход") || messageText.Contains("Кіру"))
                    userStates[chatId] = "login_iin";
                else if (messageText.Contains("Нейросеть") || messageText.Contains("Нейрожелілер"))
                    userStates[chatId] = "neural_network_query";

                if (userStates[chatId] == "register_iin")
                    await bot.SendTextMessageAsync(chatId, lang == "ru" ? "Введите ИИН для регистрации:" : "Тіркелу үшін ИИН енгізіңіз:", cancellationToken: token);
                else if (userStates[chatId] == "login_iin")
                    await bot.SendTextMessageAsync(chatId, lang == "ru" ? "Введите ИИН для входа:" : "Кіру үшін ИИН енгізіңіз:", cancellationToken: token);
                else if (userStates[chatId] == "neural_network_query")
                    await bot.SendTextMessageAsync(chatId, lang == "ru" ? "Введите ваш запрос к нейросети:" : "Нейрожелілерге сұрағыңызды енгізіңіз:", replyMarkup: GetExitKeyboard(lang), cancellationToken: token);
                break;

            case "register_iin":
                userIin[chatId] = messageText;
                if (registeredUsers.ContainsKey(messageText))
                {
                    await bot.SendTextMessageAsync(chatId, lang == "ru" ? "Этот ИИН уже зарегистрирован." : "Бұл ИИН бұрын тіркелген.", cancellationToken: token);
                    userStates[chatId] = "choose_action";
                }
                else
                {
                    userStates[chatId] = "register_password";
                    await bot.SendTextMessageAsync(chatId, lang == "ru" ? "Придумайте пароль:" : "Құпия сөз ойлап табыңыз:", cancellationToken: token);
                }
                break;

            case "register_password":
                registeredUsers[userIin[chatId]] = messageText;
                userStates[chatId] = "authenticated";
                await bot.SendTextMessageAsync(chatId, lang == "ru" ? "Успешно зарегистрированы!" : "Сәтті тіркелдіңіз!", cancellationToken: token);
                await ShowMainMenu(bot, chatId, lang, token);
                break;

            case "login_iin":
                userIin[chatId] = messageText;
                if (!registeredUsers.ContainsKey(messageText))
                {
                    await bot.SendTextMessageAsync(chatId, lang == "ru" ? "Пользователь не найден." : "Пайдаланушы табылмады.", cancellationToken: token);
                    userStates[chatId] = "choose_action";
                }
                else
                {
                    userStates[chatId] = "login_password";
                    await bot.SendTextMessageAsync(chatId, lang == "ru" ? "Введите пароль:" : "Құпия сөзді енгізіңіз:", cancellationToken: token);
                }
                break;

            case "login_password":
                if (registeredUsers.TryGetValue(userIin[chatId], out var pass) && pass == messageText)
                {
                    userStates[chatId] = "authenticated";
                    await bot.SendTextMessageAsync(chatId, lang == "ru" ? "Вход выполнен!" : "Кіру сәтті орындалды!", cancellationToken: token);
                    await ShowMainMenu(bot, chatId, lang, token);
                }
                else
                {
                    await bot.SendTextMessageAsync(chatId, lang == "ru" ? "Неверный пароль." : "Құпия сөз дұрыс емес.", cancellationToken: token);
                    userStates[chatId] = "choose_action";
                }
                break;

            case "authenticated":
                await HandleMenuSelection(bot, chatId, messageText, lang, token);
                break;

            case "neural_network_query":
                if (messageText == "🔙 Выйти")
                {
                    userStates[chatId] = "choose_action";
                    await bot.SendTextMessageAsync(chatId,
                        lang == "ru" ? "Вы вернулись в главное меню." : "Сіз басты мәзірге оралдыңыз.",
                        replyMarkup: GetMainActionKeyboard(lang),
                        cancellationToken: token);
                    break;
                }

                var response = await GetResponseFromGemini(chatId, messageText);
                await bot.SendTextMessageAsync(chatId, response, replyMarkup: GetExitKeyboard(lang), cancellationToken: token);
                break;
        }
    }

    private static async Task<string> GetResponseFromGemini(long chatId, string userInput)
    {
        if (!userConversations.ContainsKey(chatId))
            userConversations[chatId] = new();

        userConversations[chatId].Add(new Dictionary<string, string> { { "role", "user" }, { "parts", userInput } });

        var messages = new List<object>();
        foreach (var msg in userConversations[chatId])
        {
            messages.Add(new
            {
                role = msg["role"],
                parts = new[] { new { text = msg["parts"] } }
            });
        }

        var requestBody = new { contents = messages };
        var json = JsonConvert.SerializeObject(requestBody);

        var request = new HttpRequestMessage(HttpMethod.Post, geminiEndpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        try
        {
            var response = await client.SendAsync(request);
            var result = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("Gemini API error: " + result);
                return $"Ошибка: {response.StatusCode}. Попробуйте позже.";
            }

            dynamic obj = JsonConvert.DeserializeObject(result);
            string text = obj?.candidates?[0]?.content?.parts?[0]?.text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                userConversations[chatId].Add(new Dictionary<string, string> { { "role", "model" }, { "parts", text } });
                return text.Trim();
            }

            return "Нейросеть не вернула ответа.";
        }
        catch (Exception ex)
        {
            Console.WriteLine("[Gemini Exception] " + ex.Message);
            return "Ошибка при обращении к нейросети.";
        }
    }

    private static async Task ShowMainMenu(ITelegramBotClient bot, long chatId, string lang, CancellationToken token)
    {
        var keyboard = new ReplyKeyboardMarkup(new[]
        {
            new KeyboardButton[] { new(lang == "ru" ? "📄 Отчеты" : "📄 Есептер") },
            new KeyboardButton[] { new(lang == "ru" ? "📊 Налоги" : "📊 Салықтар") },
            new KeyboardButton[] { new(lang == "ru" ? "📎 Документы" : "📎 Құжаттар") },
            new KeyboardButton[] { new(lang == "ru" ? "❓ Помощь" : "❓ Көмек") },
            new KeyboardButton[] { new(lang == "ru" ? "🧠 Нейросеть" : "🧠 Нейрожелілер") }
        })
        { ResizeKeyboard = true };
        await bot.SendTextMessageAsync(chatId, lang == "ru" ? "Выберите раздел меню:" : "Мәзір бөлімін таңдаңыз:", replyMarkup: keyboard, cancellationToken: token);
    }

    private static async Task HandleMenuSelection(ITelegramBotClient bot, long chatId, string messageText, string lang, CancellationToken token)
    {
        if (messageText.Contains("Отчеты") || messageText.Contains("Есептер"))
            await bot.SendTextMessageAsync(chatId, lang == "ru" ? "Ваши отчеты:\n1. Декларация по ИПН\n2. Отчет по соц. выплатам\n3. НДС отчет" : "Сіздің есептеріңіз:\n1. ЖТС декларациясы\n2. Әлеуметтік төлемдер есебі\n3. ҚҚС есебі", cancellationToken: token);
        else if (messageText.Contains("Налоги") || messageText.Contains("Салықтар"))
            await bot.SendTextMessageAsync(chatId, lang == "ru" ? "Раздел 'Налоги' в разработке." : "'Салықтар' бөлімі әзірленуде.", cancellationToken: token);
        else if (messageText.Contains("Документы") || messageText.Contains("Құжаттар"))
            await bot.SendTextMessageAsync(chatId, lang == "ru" ? "Раздел 'Документы' в разработке." : "'Құжаттар' бөлімі әзірленуде.", cancellationToken: token);
        else if (messageText.Contains("Помощь") || messageText.Contains("Көмек"))
            await bot.SendTextMessageAsync(chatId, lang == "ru" ? "Напишите ваш вопрос, и мы постараемся помочь." : "Сұрағыңызды жазыңыз, біз көмектесуге тырысамыз.", cancellationToken: token);
    }

    private static ReplyKeyboardMarkup GetExitKeyboard(string lang) => new(new[] { new[] { new KeyboardButton("🔙 Выйти") } }) { ResizeKeyboard = true };

    private static ReplyKeyboardMarkup GetMainActionKeyboard(string lang) => new(new[]
    {
        new[] { new KeyboardButton(lang == "ru" ? "🔐 Вход" : "🔐 Кіру") },
        new[] { new KeyboardButton(lang == "ru" ? "📝 Регистрация" : "📝 Тіркелу") },
        new[] { new KeyboardButton(lang == "ru" ? "🧠 Нейросеть" : "🧠 Нейрожелілер") }
    })
    { ResizeKeyboard = true };

    private static Task HandleErrorAsync(ITelegramBotClient bot, Exception exception, CancellationToken token)
    {
        Console.WriteLine($"Ошибка: {exception.Message}");
        return Task.CompletedTask;
    }
}


    
