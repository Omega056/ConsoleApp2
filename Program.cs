using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

class Program
{
    private static readonly TelegramBotClient botClient = new TelegramBotClient("8024660428:AAFTnnOrNPy6srKgYIJ74IOidhUFORYFUds");
    private static readonly Dictionary<long, string> userStates = new();
    private static readonly Dictionary<long, string> userLang = new();
    private static readonly Dictionary<string, string> registeredUsers = new(); // ИИН -> Пароль
    private static readonly Dictionary<long, string> userIin = new(); // chatId -> ИИН

    static async Task Main()
    {
        using var cts = new CancellationTokenSource();

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = Array.Empty<UpdateType>()
        };

        botClient.StartReceiving(
            HandleUpdateAsync,
            HandleErrorAsync,
            receiverOptions,
            cancellationToken: cts.Token
        );

        var me = await botClient.GetMeAsync();
        Console.WriteLine($"Бот запущен: @{me.Username}");
        Console.ReadLine();
    }

    private static async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken token)
    {
        if (update.Message is not { } message || message.Text is not { } messageText)
            return;

        var chatId = message.Chat.Id;

        if (!userLang.ContainsKey(chatId))
        {
            await bot.SendTextMessageAsync(chatId, "Выберите язык / Тілді таңдаңыз:\n1. Русский\n2. Қазақша", cancellationToken: token);
            userLang[chatId] = "waiting_language";
            return;
        }

        if (userLang[chatId] == "waiting_language")
        {
            if (messageText == "1")
                userLang[chatId] = "ru";
            else if (messageText == "2")
                userLang[chatId] = "kz";
            else
            {
                await bot.SendTextMessageAsync(chatId, "Неверный выбор / Таңдау дұрыс емес", cancellationToken: token);
                return;
            }

            userStates[chatId] = "choose_action";
            var lang = userLang[chatId];
            var replyKeyboard = new ReplyKeyboardMarkup(new[]
            {
                new[] { new KeyboardButton(lang == "ru" ? "🔐 Вход" : "🔐 Кіру") },
                new[] { new KeyboardButton(lang == "ru" ? "📝 Регистрация" : "📝 Тіркелу") }
            })
            {
                ResizeKeyboard = true
            };

            await bot.SendTextMessageAsync(chatId, lang == "ru" ? "Выберите действие:" : "Әрекетті таңдаңыз:", replyMarkup: replyKeyboard, cancellationToken: token);
            return;
        }

        var state = userStates.GetValueOrDefault(chatId);

        switch (state)
        {
            case "choose_action":
                if (messageText.Contains("Регистрация") || messageText.Contains("Тіркелу"))
                {
                    userStates[chatId] = "register_iin";
                    await bot.SendTextMessageAsync(chatId, userLang[chatId] == "ru" ? "Введите ИИН для регистрации:" : "Тіркелу үшін ИИН енгізіңіз:", cancellationToken: token);
                }
                else if (messageText.Contains("Вход") || messageText.Contains("Кіру"))
                {
                    userStates[chatId] = "login_iin";
                    await bot.SendTextMessageAsync(chatId, userLang[chatId] == "ru" ? "Введите ИИН для входа:" : "Кіру үшін ИИН енгізіңіз:", cancellationToken: token);
                }
                break;

            case "register_iin":
                userIin[chatId] = messageText;
                if (registeredUsers.ContainsKey(messageText))
                {
                    await bot.SendTextMessageAsync(chatId, userLang[chatId] == "ru" ? "Этот ИИН уже зарегистрирован." : "Бұл ИИН бұрын тіркелген.", cancellationToken: token);
                    userStates[chatId] = "choose_action";
                }
                else
                {
                    userStates[chatId] = "register_password";
                    await bot.SendTextMessageAsync(chatId, userLang[chatId] == "ru" ? "Придумайте пароль:" : "Құпия сөз ойлап табыңыз:", cancellationToken: token);
                }
                break;

            case "register_password":
                registeredUsers[userIin[chatId]] = messageText;
                userStates[chatId] = "authenticated";
                await bot.SendTextMessageAsync(chatId, userLang[chatId] == "ru" ? "Успешно зарегистрированы!" : "Сәтті тіркелдіңіз!", cancellationToken: token);
                await ShowMainMenu(bot, chatId, userLang[chatId], token);
                break;

            case "login_iin":
                userIin[chatId] = messageText;
                if (!registeredUsers.ContainsKey(messageText))
                {
                    await bot.SendTextMessageAsync(chatId, userLang[chatId] == "ru" ? "Пользователь не найден." : "Пайдаланушы табылмады.", cancellationToken: token);
                    userStates[chatId] = "choose_action";
                }
                else
                {
                    userStates[chatId] = "login_password";
                    await bot.SendTextMessageAsync(chatId, userLang[chatId] == "ru" ? "Введите пароль:" : "Құпия сөзді енгізіңіз:", cancellationToken: token);
                }
                break;

            case "login_password":
                var enteredPassword = messageText;
                var iin = userIin[chatId];
                if (registeredUsers[iin] == enteredPassword)
                {
                    userStates[chatId] = "authenticated";
                    await bot.SendTextMessageAsync(chatId, userLang[chatId] == "ru" ? "Вход выполнен!" : "Кіру сәтті орындалды!", cancellationToken: token);
                    await ShowMainMenu(bot, chatId, userLang[chatId], token);
                }
                else
                {
                    await bot.SendTextMessageAsync(chatId, userLang[chatId] == "ru" ? "Неверный пароль." : "Құпия сөз дұрыс емес.", cancellationToken: token);
                    userStates[chatId] = "choose_action";
                }
                break;

            case "authenticated":
                await HandleMenuSelection(bot, chatId, messageText, userLang[chatId], token);
                break;
        }
    }

    private static async Task ShowMainMenu(ITelegramBotClient bot, long chatId, string lang, CancellationToken token)
    {
        var keyboard = new ReplyKeyboardMarkup(new[]
        {
            new[] { new KeyboardButton(lang == "ru" ? "📄 Отчеты" : "📄 Есептер") },
            new[] { new KeyboardButton(lang == "ru" ? "📊 Налоги" : "📊 Салықтар") },
            new[] { new KeyboardButton(lang == "ru" ? "📎 Документы" : "📎 Құжаттар") },
            new[] { new KeyboardButton(lang == "ru" ? "❓ Помощь" : "❓ Көмек") }
        })
        {
            ResizeKeyboard = true
        };

        await bot.SendTextMessageAsync(chatId, lang == "ru" ? "Выберите раздел меню:" : "Мәзір бөлімін таңдаңыз:", replyMarkup: keyboard, cancellationToken: token);
    }

    private static async Task HandleMenuSelection(ITelegramBotClient bot, long chatId, string messageText, string lang, CancellationToken token)
    {
        if (messageText.Contains("Отчеты") || messageText.Contains("Есептер"))
            await SendReports(bot, chatId, lang, token);
        else if (messageText.Contains("Налоги") || messageText.Contains("Салықтар"))
            await bot.SendTextMessageAsync(chatId, lang == "ru" ? "Раздел 'Налоги' в разработке." : "'Салықтар' бөлімі әзірленуде.", cancellationToken: token);
        else if (messageText.Contains("Документы") || messageText.Contains("Құжаттар"))
            await bot.SendTextMessageAsync(chatId, lang == "ru" ? "Раздел 'Документы' в разработке." : "'Құжаттар' бөлімі әзірленуде.", cancellationToken: token);
        else if (messageText.Contains("Помощь") || messageText.Contains("Көмек"))
            await bot.SendTextMessageAsync(chatId, lang == "ru" ? "Напишите ваш вопрос, и мы постараемся помочь." : "Сұрағыңызды жазыңыз, біз көмектесуге тырысамыз.", cancellationToken: token);
    }

    private static async Task SendReports(ITelegramBotClient bot, long chatId, string lang, CancellationToken token)
    {
        var reports = lang == "ru"
            ? "Ваши отчеты:\n1. Декларация по ИПН\n2. Отчет по соц. выплатам\n3. НДС отчет"
            : "Сіздің есептеріңіз:\n1. ЖТС декларациясы\n2. Әлеуметтік төлемдер есебі\n3. ҚҚС есебі";

        await bot.SendTextMessageAsync(chatId, reports, cancellationToken: token);
    }

    private static Task HandleErrorAsync(ITelegramBotClient bot, Exception exception, CancellationToken token)
    {
        Console.WriteLine($"Ошибка: {exception.Message}");
        return Task.CompletedTask;
    }
}
