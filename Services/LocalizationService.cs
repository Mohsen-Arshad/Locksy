using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Maui.Controls;
using MauiPreferences = Microsoft.Maui.Storage.Preferences;

namespace Locksy.Services;

/// <summary>Small, dependency-free runtime localization for Locksy's English and Persian UI.</summary>
public static class LocalizationService
{
    private const string LanguagePreferenceKey = "locksy-language";
    private const string EnglishDefaultMigrationKey = "locksy-v1.2.2-english-default-applied";
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo PersianCulture = CultureInfo.GetCultureInfo("fa-IR");

    private static readonly Regex EmbeddedLeftToRightRun = new(
        @"(?i:https?://[^\s]+)|(?<![A-Za-z0-9])[A-Za-z0-9]+(?:[._:/?#&=%+@'-][A-Za-z0-9]+)*(?:\s+[A-Za-z0-9]+(?:[._:/?#&=%+@'-][A-Za-z0-9]+)*)*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly IReadOnlyDictionary<string, string> Persian = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Set a title, then open color or alarm options only when you need them."] = "اول یک عنوان بنویس، بعد فقط در صورت نیاز رنگشو عوض کن یا زنگ هشدار یاداوری بذار براش.",
        ["Keep what matters in sight."] = "کارهای مهم را همیشه جلوی چشم نگه دار.",
        ["Your tasks, right on your lock screen."] = "لیست کارهات روی بک‌گراند قفل صفحه نمایشت",
        ["A LITTLE MORE IN SIGHT."] = "چیزهای مهم، همیشه در دیدرس تو.",
        ["Open application settings"] = "بازکردن تنظیمات برنامه",
        ["Automatically refresh wallpaper after task edits"] = "پس‌زمینه پس از تغییر کارها به‌طور خودکار تازه شود",
        ["Select a background image"] = "انتخاب تصویر پس‌زمینه",
        ["Select slideshow images"] = "انتخاب تصاویر اسلایدشو",
        ["Default Locksy background"] = "پس‌زمینه پیش‌فرض Locksy",
        ["Use the original Locksy gradient and grid."] = "از گرادیان و شبکه اصلی Locksy استفاده کن.",
        ["Choose one photo"] = "انتخاب یک تصویر",
        ["Use one photo and crop it to fill the lock screen."] = "یک تصویر انتخاب کن تا متناسب سایز صفحه، تمام صفحه را بپوشاند.",
        ["Built-in backgrounds"] = "پس‌زمینه‌های آماده",
        ["Slideshow"] = "اسلایدشو",
        ["Choose multiple photos"] = "انتخاب چند تصویر",
        ["Add several photos from your device to the slideshow."] = "چند تصویر از دستگاهت به اسلایدشو اضافه کن.",
        ["Use slideshow"] = "استفاده از اسلایدشو",
        ["Cycle through your selected photos and built-in backgrounds."] = "تصاویر به‌صورت چرخشی تغییر کند.",
        ["Current background preview · images fill the screen with center-crop."] = "پیش‌نمایش پس‌زمینه فعلی.",
        ["Current background"] = "پس‌زمینه فعلی",
        ["Custom photo"] = "تصویر دلخواه",
        ["Ocean"] = "اقیانوس",
        ["Cool blue gradients"] = "گرادیان‌های آبی خنک",
        ["Sunset"] = "غروب",
        ["Warm evening colors"] = "رنگ‌های گرم غروب",
        ["Forest"] = "جنگل",
        ["Deep greens"] = "سبزهای عمیق",
        ["Soft rose and plum"] = "رز و آلویی ملایم",
        ["Lavender"] = "اسطوخودوسی",
        ["Violet and lilac"] = "بنفش و یاسی",
        ["Golden warm tones"] = "رنگ‌های گرم طلایی",
        ["Quiet steel and graphite"] = "فولادی و گرافیتی آرام",
        ["Aurora"] = "شفق قطبی",
        ["Teal light over midnight"] = "نور سبزآبی روی زمینه نیمه‌شب",
        ["Use"] = "استفاده",
        ["Add preset to slideshow"] = "افزودن پس‌زمینه آماده به اسلایدشو",
        ["Remove preset from slideshow"] = "حذف پس‌زمینه آماده از اسلایدشو",
        ["Change every"] = "فاصله زمانی تغییر تصاویر",
        ["Slideshow interval"] = "فاصله زمانی اسلایدشو",
        ["Choose how often the background changes. Select an interval below."] = "مشخص کن پس‌زمینه هر چند وقت یک‌بار تغییر کند؛ یکی از بازه‌های زیر را انتخاب کن.",
        ["Tap an interval to select it."] = "برای انتخاب، روی یکی از بازه‌های زمانی بزن.",
        ["Change slideshow interval"] = "تغییر فاصله زمانی اسلایدشو",
        ["Back to background"] = "بازگشت به پس‌زمینه",
        ["Every 5 minutes"] = "هر ۵ دقیقه",
        ["Every 10 minutes"] = "هر ۱۰ دقیقه",
        ["Every 15 minutes"] = "هر ۱۵ دقیقه",
        ["Every 30 minutes"] = "هر ۳۰ دقیقه",
        ["Every 60 minutes"] = "هر ۶۰ دقیقه",
        ["Slideshow items:"] = "تصاویر اسلایدشو:",
        ["No slideshow items yet. Add photos or use the + button on a built-in background."] = "هنوز تصویری برای اسلایدشو وجود ندارد. تصویر اضافه کن یا دکمه + کنار پس‌زمینه‌های آماده را بزن.",
        ["Background added to slideshow."] = "پس‌زمینه به اسلایدشو اضافه شد.",
        ["The slideshow already has the maximum number of items."] = "اسلایدشو به بیشترین تعداد مجاز تصاویر رسیده است.",
        ["Add at least two backgrounds to start a slideshow."] = "برای شروع اسلایدشو حداقل دو پس‌زمینه اضافه کن.",
        ["Slideshow selected. The rotation will start when the wallpaper is applied."] = "اسلایدشو انتخاب شد؛ چرخش پس‌زمینه پس از اعمال آن شروع می‌شود.",
        ["Slideshow selected. Tap Apply wallpaper to start the rotation."] = "اسلایدشو انتخاب شد؛ برای شروع چرخش روی «اعمال پس‌زمینه» بزن.",
        ["Slideshow item removed. Apply the wallpaper to update the rotation."] = "مورد از اسلایدشو حذف شد؛ برای به‌روزرسانی چرخش، پس‌زمینه را اعمال کن.",
        ["Slideshow interval saved. Apply the wallpaper to restart its timer."] = "فاصله زمانی ذخیره شد؛ برای شروع مجدد زمان‌سنج، پس‌زمینه را اعمال کن.",
        ["Photo added. Add at least one more background to start a slideshow."] = "تصویر اضافه شد؛ برای شروع اسلایدشو حداقل یک پس‌زمینه دیگر اضافه کن.",
        ["The selected images could not be saved."] = "ذخیره تصاویر انتخاب‌شده ممکن نشد.",
        ["Default background selected. It will be applied when Locksy moves to the background."] = "پس‌زمینه پیش‌فرض انتخاب شد؛ با رفتن Locksy به پس‌زمینه اعمال می‌شود.",
        ["Default background selected. Tap Apply wallpaper to update your lock screen."] = "پس‌زمینه پیش‌فرض انتخاب شد؛ برای به‌روزرسانی تصویر قفل صفحه نمایش، روی «اعمال پس‌زمینه» بزن.",
        ["Background preset selected. It will be applied when Locksy moves to the background."] = "پس‌زمینه آماده انتخاب شد؛ با رفتن Locksy به پس‌زمینه اعمال می‌شود.",
        ["Background preset selected. Tap Apply wallpaper to update your lock screen."] = "پس‌زمینه آماده انتخاب شد؛ برای به‌روزرسانی تصویر قفل صفحه نمایش، روی «اعمال پس‌زمینه» بزن.",
        ["A built-in background is currently active."] = "در حال حاضر یکی از پس‌زمینه‌های آماده فعال است.",
        ["A custom image is currently active."] = "در حال حاضر تصویر دلخواه فعال است.",
        ["Wallpaper slideshow is currently active."] = "در حال حاضر اسلایدشوی پس‌زمینه فعال است.",
        ["GitHub · Mohsen-Arshad"] = "گیت‌هاب · Mohsen-Arshad",
        ["Switch between dark and light theme"] = "تغییر بین پوسته روشن و تیره",
        ["Drag to reorder"] = "برای مرتب‌سازی بکش",
        ["Selected reminder color"] = "رنگ انتخاب‌شده یادآور",
        ["DARK"] = "تیره",
        ["LIGHT"] = "روشن",
        ["Add a task..."] = "افزودن یک کار جدید ...",
        ["Task details"] = "متن کامل کار",
        ["Keep typing. The editor will expand automatically."] = "به نوشتن ادامه بده؛ ویرایشگر به‌صورت خودکار باز شده است.",
        ["Write your task here..."] = "متن کار را اینجا بنویس...",
        ["Done"] = "انجام شد",
        ["Add"] = "افزودن",
        ["Your list is clear"] = "فهرست خالی است",
        ["Add your first task. It will be saved on this device."] = "اولین یادآوری را اضافه کن؛ اطلاعات روی همین دستگاه ذخیره میشود",
        ["Tasks layout"] = "چیدمان کارها",
        ["Tasks below top clock"] = "کارها زیر ساعت بالای صفحه",
        ["Tasks below top clock (legacy)"] = "کارها زیر ساعت بالای صفحه",
        ["Tasks bottom"] = "کارها پایین صفحه",
        ["Tasks top"] = "کارها بالای صفحه",
        ["Centered task list"] = "فهرست وسط‌چین",
        ["Split around center clock"] = "تقسیم فهرست اطراف ساعت مرکزی",
        ["Screen-aware (auto)"] = "چیدمان خودکار متناسب با صفحه",
        ["Auto update"] = "به‌روزرسانی خودکار",
        ["Apply edits when app is in background"] = "اعمال تغییرات هنگام رفتن برنامه به پس‌زمینه یا بستن برنامه",
        ["Clear completed"] = "پاک‌کردن کارهای انتخاب شده",
        ["Apply wallpaper"] = "اعمال پس‌زمینه",
        ["Applying…"] = "در حال اعمال…",
        ["Text direction"] = "جهت نوشتار",
        ["Task text direction"] = "جهت نوشتار تسک",
        ["Automatic"] = "خودکار",
        ["Left to right"] = "چپ به راست",
        ["Right to left"] = "راست به چپ",
        ["Current setting:"] = "تنظیم فعلی:",
        ["Choose how this task's text should be arranged. Automatic follows the first letter's writing direction."] = "جهت نمایش متن این تسک را انتخاب کن. حالت خودکار بر اساس اولین حرف تعیین می‌شود.",
        ["Ready · Drag the handle to reorder"] = "آماده جابه‌جایی فهرست کارهات · دستک جابه‌جایی را نگه‌دار و بالا پایین بکش",
        ["First 8 tasks on wallpaper"] = "۸ کار اول روی پس‌زمینه نمایش داده می‌شوند",
        ["Moving task"] = "در حال جابه‌جایی کار",
        ["LOCKSY  ·  QUICK ACTION"] = "LOCKSY · عملیات سریع",
        ["LOCKSY · QUICK ACTION"] = "LOCKSY · عملیات سریع",
        ["LOCKSY · SETTINGS"] = "LOCKSY · تنظیمات",
        ["Choose the app's light or dark appearance."] = "ظاهر روشن یا تیره برنامه را انتخاب کن.",
        ["Choose a language for the app."] = "زبان برنامه را انتخاب کن.",
        ["App information and project source code."] = "اطلاعات برنامه و کد منبع پروژه.",
        ["A light surface with dark text."] = "ظاهر روشن با نوشته‌های تیره.",
        ["A dark surface for low-light use."] = "ظاهر تیره برای محیط‌های کم‌نور.",
        ["Use the default English interface."] = "از رابط پیش‌فرض انگلیسی استفاده کن.",
        ["Use the Persian right-to-left interface."] = "از رابط فارسی با چیدمان راست‌به‌چپ استفاده کن.",
        ["Select a personal image from this device."] = "یک تصویر دلخواه از همین دستگاه انتخاب کن.",
        ["Restore the built-in Locksy background."] = "پس‌زمینه اصلی Locksy را برگردان.",
        ["View the Locksy source code on GitHub."] = "کد منبع Locksy را در گیت‌هاب ببین.",
        ["Open GitHub repository"] = "بازکردن مخزن گیت‌هاب",
        ["Back to settings"] = "بازگشت به تنظیمات",
        ["Back to task options"] = "بازگشت به گزینه‌های کار",
        ["Settings"] = "تنظیمات",
        ["Theme"] = "پوسته",
        ["Manage your app theme, language, custom wallpaper and app information."] = "پوسته، زبان، تصویر پس‌زمینه و اطلاعات برنامه را مدیریت کن.",
        ["Choose the app appearance."] = "ظاهر برنامه را انتخاب کن.",
        ["Light theme"] = "پوسته روشن",
        ["Dark theme"] = "پوسته تیره",
        ["The light theme is currently active."] = "در حال حاضر پوسته روشن فعال است.",
        ["The dark theme is currently active."] = "در حال حاضر پوسته تیره فعال است.",
        ["Manage your app language, custom wallpaper and app information."] = "زبان برنامه، پس‌زمینه دلخواه و اطلاعات برنامه را مدیریت کن.",
        ["Language"] = "زبان برنامه",
        ["Choose your preferred language. English is the default."] = "زبان موردنظرت را انتخاب کن. زبان پیش‌فرض انگلیسی است.",
        ["Background image"] = "تصویر پس‌زمینه",
        ["About"] = "درباره برنامه",
        ["Close"] = "بستن",
        ["English"] = "English",
        ["فارسی"] = "فارسی",
        ["Language updated."] = "زبان برنامه تغییر کرد.",
        ["Language updated. Tap Apply wallpaper to refresh lock screen."] = "زبان برنامه تغییر کرد. برای تازه‌شدن متن قفل صفحه نمایش، روی «اعمال پس‌زمینه» بزن.",
        ["Choose a personal image for your lock-screen wallpaper."] = "می‌توانی تصویر دلخواهت را برای پس‌زمینه قفل صفحه نمایش انتخاب کنی.",
        ["Choose image"] = "انتخاب تصویر",
        ["Remove custom background"] = "حذف تصویر دلخواه",
        ["Cancel"] = "انصراف",
        ["A custom background is currently active."] = "در حال حاضر تصویر دلخواه فعال است.",
        ["The default Locksy background is currently active."] = "در حال حاضر پس‌زمینه پیش‌فرض Locksy فعال است.",
        ["Background image saved. Apply the wallpaper to see it on your lock screen."] = "تصویر پس‌زمینه ذخیره شد. برای دیدنش روی قفل صفحه نمایش، پس‌زمینه را اعمال کن.",
        ["Background image saved. It will be applied when Locksy moves to the background."] = "تصویر ذخیره شد؛ با رفتن Locksy به پس‌زمینه، روی قفل صفحه نمایش اعمال می‌شود.",
        ["Background image saved. Tap Apply wallpaper to see it on your lock screen."] = "تصویر ذخیره شد؛ برای نمایش روی قفل صفحه نمایش، روی «اعمال پس‌زمینه» بزن.",
        ["Custom background removed. The built-in background will be restored when Locksy moves to the background."] = "تصویر دلخواه حذف شد؛ با رفتن Locksy به پس‌زمینه، طرح اصلی برمی‌گردد.",
        ["Custom background removed. Tap Apply wallpaper to update your lock screen."] = "تصویر دلخواه حذف شد؛ برای به‌روزرسانی قفل صفحه نمایش، روی «اعمال پس‌زمینه» بزن.",
        ["Background image removed."] = "تصویر دلخواه حذف شد.",
        ["No image was selected."] = "تصویری انتخاب نشد.",
        ["The selected image could not be saved."] = "ذخیره تصویر انتخاب‌شده ممکن نشد.",
        ["Locksy keeps important tasks visible directly on your Android lock screen. Customize task colors, reminders, theme, layout and background image. Your task list is stored locally on this device."] = "Locksy کمک می‌کند کارهای مهمت را مستقیم روی پس‌زمینه قفل صفحه نمایش اندروید ببینی. رنگ هر کار، یادآورها، پوسته، چیدمان و تصویر پس‌زمینه را شخصی‌سازی کن. فهرست کارها به‌صورت محلی روی همین دستگاه نگهداری می‌شود.",
        ["Remove ‘{task.Title}’ from your list? This can’t be undone."] = "حذف کار از فهرست؟ این کار قابل بازگشت نیست.",
        ["Choose what to do with a task."] = "عملیات موردنظرت را برای این کار انتخاب کن.",
        ["Could not save image"] = "ذخیره تصویر ممکن نشد",
        ["Could not open GitHub"] = "گیت‌هاب باز نشد",
        ["Please check your internet connection and try again."] = "اتصال اینترنت را بررسی کن و دوباره تلاش کن.",
        ["Please select an image smaller than 25 MB."] = "تصویری کوچک‌تر از ۲۵ مگابایت انتخاب کن.",
        ["Version"] = "نسخه",
        ["Locksy could not start."] = "برنامه Locksy اجرا نشد.",
        ["Please close and reopen the app."] = "برنامه را ببند و دوباره باز کن.",
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
        ["Save alarm"] = "ذخیره زنگ",
        ["Reminder disabled."] = "یادآور غیرفعال شد.",
        ["Alarm set for {scheduledAt:dd MMM, HH:mm}."] = "زنگ برای زمان انتخاب‌شده تنظیم شد.",
        ["Choose a palette first, then a color. Your choice stays the same in Light and Dark mode."] = "ابتدا پالت و سپس رنگ را انتخاب کن. انتخابت در پوسته روشن و تیره ثابت می‌ماند.",
        ["Preview layout"] = "پیش‌نمایش چیدمان",
        ["Layout preview"] = "پیش‌نمایش چیدمان",
        ["LOCKSY · TASKS LAYOUT"] = "LOCKSY · چیدمان کارها",
        ["SCHEMATIC PREVIEW"] = "پیش‌نمایش شماتیک",
        ["Schematic only. CLOCK marks the expected clock area; the sample task cards are not added to the final wallpaper."] = "این فقط نمای شماتیک است. CLOCK محدوده تقریبی ساعت را نشان می‌دهد و کارت‌های نمونه به پس‌زمینه نهایی اضافه نمی‌شوند.",
        ["The four sample cards are visual placeholders only. They will not appear on your wallpaper."] = "چهار کارت نمونه فقط برای نمایش چیدمان هستند و در پس‌زمینه واقعی دیده نمی‌شوند.",
        ["Screen-aware preview assumes a tall portrait phone; the final layout adapts to your device."] = "پیش‌نمایش حالت خودکار بر اساس گوشی عمودی و کشیده است؛ چیدمان نهایی با نسبت صفحه دستگاه سازگار می‌شود.",
        ["Back to layout"] = "بازگشت به چیدمان",
        ["Wallpaper placement"] = "محل قرارگیری کارها",
        ["Auto adapts the layout to your screen ratio. Adjust the offset to line it up with your lock-screen clock."] = "چیدمان به نسبت صفحه سازگار می‌شود. جابه‌جایی را تنظیم کن تا فهرست با ساعت قفل صفحه نمایش هماهنگ شود.",
        ["PLACEMENT PROFILE"] = "الگوی چیدمان",
        ["Move content up or down"] = "جابه‌جایی محتوا به بالا یا پایین",
        ["Empty space around clock"] = "فضای خالی اطراف ساعت",
        ["% of screen height"] = "٪ از ارتفاع صفحه",
        ["Clock spacing applies mainly to Split around center clock. Offset moves the wallpaper content and its clear area together."] = "فاصله اطراف ساعت بیشتر در حالت تقسیم اطراف ساعت مرکزی کاربرد دارد. محتوا و فضای خالی را با هم حرکت می‌دهد.",
        ["Save"] = "ذخیره",
        ["Save changes"] = "ذخیره تغییرات",
        ["Got it"] = "متوجه شدم",
        ["Wallpaper placement saved."] = "جایگاه پس‌زمینه ذخیره شد.",
        ["Dark theme selected."] = "پوسته تیره انتخاب شد.",
        ["Light theme selected."] = "پوسته روشن انتخاب شد.",
        ["Auto Apply is enabled; the clean wallpaper will be refreshed in the background."] = "به‌روزرسانی خودکار روشن است؛ پس‌زمینه در حالت پس‌زمینه به‌روز می‌شود.",
        ["Moved ‘{source.Title}’."] = "جای کار تغییر کرد.",
        ["Drop on another task to change its position."] = "برای تغییر جایگاه، کار را روی کار دیگری رها کن.",
        ["Move over another task to place it."] = "برای جابه‌جایی، روی کار دیگری برو.",
        ["Could not select image"] = "انتخاب تصویر ممکن نشد",
        ["No offset"] = "بدون جابه‌جایی",
        ["up"] = "به بالا",
        ["down"] = "به پایین",
        ["Automatic wallpaper updates paused."] = "به‌روزرسانی خودکار پس‌زمینه متوقف شد.",
        ["Preparing lock-screen wallpaper…"] = "در حال آماده‌سازی پس‌زمینه قفل صفحه نمایش…",
        ["Lock screen updated at {DateTime.Now:HH:mm}."] = "پس‌زمینه قفل صفحه نمایش به‌روزرسانی شد.",
        ["Wallpaper update cancelled."] = "به‌روزرسانی پس‌زمینه لغو شد.",
        ["Could not update wallpaper."] = "به‌روزرسانی پس‌زمینه ممکن نشد.",
        ["Wallpaper updated"] = "پس‌زمینه به‌روزرسانی شد",
        ["Your lock-screen wallpaper is ready. Here is the exact image applied to your lock screen."] = "پس‌زمینه قفل صفحه نمایش آماده است. تصویر اعمال‌شده را در پیش‌نمایش می‌بینی.",
        ["Couldn’t update wallpaper"] = "به‌روزرسانی پس‌زمینه انجام نشد",
        ["Wallpaper updated at"] = "زمان به‌روزرسانی پس‌زمینه",
        ["Status"] = "وضعیت",
        ["Default"] = "پیش‌فرض",
        ["No color"] = "بدون رنگ",
        ["Soft Light"] = "روشن ملایم",
        ["Deep Dark"] = "تیره عمیق",
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
        ["Save settings"] = "ذخیره تنظیمات",
        ["Add reminder"] = "افزودن یادآور",
        ["The saved reminder time has passed. Open settings and choose a future date and time before adding this reminder."] = "زمان ذخیره‌شده یادآور گذشته است. پیش از افزودن، تنظیمات را باز کن و تاریخ و زمان آینده‌ای انتخاب کن.",
        ["Notifications are disabled"] = "اعلان‌ها غیرفعال‌اند",
        ["Locksy cannot show the reminder until notification permission is enabled in Android Settings."] = "تا وقتی مجوز اعلان‌ها را در تنظیمات اندروید فعال نکنی، Locksy نمی‌تواند یادآور را نمایش دهد.",
        ["The selected image is empty or could not be read."] = "تصویر انتخاب‌شده خالی است یا خوانده نمی‌شود.",
        ["characters · Scroll to read longer text"] = "نویسه · برای خواندن متن بلندتر پیمایش کن",
        ["Scroll to read longer text"] = "برای خواندن متن بلندتر پیمایش کن",
        ["Start typing a task"] = "نوشتن یک کار را شروع کن",
        ["No choices?"] = "گزینه‌ای نیست؟",
        ["Switch language"] = "تغییر زبان",
        ["Custom background"] = "پس‌زمینه دلخواه",
        ["Clear image"] = "حذف تصویر",
        ["The image could not be decoded."] = "رمزگشایی تصویر ممکن نشد.",
        ["Reminder added."] = "یادآور اضافه شد.",
        ["Alarm saved for {scheduledAt:dd MMM, HH:mm}; Android may deliver it a little late."] = "زنگ ذخیره شد؛ ممکن است اندروید آن را کمی دیرتر اجرا کند.",

