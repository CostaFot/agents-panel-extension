using System;
using System.Globalization;
using AgentsPanelExtension.Properties;

namespace AgentsPanelExtension;

// Thin helpers around the strongly-typed Properties.Resources class (generated from
// Properties/Resources.resx by VS's PublicResXFileCodeGenerator — the same setup PowerToys' own CmdPal
// extensions use). Static UI strings are read directly off Resources.<Key>; this class only covers the
// two cases that can't be: a runtime-computed key, and filling a format template.
//
// English values live in the neutral Resources.resx, embedded as "AgentsPanelExtension.Properties.Resources".
// At runtime ResourceManager resolves the right culture from CultureInfo.CurrentUICulture via satellite
// assemblies. Shipping a language is purely additive: drop a Resources.<culture>.resx with the same keys
// translated and the SDK compiles it into a satellite; missing keys fall back to English. No code changes.
//
// FAIL LOUD by design: a missing resource set or key is a real bug (packaging/embed broken, a typo, a
// forgotten resx entry), so surface it immediately rather than papering over it with a degraded UI.
internal static class Strings
{
    // Runtime-computed-key lookup. Static keys should use Resources.<Key> directly (compile-checked);
    // this is only for keys built at runtime. Throws if the key isn't in the resx (or the set can't
    // load) rather than rendering a key name.
    public static string Get(string key) =>
        Resources.ResourceManager.GetString(key, CultureInfo.CurrentUICulture)
            ?? throw new InvalidOperationException($"Missing resource string '{key}'");

    // Fill a localized format template (pass it strongly-typed, e.g. Strings.Format(Resources.X, arg)).
    // The template is localized; the args are already-formatted strings, so culture here is moot — the
    // deliberate invariant number formatting still happens in the Ui layer that produces them. A
    // malformed template throws (FormatException) — a bad placeholder is a bug, not hidden.
    public static string Format(string template, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, template, args);
}
