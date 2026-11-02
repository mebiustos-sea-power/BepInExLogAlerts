using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace BepInExLogAlerts;

internal sealed class NoesisInputBlocker : IDisposable
{
    private static NoesisInputBlocker? current;
    private readonly Harmony patches = new Harmony(ModInfo.Id + ".NoesisInput");
    private readonly Func<bool> blocked;
    private readonly Action<Exception> failure;
    private ConditionalWeakTable<NoesisView, CancelledInput> cancelled = new ConditionalWeakTable<NoesisView, CancelledInput>();
    private bool wasBlocked, disposed;

    internal static MethodInfo EventTarget => Target("ProcessEvent", typeof(Event), typeof(bool), typeof(bool));
    internal static MethodInfo UpdateTarget => Target("UpdateInputs", typeof(float));

    private static MethodInfo Target(string name, params Type[] parameters)
    {
        var method = typeof(NoesisView).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance,
            null, parameters, null) ?? throw new MissingMethodException(typeof(NoesisView).FullName, name);
        if (method.ReturnType != typeof(void)) throw new InvalidOperationException("Unsupported Noesis input method: " + name);
        return method;
    }

    internal NoesisInputBlocker(Func<bool> blocked, Action<Exception> failure)
    {
        if (current != null) throw new InvalidOperationException("The Noesis input blocker is already installed.");
        this.blocked = blocked;
        this.failure = failure;
        var events = EventTarget;
        var updates = UpdateTarget;
        current = this;
        try
        {
            var prefix = new HarmonyMethod(typeof(NoesisInputBlocker), nameof(AllowInput)) { priority = Priority.First };
            patches.Patch(events, prefix: prefix);
            patches.Patch(updates, prefix: prefix);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private static bool AllowInput(NoesisView __instance)
    {
        var owner = current;
        if (owner == null) return true;
        try
        {
            if (!owner.blocked())
            {
                if (owner.wasBlocked)
                    owner.cancelled = new ConditionalWeakTable<NoesisView, CancelledInput>();
                owner.wasBlocked = false;
                return true;
            }
            owner.wasBlocked = true;
            owner.CancelPreviousInput(__instance);
            return false;
        }
        catch (Exception error)
        {
            // Close the overlay and report the failure instead of leaving an input lock behind.
            try { owner.Dispose(); }
            catch (Exception cleanup) { error = new AggregateException(error, cleanup); }
            owner.failure(error);
            return false;
        }
    }

    private void CancelPreviousInput(NoesisView component)
    {
        var content = component.Content;
        var view = content?.View;
        if (view == null) return;
        var state = cancelled.GetValue(component, _ => new CancelledInput());
        if (ReferenceEquals(state.View, view)) return;
        state.View = view;
        // Releasing capture/focus first cancels a pending button press before Deactivate resets keys.
        content!.Mouse.Capture(null);
        content.Keyboard.ClearFocus();
        view.Deactivate();
        if (Application.isFocused) view.Activate();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (ReferenceEquals(current, this)) current = null;
        patches.UnpatchSelf();
    }

    private sealed class CancelledInput { internal Noesis.View? View; }
}
