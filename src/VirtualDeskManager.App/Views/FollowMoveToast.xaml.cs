using System;
using System.Windows;
using System.Windows.Threading;
using VirtualDeskManager.Core.Localization;
using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.App.Views;

/// <summary>
/// Asks whether to go to the desktop where a rule has just opened an application.
/// </summary>
/// <remarks>
/// Deliberately not a message box: it appears while the user is doing something else, so it must
/// not take the focus, must not block anything, and must be ignorable. If nobody answers it
/// closes on its own and nothing happens, which is the safe outcome: staying where you are can
/// always be undone by hand, whereas being yanked to another desktop mid-task cannot.
/// </remarks>
public partial class FollowMoveToast : Window
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(12);

    private readonly DispatcherTimer _timer;
    private readonly DateTime _shownAt = DateTime.UtcNow;
    private readonly Action _go;

    /// <summary>Builds the question; it places itself in the corner and starts its countdown once loaded.</summary>
    /// <param name="window">The window the rule moved, named in the question.</param>
    /// <param name="desktop">Where it went, by name or by number.</param>
    /// <param name="go">What to do if the user says yes: switch to that desktop.</param>
    public FollowMoveToast(WindowInfo window, VirtualDesktopInfo desktop, Action go)
    {
        InitializeComponent();

        _go = go;

        string application = FriendlyName(window);
        string target = desktop.HasName
            ? desktop.Name
            : Loc.Format("Overlay.DesktopNumber", desktop.Number);

        Question.Text = Loc.Format("Follow.Question", application, target);
        FlowDirection = Loc.Instance.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        GoButton.Click += (_, _) =>
        {
            _go();
            Close();
        };

        StayButton.Click += (_, _) => Close();

        _timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };

        _timer.Tick += OnTick;

        Loaded += (_, _) =>
        {
            PlaceInCorner();
            _timer.Start();
        };

        Closed += (_, _) => _timer.Stop();
    }

    /// <summary>The identifier of the desktop being offered, so two questions do not pile up.</summary>
    public Guid DesktopId { get; init; }

    private void OnTick(object? sender, EventArgs e)
    {
        double elapsed = (DateTime.UtcNow - _shownAt) / Lifetime;

        if (elapsed >= 1)
        {
            // Nobody answered: stay where we are.
            Close();
            return;
        }

        Remaining.Value = (1 - elapsed) * 100;
    }

    /// <summary>
    /// Bottom right of the working area, which leaves the taskbar alone whichever edge it is on.
    /// </summary>
    private void PlaceInCorner()
    {
        Rect area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 16;
        Top = area.Bottom - ActualHeight - 16;
    }

    private static string FriendlyName(WindowInfo window)
    {
        if (string.IsNullOrWhiteSpace(window.ProcessName))
        {
            return window.Title.Length > 0 ? window.Title : "?";
        }

        return window.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? window.ProcessName[..^4]
            : window.ProcessName;
    }
}
