using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ULT;

public partial class AboutWindow : Window
{
    [System.Text.RegularExpressions.GeneratedRegex(@"\[(?<lt>.+?)\]\((?<url>https?://\S+?)\)|<b>(?<bt>.*?)</b>", System.Text.RegularExpressions.RegexOptions.Singleline)]
    private static partial System.Text.RegularExpressions.Regex LinkAndBoldRegex();
    private record Dependency(string Name, string License, string Purpose);
    private record ChangelogEntry(string Version, string Date, string[] Changes);

    private readonly Dictionary<string, string> _docs = new()
    {
        ["Загальне"] =
        "<b>ПРИНЦИП РОБОТИ</b>\n" +
        "────────────────────────────────────────\n" +
        "  Програма працює за принципом:\n" +
        "    • <b>Оригінал</b> — текст з відкритого файлу або обраного індексу, якщо це файл з масивами мов (можна змінити через спеціальний імпорт).\n" +
        "    • <b>Переклад</b> — робоча копія, яку ви редагуєте або обраного індексу, якщо це файл з масивами мов.\n\n" +
        "  <b>Способи роботи:</b>\n" +
        "    a. Відкрити оригінальний файл.\n" +
        "        • Імпортувати переклад з раніше збереженого <b>JSON</b> або з <b>CSV/TXT</b>.\n" +
        "        • Зберігати через <b>Зберегти як</b>.\n" +
        "    b. Відкрити файл з перекладом.\n" +
        "        • Імпортувати текст з оригінального файлу в стовпчик <b>Оригінал</b> (це також корисно, коли розробник оновив текст у грі).\n" +
        "        • Зберігати через <b>Файл → Зберегти</b> або комбінацією клавіш <b>Ctrl+S</b>.\n\n" +
        "<b>ДОДАТКОВІ МОЖЛИВОСТІ</b>\n" +
        "────────────────────────────────────────\n" +
        "  • Підтримується <b>Drag&Drop</b> цілих тек та підтек.\n" +
        "<b>ВАЖЛИВО</b>\n" +
        "────────────────────────────────────────\n" +
        "  Резервна копія оригінального файлу <b>не створюється</b> — зберігайте файл під іншим ім’ям або <b>завжди робіть бекапи</b>.\n\n",

        ["Редагування"] =
        "<b>ОСНОВНЕ РЕДАГУВАННЯ</b>\n" +
        "────────────────────────────────────────\n" +
        "  Оберіть один рядок у таблиці — текст з’явиться в полі редагування знизу. Вносьте зміни безпосередньо у це поле, для збереження змін просто виберіть другий рядок з таблиці.\n\n" +
        "  Зміна рядка підсвічується кольором і позначається індикатором <b>*</b> у заголовку програми, назві вкладки та дереві файлів.\n\n" +
        "  Пам’ятайте, що символи нового рядка і табуляції замінюються маркерами під час відкриття файлу:\n" +
        "    <b>\\r\\n</b> тепер <b><cf></b>\n" +
        "    <b>\\r</b>  тепер <b><cr></b>\n" +
        "    <b>\\n</b>  тепер <b><lf></b>\n" +
        "    <b>\\t</b>  тепер <b><tb></b>\n\n" +
        "  Використовуйте саме нові маркери під час перекладу, вони автоматично перетворяться на правильні символи під час збереження.\n\n" +
        "<b>СТАТУСИ РЯДКІВ</b>\n" +
        "────────────────────────────────────────\n" +
        "Статус рядка встановлюється автоматично під час редагування:\n" +
        "    • Якщо текст відрізняється від комірки оригіналу — встановлюється статус <b>На вичитку</b>.\n" +
        "    • Якщо текст повернуто до оригіналу — статус скидається.\n\n" +
        "Статус можна встановити вручну через контекстне меню таблиці або змінити наявний статус натисканням на іконку:\n" +
        "    • <b>На вичитку</b> — текст перекладено, але ще не перевірено (помаранчевий колір).\n" +
        "    • <b>Затверджено</b> — текст перевірено (синій колір).\n\n" +
        "  Окрім статусу, у цьому ж стовпці можуть одночасно з’являтися додаткові позначки: іконка Глосарію — якщо в тексті є збіг зі словником глосарію, та іконка Пам’яті перекладів — якщо для оригіналу вже існує запис у <b>ПП.</b>\n\n" +
        "  Статуси зберігаються у <b>.STATUS</b> файл поруч зі збереженим <b>json</b>, які можна завантажити вручну через <b>Операції → Завантажити статуси рядків</b>.\n" +
        "  Створення нових <b>.STATUS</b> файлів можна вимкнути в налаштуваннях <b>(? → Налаштування)</b>. Однак, якщо файл статусів вже існує поряд з відкритим файлом — він буде автоматично завантажений при відкритті та оновлений при збереженні, навіть якщо створення нових вимкнено.\n\n" +
        "<b>СКАСУВАННЯ/ПОВТОР</b>\n" +
        "────────────────────────────────────────\n" +
        "  Функції пам’ятатимуть редагування комірки <b>(але не зміни в полі редагування)</b>, імпорт, повернення оригіналу для кількох рядків <b>(але не глобальне повернення)</b> та заміну.\n" +
        "Зберігається до <b>100</b> останніх дій. Вкладки ізольовані між собою, тому кожна матиме свою пам’ять.\n\n" +
        "<b>ВИДІЛЕННЯ РЯДКІВ</b>\n" +
        "────────────────────────────────────────\n" +
        "Виділення кількох рядків дозволяє:\n" +
        "    • Повернути їм оригінальний текст (через контекстне меню таблиці).\n" +
        "    • Змінити статус одразу для всіх (через контекстне меню таблиці або натисканням на іконку статусу).\n" +
        "    • Експортувати вибрані у <b>CSV/TXT</b> (через пункт меню <b>Операції → Експортувати</b>).\n\n\n\n",

        ["Експорт та імпорт"] =
        "<b>ВАРІАНТИ ЕКСПОРТУ</b>\n" +
        "────────────────────────────────────────\n" +
        "  <b>CSV</b> — формує файл з трьома екранованими стовпчиками (<b>key, source, Translation</b>) відповідно до стовпчиків таблиці. Два варіанти експорту:\n" +
        "    • Усі рядки.\n" +
        "    • Лише виділені рядки — такий файл потрібно імпортовувати за <b>ID</b> або за оригінальним текстом.\n" +
        "    • Пакетно всі рядки — формуються файли з кожної вкладки.\n\n" +
        "  <b>TXT</b> — простий текстовий формат. Чотири варіанти експорту:\n" +
        "    • Лише текст.\n" +
        "    • Текст з <b>ID</b> — формується у форматі <b>ID=Текст</b>.\n" +
        "    • Лише виділені рядки з ID — такий файл потрібно імпортовувати лише за <b>ID</b>.\n" +
        "    • Пакетно з <b>ID</b> — формуються файли з кожної вкладки.\n\n" +
        "<b>ВАЖЛИВО ПРО ЕКСПОРТ CSV</b>\n" +
        "────────────────────────────────────────\n" +
        "  Стовпчик <b>Translation</b> (третій) буде порожнім для незмінених рядків — експортуються лише комірки, де текст відрізняється від оригіналу. Це зроблено навмисно для певних функцій.\n\n" +
        "<b>ВАРІАНТИ ІМПОРТУ</b>\n" +
        "────────────────────────────────────────\n" +
        "  Стандартний імпорт (у стовпчик <b>Переклад</b>):\n" +
        "    • <b>CSV за порядком рядків</b> — перший рядок файлу відповідає першому рядку в таблиці.\n" +
        "    • <b>CSV за ID</b> — рядки зіставляються по ключу.\n" +
        "    • <b>CSV за оригінальним текстом</b> — зіставлення по стовпчику <b>source</b>.\n" +
        "    • <b>Пакетно CSV за порядком рядків</b> — перші рядки файлів відповідають першим рядкам у таблиці.\n" +
        "    • <b>Пакетно CSV за ID</b> — рядки зіставляються по ключу.\n" +
        "    • <b>Пакетно CSV за оригінальним текстом</b> — зіставлення по стовпчику <b>source</b>.\n" +
        "    • <b>JSON за ID</b> — рядки зіставляються по ключу.\n" +
        "    • <b>Пакетно JSON за ID</b> — рядки зіставляються по ключу.\n\n" +
        "  Імпорт у стовпчик <b>Оригінал</b>:\n" +
        "    <b>УВАГА!</b> Ця дія ЗАМІНЮЄ оригінальний текст у рядках, де знайдено співпадіння за ID.\n" +
        "    • <b>JSON за ID</b> — оновлює текст у стопвці Оригінал з іншого <b>uasset/umap</b> файлу.\n" +
        "    • <b>Пакетно JSON за ID</b> — оновлює текст у стопвці Оригінал у всіх відкритих вкладках з теки <b>JSON</b> файлів.\n" +
        "    • Після такого імпорту <b>історія змін (Undo/Redo) очищується</b>, а <b>фільтр рядків скидається</b>.\n" +
        "    • Якщо рядок мав статус <b>Затверджено</b> — він залишається незмінним. Якщо статусу не було — автоматично встановлюється <b>На вичитку</b>.\n\n" +
        "<b>ПОРАДА</b>\n" +
        "────────────────────────────────────────\n" +
        "  Якщо розробник змінив усі <b>ID</b> після оновлення, потрібно діяти наступним чином:\n" +
        "    1. Відкрити старий оригінальний <b>JSON</b>.\n" +
        "    2. Імпортувати поточний переклад зі збереженого <b>JSON</b> чи <b>CSV</b> за <b>ID</b>.\n" +
        "    3. Експортувати в <b>CSV</b> (таким чином ми матимемо старий оригінальний текст у другому стовпчику і поточний переклад у третьому).\n" +
        "    4. Відкрити новий <b>JSON</b>.\n" +
        "    5. Імпортувати <b>CSV</b> за оригінальним текстом.\n" +
        "  Відповідно потрібно завжди мати бекапи всіх <b>JSON</b>, і старих оригіналів, і старих перекладів, і нових.\n\n",

        ["Пошук, заміна і фільтри"] =
        "<b>ПОШУК</b>\n" +
        "────────────────────────────────────────\n" +
        "  Підтримується пошук двох і більше пробілів, які підсвічуються і відображаються у результатах пошуку як символ <b>•</b>.\n" +
        "Режими пошуку (перемикач <b>Текст/ID</b>):\n" +
        "    • <b>Текст</b> — пошук по оригіналу і перекладу.\n" +
        "    • <b>ID</b> — пошук по ключу рядка.\n\n" +
        "Додаткові опції:\n" +
        "    • <b>Цілі слова</b> — лише точні збіги слова.\n" +
        "    • <b>Регістр</b> — враховувати великі/малі літери.\n" +
        "    • <b>Збіг рядка</b> — рядок має збігатися повністю (пробіли теж враховуються).\n" +
        "  Якщо пошук виконувався з увімкненим фільтром таблиці — результати формуватимуться лише по ньому.\n\n" +
        "<b>ГЛОБАЛЬНИЙ ПОШУК</b>\n" +
        "────────────────────────────────────────\n" +
        "  Пошук у всіх відкритих вкладках одразу. Результати групуються по вкладках, а при виборі конкретного результату відбувається перехід на відповідну вкладку і рядок (знімаючи фільтрацію з таблиці, якщо воно було).\n\n" +
        "<b>ФІЛЬТРИ РЕЗУЛЬТАТІВ</b>\n" +
        "────────────────────────────────────────\n" +
        "  Після пошуку можна відфільтрувати збіги кнопками в бічній панелі:\n" +
        "    • В обох стовпчиках.\n" +
        "    • Лише в оригіналі.\n" +
        "    • Лише у перекладі.\n\n" +
        "<b>ЗАМІНА</b>\n" +
        "────────────────────────────────────────\n" +
        "Підтримує опції:\n" +
        "    • <b>Цілі слова</b> — лише точні збіги слова.\n" +
        "    • <b>Регістр</b> — враховувати великі/малі літери.\n" +
        "    • <b>Збіг рядка</b> — рядок має збігатися повністю (пробіли теж враховуються).\n" +
        "    • <b>Застосувати в усіх вкладках</b> — опція з’являється коли відкрито більше одного файлу, виконує заміну тексту в усіх вкладках.\n" +
        "  Можлива заміна на порожнє значення або пробіл (для випадків, коли потрібно прибрати слово чи фразу). Вкрай зручно для видалення артиклів у <b>uasset</b> файлах за допомогою опції <b>Збіг рядка</b>.\n" +
        "  Заміна може застосовуватись як до всієї таблиці, так і до увімкненого фільтру кожної вкладки (про це буде вказано).\n\n" +
        "<b>ФІЛЬТР ТАБЛИЦІ</b>\n" +
        "────────────────────────────────────────\n" +
        "Режими фільтру:\n" +
        "    • <b>Усі рядки</b> — показати всі рядки.\n" +
        "    • <b>Змінені</b> — рядки, де текст відрізняється від оригіналу.\n" +
        "    • <b>На вичитку</b> — за відповідним статусом.\n" +
        "    • <b>Затверджені</b> — за відповідним статусом.\n" +
        "    • <b>Приховати затверджені</b> — приховує рядки зі статусом <b>Затверджено</b>.\n" +
        "    • <b>Неперекладені</b> — рядки, де комірка перекладу дорівнює комірці оригіналу.\n" +
        "    • <b>Сортувати ID</b> — сортування рядків за <b>ID</b>.\n" +
        "    • <b>Сортувати оригінал</b> — сортування рядків за <b>оригінальним</b> текстом.\n" +
        "    • <b>Сортувати переклад</b> — сортування рядків за <b>перекладеним</b> текстом.\n" +
        "    • <b>Сортувати за кількістю слів оригіналу</b> — сортування рядків за кількістю слів суто за стовпчиком Оригінал.\n" +
        "    • <b>Сортувати за кількістю слів перекладу</b> — сортування рядків за кількістю слів суто за стовпчиком Оригінал.\n" +
        "    • <b>Орфографія</b> — фільтрує рядки, де переклад містить орфографічні помилки.\n" +
        "    • <b>Глосарій</b> — фільтрує рядки, де переклад містить записи з глосарію.\n" +
        "    • <b>Пам’ять перекладів</b> — фільтрує рядки, де переклад містить записи з <b>ПП</b>.\n" +
        "    • <b>ПП (лише неперекладені)</b> — фільтрує рядки, де переклад містить записи з <b>ПП</b> ігноруючи уже змінені рядки.\n" +
        "    • <b>Часті помилки</b> — фільтрує рядки, де переклад містить технічні помилки.\n\n" +

        "<b>ГЛОБАЛЬНЕ ФІЛЬТРУВАННЯ</b>\n" +
        "────────────────────────────────────────\n" +
        "  Вікно «Глобальне фільтрування» (меню <b>Операції → Глобальне фільтрування</b>) дозволяє застосувати будь-який із режимів фільтру/сортування одразу до <b>всіх</b> відкритих вкладок.\n\n",

        ["Regex у пошуку"] =
        "<b>ЯК ПРАЦЮЄ</b>\n" +
        "────────────────────────────────────────\n" +
        "  Щоб увімкнути потрібно натиснути перемикач режиму пошуку до стану <b>Regex</b>. <b>Увага: Підтримується лише у вікні Глобального пошуку</b>.\n" +
        "  У цьому режимі опції <b>Цілі слова</b> і <b>Збіг рядка</b> вимикаються — використовуйте їх аналоги через шаблони (<b>\\b</b> і <b>^...$</b>).\n\n" +
        "<b>ОСНОВНІ СИМВОЛИ</b>\n" +
        "────────────────────────────────────────\n" +
        "    • <b>.</b> — будь-який символ: <b>h.t</b> → <b>hat</b>, <b>hot</b>, <b>hit</b>\n" +
        "    • <b>+</b> — 1 або більше: <b>ho+t</b> → <b>hot</b>, <b>hooot</b>, але не <b>ht</b>\n" +
        "    • <b>*</b> — 0 або більше: <b>ho*t</b> → <b>ht</b>, <b>hot</b>, <b>hooot</b>\n" +
        "    • <b>?</b> — 0 або 1: <b>colou?r</b> → <b>color</b>, <b>colour</b>\n" +
        "    • <b>^</b> — початок рядка: <b>^Привіт</b> — рядки що починаються з <b>Привіт</b>\n" +
        "    • <b>$</b> — кінець рядка: <b>кінець$</b> — рядки що закінчуються на <b>кінець</b>\n" +
        "    • <b>\\b</b> — межа слова: <b>\\bкіт\\b</b> → <b>кіт</b>, але не <b>кіток</b>\n" +
        "    • <b>\\d</b> — цифра: <b>\\d+</b> → <b>123</b>, <b>42</b>\n" +
        "    • <b>\\s</b> — пробільний символ: <b>\\s{2,}</b> → два або більше пробілів\n" +
        "    • <b>\\w</b> — літера, цифра або <b>_</b>\n\n" +
        "<b>КЛАСИ І ГРУПИ СИМВОЛІВ</b>\n" +
        "────────────────────────────────────────\n" +
        "    • <b>[abc]</b> — один із символів: <b>[аеіоу]</b> → будь-яка голосна\n" +
        "    • <b>[^abc]</b> — не один із символів: <b>[^0-9]</b> → не цифра\n" +
        "    • <b>[а-я]</b> — діапазон: рядкові кириличні\n" +
        "    • <b>(кіт|пес)</b> — або: <b>кіт</b> або <b>пес</b>\n" +
        "    • <b>{3}</b> — рівно <b>3</b> рази\n" +
        "    • <b>{2,5}</b> — від <b>2</b> до <b>5</b> разів\n\n" +
        "<b>ЕКРАНУВАННЯ СПЕЦСИМВОЛІВ</b>\n" +
        "────────────────────────────────────────\n" +
        "  Якщо потрібно знайти буквальний спецсимвол — поставте перед ним символ <b>\\</b>:\n" +
        "    • <b>\\{PlayerName\\}</b> — шукає <b>{PlayerName}</b>\n" +
        "    • <b>\\.</b> — шукає крапку, а не будь-який символ\n" +
        "    • <b>\\(текст\\)</b> — шукає <b>(текст)</b>\n\n" +
        "<b>ПРАКТИЧНІ ПРИКЛАДИ</b>\n" +
        "────────────────────────────────────────\n" +
        "    • <b>^\\s*$</b> — порожній або лише пробіли\n" +
        "    • <b><[^>]+></b> — HTML/XML теги: <b><b></b>, <b><color=#fff></b>\n" +
        "    • <b>\\{[^}]+\\}</b> — плейсхолдери: <b>{0}</b>, <b>{PlayerName}</b>\n" +
        "    • <b>\\s{2,}</b> — подвійні та більше пробілів\n" +
        "    • <b>^[А-ЯҐЄІЇ]</b> — рядки що починаються з великої кириличної літери\n" +
        "    • <b>[а-яґєіїА-ЯҐЄІЇ][a-zA-Z]|[a-zA-Z][а-яґєіїА-ЯҐЄІЇ]</b> — змішана розкладка (кирилиця впритул до латиниці)\n\n" +
        "<b>ПОМИЛКИ У ШАБЛОНІ</b>\n" +
        "────────────────────────────────────────\n" +
        "  Якщо шаблон містить синтаксичну помилку — з’явиться повідомлення з описом помилки.\n\n",

        ["Перевірка орфографії"] =
        "<b>ЯК ПРАЦЮЄ</b>\n" +
        "────────────────────────────────────────\n" +
        "  Для роботи орфографії потрібно завантажити українські словники [brown-uk/dict_uk](https://github.com/brown-uk/dict_uk/releases) " +
        "(обирати <b>hunspell-uk_UA_*.zip</b>) і покласти файли <b>uk_UA.dic</b> та <b>uk_UA.aff</b> поруч з <b>exe</b>.\n\n" +

        "<b>Фільтр Орфографія</b>\n" +
        "  Показує лише рядки, де переклад містить слова, не розпізнані словником. Фільтрує виключно кириличні слова — латиниця, числа, теги та плейсхолдери ігноруються.\n" +
        "  При виборі рядка, збоку статистики відображатиметься список помилкових слів через кому.\n\n" +

        "<b>Виключення</b>\n" +
        "  Щоб додати слово до виключень (власні назви, терміни, ігрова лексика) — виділіть його в полі редагування, натисніть ПКМ та оберіть <b>Додати до виключень</b>.\n" +
        "  Виключення зберігаються у файлі <b>SpellCheckRules.json</b> поруч з <b>exe</b>.\n\n",

        ["Часті помилки"] =
        "<b>ЯК ПРАЦЮЄ</b>\n" +
        "────────────────────────────────────────\n" +
        "  Автоматична перевірка перекладу на типові технічні помилки — порівнює переклад з оригіналом і шукає розбіжності, які часто трапляються при перекладі, але не стосуються самої мови.\n\n" +

        "<b>ЯК ПОБАЧИТИ</b>\n" +
        "────────────────────────────────────────\n" +
        "  При виборі рядка з помилкою під полем редагування з’являється панель з іконкою та переліком знайдених помилок.\n\n" +
        "  Фільтр <b>Часті помилки</b> (випадаючий список фільтрів рядків) показує лише рядки, де переклад містить хоча б одну з таких помилок.\n\n" +

        "<b>ЩО ПЕРЕВІРЯЄТЬСЯ</b>\n" +
        "────────────────────────────────────────\n" +
        "    • Відповідність тегів і плейсхолдерів (<b>{0}</b>, <b>%s</b>, <b><color>...</color></b> тощо) — чи не загублено чи змінено.\n" +
        "    • Відповідність чисел з оригіналом.\n" +
        "    • Повторене слово підряд.\n" +
        "    • Подвійний пробіл.\n" +
        "    • Відсутній або зайвий пробіл на початку чи в кінці рядка.\n" +
        "    • Розбіжність кінцевої пунктуації (крапка, знак оклику, знак питання тощо).\n" +
        "    • Не закриті дужки чи лапки (<b>()</b>, <b>[]</b>, <b>«»</b>, <b>“”</b>, <b>„“</b>) — перевіряється, лише якщо в оригіналі вони мають пару.\n" +
        "    • Відсутній пробіл після розділового знаку.\n" +
        "    • Неправильна пара тегів або незакритий тег (наприклад <b><b>...</i></b>).\n" +
        "    • Подвоєний розділовий знак (<b>,,</b> <b>;;</b> <b>::</b>).\n\n" +

        "<b>ВАЖЛИВО</b>\n" +
        "────────────────────────────────────────\n" +
        "  Перевірка порівнює переклад з оригіналом — якщо розбіжність (наприклад, не закриті дужки чи невідповідна пунктуація) уже присутня в самому оригінальному тексті, вона не позначається як помилка перекладача.\n\n" +
        "  Маркери переносу рядків (<b><cf></b>, <b><cr></b>, <b><lf></b>, <b><tb></b>) не враховуються перевіркою тегів — вони не потребують закриваючої пари.\n\n",

        ["Глосарій"] =
        "<b>ЯК ПРАЦЮЄ</b>\n" +
        "────────────────────────────────────────\n" +
        "  Глосарій — це список термінів із затвердженими перекладами. Зберігається у файлі <b>Glossary.json</b> поруч з <b>exe</b>.\n\n" +
        "<b>РЕДАКТОР ГЛОСАРІЮ</b>\n" +
        "────────────────────────────────────────\n" +
        "  Перейти до нього можна через меню <b>Операції → Глосарій</b>. У редакторі можна:\n" +
        "    • Переглянути всі записи та їх варіанти перекладу.\n" +
        "    • Фільтрувати за оригіналом або перекладом через поле пошуку.\n" +
        "    • Додати новий варіант перекладу до наявного запису.\n" +
        "    • Видалити окремий варіант перекладу або весь запис.\n\n" +

        "  Додати термін до глосарію можна двома варіантами:\n" +
        "    • Натиснути <b>ПКМ → Додати до глосарію</b> по зміненому (перекладеному) рядку. Зручно для коротких рядків.\n" +
        "    • Виділити перекладене слово чи фразу у полі редагування і натиснути <b>ПКМ → Додати до глосарію</b>, відкриється діалогове вікно, де ви можете виділити оригінал, якому відповідає переклад. Зручно для фраз чи слів у великому тексті.\n" +
        "  Один термін може мати кілька варіантів перекладу — якщо запис вже існує, новий варіант додається до наявного.\n\n" +

        "<b>ЗВ’ЯЗOК З ПЕРЕВІРКОЮ ОРФОГРАФІЇ</b>\n" +
        "────────────────────────────────────────\n" +
        "  Усі слова з глосарію автоматично додаються до виключень перевірки орфографії — якщо термін затверджено вручну, орфографічна перевірка його не позначатиме як помилку.\n" +
        "  На відміну від звичайних виключень з правил, слова з глосарію <b>не зберігаються</b> у <b>SpellCheckRules.json</b> — вони підтягуються з глосарію при кожному запуску.\n\n",

        ["Пам’ять перекладів"] =
        "<b>ЩО ЦЕ ТАКЕ</b>\n" +
        "────────────────────────────────────────\n" +
        "  <b>Пам’ять перекладів (ПП)</b> — база раніше перекладених фраз, яка підказує готовий переклад для оригінального тексту, що вже зустрічався. Працює незалежно від глосарія: <b>ПП</b> запам’ятовує цілі перекладені рядки, а не окремі терміни.\n" +
        "  Можна створити кілька окремих пам’ятей (наприклад, під різні проєкти чи ігри) і вмикати/вимикати кожну окремо.\n\n" +

        "<b>КЕРУВАННЯ ПАМ’ЯТЯМИ</b>\n" +
        "────────────────────────────────────────\n" +
        "  Розділ доступний через <b>? → Налаштування → Пам’ять перекладів</b>:\n" +
        "    • <b>Використовувати всі одразу</b> — вмикає/вимикає використання всіх пам’ятей одночасно.\n" +
        "    • <b>Максимум варіантів перекладу</b> (повзунок від <b>3</b> до <b>8</b>, за замовчуванням <b>5</b>) — скільки останніх унікальних варіантів перекладу зберігати на кожен оригінальний текст. Змінене значення діє лише для записів, які редагуються <b>після</b> зміни — уже наявні записи не перебудовуються заднім числом.\n" +
        "    • <b>Записати переклади зі всіх вкладок</b> — примусово записує переклади з <b>усіх відкритих вкладок</b> (усі змінені рядки) у поточну вибрану <b>ціль запису</b>. Корисно, якщо <b>ПП</b> була створена чи активована вже після того, як частину тексту переклали.\n" +
        "    • <b>Застосувати переклади до всіх вкладок</b> — застосовує переклади до всіх відкритих вкладок з усіх активованих <b>ПП</b>.\n" +
        "    • <b>Прапорець зліва від кожної пам’яті</b> — вмикає/вимикає її окремо.\n" +
        "    • <b>Перемикач праворуч від пам’ятей</b> — це <b>ціль запису</b>. Лише одна пам’ять може приймати нові переклади.\n" +
        "    • <b>Створити</b> — створює нову пам’ять із заданою назвою (зберігається як окремий <b>.tm.json</b> файл поруч з <b>exe</b>, у теці <b>TranslationMemories</b>).\n" +
        "    • <b>Видалити</b> — видаляє вибрану пам’ять разом з файлом на диску (з підтвердженням).\n\n" +

        "<b>АВТОМАТИЧНИЙ ЗАПИС</b>\n" +
        "────────────────────────────────────────\n" +
        "  Кожен змінений рядок автоматично записується у <b>цільову пам’ять</b> одразу після збереження файлу(-ів).\n" +
        "  На кожен оригінальний текст зберігається до <b>N</b> останніх унікальних варіантів перекладу (<b>N</b> — значення повзунка у вікні <b>Налаштувань</b>) — найновіший завжди першим. Повторний запис того самого перекладу нічого не дублює, а просто піднімає варіант у контекстному меню.\n" +
        "  Порожні, незмінені або <b>беззмістовні</b> переклади не записуються — переклад має містити хоча б одну літеру (будь-якого алфавіту), інакше він вважається сміттям (напр. <b>123</b>, <b>.</b>, <b>!!!</b>) і в пам’ять не потрапляє.\n\n" +

        "<b>ПІДКАЗКИ ПІД ЧАС ПЕРЕКЛАДУ</b>\n" +
        "────────────────────────────────────────\n" +
        "  Рядки, що мають збіг за оригінальним текстом (і при цьому створена і активована хоча б одна <b>ПП</b>), підсвічуються спеціальною іконкою у таблиці.\n" +
        "  Натисніть <b>ПКМ</b> по такому рядку — у контекстному меню з’явиться пункт <b>Пам’ять перекладів</b> зі списком варіантів перекладу (з назвою пам’яті, звідки взято варіант). Вибір варіанта одразу підставляє його в комірку перекладу і записується в <b>Undo/Redo</b>.\n\n" +

        "  <b>УВАГА:</b> запис і підказки працюють незалежно:\n" +
        "    • Підказки використовують <b>всі активовані</b> пам’яті.\n" +
        "    • Запис відбувається лише в <b>одну цільову пам’ять</b>, і тільки якщо вона <b>активована</b>.\n\n",

        ["Статистика"] =
        "<b>ЯК РАХУЄТЬСЯ СТАТИСТИКА</b>\n" +
        "────────────────────────────────────────\n" +
        "  Перед підрахунком текст очищується від:\n" +
        "    • HTML-тегів (наприклад <b><tag></tag></b>)\n" +
        "    • Плейсхолдерів (наприклад <b>{count}</b>, <b>{Playername}</b>)\n" +
        "    • Конструкцій <b>|platform(…), |gender(…), |plural(…)</b>, але залишаються тексти у самих ключах.\n\n" +

        "  <b>Враховуються як слова:</b>\n" +
        "    • Звичайні слова з літер будь-якою мовою\n" +
        "    • Слова з апострофами (наприклад <b>o'clock</b>, <b>з’їв</b> → 1 слово)\n" +
        "    • Слова з дефісами, символами & (наприклад <b>по-справжньому</b>, <b>rock&roll</b> → 1 слово)\n" +
        "    • Слова, оточені зірочками (наприклад <b>*cough*</b> → 1 слово)\n" +
        "    • Злиті слова через зірочки (наприклад <b>*cough**cough*</b> → 2 слова)\n" +
        "    • Слова всередині конструкцій <b>|platform(…), |gender(…), |plural(…)</b>, (наприклад <b>|plural(one=день,few=дні,many=днів,other=дня)</b> → 4 слова)\n\n" +

        "  <b>НЕ враховуються як слова:</b>\n" +
        "    • Чисті числа (наприклад <b>123</b>, <b>51</b>)\n" +
        "    • Слова, що містять символ <b>_</b>, наприклад <b>NO_VALID_VOLUME</b>, <b>hello_world</b>, <b>MSG_ERROR</b>\n" +
        "    • Числові вирази (наприклад <b>441,52%</b>, <b>52+12</b>)\n\n" +

        "  Для перекладених (змінених) рядків рахуються слова <b>оригіналу</b>, а не перекладу — так довжина українського тексту не впливатиме на загальний відсоток.\n" +
        "  Формула відсотків:\n" +
        "    <b>слова оригіналу перекладених рядків / усі слова оригіналу × 100%</b>\n\n" +

        "<b>СТАТУС ЗАТВЕРДЖЕНО</b>\n" +
        "────────────────────────────────────────\n" +
        "  Окремо рахується кількість слів оригіналу у рядках зі статусом <b>Затверджено</b>.\n" +
        "  Враховуються лише перекладені (змінені) рядки.\n" +
        "  Формула відсотків:\n" +
        "    <b>слова оригіналу затверджених рядків / слова оригіналу перекладених рядків × 100%</b>\n\n" +

        "<b>ВАЖЛИВІ НЮАНСИ</b>\n" +
        "────────────────────────────────────────\n" +
        "    • Рядок рахується перекладеним (зміненим) навіть якщо переклад порожній.\n" +
        "    • Слова з символом <b>_</b> повністю ігноруються в статистиці (зазвичай це константи, ключі, змінні рушія).\n\n",

        ["Гарячі клавіші"] =
        "<b>ЗАГАЛЬНІ</b>\n" +
        "────────────────────────────────────────\n" +
        "    • <b>Ctrl+O</b> — Відкрити файл.\n" +
        "    • <b>Ctrl+Shift+O</b> — Відкрити теку (працює рекурсивно).\n" +
        "    • <b>Ctrl+S</b> — Зберегти (перезапишеться відкритий файл).\n" +
        "    • <b>Ctrl+Shift+S</b> — Зберегти все (перезапишуться всі відкриті файли зі змінами).\n" +
        "    • <b>Ctrl+F</b> — Фокус на поле пошуку.\n" +
        "    • <b>Ctrl+Shift+F</b> — Відкрити вікно глобального пошуку.\n" +
        "    • <b>Ctrl+H</b> — Відкрити вікно заміни.\n" +
        "    • <b>Ctrl+G</b> — Відкрити глосарій.\n" +
        "    • <b>Ctrl+Z</b> — Скасувати останню дію.\n" +
        "    • <b>Ctrl+Shift+Z</b> — Повторити останню дію.\n" +
        "    • <b>Ctrl+W</b> — Закрити поточну вкладку.\n" +
        "    • <b>Ctrl+T</b> — Відкрити нову вкладку.\n\n" +
        "<b>У ТАБЛИЦІ</b>\n" +
        "────────────────────────────────────────\n" +
        "    • <b>Ctrl+C</b> — Копіювати вміст вибраної комірки.\n" +
        "    • <b>Ctrl+V</b> — Вставити текст у вибрані рядки.\n\n" +
        "<b>У ПОЛІ ОРИГІНАЛЬНОГО ТЕКСТУ</b>\n" +
        "────────────────────────────────────────\n" +
        "    • <b>Ctrl+C</b> — Копіювати виділений текст.\n\n" +
        "<b>У ПОЛІ РЕДАГУВАННЯ</b>\n" +
        "────────────────────────────────────────\n" +
        "    • <b>Ctrl+Z</b> — Скасувати останню дію.\n" +
        "    • <b>Ctrl+C</b> — Копіювати виділений текст.\n" +
        "    • <b>Ctrl+V</b> — Вставити текст.\n\n",
    };

