using System;
using System.Reflection;

namespace BepInExLogAlerts;

internal sealed class UiText
{
    internal bool Japanese { get; private set; }
    private readonly UiLanguage language;
    private readonly Func<bool>? exists;
    private readonly PropertyInfo? instance, currentLanguage;
    internal UiText(Assembly game, UiLanguage language)
    {
        this.language = language;
        if (language != UiLanguage.Auto) { Japanese = language == UiLanguage.Japanese; return; }
        Type type = game.GetType("SeaPower.LanguageResourceHandler", true)!;
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        MethodInfo method = type.GetMethod("InstanceExists", flags, null, new[] { typeof(bool) }, null)
            ?? throw new MissingMethodException(type.FullName, "InstanceExists(bool)");
        exists = () => (bool)method.Invoke(null, new object[] { false })!;
        instance = type.GetProperty("Instance", flags) ?? throw new MissingMemberException(type.FullName, "Instance");
        currentLanguage = type.GetProperty("Language") ?? throw new MissingMemberException(type.FullName, "Language");
    }
    internal void Refresh()
    {
        if (language == UiLanguage.Auto && exists!())
            Japanese = string.Equals(currentLanguage!.GetValue(instance!.GetValue(null)) as string, "ja", StringComparison.OrdinalIgnoreCase);
    }
    internal string T(string ja, string en) => Japanese ? ja : en;
}
