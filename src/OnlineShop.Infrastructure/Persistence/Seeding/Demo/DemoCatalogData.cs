using OnlineShop.Domain.Catalog;

namespace OnlineShop.Infrastructure.Persistence.Seeding.Demo;

public sealed record DemoOption(string Code, DemoText Value);

public sealed record DemoSpec(
    string Code,
    SpecKind Kind,
    DemoText Name,
    DemoText Group,
    DemoText? Unit = null,
    bool Filterable = false,
    bool Summary = false,
    IReadOnlyList<DemoOption>? Options = null,
    SpecBetter Better = SpecBetter.None);

/// <summary>A product's value for one spec: exactly one of the fields is set, matching the spec's kind.</summary>
public sealed record DemoValue(string Spec, decimal? Number = null, bool? Boolean = null, string? Text = null, string? Option = null);

public sealed record DemoProduct(
    string Sku,
    string Image,
    DemoText Name,
    DemoText Description,
    decimal PriceEur,
    decimal? CompareAtEur,
    int Stock,
    IReadOnlyList<DemoValue> Specs,
    string? Brand = null,
    IReadOnlyList<DemoVariant>? Variants = null);

/// <summary>A demo variant: its SKU, stock and options (spec code → option code), all at the product's price.</summary>
public sealed record DemoVariant(string Sku, int Stock, IReadOnlyList<DemoValue> Options);

public sealed record DemoGroup(DemoText Name, IReadOnlyList<DemoProduct> Products);

public sealed record DemoSubCategory(string Slug, DemoText Name, IReadOnlyList<DemoGroup> Groups);

public sealed record DemoCategory(string Slug, DemoText Name, string Image, IReadOnlyList<DemoSubCategory> SubCategories);

/// <summary>
/// The demo shop's catalog: invented brands (Orvia, Casana, Ranna, Papira) and products, every text in fa, en, hy, de
/// and ar. Images are our own illustrations in the web app (<c>apps/web/public/demo/…</c>).
/// </summary>
public static class DemoCatalogData
{
    private static DemoText T(string fa, string en, string hy, string de, string ar) => new(fa, en, hy, de, ar);

    private static DemoValue N(string spec, decimal value) => new(spec, Number: value);
    private static DemoValue B(string spec, bool value) => new(spec, Boolean: value);
    private static DemoValue S(string spec, string value) => new(spec, Text: value);
    private static DemoValue O(string spec, string option) => new(spec, Option: option);
    private static DemoVariant V(string sku, int stock, string color, string size) => new(sku, stock, [O("color", color), O("size", size)]);

    /// <summary>The demo brands (invented names, the same in every language).</summary>
    public static IReadOnlyList<string> Brands { get; } = ["Orvia", "Casana", "Ranna", "Papira"];

    private static string ProductImage(string name) => $"/demo/products/{name}.svg";

    // Spec groups (headings on the product page).
    private static readonly DemoText General = T("مشخصات کلی", "General", "Ընդհանուր", "Allgemein", "المواصفات العامة");
    private static readonly DemoText Display = T("صفحه‌نمایش", "Display", "Էկրան", "Display", "الشاشة");
    private static readonly DemoText Memory = T("حافظه", "Memory", "Հիշողություն", "Speicher", "الذاكرة");
    private static readonly DemoText Battery = T("باتری", "Battery", "Մարտկոց", "Akku", "البطارية");
    private static readonly DemoText Features = T("امکانات", "Features", "Հնարավորություններ", "Funktionen", "الميزات");
    private static readonly DemoText Physical = T("ابعاد و وزن", "Size and weight", "Չափս և քաշ", "Maße und Gewicht", "الأبعاد والوزن");
    private static readonly DemoText Book = T("مشخصات کتاب", "Book details", "Գրքի տվյալներ", "Buchdetails", "تفاصيل الكتاب");

    // Units.
    private static readonly DemoText Inch = T("اینچ", "in", "դյույմ", "Zoll", "بوصة");
    private static readonly DemoText Gigabyte = T("گیگابایت", "GB", "ԳԲ", "GB", "غيغابايت");
    private static readonly DemoText MilliampHour = T("میلی‌آمپر ساعت", "mAh", "մԱժ", "mAh", "مللي أمبير ساعة");
    private static readonly DemoText Hour = T("ساعت", "h", "ժ", "Std.", "ساعة");
    private static readonly DemoText Gram = T("گرم", "g", "գ", "g", "غرام");
    private static readonly DemoText Watt = T("وات", "W", "Վտ", "W", "واط");
    private static readonly DemoText Litre = T("لیتر", "L", "լ", "l", "لتر");
    private static readonly DemoText Centimetre = T("سانتی‌متر", "cm", "սմ", "cm", "سم");
    private static readonly DemoText Piece = T("عدد", "pcs", "հատ", "Stück", "قطعة");
    private static readonly DemoText Page = T("صفحه", "pages", "էջ", "Seiten", "صفحة");
    private static readonly DemoText Sheet = T("برگ", "sheets", "թերթ", "Blatt", "ورقة");
    private static readonly DemoText Month = T("ماه", "months", "ամիս", "Monate", "شهرًا");