    private readonly Dictionary<string, string> _licenses = new()
    {
        ["Apache License 2.0 — ULT (Hikaro)"] =
        "Apache License 2.0\n" +
        "Copyright (c) 2026 Hikaro.\n\n" +

        "UELT is licensed under the Apache License, Version 2.0.\n" +
        "You may use, copy, modify, and redistribute this software " +
        "in accordance with the terms of the License.\n\n" +

        "You must retain all copyright, license, and attribution notices " +
        "contained in the original software.\n\n" +

        "The full license text is available at:\n" +
        "https://www.apache.org/licenses/LICENSE-2.0\n\n" +

        "NOTICE\n" +
        "Copyright (c) 2026 Hikaro.\n" +
        "Original author: Hikaro.\n" +

        "THE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTIES OR CONDITIONS " +
        "OF ANY KIND, either express or implied.",

        ["MIT — Newtonsoft.Json (James Newton-King)"] =
            "MIT License\n\n" +
            "Copyright (c) 2007 James Newton-King\n" +
            "https://github.com/JamesNK/Newtonsoft.Json\n\n" +
            "Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the \"Software\"), to deal " +
            "in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell " +
            "copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:\n\n" +
            "The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.\n\n" +
            "THE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, " +
            "FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER " +
            "LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.",

        ["Apache 2.0 — CsvHelper (Josh Close)"] =
            "Apache License\nVersion 2.0, January 2004\n\n" +
            "Copyright (c) 2009-2024 Josh Close\n" +
            "https://github.com/JoshClose/CsvHelper\n\n" +
            "Licensed under the Apache License, Version 2.0 (the \"License\"); you may not use this file except in compliance with the License.\n" +
            "You may obtain a copy of the License at\n\n" +
            "    http://www.apache.org/licenses/LICENSE-2.0\n\n" +
            "Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an \"AS IS\" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.",

        ["MIT — BouncyCastle.Cryptography (bcgit)"] =
            "MIT License (https://opensource.org/licenses/MIT)\n\n" +
            "Copyright (c) 2000-2026 The Legion of the Bouncy Castle Inc. (https://www.bouncycastle.org).\n" +
            "https://github.com/bcgit/bc-csharp\n\n" +
            "Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the \"Software\"), to deal " +
            "in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell " +
            "copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:\n\n" +
            "The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.\n\n" +
            "THE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, " +
            "FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER " +
            "LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.",

        ["MIT — DiscordRichPresence (Lachee)"] =
            "MIT License\n\n" +
            "Copyright (c) 2021 Lachee\n" +
            "https://github.com/Lachee/discord-rpc-csharp\n\n" +
            "Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the \"Software\"), to deal " +
            "in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell " +
            "copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:\n\n" +
            "The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.\n\n" +
            "THE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, " +
            "FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER " +
            "LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.",
        ["LGPL 2.1 — WeCantSpell.Hunspell (aarondandy)"] =
            "The contents of this file are subject to the GNU Lesser General Public License Version 2.1 (the \"License\"); you may not use this file except in compliance with the License. You may obtain a copy of the License at http://www.gnu.org/licenses/lgpl-2.1.html\n\n" +
            "Software distributed under the License is distributed on an \"AS IS\" basis, WITHOUT WARRANTY OF ANY KIND, either express or implied. See the License for the specific language governing rights and limitations under the License.\n" +
            "https://github.com/aarondandy/WeCantSpell.Hunspell\n\n" +
            "The Original Code is Hunspell, based on MySpell.\nThe Initial Developers of the Original Code are Kevin Hendricks (MySpell) and Németh László (Hunspell). Portions created by the Initial Developers are Copyright (C) 2002-2005 the Initial Developers. All Rights Reserved.\n" +
            "Contributor(s): David Einstein, Davide Prina, Giuseppe Modugno, Gianluca Turconi, Simon Brouwer, Noll János, Bíró Árpád, Goldman Eleonóra, Sarlós Tamás, Bencsáth Boldizsár, Halácsy Péter, Dvornik László, Gefferth András, Nagy Viktor, Varga Dániel, Chris Halls, Rene Engelhard, Bram Moolenaar, Dafydd Jones, Harri Pitkänen\n\n" +
            "Alternatively, the contents of this file may be used under the terms of either the GNU General Public License Version 2 or later (the \"GPL\"), or the GNU Lesser General Public License Version 2.1 or later(the \"LGPL\"), in which case the provisions of the GPL or the LGPL are applicable instead of those above.If you wish to allow use of your version of this file only under the terms of either the GPL or the LGPL, and not to allow others to use your version of this file under the terms of the MPL, indicate your decision by deleting the provisions above and replace them with the notice and other provisions required by the GPL or the LGPL.If you do not delete the provisions above, a recipient may use your version of this file under the terms of any one of the MPL, the GPL or the LGPL.",
    };

