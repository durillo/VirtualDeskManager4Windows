using System.Diagnostics;
using System.Text.Json;
using System.Windows.Automation;
using VirtualDeskManager.Windows.Native;

// Capturas de cada pestaña de la ventana de ajustes, para docs/ajustes.
//
//   dotnet run --project tools\Capturas -- <carpeta de salida>
//
// Desde la 0.28.0-beta.8 los ajustes están dentro de la ventana principal («Virtual Desk Manager · Ajustes»).
//
// Usa el Vdesk que está en marcha, con sus datos de verdad: le pide que abra sus ajustes (la
// señal Local\VirtualDeskManager.ShowSettings, desde la 0.23.0-beta.5), pasa de sección (o de
// pestaña, en las versiones de antes) con UI Automation y fotografía cada una con PrintWindow. No mueve el ratón ni teclea: lo único que se
// ve es la ventana de ajustes abriéndose y cambiando de pestaña. Las pestañas largas salen en
// varias fotos, una por pantalla. Al terminar deja la ventana en la primera pestaña y la cierra
// (se queda en la bandeja).

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("uso: Capturas <carpeta de salida> [texto que buscar]");
    return 2;
}

string salida = Path.GetFullPath(args[0]);
Directory.CreateDirectory(salida);

// ------------------------------------------------------------------ abrir los ajustes

try
{
    using EventWaitHandle senal = EventWaitHandle.OpenExisting(@"Local\VirtualDeskManager.ShowSettings");
    senal.Set();
}
catch (WaitHandleCannotBeOpenedException)
{
    Console.Error.WriteLine("Virtual Desk Manager no está en marcha, o es anterior a la 0.23.0-beta.5 y no sabe abrir sus ajustes así.");
    return 1;
}

nint ventana = 0;

for (int i = 0; i < 50 && ventana == 0; i++)
{
    Thread.Sleep(200);
    ventana = Process.GetProcessesByName("VirtualDeskManager")
        .Where(p => p.MainWindowHandle != 0 && (p.MainWindowTitle == "VirtualDeskManager" || p.MainWindowTitle.StartsWith("Vdesk", StringComparison.Ordinal) || p.MainWindowTitle.StartsWith("Virtual Desk Manager", StringComparison.Ordinal)))
        .Select(p => p.MainWindowHandle)
        .FirstOrDefault();
}

if (ventana == 0)
{
    Console.Error.WriteLine("No apareció la ventana de ajustes.");
    return 1;
}

// Un tamaño fijo, para que todas las capturas casen. Sin activarla ni cambiar el orden.
const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
User32.SetWindowPos(ventana, 0, 80, 40, 1240, 980, SWP_NOZORDER | SWP_NOACTIVATE);
Thread.Sleep(600);

// ------------------------------------------------------------------ recorrer las pestañas

AutomationElement raiz = AutomationElement.FromHandle(ventana);

// Desde la 0.23.0-beta.6 las secciones son una lista en la barra lateral («Nav»); antes eran
// pestañas. Vale para las dos.
AutomationElement? pestanas = raiz.FindFirst(TreeScope.Descendants,
    new PropertyCondition(AutomationElement.AutomationIdProperty, "Nav"))
    ?? raiz.FindFirst(TreeScope.Descendants,
    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Tab));

if (pestanas is null)
{
    Console.Error.WriteLine("No se encontró ni la barra lateral ni las pestañas.");
    return 1;
}

AutomationElementCollection elementos = pestanas.FindAll(TreeScope.Children,
    new OrCondition(
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem)));

List<object> indice = new();
int numero = 0;

