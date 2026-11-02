using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using System.Runtime.InteropServices;
using BepInExLogAlerts;
using BepInExLogAlerts.Core;
using HarmonyLib;

internal static class NoesisInputChecks
{
    private static int forwarded, unexpectedUnityCalls;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int UnityRead();
    private static readonly UnityRead unexpectedUnityRead = () => { unexpectedUnityCalls++; return 0; };
    [DllImport("mono-2.0-bdwgc.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void mono_add_internal_call(string name, IntPtr implementation);
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Forwarded() => forwarded++;
    private static IEnumerable<CodeInstruction> ProbeBody()
    {
        // Replace only the native-facing body; the product's real Harmony prefix still runs.
        yield return new CodeInstruction(OpCodes.Call, typeof(NoesisInputChecks).GetMethod(nameof(Forwarded), BindingFlags.Static | BindingFlags.NonPublic));
        yield return new CodeInstruction(OpCodes.Ret);
    }
    private static void Events(NoesisView view) => NoesisInputBlocker.EventTarget.Invoke(view, new object?[] { null, false, false });
    private static void Updates(NoesisView view) => NoesisInputBlocker.UpdateTarget.Invoke(view, new object[] { 0f });

    internal static void Run()
    {
        // Harmony JITs the original methods before replacing their bodies. Resolve the
        // engine-only reads referenced by those methods, and fail if any is actually used.
        IntPtr unusedRead = Marshal.GetFunctionPointerForDelegate(unexpectedUnityRead);
        foreach (string name in new[] { "UnityEngine.Screen::get_height", "UnityEngine.Input::get_simulateMouseWithTouches", "UnityEngine.Application::get_isFocused", "UnityEngine.Time::get_frameCount" })
            mono_add_internal_call(name, unusedRead);
        var host = (AlertHost)FormatterServices.GetUninitializedObject(typeof(AlertHost));
        var modal = new ModalInputGate();
        typeof(AlertHost).GetField("inputGate", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, modal);
        var query = typeof(AlertHost).GetMethod("BlockNoesisInput", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Check(!(bool)query.Invoke(host, null)!, "A closed history blocked Noesis.");
        modal.Observe(1, true, false);
        for (int i = 0; i < 3; i++) Check((bool)query.Invoke(host, null)!, "Noesis queries changed the open history state.");
        Check(unexpectedUnityCalls == 0, "Noesis polled Unity input/clock outside the script input phase.");
        Console.WriteLine("PASS Actual host Noesis queries read modal state without consuming the script input phase on game Mono");
        var probes = new Harmony("mebiustos.bepinexlogalerts.tests.noesis");
        try
        {
            var body = new HarmonyMethod(typeof(NoesisInputChecks), nameof(ProbeBody));
            probes.Patch(NoesisInputBlocker.EventTarget, transpiler: body);
            probes.Patch(NoesisInputBlocker.UpdateTarget, transpiler: body);
            var first = (NoesisView)FormatterServices.GetUninitializedObject(typeof(NoesisView));
            var second = (NoesisView)FormatterServices.GetUninitializedObject(typeof(NoesisView));
            var gate = new ModalInputGate();
            int frame = 1;
            bool open = false, held = false;
            gate.Observe(frame, open, held);
            using (var blocker = new NoesisInputBlocker(() => gate.Blocked,
                error => throw new Exception("Unexpected input-blocker failure.", error)))
            {
                Events(first); Updates(first);
                Check(forwarded == 2, "Passive mode blocked Noesis.");
                Console.WriteLine("PASS Actual Noesis common input methods forward normally with history closed on game Mono");
                frame++; open = true; gate.Observe(frame, open, held);
                Events(first); Updates(first); Events(second); Updates(second);
                Check(forwarded == 2, "The opening frame or a second Noesis view bypassed the common guard.");
                frame++; open = false; held = true; gate.Observe(frame, open, held); Events(first); Updates(first);
                frame++; held = false; gate.Observe(frame, open, held); Events(first); Updates(first); Events(second);
                Check(forwarded == 2, "A closing key or its release reached a native-facing body.");
                frame++; held = true; gate.Observe(frame, open, held); Events(first); Updates(second);
                Check(forwarded == 4, "Underlying UI did not resume after release.");
                frame++; open = true; gate.Observe(frame, open, held); Events(second); Updates(first);
                Check(forwarded == 4, "Reopening did not restore common input blocking.");
                Console.WriteLine("PASS Actual Harmony prefixes suppress both Noesis paths across views, opening, closing/release and reopening on game Mono");
                blocker.Dispose();
                Events(first); Updates(second);
                Check(forwarded == 6, "Disposal left a lock or removed another owner's Harmony patch.");
            }
            Exception? reported = null;
            var expected = new InvalidOperationException("Expected input-read failure");
            using (var blocker = new NoesisInputBlocker(() => throw expected, error => reported = error))
            {
                Events(first);
                Check(ReferenceEquals(reported, expected) && forwarded == 6, "A guard failure was hidden or forwarded its failing event.");
                Events(second); Updates(first);
                Check(forwarded == 8, "A guard failure left input blocked.");
            }
            Check(unexpectedUnityCalls == 0, "A hook test executed Unity engine code outside the game.");
            Console.WriteLine("PASS Noesis hook disposal and reported guard failures restore forwarding without removing another owner's patch on game Mono");
        }
        finally { probes.UnpatchSelf(); }
    }
}