    public static IReadOnlyList<DemoSpec> Specs { get; } =
    [
        new("color", SpecKind.Option, T("رنگ", "Colour", "Գույն", "Farbe", "اللون"), General, Filterable: true, Summary: true, Options:
        [
            new("black", T("مشکی", "Black", "Սև", "Schwarz", "أسود")),
            new("white", T("سفید", "White", "Սպիտակ", "Weiß", "أبيض")),
            new("blue", T("آبی", "Blue", "Կապույտ", "Blau", "أزرق")),
            new("gray", T("خاکستری", "Grey", "Մոխրագույն", "Grau", "رمادي")),
            new("silver", T("نقره‌ای", "Silver", "Արծաթագույն", "Silber", "فضي")),
            new("green", T("سبز", "Green", "Կանաչ", "Grün", "أخضر")),
            new("red", T("قرمز", "Red", "Կարմիր", "Rot", "أحمر")),
            new("beige", T("کرم", "Beige", "Բեժ", "Beige", "بيج")),
            new("multicolor", T("چندرنگ", "Multicolour", "Բազմագույն", "Mehrfarbig", "متعدد الألوان")),
        ]),
        new("size", SpecKind.Option, T("سایز", "Size", "Չափս", "Größe", "المقاس"), General, Filterable: true, Options:
        [
            new("s", T("S", "S", "S", "S", "S")),
            new("m", T("M", "M", "M", "M", "M")),
            new("l", T("L", "L", "L", "L", "L")),
            new("xl", T("XL", "XL", "XL", "XL", "XL")),
        ]),
        new("material", SpecKind.Option, T("جنس", "Material", "Նյութ", "Material", "الخامة"), General, Filterable: true, Options:
        [
            new("stainless_steel", T("استیل ضدزنگ", "Stainless steel", "Չժանգոտվող պողպատ", "Edelstahl", "فولاذ مقاوم للصدأ")),
            new("aluminium", T("آلومینیوم", "Aluminium", "Ալյումին", "Aluminium", "ألومنيوم")),
            new("cotton", T("نخ پنبه", "Cotton", "Բամբակ", "Baumwolle", "قطن")),
            new("mesh", T("پارچهٔ مش", "Mesh fabric", "Ցանցային գործվածք", "Mesh-Gewebe", "قماش شبكي")),
            new("polyester", T("پلی‌استر", "Polyester", "Պոլիէսթեր", "Polyester", "بوليستر")),
            new("plastic", T("پلاستیک", "Plastic", "Պլաստիկ", "Kunststoff", "بلاستيك")),
        ]),
        new("screen_size", SpecKind.Number, T("اندازهٔ صفحه‌نمایش", "Screen size", "Էկրանի չափ", "Bildschirmgröße", "حجم الشاشة"), Display, Inch, Filterable: true, Summary: true),
        new("storage", SpecKind.Number, T("حافظهٔ داخلی", "Storage", "Ներքին հիշողություն", "Speicherplatz", "سعة التخزين"), Memory, Gigabyte, Filterable: true, Summary: true, Better: SpecBetter.Higher),
        new("ram", SpecKind.Number, T("حافظهٔ رم", "RAM", "Օպերատիվ հիշողություն", "Arbeitsspeicher", "ذاكرة الوصول العشوائي"), Memory, Gigabyte, Filterable: true, Better: SpecBetter.Higher),
        new("battery_capacity", SpecKind.Number, T("ظرفیت باتری", "Battery capacity", "Մարտկոցի տարողություն", "Akkukapazität", "سعة البطارية"), Battery, MilliampHour, Better: SpecBetter.Higher),
        new("battery_life", SpecKind.Number, T("عمر باتری", "Battery life", "Մարտկոցի աշխատաժամանակ", "Akkulaufzeit", "عمر البطارية"), Battery, Hour, Summary: true, Better: SpecBetter.Higher),
        new("network_5g", SpecKind.Boolean, T("پشتیبانی از 5G", "5G support", "5G աջակցում", "5G-Unterstützung", "دعم 5G"), Features, Filterable: true),
        new("noise_cancelling", SpecKind.Boolean, T("حذف فعال نویز", "Active noise cancelling", "Ակտիվ աղմկաճնշում", "Aktive Geräuschunterdrückung", "إلغاء الضوضاء النشط"), Features, Filterable: true, Summary: true),
        new("water_resistant", SpecKind.Boolean, T("مقاوم در برابر آب", "Water resistant", "Ջրակայուն", "Wasserfest", "مقاوم للماء"), Features, Filterable: true),
        new("bluetooth", SpecKind.Text, T("نسخهٔ بلوتوث", "Bluetooth version", "Bluetooth-ի տարբերակ", "Bluetooth-Version", "إصدار البلوتوث"), Features),
        new("gps", SpecKind.Boolean, T("جی‌پی‌اس داخلی", "Built-in GPS", "Ներկառուցված GPS", "Integriertes GPS", "GPS مدمج"), Features),
        new("power", SpecKind.Number, T("توان", "Power", "Հզորություն", "Leistung", "القدرة"), General, Watt, Filterable: true, Summary: true, Better: SpecBetter.Higher),
        new("capacity", SpecKind.Number, T("گنجایش", "Capacity", "Տարողություն", "Fassungsvermögen", "السعة"), General, Litre, Filterable: true, Summary: true, Better: SpecBetter.Higher),
        new("pieces", SpecKind.Number, T("تعداد", "Pieces", "Քանակ", "Anzahl", "عدد القطع"), General, Piece, Summary: true),
        new("diameter", SpecKind.Number, T("قطر", "Diameter", "Տրամագիծ", "Durchmesser", "القطر"), Physical, Centimetre, Filterable: true),
        new("induction", SpecKind.Boolean, T("مناسب اجاق القایی", "Induction compatible", "Հարմար է ինդուկցիոն սալօջախին", "Induktionsgeeignet", "مناسب لمواقد الحث"), Features, Filterable: true),
        new("dimmable", SpecKind.Boolean, T("قابلیت تنظیم نور", "Dimmable", "Պայծառության կարգավորում", "Dimmbar", "قابل لتعديل الإضاءة"), Features),
        new("weight", SpecKind.Number, T("وزن", "Weight", "Քաշ", "Gewicht", "الوزن"), Physical, Gram, Better: SpecBetter.Lower),
        new("laptop_fit", SpecKind.Number, T("جای لپ‌تاپ تا", "Fits laptops up to", "Նոութբուքի համար մինչև", "Passend für Laptops bis", "يتسع لحاسوب حتى"), General, Inch),
        new("pages", SpecKind.Number, T("تعداد صفحات", "Pages", "Էջերի քանակ", "Seitenzahl", "عدد الصفحات"), Book, Page, Summary: true),
        new("cover", SpecKind.Option, T("نوع جلد", "Cover", "Կազմ", "Einband", "نوع الغلاف"), Book, Filterable: true, Options:
        [
            new("paperback", T("شومیز", "Paperback", "Փափուկ կազմ", "Taschenbuch", "غلاف ورقي")),
            new("hardcover", T("گالینگور", "Hardcover", "Կոշտ կազմ", "Gebunden", "غلاف مقوّى")),
        ]),
        new("sheets", SpecKind.Number, T("تعداد برگ", "Sheets", "Թերթերի քանակ", "Blattzahl", "عدد الأوراق"), General, Sheet, Summary: true),
        new("paper_size", SpecKind.Text, T("قطع", "Format", "Ձևաչափ", "Format", "المقاس"), General),
        new("warranty", SpecKind.Number, T("گارانتی", "Warranty", "Երաշխիք", "Garantie", "الضمان"), General, Month, Summary: true, Better: SpecBetter.Higher),
    ];