    public AboutWindow()
    {
        InitializeComponent();

        TxtVersion.Text = $"Версія {AppVersion.Version}";
        TxtAuthor.Text = "Hikaro/Галицький Розбишака";
        TxtFramework.Text = ".NET 10.0, WPF";
        TxtSoftware.Text = "Windows 10-11, Linux (через Proton)";

        CmbLicense.ItemsSource = _licenses.Keys.ToList();
        CmbLicense.SelectedIndex = 0;

        LstChangelog.ItemsSource = new[]
        {
             new ChangelogEntry("1.0.4-7", "25.09.2026",
            [
                "Публічний реліз.",
            ]),
        };

        LstDependencies.ItemsSource = new[]
        {
            new Dependency("BouncyCastle.Cryptography", "MIT", "Криптографічні функції"),
            new Dependency("CsvHelper", "Apache 2.0", "Робота з CSV"),
            new Dependency("DiscordRichPresence", "MIT", "Активність в Discord"),
            new Dependency("Newtonsoft.Json", "MIT", "Серіалізація JSON"),
            new Dependency("WeCantSpell.Hunspell", "LGPL 2.1", "Перевірка орфографії"),
        };
        LstDocSections.ItemsSource = _docs.Keys.ToList();
        LstDocSections.SelectedIndex = 0;
    }