        // Complete Persian localization for Settings, Background, Slideshow, and reminder disclosures.
        ["Personal background"] = "پس‌زمینه شخصی",
        ["Use the default Locksy background or choose one photo."] = "پس‌زمینه پیش‌فرض Locksy را انتخاب کن یا تصویر دلخواه خودتو قرار بده.",
        ["Browse the background designs included with Locksy."] = "طرح‌های پس‌زمینه آماده Locksy را ببین.",
        ["Slideshow settings"] = "تنظیمات اسلایدشو",
        ["Slideshow items"] = "تصاویر اسلایدشو",
        ["Choose multiple photos and adjust automatic rotation."] = "چند تصویر انتخاب کن و زمان چرخش خودکار را تنظیم کن.",
        ["Remove custom photo"] = "حذف تصویر دلخواه",
        ["Return to the default Locksy background."] = "بازگشت به پس‌زمینه پیش‌فرض Locksy.",
        ["Manage slideshow items"] = "مدیریت تصاویر اسلایدشو",
        ["Review and remove photos or built-in backgrounds."] = "تصاویر و پس‌زمینه‌های انتخاب شده را بررسی و حذف کن.",
        ["Back to slideshow settings"] = "بازگشت به تنظیمات اسلایدشو",
        ["Choose photos, manage slideshow items and set the rotation interval."] = "تصاویر را انتخاب کن، موارد اسلایدشو را مدیریت کن و فاصله زمانی تغییر تصویر را تنظیم کن.",
        ["Choose the default Locksy background or one photo from your device."] = "پس‌زمینه پیش‌فرض Locksy یا یک تصویر از دستگاهت را انتخاب کن.",
        ["Choose which part of your wallpaper settings to manage."] = "بخشی از تنظیمات پس‌زمینه را که می‌خواهی مدیریت کنی انتخاب کن.",
        ["Choose a built-in Locksy background or add one to your slideshow."] = "یک پس‌زمینه آماده Locksy انتخاب کن یا آن را به اسلایدشو اضافه کن.",
        ["Review and remove photos or built-in backgrounds in the slideshow."] = "تصاویر و پس‌زمینه‌های موجود در اسلایدشو را بررسی و حذف کن.",
        ["Choose how often the background changes."] = "مشخص کن پس‌زمینه هر چند وقت یک‌بار تغییر کند.",
        ["Built-in background"] = "پس‌زمینه آماده",
        ["Reminder alarm & time"] = "زنگ و زمان یادآور",
        ["Tap to expand or collapse this section"] = "برای باز یا بسته‌کردن این بخش لمس کن",
        ["Palette"] = "پالت رنگ",
        ["Color"] = "رنگ",
        ["Off · tap to configure"] = "باز کردن تنظیمات",
        ["On"] = "روشن",
        // End Settings and reminder localization additions.
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