    public static IReadOnlyList<DemoCategory> Categories { get; } =
    [
        new("electronics", T("کالای دیجیتال", "Electronics", "Էլեկտրոնիկա", "Elektronik", "الإلكترونيات"), "/demo/categories/electronics.svg",
        [
            new("mobile-phones", T("موبایل", "Mobile phones", "Բջջային հեռախոսներ", "Handys", "الهواتف المحمولة"),
            [
                new(T("گوشی هوشمند", "Smartphones", "Սմարթֆոններ", "Smartphones", "الهواتف الذكية"),
                [
                    new("DEMO-PHN-001", ProductImage("phone-x1"),
                        T("گوشی هوشمند اورویا X1 ظرفیت ۱۲۸ گیگابایت", "Orvia X1 Smartphone, 128 GB", "Orvia X1 սմարթֆոն, 128 ԳԲ", "Orvia X1 Smartphone, 128 GB", "هاتف Orvia X1 الذكي بسعة 128 غيغابايت"),
                        T("صفحهٔ ۶٫۵ اینچی روشن، باتری ۵۰۰۰ میلی‌آمپری برای یک روز کامل و دوربین دوگانه برای عکس‌های روزمره.",
                          "A bright 6.5-inch display, a 5000 mAh battery that lasts all day and a dual camera for everyday photos.",
                          "Պայծառ 6,5 դյույմանոց էկրան, ամբողջ օրվա համար 5000 մԱժ մարտկոց և կրկնակի տեսախցիկ ամենօրյա լուսանկարների համար։",
                          "Helles 6,5-Zoll-Display, ein 5000-mAh-Akku für den ganzen Tag und eine Dualkamera für Alltagsfotos.",
                          "شاشة ساطعة بحجم 6.5 بوصة وبطارية 5000 مللي أمبير تدوم طوال اليوم وكاميرا مزدوجة للصور اليومية."),
                        349m, 399m, 24,
                        [O("color", "blue"), N("screen_size", 6.5m), N("storage", 128), N("ram", 8), N("battery_capacity", 5000), B("network_5g", true), N("weight", 189), N("warranty", 18)], Brand: "Orvia"),
                    new("DEMO-PHN-002", ProductImage("phone-x1-pro"),
                        T("گوشی هوشمند اورویا X1 پرو ظرفیت ۲۵۶ گیگابایت", "Orvia X1 Pro Smartphone, 256 GB", "Orvia X1 Pro սմարթֆոն, 256 ԳԲ", "Orvia X1 Pro Smartphone, 256 GB", "هاتف Orvia X1 Pro الذكي بسعة 256 غيغابايت"),
                        T("نسخهٔ حرفه‌ای با صفحهٔ ۶٫۷ اینچی، ۱۲ گیگابایت رم و دوربین سه‌گانه با زوم نوری.",
                          "The Pro edition with a 6.7-inch display, 12 GB of RAM and a triple camera with optical zoom.",
                          "Pro տարբերակ՝ 6,7 դյույմանոց էկրանով, 12 ԳԲ օպերատիվ հիշողությամբ և օպտիկական խոշորացմամբ եռակի տեսախցիկով։",
                          "Die Pro-Version mit 6,7-Zoll-Display, 12 GB RAM und Dreifachkamera mit optischem Zoom.",
                          "الإصدار الاحترافي بشاشة 6.7 بوصة وذاكرة 12 غيغابايت وكاميرا ثلاثية مع تكبير بصري."),
                        529m, null, 12,
                        [O("color", "black"), N("screen_size", 6.7m), N("storage", 256), N("ram", 12), N("battery_capacity", 5200), B("network_5g", true), N("weight", 205), N("warranty", 18)], Brand: "Orvia"),
                    new("DEMO-PHN-003", ProductImage("phone-a5"),
                        T("گوشی هوشمند اورویا A5 ظرفیت ۶۴ گیگابایت", "Orvia A5 Smartphone, 64 GB", "Orvia A5 սմարթֆոն, 64 ԳԲ", "Orvia A5 Smartphone, 64 GB", "هاتف Orvia A5 الذكي بسعة 64 غيغابايت"),
                        T("گوشی سبک و مقرون‌به‌صرفه برای تماس، پیام‌رسان‌ها و شبکه‌های اجتماعی.",
                          "A light, affordable phone for calls, messaging and social media.",
                          "Թեթև և մատչելի հեռախոս զանգերի, հաղորդագրությունների և սոցիալական ցանցերի համար։",
                          "Ein leichtes, günstiges Handy für Anrufe, Messenger und soziale Netzwerke.",
                          "هاتف خفيف وبسعر مناسب للمكالمات والمراسلة ووسائل التواصل الاجتماعي."),
                        159m, 189m, 40,
                        [O("color", "green"), N("screen_size", 6.1m), N("storage", 64), N("ram", 4), N("battery_capacity", 4500), B("network_5g", false), N("weight", 172), N("warranty", 12)], Brand: "Orvia"),
                ]),
            ]),
            new("laptops", T("لپ‌تاپ", "Laptops", "Նոութբուքեր", "Laptops", "أجهزة اللابتوب"),
            [
                new(T("لپ‌تاپ", "Laptops", "Նոութբուքեր", "Laptops", "أجهزة اللابتوب"),
                [
                    new("DEMO-LAP-001", ProductImage("laptop-14"),
                        T("لپ‌تاپ ۱۴ اینچی اورویا بوک ۱۴", "Orvia Book 14 Laptop, 14-inch", "Orvia Book 14 նոութբուք, 14 դյույմ", "Orvia Book 14 Laptop, 14 Zoll", "حاسوب محمول Orvia Book 14 بشاشة 14 بوصة"),
                        T("بدنهٔ آلومینیومی سبک، صفحهٔ ۱۴ اینچی و تا ۱۲ ساعت کار با یک بار شارژ؛ مناسب دانشجو و کار روزانه.",
                          "A light aluminium body, a 14-inch display and up to 12 hours on a charge — made for students and daily work.",
                          "Թեթև ալյումինե իրան, 14 դյույմանոց էկրան և մինչև 12 ժամ աշխատանք մեկ լիցքավորմամբ՝ ուսանողների և ամենօրյա աշխատանքի համար։",
                          "Leichtes Aluminiumgehäuse, 14-Zoll-Display und bis zu 12 Stunden Akkulaufzeit – ideal für Studium und Alltag.",
                          "هيكل خفيف من الألومنيوم وشاشة 14 بوصة وحتى 12 ساعة بشحنة واحدة، مناسب للطلاب والعمل اليومي."),
                        749m, 849m, 9,
                        [O("color", "silver"), O("material", "aluminium"), N("screen_size", 14), N("storage", 512), N("ram", 16), N("battery_life", 12), N("weight", 1350), N("warranty", 24)], Brand: "Orvia"),
                    new("DEMO-LAP-002", ProductImage("laptop-16"),
                        T("لپ‌تاپ ۱۶ اینچی اورویا بوک ۱۶ پرو", "Orvia Book 16 Pro Laptop, 16-inch", "Orvia Book 16 Pro նոութբուք, 16 դյույմ", "Orvia Book 16 Pro Laptop, 16 Zoll", "حاسوب محمول Orvia Book 16 Pro بشاشة 16 بوصة"),
                        T("پردازندهٔ قدرتمند، ۳۲ گیگابایت رم و صفحهٔ ۱۶ اینچی دقیق برای طراحی، برنامه‌نویسی و تدوین.",
                          "A powerful processor, 32 GB of RAM and a sharp 16-inch display for design, coding and video editing.",
                          "Հզոր պրոցեսոր, 32 ԳԲ օպերատիվ հիշողություն և 16 դյույմանոց հստակ էկրան դիզայնի, ծրագրավորման և տեսամոնտաժի համար։",
                          "Starker Prozessor, 32 GB RAM und ein scharfes 16-Zoll-Display für Design, Programmierung und Videoschnitt.",
                          "معالج قوي وذاكرة 32 غيغابايت وشاشة دقيقة بحجم 16 بوصة للتصميم والبرمجة وتحرير الفيديو."),
                        1249m, null, 5,
                        [O("color", "gray"), O("material", "aluminium"), N("screen_size", 16), N("storage", 1024), N("ram", 32), N("battery_life", 10), N("weight", 1890), N("warranty", 24)], Brand: "Orvia"),
                ]),
            ]),
            new("audio", T("صوتی", "Audio", "Աուդիո", "Audio", "الصوتيات"),
            [
                new(T("هدفون و اسپیکر", "Headphones & speakers", "Ականջակալներ և բարձրախոսներ", "Kopfhörer & Lautsprecher", "السماعات ومكبرات الصوت"),
                [
                    new("DEMO-AUD-001", ProductImage("headphones"),
                        T("هدفون بی‌سیم اورویا هاش با حذف نویز", "Orvia Hush Wireless Noise-Cancelling Headphones", "Orvia Hush անլար ականջակալներ՝ աղմկաճնշմամբ", "Orvia Hush kabelloser Kopfhörer mit Geräuschunterdrückung", "سماعات Orvia Hush اللاسلكية بخاصية إلغاء الضوضاء"),
                        T("حذف فعال نویز برای سفر و محیط‌های شلوغ، ۳۰ ساعت پخش و شارژ سریع.",
                          "Active noise cancelling for travel and busy places, 30 hours of playback and fast charging.",
                          "Ակտիվ աղմկաճնշում ճամփորդությունների և աղմկոտ վայրերի համար, 30 ժամ նվագարկում և արագ լիցքավորում։",
                          "Aktive Geräuschunterdrückung für Reisen und laute Orte, 30 Stunden Wiedergabe und Schnellladen.",
                          "إلغاء نشط للضوضاء للسفر والأماكن المزدحمة، و30 ساعة تشغيل وشحن سريع."),
                        129m, 179m, 30,
                        [O("color", "black"), N("battery_life", 30), B("noise_cancelling", true), S("bluetooth", "5.3"), N("weight", 250), N("warranty", 12)], Brand: "Orvia"),
                    new("DEMO-AUD-002", ProductImage("earbuds"),
                        T("هندزفری بی‌سیم اورویا بادز", "Orvia Buds Wireless Earbuds", "Orvia Buds անլար ականջակալներ", "Orvia Buds kabellose In-Ear-Kopfhörer", "سماعات أذن لاسلكية Orvia Buds"),
                        T("هندزفری کوچک و سبک با کیس شارژ، مقاوم در برابر عرق برای ورزش.",
                          "Small, light earbuds with a charging case, sweat resistant for workouts.",
                          "Փոքր և թեթև ականջակալներ լիցքավորման պատյանով, քրտինքին դիմացկուն՝ մարզումների համար։",
                          "Kleine, leichte In-Ear-Kopfhörer mit Ladecase, schweißresistent fürs Training.",
                          "سماعات أذن صغيرة وخفيفة مع علبة شحن، مقاومة للعرق أثناء التمارين."),
                        59m, 79m, 50,
                        [O("color", "white"), N("battery_life", 24), B("noise_cancelling", false), B("water_resistant", true), S("bluetooth", "5.3"), N("weight", 48), N("warranty", 12)], Brand: "Orvia"),
                    new("DEMO-AUD-003", ProductImage("speaker"),
                        T("اسپیکر بلوتوثی قابل حمل اورویا ویو", "Orvia Wave Portable Bluetooth Speaker", "Orvia Wave դյուրակիր Bluetooth բարձրախոս", "Orvia Wave tragbarer Bluetooth-Lautsprecher", "مكبر صوت بلوتوث محمول Orvia Wave"),
                        T("اسپیکر بلوتوثی ۲۰ واتی مقاوم در برابر آب، همراه مناسب برای سفر و پیک‌نیک.",
                          "A 20 W water-resistant Bluetooth speaker, a good companion for trips and picnics.",
                          "20 Վտ հզորությամբ ջրակայուն Bluetooth բարձրախոս՝ լավ ուղեկից ճամփորդությունների և պիկնիկների համար։",
                          "Wasserfester 20-W-Bluetooth-Lautsprecher, der ideale Begleiter für Ausflüge und Picknicks.",
                          "مكبر صوت بلوتوث بقدرة 20 واط مقاوم للماء، رفيق مثالي للرحلات والنزهات."),
                        69m, null, 22,
                        [O("color", "red"), N("power", 20), N("battery_life", 12), B("water_resistant", true), S("bluetooth", "5.1"), N("weight", 540), N("warranty", 12)], Brand: "Orvia"),
                ]),
            ]),
            new("wearables", T("ساعت و مچ‌بند هوشمند", "Wearables", "Կրելի սարքեր", "Wearables", "الأجهزة القابلة للارتداء"),
            [
                new(T("ساعت هوشمند", "Smartwatches", "Խելացի ժամացույցներ", "Smartwatches", "الساعات الذكية"),
                [
                    new("DEMO-WRB-001", ProductImage("smartwatch"),
                        T("ساعت هوشمند اورویا پالس ۳", "Orvia Pulse 3 Smartwatch", "Orvia Pulse 3 խելացի ժամացույց", "Orvia Pulse 3 Smartwatch", "ساعة Orvia Pulse 3 الذكية"),
                        T("پایش ضربان قلب و خواب، جی‌پی‌اس داخلی برای دویدن و تا یک هفته شارژدهی.",
                          "Heart-rate and sleep tracking, built-in GPS for runs and up to a week on a charge.",
                          "Սրտի զարկերի և քնի հետևում, ներկառուցված GPS վազքի համար և մինչև մեկ շաբաթ աշխատանք մեկ լիցքավորմամբ։",
                          "Herzfrequenz- und Schlaftracking, integriertes GPS fürs Laufen und bis zu eine Woche Akkulaufzeit.",
                          "تتبع نبض القلب والنوم، وGPS مدمج للجري، وحتى أسبوع من الاستخدام بشحنة واحدة."),
                        119m, 149m, 18,
                        [O("color", "black"), N("screen_size", 1.4m), N("battery_life", 168), B("gps", true), B("water_resistant", true), N("weight", 38), N("warranty", 12)], Brand: "Orvia"),
                ]),
            ]),
        ]),
        new("home-kitchen", T("خانه و آشپزخانه", "Home & Kitchen", "Տուն և խոհանոց", "Haus & Küche", "المنزل والمطبخ"), "/demo/categories/home-kitchen.svg",
        [
            new("kitchen-appliances", T("لوازم برقی آشپزخانه", "Kitchen appliances", "Խոհանոցային տեխնիկա", "Küchengeräte", "أجهزة المطبخ"),
            [
                new(T("لوازم برقی کوچک", "Small appliances", "Փոքր կենցաղային տեխնիկա", "Kleingeräte", "الأجهزة الصغيرة"),
                [
                    new("DEMO-HOM-001", ProductImage("kettle"),
                        T("کتری برقی استیل کاسانا ۱٫۷ لیتری", "Casana Stainless-Steel Electric Kettle, 1.7 L", "Casana չժանգոտվող պողպատե էլեկտրական թեյնիկ, 1,7 լ", "Casana Edelstahl-Wasserkocher, 1,7 l", "غلاية كهربائية Casana من الفولاذ المقاوم للصدأ، 1.7 لتر"),
                        T("کتری استیل ۱٫۷ لیتری با جوش سریع، خاموشی خودکار و پایهٔ چرخان ۳۶۰ درجه.",
                          "A 1.7 L stainless-steel kettle with fast boiling, automatic shut-off and a 360° swivel base.",
                          "1,7 լ տարողությամբ չժանգոտվող պողպատե թեյնիկ՝ արագ եռացմամբ, ավտոմատ անջատմամբ և 360° պտտվող հիմքով։",
                          "1,7-Liter-Wasserkocher aus Edelstahl mit Schnellkochfunktion, Abschaltautomatik und 360°-Sockel.",
                          "غلاية من الفولاذ المقاوم للصدأ بسعة 1.7 لتر، غليان سريع وإيقاف تلقائي وقاعدة دوّارة 360 درجة."),
                        39m, 49m, 35,
                        [O("color", "silver"), O("material", "stainless_steel"), N("power", 2200), N("capacity", 1.7m), N("warranty", 12)], Brand: "Casana"),
                    new("DEMO-HOM-002", ProductImage("iron"),
                        T("اتو بخار کاسانا ۲۴۰۰ وات", "Casana Steam Iron, 2400 W", "Casana գոլորշու արդուկ, 2400 Վտ", "Casana Dampfbügeleisen, 2400 W", "مكواة بخار Casana بقدرة 2400 واط"),
                        T("اتو بخار ۲۴۰۰ واتی با کفهٔ سرامیکی و بخار قوی برای چروک‌های سخت.",
                          "A 2400 W steam iron with a ceramic soleplate and a strong steam shot for stubborn creases.",
                          "2400 Վտ գոլորշու արդուկ՝ կերամիկական ներբանով և ուժեղ գոլորշով համառ ծալքերի համար։",
                          "2400-W-Dampfbügeleisen mit Keramiksohle und kräftigem Dampfstoß für hartnäckige Falten.",
                          "مكواة بخار بقدرة 2400 واط بقاعدة سيراميك ودفعة بخار قوية للتجاعيد الصعبة."),
                        45m, null, 20,
                        [O("color", "blue"), N("power", 2400), N("capacity", 0.3m), N("warranty", 12)], Brand: "Casana"),
                    new("DEMO-HOM-003", ProductImage("coffee-maker"),
                        T("قهوه‌ساز قطره‌ای کاسانا ۱٫۲ لیتری", "Casana Drip Coffee Maker, 1.2 L", "Casana կաթիլային սրճեփ, 1,2 լ", "Casana Filterkaffeemaschine, 1,2 l", "صانعة قهوة بالتقطير Casana، 1.2 لتر"),
                        T("قهوه‌ساز قطره‌ای برای ۱۰ فنجان با گرم‌نگه‌دار و فیلتر دائمی قابل شست‌وشو.",
                          "A drip coffee maker for 10 cups with a keep-warm plate and a washable permanent filter.",
                          "Կաթիլային սրճեփ 10 բաժակի համար՝ տաք պահելու հարթակով և լվացվող մշտական զտիչով։",
                          "Filterkaffeemaschine für 10 Tassen mit Warmhalteplatte und waschbarem Dauerfilter.",
                          "صانعة قهوة بالتقطير تكفي 10 أكواب مع لوح للحفاظ على الحرارة وفلتر دائم قابل للغسل."),
                        55m, 69m, 14,
                        [O("color", "black"), N("power", 900), N("capacity", 1.2m), N("warranty", 12)], Brand: "Casana"),
                ]),
            ]),
            new("cookware", T("ظروف پخت‌وپز", "Cookware", "Խոհանոցային սպասք", "Kochgeschirr", "أواني الطهي"),
            [
                new(T("قابلمه و تابه", "Pots & pans", "Կաթսաներ և տապակներ", "Töpfe & Pfannen", "القدور والمقالي"),
                [
                    new("DEMO-HOM-004", ProductImage("cookware-set"),
                        T("سرویس قابلمهٔ ۸ پارچهٔ استیل کاسانا", "Casana 8-Piece Stainless-Steel Cookware Set", "Casana 8 իրից բաղկացած պողպատե կաթսաների հավաքածու", "Casana 8-teiliges Edelstahl-Topfset", "طقم أواني طهي Casana من 8 قطع من الفولاذ"),
                        T("سرویس ۸ پارچهٔ استیل با درهای شیشه‌ای، مناسب همهٔ اجاق‌ها از جمله القایی.",
                          "An 8-piece stainless-steel set with glass lids, suitable for every hob including induction.",
                          "8 իրից բաղկացած չժանգոտվող պողպատե հավաքածու՝ ապակե կափարիչներով, հարմար բոլոր սալօջախներին, այդ թվում ինդուկցիոն։",
                          "8-teiliges Edelstahl-Topfset mit Glasdeckeln, für alle Herdarten inklusive Induktion.",
                          "طقم من 8 قطع من الفولاذ المقاوم للصدأ مع أغطية زجاجية، مناسب لجميع المواقد بما فيها مواقد الحث."),
                        129m, 169m, 8,
                        [O("color", "silver"), O("material", "stainless_steel"), N("pieces", 8), B("induction", true), N("warranty", 24)], Brand: "Casana"),
                    new("DEMO-HOM-005", ProductImage("frying-pan"),
                        T("تابهٔ نچسب کاسانا ۲۸ سانتی‌متری", "Casana Non-Stick Frying Pan, 28 cm", "Casana չկպչող տապակ, 28 սմ", "Casana Antihaft-Bratpfanne, 28 cm", "مقلاة غير لاصقة Casana بقطر 28 سم"),
                        T("تابهٔ نچسب ۲۸ سانتی‌متری با دستهٔ خنک؛ پخت با روغن کمتر و شست‌وشوی آسان.",
                          "A 28 cm non-stick frying pan with a stay-cool handle — cook with less oil and clean up easily.",
                          "28 սմ չկպչող տապակ՝ չտաքացող բռնակով. եփեք քիչ յուղով և հեշտությամբ լվացեք։",
                          "28-cm-Bratpfanne mit Antihaftbeschichtung und kühlem Griff – weniger Öl, leichte Reinigung.",
                          "مقلاة غير لاصقة بقطر 28 سم بمقبض لا يسخن، للطهي بزيت أقل وتنظيف سهل."),
                        29m, null, 45,
                        [O("color", "gray"), O("material", "aluminium"), N("diameter", 28), B("induction", true), N("warranty", 12)], Brand: "Casana"),
                ]),
            ]),
            new("lighting", T("روشنایی", "Lighting", "Լուսավորություն", "Beleuchtung", "الإضاءة"),
            [
                new(T("چراغ", "Lamps", "Լամպեր", "Lampen", "المصابيح"),
                [
                    new("DEMO-HOM-006", ProductImage("desk-lamp"),
                        T("چراغ مطالعهٔ ال‌ای‌دی کاسانا", "Casana LED Desk Lamp", "Casana LED սեղանի լամպ", "Casana LED-Schreibtischlampe", "مصباح مكتب LED من Casana"),
                        T("چراغ مطالعهٔ ال‌ای‌دی با سه رنگ نور و پنج سطح روشنایی، بدون سوسو برای مطالعهٔ طولانی.",
                          "An LED desk lamp with three light colours and five brightness levels, flicker-free for long reading.",
                          "LED սեղանի լամպ՝ լույսի երեք գույնով և պայծառության հինգ մակարդակով, առանց թարթման՝ երկար ընթերցանության համար։",
                          "LED-Schreibtischlampe mit drei Lichtfarben und fünf Helligkeitsstufen, flimmerfrei für langes Lesen.",
                          "مصباح مكتب LED بثلاثة ألوان للضوء وخمس درجات سطوع، دون وميض للقراءة الطويلة."),
                        34m, 42m, 27,
                        [O("color", "white"), O("material", "plastic"), N("power", 8), B("dimmable", true), N("warranty", 12)], Brand: "Casana"),
                ]),
            ]),
        ]),
        new("fashion", T("مد و پوشاک", "Fashion", "Նորաձևություն", "Mode", "الأزياء"), "/demo/categories/fashion.svg",
        [
            new("shoes", T("کفش", "Shoes", "Կոշիկներ", "Schuhe", "الأحذية"),
            [
                new(T("کفش ورزشی", "Sneakers", "Սպորտային կոշիկներ", "Sneaker", "الأحذية الرياضية"),
                [
                    new("DEMO-FSH-001", ProductImage("walking-shoes"),
                        T("کفش پیاده‌روی مردانهٔ رنا", "Ranna Men's Walking Shoes", "Ranna տղամարդու քայլքի կոշիկներ", "Ranna Herren-Walkingschuhe", "حذاء المشي الرجالي Ranna"),
                        T("رویهٔ مش تنفس‌پذیر و زیرهٔ نرم برای پیاده‌روی‌های طولانی شهری.",
                          "A breathable mesh upper and a soft sole for long walks around town.",
                          "Շնչող ցանցային վերին մաս և փափուկ ներբան քաղաքում երկար զբոսանքների համար։",
                          "Atmungsaktives Mesh-Obermaterial und weiche Sohle für lange Spaziergänge in der Stadt.",
                          "جزء علوي شبكي يسمح بالتهوية ونعل ناعم للمشي الطويل في المدينة."),
                        65m, 89m, 33,
                        [O("color", "gray"), O("material", "mesh"), N("weight", 280)], Brand: "Ranna"),
                ]),
            ]),
            new("bags", T("کیف و کوله", "Bags", "Պայուսակներ", "Taschen", "الحقائب"),
            [
                new(T("کوله‌پشتی", "Backpacks", "Ուսապարկեր", "Rucksäcke", "حقائب الظهر"),
                [
                    new("DEMO-FSH-002", ProductImage("backpack"),
                        T("کوله‌پشتی لپ‌تاپ رنا ۲۵ لیتری", "Ranna Laptop Backpack, 25 L", "Ranna նոութբուքի ուսապարկ, 25 լ", "Ranna Laptop-Rucksack, 25 l", "حقيبة ظهر للحاسوب Ranna بسعة 25 لترًا"),
                        T("کولهٔ ۲۵ لیتری ضدآب با جای لپ‌تاپ تا ۱۵٫۶ اینچ و درگاه USB برای شارژ در مسیر.",
                          "A 25 L water-resistant backpack with a sleeve for laptops up to 15.6 inches and a USB port for charging on the go.",
                          "25 լ ջրակայուն ուսապարկ՝ մինչև 15,6 դյույմ նոութբուքի բաժանմունքով և ճանապարհին լիցքավորելու USB միակցիչով։",
                          "Wasserfester 25-Liter-Rucksack mit Laptopfach bis 15,6 Zoll und USB-Anschluss zum Laden unterwegs.",
                          "حقيبة ظهر مقاومة للماء بسعة 25 لترًا مع جيب لحاسوب حتى 15.6 بوصة ومنفذ USB للشحن أثناء التنقل."),
                        49m, null, 21,
                        [O("color", "black"), O("material", "polyester"), N("capacity", 25), N("laptop_fit", 15.6m), B("water_resistant", true), N("weight", 720)], Brand: "Ranna"),
                ]),
            ]),
            new("clothing", T("لباس", "Clothing", "Հագուստ", "Bekleidung", "الملابس"),
            [
                new(T("هودی و سویشرت", "Hoodies & sweatshirts", "Հուդիներ և սվիտշոթներ", "Hoodies & Sweatshirts", "الهوديز والسويتشيرت"),
                [
                    new("DEMO-FSH-003", ProductImage("hoodie"),
                        T("هودی نخی رنا مناسب آقایان و بانوان", "Ranna Unisex Cotton Hoodie", "Ranna ունիսեքս բամբակյա հուդի", "Ranna Unisex-Baumwoll-Hoodie", "هودي قطني للجنسين من Ranna"),
                        T("هودی نخی نرم با برش آزاد، مناسب آقایان و بانوان برای روزهای خنک.",
                          "A soft cotton hoodie with a relaxed fit, for everyone, for cool days.",
                          "Փափուկ բամբակյա հուդի ազատ ձևվածքով՝ բոլորի համար, զով օրերին։",
                          "Weicher Baumwoll-Hoodie mit lockerer Passform, für alle und für kühle Tage.",
                          "هودي قطني ناعم بقصة واسعة يناسب الجميع في الأيام الباردة."),
                        39m, 52m, 60,
                        [O("material", "cotton")], Brand: "Ranna",
                        Variants:
                        [
                            V("DEMO-FSH-003-BG-S", 8, "beige", "s"), V("DEMO-FSH-003-BG-M", 14, "beige", "m"),
                            V("DEMO-FSH-003-BG-L", 12, "beige", "l"), V("DEMO-FSH-003-BG-XL", 6, "beige", "xl"),
                            V("DEMO-FSH-003-GR-S", 5, "gray", "s"), V("DEMO-FSH-003-GR-M", 9, "gray", "m"),
                            V("DEMO-FSH-003-GR-L", 6, "gray", "l"), V("DEMO-FSH-003-GR-XL", 0, "gray", "xl"),
                        ]),
                ]),
            ]),
        ]),
        new("books-stationery", T("کتاب و لوازم تحریر", "Books & Stationery", "Գրքեր և գրենական պիտույքներ", "Bücher & Schreibwaren", "الكتب والقرطاسية"), "/demo/categories/books-stationery.svg",
        [
            new("books", T("کتاب", "Books", "Գրքեր", "Bücher", "الكتب"),
            [
                new(T("کتاب", "Books", "Գրքեր", "Bücher", "الكتب"),
                [
                    new("DEMO-BOK-001", ProductImage("book-small-shop"),
                        T("کتاب «فروشگاه کوچک، ایده‌های بزرگ»", "Small Shop, Big Ideas (book)", "«Փոքր խանութ, մեծ գաղափարներ» գիրք", "Buch „Kleiner Laden, große Ideen“", "كتاب «متجر صغير، أفكار كبيرة»"),
                        T("راهنمای ساده و کاربردی برای راه‌اندازی و رشد یک کسب‌وکار کوچک آنلاین؛ از اولین محصول تا مشتری وفادار.",
                          "A simple, practical guide to starting and growing a small online business — from the first product to loyal customers.",
                          "Պարզ և գործնական ուղեցույց փոքր առցանց բիզնես սկսելու և զարգացնելու համար՝ առաջին ապրանքից մինչև հավատարիմ հաճախորդներ։",
                          "Ein einfacher, praktischer Leitfaden für Gründung und Wachstum eines kleinen Onlinegeschäfts – vom ersten Produkt bis zur treuen Kundschaft.",
                          "دليل بسيط وعملي لبدء مشروع صغير على الإنترنت وتنميته، من المنتج الأول حتى العملاء الأوفياء."),
                        15m, null, 40,
                        [N("pages", 240), O("cover", "paperback")]),
                    new("DEMO-BOK-002", ProductImage("cookbook"),
                        T("کتاب آشپزی «آشپزخانهٔ ۳۰ دقیقه‌ای»", "Kitchen in 30 Minutes (cookbook)", "«Խոհանոց 30 րոպեում» խոհարարական գիրք", "Kochbuch „Küche in 30 Minuten“", "كتاب الطبخ «مطبخ في 30 دقيقة»"),
                        T("۸۰ دستور غذای سریع و سالم برای روزهای شلوغ، با عکس رنگی برای هر غذا.",
                          "80 quick, healthy recipes for busy days, with a colour photo for every dish.",
                          "80 արագ և առողջ բաղադրատոմս զբաղված օրերի համար՝ յուրաքանչյուր ուտեստի գունավոր լուսանկարով։",
                          "80 schnelle, gesunde Rezepte für volle Tage, mit Farbfoto zu jedem Gericht.",
                          "80 وصفة سريعة وصحية للأيام المزدحمة، مع صورة ملونة لكل طبق."),
                        22m, 26m, 25,
                        [N("pages", 184), O("cover", "hardcover")]),
                ]),
            ]),
            new("stationery", T("لوازم تحریر", "Stationery", "Գրենական պիտույքներ", "Schreibwaren", "القرطاسية"),
            [
                new(T("دفتر و نوشت‌افزار", "Notebooks & pens", "Տետրեր և գրիչներ", "Hefte & Stifte", "الدفاتر والأقلام"),
                [
                    new("DEMO-STN-001", ProductImage("notebook"),
                        T("دفتر سیمی پاپیرا A5 صد برگ", "Papira Spiral Notebook, A5, 100 Sheets", "Papira զսպանակավոր տետր, A5, 100 թերթ", "Papira Spiralheft, A5, 100 Blatt", "دفتر سلكي Papira مقاس A5 بـ100 ورقة"),
                        T("دفتر سیمی خط‌دار با جلد سخت و کاغذ ۸۰ گرمی که جوهر پشت آن نمی‌زند.",
                          "A ruled spiral notebook with a hard cover and 80 gsm paper that ink does not bleed through.",
                          "Գծավոր զսպանակավոր տետր՝ կոշտ կազմով և 80 գ/մ² թղթով, որից թանաքը չի անցնում։",
                          "Liniertes Spiralheft mit festem Einband und 80-g-Papier, durch das Tinte nicht durchdrückt.",
                          "دفتر سلكي مسطر بغلاف صلب وورق 80 غرامًا لا يتسرب منه الحبر."),
                        6m, null, 120,
                        [O("color", "blue"), N("sheets", 100), S("paper_size", "A5")], Brand: "Papira"),
                    new("DEMO-STN-002", ProductImage("gel-pens"),
                        T("ست خودکار ژله‌ای پاپیرا ۱۲ رنگ", "Papira Gel Pen Set, 12 Colours", "Papira գելային գրիչների հավաքածու, 12 գույն", "Papira Gelstift-Set, 12 Farben", "طقم أقلام جل Papira بـ12 لونًا"),
                        T("۱۲ خودکار ژله‌ای با رنگ‌های روشن و نوک ۰٫۵ میلی‌متری برای یادداشت و طراحی.",
                          "12 gel pens in bright colours with a 0.5 mm tip for notes and drawing.",
                          "12 գելային գրիչ վառ գույներով և 0,5 մմ ծայրով՝ նշումների և նկարչության համար։",
                          "12 Gelstifte in leuchtenden Farben mit 0,5-mm-Spitze für Notizen und Zeichnungen.",
                          "12 قلم جل بألوان زاهية ورأس 0.5 مم للملاحظات والرسم."),
                        9m, 12m, 80,
                        [O("color", "multicolor"), N("pieces", 12)], Brand: "Papira"),
                ]),
            ]),
        ]),
    ];

    public static IEnumerable<DemoProduct> AllProducts =>
        Categories.SelectMany(c => c.SubCategories).SelectMany(s => s.Groups).SelectMany(g => g.Products);
}
