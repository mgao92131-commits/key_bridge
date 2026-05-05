using BlueType.Agent.Core;
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
                                    form.BeginInvoke(new MethodInvoker(form.Close));
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
                        if (_activeForm == form) _activeForm = null;
                    }
                    form?.Dispose();
                }
            },
            null);

        return completion.Task;
    }
}