    private void CmbLicense_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbLicense.SelectedItem is string key && _licenses.TryGetValue(key, out var text))
            TxtLicense.Text = text;
    }

    private void LstDocSections_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstDocSections.SelectedItem is string key && _docs.TryGetValue(key, out var text))
        {
            TxtDocContent.Document = BuildDocument(text);
            TxtDocContent.ScrollToHome();
        }
    }

    private static FlowDocument BuildDocument(string text)
    {
        var doc = new FlowDocument();
        var para = new Paragraph { LineHeight = 20 };

        int last = 0;
        foreach (System.Text.RegularExpressions.Match m in LinkAndBoldRegex().Matches(text))
        {
            if (m.Index > last)
                para.Inlines.Add(new Run(text[last..m.Index]));

            if (m.Groups["url"].Success)
            {
                var link = new Hyperlink(new Run(m.Groups["lt"].Value))
                {
                    NavigateUri = new Uri(m.Groups["url"].Value)
                };
                link.RequestNavigate += (s, e) =>
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                para.Inlines.Add(link);
            }
            else if (m.Groups["bt"].Success)
            {
                para.Inlines.Add(new Run(m.Groups["bt"].Value)
                {
                    FontWeight = FontWeights.SemiBold
                });
            }

            last = m.Index + m.Length;
        }

        if (last < text.Length)
            para.Inlines.Add(new Run(text[last..]));

        doc.Blocks.Add(para);
        return doc;
    }

    private void BtnDonateLink_Click(object sender, RoutedEventArgs e)
    {
        const string donateUrl = "https://send.monobank.ua/jar/47CywvjmpG";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(donateUrl) { UseShellExecute = true });
        }
        catch { /* ігноруємо, якщо браузер не вдалося відкрити */ }
    }
}