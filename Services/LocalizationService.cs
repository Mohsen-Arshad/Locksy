using System.Globalization;
using Microsoft.Maui.Storage;
using Microsoft.Maui.Controls;

namespace Locksy.Services;

/// <summary>Small, dependency-free runtime localization for Locksy's English and Persian UI.</summary>
public static class LocalizationService
{
    private const string LanguagePreferenceKey = "locksy-language";
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo PersianCulture = CultureInfo.GetCultureInfo("fa-IR");

    private static readonly IReadOnlyDictionary<string, string> Persian = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Keep what matters in sight."] = "چیزهای مهم را همیشه جلوی چشم نگه دار.",
        ["Your tasks, right on your lock screen."] = "کارهایت، مستقیم روی صفحهٔ قفل.",
        ["A LITTLE MORE IN SIGHT."] = "چیزهای مهم، همیشه در دیدرس.",
        ["Open application settings"] = "بازکردن تنظیمات برنامه",
        ["Automatically refresh wallpaper after task edits"] = "پس‌زمینه پس از تغییر کارها به‌طور خودکار تازه شود",
        ["Select a background image"] = "انتخاب تصویر پس‌زمینه",
        ["GitHub · Mohsen-Arshad"] = "گیت‌هاب · Mohsen-Arshad",
        ["Switch between dark and light theme"] = "تغییر بین پوستهٔ روشن و تیره",
        ["Drag to reorder"] = "برای مرتب‌سازی بکش",
        ["Selected reminder color"] = "رنگ انتخاب‌شدهٔ یادآور",
        ["DARK"] = "تیره",
        ["LIGHT"] = "روشن",
        ["Add a task..."] = "افزودن یک کار...",
        ["＋ Add"] = "＋ افزودن",
        ["Your list is clear"] = "فهرستت خالی است",
        ["Add your first task. It will be saved on this device."] = "اولین کارت را اضافه کن؛ اطلاعات روی همین دستگاه ذخیره می‌شود.",
        ["Clock layout"] = "چیدمان ساعت",
        ["Tasks below top clock"] = "کارها زیر ساعت بالای صفحه",
        ["Tasks below top clock (legacy)"] = "کارها زیر ساعت بالای صفحه",
        ["Tasks bottom"] = "کارها پایین صفحه",
        ["Tasks top"] = "کارها بالای صفحه",
        ["Centered task list"] = "فهرست وسط‌چین",
        ["Split around center clock"] = "تقسیم فهرست اطراف ساعت مرکزی",
        ["Screen-aware (auto)"] = "چیدمان خودکار متناسب با صفحه",
        ["Auto update"] = "به‌روزرسانی خودکار",
        ["Apply edits when app is in background"] = "اعمال تغییرات هنگام رفتن برنامه به پس‌زمینه",
        ["Clear completed"] = "پاک‌کردن کارهای انجام‌شده",
        ["Apply wallpaper"] = "اعمال پس‌زمینه",
        ["Applying…"] = "در حال اعمال…",
        ["Ready · Drag ⠿ to reorder"] = "آماده · برای مرتب‌سازی، ⠿ را بکش",
        ["First 8 tasks on wallpaper"] = "۸ کار اول روی پس‌زمینه نمایش داده می‌شوند",
        ["Moving task"] = "در حال جابه‌جایی کار",
        ["LOCKSY  ·  QUICK ACTION"] = "LOCKSY · عملیات سریع",
        ["LOCKSY · QUICK ACTION"] = "LOCKSY · عملیات سریع",
        ["LOCKSY · SETTINGS"] = "LOCKSY · تنظیمات",
        ["Choose the app's light or dark appearance."] = "ظاهر روشن یا تیرهٔ برنامه را انتخاب کن.",
        ["Choose English or Persian for the app."] = "زبان انگلیسی یا فارسی را برای برنامه انتخاب کن.",
        ["App information and project source code."] = "اطلاعات برنامه و کد منبع پروژه.",
        ["A light surface with dark text."] = "ظاهر روشن با نوشته‌های تیره.",
        ["A dark surface for low-light use."] = "ظاهر تیره برای محیط‌های کم‌نور.",
        ["Use the default English interface."] = "از رابط پیش‌فرض انگلیسی استفاده کن.",
        ["Use the Persian right-to-left interface."] = "از رابط فارسی با چیدمان راست‌به‌چپ استفاده کن.",
        ["Select a personal image from this device."] = "یک تصویر دلخواه از همین دستگاه انتخاب کن.",
        ["Restore the built-in Locksy background."] = "پس‌زمینهٔ اصلی Locksy را برگردان.",
        ["View the Locksy source code on GitHub."] = "کد منبع Locksy را در گیت‌هاب ببین.",
        ["Open GitHub repository"] = "بازکردن مخزن گیت‌هاب",
        ["Back to settings"] = "بازگشت به تنظیمات",
        ["Settings"] = "تنظیمات",
        ["Theme"] = "پوسته",
        ["Manage your app theme, language, custom wallpaper and app information."] = "پوسته، زبان، تصویر پس‌زمینه و اطلاعات برنامه را مدیریت کن.",
        ["Choose the app appearance."] = "ظاهر برنامه را انتخاب کن.",
        ["Light theme"] = "پوستهٔ روشن",
        ["Dark theme"] = "پوستهٔ تیره",
        ["The light theme is currently active."] = "در حال حاضر پوستهٔ روشن فعال است.",
        ["The dark theme is currently active."] = "در حال حاضر پوستهٔ تیره فعال است.",
        ["Manage your app language, custom wallpaper and app information."] = "زبان برنامه، پس‌زمینهٔ دلخواه و اطلاعات برنامه را مدیریت کن.",
        ["Language"] = "زبان برنامه",
        ["Choose your preferred language. English is the default."] = "زبان موردنظرت را انتخاب کن. زبان پیش‌فرض انگلیسی است.",
        ["Background image"] = "تصویر پس‌زمینه",
        ["About"] = "دربارهٔ برنامه",
        ["Close"] = "بستن",
        ["English"] = "English",
        ["فارسی"] = "فارسی",
        ["Language updated."] = "زبان برنامه تغییر کرد.",
        ["Choose a personal image for your lock-screen wallpaper."] = "می‌توانی تصویر دلخواهت را برای پس‌زمینهٔ صفحهٔ قفل انتخاب کنی.",
        ["Choose image"] = "انتخاب تصویر",
        ["Remove custom background"] = "حذف تصویر دلخواه",
        ["Cancel"] = "انصراف",
        ["A custom background is currently active."] = "در حال حاضر تصویر دلخواه فعال است.",
        ["The default Locksy background is currently active."] = "در حال حاضر پس‌زمینهٔ پیش‌فرض Locksy فعال است.",
        ["Background image saved. Apply the wallpaper to see it on your lock screen."] = "تصویر پس‌زمینه ذخیره شد. برای دیدنش روی صفحهٔ قفل، پس‌زمینه را اعمال کن.",
        ["Background image saved. It will be applied when Locksy moves to the background."] = "تصویر ذخیره شد؛ با رفتن Locksy به پس‌زمینه، روی صفحهٔ قفل اعمال می‌شود.",
        ["Background image saved. Tap Apply wallpaper to see it on your lock screen."] = "تصویر ذخیره شد؛ برای نمایش روی صفحهٔ قفل، روی «اعمال پس‌زمینه» بزن.",
        ["Custom background removed. The built-in background will be restored when Locksy moves to the background."] = "تصویر دلخواه حذف شد؛ با رفتن Locksy به پس‌زمینه، طرح اصلی برمی‌گردد.",
        ["Custom background removed. Tap Apply wallpaper to update your lock screen."] = "تصویر دلخواه حذف شد؛ برای به‌روزرسانی صفحهٔ قفل، روی «اعمال پس‌زمینه» بزن.",
        ["Background image removed."] = "تصویر دلخواه حذف شد.",
        ["No image was selected."] = "تصویری انتخاب نشد.",
        ["The selected image could not be saved."] = "ذخیرهٔ تصویر انتخاب‌شده ممکن نشد.",
        ["Locksy keeps important tasks visible directly on your Android lock screen. Customize task colors, reminders, theme, layout and background image. Your task list is stored locally on this device."] = "Locksy کمک می‌کند کارهای مهمت را مستقیم روی صفحهٔ قفل اندروید ببینی. رنگ هر کار، یادآورها، پوسته، چیدمان و تصویر پس‌زمینه را شخصی‌سازی کن. فهرست کارها به‌صورت محلی روی همین دستگاه نگهداری می‌شود.",
        ["Remove ‘{task.Title}’ from your list? This can’t be undone."] = "حذف کار از فهرست؟ این کار قابل بازگشت نیست.",
        ["Choose what to do with a task."] = "عملیات موردنظرت را برای این کار انتخاب کن.",
        ["Could not save image"] = "ذخیرهٔ تصویر ممکن نشد",
        ["Could not open GitHub"] = "گیت‌هاب باز نشد",
        ["Please check your internet connection and try again."] = "اتصال اینترنت را بررسی کن و دوباره تلاش کن.",
        ["Please select an image smaller than 25 MB."] = "تصویری کوچک‌تر از ۲۵ مگابایت انتخاب کن.",
        ["Version"] = "نسخه",
        ["Open GitHub"] = "بازکردن گیت‌هاب",
        ["GitHubProfileLabel"] = "پروفایل گیت‌هاب سازنده",
        ["Reminder title"] = "عنوان یادآور",
        ["Task color"] = "رنگ کار",
        ["Reminder alarm"] = "زنگ یادآور",
        ["Reminder date and time"] = "تاریخ و زمان یادآور",
        ["Choose a future date and time for the alarm."] = "برای زنگ، تاریخ و زمانی در آینده انتخاب کن.",
        ["Enter a reminder title before adding it."] = "پیش از افزودن، عنوانی برای یادآور وارد کن.",
        ["Settings saved for the next reminder."] = "تنظیمات برای یادآور بعدی ذخیره شد.",
        ["Notification permission"] = "مجوز اعلان‌ها",
        ["Choose a future time"] = "زمانی در آینده انتخاب کن",
        ["The alarm time must be later than the current time."] = "زمان زنگ باید از زمان فعلی دیرتر باشد.",
        ["Press back again to leave Locksy."] = "برای خروج از Locksy، یک بار دیگر بازگشت را بزن.",
        ["Auto update queued for background."] = "به‌روزرسانی خودکار برای پس‌زمینه در صف قرار گرفت.",
        ["Task options"] = "گزینه‌های کار",
        ["Choose what to do with ‘{task.Title}’. "] = "عملیات موردنظرت را برای این کار انتخاب کن.",
        ["Edit task"] = "ویرایش کار",
        ["Change row color"] = "تغییر رنگ کار",
        ["Set / edit alarm"] = "تنظیم / ویرایش زنگ",
        ["Delete task"] = "حذف کار",
        ["Delete this task?"] = "این کار حذف شود؟",
        ["Make the change and save when you’re ready."] = "تغییرات را انجام بده و در پایان ذخیره کن.",
        ["Task text"] = "متن کار",
        ["Task alarm"] = "زنگ کار",
        ["Choose a date and time for a one-time reminder. Android may delay reminders slightly if exact-alarm access is disabled."] = "برای یادآور یک‌باره تاریخ و زمان انتخاب کن. اگر دسترسی زنگ دقیق غیرفعال باشد، ممکن است اندروید کمی تأخیر داشته باشد.",
        ["Enable reminder"] = "فعال‌کردن یادآور",
        ["Reminder date"] = "تاریخ یادآور",
        ["Reminder time"] = "زمان یادآور",
        ["Save alarm"] = "ذخیرهٔ زنگ",
        ["Reminder disabled."] = "یادآور غیرفعال شد.",
        ["Alarm set for {scheduledAt:dd MMM, HH:mm}."] = "زنگ برای زمان انتخاب‌شده تنظیم شد.",
        ["Choose a palette first, then a color. Your choice stays the same in Light and Dark mode."] = "ابتدا پالت و سپس رنگ را انتخاب کن. انتخابت در پوستهٔ روشن و تیره ثابت می‌ماند.",
        ["Wallpaper placement"] = "جایگاه پس‌زمینه",
        ["Auto adapts the layout to your screen ratio. Adjust the offset to line it up with your lock-screen clock."] = "چیدمان به نسبت صفحه سازگار می‌شود. جابه‌جایی را تنظیم کن تا فهرست با ساعت صفحهٔ قفل هماهنگ شود.",
        ["PLACEMENT PROFILE"] = "الگوی چیدمان",
        ["Move content up or down"] = "جابه‌جایی محتوا به بالا یا پایین",
        ["Empty space around clock"] = "فضای خالی اطراف ساعت",
        ["% of screen height"] = "٪ از ارتفاع صفحه",
        ["Clock spacing applies mainly to Split around center clock. Offset moves the wallpaper content and its clear area together."] = "فاصلهٔ اطراف ساعت بیشتر در حالت تقسیم اطراف ساعت مرکزی کاربرد دارد. جابه‌جایی، محتوا و فضای خالی را با هم حرکت می‌دهد.",
        ["Save"] = "ذخیره",
        ["Save changes"] = "ذخیرهٔ تغییرات",
        ["Got it"] = "متوجه شدم",
        ["Wallpaper placement saved."] = "جایگاه پس‌زمینه ذخیره شد.",
        ["Dark theme selected."] = "پوستهٔ تیره انتخاب شد.",
        ["Light theme selected."] = "پوستهٔ روشن انتخاب شد.",
        ["Auto Apply is enabled; the clean wallpaper will be refreshed in the background."] = "به‌روزرسانی خودکار روشن است؛ پس‌زمینه در حالت پس‌زمینه به‌روز می‌شود.",
        ["Moved ‘{source.Title}’."] = "جای کار تغییر کرد.",
        ["Drop on another task to change its position."] = "برای تغییر جایگاه، کار را روی کار دیگری رها کن.",
        ["Move over another task to place it."] = "برای جابه‌جایی، روی کار دیگری برو.",
        ["Could not select image"] = "انتخاب تصویر ممکن نشد",
        ["No offset"] = "بدون جابه‌جایی",
        ["up"] = "به بالا",
        ["down"] = "به پایین",
        ["Automatic wallpaper updates paused."] = "به‌روزرسانی خودکار پس‌زمینه متوقف شد.",
        ["Preparing lock-screen wallpaper…"] = "در حال آماده‌سازی پس‌زمینهٔ صفحهٔ قفل…",
        ["Lock screen updated at {DateTime.Now:HH:mm}."] = "پس‌زمینهٔ صفحهٔ قفل به‌روزرسانی شد.",
        ["Wallpaper update cancelled."] = "به‌روزرسانی پس‌زمینه لغو شد.",
        ["Could not update wallpaper."] = "به‌روزرسانی پس‌زمینه ممکن نشد.",
        ["Wallpaper updated"] = "پس‌زمینه به‌روزرسانی شد",
        ["Your lock-screen wallpaper is ready. Here is the exact image applied to your lock screen."] = "پس‌زمینهٔ صفحهٔ قفل آماده است. تصویر اعمال‌شده را در پیش‌نمایش می‌بینی.",
        ["Couldn’t update wallpaper"] = "به‌روزرسانی پس‌زمینه انجام نشد",
        ["Wallpaper updated at"] = "زمان به‌روزرسانی پس‌زمینه",
        ["Status"] = "وضعیت",
        ["Default"] = "پیش‌فرض",
        ["No color"] = "بدون رنگ",
        ["Soft Light"] = "روشن ملایم",
        ["Deep Dark"] = "تیرهٔ عمیق",
        ["Vibrant"] = "پررنگ",
        ["Green"] = "سبز",
        ["Blue"] = "آبی",
        ["Amber"] = "کهربایی",
        ["Rose"] = "رز",
        ["Violet"] = "بنفش",
        ["Teal"] = "سبزآبی",
        ["Orange"] = "نارنجی",
        ["Slate"] = "خاکستری سنگی",
        ["Mint"] = "نعنایی",
        ["Sky Blue"] = "آبی آسمانی",
        ["Lilac"] = "یاسی",
        ["Lemon"] = "لیمویی",
        ["Coral"] = "مرجانی",
        ["Periwinkle"] = "آبی پریوش",
        ["Peach"] = "هلویی",
        ["Seafoam"] = "سبز دریایی",
        ["New reminder settings"] = "تنظیم یادآور جدید",
        ["Enter the reminder, choose its color, and optionally set an alarm before adding it."] = "متن یادآور را بنویس، رنگش را انتخاب کن و در صورت نیاز پیش از افزودن، زنگی برایش تنظیم کن.",
        ["What do you need to remember?"] = "چه چیزی را می‌خواهی به خاطر بسپاری؟",
        ["Save settings"] = "ذخیرهٔ تنظیمات",
        ["Add reminder"] = "افزودن یادآور",
        ["The saved reminder time has passed. Open settings and choose a future date and time before adding this reminder."] = "زمان ذخیره‌شدهٔ یادآور گذشته است. پیش از افزودن، تنظیمات را باز کن و تاریخ و زمان آینده‌ای انتخاب کن.",
        ["Notifications are disabled"] = "اعلان‌ها غیرفعال‌اند",
        ["Locksy cannot show the reminder until notification permission is enabled in Android Settings."] = "تا وقتی مجوز اعلان‌ها را در تنظیمات اندروید فعال نکنی، Locksy نمی‌تواند یادآور را نمایش دهد.",
        ["The selected image is empty or could not be read."] = "تصویر انتخاب‌شده خالی است یا خوانده نمی‌شود.",
        ["characters · Scroll to read longer text"] = "نویسه · برای خواندن متن بلندتر پیمایش کن",
        ["Scroll to read longer text"] = "برای خواندن متن بلندتر پیمایش کن",
        ["Start typing a task"] = "نوشتن یک کار را شروع کن",
        ["No choices?"] = "گزینه‌ای نیست؟",
        ["Switch language"] = "تغییر زبان",
        ["Custom background"] = "پس‌زمینهٔ دلخواه",
        ["Clear image"] = "حذف تصویر",
        ["The image could not be decoded."] = "رمزگشایی تصویر ممکن نشد.",
        ["Reminder added."] = "یادآور اضافه شد.",
        ["Alarm saved for {scheduledAt:dd MMM, HH:mm}; Android may deliver it a little late."] = "زنگ ذخیره شد؛ ممکن است اندروید آن را کمی دیرتر اجرا کند.",
    };

    public static string FormatCharacterCounter(int length, int maxLength) => IsPersian
        ? $"{ToPersianDigits(length)}/{ToPersianDigits(maxLength)} نویسه · برای خواندن متن بلندتر پیمایش کن"
        : $"{length}/{maxLength} characters · Scroll to read longer text";

    private static string ToPersianDigits(int value) => LocalizeDigits(value.ToString(CultureInfo.InvariantCulture));

    public static string LocalizeDigits(string value)
    {
        if (!IsPersian || string.IsNullOrEmpty(value)) return value;
        return value.Replace('0', '۰').Replace('1', '۱').Replace('2', '۲').Replace('3', '۳').Replace('4', '۴')
            .Replace('5', '۵').Replace('6', '۶').Replace('7', '۷').Replace('8', '۸').Replace('9', '۹');
    }

    public static string CurrentLanguage => Preferences.Default.Get(LanguagePreferenceKey, "en");
    public static bool IsPersian => string.Equals(CurrentLanguage, "fa", StringComparison.OrdinalIgnoreCase);

    public static string Translate(string? value)
    {
        if (string.IsNullOrEmpty(value) || !IsPersian) return value ?? string.Empty;
        return Persian.TryGetValue(value, out var translated) ? translated : value;
    }

    public static void SetLanguage(string languageCode)
    {
        var code = string.Equals(languageCode, "fa", StringComparison.OrdinalIgnoreCase) ? "fa" : "en";
        Preferences.Default.Set(LanguagePreferenceKey, code);
        ApplyCulture();
    }

    public static void ApplyCulture()
    {
        var culture = IsPersian ? PersianCulture : EnglishCulture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public static FlowDirection CurrentFlowDirection => IsPersian ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
}
