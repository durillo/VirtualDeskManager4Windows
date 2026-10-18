using System;
using System.Windows;
using System.Windows.Input;
using VirtualDeskManager.App.ViewModels;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Windows.Hotkeys;
using VirtualDeskManager.Windows.Native;

namespace VirtualDeskManager.App.Views;

public partial class HotkeyEditorWindow : Window
{
    private readonly HotkeyEditorViewModel _viewModel;

    /// <summary>
    /// Lee las teclas antes de que el shell se las quede.
    /// </summary>
    /// <remarks>
    /// Sin esto la tecla Windows no se puede asignar. Windows se reserva casi todas las
    /// combinaciones con ella y las consume antes de que lleguen a la ventana, de modo que
    /// <c>PreviewKeyDown</c> no llega a dispararse siquiera. El manejador normal se conserva como
    /// respaldo por si el hook no se puede instalar.
    /// </remarks>
    private readonly KeyCaptureHook _capture;

    private readonly IAppLogger _logger;

    /// <summary>A cuál de las dos casillas van las teclas que llegan ahora mismo.</summary>
    private CaptureSlot _slot = CaptureSlot.Primary;

    /// <summary>
    /// El editor de un atajo. La captura con gancho sólo está puesta mientras la casilla de las
    /// teclas tiene el foco.
    /// </summary>
    public HotkeyEditorWindow(HotkeyEditorViewModel viewModel, IAppLogger? logger = null)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        _logger = logger ?? NullAppLogger.Instance;
        _capture = new KeyCaptureHook(_logger);
        _capture.KeyCaptured += OnKeyCaptured;

        CaptureBox.PreviewKeyDown += OnCapturePreviewKeyDown;
        CaptureBox.MouseLeftButtonDown += (_, _) => CaptureBox.Focus();
        CaptureBox.GotKeyboardFocus += (_, _) =>
        {
            _slot = CaptureSlot.Primary;
            bool started = _capture.Start();

            // Si esto dice False, la tecla Windows no se va a poder asignar y el editor se queda
            // con el manejador normal de WPF. Es lo primero que hay que mirar si alguien vuelve a
            // decir que no le deja.
            _logger.Info(
                LogEvents.DesktopOperation,
                ("Operation", "HotkeyCaptureStarted"),
                ("HookInstalled", started));
        };

        CaptureBox.LostKeyboardFocus += (_, _) => _capture.Stop();

        // La segunda casilla usa exactamente el mismo camino: el mismo hook, el mismo manejador
        // de respaldo, y sólo cambia dónde se guarda lo que se pulse.
        AlternateCaptureBox.PreviewKeyDown += OnCapturePreviewKeyDown;
        AlternateCaptureBox.MouseLeftButtonDown += (_, _) => AlternateCaptureBox.Focus();
        AlternateCaptureBox.GotKeyboardFocus += (_, _) =>
        {
            _slot = CaptureSlot.Alternate;
            _capture.Start();
        };

        AlternateCaptureBox.LostKeyboardFocus += (_, _) => _capture.Stop();
        ClearAlternateButton.Click += (_, _) => _viewModel.ClearAlternate();

        SaveButton.Click += OnSave;
        OverwriteButton.Click += (_, _) => _viewModel.ConfirmOverwrite();
        CancelButton.Click += (_, _) => DialogResult = false;

        Loaded += (_, _) => CaptureBox.Focus();

        // El hook se quita pase lo que pase. Uno que se quede puesto tragandose teclas deja al
        // usuario sin teclado en todo el equipo, asi que esto no es opcional.
        Closed += (_, _) => _capture.Dispose();
    }

    /// <summary>
    /// Llega del hook, que corre en este mismo hilo porque es quien lo instalo.
    /// </summary>
    private void OnKeyCaptured(object? sender, KeyCapturedEventArgs e)
    {
        // Escape no se asigna: es como se sale del cuadro de captura.
        if (e.Key == HotkeyKey.Escape && e.Modifiers == HotkeyModifiers.None)
        {
            _capture.Stop();
            Keyboard.ClearFocus();
            return;
        }

        _logger.Info(
            LogEvents.DesktopOperation,
            ("Operation", "HotkeyCaptured"),
            ("Via", "hook"),
            ("Shortcut", HotkeyDefinition.Format(e.Modifiers, e.Key)));

        _viewModel.Capture(e.Modifiers, e.Key, _slot);
    }

    /// <summary>The shortcut the user accepted, or null when the dialog was cancelled.</summary>
    public HotkeyDefinition? Result { get; private set; }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        HotkeyDefinition? built = _viewModel.TryBuild();

        if (built is null)
        {
            // The view model already explains what is missing.
            return;
        }

        Result = built;
        DialogResult = true;
    }

    /// <summary>
    /// Si una tecla está pulsada ahora mismo, según el estado real del teclado.
    /// </summary>
    /// <remarks>
    /// El bit alto de <c>GetAsyncKeyState</c> es el que dice si la tecla está abajo; el bajo dice
    /// si se ha pulsado desde la última consulta, que aquí no interesa.
    /// </remarks>
    private static bool IsDown(ushort virtualKey) =>
        (User32.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    /// <summary>
    /// Reads the combination straight from the keyboard instead of asking the user to type it.
    /// </summary>
    private void OnCapturePreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Nothing here should reach the rest of the window: Alt and Tab are legitimate choices.
        e.Handled = true;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Pressing only a modifier is not a combination yet.
        if (key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin)
        {
            return;
        }

        int virtualKey = KeyInterop.VirtualKeyFromKey(key);

        if (virtualKey == 0)
        {
            return;
        }

        HotkeyModifiers modifiers = HotkeyModifiers.None;
        ModifierKeys pressed = Keyboard.Modifiers;

        if (pressed.HasFlag(ModifierKeys.Control))
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (pressed.HasFlag(ModifierKeys.Alt))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (pressed.HasFlag(ModifierKeys.Shift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        // La tecla Windows hay que preguntarla aparte, y no es un capricho: WPF **nunca** la
        // informa en Keyboard.Modifiers. Esa propiedad se calcula mirando sólo Ctrl, Alt y Mayús,
        // de modo que ModifierKeys.Windows existe en el enum pero no aparece ahí jamás.
        //
        // La primera versión de esto preguntaba por Keyboard.Modifiers como con las otras tres,
        // y el resultado era una rama de código que no se ejecutaba nunca: la tecla Windows
        // simplemente no se podía asignar. Se pregunta al estado real del teclado.
        if (IsDown(NativeConstants.VK_LWIN) || IsDown(NativeConstants.VK_RWIN))
        {
            modifiers |= HotkeyModifiers.Windows;
        }

        // Cualquier código de tecla vale, esté o no con nombre en HotkeyKey.
        //
        // Antes se descartaba lo que el enum no conociera, y el enum sólo tenía cinco teclas OEM
        // de las trece que hay. En un teclado español eso dejaba fuera la º, la ç, la ´, la ¡ y
        // la < entre otras: al pulsarlas no pasaba nada de nada, sin un mensaje que lo explicara,
        // y el editor parecía estropeado.
        _viewModel.Capture(modifiers, (HotkeyKey)virtualKey, _slot);
    }
}
