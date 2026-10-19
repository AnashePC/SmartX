using Microsoft.AspNetCore.Components;
using SmartX.Client.Services;

namespace SmartX.Client.Shared;

/// <summary>
/// Base class for every component that renders GatewayState. It subscribes on creation and unsubscribes on
/// disposal, so a page that is navigated away from stops re-rendering while the state itself lives on.
/// </summary>
public abstract class StateAwareComponent : ComponentBase, IDisposable
{
    [Inject] protected GatewayState State { get; set; } = default!;

    protected override void OnInitialized()
    {
        State.Changed += HandleStateChanged;
    }

    private void HandleStateChanged() => _ = InvokeAsync(StateHasChanged);

    public virtual void Dispose()
    {
        State.Changed -= HandleStateChanged;
    }
}
