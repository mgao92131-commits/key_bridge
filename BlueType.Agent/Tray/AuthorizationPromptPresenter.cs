using BlueType.Agent.Core;
using BlueType.Agent.Infrastructure.Logging;
using BlueType.Agent.Models;
using BlueType.Agent.Native;

namespace BlueType.Agent.Tray;

internal sealed class AuthorizationPromptPresenter
{
    private readonly SynchronizationContext _syncContext;
    private readonly object _lock = new();
    private AuthPromptForm? _activeForm;

    public AuthorizationPromptPresenter(SynchronizationContext syncContext)
    {
        _syncContext = syncContext;
    }

    public bool HasActivePrompt => _activeForm != null;

    public void BringToFrontIfActive()
    {
        _syncContext.Post(_ =>
        {
            lock (_lock)
            {
                if (_activeForm != null && !_activeForm.IsDisposed && _activeForm.IsHandleCreated)
                {
                    _activeForm.Activate();
                    _activeForm.BringToFront();
                }
            }
        }, null);
    }

    /// <summary>
    /// Closes any active authorization dialog on the UI thread.
    /// Must be called from the WinForms UI thread during shutdown so Close runs
    /// immediately instead of being queued behind a blocked ExitThreadCore.
    /// </summary>
    public void CloseActivePrompt()
    {
        AuthPromptForm? form;
        lock (_lock)
        {
            form = _activeForm;
        }

        if (form is null || form.IsDisposed)
        {
            return;
        }

        try
        {
            if (form.IsHandleCreated && form.InvokeRequired)
            {
                form.Invoke(new MethodInvoker(() =>
                {
                    if (!form.IsDisposed)
                    {
                        form.Close();
                    }
                }));
            }
            else if (!form.IsDisposed)
            {
                form.Close();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to close authorization prompt.", ex);
        }
    }

    public Task<AuthPromptDecision> ShowAsync(AuthPromptRequest request, CancellationToken cancellationToken)
    {
        if (!Environment.UserInteractive || !Win32.CanAccessInputDesktop())
        {
            AppLogger.Info("Authorization prompt unavailable because input desktop is not accessible.");
            return Task.FromResult(AuthPromptDecision.Unavailable);
        }

        var completion = new TaskCompletionSource<AuthPromptDecision>(TaskCreationOptions.RunContinuationsAsynchronously);

        _syncContext.Post(
            _ =>
            {
                AuthPromptForm? form = null;
                CancellationTokenRegistration registration = default;

                try
                {
                    form = new AuthPromptForm(request);
                    lock (_lock)
                    {
                        _activeForm = form;
                    }

                    registration = cancellationToken.Register(
                        () =>
                        {
                            try
                            {
                                if (!form.IsDisposed && form.IsHandleCreated)
                                {
                                    // Prefer Invoke so a canceled session can close a modal dialog
                                    // even when the UI thread is pumping ShowDialog.
                                    if (form.InvokeRequired)
                                    {
                                        form.BeginInvoke(new MethodInvoker(form.Close));
                                    }
                                    else
                                    {
                                        form.Close();
                                    }
                                }
                            }
                            catch
                            {
                                // Best effort only.
                            }
                        });

                    form.ShowDialog();
                    if (cancellationToken.IsCancellationRequested)
                    {
                        completion.TrySetCanceled(cancellationToken);
                    }
                    else
                    {
                        completion.TrySetResult(form.Decision);
                    }
                }
                catch
                {
                    completion.TrySetResult(AuthPromptDecision.Unavailable);
                }
                finally
                {
                    registration.Dispose();
                    lock (_lock)
                    {
                        if (_activeForm == form)
                        {
                            _activeForm = null;
                        }
                    }

                    form?.Dispose();
                }
            },
            null);

        return completion.Task;
    }
}