foreach (AutomationElement pestana in elementos)
{
    numero++;
    string nombre = pestana.Current.Name;
    string base_ = $"{numero:00}-{Slug(nombre)}";

    ((SelectionItemPattern)pestana.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
    Thread.Sleep(900);

    List<string> fotos = new();
    ScrollPattern? desplazamiento = MainScroll(raiz);

    // Sólo si de verdad se desplaza: pedírselo a una que no, da error.
    if (desplazamiento is { Current.VerticallyScrollable: true })
    {
        desplazamiento.SetScrollPercent(ScrollPattern.NoScroll, 0);
        Thread.Sleep(300);
    }

    for (int pagina = 1; pagina <= 12; pagina++)
    {
        string fichero = pagina == 1 && !(desplazamiento?.Current.VerticallyScrollable ?? false)
            ? base_ + ".png"
            : $"{base_}-{pagina}.png";

        byte[]? png = WindowCapture.CapturePng(ventana);

        if (png is null)
        {
            Console.Error.WriteLine($"  {nombre}: no se pudo fotografiar");
            break;
        }

        File.WriteAllBytes(Path.Combine(salida, fichero), png);
        fotos.Add(fichero);

        if (desplazamiento is null || !desplazamiento.Current.VerticallyScrollable
            || desplazamiento.Current.VerticalScrollPercent >= 99.5)
        {
            break;
        }

        double antes = desplazamiento.Current.VerticalScrollPercent;

        try
        {
            desplazamiento.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeIncrement);
        }
        catch (InvalidOperationException)
        {
            break;
        }

        Thread.Sleep(350);

        if (Math.Abs(desplazamiento.Current.VerticalScrollPercent - antes) < 0.1)
        {
            break;
        }
    }

    Console.WriteLine($"  {numero:00} {nombre}: {fotos.Count} captura(s)");
    indice.Add(new { numero, nombre, fotos });
}

// ------------------------------------------------------------------ dejarla como estaba

if (elementos.Count > 0)
{
    ((SelectionItemPattern)elementos[0].GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
    Thread.Sleep(500);
}

// ------------------------------------------------------------------ el buscador (opcional)

// Con un segundo argumento, lo escribe en «Buscar un ajuste» (por UI Automation, sin teclado),
// fotografía los resultados, pulsa el primero y fotografía adónde lleva.
if (args.Length > 1 && raiz.FindFirst(TreeScope.Descendants,
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)) is { } caja
    && caja.TryGetCurrentPattern(ValuePattern.Pattern, out object? valor))
{
    ((ValuePattern)valor).SetValue(args[1]);
    Thread.Sleep(700);
    Guardar("buscar.png");

    // Por su identificador: el primer botón a secas es el de minimizar de la ventana.
    AutomationElement? primero = raiz.FindFirst(TreeScope.Descendants,
        new PropertyCondition(AutomationElement.AutomationIdProperty, "SearchHit"));

    if (primero?.TryGetCurrentPattern(InvokePattern.Pattern, out object? pulsar) == true)
    {
        Console.WriteLine($"  buscar «{args[1]}»: primero «{primero.Current.Name}»");
        ((InvokePattern)pulsar).Invoke();
        Thread.Sleep(450);
        Guardar("buscar-ir.png");
    }

    ((ValuePattern)valor).SetValue(string.Empty);
    ((SelectionItemPattern)elementos[0].GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
}

void Guardar(string fichero)
{
    if (WindowCapture.CapturePng(ventana) is { } png)
    {
        File.WriteAllBytes(Path.Combine(salida, fichero), png);
    }
}

if (raiz.TryGetCurrentPattern(WindowPattern.Pattern, out object? patron))
{
    ((WindowPattern)patron).Close();
}

File.WriteAllText(Path.Combine(salida, "indice.json"),
    JsonSerializer.Serialize(indice, new JsonSerializerOptions { WriteIndented = true }));

return 0;

// La zona desplazable de la página elegida: la más grande a la vista que no sea una lista (la
// del Diario y la barra lateral se desplazan por sí mismas y no hace falta paginarlas).
static ScrollPattern? MainScroll(AutomationElement raiz)
{
    AutomationElementCollection candidatos = raiz.FindAll(TreeScope.Descendants,
        new PropertyCondition(AutomationElement.IsScrollPatternAvailableProperty, true));

    AutomationElement? mejor = null;
    double area = 0;

    foreach (AutomationElement c in candidatos)
    {
        if (c.Current.IsOffscreen || c.Current.ControlType == ControlType.List || c.Current.ControlType == ControlType.DataGrid)
        {
            continue;
        }

        System.Windows.Rect r = c.Current.BoundingRectangle;
        double a = r.Width * r.Height;

        if (a > area)
        {
            area = a;
            mejor = c;
        }
    }

    return mejor?.GetCurrentPattern(ScrollPattern.Pattern) as ScrollPattern;
}

static string Slug(string texto)
{
    string normal = texto.Normalize(System.Text.NormalizationForm.FormD);
    var letras = normal.Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark);
    string limpio = new string(letras.ToArray()).ToLowerInvariant();
    return System.Text.RegularExpressions.Regex.Replace(limpio, "[^a-z0-9]+", "-").Trim('-');
}