    public static string CurrentLanguage => MauiPreferences.Default.Get(LanguagePreferenceKey, "en");
    public static bool IsPersian => string.Equals(CurrentLanguage, "fa", StringComparison.OrdinalIgnoreCase);

    public static string Translate(string? value)
    {
        if (string.IsNullOrEmpty(value) || !IsPersian) return value ?? string.Empty;
        return Persian.TryGetValue(value, out var translated) ? translated : value;
    }


    /// <summary>
    /// Applies display-only Unicode bidi embeddings to mixed Persian/Latin text.
    /// Older MAUI/Android text paths can ignore isolate controls in Label text, so use
    /// explicit RTL paragraph embedding and LTR embeddings for Latin runs instead.
    /// This changes only the rendered string; stored task titles remain untouched.
    /// </summary>
    public static string PrepareTextForDisplay(string? value)
    {
        if (string.IsNullOrEmpty(value) || !IsPersian) return value ?? string.Empty;

        var hasRightToLeftText = value.Any(c => c >= '\u0600' && c <= '\u06FF');
        var hasAsciiRun = value.Any(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9');
        if (!hasRightToLeftText || !hasAsciiRun) return value;

        // RLE forces the paragraph's visual base direction to RTL, even when its first
        // visible token is English (for example, "Locksy helps..."). Each LRE/PDF pair
        // preserves the internal character order of English words, filenames and URLs.
        var mixed = EmbeddedLeftToRightRun.Replace(value, match => "\u202A" + match.Value + "\u202C");
        return "\u202B" + mixed + "\u202C";
    }

    /// <summary>
    /// One-time upgrade migration: open v1.2.2 in English by default, then retain any
    /// language the user selects from that point onward.
    /// </summary>
    public static void EnsureEnglishDefaultForV122()
    {
        if (MauiPreferences.Default.Get(EnglishDefaultMigrationKey, false))
            return;

        var previousLanguage = MauiPreferences.Default.Get(LanguagePreferenceKey, "en");
        MauiPreferences.Default.Set(LanguagePreferenceKey, "en");
        MauiPreferences.Default.Set(EnglishDefaultMigrationKey, true);

        // If an installed build was using Persian, queue one wallpaper regeneration so
        // the old generated lock-screen bitmap can pick up the corrected English default.
        if (string.Equals(previousLanguage, "fa", StringComparison.OrdinalIgnoreCase)
            && MauiPreferences.Default.Get("locksy-auto-apply", false))
        {
            MauiPreferences.Default.Set("locksy-auto-apply-pending", true);
        }
    }

    public static void SetLanguage(string languageCode)
    {
        var code = string.Equals(languageCode, "fa", StringComparison.OrdinalIgnoreCase) ? "fa" : "en";
        MauiPreferences.Default.Set(LanguagePreferenceKey, code);
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
